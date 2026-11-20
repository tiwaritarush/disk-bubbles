using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace DiskBubbles
{
    internal static class ExplorerShell
    {
        private const uint UseIdList = 0x4;
        private const uint WaitForShell = 0x100;
        private const uint SuppressShellErrors = 0x400;
        internal sealed class Target : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal string Path { get; }
            internal bool IsFolder { get; }
            internal Target(IntPtr item, string path, bool folder) : base(true)
            {
                SetHandle(item);
                Path = path;
                IsFolder = folder;
            }
            protected override bool ReleaseHandle() { Marshal.FreeCoTaskMem(handle); return true; }
        }

        internal static Target Resolve(string path, bool folder)
        {
            string fullPath = System.IO.Path.GetFullPath(path);
            if (fullPath.Length > System.IO.Path.GetPathRoot(fullPath).Length)
                fullPath = fullPath.TrimEnd('\\');
            FileAttributes attributes = File.GetAttributes(fullPath);
            if (((attributes & FileAttributes.Directory) != 0) != folder)
                throw new IOException("The item type changed. Rescan this folder before opening it.");
            IntPtr item = IntPtr.Zero;
            IntPtr name = IntPtr.Zero;
            try
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(fullPath, IntPtr.Zero, out item, 0, out _));
                if (item == IntPtr.Zero)
                    throw new IOException("Windows could not identify this item.");
                const uint fileSystemPath = 0x80058000;
                Marshal.ThrowExceptionForHR(SHGetNameFromIDList(item, fileSystemPath, out name));
                string shellPath = Marshal.PtrToStringUni(name);
                if (string.IsNullOrEmpty(shellPath) || !string.Equals(fullPath, shellPath, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Windows resolved another location. Rescan the actual folder.");
                var target = new Target(item, shellPath, folder);
                item = IntPtr.Zero;
                return target;
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
                Marshal.FreeCoTaskMem(item);
            }
        }

        internal static Task OpenAsync(string path, bool folder, IntPtr owner)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() =>
            {
                int initialized = CoInitializeEx(IntPtr.Zero, 2);
                try
                {
                    Marshal.ThrowExceptionForHR(initialized);
                    using var target = Resolve(path, folder);
                    if (folder)
                    {
                        var request = new ExecuteInfo
                        {
                            Size = Marshal.SizeOf<ExecuteInfo>(), Mask = UseIdList | WaitForShell | SuppressShellErrors,
                            Owner = owner, Verb = "explore", Item = target.DangerousGetHandle(), Show = 1,
                        };
                        if (!ShellExecuteEx(ref request))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    else
                        Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(target, 0, IntPtr.Zero, 0));
                    completion.SetResult(true);
                }
                catch (Exception error) { completion.SetException(error); }
                finally { if (initialized >= 0) CoUninitialize(); }
            }) { IsBackground = true, Name = "Open in Explorer" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return completion.Task;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ExecuteInfo
        {
            public int Size;
            public uint Mask;
            public IntPtr Owner;
            public string Verb, File, Parameters, Directory;
            public int Show;
            public IntPtr Instance, Item;
            public string Class;
            public IntPtr ClassKey;
            public uint HotKey;
            public IntPtr Icon, Process;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr item, uint attributes, out uint returned);
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHGetNameFromIDList(IntPtr item, uint form, out IntPtr name);
        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(Target folder, uint count, IntPtr children, uint flags);
        [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShellExecuteEx(ref ExecuteInfo request);
        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoInitializeEx(IntPtr reserved, int apartment);
        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern void CoUninitialize();
    }
}
