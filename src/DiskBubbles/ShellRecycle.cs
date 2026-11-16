using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace DiskBubbles
{
    internal static class ShellRecycle
    {
        private static readonly Guid RecycleBin = new Guid("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC");
        private static readonly Guid Transfer = new Guid("D5E346A1-F753-4932-B403-4574800E2498");
        private static readonly string[] DeviceNames = { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" };

        public static EntrySnapshot ReadSnapshot(string path, DiskEntry entry)
        {
            const int readAttributes = 0x80;
            VerifyShellPath(path);
            using (var handle = NativeDirectoryReader.OpenEntry(path, readAttributes))
            {
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out HandleInformation info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var attributes = (FileAttributes)info.Attributes;
                if ((attributes & (FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
                    throw new IOException("Windows identifies this as a system item or filesystem link.");
                if (entry.IsFolder != ((attributes & FileAttributes.Directory) != 0))
                    throw new IOException("The item type changed. Rescan this folder to review it.");
                if (!entry.IsFolder && entry.Bytes != (long)(((ulong)info.SizeHigh << 32) | info.SizeLow))
                    throw new IOException("The file size changed. Rescan this folder to review it.");

                var buffer = new StringBuilder(512);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length >= buffer.Capacity)
                {
                    buffer.EnsureCapacity(checked((int)length + 1));
                    length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                }
                if (length == 0 || length >= buffer.Capacity)
                    throw new IOException("Windows could not verify the item's location.");

                string fullPath = buffer.ToString();
                if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
                    fullPath = fullPath.Substring(4);
                if (!IsSelectedPath(path, fullPath))
                    throw new IOException("This location resolves through a link or alias. Scan the actual folder to review it.");
                if (SystemProtection.ForPath(fullPath) != EntryProtection.None)
                    throw new IOException("This location is protected.");

                const int fileIdInfo = 18;
                if (!GetFileId(handle, fileIdInfo, out FileIdentity identity, Marshal.SizeOf(typeof(FileIdentity))) ||
                    (identity.Low == 0 && identity.High == 0))
                    throw new IOException("Windows could not verify the item's file identity.");
                return new EntrySnapshot(fullPath, identity, info.Creation, info.Write);
            }
        }

        private static bool IsSelectedPath(string requested, string resolved) =>
            string.Equals(requested, resolved, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(requested), Path.GetFileName(resolved), StringComparison.Ordinal);

        private static void VerifyShellPath(string path)
        {
            if (path.Length <= 3 || path[1] != ':' || path[2] != '\\' || !char.IsLetter(path[0]) ||
                new DriveInfo(path.Substring(0, 3)).DriveType == DriveType.Network)
                throw new IOException("Recycling requires a local Windows drive path.");

            foreach (string name in path.Substring(3).Split('\\'))
            {
                if (!NativeDirectoryReader.IsEntryName(name) || name.EndsWith(" ", StringComparison.Ordinal) ||
                    name.EndsWith(".", StringComparison.Ordinal) || IsDeviceName(name))
                    throw new IOException("Windows Shell could interpret this name as a different path.");
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

        public static void Move(EntrySnapshot snapshot, DiskEntry entry)
        {
            IShellItem item = null;
            IShellItem parent = null;
            IShellItem bin = null;
            IShellItem recycled = null;
            object handler = null;
            try
            {
                Guid itemId = typeof(IShellItem).GUID;
                Guid sourceId = typeof(ITransferSource).GUID;
                Guid folderId = RecycleBin;
                Guid transferId = Transfer;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(snapshot.Path, IntPtr.Zero, ref itemId, out item));
                Marshal.ThrowExceptionForHR(SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref itemId, out bin));
                item.GetParent(out parent);
                parent.BindToHandler(IntPtr.Zero, ref transferId, ref sourceId, out handler);
                const uint fileSystemPath = 0x80058000;
                item.GetDisplayName(fileSystemPath, out string shellPath);
                if (!snapshot.Matches(ReadSnapshot(shellPath, entry)))
                    throw new IOException("The Windows Shell resolved a different item. Rescan to review it again.");

                int result = ((ITransferSource)handler).RecycleItem(item, bin, 0, out recycled);
                Marshal.ThrowExceptionForHR(result);
                if (result != 0 || recycled == null)
                    throw new UnconfirmedMoveException();
            }
            finally
            {
                Release(recycled);
                Release(handler);
                Release(bin);
                Release(parent);
                Release(item);
            }
        }

        private static void Release(object value)
        {
            if (value != null)
                Marshal.ReleaseComObject(value);
        }

        internal sealed class UnconfirmedMoveException : IOException
        {
            public UnconfirmedMoveException() : base("Windows did not confirm the move to the Recycle Bin.") { }
        }

        internal readonly struct EntrySnapshot
        {
            private readonly FileIdentity _id;
            private readonly FILETIME _created;
            private readonly FILETIME _written;
            public string Path { get; }

            public EntrySnapshot(string path, FileIdentity id, FILETIME created, FILETIME written)
            {
                Path = path;
                _id = id;
                _created = created;
                _written = written;
            }

            public bool Matches(EntrySnapshot other) =>
                string.Equals(Path, other.Path, StringComparison.Ordinal) && _id.Volume == other._id.Volume &&
                _id.Low == other._id.Low && _id.High == other._id.High && _created.Equals(other._created) &&
                _written.Equals(other._written);

            internal void WriteIdentity(BinaryWriter writer)
            {
                writer.Write(Path);
                writer.Write(_id.Volume);
                writer.Write(_id.Low);
                writer.Write(_id.High);
                writer.Write(_created.dwHighDateTime);
                writer.Write(_created.dwLowDateTime);
                writer.Write(_written.dwHighDateTime);
                writer.Write(_written.dwLowDateTime);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileIdentity
        {
            public ulong Volume, Low, High;
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

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileId(SafeFileHandle handle, int infoClass, out FileIdentity info, int length);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid id,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHGetKnownFolderItem(ref Guid folder, uint flags, IntPtr token, ref Guid id,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handlerId, ref Guid interfaceId,
                [MarshalAs(UnmanagedType.Interface)] out object handler);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint nameKind, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        }

        [ComImport, Guid("00ADB003-BDE9-45C6-8E29-D09F9353E108"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITransferSource
        {
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetProperties(IntPtr properties);
            void OpenItem(IShellItem item, uint flags, ref Guid id, out IntPtr opened);
            void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name,
                uint flags, out IShellItem moved);
            [PreserveSig] int RecycleItem(IShellItem item, IShellItem destination, uint flags, out IShellItem recycled);
        }
    }
}
