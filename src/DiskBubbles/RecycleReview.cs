using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DiskBubbles
{
    internal sealed class RecycleReview : Window
    {
        public RecycleReview(Window owner, IReadOnlyList<RecycleItem> items, DiskEntry current, DiskEntry root,
            bool completed = false)
        {
            Owner = owner;
            Title = completed ? "Recycle results" : "Review Recycle Bin move";
            Width = 816;
            Height = 494;
            MinWidth = 590;
            MinHeight = 376;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(Control.BackgroundProperty, "Workspace");
            SetResourceReference(Control.ForegroundProperty, "Ink");
            FontFamily = owner.FontFamily;
            Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
            UseLayoutRounding = true;
            UiVisuals.ThemeCaption(this);

            var layout = new DockPanel();
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var headingText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var emblem = UiVisuals.Surface(new Border
            {
                Child = UiVisuals.Icon(UiVisuals.Bin, "Accent", 22),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Width = 48, Height = 48,
                Margin = new Thickness(0, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center,
            }, "Raised", "Line");
            DockPanel.SetDock(emblem, Dock.Left);
            heading.Children.Add(emblem);
            heading.Children.Add(headingText);
            headingText.Children.Add(Label(Title, 25, "Ink", FontWeights.SemiBold));
            int ready = items.Count(item => item.Ready);
            long bytes = items.Where(item => item.Ready).Aggregate(0L, (total, item) => DiskScanner.AddBytes(total, item.Entry.Bytes));
            string summary = completed ? (items.Count == 1 ? "1 item needs attention" : $"{items.Count:N0} items need attention") :
                ready == 0 ? "Windows could not verify any selected items for recycling." :
                $"{ready:N0} moving · {DiskEntry.FormatSize(bytes)}" + (items.Count > ready ? $"  ·  {items.Count - ready:N0} staying in place" : "");
            var status = Label(summary, 13, "Muted");
            status.Margin = new Thickness(0, 3, 0, 0);
            headingText.Children.Add(status);
            if (items.Count > 0)
            {
                string location = Path.GetDirectoryName(items[0].Path);
                if (items.Any(item => !string.Equals(Path.GetDirectoryName(item.Path), location, StringComparison.Ordinal)))
                    location = "Across multiple folders";
                var path = Label(location, 12, "Muted");
                path.TextWrapping = TextWrapping.NoWrap;
                path.TextTrimming = TextTrimming.CharacterEllipsis;
                path.ToolTip = location;
                path.Margin = new Thickness(0, 4, 0, 0);
                headingText.Children.Add(path);
            }
            DockPanel.SetDock(heading, Dock.Top);
            layout.Children.Add(heading);

            var footer = new StackPanel { Margin = new Thickness(0, 13, 0, 0) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var close = new Button { Content = completed ? "Close" : "Cancel" };
            Button move = null;
            actions.Children.Add(close);
            if (!completed)
            {
                var note = Label("Recycled items still use drive space until removed. You can restore them from the Recycle Bin.", 12, "Muted");
                note.Margin = new Thickness(0, 0, 0, 9);
                footer.Children.Add(note);
                move = new Button { Content = "Move to Recycle Bin", IsEnabled = ready > 0, Margin = new Thickness(8, 0, 0, 0) };
                UiVisuals.Emphasize(move);
                actions.Children.Add(move);
            }
            footer.Children.Add(actions);
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(footer);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition());
            if (!completed && root != null)
            {
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
                var preview = new StackPanel { Margin = new Thickness(17, 16, 16, 15) };
                preview.Children.Add(Label("After the move", 16, "Ink", FontWeights.SemiBold));
                if (current != null && current != root)
                    AddProjection(preview, "Current folder", current.Bytes, bytes);
                AddProjection(preview, "Entire scan", root.Bytes, bytes);
                var previewPanel = UiVisuals.Surface(new Border
                {
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    CornerRadius = new CornerRadius(0, 12, 12, 0),
                    Child = new ScrollViewer { Background = Brushes.Transparent, Content = preview,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                }, "Chart", "Line");
                Grid.SetColumn(previewPanel, 1);
                content.Children.Add(previewPanel);
            }

            var list = new ListBox { ItemsSource = items.OrderByDescending(item => item.Ready).ToList(), BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                ItemTemplate = (DataTemplate)FindResource("RecycleEntry"), Padding = new Thickness(6) };
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            list.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            list.ItemContainerStyle = (Style)FindResource("EntryRow");
            content.Children.Add(list);
            layout.Children.Add(Card(content));
            var body = UiVisuals.Surface(new Border { Padding = new Thickness(24), Child = layout }, "Workspace");
            Content = body;
            var motion = new DialogMotion(this, body);
            close.Click += (sender, args) => motion.Close(Close);
            Loaded += (sender, args) => close.Focus();
            if (move != null)
                move.Click += (sender, args) => motion.Close(() => DialogResult = true);
        }

        private void AddProjection(Panel panel, string label, long before, long moved)
        {
            long after = Math.Max(0, before - moved);
            var title = Label(label, 11, "Muted");
            title.Margin = new Thickness(0, 13, 0, 3);
            panel.Children.Add(title);
            panel.Children.Add(Label(DiskEntry.FormatSize(before) + "  →  " + DiskEntry.FormatSize(after),
                14, "Ink", FontWeights.SemiBold));
        }

        private static TextBlock Label(string text, double size, string brush, FontWeight? weight = null)
        {
            var label = new TextBlock
            {
                Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return label;
        }

        private static Border Card(UIElement child) => UiVisuals.Surface(new Border
        {
            Child = child, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
        }, "Panel", "GlassEdge");
    }
}
