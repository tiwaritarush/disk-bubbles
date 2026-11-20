using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using IOPath = System.IO.Path;

namespace DiskBubbles
{
    internal sealed partial class MainWindow
    {
        private Border BuildHeader()
        {
            var header = new Grid { Margin = new Thickness(14, 7, 12, 7) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());

            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var mark = new BrandMark { Width = 34, Height = 30 };
            _artwork.Add(mark);
            brand.Children.Add(mark);
            var title = Label("Disk Bubbles", 19, "Ink", FontWeights.SemiBold);
            title.Margin = new Thickness(10, 0, 0, 1);
            title.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(title);
            header.Children.Add(brand);

            var modes = BuildModeControl();
            Grid.SetColumn(modes, 1);
            header.Children.Add(modes);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center };
            var toolbarAction = (Style)FindResource("ToolbarAction");
            foreach (var button in new[] { _folder, _drive, _recycleBin, _appearance })
            {
                button.Style = toolbarAction;
                actions.Children.Add(button);
            }
            SetToolbarIcon(_folder, UiVisuals.Folder, "Scan a folder");
            SetToolbarIcon(_drive, UiVisuals.Drive, "Scan a drive");
            SetToolbarIcon(_recycleBin, UiVisuals.Bin, "Open the Recycle Bin");
            UpdateAppearanceButton();
            _appearance.Click += (sender, args) =>
            {
                ChangeAppearance(!Appearance.IsLight);
                Appearance.SavePreference();
            };
            Grid.SetColumn(actions, 2);
            header.Children.Add(actions);
            var toolbar = UiVisuals.FrostedPane(header, "GlassPane", 20);
            toolbar.Margin = new Thickness(22, 12, 22, 0);
            return toolbar;
        }

        private static void SetToolbarIcon(Button button, string icon, string label)
        {
            button.Content = UiVisuals.Icon(icon, "Muted", 19);
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
        }

        private void ChangeAppearance(bool light)
        {
            Appearance.Apply(light);
            UiVisuals.UpdateCaption(this);
            UpdateAppearanceButton();
            _ambient.InvalidateVisual();
            _map.UpdateAppearance();
            _completed.UpdateAppearance();
            foreach (FrameworkElement artwork in _artwork)
                artwork.InvalidateVisual();
        }

        private Border BuildModeControl()
        {
            var modes = new UniformGrid { Rows = 1, Columns = 2 };
            foreach (Button button in new[] { _diskMode, _appsMode })
            {
                button.Style = (Style)FindResource("HeaderButton");
                button.Width = 120;
                button.MinHeight = 36;
                button.Padding = new Thickness(10, 5, 10, 5);
                button.Margin = new Thickness(1, 0, 1, 0);
                modes.Children.Add(button);
            }
            _diskMode.Click += (sender, args) => ShowPage(_scan != null ? _scanning : _result == null ? _landing : _browser);
            _appsMode.Click += async (sender, args) =>
            {
                ShowPage(_appCleaner);
                await _appCleaner.OpenAsync();
            };
            return UiVisuals.Surface(new Border { Child = modes, Padding = new Thickness(3), CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }, "ModeTrack", "GlassInner");
        }

        private static void SetActiveMode(Button button, bool selected)
        {
            if (selected)
                button.SetResourceReference(Control.BackgroundProperty, "ModeSelection");
            else
                button.ClearValue(Control.BackgroundProperty);
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            button.SetResourceReference(Control.ForegroundProperty, selected ? "Ink" : "Muted");
            AutomationProperties.SetItemStatus(button, selected ? "Current mode" : "");
        }

        private void UpdateAppearanceButton()
        {
            string action = Appearance.IsLight ? "Switch to dark mode" : "Switch to light mode";
            SetToolbarIcon(_appearance, Appearance.IsLight ? UiVisuals.Moon : UiVisuals.Sun, action);
        }

