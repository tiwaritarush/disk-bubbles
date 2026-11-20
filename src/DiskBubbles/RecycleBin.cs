using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace DiskBubbles
{
    internal enum RecycleOutcome
    {
        Ready,
        Blocked,
        Recycled,
        Deleted,
        Kept,
        Failed,
    }

    /// <summary>One file or folder proposed for removal, and what finally happened to it.</summary>
    public sealed class RecycleItem
    {
        internal DiskEntry Entry;
        internal long Files;
        internal long Folders;
        internal bool NeedsRescan;
        private RecycleBin.Identity _identity;

        public string Path { get; internal set; }

        /// <summary>The containing folder, shortened in the middle so the drive and the nearest folders stay readable.</summary>
        public string Location
        {
            get
            {
                string folder = System.IO.Path.GetDirectoryName(Path) ?? Path;
                string[] parts = folder.Split('\\');
                return parts.Length <= 5 ? "in " + folder : "in " + string.Join("\\", parts.Take(2)) + "\\…\\" + string.Join("\\", parts.Skip(parts.Length - 2));
            }
        }
        public string Name => Entry.Name;
        public string SizeLabel => Entry.SizeLabel;
        public string Error { get; internal set; }
        internal RecycleOutcome Outcome { get; private set; }
        public bool Ready => Outcome == RecycleOutcome.Ready;
        public bool Removed => Outcome == RecycleOutcome.Recycled || Outcome == RecycleOutcome.Deleted;
        public string AccessibleName => Name + ", " + SizeLabel + ", " + ReviewStatus;

        public string ReviewStatus
        {
            get
            {
                switch (Outcome)
                {
                    case RecycleOutcome.Ready: return "Ready to move";
                    case RecycleOutcome.Recycled: return "Moved to the Recycle Bin";
                    case RecycleOutcome.Deleted: return "Permanently deleted";
                    default: return Error.TrimEnd('.') + (NeedsRescan ? " · Rescan to update" : "");
                }
            }
        }

        internal void Block(string reason, bool rescan = false) => Finish(RecycleOutcome.Blocked, reason, rescan);
        internal void Fail(string reason, bool rescan = false) => Finish(RecycleOutcome.Failed, reason, rescan);
        internal void Keep(string reason) => Finish(RecycleOutcome.Kept, reason, false);
        internal void Succeed(bool recycled) => Finish(recycled ? RecycleOutcome.Recycled : RecycleOutcome.Deleted, null, false);

        private void Finish(RecycleOutcome outcome, string reason, bool rescan)
        {
            Outcome = outcome;
            Error = reason;
            NeedsRescan |= rescan;
        }

        /// <summary>
        /// Checks the selection against what is on disk right now. This is deliberately cheap: one handle per
        /// selected item, never a re-scan of folder contents, so reviewing a 200,000-file folder is instant.
        /// </summary>
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
                    item.Block("This name can't be used as a single Windows filename");
                    continue;
                }
                item.Path = System.IO.Path.Combine(folder, entry.Name);
                if (entry.Protection != EntryProtection.None)
                {
                    item.Block(SystemProtection.Reason(entry));
                    continue;
                }

                try
                {
                    item._identity = RecycleBin.ReadIdentity(item.Path, entry);
                    (item.Files, item.Folders) = Count(entry);
                }
                catch (Exception error) when (RecycleBin.IsItemError(error))
                {
                    item.Block(RecycleBin.Explain(error), rescan: true);
                }
            }
            return items;
        }

        internal bool StillMatches()
        {
            try { return _identity.Equals(RecycleBin.ReadIdentity(Path, Entry)); }
            catch (Exception error) when (RecycleBin.IsItemError(error)) { return false; }
        }

        private static (long Files, long Folders) Count(DiskEntry root)
        {
            if (!root.IsFolder)
                return (1, 0);
            long files = 0, folders = 0;
            var pending = new Stack<DiskEntry>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                DiskEntry entry = pending.Pop();
                if (!entry.IsFolder)
                {
                    files++;
                    continue;
                }
                folders++;
                foreach (DiskEntry child in entry.Children)
                    pending.Push(child);
            }
            return (files, folders);
        }
    }

    internal readonly struct RecycleProgress
    {
        public RecycleProgress(int done, int total, double fraction, string current)
        {
            Done = done;
            Total = total;
            Fraction = fraction;
            Current = current;
        }

        public int Done { get; }
        public int Total { get; }
        public double Fraction { get; }
        public string Current { get; }
    }

    /// <summary>
    /// Moves reviewed items to the Windows Recycle Bin through IFileOperation, the same engine File Explorer
    /// uses. Every item ends with a definite outcome reported by Windows: recycled, permanently deleted (only
    /// after the user accepts the standard Windows prompt for items too large for the Bin), kept, or failed.
    /// </summary>
    internal static class RecycleBin
    {
        private const uint NoConfirmation = 0x0010;
        private const uint Silent = 0x0004;
        private const uint NoErrorUi = 0x0400;
        private const uint AllowUndo = 0x0040;
        private const uint WantNukeWarning = 0x4000;
        private const uint RecycleOnDelete = 0x00080000;
        private const uint FileSystemPath = 0x80058000;
        private static readonly string[] DeviceNames = { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" };

        internal static Task MoveAsync(IReadOnlyList<RecycleItem> items, IntPtr owner,
            IProgress<RecycleProgress> progress = null, Action<RecycleItem> lastCheck = null)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() =>
            {
                try
                {
                    Move(items.Where(item => item.Ready).ToList(), owner, progress, lastCheck);
                    completion.SetResult(true);
                }
                catch (Exception error)
                {
                    foreach (RecycleItem item in items.Where(item => item.Ready))
                        item.Fail(Explain(error), rescan: true);
                    completion.SetResult(false);
                }
            }) { IsBackground = true, Name = "Recycle Bin transfer" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return completion.Task;
        }

        private static void Move(List<RecycleItem> items, IntPtr owner, IProgress<RecycleProgress> progress,
            Action<RecycleItem> lastCheck)
        {
            var queued = new Dictionary<string, RecycleItem>(StringComparer.OrdinalIgnoreCase);
            foreach (RecycleItem item in items)
            {
                try
                {
                    lastCheck?.Invoke(item);
                    if (!Exists(item.Path))
                        item.Fail("This item no longer exists", rescan: true);
                    else if (!item.StillMatches())
                        item.Fail("This item changed after review", rescan: true);
                    else
                        queued[item.Path] = item;
                }
                catch (Exception error) when (IsItemError(error))
                {
                    item.Fail(Explain(error), rescan: true);
                }
            }
            if (queued.Count == 0)
                return;

            var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
            var sink = new ProgressSink(queued, progress);
            uint cookie = 0;
            int batchResult = 0;
            try
            {
                operation.SetOperationFlags(NoConfirmation | Silent | NoErrorUi | AllowUndo | WantNukeWarning | RecycleOnDelete);
                if (owner != IntPtr.Zero)
                    operation.SetOwnerWindow(owner);
                operation.Advise(sink, out cookie);
                foreach (RecycleItem item in queued.Values.ToList())
                {
                    Guid shellItem = typeof(IShellItem).GUID;
                    int created = SHCreateItemFromParsingName(item.Path, IntPtr.Zero, ref shellItem, out IShellItem target);
                    if (created < 0)
                    {
                        item.Fail(ExplainResult(created), rescan: true);
                        queued.Remove(item.Path);
                        continue;
                    }
                    try { operation.DeleteItem(target, null); }
                    finally { Marshal.ReleaseComObject(target); }
                }
                progress?.Report(new RecycleProgress(0, queued.Count, 0, null));
                if (queued.Count > 0)
                {
                    // A failing item makes the batch report an error too; each item's own result is what counts.
                    try { operation.PerformOperations(); }
                    catch (COMException error) { batchResult = error.HResult; }
                }
            }
            finally
            {
                if (cookie != 0)
                    operation.Unadvise(cookie);
                Marshal.FinalReleaseComObject(operation);
            }

            // Windows reports each top-level item through PostDeleteItem. Anything it did not report is settled
            // from the filesystem so the app never claims a result it cannot see.
            foreach (RecycleItem item in queued.Values.Where(item => item.Ready))
            {
                if (!Exists(item.Path))
                    item.Succeed(recycled: true);
                else if (IsCancellation(batchResult))
                    item.Keep(ExplainResult(batchResult));
                else
                    item.Fail(batchResult < 0 ? ExplainResult(batchResult) : "Windows didn't move this item", rescan: true);
            }
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        private static bool IsCancellation(int result) =>
            unchecked((uint)result) == 0x80270000 || unchecked((uint)result) == 0x800704C7 || unchecked((uint)result) == 0x80004004;

        internal readonly struct Identity : IEquatable<Identity>
        {
            private readonly uint _volume;
            private readonly ulong _index;
            private readonly long _created;
            private readonly long _written;

            /// <summary>Volume and file index identify the item; for files the last-write time also catches edits.</summary>
            public Identity(uint volume, ulong index, FILETIME created, FILETIME written)
            {
                _volume = volume;
                _index = index;
                _created = Ticks(created);
                _written = Ticks(written);
            }

            private static long Ticks(FILETIME time) => ((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

            public bool Equals(Identity other) => _volume == other._volume && _index == other._index &&
                _created == other._created && _written == other._written;
        }

        /// <summary>Confirms the path is the same plain local item the scan saw and returns its file identity.</summary>
        internal static Identity ReadIdentity(string path, DiskEntry entry)
        {
            VerifyShellPath(path);
            const int readAttributes = 0x80;
            using (SafeFileHandle handle = NativeDirectoryReader.OpenEntry(path, readAttributes))
            {
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out HandleInformation info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var attributes = (FileAttributes)info.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("This item is a filesystem link. Open its actual location instead");
                if (entry.IsFolder != ((attributes & FileAttributes.Directory) != 0))
                    throw new IOException("This item changed type since the scan");
                if (!entry.IsFolder && entry.Bytes != (long)(((ulong)info.SizeHigh << 32) | info.SizeLow))
                    throw new IOException("This file changed size since the scan");

                string resolved = FinalPath(handle);
                if (!string.Equals(path, resolved, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(System.IO.Path.GetFileName(path), System.IO.Path.GetFileName(resolved), StringComparison.Ordinal))
                    throw new IOException("This location resolves through a link or alias. Scan the actual folder instead");
                if (SystemProtection.ForEntry(SystemProtection.ForPath(resolved, attributes), attributes, entry.Name) != EntryProtection.None)
                    throw new IOException("This location is protected by Windows");

                return new Identity(info.Volume, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, info.Creation,
                    entry.IsFolder ? default : info.Write);
            }
        }

        private static string FinalPath(SafeFileHandle handle)
        {
            var buffer = new StringBuilder(512);
            uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length >= buffer.Capacity)
            {
                buffer.EnsureCapacity(checked((int)length + 1));
                length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            }
            if (length == 0 || length >= buffer.Capacity)
                throw new IOException("Windows couldn't confirm where this item is");
            string path = buffer.ToString();
            return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path;
        }

        private static void VerifyShellPath(string path)
        {
            if (path.Length <= 3 || path[1] != ':' || path[2] != '\\' || !char.IsLetter(path[0]) ||
                new DriveInfo(path.Substring(0, 3)).DriveType == DriveType.Network)
                throw new IOException("Only items on a local drive can be moved to the Recycle Bin");

            foreach (string name in path.Substring(3).Split('\\'))
            {
                if (!NativeDirectoryReader.IsEntryName(name) || name.EndsWith(" ", StringComparison.Ordinal) ||
                    name.EndsWith(".", StringComparison.Ordinal) || IsDeviceName(name))
                    throw new IOException("Windows could read this name as a different path. Rename it in File Explorer first");
            }
        }

        private static bool IsDeviceName(string name)
        {
            int end = name.IndexOf('.');
            string stem = (end < 0 ? name : name.Substring(0, end)).TrimEnd(' ');
            foreach (string device in DeviceNames)
                if (stem.Equals(device, StringComparison.OrdinalIgnoreCase))
                    return true;
            return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".IndexOf(stem[3]) >= 0;
        }

        internal static bool IsItemError(Exception error) => DiskScanner.IsFileSystemError(error) ||
            error is Win32Exception || error is COMException || error is ArgumentException;

        internal static string Explain(Exception error)
        {
            if (error is FileNotFoundException || error is DirectoryNotFoundException ||
                (error is Win32Exception missing && (missing.NativeErrorCode == 2 || missing.NativeErrorCode == 3)))
                return "This item no longer exists";
            if (error is UnauthorizedAccessException || (error is Win32Exception denied && denied.NativeErrorCode == 5))
                return "Windows denied access to this item";
            if (error is Win32Exception shared && shared.NativeErrorCode == 32)
                return "Another app is using this item";
            if (error is COMException com)
                return ExplainResult(com.HResult);
            return error.Message.TrimEnd('.');
        }

        internal static string ExplainResult(int result)
        {
            // Copy-engine codes from the Windows SDK's sherrors.h.
            switch (unchecked((uint)result))
            {
                case 0x80070002:
                case 0x80070003:
                case 0x80270023:
                    return "This item no longer exists";
                case 0x80070005:
                case 0x80270021:
                case 0x80270022:
                    return "Windows denied access to this item. It may need administrator permission";
                case 0x80270002:
                    return "Windows needs administrator permission to remove this item";
                case 0x80070020:
                case 0x80270027:
                case 0x80270028:
                    return "An app is using this item or a file inside it. Close that app and try again";
                case 0x80270000:
                case 0x800704C7:
                case 0x80004004:
                    return "Not deleted. It was too large for the Recycle Bin and permanent deletion was declined";
                case 0x80270036:
                    return "The Recycle Bin is turned off for this drive";
                case 0x80270037:
                    return "Too large for the Recycle Bin";
                case 0x80270038:
                    return "The folder is nested too deeply for the Recycle Bin";
                case 0x8027003A:
                    return "This drive has no Recycle Bin";
                case 0x80270035:
                    return "Windows couldn't reach this item";
                default:
                    return $"Windows couldn't remove this item (0x{result:X8})";
            }
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        private sealed class ProgressSink : IFileOperationProgressSink
        {
            private readonly Dictionary<string, RecycleItem> _items;
            private readonly IProgress<RecycleProgress> _progress;
            private int _done;
            private double _fraction;
            private string _current;

            public ProgressSink(Dictionary<string, RecycleItem> items, IProgress<RecycleProgress> progress)
            {
                _items = items;
                _progress = progress;
            }

            private RecycleItem Find(IShellItem item)
            {
                if (item == null)
                    return null;
                try
                {
                    item.GetDisplayName(FileSystemPath, out string path);
                    return path != null && _items.TryGetValue(path, out RecycleItem match) ? match : null;
                }
                catch (COMException)
                {
                    return null;
                }
            }

            public int PreDeleteItem(uint flags, IShellItem item)
            {
                RecycleItem match = Find(item);
                if (match != null)
                {
                    _current = match.Name;
                    Report();
                }
                return 0;
            }

            public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem created)
            {
                RecycleItem match = Find(item);
                if (match == null || !match.Ready)
                    return 0;
                if (result < 0)
                {
                    if (IsCancellation(result))
                        match.Keep(ExplainResult(result));
                    else
                        match.Fail(ExplainResult(result));
                }
                else if (Exists(match.Path))
                {
                    match.Fail("Windows reported success, but the item is still there", rescan: true);
                }
                else
                {
                    match.Succeed(recycled: created != null);
                }
                _done++;
                Report();
                return 0;
            }

            public int UpdateProgress(uint total, uint soFar)
            {
                _fraction = total == 0 ? 0 : Math.Min(1, (double)soFar / total);
                Report();
                return 0;
            }

            private void Report() => _progress?.Report(new RecycleProgress(_done, _items.Count, _fraction, _current));

            public int StartOperations() => 0;
            public int FinishOperations(int result) => 0;
            public int PreRenameItem(uint flags, IShellItem item, string name) => 0;
            public int PostRenameItem(uint flags, IShellItem item, string name, int result, IShellItem created) => 0;
            public int PreMoveItem(uint flags, IShellItem item, IShellItem folder, string name) => 0;
            public int PostMoveItem(uint flags, IShellItem item, IShellItem folder, string name, int result, IShellItem created) => 0;
            public int PreCopyItem(uint flags, IShellItem item, IShellItem folder, string name) => 0;
            public int PostCopyItem(uint flags, IShellItem item, IShellItem folder, string name, int result, IShellItem created) => 0;
            public int PreNewItem(uint flags, IShellItem folder, string name) => 0;
            public int PostNewItem(uint flags, IShellItem folder, string name, string template, uint attributes, int result, IShellItem item) => 0;
            public int ResetTimer() => 0;
            public int PauseTimer() => 0;
            public int ResumeTimer() => 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HandleInformation
        {
            public uint Attributes;
            public FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, LinkCount, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInformation info);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid id,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handlerId, ref Guid interfaceId, out IntPtr handler);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint nameKind, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        }

        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            void Advise(IFileOperationProgressSink sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOperationFlags(uint flags);
            void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
            void SetProgressDialog(IntPtr dialog);
            void SetProperties(IntPtr properties);
            void SetOwnerWindow(IntPtr owner);
            void ApplyPropertiesToItem(IShellItem item);
            void ApplyPropertiesToItems(IntPtr items);
            void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
            void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
            void MoveItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
            void MoveItems(IntPtr items, IShellItem folder);
            void CopyItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
            void CopyItems(IntPtr items, IShellItem folder);
            void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
            void DeleteItems(IntPtr items);
            void NewItem(IShellItem folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
                [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink sink);
            void PerformOperations();
            [return: MarshalAs(UnmanagedType.Bool)] bool GetAnyOperationsAborted();
        }

        [ComImport, Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IFileOperationProgressSink
        {
            [PreserveSig] int StartOperations();
            [PreserveSig] int FinishOperations(int result);
            [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
            [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
            [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
            [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
            [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem created);
            [PreserveSig] int PreNewItem(uint flags, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostNewItem(uint flags, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name,
                [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem item);
            [PreserveSig] int UpdateProgress(uint total, uint soFar);
            [PreserveSig] int ResetTimer();
            [PreserveSig] int PauseTimer();
            [PreserveSig] int ResumeTimer();
        }
    }
}
