using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using IOPath = System.IO.Path;

namespace DiskBubbles
{
    internal sealed partial class MainWindow
    {
        private Border BuildHeader()
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());

            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Mark(29));
            var title = Label("Disk Bubbles", 18, "Ink", FontWeights.SemiBold);
            title.Margin = new Thickness(9, 0, 0, 0);
            title.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(title);
            header.Children.Add(brand);

            var modes = BuildModeControl();
            Grid.SetColumn(modes, 1);
            header.Children.Add(modes);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center };
            var quiet = (Style)FindResource("QuietButton");
            foreach (var button in new[] { _folder, _drive, _recycleBin, _appearance })
            {
                button.Style = quiet;
                button.Width = 36;
                button.Padding = new Thickness(7);
                button.Margin = new Thickness(1, 0, 1, 0);
                actions.Children.Add(button);
            }
            SetToolbarIcon(_folder, UiVisuals.Folder, "Choose a folder to scan");
            SetToolbarIcon(_drive, UiVisuals.Drive, "Choose a drive to scan");
            SetToolbarIcon(_recycleBin, UiVisuals.Bin, "Open Recycle Bin");
            UpdateAppearanceButton();
            _appearance.Click += (sender, args) =>
            {
                ChangeAppearance(!Appearance.IsLight);
                Appearance.SavePreference();
            };
            Grid.SetColumn(actions, 2);
            header.Children.Add(actions);
            return UiVisuals.Surface(new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 10, 24, 10), Child = header,
            }, "Header", "Line");
        }

        private static void SetToolbarIcon(Button button, string icon, string label)
        {
            button.Content = UiVisuals.Icon(icon, "Muted", 17);
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
        }

        private void ChangeAppearance(bool light)
        {
            Appearance.Apply(light);
            UiVisuals.UpdateCaption(this);
            UpdateAppearanceButton();
            _map.UpdateAppearance();
            foreach (StorageIllustration illustration in _illustrations)
                illustration.InvalidateVisual();
        }

        private Border BuildModeControl()
        {
            var modes = new UniformGrid { Rows = 1, Columns = 2 };
            foreach (Button button in new[] { _diskMode, _appsMode })
            {
                button.Style = (Style)FindResource("QuietButton");
                button.Width = 108;
                button.MinHeight = 34;
                button.Padding = new Thickness(10, 5, 10, 5);
                modes.Children.Add(button);
            }
            _diskMode.Click += (sender, args) => ShowPage(_scan != null ? _scanning : _result == null ? _landing : _browser);
            _appsMode.Click += async (sender, args) =>
            {
                ShowPage(_appCleaner);
                await _appCleaner.OpenAsync();
            };
            return UiVisuals.Surface(new Border { Child = modes, Padding = new Thickness(3), CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }, "Panel", "Line");
        }

        private static void SetActiveMode(Button button, bool selected)
        {
            if (selected)
            {
                button.SetResourceReference(Control.BackgroundProperty, "Raised");
                button.SetResourceReference(Control.BorderBrushProperty, "Line");
            }
            else
            {
                button.ClearValue(Control.BackgroundProperty);
                button.ClearValue(Control.BorderBrushProperty);
            }
            button.SetResourceReference(Control.ForegroundProperty, selected ? "Ink" : "Muted");
            AutomationProperties.SetItemStatus(button, selected ? "Current mode" : "");
        }

        private void UpdateAppearanceButton()
        {
            string action = Appearance.IsLight ? "Switch to dark mode" : "Switch to light mode";
            _appearance.Content = UiVisuals.Icon(Appearance.IsLight ? UiVisuals.Moon : UiVisuals.Sun, "Muted", 17);
            _appearance.ToolTip = action;
            AutomationProperties.SetName(_appearance, action);
        }

        private void BuildBrowser()
        {
            _browser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _browser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _browser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var navigation = new DockPanel { Margin = new Thickness(3, 0, 3, 17) };
            var history = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0) };
            _back.Content = UiVisuals.Icon(UiVisuals.Back, "Ink");
            _forward.Content = UiVisuals.Icon(UiVisuals.Forward, "Ink");
            _up.Content = UiVisuals.Icon(UiVisuals.Up, "Ink");
            _up.ToolTip = "Up one folder (Alt+Up)";
            _up.Width = 36;
            _back.Padding = _forward.Padding = _up.Padding = new Thickness(7);
            _back.Margin = _forward.Margin = new Thickness(0, 0, 5, 0);
            _rescan.Content = UiVisuals.WithIcon("Rescan", UiVisuals.Refresh, "Muted");
            AutomationProperties.SetName(_rescan, "Rescan this location");
            history.Children.Add(_back);
            history.Children.Add(_forward);
            history.Children.Add(_up);
            DockPanel.SetDock(history, Dock.Left);
            DockPanel.SetDock(_rescan, Dock.Right);
            _up.VerticalAlignment = _rescan.VerticalAlignment = VerticalAlignment.Center;
            navigation.Children.Add(history);
            navigation.Children.Add(_rescan);
            var location = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            _crumbs.MinHeight = 27;
            _crumbs.VerticalAlignment = VerticalAlignment.Center;
            var crumbScroll = new ScrollViewer
            {
                Content = _crumbs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            location.Children.Add(crumbScroll);
            FormatLabel(_path, 11, "Muted");
            _path.Margin = new Thickness(8, 1, 0, 0);
            location.Children.Add(_path);
            navigation.Children.Add(location);
            _browser.Children.Add(navigation);

            var panels = new Grid();
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 278, MaxWidth = 384 });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.32, GridUnitType.Star) });
            var itemPanel = UiVisuals.Surface(new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                CornerRadius = new CornerRadius(15, 0, 0, 15), Child = BuildEntries(),
            }, "GlassPane", "Line");
            panels.Children.Add(itemPanel);
            var storageMap = UiVisuals.Surface(new Border { Child = BuildMap(),
                CornerRadius = new CornerRadius(0, 15, 15, 0) }, "GlassClear");
            Grid.SetColumn(storageMap, 1);
            panels.Children.Add(storageMap);
            var workspace = Card(panels, "Chart");
            Grid.SetRow(workspace, 1);
            _browser.Children.Add(workspace);

            var footer = new DockPanel { Margin = new Thickness(3, 14, 3, 1) };
            FormatLabel(_status, 12, "Muted");
            _status.TextAlignment = TextAlignment.Right;
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(20, 0, 0, 0);
            _capacityPanel.Width = 205;
            FormatLabel(_capacity, 11, "Ink", FontWeights.SemiBold);
            _capacityBar.SetResourceReference(Control.ForegroundProperty, "Accent");
            _capacityBar.SetResourceReference(Control.BackgroundProperty, "Line");
            _capacityBar.Template = (ControlTemplate)FindResource("VolumeMeter");
            _capacityBar.Margin = new Thickness(0, 6, 0, 0);
            _capacityPanel.Children.Add(_capacity);
            _capacityPanel.Children.Add(_capacityBar);
            _capacityPanel.ToolTip = "Space Windows reports for this account. Disk quotas can affect these values.";
            AutomationProperties.SetName(_capacityBar, "Volume used space");
            DockPanel.SetDock(_capacityPanel, Dock.Left);
            footer.Children.Add(_capacityPanel);
            var cleanup = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
            FormatLabel(_selectionSummary, 12, "Ink", FontWeights.SemiBold);
            _selectionSummary.VerticalAlignment = VerticalAlignment.Center;
            cleanup.Children.Add(_selectionSummary);
            _clearMarks.Style = (Style)FindResource("QuietButton");
            _clearMarks.Margin = new Thickness(6, 0, 4, 0);
            AutomationProperties.SetName(_clearMarks, "Clear all marked items");
            cleanup.Children.Add(_clearMarks);
            _reviewRecycle.Content = UiVisuals.Icon(UiVisuals.Bin, "AccentInk", 17);
            _reviewRecycle.ToolTip = "Review marked items for the Recycle Bin";
            _reviewRecycle.Width = 38;
            _reviewRecycle.Padding = new Thickness(7);
            UiVisuals.Emphasize(_reviewRecycle);
            AutomationProperties.SetName(_reviewRecycle, "Review marked items for the Recycle Bin");
            cleanup.Children.Add(_reviewRecycle);
            DockPanel.SetDock(cleanup, Dock.Right);
            footer.Children.Add(cleanup);
            footer.Children.Add(_status);
            Grid.SetRow(footer, 2);
            _browser.Children.Add(footer);
        }

        private DockPanel BuildEntries()
        {
            var sidebar = new DockPanel { Margin = new Thickness(12, 16, 9, 12) };
            var heading = new StackPanel { Margin = new Thickness(8, 0, 9, 11) };
            heading.Children.Add(Label("Folders and files", 18, "Ink", FontWeights.SemiBold));
            FormatLabel(_itemCount, 12, "Muted");
            _itemCount.Margin = new Thickness(0, 4, 0, 0);
            heading.Children.Add(_itemCount);
            DockPanel.SetDock(heading, Dock.Top);
            sidebar.Children.Add(heading);

            _entries.Background = Brushes.Transparent;
            _entries.BorderThickness = new Thickness(0);
            _entries.ItemContainerStyle = (Style)FindResource("EntryRow");
            _entries.ItemTemplate = (DataTemplate)FindResource("EntryTemplate");
            _entries.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            _entries.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            AutomationProperties.SetName(_entries, "Folders and files, largest first");
            sidebar.Children.Add(_entries);
            return sidebar;
        }

        private DockPanel BuildMap()
        {
            var chart = new DockPanel();
            var heading = new DockPanel { Margin = new Thickness(24, 16, 24, 10) };
            var size = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            FormatLabel(_total, 27, "Ink", FontWeights.SemiBold);
            _total.TextAlignment = TextAlignment.Right;
            size.Children.Add(_total);
            var sizeLabel = Label("Scanned size", 11, "Muted");
            sizeLabel.TextAlignment = TextAlignment.Right;
            size.Children.Add(sizeLabel);
            size.ToolTip = "Combined file sizes in this folder. Allocated disk space can differ.";
            DockPanel.SetDock(size, Dock.Right);
            heading.Children.Add(size);

            var name = new StackPanel { Margin = new Thickness(0, 3, 12, 0) };
            name.Children.Add(Label("Storage map", 21, "Ink", FontWeights.SemiBold));
            FormatLabel(_mapNote, 12, "Muted");
            _mapNote.Margin = new Thickness(0, 5, 0, 0);
            name.Children.Add(_mapNote);
            heading.Children.Add(name);
            DockPanel.SetDock(heading, Dock.Top);
            chart.Children.Add(heading);

            _mapPager.Orientation = Orientation.Horizontal;
            _mapPager.HorizontalAlignment = HorizontalAlignment.Center;
            _mapPager.Margin = new Thickness(0, 0, 0, 10);
            _mapPrevious.Content = UiVisuals.Icon(UiVisuals.Back, "Ink", 12);
            _mapNext.Content = UiVisuals.Icon(UiVisuals.Forward, "Ink", 12);
            _mapPrevious.ToolTip = "Previous bubbles";
            _mapNext.ToolTip = "Next bubbles";
            AutomationProperties.SetName(_mapPrevious, "Previous bubbles");
            AutomationProperties.SetName(_mapNext, "Next bubbles");
            foreach (var button in new[] { _mapPrevious, _mapNext })
            {
                button.Style = (Style)FindResource("QuietButton");
                button.MinHeight = 32;
                button.Width = 36;
                button.Padding = new Thickness(5);
            }
            FormatLabel(_mapRange, 12, "Muted");
            _mapRange.Margin = new Thickness(8, 0, 8, 0);
            _mapRange.MinWidth = 76;
            _mapRange.TextAlignment = TextAlignment.Center;
            _mapRange.VerticalAlignment = VerticalAlignment.Center;
            _mapRange.ToolTip = "Sorted by size. Each item has a bubble. Small items use a minimum visible size.";
            _mapPager.Children.Add(_mapPrevious);
            _mapPager.Children.Add(_mapRange);
            _mapPager.Children.Add(_mapNext);
            DockPanel.SetDock(_mapPager, Dock.Bottom);
            chart.Children.Add(_mapPager);

            var plot = new Grid();
            plot.Children.Add(_map);
            FormatLabel(_mapEmpty, 14, "Muted");
            _mapEmpty.VerticalAlignment = VerticalAlignment.Center;
            _mapEmpty.HorizontalAlignment = HorizontalAlignment.Center;
            _mapEmpty.IsHitTestVisible = false;
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false };
            var emptyGlyph = UiVisuals.Icon(UiVisuals.Folder, "Subtle", 22);
            empty.Children.Add(UiVisuals.Surface(new Border
            {
                Width = 54, Height = 54, Child = emptyGlyph,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 0, 13),
            }, "Raised", "Line"));
            empty.Children.Add(_mapEmpty);
            empty.SetBinding(VisibilityProperty, new Binding("Visibility") { Source = _mapEmpty });
            plot.Children.Add(empty);
            chart.Children.Add(plot);
            return chart;
        }

        private void BuildLanding()
        {
            var body = new StackPanel();
            var heading = Label("See where your space went.", 38, "Ink", FontWeights.SemiBold);
            heading.TextAlignment = TextAlignment.Center;
            body.Children.Add(heading);
            var hint = Label("Scan a drive or folder to see what’s taking up space.", 15, "Muted");
            hint.Margin = new Thickness(0, 9, 0, 0);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.TextAlignment = TextAlignment.Center;
            body.Children.Add(hint);
            string systemDrive = IOPath.GetPathRoot(Environment.SystemDirectory);
            var start = new Button { Content = UiVisuals.WithIcon("Scan " + systemDrive.TrimEnd('\\'), UiVisuals.Drive, "AccentInk"),
                MinHeight = 42, Margin = new Thickness(0, 0, 4, 0) };
            AutomationProperties.SetName(start, "Scan " + systemDrive.TrimEnd('\\'));
            UiVisuals.Emphasize(start);
            start.Click += async (sender, args) => await ScanAsync(systemDrive);
            var choose = new Button { Content = "Choose folder…", MinHeight = 42, Margin = new Thickness(4, 0, 0, 0) };
            choose.Click += async (sender, args) => await ChooseFolderAsync();
            var actions = new UniformGrid { Rows = 1, Columns = 2, Width = 296,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
            actions.Children.Add(start);
            actions.Children.Add(choose);
            body.Children.Add(actions);
            BuildIntro(_landing, body);
        }

        private void BuildScanning()
        {
            var body = new StackPanel();
            var heading = Label("Scanning storage", 34, "Ink", FontWeights.SemiBold);
            heading.TextAlignment = TextAlignment.Center;
            body.Children.Add(heading);
            FormatLabel(_scanCounts, 13, "Muted");
            _scanCounts.TextAlignment = TextAlignment.Center;
            _scanCounts.Margin = new Thickness(0, 10, 0, 10);
            FormatLabel(_scanPath, 13, "Muted");
            _scanPath.TextAlignment = TextAlignment.Center;
            _scanPath.Margin = new Thickness(0, 0, 0, 18);
            _scanPath.SetBinding(ToolTipProperty, new Binding("Text") { Source = _scanPath });
            body.Children.Add(_scanCounts);
            body.Children.Add(_scanPath);
            _spinner.SetResourceReference(Control.ForegroundProperty, "Accent");
            _spinner.SetResourceReference(Control.BackgroundProperty, "Line");
            _spinner.HorizontalAlignment = HorizontalAlignment.Center;
            _spinner.Template = (ControlTemplate)FindResource("ScanMeter");
            AutomationProperties.SetName(_spinner, "Scan in progress");
            body.Children.Add(_spinner);
            _cancel.HorizontalAlignment = HorizontalAlignment.Center;
            _cancel.Margin = new Thickness(0, 24, 0, 0);
            _cancel.Click += (sender, args) =>
            {
                _cancel.IsEnabled = false;
                _scanCounts.Text = "Stopping…";
                _scan?.Cancel();
            };
            body.Children.Add(_cancel);
            BuildIntro(_scanning, body);
        }

        private void BuildIntro(Grid page, StackPanel body)
        {
            var intro = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 560, Margin = new Thickness(16, 12, 16, 20) };
            var art = new StorageIllustration { IsHitTestVisible = false, Width = 460, Height = 174,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
            _illustrations.Add(art);
            intro.Children.Add(art);
            intro.Children.Add(body);
            page.Children.Add(intro);
        }

        private TextBlock Label(string text, double size, string color, FontWeight? weight = null)
        {
            var label = new TextBlock { Text = text };
            FormatLabel(label, size, color, weight);
            return label;
        }

        private void FormatLabel(TextBlock label, double size, string color, FontWeight? weight = null)
        {
            label.FontSize = size;
            label.SetResourceReference(TextBlock.ForegroundProperty, color);
            label.FontWeight = weight ?? FontWeights.Normal;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
        }

        private Border Card(UIElement content, string fill) => UiVisuals.Surface(new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Child = content,
        }, fill, "GlassEdge");

        private Canvas Mark(double size)
        {
            var canvas = new Canvas { Width = size, Height = size };
            AddDot(0.04, 0.11, 0.64);
            AddDot(0.69, 0.32, 0.26);
            AddDot(0.55, 0.69, 0.24);
            return canvas;

            void AddDot(double x, double y, double diameter)
            {
                var dot = new Ellipse
                {
                    Width = size * diameter, Height = size * diameter,
                    StrokeThickness = 1.2,
                };
                dot.SetResourceReference(Shape.FillProperty, "LogoFill");
                dot.SetResourceReference(Shape.StrokeProperty, "LogoEdge");
                Canvas.SetLeft(dot, x * size);
                Canvas.SetTop(dot, y * size);
                canvas.Children.Add(dot);
            }
        }
    }
}
