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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using IOPath = System.IO.Path;

namespace DiskBubbles
{
    internal sealed partial class MainWindow : Window
    {
        private sealed class FolderView
        {
            public bool ShowOther;
            public double Offset;
        }

        private static readonly IComparer<DiskEntry> LargestFirst = Comparer<DiskEntry>.Create(DiskScanner.CompareEntries);
        private readonly Grid _surface = new Grid();
        private readonly Grid _browser = new Grid();
        private readonly ListBox _entries = new ListBox();
        private readonly BubbleMap _map = new BubbleMap();
        private readonly StackPanel _crumbs = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Button _folder = new Button { Content = "Open folder…" };
        private readonly Button _drive = new Button { Content = "Scan drive  ▾" };
        private readonly Button _recycleBin = new Button { Content = "Recycle Bin" };
        private readonly Button _appearance = new Button { Width = 36, Padding = new Thickness(8) };
        private readonly Button _up = new Button { Content = "↑  Up" };
        private readonly Button _back = new Button { ToolTip = "Back (Alt+Left)", Width = 36 };
        private readonly Button _forward = new Button { ToolTip = "Forward (Alt+Right)", Width = 36 };
        private readonly Button _rescan = new Button { Content = "Rescan" };
        private readonly Button _entryAction = new Button();
        private readonly Button _reviewRecycle = new Button();
        private readonly Border _selectionPane = new Border { Visibility = Visibility.Collapsed };
        private readonly TextBlock _selectionSummary = new TextBlock();
        private readonly Button _otherToggle = new Button();
        private readonly Button _cancel = new Button { Content = "Cancel" };
        private readonly TextBlock _path = new TextBlock();
        private readonly TextBlock _itemCount = new TextBlock();
        private readonly TextBlock _mapNote = new TextBlock();
        private readonly TextBlock _mapEmpty = new TextBlock();
        private readonly TextBlock _total = new TextBlock();
        private readonly TextBlock _status = new TextBlock();
        private readonly TextBlock _capacity = new TextBlock();
        private readonly ProgressBar _capacityBar = new ProgressBar { Height = 5, Maximum = 1 };
        private readonly StackPanel _capacityPanel = new StackPanel();
        private readonly TextBlock _scanCounts = new TextBlock();
        private readonly TextBlock _scanPath = new TextBlock();
        private readonly ProgressBar _spinner = new ProgressBar { Height = 6, Width = 288 };
        private readonly Grid _landing = new Grid();
        private readonly Grid _scanning = new Grid();
        private readonly List<StorageIllustration> _illustrations = new List<StorageIllustration>(2);
        private readonly Stack<List<DiskEntry>> _backHistory = new Stack<List<DiskEntry>>();
        private readonly Stack<List<DiskEntry>> _forwardHistory = new Stack<List<DiskEntry>>();
        private readonly ConditionalWeakTable<DiskEntry, FolderView> _folderViews = new ConditionalWeakTable<DiskEntry, FolderView>();
        private int _listingRevision;
        private List<DiskEntry> _trail = new List<DiskEntry>();
        private bool _showOther;
        private bool _recycleBusy;
        private bool _movingToBin;
        private ListBoxItem _linkedRow;
        private ScanResult _result;
        private string _rootPath;
        private CancellationTokenSource _scan;
        private CancellationTokenSource _recycleCheck;
        private bool _closed;

