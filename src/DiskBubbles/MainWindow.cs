using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using IOPath = System.IO.Path;

namespace DiskBubbles
{
    internal sealed partial class MainWindow : Window
    {
        private sealed class FolderView
        {
            public int MapStart;
            public double Offset;
        }

        private readonly Grid _surface = new Grid();
        private readonly Grid _browser = new Grid();
        private readonly AmbientField _ambient = new AmbientField();
        private readonly AppCleanerPage _appCleaner;
        private readonly Button _diskMode = new Button { Content = "Disk Scan" };
        private readonly Button _appsMode = new Button { Content = "App Cleaner" };
        private readonly ListBox _entries = new ListBox();
        private readonly BubbleMap _map = new BubbleMap();
        private readonly StackPanel _crumbs = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Button _folder = new Button();
        private readonly Button _drive = new Button();
        private readonly Button _recycleBin = new Button();
        private readonly Button _appearance = new Button();
        private readonly Button _up = new Button { ToolTip = "Up one folder (Alt+Up)" };
        private readonly Button _back = new Button { ToolTip = "Back (Alt+Left)" };
        private readonly Button _forward = new Button { ToolTip = "Forward (Alt+Right)" };
        private readonly Button _rescan = new Button();
        private readonly Button _reviewRecycle = new Button();
        private readonly Button _clearMarks = new Button { Content = "Clear" };
        private readonly Border _selectionTray = new Border();
        private readonly TextBlock _selectionCount = new TextBlock();
        private readonly TextBlock _selectionSize = new TextBlock();
        private readonly Border _pager = new Border();
        private readonly Button _pagePrevious = new Button();
        private readonly Button _pageNext = new Button();
        private readonly TextBlock _pageRange = new TextBlock();
        private readonly TextBlock _pageZoom = new TextBlock();
        private readonly WorkSheet _work = new WorkSheet();
        private readonly Button _cancel = new Button { Content = "Cancel scan" };
        private readonly TextBlock _path = new TextBlock();
        private readonly TextBlock _itemCount = new TextBlock();
        private readonly TextBlock _mapNote = new TextBlock();
        private readonly TextBlock _mapEmpty = new TextBlock();
        private readonly StackPanel _empty = new StackPanel();
        private readonly TextBlock _total = new TextBlock();
        private readonly TextBlock _totalNote = new TextBlock();
        private readonly TextBlock _status = new TextBlock();
        private readonly TextBlock _capacity = new TextBlock();
        private readonly ProgressBar _capacityBar = new ProgressBar { Height = 6, Maximum = 1 };
        private readonly StackPanel _capacityPanel = new StackPanel();
        private readonly TextBlock _scanCounts = new TextBlock();
        private readonly TextBlock _scanPath = new TextBlock();
        private readonly ProgressBar _spinner = new ProgressBar { Height = 6, Width = 300 };
        private readonly Grid _landing = new Grid();
        private readonly Grid _scanning = new Grid();
        private readonly RecycleCompletePage _completed = new RecycleCompletePage();
        private readonly List<FrameworkElement> _artwork = new List<FrameworkElement>();
        private readonly Stack<List<DiskEntry>> _backHistory = new Stack<List<DiskEntry>>();
        private readonly Stack<List<DiskEntry>> _forwardHistory = new Stack<List<DiskEntry>>();
        private readonly ConditionalWeakTable<DiskEntry, FolderView> _folderViews = new ConditionalWeakTable<DiskEntry, FolderView>();
        private List<DiskEntry> _trail = new List<DiskEntry>();
        private DiskEntry _rowPressed;
        private DiskEntry _markPressed;
        private Point _rowPressPoint;
        private bool _recycleBusy;
        private bool _movingToBin;
        private DiskEntry _linkedEntry;
        private ScanResult _result;
        private string _rootPath;
        private CancellationTokenSource _scan;
        private CancellationTokenSource _recycleCheck;
        private bool _closed;

