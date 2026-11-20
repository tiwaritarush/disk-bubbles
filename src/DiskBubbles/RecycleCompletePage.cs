using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DiskBubbles
{
    /// <summary>The result of a removal: the bin animation, what was freed, and anything that stayed and why.</summary>
    internal sealed class RecycleCompletePage : Grid
    {
        private readonly RecycleCelebration _scene = new RecycleCelebration();
        private readonly TextBlock _size = new TextBlock();
        private readonly TextBlock _detail = new TextBlock();
        private readonly StackPanel _stayed = new StackPanel();
        private readonly TextBlock _stayedTitle = new TextBlock();
        private readonly ItemsControl _stayedList = new ItemsControl();
        private readonly Button _back = new Button { Content = "Back to results", MinWidth = 170, Margin = new Thickness(5) };
        public event Action BackRequested;
        public event Action HomeRequested;
        public event Action RecycleBinRequested;

        public RecycleCompletePage()
        {
            var content = new Grid { MaxWidth = 720, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Stretch };
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 150, MaxHeight = 340 });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Children.Add(content);
            content.Children.Add(_scene);
            AutomationProperties.SetName(_scene, "A bubble circles an open Recycle Bin and dissolves into it");

            var summary = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            _size.FontSize = 60;
            _size.FontWeight = FontWeights.Bold;
            _size.TextAlignment = TextAlignment.Center;
            _size.Margin = new Thickness(0, 0, 0, 0);
            _size.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            summary.Children.Add(_size);
            _detail.FontSize = 15;
            _detail.TextAlignment = TextAlignment.Center;
            _detail.TextWrapping = TextWrapping.Wrap;
            _detail.Margin = new Thickness(0, 4, 0, 0);
            _detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            summary.Children.Add(_detail);
            Grid.SetRow(summary, 1);
            content.Children.Add(summary);

            _stayedTitle.FontSize = 14;
            _stayedTitle.FontWeight = FontWeights.SemiBold;
            _stayedTitle.SetResourceReference(TextBlock.ForegroundProperty, "Attention");
            _stayed.Children.Add(_stayedTitle);
            _stayedList.ItemTemplate = (DataTemplate)Application.Current.FindResource("RecycleEntry");
            _stayed.Children.Add(new ScrollViewer
            {
                Content = _stayedList, MaxHeight = 120, Margin = new Thickness(0, 8, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            var stayedCard = UiVisuals.FrostedPane(new Border { Padding = new Thickness(18, 14, 14, 12), Child = _stayed }, "GlassPane", 16);
            stayedCard.Margin = new Thickness(0, 18, 0, 0);
            stayedCard.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = _stayed });
            Grid.SetRow(stayedCard, 2);
            content.Children.Add(stayedCard);

            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 16) };
            UiVisuals.Emphasize(_back);
            var bin = new Button { Content = UiVisuals.WithIcon("Open Recycle Bin", UiVisuals.Bin, "Muted"), MinWidth = 170, Margin = new Thickness(5) };
            var home = new Button { Content = "Home", MinWidth = 110, Margin = new Thickness(5) };
            _back.Click += (sender, args) => BackRequested?.Invoke();
            bin.Click += (sender, args) => RecycleBinRequested?.Invoke();
            home.Click += (sender, args) => HomeRequested?.Invoke();
            actions.Children.Add(_back);
            actions.Children.Add(bin);
            actions.Children.Add(home);
            Grid.SetRow(actions, 3);
            content.Children.Add(actions);
            IsVisibleChanged += (sender, args) =>
            {
                if (IsVisible)
                    Dispatcher.BeginInvoke(new Action(() => _back.Focus()));
            };
        }

        public void ShowResult(IReadOnlyList<RecycleItem> removed, IReadOnlyList<RecycleItem> stayed)
        {
            long bytes = removed.Aggregate(0L, (total, item) => DiskScanner.AddBytes(total, item.Entry.Bytes));
            int recycled = removed.Count(item => item.Outcome == RecycleOutcome.Recycled);
            int deleted = removed.Count - recycled;
            // One headline: the number carries the weight and the verb completes it, no label above it.
            _size.Inlines.Clear();
            _size.Inlines.Add(new System.Windows.Documents.Run(DiskEntry.FormatSize(bytes)));
            var verb = new System.Windows.Documents.Run(" deleted") { FontSize = 30, FontWeight = FontWeights.SemiBold };
            verb.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
            _size.Inlines.Add(verb);
            var parts = new List<string>();
            if (recycled > 0)
                parts.Add($"{Count(recycled)} moved to the Recycle Bin");
            if (deleted > 0)
                parts.Add($"{Count(deleted)} permanently deleted");
            _detail.Text = string.Join("  ·  ", parts) + (recycled > 0 ? "\nRecycled items keep using drive space until you empty the Recycle Bin." : "");
            _stayedTitle.Text = stayed.Count == 1 ? "1 item stayed in place" : $"{stayed.Count:N0} items stayed in place";
            _stayedList.ItemsSource = stayed;
            _stayed.Visibility = stayed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(this, $"{_size.Text}. {_detail.Text}" + (stayed.Count > 0 ? " " + _stayedTitle.Text : ""));
            _scene.Restart();
        }

        private static string Count(int count) => count == 1 ? "1 item" : $"{count:N0} items";

        private static TextBlock Text(string value, double size, string brush, FontWeight weight)
        {
            var text = new TextBlock { Text = value, FontSize = size, FontWeight = weight, TextAlignment = TextAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return text;
        }

        public void UpdateAppearance() => _scene.InvalidateVisual();
    }
}