        private void BuildBrowser()
        {
            _browser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _browser.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _browser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var navigation = new DockPanel { Margin = new Thickness(2, 0, 2, 14) };
            var history = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0) };
            _back.Content = UiVisuals.Icon(UiVisuals.Back, "Ink", 15);
            _forward.Content = UiVisuals.Icon(UiVisuals.Forward, "Ink", 15);
            _up.Content = UiVisuals.Icon(UiVisuals.Up, "Ink", 15);
            foreach (var button in new[] { _back, _forward, _up })
            {
                button.Style = (Style)FindResource("RoundButton");
                history.Children.Add(button);
            }
            _rescan.Content = UiVisuals.WithIcon("Rescan", UiVisuals.Refresh, "Muted");
            _rescan.Style = (Style)FindResource("GlassButton");
            AutomationProperties.SetName(_rescan, "Rescan this location");
            DockPanel.SetDock(history, Dock.Left);
            DockPanel.SetDock(_rescan, Dock.Right);
            _rescan.VerticalAlignment = VerticalAlignment.Center;
            navigation.Children.Add(history);
            navigation.Children.Add(_rescan);
            var location = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            _crumbs.Height = 30;
            _crumbs.VerticalAlignment = VerticalAlignment.Center;
            location.Children.Add(new ScrollViewer
            {
                Content = _crumbs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            FormatLabel(_path, 12.5, "Muted");
            _path.Margin = new Thickness(8, 0, 0, 0);
            location.Children.Add(_path);
            navigation.Children.Add(location);
            _browser.Children.Add(navigation);

            var panels = new Grid();
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 290, MaxWidth = 440 });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            panels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.0, GridUnitType.Star) });
            panels.Children.Add(UiVisuals.FrostedPane(BuildEntries()));
            var storageMap = UiVisuals.FrostedPane(BuildMap(), "GlassClear");
            Grid.SetColumn(storageMap, 2);
            panels.Children.Add(storageMap);
            Grid.SetRow(panels, 1);
            _browser.Children.Add(panels);
            Grid.SetRow(_work, 1);
            _browser.Children.Add(_work);

            var footer = new DockPanel { Margin = new Thickness(4, 12, 2, 0), MinHeight = 50, LastChildFill = true };
            _capacityPanel.Width = 236;
            _capacityPanel.VerticalAlignment = VerticalAlignment.Center;
            FormatLabel(_capacity, 12.5, "Ink", FontWeights.SemiBold);
            _capacityBar.SetResourceReference(Control.ForegroundProperty, "Accent");
            _capacityBar.SetResourceReference(Control.BackgroundProperty, "Line");
            _capacityBar.Template = (ControlTemplate)FindResource("VolumeMeter");
            _capacityBar.Margin = new Thickness(0, 7, 0, 0);
            _capacityPanel.Children.Add(_capacity);
            _capacityPanel.Children.Add(_capacityBar);
            _capacityPanel.ToolTip = "Space Windows reports for this drive. Disk quotas can affect these values.";
            AutomationProperties.SetName(_capacityBar, "Drive used space");
            DockPanel.SetDock(_capacityPanel, Dock.Left);
            footer.Children.Add(_capacityPanel);

            BuildSelectionTray();
            DockPanel.SetDock(_selectionTray, Dock.Right);
            footer.Children.Add(_selectionTray);
            FormatLabel(_status, 12.5, "Muted");
            _status.TextAlignment = TextAlignment.Right;
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(20, 0, 18, 0);
            footer.Children.Add(_status);
            Grid.SetRow(footer, 2);
            _browser.Children.Add(footer);
        }

        private void BuildSelectionTray()
        {
            var tray = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(4, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Border.BackgroundProperty, "Accent");
            tray.Children.Add(dot);
            var figures = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            FormatLabel(_selectionSize, 16, "Ink", FontWeights.SemiBold);
            FormatLabel(_selectionCount, 11.5, "Muted");
            figures.Children.Add(_selectionSize);
            figures.Children.Add(_selectionCount);
            tray.Children.Add(figures);
            _clearMarks.Style = (Style)FindResource("QuietButton");
            _clearMarks.Margin = new Thickness(0, 0, 4, 0);
            _clearMarks.FontSize = 13.5;
            _clearMarks.Padding = new Thickness(12, 6, 12, 6);
            AutomationProperties.SetName(_clearMarks, "Clear all marks");
            tray.Children.Add(_clearMarks);
            _reviewRecycle.Content = UiVisuals.WithIcon("Review & delete", UiVisuals.Bin, "AccentInk");
            _reviewRecycle.Padding = new Thickness(16, 7, 18, 7);
            _reviewRecycle.FontSize = 14;
            UiVisuals.Emphasize(_reviewRecycle);
            _reviewRecycle.ToolTip = "Check the marked items and move them to the Recycle Bin";
            AutomationProperties.SetName(_reviewRecycle, "Review and delete marked items");
            tray.Children.Add(_reviewRecycle);
            _selectionTray.Child = tray;
            _selectionTray.Padding = new Thickness(10, 5, 5, 5);
            _selectionTray.CornerRadius = new CornerRadius(18);
            _selectionTray.BorderThickness = new Thickness(1);
            _selectionTray.SetResourceReference(Border.BackgroundProperty, "GlassPane");
            _selectionTray.SetResourceReference(Border.BorderBrushProperty, "GlassInner");
            _selectionTray.Visibility = Visibility.Collapsed;
        }

        private DockPanel BuildEntries()
        {
            var sidebar = new DockPanel { Margin = new Thickness(10, 16, 6, 10) };
            var heading = new StackPanel { Margin = new Thickness(10, 0, 9, 10) };
            heading.Children.Add(Label("Contents", 19, "Ink", FontWeights.SemiBold));
            FormatLabel(_itemCount, 12.5, "Muted");
            _itemCount.Margin = new Thickness(0, 3, 0, 0);
            heading.Children.Add(_itemCount);
            DockPanel.SetDock(heading, Dock.Top);
            sidebar.Children.Add(heading);

            _entries.Background = Brushes.Transparent;
            _entries.Style = (Style)FindResource("AppResults");
            _entries.BorderThickness = new Thickness(0);
            _entries.ItemContainerStyle = (Style)FindResource("EntryRow");
            _entries.ItemTemplate = (DataTemplate)FindResource("EntryTemplate");
            _entries.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            _entries.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            AutomationProperties.SetName(_entries, "Folders and files, largest first. Space marks, Delete reviews for the Recycle Bin.");
            sidebar.Children.Add(_entries);
            return sidebar;
        }

        private Grid BuildMap()
        {
            var chart = new Grid();
            chart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var heading = new DockPanel { Margin = new Thickness(24, 16, 22, 0) };
            var size = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            FormatLabel(_total, 30, "Ink", FontWeights.SemiBold);
            _total.TextAlignment = TextAlignment.Right;
            size.Children.Add(_total);
            FormatLabel(_totalNote, 12.5, "Muted");
            _totalNote.TextAlignment = TextAlignment.Right;
            size.Children.Add(_totalNote);
            size.ToolTip = "Combined file sizes in this folder. Space used on disk can differ.";
            DockPanel.SetDock(size, Dock.Right);
            heading.Children.Add(size);
            var name = new StackPanel { Margin = new Thickness(0, 6, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            FormatLabel(_mapNote, 15, "Muted");
            name.Children.Add(_mapNote);
            heading.Children.Add(name);
            chart.Children.Add(heading);

            var plot = new Grid();
            Grid.SetRow(plot, 1);
            plot.Children.Add(_map);

            _empty.HorizontalAlignment = HorizontalAlignment.Center;
            _empty.VerticalAlignment = VerticalAlignment.Center;
            _empty.IsHitTestVisible = false;
            var emptyArt = new EmptyBubble { Width = 92, Height = 92, Margin = new Thickness(0, 0, 0, 14) };
            _artwork.Add(emptyArt);
            _empty.Children.Add(emptyArt);
            FormatLabel(_mapEmpty, 16, "Muted");
            _mapEmpty.TextAlignment = TextAlignment.Center;
            _empty.Children.Add(_mapEmpty);
            _empty.Visibility = Visibility.Collapsed;
            plot.Children.Add(_empty);

            var pager = new StackPanel { Orientation = Orientation.Horizontal };
            _pagePrevious.Content = UiVisuals.Icon(UiVisuals.Back, "Ink", 13);
            _pagePrevious.ToolTip = "Previous page (Page Up)";
            AutomationProperties.SetName(_pagePrevious, "Previous page of bubbles");
            var nextContent = new StackPanel { Orientation = Orientation.Horizontal };
            nextContent.Children.Add(new TextBlock { Text = "Next page", VerticalAlignment = VerticalAlignment.Center });
            var nextIcon = UiVisuals.Icon(UiVisuals.Forward, "AccentInk", 13);
            nextIcon.Margin = new Thickness(8, 0, 0, 0);
            nextContent.Children.Add(nextIcon);
            _pageNext.Content = nextContent;
            _pageNext.ToolTip = "Next page (Page Down)";
            AutomationProperties.SetName(_pageNext, "Next page of bubbles");
            _pagePrevious.Style = (Style)FindResource("RoundButton");
            _pagePrevious.Width = _pagePrevious.Height = 34;
            _pageNext.FontSize = 13.5;
            _pageNext.MinHeight = 34;
            _pageNext.Padding = new Thickness(14, 4, 12, 4);
            UiVisuals.Emphasize(_pageNext);
            FormatLabel(_pageRange, 13.5, "Ink", FontWeights.SemiBold);
            _pageRange.TextAlignment = TextAlignment.Center;
            FormatLabel(_pageZoom, 11, "Muted");
            _pageZoom.TextAlignment = TextAlignment.Center;
            var range = new StackPanel { Margin = new Thickness(12, 0, 12, 0), MinWidth = 96, VerticalAlignment = VerticalAlignment.Center };
            range.Children.Add(_pageRange);
            range.Children.Add(_pageZoom);
            pager.Children.Add(_pagePrevious);
            pager.Children.Add(range);
            pager.Children.Add(_pageNext);
            _pager.Child = pager;
            _pager.Padding = new Thickness(5);
            _pager.CornerRadius = new CornerRadius(22);
            _pager.BorderThickness = new Thickness(1);
            _pager.SetResourceReference(Border.BackgroundProperty, "GlassPane");
            _pager.SetResourceReference(Border.BorderBrushProperty, "GlassInner");
            _pager.HorizontalAlignment = HorizontalAlignment.Center;
            _pager.VerticalAlignment = VerticalAlignment.Bottom;
            _pager.Margin = new Thickness(0, 0, 0, 14);
            _pager.Visibility = Visibility.Collapsed;
            plot.Children.Add(_pager);
            chart.Children.Add(plot);
            return chart;
        }

        private void BuildLanding()
        {
            var body = new StackPanel();
            var heading = Label("See where your space went.", 40, "Ink", FontWeights.SemiBold);
            heading.TextAlignment = TextAlignment.Center;
            body.Children.Add(heading);
            var hint = Label("Scan a drive or folder. Every bubble is true to size, so the big ones stand out.", 16, "Muted");
            hint.Margin = new Thickness(0, 10, 0, 0);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.TextAlignment = TextAlignment.Center;
            body.Children.Add(hint);
            string systemDrive = IOPath.GetPathRoot(Environment.SystemDirectory);
            var start = new Button { Content = UiVisuals.WithIcon("Scan " + systemDrive.TrimEnd('\\'), UiVisuals.Drive, "AccentInk"),
                MinHeight = 46, Margin = new Thickness(0, 0, 5, 0), FontSize = 15.5 };
            AutomationProperties.SetName(start, "Scan " + systemDrive.TrimEnd('\\'));
            UiVisuals.Emphasize(start);
            start.Click += async (sender, args) => await ScanAsync(systemDrive);
            var choose = new Button { Content = UiVisuals.WithIcon("Choose folder…", UiVisuals.Folder, "Muted"), MinHeight = 46,
                Margin = new Thickness(5, 0, 0, 0), FontSize = 15.5, Style = (Style)FindResource("GlassButton") };
            choose.Click += async (sender, args) => await ChooseFolderAsync();
            var actions = new UniformGrid { Rows = 1, Columns = 2, Width = 360,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
            actions.Children.Add(start);
            actions.Children.Add(choose);
            body.Children.Add(actions);
            BuildIntro(_landing, body);
        }

        private void BuildScanning()
        {
            var body = new StackPanel();
            var heading = Label("Scanning", 36, "Ink", FontWeights.SemiBold);
            heading.TextAlignment = TextAlignment.Center;
            body.Children.Add(heading);
            FormatLabel(_scanCounts, 15, "Ink");
            _scanCounts.TextAlignment = TextAlignment.Center;
            _scanCounts.Margin = new Thickness(0, 12, 0, 6);
            FormatLabel(_scanPath, 13, "Muted");
            _scanPath.TextAlignment = TextAlignment.Center;
            _scanPath.Margin = new Thickness(0, 0, 0, 20);
            _scanPath.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("Text") { Source = _scanPath });
            body.Children.Add(_scanCounts);
            body.Children.Add(_scanPath);
            _spinner.SetResourceReference(Control.ForegroundProperty, "Accent");
            _spinner.SetResourceReference(Control.BackgroundProperty, "Line");
            _spinner.HorizontalAlignment = HorizontalAlignment.Center;
            _spinner.Template = (ControlTemplate)FindResource("ScanMeter");
            AutomationProperties.SetName(_spinner, "Scan in progress");
            body.Children.Add(_spinner);
            _cancel.HorizontalAlignment = HorizontalAlignment.Center;
            _cancel.Margin = new Thickness(0, 26, 0, 0);
            _cancel.Style = (Style)FindResource("GlassButton");
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
                MaxWidth = 600, Margin = new Thickness(16, 12, 16, 24) };
            var art = new StorageIllustration { IsHitTestVisible = false, Width = 480, Height = 190,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) };
            _artwork.Add(art);
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

        private static void FormatLabel(TextBlock label, double size, string color, FontWeight? weight = null)
        {
            label.FontSize = size;
            label.SetResourceReference(TextBlock.ForegroundProperty, color);
            label.FontWeight = weight ?? FontWeights.Normal;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
        }
    }

    /// <summary>The product mark: three glass bubbles, drawn with the same renderer as the map.</summary>
    internal sealed class BrandMark : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing)
        {
            double unit = Math.Min(ActualWidth / 34, ActualHeight / 30);
            GlassOrb.For(UiVisuals.KindColor("Folder")).Draw(drawing, new Point(12 * unit, 15 * unit), 10.5 * unit, quiet: false);
            GlassOrb.For(UiVisuals.KindColor("Video")).Draw(drawing, new Point(27 * unit, 9 * unit), 5.5 * unit, quiet: false);
            GlassOrb.For(UiVisuals.KindColor("Image")).Draw(drawing, new Point(26 * unit, 22.5 * unit), 4.5 * unit, quiet: false);
        }
    }

    /// <summary>An empty glass bubble for folders with nothing in them.</summary>
    internal sealed class EmptyBubble : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing)
        {
            double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 4;
            if (radius > 0)
                GlassOrb.For(UiVisuals.KindColor("Other")).Draw(drawing, new Point(ActualWidth / 2, ActualHeight / 2), radius, quiet: false);
        }
    }
}
