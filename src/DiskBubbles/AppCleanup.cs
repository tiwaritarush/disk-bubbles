using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace DiskBubbles
{
    internal static class AppCleanup
    {
        private static readonly string LocalData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string RoamingData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly Regex PackageFamily = new Regex(@"^[A-Za-z0-9.-]{3,50}_[A-Za-z0-9]{13}$", RegexOptions.CultureInvariant);
        private static readonly Regex ServiceFile = new Regex(
            @"^(?:""(?<file>[^""]+)""|(?<file>.+?\.(?:exe|dll|sys))(?=\s|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> DriverFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".sys", ".inf", ".cat" };
        private static readonly HashSet<string> SharedDataRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Microsoft Corporation", "Windows", "Packages", "Programs", "Temp", "VirtualStore",
        };

        internal static IEnumerable<(string Path, string Kind)> Candidates(InstalledApp app, bool deep)
        {
            if (app.Locked)
                yield break;

            if (app.Package != null)
            {
                if (deep && !string.IsNullOrEmpty(LocalData) && PackageFamily.IsMatch(app.PackageFamily ?? ""))
                {
                    string profile = Path.Combine(LocalData, "Packages", app.PackageFamily);
                    foreach (string folder in new[] { "LocalCache", "TempState", "LocalState", "RoamingState", "Settings" })
                        yield return (Path.Combine(profile, folder), "Store app data");
                }
                yield break;
            }

            if (app.RegisteredLocation && AppInventory.IsDedicated(app.Location))
                yield return (app.Location, "Install leftover");

            if (!deep || !IsPathSegment(app.Publisher) || !IsPathSegment(app.Name) || SharedDataRoots.Contains(app.Publisher) ||
                app.Publisher.Equals(app.Name, StringComparison.OrdinalIgnoreCase))
                yield break;

            bool installNameMatches = app.RegisteredLocation && AppInventory.IsDedicated(app.Location) &&
                app.Name.Length >= 5 && !SharedDataRoots.Contains(app.Name) &&
                app.Name.Equals(Path.GetFileName(app.Location), StringComparison.OrdinalIgnoreCase);
            foreach (string root in new[] { LocalData, RoamingData }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                yield return (Path.Combine(root, app.Publisher, app.Name), "Matching app data");
                if (installNameMatches)
                    yield return (Path.Combine(root, app.Name), "Matching app data");
            }
        }

        internal static List<AppLeftover> Find(InstalledApp app, bool deep)
        {
            var paths = Candidates(app, deep).Where(item => IsUserFolder(item.Path)).ToArray();
            var found = new List<AppLeftover>();
            if (paths.Length == 0 || IsRegistered(app))
                return found;

            foreach (var item in paths)
            {
                if (HasOtherOwner(item.Path, app))
                    continue;
                var scan = DiskScanner.Scan(item.Path, CancellationToken.None);
                if (scan.Skipped == 0 && CanRecycleTree(scan.Root))
                    found.Add(new AppLeftover(app, item.Path, item.Kind, scan.Root.Bytes));
            }
            return found;
        }

        internal static bool IsRegistered(InstalledApp app) => app.Package != null
            ? AppInventory.IsPackageFamilyPresent(app.PackageFamily) : AppInventory.IsRegistered(app);

        internal static bool IsSafeLocation(string path, InstalledApp app) => IsUserFolder(path) && !HasOtherOwner(path, app);

        internal static List<RecycleItem> Prepare(IEnumerable<AppLeftover> leftovers)
        {
            var result = new List<RecycleItem>();
            foreach (var group in leftovers.GroupBy(item => item.App.Id, StringComparer.OrdinalIgnoreCase))
            {
                if (IsRegistered(group.First().App))
                    continue;
                foreach (var item in group)
                {
                    if (!IsSafeLocation(item.Location, item.App))
                        continue;
                    var scan = DiskScanner.Scan(item.Location, CancellationToken.None);
                    if (scan.Skipped != 0 || !CanRecycleTree(scan.Root))
                        continue;
                    result.AddRange(RecycleItem.Prepare(Path.GetDirectoryName(item.Location), new[] { scan.Root }, CancellationToken.None));
                }
            }
            return result;
        }

        internal static bool CanRecycleTree(DiskEntry root)
        {
            var pending = new Stack<DiskEntry>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var entry = pending.Pop();
                if (entry.Protection != EntryProtection.None || (!entry.IsFolder && DriverFiles.Contains(Path.GetExtension(entry.Name))))
                    return false;
                if (entry.IsFolder)
                    foreach (var child in entry.Children)
                        pending.Push(child);
            }
            return true;
        }

        private static bool IsPathSegment(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 100 &&
            NativeDirectoryReader.IsEntryName(text) && text.IndexOf('%') < 0 &&
            !text.EndsWith(".", StringComparison.Ordinal) && !text.EndsWith(" ", StringComparison.Ordinal);

        private static bool IsUserFolder(string path) => string.Equals(AppInventory.Normalize(path), path, StringComparison.OrdinalIgnoreCase) &&
            SystemProtection.ForPath(path) == EntryProtection.None && AppInventory.IsPlainDirectory(path);

        private static bool HasOtherOwner(string path, InstalledApp app) => AppInventory.HasOtherRegistration(path, app) || UsedByService(path);

        private static bool UsedByService(string folder)
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services == null)
                throw new IOException("Windows did not provide the services list.");
            foreach (string name in services.GetSubKeyNames())
            {
                using var service = services.OpenSubKey(name);
                using var parameters = service?.OpenSubKey("Parameters");
                if (PointsInside(service?.GetValue("ImagePath") as string, folder) ||
                    PointsInside(parameters?.GetValue("ServiceDll") as string, folder))
                    return true;
            }
            return false;
        }

        private static bool PointsInside(string value, string folder)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            var match = ServiceFile.Match(Environment.ExpandEnvironmentVariables(value).Trim());
            if (!match.Success)
                return false;
            string path = match.Groups["file"].Value;
            if (path.StartsWith(@"\??\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal))
                path = path.Substring(4);
            if (path.IndexOf('~') >= 0)
            {
                var buffer = new StringBuilder(32768);
                uint length = GetLongPathName(path, buffer, (uint)buffer.Capacity);
                if (length > 0 && length < buffer.Capacity)
                    path = buffer.ToString();
            }
            return SystemProtection.IsInside(path, folder);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);
    }
}
