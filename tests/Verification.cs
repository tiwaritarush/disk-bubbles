using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Verification
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static void Main(string[] args)
    {
        var fixture = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(fixture);
        var files = new Dictionary<string, long>
        {
            { "Projects/Design Sources/scene.bin", 42000000 }, { "Projects/Archive/sprint.zip", 11400000 },
            { "Projects/Build Assets/installer.msi", 5000000 }, { "Videos/raw-footage.mp4", 36000000 },
            { "Downloads/offline-installer.msi", 18600000 }, { "Backups/backup.zip", 10100000 },
            { "Libraries/reference.db", 5200000 }, { "Exports/concept.png", 4900000 },
            { "Photos/poster.psd", 4500000 }, { "Cache/texture.cache", 2500000 },
            { "Desktop/demo.mp4", 2000000 }, { "Firmware/device.img", 1300000 },
            { "Fonts/typeface.otf", 600000 }, { "Report.pdf", 260000 }, { "Empty/blank.txt", 0 },
            { "System data.bin", 3400000 },
            { "Windows/System32/runtime.dll", 7400000 }, { "Windows/Logs/Setup.log", 1200000 },
        };
        for (int i = 1; i <= 18; i++)
            files.Add($"Sample note {i}.txt", 12000 + 4 * i);

        foreach (var item in files)
        {
            var path = Path.Combine(fixture, item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = File.Open(path, FileMode.OpenOrCreate))
                file.SetLength(item.Value);
        }
        File.SetAttributes(Path.Combine(fixture, "System data.bin"), FileAttributes.System);

        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var application = (Application)assembly.GetType("DiskBubbles.App")
            .GetMethod("CreateApplication", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        var windowType = assembly.GetType("DiskBubbles.MainWindow");
        var window = (Window)Activator.CreateInstance(windowType);
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -6000;
        window.Top = -6000;
        window.ShowActivated = false;

        window.Loaded += async (sender, eventArgs) =>
        {
            try
            {
                var setAppearance = windowType.GetMethod("ChangeAppearance", PrivateInstance);
                setAppearance.Invoke(window, new object[] { true });
                await Capture(window, Path.Combine(fixture, "../start-light.png"));
                setAppearance.Invoke(window, new object[] { false });
                await Capture(window, Path.Combine(fixture, "../start.png"));
                double startWidth = window.Width;
                double startHeight = window.Height;
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                await Capture(window, Path.Combine(fixture, "../start-compact.png"));
                window.Width = startWidth;
                window.Height = startHeight;

                var showPage = windowType.GetMethod("ShowPage", PrivateInstance);
                showPage.Invoke(window, new[] { WindowField(window, "_scanning") });
                ((TextBlock)WindowField(window, "_scanCounts")).Text = "36,000 files  ·  1,253 folders";
                ((TextBlock)WindowField(window, "_scanPath")).Text = @"C:\Storage sample\Projects\Build";
                var spinner = (ProgressBar)WindowField(window, "_spinner");
                spinner.IsIndeterminate = SystemParameters.ClientAreaAnimation;
                await Task.Delay(600);
                await Capture(window, Path.Combine(fixture, "../scanning.png"));
                await (Task)windowType.GetMethod("ScanAsync", PrivateInstance).Invoke(window, new object[] { fixture });
                await Task.Delay(260);
                await Capture(window, Path.Combine(fixture, "../map.png"));
                object result = WindowField(window, "_result");
                var root = result.GetType().GetField("Root").GetValue(result);
                var entries = (ListBox)WindowField(window, "_entries");
                var entryType = root.GetType();
                var isSystem = entryType.GetProperty("IsSystem");
                var protection = entryType.GetProperty("Protection", PrivateInstance);
                object windows = entries.Items.Cast<object>().First(entry => EntryName(entry) == "Windows");
                Assert(!(bool)isSystem.GetValue(windows), "A user folder named Windows was marked critical.");
                string realWindows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var forPath = assembly.GetType("DiskBubbles.SystemProtection").GetMethod("ForPath");
                var actualProtection = forPath.Invoke(null, new object[] { realWindows, File.GetAttributes(realWindows) });
                var innerProtection = forPath.Invoke(null, new object[] { Environment.SystemDirectory, FileAttributes.Directory });
                var inherited = assembly.GetType("DiskBubbles.SystemProtection").GetMethod("ForEntry")
                    .Invoke(null, new object[] { actualProtection, FileAttributes.Directory });
                Assert(actualProtection.ToString().Contains("CriticalFolder") && !innerProtection.ToString().Contains("CriticalFolder") &&
                    innerProtection.ToString().Contains("System") && inherited.ToString() == "System",
                    "The Windows root or its descendant was mislabeled.");
                protection.SetValue(windows, actualProtection);
                var map = (FrameworkElement)WindowField(window, "_map");
                var changed = Array.CreateInstance(entryType, 1);
                changed.SetValue(root, 0);
                map.GetType().GetMethod("InvalidateLayout").Invoke(map, new object[] { changed });
                windowType.GetMethod("RefreshBrowser", PrivateInstance).Invoke(window, new object[] { null, false, false });
                entries.Items.Refresh();
                var displayPath = (TextBlock)WindowField(window, "_path");
                var capacityLabel = (TextBlock)WindowField(window, "_capacity");
                var capacityMeter = (ProgressBar)WindowField(window, "_capacityBar");
                string previousPath = displayPath.Text;
                string previousCapacity = capacityLabel.Text;
                double previousMeter = capacityMeter.Value;
                displayPath.Text = @"D:\Storage sample";
                capacityLabel.Text = "320 GB used   ·   192 GB free";
                capacityMeter.Value = 0.625;
                await Capture(window, Path.Combine(fixture, "../preview.png"));
                await Capture(window, Path.Combine(fixture, "../preview-2x.png"), 192);
                setAppearance.Invoke(window, new object[] { true });
                Assert(((SolidColorBrush)window.Background).Color == Color.FromRgb(239, 231, 217) && displayPath.Text == @"D:\Storage sample",
                    "The light appearance failed to update in place.");
                await Capture(window, Path.Combine(fixture, "../preview-light.png"));
                setAppearance.Invoke(window, new object[] { false });
                displayPath.Text = previousPath;
                capacityLabel.Text = previousCapacity;
                capacityMeter.Value = previousMeter;
                long expected = files.Values.Sum();
                Assert((long)root.GetType().GetProperty("Bytes").GetValue(root) == expected, "File sizes did not match the fixture.");
                Assert((long)result.GetType().GetField("Files").GetValue(result) == files.Count, "File count differed.");
                if (Directory.Exists(Path.Combine(fixture, "Junction")))
                    Assert((long)result.GetType().GetField("Skipped").GetValue(result) >= 1, "The directory junction was followed.");
                Assert(EntryName(entries.Items[0]) == "Projects", "Entries are out of size order.");
                object system = entries.Items.Cast<object>().First(entry => EntryName(entry) == "System data.bin");
                object report = entries.Items.Cast<object>().First(entry => EntryName(entry) == "Report.pdf");
                Assert(!(bool)isSystem.GetValue(system) && protection.GetValue(system).ToString().Contains("System"),
                    "A System attribute changed the badge or lost Recycle Bin protection.");
                var windowRow = (ListBoxItem)entries.ItemContainerGenerator.ContainerFromItem(windows);
                var ordinaryRow = (ListBoxItem)entries.ItemContainerGenerator.ContainerFromItem(system);
                Assert(FindVisual<Border>(windowRow, item => item.Name == "SystemTag")?.Visibility == Visibility.Visible &&
                    FindVisual<Border>(ordinaryRow, item => item.Name == "SystemTag")?.Visibility == Visibility.Collapsed,
                    "The badge template did not recognize the critical folder boundary.");
                var recycleItem = assembly.GetType("DiskBubbles.RecycleItem");
                var prepare = recycleItem.GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic);
                var move = recycleItem.GetMethod("MoveAsync", BindingFlags.Static | BindingFlags.NonPublic);
                var readSnapshot = assembly.GetType("DiskBubbles.ShellRecycle").GetMethod("ReadSnapshot");
                AssertThrowsIOException(() => readSnapshot.Invoke(null, new[] { Path.Combine(fixture, "Report.pdf."), report }), "different path");
                AssertThrowsIOException(() => readSnapshot.Invoke(null, new[] { Path.Combine(fixture, "NUL.txt"), report }), "different path");
                AssertThrowsIOException(() => readSnapshot.Invoke(null, new[] { @"\\server\share\Report.pdf", report }), "local Windows drive");
                var batch = Array.CreateInstance(system.GetType(), 3);
                batch.SetValue(report, 0);
                batch.SetValue(system, 1);
                batch.SetValue(windows, 2);
                var prepared = (IList)prepare.Invoke(null, new object[] { fixture, batch, CancellationToken.None });
                Assert((bool)recycleItem.GetProperty("Ready").GetValue(prepared[0]), "Normal file could not be reviewed.");
                Assert(!(bool)recycleItem.GetProperty("Ready").GetValue(prepared[1]), "System file was eligible to recycle.");
                Assert(!(bool)recycleItem.GetProperty("Ready").GetValue(prepared[2]), "The critical folder was eligible to recycle.");
                var reviewType = assembly.GetType("DiskBubbles.RecycleReview");
                var review = (Window)Activator.CreateInstance(reviewType, window, prepared, root, root, false);
                review.WindowStartupLocation = WindowStartupLocation.Manual;
                review.Left = -6000;
                review.Top = -6000;
                review.ShowActivated = false;
                review.Show();
                await Task.Delay(260);
                await Capture(review, Path.Combine(fixture, "../recycle-review.png"));
                review.Width = review.MinWidth;
                review.Height = review.MinHeight;
                await Capture(review, Path.Combine(fixture, "../recycle-review-compact.png"));
                await Capture(review, Path.Combine(fixture, "../recycle-review-compact-2x.png"), 192);
                review.Close();
                setAppearance.Invoke(window, new object[] { true });
                var lightReview = (Window)Activator.CreateInstance(reviewType, window, prepared, root, root, false);
                lightReview.WindowStartupLocation = WindowStartupLocation.Manual;
                lightReview.Left = -6000;
                lightReview.Top = -6000;
                lightReview.ShowActivated = false;
                lightReview.Show();
                await Task.Delay(260);
                await Capture(lightReview, Path.Combine(fixture, "../recycle-review-light.png"));
                lightReview.Close();
                setAppearance.Invoke(window, new object[] { false });

                entries.SelectedItem = report;
                entries.ScrollIntoView(report);
                var reviewButton = (Button)WindowField(window, "_reviewRecycle");
                Assert(reviewButton.IsVisible, "Selecting a file did not expose the review action.");
                setAppearance.Invoke(window, new object[] { true });
                Assert(ReferenceEquals(entries.SelectedItem, report), "Appearance changed the current selection.");
                await Capture(window, Path.Combine(fixture, "../selection-light.png"));
                setAppearance.Invoke(window, new object[] { false });
                await Capture(window, Path.Combine(fixture, "../selection.png"));
                await CancelReview(window, reviewButton);
                Assert(!(bool)WindowField(window, "_recycleBusy") && File.Exists(Path.Combine(fixture, "Report.pdf")),
                    "Cancel did not release the browser or preserve the selected file.");
                var confirmation = (Window)Activator.CreateInstance(reviewType, window, prepared, root, root, false);
                confirmation.WindowStartupLocation = WindowStartupLocation.Manual;
                confirmation.Left = -6000;
                confirmation.Top = -6000;
                confirmation.ShowInTaskbar = false;
                confirmation.Loaded += (source, args) => confirmation.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var confirm = FindVisual<Button>(confirmation, button => Equals(button.Content, "Move to Recycle Bin"));
                    if (confirm?.IsEnabled == true)
                        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else
                        confirmation.Close();
                }));
                Assert(confirmation.ShowDialog() == true, "The animated confirmation lost the approved result.");
                entries.UnselectAll();

                Point left = entries.TranslatePoint(new Point(), window);
                Point right = map.TranslatePoint(new Point(), window);
                Assert(left.X + entries.ActualWidth <= right.X, "The file list should sit left of the map.");
                Assert((long)result.GetType().GetField("VolumeBytes").GetValue(result) > 0, "Volume capacity was unavailable.");
                var packed = (IList)map.GetType().GetField("_bubbles", PrivateInstance).GetValue(map);
                Assert(packed.Count > 5 && packed.Count <= 11, "Map uses the wrong number of bubbles.");
                var drawingField = map.GetType().GetField("_drawing", PrivateInstance);
                var originalArtwork = ((DrawingGroup)drawingField.GetValue(map)).Children[0];
                var circles = packed.Cast<object>().Select(circle => new
                {
                    Center = (Point)circle.GetType().GetField("Center").GetValue(circle),
                    Radius = (double)circle.GetType().GetField("Radius").GetValue(circle),
                }).ToList();
                var trackPointer = map.GetType().GetMethod("TrackPointer", PrivateInstance);
                var firstCircle = circles[0];
                object firstEntry = packed[0].GetType().GetField("Entry").GetValue(packed[0]);
                entries.ScrollIntoView(firstEntry);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var pointer = firstCircle.Center + new Vector(-firstCircle.Radius * 0.42, -firstCircle.Radius * 0.38);
                trackPointer.Invoke(map, new object[] { pointer });
                var highlighted = (DrawingGroup)map.GetType().GetField("_highlight", PrivateInstance).GetValue(map);
                var pointerLight = (DrawingGroup)map.GetType().GetField("_pointerLight", PrivateInstance).GetValue(map);
                Assert(highlighted.Children.Count > 0, "Bubble hover did not draw its highlight.");
                Assert((string)((ListBoxItem)entries.ItemContainerGenerator.ContainerFromItem(firstEntry)).Tag == "Linked",
                    "The map hover did not identify the matching row.");
                var caption = (TextBlock)WindowField(window, "_mapNote");
                Assert(caption.Text.StartsWith("Projects", StringComparison.Ordinal) && caption.Text.Contains("of this folder"),
                    "Hover did not show the item's size and share.");
                var shifted = pointer + new Vector(10, 5);
                trackPointer.Invoke(map, new object[] { shifted });
                if (SystemParameters.ClientAreaAnimation)
                    Assert(pointerLight.Children.Count > 0 && (Point)map.GetType().GetField("_hoverPoint", PrivateInstance).GetValue(map) == shifted,
                        "Bubble reflection did not track the pointer.");
                await Capture(window, Path.Combine(fixture, "../hover.png"));
                var small = circles[circles.Count - 1];
                var smallEntry = packed[packed.Count - 1].GetType().GetField("Entry").GetValue(packed[packed.Count - 1]);
                trackPointer.Invoke(map, new object[] { small.Center });
                Assert(map.ToolTip is string tooltip && tooltip.StartsWith(EntryName(smallEntry), StringComparison.Ordinal),
                    "A small bubble tooltip did not identify its entry.");
                trackPointer.Invoke(map, new object[] { new Point(-1, -1) });
                Assert(highlighted.Children.Count == 0 && pointerLight.Children.Count == 0, "A stale reflection remained after leaving the bubble.");

                for (int i = 0; i < circles.Count; i++)
                {
                    var circle = circles[i];
                    Assert(circle.Center.X >= circle.Radius && circle.Center.X + circle.Radius <= map.ActualWidth, "Bubble exceeds map width.");
                    Assert(circle.Center.Y >= circle.Radius && circle.Center.Y + circle.Radius <= map.ActualHeight, "Bubble exceeds map height.");
                    for (int j = 0; j < i; j++)
                        Assert((circle.Center - circles[j].Center).Length >= circle.Radius + circles[j].Radius + 8, "Bubbles have insufficient separation.");
                }

                var action = (Delegate)map.GetType().GetField("Activated", PrivateInstance).GetValue(map);
                var other = map.GetType().GetProperty("Other").GetValue(map);
                Assert(other != null, "Other items group is missing.");
                int otherCount = entries.Items.Count - (int)map.GetType().GetProperty("ListedCount").GetValue(map);
                action.DynamicInvoke(other);
                Assert(entries.Items.Count == otherCount && entries.Items.Cast<object>().Contains(report), "Other items filter shows the wrong entries.");
                action.DynamicInvoke(other);
                Assert(EntryName(entries.Items[0]) == "Projects", "Other items toggle did not restore the list.");
                var press = map.GetType().GetMethod("Press", PrivateInstance);
                var release = map.GetType().GetMethod("Release", PrivateInstance);
                var path = (TextBlock)WindowField(window, "_path");
                press.Invoke(map, new object[] { firstCircle.Center, 1 });
                Assert(pointerLight.Children.Count == 0, "Press feedback did not replace the hover reflection.");
                release.Invoke(map, new object[] { circles[1].Center });
                Assert(path.Text == fixture, "Releasing over a different bubble opened a folder.");
                press.Invoke(map, new object[] { firstCircle.Center, 1 });
                release.Invoke(map, new object[] { firstCircle.Center });
                var transitioning = map.GetType().GetField("_transitioning", PrivateInstance);
                if (SystemParameters.ClientAreaAnimation)
                    Assert((bool)transitioning.GetValue(map), "Folder zoom did not start.");
                Assert(path.Text == Path.Combine(fixture, "Projects"), "Bubble activation did not navigate.");
                Assert(EntryName(entries.Items[0]) == "Design Sources", "Child entries did not populate.");
                await Task.Delay(365);
                Assert(!(bool)transitioning.GetValue(map), "Folder zoom did not settle.");
                await Capture(window, Path.Combine(fixture, "../folder.png"));

                var up = (Button)WindowField(window, "_up");
                press.Invoke(map, new object[] { new Point(1, 1), 1 });
                release.Invoke(map, new object[] { new Point(1, 1) });
                Assert(path.Text == Path.Combine(fixture, "Projects"), "A single background click navigated up.");
                press.Invoke(map, new object[] { new Point(1, 1), 2 });
                Assert(path.Text == fixture, "Double-clicking the background did not go up one folder.");
                Assert(EntryName(entries.Items[0]) == "Projects", "Root entries did not repopulate.");
                Assert(ReferenceEquals(packed, map.GetType().GetField("_bubbles", PrivateInstance).GetValue(map)), "Returning did not reuse packed geometry.");
                Assert(ReferenceEquals(originalArtwork, ((DrawingGroup)drawingField.GetValue(map)).Children[0]), "Returning rebuilt glass artwork.");
                var back = (Button)WindowField(window, "_back");
                var forward = (Button)WindowField(window, "_forward");
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(path.Text == Path.Combine(fixture, "Projects"), "Back failed to restore the prior location.");
                entries.SelectedIndex = 0;
                windowType.GetMethod("OpenSelected", PrivateInstance).Invoke(window, new object[] { false });
                Assert(path.Text == Path.Combine(fixture, "Projects", "Design Sources") && !(bool)transitioning.GetValue(map),
                    "Keyboard navigation waited for an in-flight zoom.");
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                forward.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(path.Text == fixture, "Forward failed to restore the next location.");

                var activate = windowType.GetMethod("Activate", PrivateInstance);
                object projects = entries.Items[0];
                action.DynamicInvoke(other);
                var scrollMethod = windowType.GetMethod("ListingScrollViewer", BindingFlags.Static | BindingFlags.NonPublic);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var listScroll = (ScrollViewer)scrollMethod.Invoke(null, new object[] { entries });
                listScroll.ScrollToVerticalOffset(4);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                double offset = listScroll.VerticalOffset;
                Assert(offset > 0, "The filtered list did not scroll in the fixture.");
                activate.Invoke(window, new[] { projects });
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert(entries.Items.Count == otherCount && listScroll.VerticalOffset == offset,
                    "Back did not restore the folder's filter and scroll position.");
                action.DynamicInvoke(other);
                activate.Invoke(window, new[] { projects });
                object design = entries.Items[0];
                activate.Invoke(window, new[] { design });
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(path.Text == Path.Combine(fixture, "Projects"), "Could not return to parent for recycle test.");
                var picked = Array.CreateInstance(design.GetType(), 1);
                picked.SetValue(design, 0);
                var beforeReplacement = (IList)prepare.Invoke(null, new object[] { path.Text, picked, CancellationToken.None });
                Assert((bool)recycleItem.GetProperty("Ready").GetValue(beforeReplacement[0]), "The original folder failed verification.");
                string scene = Path.Combine(path.Text, "Design Sources", "scene.bin");
                string replacement = Path.Combine(fixture, "..", "replacement.bin");
                using (var candidate = File.Create(replacement))
                    candidate.SetLength(new FileInfo(scene).Length);
                DateTime fileTime = File.GetLastWriteTimeUtc(scene);
                string sceneFolder = Path.GetDirectoryName(scene);
                DateTime parentTime = Directory.GetLastWriteTimeUtc(sceneFolder);
                File.SetLastWriteTimeUtc(replacement, fileTime);
                File.Replace(replacement, scene, null);
                Directory.SetLastWriteTimeUtc(sceneFolder, parentTime);
                var changedFolder = (Task)move.Invoke(null, new object[] { beforeReplacement });
                await changedFolder;
                Assert(((IList)changedFolder.GetType().GetProperty("Result").GetValue(changedFolder)).Count == 0 && File.Exists(scene),
                    "A folder with an identically sized replacement crossed the recycle boundary.");
                Assert((bool)recycleItem.GetField("NeedsRescan", PrivateInstance).GetValue(beforeReplacement[0]),
                    "A changed folder was not marked for a rescan.");

                var simulatedMove = (IList)prepare.Invoke(null, new object[] { path.Text, picked, CancellationToken.None });
                recycleItem.GetField("Files", PrivateInstance).SetValue(simulatedMove[0], 1L);
                recycleItem.GetField("Folders", PrivateInstance).SetValue(simulatedMove[0], 1L);
                windowType.GetMethod("ApplyRecycled", PrivateInstance).Invoke(window, new object[] { simulatedMove });
                Assert(entries.Items.Cast<object>().All(item => EntryName(item) != "Design Sources"), "Map still contains the recycled folder.");
                Assert((long)root.GetType().GetProperty("Bytes").GetValue(root) == expected - 42000000,
                    "Affected ancestor size was not updated.");
                Assert(!forward.IsEnabled, "Navigation history still points to the moved folder.");
                up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                string emptyPath = Path.Combine(fixture, "Empty");
                var scanMethod = windowType.GetMethod("ScanAsync", PrivateInstance);
                var cancelled = (Task)scanMethod.Invoke(window, new object[] { emptyPath });
                ((CancellationTokenSource)WindowField(window, "_scan")).Cancel();
                await cancelled;
                Assert(path.Text == fixture, "Cancellation discarded the last completed scan.");

                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                await Capture(window, Path.Combine(fixture, "../compact.png"));
                setAppearance.Invoke(window, new object[] { true });
                await Capture(window, Path.Combine(fixture, "../compact-light.png"));
                await (Task)scanMethod.Invoke(window, new object[] { emptyPath });
                Assert(path.Text == emptyPath && entries.Items.Count == 1, "Zero-size folder scan was incomplete.");
                Assert(((TextBlock)WindowField(window, "_mapEmpty")).Visibility == Visibility.Visible, "Empty map message is missing.");
                await Capture(window, Path.Combine(fixture, "../empty.png"));

                string reportPath = Path.Combine(fixture, "Report.pdf");
                File.SetLastWriteTimeUtc(reportPath, File.GetLastWriteTimeUtc(reportPath).AddSeconds(5));
                var stale = (Task)move.Invoke(null, new object[] { prepared });
                await stale;
                Assert(((IList)stale.GetType().GetProperty("Result").GetValue(stale)).Count == 0 && File.Exists(reportPath),
                    "An entry changed after review but was still moved.");
                Assert(File.Exists(Path.Combine(fixture, "System data.bin")), "A system item was moved.");
                var visuals = assembly.GetType("DiskBubbles.UiVisuals");
                var notice = (Window)visuals.GetMethod("Notice").Invoke(null, new object[] { window, "Can’t open this location",
                    "Windows couldn’t read this location. Choose another folder or drive." });
                notice.WindowStartupLocation = WindowStartupLocation.Manual;
                notice.Left = -6000;
                notice.Top = -6000;
                notice.ShowActivated = false;
                notice.Show();
                await Task.Delay(260);
                await Capture(notice, Path.Combine(fixture, "../notice.png"));
                notice.Close();
                Console.WriteLine($"Verified {files.Count} files and {expected} bytes, both themes, map interaction and navigation, cancellation, Recycle Bin review, and rejection of protected, ambiguous, remote and changed targets.");
                application.Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                application.Shutdown(1);
            }
        };

        Environment.ExitCode = application.Run(window);
    }

    private static object WindowField(Window window, string name) =>
        window.GetType().GetField(name, PrivateInstance).GetValue(window);

    private static string EntryName(object item) => (string)item.GetType().GetProperty("Name").GetValue(item);

    private static void AssertThrowsIOException(Action action, string expectedMessage)
    {
        try { action(); }
        catch (TargetInvocationException error) when (error.InnerException is IOException failure && failure.Message.Contains(expectedMessage))
        {
            return;
        }
        throw new InvalidOperationException("The unsafe path did not fail before opening a file.");
    }

    private static T FindVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        if (root is T element && match(element))
            return element;
        if (root != null)
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                T child = FindVisual(VisualTreeHelper.GetChild(root, i), match);
                if (child != null)
                    return child;
            }
        return null;
    }

    private static async Task CancelReview(Window owner, Button reviewButton)
    {
        var reviewed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (sender, args) =>
        {
            var dialog = Application.Current.Windows.Cast<Window>().FirstOrDefault(candidate => candidate.IsVisible &&
                candidate.Owner == owner && candidate.Title == "Review Recycle Bin move");
            if (dialog == null && DateTime.UtcNow < deadline)
                return;

            timer.Stop();
            if (dialog == null)
                reviewed.SetResult(false);
            else
            {
                dialog.Closed += (closedSender, closedArgs) => reviewed.SetResult(true);
                dialog.Close();
            }
        };
        timer.Start();
        reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(await reviewed.Task, "The review action did not open its confirmation dialog.");
        await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static async Task Capture(Window window, string path, double dpi = 96)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var view = (FrameworkElement)window.Content;
        view.UpdateLayout();
        double scale = dpi / 96;
        var bitmap = new RenderTargetBitmap((int)Math.Round(view.ActualWidth * scale), (int)Math.Round(view.ActualHeight * scale),
            dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path))
            encoder.Save(file);
    }

    private static void Assert(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
