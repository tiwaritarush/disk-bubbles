using System;
using System.Collections.Generic;
using System.IO;

namespace DiskBubbles
{
    [Flags]
    internal enum EntryProtection : byte
    {
        None = 0,
        System = 1,
        Managed = 2,
        Incomplete = 4,
        ProtectedDescendant = 8,
        CriticalFolder = 16,
    }

    internal static class SystemProtection
    {
        private static readonly string WindowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static readonly string WindowsDrive = Path.GetPathRoot(WindowsFolder);
        private static readonly string[] ManagedFolders =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };

        private static readonly HashSet<string> SystemVolumeEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin", "System Volume Information", "Recovery", "Boot", "EFI", "bootmgr", "bootmgr.efi",
            "BOOTNXT", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp",
        };

        private static readonly HashSet<string> CriticalBootFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Recovery", "Boot", "EFI",
        };

        public static EntryProtection ForPath(string path, FileAttributes attributes = 0)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                path = path.Substring(4);

            string root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return EntryProtection.Managed;

            if (IsInside(path, WindowsFolder))
                return WindowsFolder.Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    ? EntryProtection.System | EntryProtection.CriticalFolder : EntryProtection.System;

            int start = root.Length;
            while (start < path.Length && path[start] == '\\')
                start++;
            int firstSeparator = path.IndexOf('\\', start);
            string first = firstSeparator < 0 ? path.Substring(start) : path.Substring(start, firstSeparator - start);
            if (SystemVolumeEntries.Contains(first))
            {
                bool localVolume = root.Length == 3 && root[1] == ':';
                bool exact = firstSeparator < 0 || firstSeparator == path.Length - 1;
                bool critical = first.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                    (root.Equals(WindowsDrive, StringComparison.OrdinalIgnoreCase) && CriticalBootFolders.Contains(first) &&
                        (attributes & FileAttributes.System) != 0);
                return localVolume && exact && critical ? EntryProtection.System | EntryProtection.CriticalFolder : EntryProtection.System;
            }

            foreach (string folder in ManagedFolders)
                if (IsInside(path, folder))
                    return EntryProtection.Managed;

            return EntryProtection.None;
        }

        public static EntryProtection ForEntry(EntryProtection folder, FileAttributes attributes) =>
            (folder & (EntryProtection.System | EntryProtection.Managed)) |
            ((attributes & FileAttributes.System) != 0 ? EntryProtection.System : EntryProtection.None);

        public static bool IsInside(string path, string folder) => !string.IsNullOrEmpty(folder) &&
            (path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
             (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && path.Length > folder.Length && path[folder.Length] == '\\'));

        public static string Reason(DiskEntry item)
        {
            if (item.IsSystem)
                return "Critical Windows folder";
            if ((item.Protection & EntryProtection.System) != 0)
                return "Protected Windows item";
            if ((item.Protection & EntryProtection.Managed) != 0)
                return "Windows-managed location";
            if ((item.Protection & EntryProtection.Incomplete) != 0)
                return "Folder contains entries the scan skipped";
            return "Folder contains protected or unscanned items";
        }
    }
}
