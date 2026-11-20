using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace DiskBubbles
{
    internal static class AppIcons
    {
        internal static ImageSource Load(InstalledApp app)
        {
            try
            {
                return app.Package == null ? LoadDesktop(app.DisplayIcon) : LoadPackage(app.Location);
            }
            catch (Exception error) when (AppInventory.IsRegistryError(error) || error is ArgumentException ||
                error is NotSupportedException || error is InvalidOperationException || error is XmlException || error is COMException)
            {
                return null;
            }
        }

        private static BitmapSource LoadDesktop(string registration)
        {
            if (string.IsNullOrWhiteSpace(registration))
                return null;
            int index = 0;
            int comma = registration.LastIndexOf(',');
            if (comma >= 0 && int.TryParse(registration.Substring(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int resource))
            {
                registration = registration.Substring(0, comma);
                index = resource;
            }
            string path = AppInventory.NormalizeIconFile(registration);
            if (path == null)
                return null;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png":
                    return LoadPng(path);
                case ".exe":
                case ".dll":
                case ".ico":
                    if (index < -65535 || index > 65535 || !IsPlainFile(path))
                        return null;
                    break;
                default:
                    return null;
            }

            IntPtr icon = IntPtr.Zero;
            try
            {
                if (ExtractIconExW(path, index, out icon, IntPtr.Zero, 1) != 1 || icon == IntPtr.Zero)
                    return null;
                var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally
            {
                if (icon != IntPtr.Zero)
                    DestroyIcon(icon);
            }
        }

        private static BitmapSource LoadPackage(string folder)
        {
            if (folder == null)
                return null;
            string manifest = Path.Combine(folder, "AppxManifest.xml");
            if (!IsPlainFile(manifest))
                return null;
            using var file = File.OpenRead(manifest);
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 1024 * 1024 });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "VisualElements")
                    continue;
                var icon = FindLogo(folder, reader.GetAttribute("Square44x44Logo")) ?? FindLogo(folder, reader.GetAttribute("Square150x150Logo"));
                if (icon != null)
                    return icon;
            }
            return null;
        }

        private static BitmapSource FindLogo(string folder, string asset)
        {
            if (string.IsNullOrEmpty(asset))
                return null;
            string path = AppInventory.NormalizeIconFile(Path.Combine(folder, asset.Replace('/', '\\')));
            if (path == null || !SystemProtection.IsInside(path, folder) ||
                !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                return null;
            string stem = Path.ChangeExtension(path, null);
            foreach (string suffix in new[] { ".targetsize-48_altform-unplated.png", ".targetsize-48.png", ".scale-150.png", ".scale-100.png", ".scale-200.png", ".png" })
            {
                var logo = LoadPng(stem + suffix);
                if (logo != null)
                    return logo;
            }
            return null;
        }

        private static BitmapSource LoadPng(string path)
        {
            if (!IsPlainFile(path))
                return null;
            using var file = File.OpenRead(path);
            if (file.Length < 24 || file.Length > 2 * 1024 * 1024)
                return null;
            using var header = new BinaryReader(file);
            if (header.ReadUInt64() != 0x0A1A0A0D474E5089 || header.ReadUInt64() != 0x524448490D000000)
                return null;
            int ReadDimension() => header.ReadByte() << 24 | header.ReadByte() << 16 | header.ReadByte() << 8 | header.ReadByte();
            if (ReadDimension() is < 1 or > 1024 || ReadDimension() is < 1 or > 1024)
                return null;
            file.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = file;
            image.DecodePixelWidth = 48;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private static bool IsPlainFile(string path) => File.Exists(path) &&
            AppInventory.IsPlainDirectory(Path.GetDirectoryName(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint ExtractIconExW(string file, int index, out IntPtr large, IntPtr small, uint count);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}
