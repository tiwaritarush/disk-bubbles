using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
            var header = new DockPanel { LastChildFill = true };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _recycleBin.Style = (Style)FindResource("QuietButton");
            _recycleBin.Content = UiVisuals.WithIcon("Recycle Bin", UiVisuals.Bin, "Muted");
            AutomationProperties.SetName(_recycleBin, "Open Recycle Bin");
            _recycleBin.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(_recycleBin);
            _folder.Content = UiVisuals.WithIcon("Open folder…", UiVisuals.Folder, "Muted");
            AutomationProperties.SetName(_folder, "Open folder to scan");
            _folder.Margin = new Thickness(0, 0, 8, 0);
            _drive.Content = UiVisuals.WithIcon("Scan drive  ▾", UiVisuals.Drive, "Muted");
            AutomationProperties.SetName(_drive, "Choose a drive to scan");
            actions.Children.Add(_folder);
            actions.Children.Add(_drive);
            _appearance.Margin = new Thickness(8, 0, 0, 0);
            _appearance.Style = (Style)FindResource("QuietButton");
            UpdateAppearanceButton();
            _appearance.Click += (sender, args) =>
            {
                ChangeAppearance(!Appearance.IsLight);
                Appearance.SavePreference();
            };
            actions.Children.Add(_appearance);
            DockPanel.SetDock(actions, Dock.Right);
            header.Children.Add(actions);

            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Mark(33));
            var title = Label("Disk Bubbles", 20, "Ink", FontWeights.SemiBold);
            title.Margin = new Thickness(12, 0, 0, 0);
            title.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(title);
            header.Children.Add(brand);
            return UiVisuals.Surface(new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 12, 24, 12), Child = header,
            }, "Header", "GlassEdge");
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
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 263, MaxWidth = 384 });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.32, GridUnitType.Star) });
            var itemPanel = UiVisuals.Surface(new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                CornerRadius = new CornerRadius(15, 0, 0, 15), Child = BuildEntries(),
            }, "Panel", "Line");
            panels.Children.Add(itemPanel);
            var storageMap = BuildMap();
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
            _capacityPanel.Width = 281;
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

            var actions = new StackPanel();
            FormatLabel(_selectionSummary, 12, "Ink", FontWeights.SemiBold);
            _selectionSummary.Margin = new Thickness(5, 0, 5, 9);
            actions.Children.Add(_selectionSummary);
            UiVisuals.Emphasize(_reviewRecycle);
            actions.Children.Add(_reviewRecycle);
            _entryAction.Margin = new Thickness(0, 6, 0, 0);
            actions.Children.Add(_entryAction);
            _selectionPane.Margin = new Thickness(1, 8, 4, 0);
            _selectionPane.Padding = new Thickness(0, 10, 0, 0);
            _selectionPane.SetResourceReference(Border.BorderBrushProperty, "Line");
            _selectionPane.BorderThickness = new Thickness(0, 1, 0, 0);
            _selectionPane.Child = actions;
            DockPanel.SetDock(_selectionPane, Dock.Bottom);
            sidebar.Children.Add(_selectionPane);
            DockPanel.SetDock(_otherToggle, Dock.Bottom);
            _otherToggle.Margin = new Thickness(1, 9, 4, 0);
            _otherToggle.Style = (Style)FindResource("QuietButton");
            sidebar.Children.Add(_otherToggle);

            _entries.Background = Brushes.Transparent;
            _entries.BorderThickness = new Thickness(0);
            _entries.ItemContainerStyle = (Style)FindResource("EntryRow");
            _entries.ItemTemplate = (DataTemplate)FindResource("EntryTemplate");
            _entries.SelectionMode = SelectionMode.Extended;
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
            var heading = Label("See what’s taking\nup space.", 48, "Ink", FontWeights.SemiBold);
            heading.TextTrimming = TextTrimming.None;
            heading.TextWrapping = TextWrapping.Wrap;
            heading.LineHeight = 53;
            heading.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            SizeChanged += (sender, args) =>
            {
                bool compact = ActualWidth < 900;
                heading.Text = compact ? "What’s taking\nup space?" : "See what’s taking\nup space.";
                heading.FontSize = compact ? 37 : 48;
                heading.LineHeight = compact ? 43 : 53;
            };
            body.Children.Add(heading);
            var hint = Label("Scan a drive or folder. Open the largest items directly from the map.", 16, "Muted");
            hint.Margin = new Thickness(0, 20, 0, 0);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.TextTrimming = TextTrimming.None;
            hint.MaxWidth = 322;
            hint.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(hint);
            string systemDrive = IOPath.GetPathRoot(Environment.SystemDirectory);
            var start = new Button { Content = UiVisuals.WithIcon("Scan " + systemDrive.TrimEnd('\\'), UiVisuals.Drive, "AccentInk"),
                MinHeight = 44, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(start, "Scan " + systemDrive.TrimEnd('\\'));
            UiVisuals.Emphasize(start);
            start.Click += async (sender, args) => await ScanAsync(systemDrive);
            var choose = new Button { Content = "Choose folder…", MinHeight = 44 };
            choose.Click += async (sender, args) => await ChooseFolderAsync();
            var actions = new WrapPanel { Margin = new Thickness(0, 26, 0, 0) };
            actions.Children.Add(start);
            actions.Children.Add(choose);
            body.Children.Add(actions);
            BuildIntro(_landing, body);
        }

        private void BuildScanning()
        {
            var body = new StackPanel();
            var heading = Label("Scanning storage", 37, "Ink", FontWeights.SemiBold);
            body.Children.Add(heading);
            FormatLabel(_scanCounts, 13, "Muted");
            _scanCounts.Margin = new Thickness(0, 10, 0, 10);
            FormatLabel(_scanPath, 13, "Muted");
            _scanPath.Margin = new Thickness(0, 0, 0, 18);
            _scanPath.SetBinding(ToolTipProperty, new Binding("Text") { Source = _scanPath });
            body.Children.Add(_scanCounts);
            body.Children.Add(_scanPath);
            _spinner.SetResourceReference(Control.ForegroundProperty, "Accent");
            _spinner.SetResourceReference(Control.BackgroundProperty, "Line");
            _spinner.HorizontalAlignment = HorizontalAlignment.Left;
            _spinner.Template = (ControlTemplate)FindResource("ScanMeter");
            AutomationProperties.SetName(_spinner, "Scan in progress");
            body.Children.Add(_spinner);
            _cancel.HorizontalAlignment = HorizontalAlignment.Left;
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
            var layout = new Grid { ClipToBounds = true };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 338 });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.12, GridUnitType.Star) });
            body.VerticalAlignment = VerticalAlignment.Center;
            body.Margin = new Thickness(42, 20, 12, 20);
            layout.Children.Add(body);
            var art = new StorageIllustration { IsHitTestVisible = false, Margin = new Thickness(2, 18, 23, 18) };
            _illustrations.Add(art);
            Grid.SetColumn(art, 1);
            layout.Children.Add(art);
            page.Children.Add(Card(layout, "Hero"));
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