        public MainWindow()
        {
            Title = "Disk Bubbles";
            Width = Math.Min(1320, SystemParameters.WorkArea.Width);
            Height = Math.Min(880, SystemParameters.WorkArea.Height);
            MinWidth = 820;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(BackgroundProperty, "Base");
            SetResourceReference(ForegroundProperty, "Ink");
            FontFamily = new FontFamily("Segoe UI");
            Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UiVisuals.ThemeCaption(this);

            _appCleaner = new AppCleanerPage(this, async path => await OpenExplorerPathAsync(path, true), () =>
            {
                if (_result == null) return;
                _result.NeedsRefresh = true;
                UpdateScanStatus();
            }, busy => _diskMode.IsEnabled = !busy);

            var shell = new DockPanel { LayoutTransform = new ScaleTransform(1.06, 1.06) };
            var header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            shell.Children.Add(header);
            _surface.Margin = new Thickness(22, 14, 22, 14);
            shell.Children.Add(_surface);
            var viewport = new Grid();
            viewport.Children.Add(_ambient);
            viewport.Children.Add(shell);
            Content = viewport;
            BuildBrowser();
            BuildLanding();
            BuildScanning();
            foreach (UIElement page in new UIElement[] { _browser, _landing, _scanning, _appCleaner, _completed })
                _surface.Children.Add(page);
            _completed.BackRequested += () => ShowPage(_browser);
            _completed.HomeRequested += () => ShowPage(_landing);
            _completed.RecycleBinRequested += () => OpenExplorer("shell:RecycleBinFolder");
            ShowPage(_landing);

            _folder.Click += async (sender, args) => await ChooseFolderAsync();
            _recycleBin.Click += (sender, args) => OpenExplorer("shell:RecycleBinFolder");
            _drive.Click += (sender, args) => ShowDriveMenu();
            _up.Click += (sender, args) => Navigate(_trail.Count - 2);
            _back.Click += (sender, args) => MoveInHistory(_backHistory, _forwardHistory);
            _forward.Click += (sender, args) => MoveInHistory(_forwardHistory, _backHistory);
            AutomationProperties.SetName(_back, "Back");
            AutomationProperties.SetName(_forward, "Forward");
            AutomationProperties.SetName(_up, "Up one folder");
            _pagePrevious.Click += (sender, args) => _map.PreviousPage();
            _pageNext.Click += (sender, args) => _map.NextPage();
            _rescan.Click += async (sender, args) => await ScanAsync(_rootPath);
            _map.Activated += Activate;
            _map.PageChanged += () =>
            {
                UpdateMapPager();
                UpdateSelection();
            };
            _map.ContextRequested += entry =>
            {
                ContextMenu menu = BuildBubbleMenu(entry);
                if (menu != null)
                    menu.IsOpen = true;
            };
            _map.BackgroundActivated += () => Navigate(_trail.Count - 2);
            _map.HoverChanged += entry =>
            {
                if (entry != null)
                    LinkEntry(entry);
                _ambient.Focus(entry == null ? (Color?)null : UiVisuals.KindColor(entry.Kind));
                UpdateMapCaption();
            };
            _entries.MouseMove += (sender, args) =>
            {
                var row = ItemsControl.ContainerFromElement(_entries, args.OriginalSource as DependencyObject) as ListBoxItem;
                var entry = row?.Content as DiskEntry;
                if (_map.RowHover == entry)
                    return;
                _map.RowHover = entry;
                UpdateMapCaption();
            };
            _entries.MouseLeave += (sender, args) =>
            {
                _markPressed = null;
                _map.RowHover = null;
                UpdateMapCaption();
            };
            _reviewRecycle.Click += async (sender, args) => await ReviewRecycleAsync();
            _clearMarks.Click += (sender, args) => ClearMarks();
            _entries.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, args) =>
            {
                if (args.OriginalSource is CheckBox box && box.DataContext is DiskEntry entry)
                {
                    args.Handled = true;
                    SetMarked(entry, box.IsChecked == true);
                }
            }));
            _entries.SelectionChanged += (sender, args) =>
            {
                if (_entries.SelectedItem is DiskEntry entry)
                    _map.Reveal(entry);
                UpdateSelection();
            };
            _entries.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                _markPressed = MarkAt(args.OriginalSource);
                if (_markPressed != null)
                    args.Handled = true;
                _rowPressed = args.ClickCount == 1 ? RowAt(args.OriginalSource) : null;
                _rowPressPoint = args.GetPosition(_entries);
            };
            _entries.PreviewMouseLeftButtonUp += (sender, args) =>
            {
                DiskEntry pressed = _rowPressed;
                _rowPressed = null;
                Vector movement = args.GetPosition(_entries) - _rowPressPoint;
                bool stayed = Math.Abs(movement.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(movement.Y) <= SystemParameters.MinimumVerticalDragDistance;
                DiskEntry mark = _markPressed;
                _markPressed = null;
                if (mark != null)
                {
                    args.Handled = true;
                    if (mark == MarkAt(args.OriginalSource) && stayed && mark.CanRecycle)
                        SetMarked(mark, !mark.IsMarked);
                    return;
                }
                if (pressed == null || !pressed.IsFolder || pressed != RowAt(args.OriginalSource) || !stayed)
                    return;
                args.Handled = true;
                Activate(pressed);
            };
            _entries.MouseDoubleClick += (sender, args) =>
            {
                if (RowAt(args.OriginalSource) is DiskEntry entry && !entry.IsFolder)
                    OpenFileInExplorer(entry);
            };
            _entries.KeyDown += async (sender, args) =>
            {
                if (args.OriginalSource is CheckBox || !(_entries.SelectedItem is DiskEntry entry))
                    return;
                if (args.Key == Key.Enter)
                    OpenSelected(animate: false);
                else if (args.Key == Key.Space && entry.CanRecycle)
                    SetMarked(entry, !entry.IsMarked);
                else if (args.Key == Key.Delete)
                    await RecycleAsync(new[] { MarkFor(entry) }, review: true);
                else
                    return;
                args.Handled = true;
            };
            Closed += (sender, args) =>
            {
                _closed = true;
                _scan?.Cancel();
                _recycleCheck?.Cancel();
            };
            Closing += (sender, args) => args.Cancel = _movingToBin || _appCleaner.MovingFiles;
            PreviewKeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape && _recycleCheck != null)
                {
                    _recycleCheck.Cancel();
                    args.Handled = true;
                    return;
                }
                if (_recycleBusy || _browser.Visibility != Visibility.Visible)
                    return;
                if (Keyboard.Modifiers == ModifierKeys.Alt && args.Key == Key.System)
                {
                    if (args.SystemKey == Key.Left)
                        MoveInHistory(_backHistory, _forwardHistory, animate: false);
                    else if (args.SystemKey == Key.Right)
                        MoveInHistory(_forwardHistory, _backHistory, animate: false);
                    else if (args.SystemKey == Key.Up)
                        Navigate(_trail.Count - 2, animate: false);
                    else
                        return;
                    args.Handled = true;
                }
                else if (Keyboard.Modifiers == ModifierKeys.None && args.Key == Key.Back && !(args.OriginalSource is TextBox))
                {
                    Navigate(_trail.Count - 2, animate: false);
                    args.Handled = true;
                }
                else if (Keyboard.Modifiers == ModifierKeys.None && (args.Key == Key.PageDown || args.Key == Key.PageUp) && !_entries.IsKeyboardFocusWithin)
                {
                    if (args.Key == Key.PageDown) _map.NextPage(); else _map.PreviousPage();
                    args.Handled = true;
                }
            };
        }

        private MarkedEntry MarkFor(DiskEntry entry) =>
            new MarkedEntry { Entry = entry, Parents = _trail.ToArray(), Folder = CurrentPath() };

        private void ShowDriveMenu()
        {
            var menu = new ContextMenu { PlacementTarget = _drive, Placement = PlacementMode.Bottom };
            foreach (var drive in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.Fixed || item.DriveType == DriveType.Removable))
            {
                string path = drive.RootDirectory.FullName;
                string label = path.TrimEnd('\\');
                try
                {
                    if (drive.IsReady)
                        label += $"   {DiskEntry.FormatSize(drive.TotalSize - drive.TotalFreeSpace)} of {DiskEntry.FormatSize(drive.TotalSize)} used";
                }
                catch (Exception error) when (DiskScanner.IsFileSystemError(error))
                {
                }
                var item = new MenuItem { Header = label };
                item.Click += async (sender, args) => await ScanAsync(path);
                menu.Items.Add(item);
            }
            _drive.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private async Task ChooseFolderAsync()
        {
            using (var picker = new Forms.FolderBrowserDialog { ShowNewFolderButton = false, Description = "Choose a folder to scan" })
            {
                if (picker.ShowDialog() == Forms.DialogResult.OK)
                    await ScanAsync(picker.SelectedPath);
            }
        }

        internal async Task ScanAsync(string path)
        {
            if (_scan != null || _recycleBusy || string.IsNullOrEmpty(path))
                return;

            var scan = new CancellationTokenSource();
            _scan = scan;
            _folder.IsEnabled = _drive.IsEnabled = false;
            _scanPath.Text = path;
            _scanCounts.Text = "Reading folders…";
            _cancel.IsEnabled = true;
            bool completed = false;
            _spinner.IsIndeterminate = SystemParameters.ClientAreaAnimation;
            ShowPage(_scanning);
            long lastCount = -1;
            var progress = new Progress<(string Path, long Files, long Folders, long Skipped)>(value =>
            {
                long count = value.Files + value.Folders + value.Skipped;
                if (ReferenceEquals(_scan, scan) && !scan.IsCancellationRequested && count >= lastCount)
                {
                    lastCount = count;
                    _scanPath.Text = value.Path;
                    _scanCounts.Text = Counts(value.Files, value.Folders, value.Skipped);
                }
            });
            try
            {
                string fullPath = new DirectoryInfo(path).FullName;
                ScanResult result = await Task.Run(() => DiskScanner.Scan(fullPath, scan.Token,
                    (files, folders, skipped, folder) => ((IProgress<(string, long, long, long)>)progress).Report(
                        (folder, files, folders, skipped))), scan.Token);
                scan.Token.ThrowIfCancellationRequested();
                if (_closed)
                    return;

                if (result.RootPartial && result.Root.Children.Count == 0)
                {
                    UiVisuals.Notice(this, "Can’t open this location", "Windows couldn’t read this location. Choose another folder or drive.").ShowDialog();
                    return;
                }

                _rootPath = fullPath;
                _result = result;
                foreach (DiskEntry entry in _marked.Keys)
                    entry.IsMarked = false;
                _marked.Clear();
                _backHistory.Clear();
                _forwardHistory.Clear();
                _trail = new List<DiskEntry> { result.Root };
                completed = true;
            }
            catch (OperationCanceledException) when (scan.IsCancellationRequested)
            {
            }
            catch (Exception error) when (DiskScanner.IsFileSystemError(error) || error is ArgumentException)
            {
                if (!_closed)
                    UiVisuals.Notice(this, "Can’t scan this location", error.Message).ShowDialog();
            }
            finally
            {
                _scan = null;
                scan.Dispose();
                if (!_closed)
                {
                    _spinner.IsIndeterminate = false;
                    _folder.IsEnabled = _drive.IsEnabled = true;
                    if (_appCleaner.Visibility != Visibility.Visible)
                        ShowPage(_result == null ? _landing : _browser);
                    if (completed)
                    {
                        RefreshBrowser();
                        _map.Reveal();
                    }
                }
            }
        }

        private void RefreshBrowser(DiskEntry origin = null, bool returning = false, bool restoreView = false, bool animate = false)
        {
            LinkEntry(null);
            _markPressed = null;
            DiskEntry current = _trail[_trail.Count - 1];
            current.OrderChildren();
            FolderView view = restoreView && _folderViews.TryGetValue(current, out FolderView remembered) ? remembered : null;
            _map.ShowEntries(current, view?.MapStart ?? 0, origin, returning, animate);
            UpdateMapPager();
            _rowPressed = null;
            _entries.ItemsSource = current.Children;
            _entries.SelectedItem = null;
            ListingScrollViewer(_entries)?.ScrollToVerticalOffset(view?.Offset ?? 0);
            _itemCount.Text = current.Children.Count > 1 ? $"{CountLabel(current.Children.Count, "item")} · largest first"
                : CountLabel(current.Children.Count, "item");
            _empty.Visibility = current.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _mapEmpty.Text = current.Children.Count == 0 && (current.Protection & EntryProtection.Incomplete) != 0
                ? "Windows didn’t let Disk Bubbles read this folder" : "This folder is empty";
            _total.Text = current.SizeLabel;
            _totalNote.Text = _trail.Count == 1 || _result.Root.Bytes == 0 ? "Scanned size"
                : $"{(double)current.Bytes / _result.Root.Bytes:P1} of this scan";
            _path.Text = CurrentPath();
            _path.ToolTip = _path.Text;
            UpdateScanStatus();
            _up.IsEnabled = _trail.Count > 1;
            _back.IsEnabled = _backHistory.Count > 0;
            _forward.IsEnabled = _forwardHistory.Count > 0;
            _capacityPanel.Visibility = _result.VolumeBytes > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_result.VolumeBytes > 0)
            {
                long used = _result.VolumeBytes - _result.FreeBytes;
                _capacity.Text = DiskEntry.FormatSize(used) + " used  ·  " + DiskEntry.FormatSize(_result.FreeBytes) + " free";
                _capacityBar.Value = (double)used / _result.VolumeBytes;
            }
            UpdateBreadcrumbs();
            UpdateMarks();
        }

        private void UpdateScanStatus()
        {
            if (_result == null)
                return;
            _status.Text = Counts(_result.Files, _result.Folders, _result.Skipped) + $"  ·  {_result.Elapsed.TotalSeconds:0.0} s";
            if (_result.NeedsRefresh)
                _status.Text += "  ·  Rescan to refresh totals";
            _status.ToolTip = _result.NeedsRefresh ? "Something changed outside this scan, or an item couldn’t be checked. Rescan for exact totals."
                : _result.Skipped > 0 ? "Unreadable entries, unsupported names and filesystem links were skipped."
                : "Totals are file sizes. Space used on disk can differ slightly.";
        }

        private void UpdateMapPager()
        {
            bool paged = _map.HasPrevious || _map.HasNext;
            _pager.Visibility = paged ? Visibility.Visible : Visibility.Collapsed;
            _pagePrevious.IsEnabled = _map.HasPrevious;
            _pageNext.Visibility = _map.HasNext ? Visibility.Visible : Visibility.Collapsed;
            _pageRange.Text = _map.PageEnd - _map.PageStart > 1
                ? $"{_map.PageStart + 1:N0}–{_map.PageEnd:N0} of {_map.ItemCount:N0}"
                : $"{_map.PageEnd:N0} of {_map.ItemCount:N0}";
            double zoom = _map.PageZoom;
            _pageZoom.Text = zoom >= 1.5 ? $"shown {(zoom >= 100 ? zoom.ToString("N0") : zoom.ToString("0.#"))}× larger than page 1" : "";
            _pageZoom.Visibility = _pageZoom.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _pager.ToolTip = _map.HasNext
                ? $"{_map.ItemCount - _map.PageEnd:N0} smaller items continue on the next page (Page Down)"
                : "Sizes are true to scale within each page";
        }

        private void UpdateMapCaption()
        {
            if (_trail.Count == 0)
                return;

            DiskEntry entry = _map.Inspected ?? (_entries.SelectedItems.Count == 1 ? _entries.SelectedItem as DiskEntry : null);
            DiskEntry folder = _trail[_trail.Count - 1];
            string caption = _trail.Count == 1 ? "Click to open · right-click for more" : "Double-click empty space to go up";
            if (entry != null)
            {
                caption = entry.Name + "  ·  " + entry.SizeLabel;
                if (folder.Bytes > 0)
                    caption += $"  ·  {(double)entry.Bytes / folder.Bytes:P1}";
                if (entry.IsSystem)
                    caption += "  ·  System";
            }
            if (_mapNote.Text == caption)
                return;
            _mapNote.Text = caption;
            _mapNote.SetResourceReference(TextBlock.ForegroundProperty, entry == null ? "Muted" : "Ink");
        }

        private void UpdateBreadcrumbs()
        {
            _crumbs.Children.Clear();
            for (int i = 0; i < _trail.Count; i++)
            {
                if (_trail.Count > 4 && i > 0 && i < _trail.Count - 3)
                {
                    if (i == 1)
                        AddDivider("›  …");
                    continue;
                }
                if (i > 0)
                    AddDivider("›");

                int level = i;
                string name = _trail[i].Name.TrimEnd('\\');
                bool current = i == _trail.Count - 1;
                string text = name.Length > 26 ? name.Substring(0, 25) + "…" : name;
                if (current)
                {
                    var crumb = Label(text, 17, "Ink", FontWeights.SemiBold);
                    crumb.Margin = new Thickness(7, 2, 0, 2);
                    crumb.VerticalAlignment = VerticalAlignment.Center;
                    crumb.ToolTip = name;
                    _crumbs.Children.Add(crumb);
                }
                else
                {
                    var crumb = new Button { Content = text, ToolTip = name, Style = (Style)FindResource("Crumb") };
                    crumb.Click += (sender, args) => Navigate(level);
                    _crumbs.Children.Add(crumb);
                }
            }

            void AddDivider(string text)
            {
                var divider = Label(text, 15, "Subtle");
                divider.VerticalAlignment = VerticalAlignment.Center;
                _crumbs.Children.Add(divider);
            }
        }

        private void Navigate(int index, bool animate = true)
        {
            if (index < 0 || index >= _trail.Count - 1)
                return;
            OpenLocation(_trail.GetRange(0, index + 1), animate);
        }

        private void OpenLocation(List<DiskEntry> next, bool animate = true)
        {
            _backHistory.Push(_trail);
            _forwardHistory.Clear();
            ChangeLocation(next, animate);
        }

        private void MoveInHistory(Stack<List<DiskEntry>> source, Stack<List<DiskEntry>> destination, bool animate = true)
        {
            if (source.Count == 0)
                return;
            destination.Push(_trail);
            ChangeLocation(source.Pop(), animate);
        }

        private void ChangeLocation(List<DiskEntry> next, bool animate)
        {
            FolderView view = _folderViews.GetValue(_trail[_trail.Count - 1], key => new FolderView());
            view.MapStart = _map.PageStart;
            view.Offset = ListingScrollViewer(_entries)?.VerticalOffset ?? 0;
            bool returning = next.Count < _trail.Count;
            var deeper = returning ? _trail : next;
            var shallower = returning ? next : _trail;
            DiskEntry origin = animate && deeper.Count > shallower.Count && deeper[shallower.Count - 1] == shallower[shallower.Count - 1]
                ? deeper[shallower.Count] : null;
            _trail = next;
            RefreshBrowser(origin, returning, restoreView: true, animate: animate);
        }

        private static ScrollViewer ListingScrollViewer(DependencyObject parent)
        {
            if (parent is ScrollViewer scroll)
                return scroll;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                ScrollViewer child = ListingScrollViewer(VisualTreeHelper.GetChild(parent, i));
                if (child != null)
                    return child;
            }
            return null;
        }

        private string CurrentPath()
        {
            string path = _rootPath;
            for (int level = 1; level < _trail.Count; level++)
                path = IOPath.Combine(path, _trail[level].Name);
            return path;
        }

        private void Activate(DiskEntry entry)
        {
            if (entry.IsFolder)
            {
                OpenLocation(new List<DiskEntry>(_trail) { entry });
                return;
            }
            _entries.SelectedItem = entry;
            _entries.ScrollIntoView(entry);
        }

        private void UpdateSelection()
        {
            _map.SetSelection(_entries.SelectedItem as DiskEntry, _marked.Keys);
            UpdateMapCaption();
        }

        private void LinkEntry(DiskEntry entry)
        {
            if (_linkedEntry == entry)
                return;
            if (_linkedEntry != null)
                _linkedEntry.IsLinked = false;
            _linkedEntry = entry;
            if (entry != null)
            {
                entry.IsLinked = true;
                _entries.ScrollIntoView(entry);
            }
        }

        private DiskEntry MarkAt(object source)
        {
            for (var node = source as Visual; node != null && node != _entries; node = VisualTreeHelper.GetParent(node) as Visual)
            {
                if (node is CheckBox box)
                    return box.DataContext as DiskEntry;
                if (node is Grid { Name: "MarkArea" } area)
                    return area.DataContext as DiskEntry;
                if (node is ListBoxItem)
                    break;
            }
            return null;
        }

        private DiskEntry RowAt(object source)
        {
            for (var node = source as Visual; node != null && node != _entries; node = VisualTreeHelper.GetParent(node) as Visual)
            {
                if (node is CheckBox || node is Grid { Name: "MarkArea" })
                    return null;
                if (node is ListBoxItem row)
                    return row.Content as DiskEntry;
            }
            return null;
        }

        private void OpenSelected(bool animate = true)
        {
            if (_entries.SelectedItems.Count != 1 || !(_entries.SelectedItem is DiskEntry entry))
                return;
            if (entry.IsFolder)
                OpenLocation(new List<DiskEntry>(_trail) { entry }, animate);
            else
                OpenFileInExplorer(entry);
        }

        private ContextMenu BuildBubbleMenu(DiskEntry entry)
        {
            if (_trail.Count == 0 || !NativeDirectoryReader.IsEntryName(entry.Name))
                return null;
            string path = IOPath.Combine(CurrentPath(), entry.Name);
            MarkedEntry target = MarkFor(entry);
            var menu = new ContextMenu { PlacementTarget = _map, Placement = PlacementMode.MousePoint, FontSize = 14 };
            if (entry.IsFolder)
            {
                var open = new MenuItem { Header = "Open" };
                open.Click += (sender, args) => Activate(entry);
                menu.Items.Add(open);
            }
            var mark = new MenuItem { Header = entry.IsMarked ? "Unmark" : "Mark for cleanup", IsEnabled = entry.CanRecycle,
                ToolTip = entry.CanRecycle ? null : SystemProtection.Reason(entry) };
            mark.Click += (sender, args) =>
            {
                if (_trail.Count == 0 || !_trail[_trail.Count - 1].Children.Contains(entry)) return;
                SetMarked(entry, !entry.IsMarked);
                _entries.SelectedItem = entry;
                LinkEntry(entry);
            };
            menu.Items.Add(mark);
            var delete = new MenuItem { Header = "Move to Recycle Bin", IsEnabled = entry.CanRecycle,
                ToolTip = entry.CanRecycle ? "Move only this item to the Recycle Bin" : SystemProtection.Reason(entry) };
            delete.Click += async (sender, args) => await RecycleAsync(new[] { target }, review: false);
            menu.Items.Add(delete);
            menu.Items.Add(new Separator());
            var explorer = new MenuItem { Header = entry.IsFolder ? "Open in File Explorer" : "Show in File Explorer", ToolTip = path };
            explorer.Click += async (sender, args) => await OpenExplorerPathAsync(path, entry.IsFolder);
            menu.Items.Add(explorer);
            return menu;
        }

        private async void OpenFileInExplorer(DiskEntry entry)
        {
            if (NativeDirectoryReader.IsEntryName(entry.Name))
                await OpenExplorerPathAsync(IOPath.Combine(CurrentPath(), entry.Name), false);
        }

        private async Task OpenExplorerPathAsync(string path, bool folder)
        {
            try { await ExplorerShell.OpenAsync(path, folder, new WindowInteropHelper(this).Handle); }
            catch (Exception error) when (RecycleBin.IsItemError(error))
            {
                if (!_closed)
                    UiVisuals.Notice(this, "Can’t open this location", path + "\n\n" + error.Message).ShowDialog();
            }
        }

        private void OpenExplorer(string arguments)
        {
            try
            {
                string explorer = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(explorer, arguments);
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                UiVisuals.Notice(this, "Can’t open File Explorer", error.Message).ShowDialog();
            }
        }

        private void ShowPage(UIElement page)
        {
            foreach (UIElement child in _surface.Children)
                child.Visibility = child == page ? Visibility.Visible : Visibility.Collapsed;
            _folder.Visibility = page == _browser ? Visibility.Visible : Visibility.Collapsed;
            bool apps = page == _appCleaner;
            _drive.Visibility = page == _scanning || apps ? Visibility.Collapsed : Visibility.Visible;
            SetActiveMode(_diskMode, !apps);
            SetActiveMode(_appsMode, apps);
            if (page != _browser)
                _ambient.Focus(null);
        }

        private static string Counts(long files, long folders, long skipped) =>
            $"{CountLabel(files, "file")}  ·  {CountLabel(folders, "folder")}" + (skipped > 0 ? $"  ·  {skipped:N0} skipped" : string.Empty);

        private static string CountLabel(long count, string noun) => $"{count:N0} {noun}" + (count == 1 ? "" : "s");
    }
}
