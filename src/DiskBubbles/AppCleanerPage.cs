using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DiskBubbles
{
    internal sealed class AppCleanerPage : Grid
    {
        private readonly Window _owner;
        private readonly Action<string> _openExplorer;
        private readonly Action _storageChanged;
        private readonly Action<bool> _writeStateChanged;
        private readonly Grid _results = new Grid();
        private readonly StackPanel _scanView = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 410 };
        private readonly TextBlock _scanHeading = Label("", 32, "Ink", true);
        private readonly TextBlock _scanDetail = Label("", 15, "Muted");
        private readonly ProgressBar _scanMeter = new ProgressBar { Width = 280, Height = 5, Margin = new Thickness(0, 20, 0, 0) };
        private readonly Button _scanAction = new Button { Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        private readonly ListBox _list = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        private readonly TextBox _search = new TextBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Button _refresh = new Button { Content = "Scan apps" };
        private readonly Button _leftoverMode = new Button { Content = "Leftovers" };
        private readonly Button _review = new Button();
        private readonly Button _openFolder = new Button { Content = "Open folder…" };
        private readonly TextBlock _status = Label("", 13, "Muted");
        private readonly TextBlock _summary = Label("", 13, "Ink", true);
        private readonly ContentControl _itemIcon = new ContentControl { Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12), Focusable = false, IsHitTestVisible = false };
        private readonly TextBlock _itemName = Label("", 23, "Ink", true);
        private readonly TextBlock _itemInfo = Label("", 14, "Muted");
        private readonly TextBlock _itemSize = Label("", 25, "Ink", true);
        private readonly TextBlock _itemPath = Label("", 12, "Muted");
        private readonly List<InstalledApp> _receipts = new List<InstalledApp>();
        private readonly List<AppLeftover> _leftovers = new List<AppLeftover>();
        private List<InstalledApp> _apps = new List<InstalledApp>();
        private CancellationTokenSource _scan;
        private bool _loaded;
        private bool _writing;
        private bool _showLeftovers;
        private bool _closed;
        internal bool MovingFiles { get; private set; }

        public AppCleanerPage(Window owner, Action<string> openExplorer, Action storageChanged, Action<bool> writeStateChanged)
        {
            _owner = owner;
            _openExplorer = openExplorer;
            _storageChanged = storageChanged;
            _writeStateChanged = writeStateChanged;
            _owner.Closed += (sender, args) => { _closed = true; _scan?.Cancel(); };
            Build();
            _refresh.Click += async (sender, args) => await ScanAsync();
            _scanAction.Click += async (sender, args) =>
            {
                if (_scan == null) await ScanAsync();
                else
                {
                    _scanAction.IsEnabled = false;
                    _scanDetail.Text = "Canceling scan…";
                    _scan.Cancel();
                }
            };
            _leftoverMode.Click += (sender, args) => { _showLeftovers = !_showLeftovers; _search.Clear(); UpdateList(); };
            _search.TextChanged += (sender, args) => UpdateList();
            _list.SelectionChanged += (sender, args) => UpdateActions();
            _list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, args) =>
            {
                if (args.OriginalSource is CheckBox box && box.DataContext is InstalledApp app)
                {
                    app.Marked = box.IsChecked == true;
                    UpdateActions();
                }
            }));
            _openFolder.Click += (sender, args) =>
            {
                string location = SelectedLocation();
                if (location != null && Directory.Exists(location)) _openExplorer("\"" + location + "\"");
            };
            _review.Click += async (sender, args) =>
            {
                if (_showLeftovers) await RecycleLeftoverAsync();
                else await ReviewUninstallAsync();
            };
            UpdateList();
        }

        internal Task OpenAsync() => _loaded ? Task.CompletedTask : ScanAsync();

        private void Build()
        {
            Children.Add(_results);
            _results.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _results.RowDefinitions.Add(new RowDefinition());
            _results.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new DockPanel { Margin = new Thickness(3, 0, 3, 15) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _leftoverMode.Margin = new Thickness(0, 0, 7, 0);
            actions.Children.Add(_leftoverMode);
            actions.Children.Add(_refresh);
            DockPanel.SetDock(actions, Dock.Right);
            top.Children.Add(actions);
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(0, 0, 12, 0);
            top.Children.Add(_status);
            _results.Children.Add(top);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(242) });
            var table = new DockPanel { Margin = new Thickness(15, 14, 12, 10) };
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 8, 9) };
            var filter = Label("Filter by name", 13, "Muted");
            filter.VerticalAlignment = VerticalAlignment.Center;
            filter.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(filter, Dock.Left);
            toolbar.Children.Add(filter);
            _search.Height = 34;
            _search.Padding = new Thickness(8, 0, 8, 0);
            _search.Style = (Style)FindResource("FilterInput");
            _search.SetResourceReference(Control.BackgroundProperty, "Raised");
            _search.SetResourceReference(Control.ForegroundProperty, "Ink");
            _search.SetResourceReference(Control.BorderBrushProperty, "Line");
            _search.SetResourceReference(TextBox.CaretBrushProperty, "Ink");
            AutomationProperties.SetName(_search, "Filter by app or publisher name");
            toolbar.Children.Add(_search);
            DockPanel.SetDock(toolbar, Dock.Top);
            table.Children.Add(toolbar);

            _list.ItemContainerStyle = (Style)FindResource("EntryRow");
            _list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            _list.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            AutomationProperties.SetName(_list, "App Cleaner results");
            table.Children.Add(_list);
            columns.Children.Add(UiVisuals.FrostedPane(table));

            var details = new StackPanel { Margin = new Thickness(17, 19, 16, 16) };
            _itemIcon.ContentTemplate = (DataTemplate)FindResource("AppGlyph");
            details.Children.Add(_itemIcon);
            details.Children.Add(_itemName);
            _itemName.TextWrapping = TextWrapping.Wrap;
            _itemInfo.TextWrapping = TextWrapping.Wrap;
            _itemInfo.Margin = new Thickness(0, 7, 0, 0);
            details.Children.Add(_itemInfo);
            _itemSize.Margin = new Thickness(0, 15, 0, 0);
            details.Children.Add(_itemSize);
            _itemPath.TextWrapping = TextWrapping.Wrap;
            _itemPath.Margin = new Thickness(0, 12, 0, 0);
            details.Children.Add(_itemPath);
            _openFolder.Margin = new Thickness(0, 14, 0, 0);
            _openFolder.HorizontalAlignment = HorizontalAlignment.Left;
            details.Children.Add(_openFolder);
            var side = UiVisuals.FrostedPane(new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                "GlassClear");
            SetColumn(side, 2);
            columns.Children.Add(side);
            SetRow(columns, 1);
            _results.Children.Add(columns);

            var footer = new DockPanel { Margin = new Thickness(3, 12, 3, 0) };
            UiVisuals.Emphasize(_review);
            DockPanel.SetDock(_review, Dock.Right);
            footer.Children.Add(_review);
            _summary.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(_summary);
            SetRow(footer, 2);
            _results.Children.Add(footer);

            var icon = UiVisuals.Icon(UiVisuals.Apps, "Muted", 34);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            _scanView.Children.Add(UiVisuals.Surface(new Border { Child = icon, Width = 68, Height = 68,
                CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 16), HorizontalAlignment = HorizontalAlignment.Center }, "Raised", "GlassEdge"));
            _scanHeading.TextAlignment = TextAlignment.Center;
            _scanDetail.TextAlignment = TextAlignment.Center;
            _scanDetail.TextWrapping = TextWrapping.Wrap;
            _scanDetail.Margin = new Thickness(0, 9, 0, 0);
            _scanView.Children.Add(_scanHeading);
            _scanView.Children.Add(_scanDetail);
            _scanMeter.Template = (ControlTemplate)FindResource("ScanMeter");
            _scanMeter.SetResourceReference(Control.ForegroundProperty, "Accent");
            _scanMeter.SetResourceReference(Control.BackgroundProperty, "Line");
            AutomationProperties.SetName(_scanMeter, "App scan in progress");
            _scanView.Children.Add(_scanMeter);
            _scanView.Children.Add(_scanAction);
            Children.Add(_scanView);
            UpdateScanView();
        }

        private void UpdateScanView()
        {
            bool scanning = _scan != null;
            bool showResults = _loaded && !scanning;
            _results.Visibility = showResults ? Visibility.Visible : Visibility.Collapsed;
            _scanView.Visibility = showResults ? Visibility.Collapsed : Visibility.Visible;
            _scanHeading.Text = scanning ? "Scanning installed apps…" : "Scan installed apps";
            _scanDetail.Text = scanning ? "Reading the Windows app list…" : _status.Text.Length == 0 ? "Find apps and see how much space they use." : _status.Text;
            _scanMeter.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
            _scanMeter.IsIndeterminate = scanning && SystemParameters.ClientAreaAnimation;
            _scanAction.Content = scanning ? "Cancel scan" : "Scan apps";
            _scanAction.IsEnabled = true;
        }

        private async Task ScanAsync()
        {
            if (_scan != null || _writing)
                return;
            using var cancellation = new CancellationTokenSource();
            _scan = cancellation;
            bool moveFocus = IsKeyboardFocusWithin;
            UpdateScanView();
            if (moveFocus) _scanAction.Focus();
            var progress = new Progress<string>(text =>
            {
                if (ReferenceEquals(_scan, cancellation) && !cancellation.IsCancellationRequested && !_closed)
                    _scanDetail.Text = text;
            });
            try
            {
                var result = await Task.Run(() => AppInventory.Scan(cancellation.Token, progress), cancellation.Token);
                if (_closed) return;
                cancellation.Token.ThrowIfCancellationRequested();
                var leftovers = await Task.Run(() => FindLeftovers(result.Apps, cancellation.Token), cancellation.Token);
                if (_closed) return;
                cancellation.Token.ThrowIfCancellationRequested();
                var marked = new HashSet<string>(_apps.Where(app => app.Marked).Select(app => app.Id), StringComparer.OrdinalIgnoreCase);
                _apps = result.Apps.OrderByDescending(app => app.Bytes).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach (var app in _apps) app.Marked = app.CanMark && marked.Contains(app.Id);
                _loaded = true;
                _status.Text = $"{_apps.Count:N0} installed apps · largest measured first" + (result.StoreUnavailable ? " · Store list unavailable" : "");
                _leftovers.Clear();
                _leftovers.AddRange(leftovers);
                UpdateList();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { _status.Text = "App scan canceled."; }
            catch (Exception error) when (AppInventory.IsRegistryError(error) || error is Win32Exception)
            { _status.Text = "Windows couldn’t scan installed apps. Try again."; }
            finally
            {
                _scan = null;
                if (!_closed)
                {
                    bool returnFocus = IsKeyboardFocusWithin;
                    UpdateScanView();
                    UpdateActions();
                    if (returnFocus && _loaded) _refresh.Focus();
                }
            }
        }

        private List<AppLeftover> FindLeftovers(IReadOnlyList<InstalledApp> installed, CancellationToken token)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<AppLeftover>();
            foreach (var app in _receipts)
            {
                token.ThrowIfCancellationRequested();
                if (IsLeftover(app, installed) && seen.Add(app.Location))
                    items.Add(new AppLeftover(app, AppInventory.Measure(app.Location, token).Bytes));
            }
            return items;
        }

        private static bool IsLeftover(InstalledApp app, IReadOnlyList<InstalledApp> installed)
        {
            if (!AppInventory.IsDedicated(app.Location) || !AppInventory.IsPlainDirectory(app.Location))
                return false;
            try
            {
                if (AppInventory.IsRegistered(app))
                    return false;
            }
            catch (Exception error) when (AppInventory.IsRegistryError(error)) { return false; }

            return !installed.Any(other => other.Location != null && (SystemProtection.IsInside(other.Location, app.Location) ||
                SystemProtection.IsInside(app.Location, other.Location)));
        }

        private void UpdateList()
        {
            string query = _search.Text.Trim();
            bool Matches(string text) => text?.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
            object selected = _list.SelectedItem;
            _list.ItemTemplate = (DataTemplate)FindResource(_showLeftovers ? "AppLeftover" : "AppRow");
            _list.ItemsSource = _showLeftovers ? (System.Collections.IEnumerable)_leftovers.Where(item => Matches(item.Name) || Matches(item.Location)).ToList()
                : _apps.Where(item => Matches(item.Name) || Matches(item.Publisher)).ToList();
            if (selected != null && _list.Items.Contains(selected)) _list.SelectedItem = selected;
            _leftoverMode.Content = _showLeftovers ? "Installed apps" : $"Leftovers ({_leftovers.Count:N0})";
            UpdateActions();
        }

        private InstalledApp[] SelectedApps() => _apps.Any(app => app.Marked) ? _apps.Where(app => app.Marked).ToArray()
            : _list.SelectedItem is InstalledApp selected && !selected.Locked ? new[] { selected } : Array.Empty<InstalledApp>();

        private string SelectedLocation() => (_list.SelectedItem as InstalledApp)?.Location ?? (_list.SelectedItem as AppLeftover)?.Location;

        private void SetWriting(bool writing)
        {
            _writing = writing;
            _writeStateChanged(writing);
            if (!_closed) UpdateActions();
        }

        private void UpdateActions()
        {
            bool ready = _scan == null && !_writing;
            _itemIcon.Content = _list.SelectedItem as InstalledApp;
            _itemIcon.Visibility = _itemIcon.Content == null ? Visibility.Collapsed : Visibility.Visible;
            _review.Content = _showLeftovers ? "Review Recycle Bin move" : "Review uninstall";
            _search.IsEnabled = _list.IsEnabled = ready;
            _refresh.IsEnabled = !_writing;
            _leftoverMode.IsEnabled = ready;
            _openFolder.Visibility = SelectedLocation() == null ? Visibility.Collapsed : Visibility.Visible;
            if (_showLeftovers)
            {
                var item = _list.SelectedItem as AppLeftover;
                bool canRecycle = item != null && SystemProtection.ForPath(item.Location) == EntryProtection.None;
                _itemName.Text = item?.Name ?? "Leftover folders";
                _itemInfo.Text = item == null ? "Folders still present after uninstall will appear here."
                    : canRecycle ? "Can be reviewed for recycling" : "Managed by Windows";
                _itemSize.Text = item?.SizeLabel ?? "";
                _summary.Text = $"{_leftovers.Count:N0} leftover folders";
                _review.IsEnabled = ready && canRecycle;
            }
            else
            {
                var app = _list.SelectedItem as InstalledApp;
                var marked = _apps.Where(item => item.Marked).ToArray();
                int count = marked.Length;
                long measured = marked.Where(item => item.Bytes.HasValue)
                    .GroupBy(item => item.Location, StringComparer.OrdinalIgnoreCase)
                    .Aggregate(0L, (total, group) => DiskScanner.AddBytes(total, group.First().Bytes.Value));
                _itemName.Text = app?.Name ?? "App details";
                _itemInfo.Text = app == null ? "Select an app to see its install folder and measured size."
                    : string.Join(" · ", new[] { app.Kind, app.Publisher, app.Version }.Where(text => !string.IsNullOrWhiteSpace(text)));
                _itemSize.Text = app?.SizeLabel ?? "";
                _summary.Text = count == 0 ? $"{_list.Items.Count:N0} apps shown" : $"{count:N0} marked" +
                    (measured > 0 ? $" · {DiskEntry.FormatSize(measured)} measured" : "");
                _review.IsEnabled = ready && (count > 0 || (app != null && !app.Locked));
            }
            _itemPath.Text = SelectedLocation() ?? "";
            _itemPath.ToolTip = _itemPath.Text;
            _itemSize.Visibility = _itemSize.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task ReviewUninstallAsync()
        {
            if (_writing || _scan != null) return;
            var selected = SelectedApps();
            if (selected.Length == 0) return;
            var launchable = selected.Where(AppRemoval.CanLaunch).ToArray();
            var rows = selected.Select(app => $"{app.Name}  ·  {app.Kind}\n" + (launchable.Contains(app)
                ? app.Package == null ? "Opens the app’s registered uninstaller" : "Removes this package for your Windows account"
                : "Manage this app in Windows Settings"));
            if (!Confirm(rows, launchable.Length > 0 ? "Start uninstall" : "Open Windows Settings")) return;
            if (launchable.Length == 0)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
                    _storageChanged();
                }
                catch (Win32Exception) { UiVisuals.Notice(_owner, "Can’t open Settings", "Open Installed apps from Windows Settings.").ShowDialog(); }
                return;
            }

            SetWriting(true);
            int removed = 0;
            var unsuccessful = new List<string>();
            try
            {
                foreach (var app in launchable)
                {
                    _status.Text = $"Waiting for {app.Name}…";
                    if (app.Package == null && AppInventory.IsDedicated(app.Location) && !_receipts.Any(entry => entry.Id == app.Id))
                        _receipts.Add(app);
                    bool gone = false;
                    try { gone = await Task.Run(() => AppRemoval.Uninstall(app)); }
                    catch (Exception error) when (AppInventory.IsRegistryError(error) || error is Win32Exception || error is ArgumentException)
                    { unsuccessful.Add(app.Name + ": " + error.Message); }
                    if (_closed) return;
                    _storageChanged();
                    if (gone) { removed++; app.Marked = false; }
                    else if (!unsuccessful.Any(text => text.StartsWith(app.Name + ":", StringComparison.Ordinal)))
                        unsuccessful.Add(app.Name + " is still listed by Windows.");
                }
            }
            finally
            {
                SetWriting(false);
            }
            await ScanAsync();
            _status.Text = $"{removed:N0} removed" + (unsuccessful.Count > 0 ? $" · {unsuccessful.Count:N0} need attention" : "") +
                (selected.Length > launchable.Length ? $" · {selected.Length - launchable.Length:N0} available in Windows Settings" : "");
            if (unsuccessful.Count > 0 && IsVisible)
                UiVisuals.Notice(_owner, "Uninstall results", string.Join("\n\n", unsuccessful)).ShowDialog();
        }

        private bool Confirm(IEnumerable<string> apps, string label)
        {
            var window = new Window { Owner = _owner, Title = "Review uninstall", Width = 570, Height = 390,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = _owner.FontFamily };
            UiVisuals.ThemeCaption(window);
            var surface = new DockPanel { Margin = new Thickness(24) };
            var heading = Label("Review uninstall", 24, "Ink", true);
            heading.Margin = new Thickness(0, 0, 0, 14);
            DockPanel.SetDock(heading, Dock.Top);
            surface.Children.Add(heading);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 7, 0) };
            var confirm = new Button { Content = label };
            UiVisuals.Emphasize(confirm);
            actions.Children.Add(cancel);
            actions.Children.Add(confirm);
            DockPanel.SetDock(actions, Dock.Bottom);
            surface.Children.Add(actions);
            var stack = new StackPanel();
            foreach (string app in apps)
            {
                var item = Label(app, 14, "Ink");
                item.TextWrapping = TextWrapping.Wrap;
                item.Margin = new Thickness(0, 0, 0, 10);
                stack.Children.Add(item);
            }
            var note = Label("Uninstalling may remove app data. Leftover folders are reviewed separately.", 13, "Muted");
            note.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(note);
            surface.Children.Add(new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            window.Content = UiVisuals.Surface(new Border { Child = surface }, "Workspace");
            var motion = new DialogMotion(window, (FrameworkElement)window.Content);
            window.Loaded += (sender, args) => cancel.Focus();
            confirm.Click += (sender, args) => motion.Close(() => window.DialogResult = true);
            return window.ShowDialog() == true;
        }

        private async Task RecycleLeftoverAsync()
        {
            if (_writing || _scan != null || !(_list.SelectedItem is AppLeftover leftover)) return;
            SetWriting(true);
            try
            {
                var prepared = await Task.Run(() =>
                {
                    if (!IsLeftover(leftover.App, _apps) || SystemProtection.ForPath(leftover.Location) != EntryProtection.None ||
                        AppInventory.HasOtherRegistration(leftover.Location))
                        throw new IOException("This folder is in use or managed by Windows.");
                    var scan = DiskScanner.Scan(leftover.Location, CancellationToken.None);
                    return RecycleItem.Prepare(Path.GetDirectoryName(leftover.Location), new[] { scan.Root }, CancellationToken.None);
                });
                if (_closed || !IsVisible) return;
                if (new RecycleReview(_owner, prepared, null, prepared[0].Entry).ShowDialog() != true) return;
                if (await Task.Run(() => AppInventory.IsRegistered(leftover.App) || AppInventory.HasOtherRegistration(leftover.Location)))
                    throw new IOException("An installed app now uses this folder. Scan apps again.");
                if (_closed) return;
                MovingFiles = true;
                var moved = await RecycleItem.MoveAsync(prepared);
                MovingFiles = false;
                if (moved.Count == 0)
                    new RecycleReview(_owner, prepared, null, prepared[0].Entry, completed: true).ShowDialog();
                else
                {
                    _storageChanged();
                    _leftovers.Remove(leftover);
                    _receipts.RemoveAll(app => string.Equals(app.Location, leftover.Location, StringComparison.OrdinalIgnoreCase));
                    _status.Text = "Leftover folder moved to the Recycle Bin.";
                    UpdateList();
                }
            }
            catch (Exception error) when (AppInventory.IsRegistryError(error) || error is Win32Exception)
            {
                if (IsVisible) UiVisuals.Notice(_owner, "Folder stayed in place", error.Message).ShowDialog();
            }
            finally
            {
                MovingFiles = false;
                SetWriting(false);
            }
        }

        private static TextBlock Label(string value, double size, string brush, bool emphasis = false)
        {
            var text = new TextBlock { Text = value, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = emphasis ? FontWeights.SemiBold : FontWeights.Normal };
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return text;
        }
    }
}
