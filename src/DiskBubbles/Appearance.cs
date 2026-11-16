using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace DiskBubbles
{
    internal static class Appearance
    {
        private static readonly string Preference = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Disk Bubbles", "appearance");
        private static ResourceDictionary _palette;
        private static ResourceDictionary _light;
        private static ResourceDictionary _dark;

        public static bool IsLight { get; private set; }

        public static void Initialize(Application app)
        {
            app.Resources = Load("Theme");
            bool light = false;
            try { light = File.ReadAllText(Preference).Trim() == "light"; }
            catch (Exception error) when (DiskScanner.IsFileSystemError(error)) { }
            Apply(light);
        }

        public static void Apply(bool light)
        {
            var palettes = Application.Current.Resources.MergedDictionaries;
            if (_palette != null)
                palettes.Remove(_palette);
            IsLight = light;
            _palette = light ? (_light ??= Load("Light")) : (_dark ??= Load("Dark"));
            palettes.Add(_palette);
        }

        public static void SavePreference()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Preference));
                File.WriteAllText(Preference, IsLight ? "light" : "dark");
            }
            catch (Exception error) when (DiskScanner.IsFileSystemError(error))
            {
            }
        }

        private static ResourceDictionary Load(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"DiskBubbles.{name}.xaml");
            return (ResourceDictionary)XamlReader.Load(stream);
        }
    }
}
