using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

internal static class ScanBenchmark
{
    private static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var scan = assembly.GetType("DiskBubbles.DiskScanner").GetMethod("Scan");
        int workers = int.Parse(args[2], CultureInfo.InvariantCulture);
        var timer = Stopwatch.StartNew();
        var result = scan.Invoke(null, new object[] { Path.GetFullPath(args[1]), CancellationToken.None, null, workers });
        var type = result.GetType();
        var root = type.GetField("Root").GetValue(result);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F3},{2},{3},{4},{5}",
            workers, timer.Elapsed.TotalSeconds, type.GetField("Files").GetValue(result),
            type.GetField("Folders").GetValue(result), type.GetField("Skipped").GetValue(result),
            root.GetType().GetProperty("Bytes").GetValue(root)));
    }
}
