using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DiskBubbles
{
    public sealed class RecycleItem
    {
        internal DiskEntry Entry;
        internal ShellRecycle.EntrySnapshot Snapshot;
        private byte[] _folderIdentity;
        internal long Files;
        internal long Folders;
        internal bool NeedsRescan;
        public string Path { get; internal set; }
        public string Name => Entry.Name;
        public string SizeLabel => Entry.SizeLabel;
        public string AccessibleName => Name + ", " + SizeLabel + ", " + ReviewStatus;
        public string Error { get; internal set; }
        public bool Ready => Error == null;
        public string ReviewStatus => Ready ? "Ready to move" : Error.TrimEnd('.') + (NeedsRescan ? " · Rescan this folder" : " · Stays in place");

        internal static List<RecycleItem> Prepare(string folder, IEnumerable<DiskEntry> selection, CancellationToken cancellation)
        {
            var items = new List<RecycleItem>();
            foreach (DiskEntry entry in selection)
            {
                cancellation.ThrowIfCancellationRequested();
                var item = new RecycleItem { Entry = entry, Path = folder };
                items.Add(item);
                if (!NativeDirectoryReader.IsEntryName(entry.Name))
                {
                    item.Error = "This name cannot be used as a single Windows filename";
                    continue;
                }
                item.Path = System.IO.Path.Combine(folder, entry.Name);
                if (entry.Protection != EntryProtection.None)
                {
                    item.Error = SystemProtection.Reason(entry);
                    continue;
                }

                try
                {
                    item.Snapshot = ShellRecycle.ReadSnapshot(item.Path, entry);
                    if (entry.IsFolder)
                        item._folderIdentity = item.ReadFolderIdentity(cancellation);
                }
                catch (Exception error) when (IsItemError(error))
                {
                    item.Error = error.Message;
                    item.NeedsRescan = item.Snapshot.Path != null && !item.StillMatches();
                }
            }
            return items;
        }

        internal static Task<List<RecycleItem>> MoveAsync(IEnumerable<RecycleItem> selection) => MoveWithCheckAsync(selection, null);

        internal static Task<List<RecycleItem>> MoveWithCheckAsync(IEnumerable<RecycleItem> selection, Action<RecycleItem> check)
        {
            var completion = new TaskCompletionSource<List<RecycleItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() =>
            {
                var moved = new List<RecycleItem>();
                try
                {
                    foreach (RecycleItem item in selection.Where(item => item.Ready))
                    {
                        try
                        {
                            check?.Invoke(item);
                            item.Move();
                            moved.Add(item);
                        }
                        catch (Exception error) when (IsItemError(error))
                        {
                            item.Error = Explain(error);
                            item.NeedsRescan |= error is ShellRecycle.UnconfirmedMoveException || !item.StillMatches();
                        }
                    }
                }
                catch (Exception error)
                {
                    foreach (RecycleItem item in selection.Where(item => item.Ready && !moved.Contains(item)))
                    {
                        item.Error = error.Message;
                        item.NeedsRescan = true;
                    }
                }
                completion.SetResult(moved);
            }) { IsBackground = true, Name = "Recycle Bin transfer" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return completion.Task;
        }

        private void Move()
        {
            if (Entry.IsFolder)
            {
                try
                {
                    if (!_folderIdentity.SequenceEqual(ReadFolderIdentity(CancellationToken.None)))
                        throw new IOException("An item inside this folder changed after review.");
                }
                catch (Exception error) when (IsItemError(error))
                {
                    NeedsRescan = true;
                    throw;
                }
            }
            else
            {
                Files = 1;
            }
            if (!StillMatches())
                throw new IOException("The item changed after review. Rescan to review it again.");

            ShellRecycle.Move(Snapshot, Entry);
        }

        private bool StillMatches()
        {
            try { return Snapshot.Matches(ShellRecycle.ReadSnapshot(Path, Entry)); }
            catch (Exception error) when (IsItemError(error)) { return false; }
        }

        private byte[] ReadFolderIdentity(CancellationToken cancellation)
        {
            ScanResult current = DiskScanner.Scan(Snapshot.Path, cancellation);
            if (current.Root.Protection != EntryProtection.None || current.Skipped != 0)
                throw new IOException("The folder contains protected or unreadable items.");
            if (!SameTree(Entry, current.Root))
                throw new IOException("The folder contents changed. Rescan to review the current items.");

            using var digest = SHA256.Create();
            using var stream = new CryptoStream(Stream.Null, digest, CryptoStreamMode.Write);
            using var writer = new BinaryWriter(stream);
            var pending = new Stack<(DiskEntry Entry, string Path)>();
            pending.Push((current.Root, Snapshot.Path));
            while (pending.Count != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var target = pending.Pop();
                ShellRecycle.ReadSnapshot(target.Path, target.Entry).WriteIdentity(writer);
                if (target.Entry.IsFolder)
                    foreach (DiskEntry child in target.Entry.Children)
                        pending.Push((child, System.IO.Path.Combine(target.Path, child.Name)));
            }
            if (!StillMatches())
                throw new IOException("The folder changed while Windows was verifying it.");

            stream.FlushFinalBlock();
            Files = current.Files;
            Folders = current.Folders;
            return digest.Hash;
        }

        private static bool SameTree(DiskEntry first, DiskEntry second)
        {
            var pending = new Stack<(DiskEntry Before, DiskEntry Now)>();
            pending.Push((first, second));
            while (pending.Count > 0)
            {
                var pair = pending.Pop();
                if (pair.Before.Bytes != pair.Now.Bytes || pair.Before.IsFolder != pair.Now.IsFolder ||
                    !string.Equals(pair.Before.Name, pair.Now.Name, StringComparison.Ordinal))
                    return false;
                if (!pair.Before.IsFolder)
                    continue;

                var before = pair.Before.Children;
                var now = pair.Now.Children;
                if (before.Count != now.Count)
                    return false;
                pair.Before.OrderChildren();
                pair.Now.OrderChildren();
                for (int i = 0; i < before.Count; i++)
                    pending.Push((before[i], now[i]));
            }
            return true;
        }

        private static bool IsItemError(Exception error) => DiskScanner.IsFileSystemError(error) ||
            error is Win32Exception || error is COMException || error is ArgumentException;

        private static string Explain(Exception error)
        {
            switch (unchecked((uint)error.HResult))
            {
                case 0x80270021:
                case 0x80070005:
                    return "Windows denied access to this item or its Recycle Bin.";
                default:
                    return error is COMException ? $"Windows could not recycle the item (0x{error.HResult:X8})." : error.Message;
            }
        }
    }
}
