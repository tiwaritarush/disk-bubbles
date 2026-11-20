using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace DiskBubbles
{
    internal enum AppUninstallResult
    {
        Removed,
        RestartRequired,
        StillRegistered,
    }

    internal static class AppRemoval
    {
        private static readonly Regex ExeCommand = new Regex(@"^(?:""(?<path>[^""\r\n]+\.exe)""|(?<path>[^""\r\n]+?\.exe))(?:[ \t]+(?<args>[^\r\n]*))?$", RegexOptions.IgnoreCase);
        private static readonly Regex VendorTool = new Regex(@"^(?:unins|remove|maintenancetool)[a-z0-9 ._()-]*$", RegexOptions.IgnoreCase);
        private static readonly Regex UpdaterUninstall = new Regex(@"^--uninstall(?:[ \t]+-s)?$", RegexOptions.IgnoreCase);
        private static readonly Regex SetupUninstall = new Regex(@"(?:^|[ \t])(?:--uninstall|/uninstall|/x)(?:[ \t]|$)", RegexOptions.IgnoreCase);
        private static readonly Regex DriverName = new Regex(@"\bdrivers?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool IsDriverName(string name) => DriverName.IsMatch(name);

        internal static ProcessStartInfo DesktopLauncher(InstalledApp app)
        {
            if (app.Locked || string.IsNullOrEmpty(app.Command))
                return null;
            var command = ExeCommand.Match(app.Command.Trim());
            if (!command.Success)
                return null;

            string path = Environment.ExpandEnvironmentVariables(command.Groups["path"].Value);
            if (app.WindowsInstaller && Guid.TryParse(app.Key, out Guid product) &&
                Path.GetFileName(path).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
                return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), $"/x {product:B} /norestart")
                {
                    WorkingDirectory = Environment.SystemDirectory, UseShellExecute = true,
                };

            string args = command.Groups["args"].Value.Trim();
            string fileName = Path.GetFileNameWithoutExtension(path);
            bool uninstallCommand = VendorTool.IsMatch(fileName) ||
                (fileName.Equals("Update", StringComparison.OrdinalIgnoreCase) && UpdaterUninstall.IsMatch(args)) ||
                (fileName.Equals("Setup", StringComparison.OrdinalIgnoreCase) && SetupUninstall.IsMatch(args));
            if (AppInventory.Normalize(path) == null || !File.Exists(path) || !uninstallCommand)
                return null;
            string directory = Path.GetDirectoryName(path);
            if (!AppInventory.IsDedicated(directory) || (app.Location != null && !SystemProtection.IsInside(directory, app.Location) &&
                (app.RegisteredLocation || !SystemProtection.IsInside(app.Location, directory))) ||
                !AppInventory.IsPlainDirectory(directory) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return null;

            return new ProcessStartInfo(path, args) { WorkingDirectory = directory, UseShellExecute = true };
        }

        internal static bool CanLaunch(InstalledApp app)
        {
            if (app.Locked)
                return false;
            if (app.Package != null)
                return true;
            try { return DesktopLauncher(app) != null; }
            catch (Exception error) when (AppInventory.IsRegistryError(error) || error is ArgumentException) { return false; }
        }

        internal static AppUninstallResult Uninstall(InstalledApp app)
        {
            if (app.Locked)
                throw new IOException("This entry is managed by Windows or is a driver package.");

            if (app.Package != null)
            {
                AppInventory.RemovePackage(app.Package, CancellationToken.None);
                return AppUninstallResult.Removed;
            }

            var current = AppInventory.ReadCurrent(app);
            if (current == null || current.Command != app.Command || current.Name != app.Name ||
                current.Version != app.Version || !string.Equals(current.Location, app.Location, StringComparison.OrdinalIgnoreCase))
                throw new IOException("This app’s registration changed. Refresh the list and review it again.");
            var launcher = DesktopLauncher(current);
            if (launcher == null)
                throw new IOException("Disk Bubbles couldn’t verify this app’s registered uninstaller.");
            using var process = Process.Start(launcher);
            if (process == null)
                throw new IOException("Windows couldn’t start the registered uninstaller.");
            process.WaitForExit();
            if (process.ExitCode == 3010 || process.ExitCode == 1641)
                return AppUninstallResult.RestartRequired;
            if (process.ExitCode != 0)
                throw new IOException($"The uninstaller reported exit code {process.ExitCode}.");
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (!AppInventory.IsRegistered(app))
                    return AppUninstallResult.Removed;
                Thread.Sleep(400);
            }
            return AppUninstallResult.StillRegistered;
        }
    }

    internal sealed class AppLeftover
    {
        public string Name { get; }
        public string Location { get; }
        public string SizeLabel => DiskEntry.FormatSize(Bytes);
        public string AccessibleName => $"{Name}, {Kind}, {SizeLabel}";
        internal long Bytes { get; }
        internal InstalledApp App { get; }

        internal AppLeftover(InstalledApp app, string path, string kind, long bytes)
        {
            App = app;
            Name = app.Name;
            Location = path;
            Kind = kind;
            Bytes = bytes;
        }

        internal string Kind { get; }
    }
}
