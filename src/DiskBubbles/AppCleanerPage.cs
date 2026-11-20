using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _search = new TextBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Button _refresh = new Button { Content = "Scan apps" };
        private readonly Button _leftoverMode = new Button { Content = "Leftovers" };
        private readonly Button _uninstall = new Button { Content = "Uninstall" };
        private readonly Button _deepUninstall = new Button { Content = "Deep Uninstall" };
        private readonly Button _recycleLeftover = new Button { Content = "Review Recycle Bin move" };
        private readonly TextBlock _actionNote = Label("", 13, "Muted");
        private readonly Button _openFolder = new Button { Content = "Open folder…" };
        private readonly TextBlock _status = Label("", 14, "Muted");
        private readonly TextBlock _summary = Label("", 14, "Ink", true);
        private readonly ContentControl _itemIcon = new ContentControl { Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12), Focusable = false, IsHitTestVisible = false };
        private readonly TextBlock _itemName = Label("", 24, "Ink", true);
        private readonly TextBlock _itemInfo = Label("", 15, "Muted");
        private readonly TextBlock _itemSize = Label("", 27, "Ink", true);
        private readonly TextBlock _itemPath = Label("", 13, "Muted");
        private readonly List<AppLeftover> _leftovers = new List<AppLeftover>();
        private List<InstalledApp> _apps = new List<InstalledApp>();
        private CancellationTokenSource _scan;
        private bool _loaded;
        private bool _writing;
        private bool _showLeftovers;
        private bool _closed;
        internal bool MovingFiles => _writing;

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
                if (location != null) _openExplorer(location);
            };
            _uninstall.Click += async (sender, args) => await ReviewUninstallAsync(deep: false);
            _deepUninstall.Click += async (sender, args) => await ReviewUninstallAsync(deep: true);
            _recycleLeftover.Click += async (sender, args) =>
            {
                if (_list.SelectedItem is AppLeftover item) await ReviewLeftoversAsync(new[] { item });
            };
            UpdateList();
        }

        internal Task OpenAsync() => _loaded ? Task.CompletedTask : ScanAsync();

        private void Build()
        {
            Children.Add(_results);
            _results.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _results.RowDefinitions.Add(new RowDefinition());

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
            var tableHeader = new StackPanel { Margin = new Thickness(0, 0, 8, 9) };
            var toolbar = new DockPanel();
            var filter = Label("Filter by name", 14, "Muted");
            filter.VerticalAlignment = VerticalAlignment.Center;
            filter.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(filter, Dock.Left);
            toolbar.Children.Add(filter);
            _search.Height = 36;
            _search.FontSize = 15;
            _search.Padding = new Thickness(8, 0, 8, 0);
            _search.Style = (Style)FindResource("FilterInput");
            _search.SetResourceReference(Control.BackgroundProperty, "Raised");
            _search.SetResourceReference(Control.ForegroundProperty, "Ink");
            _search.SetResourceReference(Control.BorderBrushProperty, "Line");
            _search.SetResourceReference(TextBox.CaretBrushProperty, "Ink");
            AutomationProperties.SetName(_search, "Filter by app or publisher name");
            toolbar.Children.Add(_search);
            tableHeader.Children.Add(toolbar);
            _summary.Margin = new Thickness(0, 7, 0, 0);
            tableHeader.Children.Add(_summary);
            DockPanel.SetDock(tableHeader, Dock.Top);
            table.Children.Add(tableHeader);

            _list.Style = (Style)FindResource("AppResults");
            _list.ItemContainerStyle = (Style)FindResource("EntryRow");
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
            var inspector = new DockPanel();
            var choices = new StackPanel { Margin = new Thickness(17, 10, 16, 16) };
            _actionNote.TextWrapping = TextWrapping.Wrap;
            _actionNote.Margin = new Thickness(0, 0, 0, 8);
            choices.Children.Add(_actionNote);
            _deepUninstall.Margin = new Thickness(0, 7, 0, 0);
            choices.Children.Add(_uninstall);
            choices.Children.Add(_deepUninstall);
            choices.Children.Add(_recycleLeftover);
            DockPanel.SetDock(choices, Dock.Bottom);
            inspector.Children.Add(choices);
            inspector.Children.Add(new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var side = UiVisuals.FrostedPane(inspector, "GlassClear");
            SetColumn(side, 2);
            columns.Children.Add(side);
            SetRow(columns, 1);
            _results.Children.Add(columns);

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
                var marked = new HashSet<string>(_apps.Where(app => app.Marked).Select(app => app.Id), StringComparer.OrdinalIgnoreCase);
                string previous = (_list.SelectedItem as InstalledApp)?.Id;
                _apps = result.Apps.OrderByDescending(app => app.Bytes).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                foreach (var app in _apps) app.Marked = app.CanMark && marked.Contains(app.Id);
                _loaded = true;
                _status.Text = $"{_apps.Count:N0} installed apps · largest measured first" + (result.StoreUnavailable ? " · Store list unavailable" : "");
                _leftovers.RemoveAll(item => _apps.Any(app => app.Id == item.App.Id));
                UpdateList();
                if (previous != null && !_showLeftovers)
                    _list.SelectedItem = _apps.FirstOrDefault(app => app.Id == previous);
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

        private void UpdateList()
        {
            string query = _search.Text.Trim();
            bool Matches(string text) => text?.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
            object selected = _list.SelectedItem;
            _list.ItemTemplate = (DataTemplate)FindResource(_showLeftovers ? "AppLeftover" : "AppRow");
            _list.ItemsSource = _showLeftovers ? (System.Collections.IEnumerable)_leftovers.Where(item => Matches(item.Name) || Matches(item.Location)).ToList()
                : new ObservableCollection<InstalledApp>(_apps.Where(item => Matches(item.Name) || Matches(item.Publisher)));
            if (selected != null && _list.Items.Contains(selected)) _list.SelectedItem = selected;
            UpdateLeftoverButton();
            UpdateActions();
        }

        private void RemoveInstalledApp(InstalledApp app)
        {
            _apps.Remove(app);
            app.Marked = false;
            if (_list.ItemsSource is ObservableCollection<InstalledApp> visible)
                visible.Remove(app);
            UpdateActions();
        }

        private void UpdateLeftoverButton() =>
            _leftoverMode.Content = _showLeftovers ? "Installed apps" : $"Leftovers ({_leftovers.Count:N0})";

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
            _recycleLeftover.Visibility = _showLeftovers ? Visibility.Visible : Visibility.Collapsed;
            _search.IsEnabled = _list.IsEnabled = ready;
            _refresh.IsEnabled = !_writing;
            _leftoverMode.IsEnabled = ready;
            _openFolder.Visibility = SelectedLocation() == null ? Visibility.Collapsed : Visibility.Visible;
            _openFolder.IsEnabled = ready;
            if (_showLeftovers)
            {
                var item = _list.SelectedItem as AppLeftover;
                bool canRecycle = item != null && SystemProtection.ForPath(item.Location) == EntryProtection.None;
                _itemName.Text = item?.Name ?? "Leftover folders";
                _itemInfo.Text = item?.Kind ?? "Folders found after uninstall appear here.";
                _itemSize.Text = item?.SizeLabel ?? "";
                _summary.Text = $"{_leftovers.Count:N0} leftover folders";
                _recycleLeftover.IsEnabled = ready && canRecycle;
                _uninstall.Visibility = _deepUninstall.Visibility = Visibility.Collapsed;
                _actionNote.Text = "";
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
                _uninstall.Visibility = _deepUninstall.Visibility = count > 0 || app != null ? Visibility.Visible : Visibility.Collapsed;
                _uninstall.IsEnabled = _deepUninstall.IsEnabled = ready && (count > 0 || (app != null && !app.Locked));
                _actionNote.Text = count > 1 ? $"Actions apply to {count:N0} marked apps."
                    : count == 1 && marked[0] != app ? $"Actions apply to {marked[0].Name}."
                    : app?.Locked == true ? "Managed by Windows or a driver package." : "";
            }
            _actionNote.Visibility = _actionNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _itemPath.Text = SelectedLocation() ?? "";
            _itemPath.ToolTip = _itemPath.Text;
            _itemSize.Visibility = _itemSize.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task ReviewUninstallAsync(bool deep)
        {
            if (_writing || _scan != null) return;
            var selected = SelectedApps();
            if (selected.Length == 0) return;

            SetWriting(true);
            int removed = 0;
            int restart = 0;
            int unavailable = 0;
            var found = new List<AppLeftover>();
            var issues = new List<string>();
            try
            {
                var launchable = await Task.Run(() => selected.Where(AppRemoval.CanLaunch).ToArray());
                unavailable = selected.Length - launchable.Length;
                if (launchable.Length == 0)
                {
                    UiVisuals.Notice(_owner, "Uninstall unavailable", "Disk Bubbles couldn’t verify a registered uninstaller for the selected apps.").ShowDialog();
                    return;
                }

                var rows = selected.Select(app => app.Name + " · " + app.Kind + "\n" +
                    (!launchable.Contains(app) ? "Unavailable for in-app uninstall" :
                    app.Package != null ? "Remove for the current Windows account" : "Run the registered uninstaller"));
                if (!Confirm(rows, deep))
                    return;

                foreach (var app in launchable)
                {
                    _status.Text = $"Uninstalling {app.Name}…";
                    try
                    {
                        var result = await Task.Run(() => AppRemoval.Uninstall(app));
                        _storageChanged();
                        if (result == AppUninstallResult.RestartRequired)
                        {
                            restart++;
                            continue;
                        }
                        if (result != AppUninstallResult.Removed)
                        {
                            issues.Add(app.Name + " is still listed by Windows.");
                            continue;
                        }

                        removed++;
                        RemoveInstalledApp(app);
                        _status.Text = $"Checking leftovers for {app.Name}…";
                        var detected = await Task.Run(() => AppCleanup.Find(app, deep));
                        foreach (var item in detected)
                        {
                            if (_leftovers.Any(other => string.Equals(other.Location, item.Location, StringComparison.OrdinalIgnoreCase)))
                                continue;
                            found.Add(item);
                            _leftovers.Add(item);
                        }
                    }
                    catch (Exception error) when (AppInventory.IsRegistryError(error) || error is Win32Exception || error is ArgumentException)
                    {
                        issues.Add(app.Name + ": " + error.Message);
                    }
                }

                if (found.Count > 0)
                    await MoveReviewedLeftoversAsync(found);
            }
            finally
            {
                SetWriting(false);
            }

            if (_closed) return;
            UpdateLeftoverButton();
            _status.Text = $"{removed:N0} removed" + (restart > 0 ? $" · {restart:N0} need a restart" : "") +
                (issues.Count > 0 ? $" · {issues.Count:N0} need attention" : "") +
                (unavailable > 0 ? $" · {unavailable:N0} unavailable" : "") +
                (_leftovers.Count > 0 ? $" · {_leftovers.Count:N0} leftovers to review" : "");
            if (issues.Count > 0 && IsVisible)
                UiVisuals.Notice(_owner, "Uninstall results", string.Join("\n\n", issues)).ShowDialog();
        }

        private bool Confirm(IEnumerable<string> apps, bool deep)
        {
            var window = new Window { Owner = _owner, Title = deep ? "Review deep uninstall" : "Review uninstall", Width = 570, Height = 390,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = _owner.FontFamily };
            UiVisuals.ThemeCaption(window);
            var surface = new DockPanel { Margin = new Thickness(24) };
            var heading = Label(window.Title, 24, "Ink", true);
            heading.Margin = new Thickness(0, 0, 0, 14);
            DockPanel.SetDock(heading, Dock.Top);
            surface.Children.Add(heading);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 7, 0) };
            var confirm = new Button { Content = deep ? "Start deep uninstall" : "Start uninstall" };
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
            var note = Label(deep
                ? "After uninstall, Disk Bubbles checks for remaining install files, caches, and matching app data. Review any folders before moving them to the Recycle Bin."
                : "After uninstall, review any verified leftover install folders. The uninstaller may remove app data.", 13, "Muted");
            note.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(note);
            surface.Children.Add(new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            window.Content = UiVisuals.Surface(new Border { Child = surface }, "Workspace");
            var motion = new DialogMotion(window, (FrameworkElement)window.Content);
            window.Loaded += (sender, args) => cancel.Focus();
            confirm.Click += (sender, args) => motion.Close(() => window.DialogResult = true);
            return window.ShowDialog() == true;
        }

        private async Task ReviewLeftoversAsync(IReadOnlyList<AppLeftover> items)
        {
            if (_writing || _scan != null) return;
            SetWriting(true);
            try { await MoveReviewedLeftoversAsync(items); }
            finally { SetWriting(false); }
        }

        private async Task MoveReviewedLeftoversAsync(IReadOnlyList<AppLeftover> items)
        {
            string pendingStatus = _status.Text;
            try
            {
                _status.Text = "Verifying leftover folders…";
                var prepared = await Task.Run(() => AppCleanup.Prepare(items));
                if (_closed || !IsVisible) return;
                if (prepared.Count == 0)
                {
                    UiVisuals.Notice(_owner, "No verified leftovers", "Disk Bubbles couldn’t verify a folder for recycling.").ShowDialog();
                    return;
                }
                if (new RecycleReview(_owner, prepared, null, null).ShowDialog() != true) return;

                var byPath = items.ToDictionary(item => item.Location, StringComparer.OrdinalIgnoreCase);
                _status.Text = "Moving leftover folders to the Recycle Bin…";
                await RecycleBin.MoveAsync(prepared, new System.Windows.Interop.WindowInteropHelper(_owner).Handle, null, item =>
                {
                    var owner = byPath[item.Path];
                    if (AppCleanup.IsRegistered(owner.App) || !AppCleanup.IsSafeLocation(owner.Location, owner.App))
                        throw new IOException("An app or service now uses this folder");
                });
                var moved = prepared.Where(item => item.Removed).ToList();
                var incomplete = prepared.Where(item => !item.Removed).ToArray();
                if (incomplete.Length > 0)
                    new RecycleReview(_owner, incomplete, null, null, completed: true).ShowDialog();
                if (moved.Count == 0) return;

                _storageChanged();
                _leftovers.RemoveAll(leftover => moved.Any(item => string.Equals(item.Path, leftover.Location, StringComparison.OrdinalIgnoreCase)));
                pendingStatus = $"{moved.Count:N0} leftover folders moved to the Recycle Bin.";
                if (_showLeftovers) UpdateList();
                else UpdateLeftoverButton();
            }
            catch (Exception error) when (AppInventory.IsRegistryError(error) || error is Win32Exception || error is ArgumentException)
            {
                if (!_closed && IsVisible) UiVisuals.Notice(_owner, "Cleanup stopped", error.Message).ShowDialog();
            }
            finally
            {
                if (!_closed) _status.Text = pendingStatus;
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
