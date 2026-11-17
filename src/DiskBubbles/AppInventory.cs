using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Win32;

namespace DiskBubbles
{
    internal sealed class InstalledApp
    {
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public string Location { get; set; }
        public string Package { get; set; }
        public ImageSource Icon { get; set; }
        public long? Bytes { get; set; }
        public bool PartialSize { get; set; }
        public bool Marked { get; set; }
        public bool CanMark => !Locked;
        public string Kind => Package == null ? "Desktop" : "Store";
        public string SizeLabel => Bytes is long bytes ? (PartialSize ? "≥ " : "") + DiskEntry.FormatSize(bytes) : "Unavailable";
        public string AccessibleName => $"{Name}, {Kind}, {SizeLabel}";
        internal RegistryHive Hive;
        internal RegistryView View;
        internal string Key;
        internal string Command;
        internal string DisplayIcon;
        internal string PackageFamily;
        internal bool RegisteredLocation;
        internal bool WindowsInstaller;
        internal bool Locked;
        internal string Id => Package ?? $"{Hive}:{View}:{Key}";
    }

    internal sealed class AppInventory
    {
        private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string StoreFilter = "$_.NonRemovable -ne $true -and !$_.IsFramework -and !$_.IsResourcePackage -and " +
            "$_.SignatureKind -ne 'System' -and $_.Name -notin @('Microsoft.WindowsStore','Microsoft.StorePurchaseApp','Microsoft.DesktopAppInstaller')";
        private const string ReadPackages = @"
$labels = @{}
try {
  Get-StartApps | ForEach-Object {
    $family = ($_.AppID -split '!',2)[0]
    if (!$labels.ContainsKey($family)) { $labels[$family] = $_.Name }
  }
} catch {}
Get-AppxPackage -PackageTypeFilter Main,Bundle | Where-Object { " + StoreFilter + @" } |
  Group-Object PackageFamilyName | ForEach-Object {
    $main = $_.Group | Where-Object {!$_.IsBundle} | Select-Object -First 1
    $package = $_.Group | Sort-Object IsBundle -Descending | Select-Object -First 1
    $name = $labels[$package.PackageFamilyName]
    if (!$name) { $name = $package.Name }
    $fields = @($package.PackageFullName, $name, [string]$package.Version, $main.InstallLocation, $package.PackageFamilyName)
    ($fields | ForEach-Object { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_)) }) -join [char]9
  }";
        private static readonly RegistryHive[] Hives = { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
        private static readonly RegistryView[] Views = { RegistryView.Registry64, RegistryView.Registry32 };
        private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
        internal static readonly string UserPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        internal static readonly string[] ProgramRoots =
        {
            UserPrograms, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        private static readonly string[] WideRoots = ProgramRoots.Concat(new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
        }).ToArray();

        public List<InstalledApp> Apps { get; } = new List<InstalledApp>();
        public bool StoreUnavailable { get; private set; }

        public static AppInventory Scan(CancellationToken token, IProgress<string> progress)
        {
            var inventory = new AppInventory();
            foreach (var hive in Hives)
                foreach (var view in Views)
                    inventory.Apps.AddRange(ReadDesktop(hive, view, token));

            inventory.Apps.RemoveAll(app => inventory.Apps.Any(other => other != app && other.Hive == app.Hive &&
                other.View == RegistryView.Registry64 && app.View == RegistryView.Registry32 && Paths.Equals(app.Key, other.Key) &&
                Paths.Equals(app.Command, other.Command) && Paths.Equals(app.Location, other.Location)));
            try
            {
                progress?.Report("Checking Microsoft Store apps…");
                foreach (string row in RunPowerShell(ReadPackages, null, token).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] fields = row.Split('\t');
                    if (fields.Length != 5)
                        continue;
                    string[] values = fields.Select(field => Encoding.UTF8.GetString(Convert.FromBase64String(field))).ToArray();
                    inventory.Apps.Add(new InstalledApp
                    {
                        Package = values[0], Name = values[1], Version = values[2], Location = Normalize(values[3]), PackageFamily = values[4],
                    });
                }
            }
            catch (Exception error) when (DiskScanner.IsFileSystemError(error) || error is Win32Exception || error is FormatException)
            {
                inventory.StoreUnavailable = true;
            }

            var folders = inventory.Apps.Where(app => app.Location != null).GroupBy(app => app.Location, Paths).ToArray();
            int complete = 0;
            progress?.Report($"Measuring {folders.Length:N0} install folders…");
            Parallel.ForEach(folders, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = 3 }, group =>
            {
                var size = Measure(group.Key, token);
                foreach (var app in group)
                {
                    app.Bytes = size.Bytes;
                    app.PartialSize = size.Partial;
                }
                int done = Interlocked.Increment(ref complete);
                if (done % 8 == 0)
                    progress?.Report($"Measured {done:N0} of {folders.Length:N0} folders…");
            });
            progress?.Report("Loading app icons…");
            Parallel.ForEach(inventory.Apps, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = 3 },
                app => app.Icon = AppIcons.Load(app));
            return inventory;
        }

        private static List<InstalledApp> ReadDesktop(RegistryHive hive, RegistryView view, CancellationToken token)
        {
            var apps = new List<InstalledApp>();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(RegistryPath);
                if (uninstall == null)
                    return apps;
                foreach (string key in uninstall.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var registration = uninstall.OpenSubKey(key);
                        var app = ReadDesktop(registration, hive, view, key);
                        if (app != null)
                            apps.Add(app);
                    }
                    catch (Exception error) when (IsRegistryError(error)) { }
                }
            }
            catch (Exception error) when (IsRegistryError(error)) { }
            return apps;
        }

        internal static InstalledApp ReadCurrent(InstalledApp app)
        {
            using var root = RegistryKey.OpenBaseKey(app.Hive, app.View);
            using var registration = root.OpenSubKey(RegistryPath + "\\" + app.Key);
            return ReadDesktop(registration, app.Hive, app.View, app.Key);
        }

        internal static bool IsRegistered(InstalledApp app)
        {
            using var root = RegistryKey.OpenBaseKey(app.Hive, app.View);
            using var registration = root.OpenSubKey(RegistryPath + "\\" + app.Key);
            return registration != null;
        }

        internal static bool HasOtherRegistration(string location, InstalledApp owner)
        {
            foreach (var hive in Hives)
                foreach (var view in Views)
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(RegistryPath);
                    if (uninstall == null)
                        continue;
                    foreach (string name in uninstall.GetSubKeyNames())
                    {
                        using var key = uninstall.OpenSubKey(name);
                        if (key == null)
                            continue;
                        if (owner.Package == null && Paths.Equals(Convert.ToString(key.GetValue("DisplayName")).Trim(), owner.Name) &&
                            Paths.Equals(Convert.ToString(key.GetValue("Publisher")).Trim(), owner.Publisher))
                            return true;
                        string other = Normalize(Convert.ToString(key.GetValue("InstallLocation")))
                            ?? IconLocation(Convert.ToString(key.GetValue("DisplayIcon")));
                        if (other != null && (SystemProtection.IsInside(other, location) || SystemProtection.IsInside(location, other)))
                            return true;
                    }
                }
            return false;
        }

        private static InstalledApp ReadDesktop(RegistryKey registration, RegistryHive hive, RegistryView view, string key)
        {
            if (registration == null)
                return null;
            string Read(string name) => Convert.ToString(registration.GetValue(name))?.Trim() ?? "";
            string name = Read("DisplayName");
            if (name.Length == 0 || Read("SystemComponent") == "1" || Read("ParentKeyName").Length != 0 ||
                Read("ReleaseType").Length != 0 || name.StartsWith("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft .NET", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft Windows Desktop Runtime", StringComparison.OrdinalIgnoreCase))
                return null;

            string displayIcon = Read("DisplayIcon");
            string registered = Normalize(Read("InstallLocation"));
            return new InstalledApp
            {
                Name = name, Publisher = Read("Publisher"), Version = Read("DisplayVersion"),
                Location = registered ?? IconLocation(displayIcon), RegisteredLocation = registered != null,
                Hive = hive, View = view, Key = key, Command = Read("UninstallString"), DisplayIcon = displayIcon,
                WindowsInstaller = Read("WindowsInstaller") == "1",
                Locked = Read("NoRemove") == "1" || Read("DriverPackage") == "1" || AppRemoval.IsDriverName(name),
            };
        }

        private static string IconLocation(string icon)
        {
            if (string.IsNullOrEmpty(icon))
                return null;
            int end = icon.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end <= 0)
                return null;
            string executable = Normalize(icon.Substring(0, end + 4).Trim('"'));
            string folder = executable == null ? null : Normalize(Path.GetDirectoryName(executable));
            return IsDedicated(folder) ? folder : null;
        }

        internal static bool IsRegistryError(Exception error) => DiskScanner.IsFileSystemError(error) || error is SecurityException;

        internal static string Normalize(string value) => NormalizePath(value, false);

        internal static string NormalizeIconFile(string value) => NormalizePath(value, true);

        private static string NormalizePath(string value, bool icon)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            try
            {
                string path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                if (path.Length < 4 || !char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
                    path.IndexOfAny(new[] { '"', '\r', '\n', '/' }) >= 0 || path.Substring(2).Contains(":"))
                    return null;
                foreach (string part in path.TrimEnd('\\').Substring(3).Split('\\'))
                    if (!NativeDirectoryReader.IsEntryName(part) || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal))
                        return null;
                path = Path.GetFullPath(path).TrimEnd('\\');
                if (WideRoots.Any(root => !string.IsNullOrEmpty(root) && SystemProtection.IsInside(root, path)) ||
                    (!icon && (SystemProtection.ForPath(path) & EntryProtection.System) != 0) ||
                    new DriveInfo(Path.GetPathRoot(path)).DriveType != DriveType.Fixed)
                    return null;
                return path;
            }
            catch (Exception error) when (DiskScanner.IsFileSystemError(error) || error is ArgumentException) { return null; }
        }

        internal static bool IsDedicated(string path) => path != null && ProgramRoots.Any(root => !Paths.Equals(path, root) && SystemProtection.IsInside(path, root)) &&
            !path.Split('\\').Any(part => Paths.Equals(part, "Common Files") || Paths.Equals(part, "WindowsApps") || Paths.Equals(part, "ModifiableWindowsApps"));

        internal static bool IsPlainDirectory(string path)
        {
            try
            {
                for (string part = path; part != null && part.Length > 3; part = Path.GetDirectoryName(part))
                {
                    var attributes = File.GetAttributes(part);
                    if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                        return false;
                }
                return Directory.Exists(path);
            }
            catch (Exception error) when (DiskScanner.IsFileSystemError(error) || error is ArgumentException) { return false; }
        }

        internal static (long? Bytes, bool Partial) Measure(string path, CancellationToken token)
        {
            if (!IsPlainDirectory(path))
                return (null, false);
            using var reader = new NativeDirectoryReader();
            var pending = new Stack<string>();
            pending.Push(path);
            bool partial = false;
            long bytes = 0;
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string folder = pending.Pop();
                try
                {
                    bool read = reader.TryRead(folder, false, token, (name, attributes, size) =>
                    {
                        if ((attributes & FileAttributes.ReparsePoint) != 0 || !NativeDirectoryReader.IsEntryName(name))
                            partial = true;
                        else if ((attributes & FileAttributes.Directory) != 0)
                            pending.Push(Path.Combine(folder, name));
                        else
                            bytes = DiskScanner.AddBytes(bytes, size);
                    });
                    if (!read && folder == path)
                        return (null, false);
                    partial |= !read;
                }
                catch (Exception error) when (DiskScanner.IsFileSystemError(error) || error is ArgumentException)
                {
                    if (folder == path) return (null, false);
                    partial = true;
                }
            }
            return (bytes, partial);
        }

        internal static void RemovePackage(string package, CancellationToken token) => RunPowerShell(@"
$package = Get-AppxPackage -PackageTypeFilter Main,Bundle | Where-Object { $_.PackageFullName -ceq $env:DISK_BUBBLES_PACKAGE -and (" + StoreFilter + @") }
if (@($package).Count -ne 1) { throw 'This package is no longer available for uninstall.' }
Remove-AppxPackage -Package $package.PackageFullName -Confirm:$false
$remaining = Get-AppxPackage -PackageTypeFilter Main,Bundle | Where-Object {$_.PackageFamilyName -ceq $package.PackageFamilyName}
if ($remaining) { throw 'The package is still registered.' }
", package, token);

        internal static bool IsPackageFamilyPresent(string family)
        {
            string result = RunPowerShell(@"
if (Get-AppxPackage -PackageTypeFilter Main,Bundle | Where-Object {$_.PackageFamilyName -ceq $env:DISK_BUBBLES_PACKAGE}) { 'present' }
else { 'absent' }
", family, CancellationToken.None, catalogQuery: true).Trim();
            if (result == "present") return true;
            if (result == "absent") return false;
            throw new IOException("Windows did not confirm the Store package status.");
        }

        private static string RunPowerShell(string code, string package, CancellationToken token, bool catalogQuery = false)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop';\n" + code)))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Environment.SystemDirectory,
            };
            start.EnvironmentVariables["DISK_BUBBLES_PACKAGE"] = package ?? "";
            start.EnvironmentVariables["PSModulePath"] = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules");
            using var process = Process.Start(start);
            if (process == null)
                throw new IOException("Windows could not open the package catalog.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            var timer = Stopwatch.StartNew();
            while (!process.WaitForExit(80))
            {
                if ((package != null && !catalogQuery) || (!token.IsCancellationRequested && timer.Elapsed < TimeSpan.FromSeconds(18)))
                    continue;
                try { process.Kill(); } catch (InvalidOperationException) { }
                token.ThrowIfCancellationRequested();
                throw new IOException("Windows did not return the Store catalog in time.");
            }
            errors.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                throw new IOException(package == null || catalogQuery ? "Windows did not return the Store catalog." : "Windows could not confirm that this Store app was removed.");
            return output.GetAwaiter().GetResult();
        }
    }
}
