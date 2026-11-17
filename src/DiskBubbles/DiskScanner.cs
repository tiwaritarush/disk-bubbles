using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace DiskBubbles
{
    public sealed class DiskEntry
    {
        private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
        private bool _ordered;

        public DiskEntry(string name, List<DiskEntry> children, long bytes = 0)
        {
            Name = name;
            Children = children;
            Bytes = bytes;
        }

        public string Name { get; }
        public long Bytes { get; internal set; }
        public bool IsFolder => Children != null;
        public string SizeLabel => FormatSize(Bytes);
        public bool IsSystem => IsFolder && (Protection & EntryProtection.CriticalFolder) != 0;
        public bool CanMark => Protection == EntryProtection.None;
        public bool IsMarked { get; internal set; }
        public string AccessibleName => Name + ", " + SizeLabel + (IsSystem ? ", critical Windows folder" : "");
        internal EntryProtection Protection { get; set; }
        internal List<DiskEntry> Children { get; }

        internal void OrderChildren()
        {
            if (_ordered)
                return;
            if (Children.Count > 1)
                Children.Sort(DiskScanner.CompareEntries);
            _ordered = true;
        }

        internal static string FormatSize(long bytes)
        {
            double value = bytes;
            int unit = 0;
            while (value >= 1000 && unit < SizeUnits.Length - 1)
            {
                value /= 1000;
                unit++;
            }
            return value.ToString(unit == 0 ? "N0" : "0.#") + " " + SizeUnits[unit];
        }
    }

    internal sealed class ScanResult
    {
        public DiskEntry Root;
        public long Files;
        public long Folders;
        public long Skipped;
        public long VolumeBytes;
        public long FreeBytes;
        public TimeSpan Elapsed;
        public bool RootPartial;
        public bool NeedsRefresh;
    }

    internal static class DiskScanner
    {
        private sealed class PendingFolder
        {
            public string Path;
            public DiskEntry Entry;
            public DiskEntry Parent;
            public bool IsVolumeRoot;
        }

        public static ScanResult Scan(string path, CancellationToken cancellation,
            Action<long, long, long, string> progress = null, int workerCount = 0)
        {
            var elapsed = Stopwatch.StartNew();
            var root = new DirectoryInfo(path);
            var result = new ScanResult { Root = new DiskEntry(root.Name, new List<DiskEntry>())
                { Protection = SystemProtection.ForPath(root.FullName) } };
            try
            {
                FileAttributes attributes = root.Attributes;
                result.Root.Protection = SystemProtection.ForPath(root.FullName, attributes) |
                    SystemProtection.ForEntry(result.Root.Protection, attributes);
            }
            catch (Exception error) when (IsFileSystemError(error))
            {
            }
            int workers = workerCount > 0 ? workerCount : Math.Min(8, Environment.ProcessorCount);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            using var available = new SemaphoreSlim(0);
            var pending = new ConcurrentQueue<PendingFolder>();
            var visited = new ConcurrentQueue<PendingFolder>();
            int outstanding = 0;
            long progressAt = Stopwatch.GetTimestamp();
            CancellationToken token = stop.Token;
            Enqueue(new PendingFolder
            {
                Path = root.FullName, Entry = result.Root,
                IsVolumeRoot = root.FullName.Equals(root.Root.FullName, StringComparison.OrdinalIgnoreCase),
            });

            var tasks = new Task[workers];
            for (int i = 0; i < tasks.Length; i++)
                tasks[i] = Task.Factory.StartNew(Traverse, CancellationToken.None,
                    workers == 1 ? TaskCreationOptions.None : TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Task.WhenAll(tasks).GetAwaiter().GetResult();

            PendingFolder[] folders = visited.ToArray();
            for (int index = folders.Length - 1; index >= 0; index--)
            {
                cancellation.ThrowIfCancellationRequested();
                PendingFolder folder = folders[index];
                if (folder.Parent == null || folder.Entry.Children.Count >= 1024)
                    folder.Entry.OrderChildren();
                if (folder.Parent != null)
                {
                    folder.Parent.Bytes = AddBytes(folder.Parent.Bytes, folder.Entry.Bytes);
                    if (folder.Entry.Protection != EntryProtection.None)
                        folder.Parent.Protection |= EntryProtection.ProtectedDescendant;
                }
            }

            cancellation.ThrowIfCancellationRequested();
            if (!result.RootPartial)
                NativeDirectoryReader.ReadCapacity(root.FullName, result);
            cancellation.ThrowIfCancellationRequested();
            result.Elapsed = elapsed.Elapsed;
            return result;

            void Enqueue(PendingFolder folder)
            {
                Interlocked.Increment(ref outstanding);
                pending.Enqueue(folder);
                available.Release();
            }

            void Traverse()
            {
                try
                {
                    using var reader = new NativeDirectoryReader();
                    while (true)
                    {
                        available.Wait(token);
                        if (!pending.TryDequeue(out PendingFolder folder))
                            return;

                        visited.Enqueue(folder);
                        Interlocked.Increment(ref result.Folders);
                        ReadFolder(folder, reader);
                        folder.Path = null;
                        if (Interlocked.Decrement(ref outstanding) == 0)
                            available.Release(workers);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    cancellation.ThrowIfCancellationRequested();
                }
                catch
                {
                    stop.Cancel();
                    throw;
                }
            }

            void ReadFolder(PendingFolder folder, NativeDirectoryReader reader)
            {
                long files = 0;
                long skipped = 0;
                int entryCount = 0;
                try
                {
                    if (!reader.TryRead(folder.Path, folder.Parent == null, token, Accept))
                    {
                        var directory = new DirectoryInfo(folder.Path);
                        if (folder.Parent != null && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("The folder became a reparse point during the scan.");
                        ReadManaged(directory, token, Accept, Skip);
                    }
                }
                catch (Exception error) when (IsFileSystemError(error))
                {
                    Skip();
                    if (folder.Parent == null)
                        result.RootPartial = true;
                }
                PublishCounts();

                void Accept(string name, FileAttributes attributes, long length)
                {
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    bool needsPath = directory || folder.IsVolumeRoot;
                    if ((attributes & FileAttributes.ReparsePoint) != 0 || (needsPath && !NativeDirectoryReader.IsEntryName(name)))
                    {
                        Skip();
                    }
                    else
                    {
                        string path = needsPath ? Path.Combine(folder.Path, name) : null;
                        EntryProtection protection = SystemProtection.ForEntry(folder.Entry.Protection, attributes);
                        if (folder.IsVolumeRoot)
                            protection |= SystemProtection.ForPath(path, attributes);

                        var child = new DiskEntry(name, directory ? new List<DiskEntry>() : null, directory ? 0 : length)
                        {
                            Protection = protection,
                        };
                        folder.Entry.Children.Add(child);
                        if (directory)
                        {
                            Enqueue(new PendingFolder { Path = path, Entry = child, Parent = folder.Entry });
                        }
                        else
                        {
                            folder.Entry.Bytes = AddBytes(folder.Entry.Bytes, length);
                            if (protection != EntryProtection.None)
                                folder.Entry.Protection |= EntryProtection.ProtectedDescendant;
                            files++;
                        }
                    }

                    if ((++entryCount & 127) == 0)
                        PublishCounts();
                }

                void Skip()
                {
                    skipped++;
                    folder.Entry.Protection |= EntryProtection.Incomplete;
                }

                void PublishCounts()
                {
                    if (files != 0)
                    {
                        Interlocked.Add(ref result.Files, files);
                        files = 0;
                    }
                    if (skipped != 0)
                    {
                        Interlocked.Add(ref result.Skipped, skipped);
                        skipped = 0;
                    }
                    if (progress == null)
                        return;

                    long previous = Interlocked.Read(ref progressAt);
                    long now = Stopwatch.GetTimestamp();
                    if (now - previous >= Stopwatch.Frequency / 5 &&
                        Interlocked.CompareExchange(ref progressAt, now, previous) == previous)
                        progress(Interlocked.Read(ref result.Files), Interlocked.Read(ref result.Folders),
                            Interlocked.Read(ref result.Skipped), folder.Path);
                }
            }
        }

        internal static bool IsFileSystemError(Exception error) =>
            error is IOException || error is UnauthorizedAccessException || error is SecurityException;

        private static void ReadManaged(DirectoryInfo directory, CancellationToken cancellation,
            Action<string, FileAttributes, long> accept, Action skipped)
        {
            foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    FileAttributes attributes = item.Attributes;
                    long length = (attributes & FileAttributes.ReparsePoint) == 0 && item is FileInfo file ? file.Length : 0;
                    accept(item.Name, attributes, length);
                }
                catch (Exception error) when (IsFileSystemError(error))
                {
                    skipped();
                }
            }
        }

        internal static long AddBytes(long total, long bytes) =>
            total > long.MaxValue - bytes ? long.MaxValue : total + bytes;

        internal static int CompareEntries(DiskEntry first, DiskEntry second)
        {
            int bySize = second.Bytes.CompareTo(first.Bytes);
            if (bySize != 0)
                return bySize;
            int byName = StringComparer.OrdinalIgnoreCase.Compare(first.Name, second.Name);
            return byName != 0 ? byName : StringComparer.Ordinal.Compare(first.Name, second.Name);
        }

    }
}