        public MainWindow()
        {
            Title = "Disk Bubbles";
            Width = 1200;
            Height = 800;
            MinWidth = 760;
            MinHeight = 530;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(Control.BackgroundProperty, "Base");
            SetResourceReference(Control.ForegroundProperty, "Ink");
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UiVisuals.ThemeCaption(this);

            var shell = new DockPanel();
            shell.SetResourceReference(Panel.BackgroundProperty, "Workspace");
            var header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            shell.Children.Add(header);
            _surface.Margin = new Thickness(24, 20, 24, 15);
            shell.Children.Add(_surface);
            Content = shell;
            BuildBrowser();
            BuildLanding();
            BuildScanning();
            _surface.Children.Add(_browser);
            _surface.Children.Add(_landing);
            _surface.Children.Add(_scanning);
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
            _otherToggle.Click += (sender, args) => ShowOther(!_showOther);
            _rescan.Click += async (sender, args) => await ScanAsync(_rootPath);
            _map.Activated += Activate;
            _map.BackgroundActivated += () => Navigate(_trail.Count - 2);
            _map.HoverChanged += entry =>
            {
                _linkedRow?.ClearValue(TagProperty);
                _linkedRow = entry == null ? null : _entries.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem;
                if (_linkedRow != null)
                    _linkedRow.Tag = "Linked";
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
                _map.RowHover = null;
                UpdateMapCaption();
            };
            _entryAction.Click += (sender, args) => OpenSelected();
            _reviewRecycle.Click += async (sender, args) => await ReviewRecycleAsync();
            _entries.SelectionChanged += (sender, args) => UpdateSelection();
            _entries.MouseDoubleClick += (sender, args) =>
            {
                if (ItemsControl.ContainerFromElement(_entries, args.OriginalSource as DependencyObject) is ListBoxItem)
                    OpenSelected();
            };
            _entries.KeyDown += (sender, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    OpenSelected(animate: false);
                    args.Handled = true;
                }
            };
            Closed += (sender, args) =>
            {
                _closed = true;
                _scan?.Cancel();
                _recycleCheck?.Cancel();
            };
            Closing += (sender, args) => args.Cancel = _movingToBin;
            PreviewKeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape && _recycleCheck != null)
                {
                    _recycleCheck.Cancel();
                    args.Handled = true;
                    return;
                }
                if (_recycleBusy || _browser.Visibility != Visibility.Visible || Keyboard.Modifiers != ModifierKeys.Alt || args.Key != Key.System)
                    return;
                if (args.SystemKey == Key.Left)
                    MoveInHistory(_backHistory, _forwardHistory, animate: false);
                else if (args.SystemKey == Key.Right)
                    MoveInHistory(_forwardHistory, _backHistory, animate: false);
                else if (args.SystemKey == Key.Up)
                    Navigate(_trail.Count - 2, animate: false);
                else
                    return;
                args.Handled = true;
            };
        }

        private void ShowDriveMenu()
        {
            var menu = new ContextMenu { PlacementTarget = _drive, Placement = PlacementMode.Bottom };
            foreach (var drive in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.Fixed || item.DriveType == DriveType.Removable))
            {
                string path = drive.RootDirectory.FullName;
                var item = new MenuItem { Header = path };
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
            if (_scan != null || _recycleBusy)
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
                _backHistory.Clear();
                _forwardHistory.Clear();
                _trail = new List<DiskEntry> { result.Root };
                RefreshBrowser();
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
                    ShowPage(_result == null ? _landing : _browser);
                    if (completed)
                        _map.Reveal();
                }
            }
        }

        private void RefreshBrowser(DiskEntry origin = null, bool returning = false, bool restoreView = false)
        {
            DiskEntry current = _trail[_trail.Count - 1];
            current.OrderChildren();
            _map.ShowEntries(current, origin, returning);
            FolderView view = restoreView && _folderViews.TryGetValue(current, out FolderView remembered) ? remembered : null;
            ShowOther(view?.ShowOther == true);
            if (view != null && view.Offset > 0)
            {
                int revision = _listingRevision;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (revision == _listingRevision)
                        ListingScrollViewer(_entries)?.ScrollToVerticalOffset(view.Offset);
                }));
            }
            _mapEmpty.Text = current.Children.Count == 0 ? "This folder is empty" : "No items with a measurable size";
            _mapEmpty.Visibility = _map.VisibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            _total.Text = current.SizeLabel;
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
                _capacity.Text = DiskEntry.FormatSize(used) + " used   ·   " + DiskEntry.FormatSize(_result.FreeBytes) + " free";
                _capacityBar.Value = (double)used / _result.VolumeBytes;
            }
            UpdateBreadcrumbs();
        }

        private void UpdateScanStatus()
        {
            _status.Text = Counts(_result.Files, _result.Folders, _result.Skipped) + $"  ·  {_result.Elapsed.TotalSeconds:0.00} s";
            if (_result.NeedsRefresh)
                _status.Text += "  ·  Rescan to update changed items";
            _status.ToolTip = _result.NeedsRefresh ? "A move or external change may have changed the scan totals."
                : _result.Skipped > 0 ? "Unreadable entries, unsupported names and filesystem links were skipped."
                : "Totals are based on file sizes.";
        }

        private void ShowOther(bool show)
        {
            _listingRevision++;
            _map.RowHover = null;
            List<DiskEntry> items = _trail[_trail.Count - 1].Children;
            _showOther = show && _map.Other != null;
            int otherCount = items.Count - _map.ListedCount;
            var visible = _showOther ? items.GetRange(_map.ListedCount, otherCount) : items;
            _entries.ItemsSource = visible;
            _entries.SelectedItem = null;
            if (visible.Count > 0)
                _entries.ScrollIntoView(visible[0]);
            _otherToggle.Visibility = _map.Other != null ? Visibility.Visible : Visibility.Collapsed;
            _otherToggle.Content = _showOther ? "Show all items" : $"Other items ({otherCount:N0})  ›";
            _itemCount.Text = _showOther ? $"{CountLabel(otherCount, "other item")} · {_map.Other.SizeLabel}"
                : items.Count > 1 ? $"{CountLabel(items.Count, "item")} · largest first" : CountLabel(items.Count, "item");
            UpdateSelection();
        }

        private void UpdateMapCaption()
        {
            if (_trail.Count == 0)
                return;

            DiskEntry entry = _map.Inspected ?? (_entries.SelectedItems.Count == 1 ? _entries.SelectedItem as DiskEntry : null);
            DiskEntry folder = _trail[_trail.Count - 1];
            string caption = _trail.Count == 1 ? "Click a folder bubble to open it" : "Double-click empty space to go up";
            if (entry != null)
            {
                caption = entry.Name + "  ·  " + entry.SizeLabel;
                if (folder.Bytes > 0)
                    caption += $"  ·  {((double)entry.Bytes / folder.Bytes):P1} of this folder";
                if (entry.IsSystem)
                    caption += "  ·  System";
            }
            if (_mapNote.Text == caption)
                return;

            _mapNote.Text = caption;
            _mapNote.ToolTip = entry == null ? "Click a folder bubble to open it. Double-click an empty part of the map to go up." : caption;
        }

        private void UpdateBreadcrumbs()
        {
            _crumbs.Children.Clear();
            for (int i = 0; i < _trail.Count; i++)
            {
                if (_trail.Count > 4 && i > 0 && i < _trail.Count - 3)
                {
                    if (i == 1)
                        _crumbs.Children.Add(Label("›  …  ", 14, "Subtle"));
                    continue;
                }

                if (i > 0)
                    _crumbs.Children.Add(Label("›", 14, "Subtle"));

                int level = i;
                string name = _trail[i].Name;
                bool current = i == _trail.Count - 1;
                string text = name.Length > 24 ? name.Substring(0, 23) + "…" : name;
                if (current)
                {
                    var crumb = Label(text, 13, "Ink", FontWeights.SemiBold);
                    crumb.Margin = new Thickness(8, 5, 0, 5);
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
            view.ShowOther = _showOther;
            view.Offset = ListingScrollViewer(_entries)?.VerticalOffset ?? 0;
            bool returning = next.Count < _trail.Count;
            var deeper = returning ? _trail : next;
            var shallower = returning ? next : _trail;
            DiskEntry origin = animate && deeper.Count > shallower.Count && deeper[shallower.Count - 1] == shallower[shallower.Count - 1]
                ? deeper[shallower.Count] : null;
            _trail = next;
            RefreshBrowser(origin, returning, restoreView: true);
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
            if (entry == _map.Other)
            {
                ShowOther(!_showOther);
                return;
            }
            if (entry.IsFolder)
            {
                OpenLocation(new List<DiskEntry>(_trail) { entry });
            }
            else
            {
                if (_showOther && !_entries.Items.Contains(entry))
                    ShowOther(false);
                _entries.UnselectAll();
                _entries.SelectedItem = entry;
                _entries.ScrollIntoView(entry);
            }
        }

        private void UpdateSelection()
        {
            var entry = _entries.SelectedItem as DiskEntry;
            int count = _entries.SelectedItems.Count;
            _map.SetSelection(entry ?? (_showOther ? _map.Other : null), count > 1 ? _entries.SelectedItems.Cast<DiskEntry>() : null);
            _entryAction.Visibility = count == 1 ? Visibility.Visible : Visibility.Collapsed;
            _entryAction.Content = entry?.IsFolder == true ? "Open selected folder" : "Show in File Explorer";
            _reviewRecycle.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _reviewRecycle.Content = count == 1 ? "Review for Recycle Bin" : $"Review {count:N0} items for Recycle Bin";
            _selectionPane.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _mapNote.Visibility = _map.VisibleCount > 0 ? Visibility.Visible : Visibility.Hidden;
            UpdateMapCaption();
            if (count > 0)
            {
                long bytes = count == 1 && entry != null ? entry.Bytes : _entries.SelectedItems.Cast<DiskEntry>()
                    .Aggregate(0L, (total, item) => DiskScanner.AddBytes(total, item.Bytes));
                _selectionSummary.Text = $"{count:N0} selected  ·  {DiskEntry.FormatSize(bytes)}";
            }
        }

        private void OpenSelected(bool animate = true)
        {
            if (_entries.SelectedItems.Count != 1 || !(_entries.SelectedItem is DiskEntry entry))
                return;

            if (entry.IsFolder)
            {
                OpenLocation(new List<DiskEntry>(_trail) { entry }, animate);
                return;
            }

            if (NativeDirectoryReader.IsEntryName(entry.Name))
                OpenExplorer("/select,\"" + IOPath.Combine(CurrentPath(), entry.Name) + "\"");
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

        private async Task ReviewRecycleAsync()
        {
            if (_recycleBusy || _scan != null || _entries.SelectedItems.Count == 0)
                return;

            DiskEntry[] selection = _entries.SelectedItems.Cast<DiskEntry>().ToArray();
            string folder = CurrentPath();
            bool refreshed = false;
            using var cancellation = new CancellationTokenSource();
            _recycleCheck = cancellation;
            SetRecycleBusy(true);
            _status.Text = "Checking selected items…  ·  Esc to cancel";
            try
            {
                List<RecycleItem> items = await Task.Run(() => RecycleItem.Prepare(folder, selection, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                _recycleCheck = null;
                if (_closed)
                    return;

                if (items.Any(item => item.NeedsRescan))
                    _result.NeedsRefresh = true;
                if (new RecycleReview(this, items, _trail[_trail.Count - 1], _result.Root).ShowDialog() != true)
                    return;

                _movingToBin = true;
                _status.Text = "Moving reviewed items to the Recycle Bin…";
                var reviewed = items.Where(item => item.Ready).ToArray();
                List<RecycleItem> moved = await RecycleItem.MoveAsync(reviewed);
                _movingToBin = false;
                var failures = reviewed.Where(item => !item.Ready).ToArray();
                if (failures.Any(item => item.NeedsRescan) || (moved.Count > 0 && ScannedRecycleBin()))
                    _result.NeedsRefresh = true;
                if (moved.Count > 0)
                {
                    ApplyRecycled(moved);
                    refreshed = true;
                    _status.Text += $"  ·  {moved.Count:N0} moved to Recycle Bin";
                }

                if (failures.Length > 0)
                    new RecycleReview(this, failures, _trail[_trail.Count - 1], _result.Root, completed: true).ShowDialog();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error) when (DiskScanner.IsFileSystemError(error) || error is ArgumentException)
            {
                if (!_closed)
                    UiVisuals.Notice(this, "Can’t review these items", error.Message).ShowDialog();
            }
            finally
            {
                _recycleCheck = null;
                _movingToBin = false;
                if (!_closed)
                {
                    SetRecycleBusy(false);
                    if (!refreshed)
                        UpdateScanStatus();
                }
            }
        }

        private void SetRecycleBusy(bool busy)
        {
            _recycleBusy = busy;
            _browser.IsEnabled = _folder.IsEnabled = _drive.IsEnabled = !busy;
        }

        private bool ScannedRecycleBin() =>
            string.Equals(_rootPath, IOPath.GetPathRoot(_rootPath), StringComparison.OrdinalIgnoreCase) &&
            _result.Root.Children.Any(entry => entry.Name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase));

        private void ApplyRecycled(IReadOnlyList<RecycleItem> moved)
        {
            _entries.ItemsSource = null;
            var removed = new HashSet<DiskEntry>(moved.Select(item => item.Entry));
            DiskEntry current = _trail[_trail.Count - 1];
            current.Children.RemoveAll(removed.Contains);
            long movedBytes = moved.Aggregate(0L, (total, item) => DiskScanner.AddBytes(total, item.Entry.Bytes));
            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                DiskEntry folder = _trail[i];
                if (i + 1 < _trail.Count)
                {
                    DiskEntry changed = _trail[i + 1];
                    folder.Children.Remove(changed);
                    int index = folder.Children.BinarySearch(changed, LargestFirst);
                    folder.Children.Insert(index < 0 ? ~index : index, changed);
                }

                folder.Bytes = folder.Bytes == long.MaxValue
                    ? folder.Children.Aggregate(0L, (total, child) => DiskScanner.AddBytes(total, child.Bytes))
                    : Math.Max(0, folder.Bytes - movedBytes);
            }

            _result.Files -= moved.Sum(item => item.Files);
            _result.Folders -= moved.Sum(item => item.Folders);
            PruneHistory(_backHistory, removed);
            PruneHistory(_forwardHistory, removed);
            _map.InvalidateLayout(_trail);
            RefreshBrowser();
        }

        private static void PruneHistory(Stack<List<DiskEntry>> history, HashSet<DiskEntry> removed)
        {
            List<DiskEntry>[] valid = history.Where(path => !path.Any(removed.Contains)).Reverse().ToArray();
            history.Clear();
            foreach (List<DiskEntry> path in valid)
                history.Push(path);
        }

        private void ShowPage(UIElement page)
        {
            foreach (UIElement child in _surface.Children)
                child.Visibility = child == page ? Visibility.Visible : Visibility.Collapsed;
            _folder.Visibility = page == _browser ? Visibility.Visible : Visibility.Collapsed;
            _drive.Visibility = page == _scanning ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string Counts(long files, long folders, long skipped) =>
            $"{CountLabel(files, "file")}  ·  {CountLabel(folders, "folder")}" + (skipped > 0 ? $"  ·  {skipped:N0} skipped" : string.Empty);

        private static string CountLabel(long count, string noun) => $"{count:N0} {noun}" + (count == 1 ? "" : "s");


    }
}
