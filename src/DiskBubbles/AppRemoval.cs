using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace DiskBubbles
{
    internal static class AppRemoval
    {
        private static readonly Regex ExeCommand = new Regex(@"^(?:""(?<path>[^""\r\n]+\.exe)""|(?<path>[^""\r\n]+?\.exe))(?:[ \t]+(?<args>[^\r\n]*))?$", RegexOptions.IgnoreCase);
        private static readonly Regex VendorTool = new Regex(@"^(?:unins|uninstall|uninst|remove|maintenancetool|setup)[a-z0-9 ._()-]*$", RegexOptions.IgnoreCase);

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

            if (AppInventory.Normalize(path) == null || !File.Exists(path) ||
                !VendorTool.IsMatch(Path.GetFileNameWithoutExtension(path)))
                return null;
            string directory = Path.GetDirectoryName(path);
            if (!AppInventory.IsDedicated(app.Location) || !SystemProtection.IsInside(directory, app.Location) ||
                !AppInventory.IsPlainDirectory(directory) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return null;

            return new ProcessStartInfo(path, command.Groups["args"].Value) { WorkingDirectory = directory, UseShellExecute = true };
        }

        internal static bool CanLaunch(InstalledApp app)
        {
            if (app.Package != null)
                return true;
            try { return DesktopLauncher(app) != null; }
            catch (Exception error) when (AppInventory.IsRegistryError(error) || error is ArgumentException) { return false; }
        }

        internal static bool Uninstall(InstalledApp app)
        {
            if (app.Package != null)
            {
                AppInventory.RemovePackage(app.Package, CancellationToken.None);
                return true;
            }

            var current = AppInventory.ReadCurrent(app);
            if (current == null || current.Command != app.Command || current.Name != app.Name ||
                current.Version != app.Version || !string.Equals(current.Location, app.Location, StringComparison.OrdinalIgnoreCase))
                throw new IOException("This app’s registration changed. Refresh the list and review it again.");
            var launcher = DesktopLauncher(current);
            if (launcher == null)
                throw new IOException("This app can be managed in Windows Settings.");
            using var process = Process.Start(launcher);
            if (process == null)
                return false;
            process.WaitForExit();
            return !AppInventory.IsRegistered(app);
        }
    }

    internal sealed class AppLeftover
    {
        public string Name { get; }
        public string Location { get; }
        public string SizeLabel => Bytes is long bytes ? DiskEntry.FormatSize(bytes) : "Unavailable";
        public string AccessibleName => $"{Name}, leftover folder, {SizeLabel}";
        internal long? Bytes { get; }
        internal InstalledApp App { get; }

        internal AppLeftover(InstalledApp app, long? bytes)
        {
            App = app;
            Name = app.Name;
            Location = app.Location;
            Bytes = bytes;
        }
    }
}
