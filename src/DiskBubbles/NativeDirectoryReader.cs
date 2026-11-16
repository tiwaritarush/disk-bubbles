using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DiskBubbles
{
    internal sealed class NativeDirectoryReader : IDisposable
    {
        private const int BufferSize = 64 * 1024;
        private const int FileFullDirectoryInfo = 14;
        private const int FileListDirectory = 1;
        private const int ShareReadWriteDelete = 7;
        private const int OpenExisting = 3;
        private const int BackupSemantics = 0x02000000;
        private const int OpenReparsePoint = 0x00200000;
        private const int NoMoreFiles = 18;
        private const int InvalidDirectory = 267;
        private const int NameOffset = 68;
        private static readonly char[] InvalidName = Path.GetInvalidFileNameChars();
        private readonly IntPtr _buffer = Marshal.AllocHGlobal(BufferSize);

        [StructLayout(LayoutKind.Explicit)]
        private struct DirectoryRecord
        {
            [FieldOffset(0)] public int NextOffset;
            [FieldOffset(40)] public long EndOfFile;
            [FieldOffset(56)] public FileAttributes Attributes;
            [FieldOffset(60)] public int NameLength;
        }

        public unsafe bool TryRead(string directory, bool followRoot, CancellationToken token,
            Action<string, FileAttributes, long> accept)
        {
            token.ThrowIfCancellationRequested();
            using (SafeFileHandle handle = OpenEntry(directory, FileListDirectory, followRoot))
            {
                if (handle.IsInvalid)
                    return false;

                bool readAnyBatch = false;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (!GetFileInformationByHandleEx(handle, FileFullDirectoryInfo, _buffer, BufferSize))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == NoMoreFiles)
                            return true;
                        if (!readAnyBatch && (followRoot || error != InvalidDirectory))
                            return false;
                        throw new IOException("Windows could not enumerate the directory.", new Win32Exception(error));
                    }

                    readAnyBatch = true;
                    ReadBatch((byte*)_buffer, token, accept);
                }
            }
        }

        private static unsafe void ReadBatch(byte* buffer, CancellationToken token, Action<string, FileAttributes, long> accept)
        {
            int offset = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (offset > BufferSize - NameOffset)
                    throw new IOException("Windows returned an invalid directory record.");

                byte* start = buffer + offset;
                DirectoryRecord record = *(DirectoryRecord*)start;
                int length = record.NextOffset == 0 ? BufferSize - offset : record.NextOffset;
                if (length < NameOffset || length > BufferSize - offset || record.NameLength <= 0 ||
                    record.NameLength > length - NameOffset || (record.NameLength & 1) != 0 ||
                    (record.NextOffset & 7) != 0)
                    throw new IOException("Windows returned an invalid directory record.");

                char* name = (char*)(start + NameOffset);
                int chars = record.NameLength / sizeof(char);
                bool isDot = name[0] == '.' && (chars == 1 || (chars == 2 && name[1] == '.'));
                if (!isDot)
                    accept(new string(name, 0, chars), record.Attributes, Math.Max(0, record.EndOfFile));

                if (record.NextOffset == 0)
                    return;
                offset += record.NextOffset;
            }
        }

        private static string ExtendedPath(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
                return path;
            return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }

        internal static bool IsEntryName(string name) => !string.IsNullOrEmpty(name) && name != "." && name != ".." &&
            name.IndexOfAny(InvalidName) < 0;

        internal static SafeFileHandle OpenEntry(string path, int access, bool followLink = false) =>
            CreateFile(ExtendedPath(path), access, ShareReadWriteDelete, IntPtr.Zero, OpenExisting,
                BackupSemantics | (followLink ? 0 : OpenReparsePoint), IntPtr.Zero);

        public static void ReadCapacity(string path, ScanResult result)
        {
            string directory = ExtendedPath(path.TrimEnd('\\') + "\\");
            if (GetDiskFreeSpaceEx(directory, out ulong free, out ulong capacity, out _) &&
                capacity > 0 && capacity <= long.MaxValue && free <= capacity)
            {
                result.VolumeBytes = (long)capacity;
                result.FreeBytes = (long)free;
            }
        }

        public void Dispose() => Marshal.FreeHGlobal(_buffer);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFile(string name, int access, int shareMode, IntPtr security,
            int creationDisposition, int flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, IntPtr buffer, int size);

        [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong totalFree);
    }
}
