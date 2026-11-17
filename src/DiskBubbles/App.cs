using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;

[assembly: AssemblyTitle("Disk Bubbles")]
[assembly: AssemblyProduct("Disk Bubbles")]
[assembly: AssemblyDescription("Interactive disk usage map for Windows")]
[assembly: AssemblyVersion("10.3.7.0")]
[assembly: AssemblyFileVersion("10.3.7.0")]
[assembly: AssemblyInformationalVersion("10.3.7")]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace DiskBubbles
{
    internal static class App
    {
        [STAThread]
        private static void Main(string[] args)
        {
            const uint failCriticalErrors = 1;
            SetErrorMode(GetErrorMode() | failCriticalErrors);
            var application = CreateApplication();
            var window = new MainWindow();
            if (args.Length == 1)
            {
                window.Loaded += async (sender, eventArgs) => await window.ScanAsync(args[0]);
            }

            application.Run(window);
        }

        internal static Application CreateApplication()
        {
            var application = new Application();
            Appearance.Initialize(application);
            return application;
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern uint GetErrorMode();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern uint SetErrorMode(uint mode);
    }
}
