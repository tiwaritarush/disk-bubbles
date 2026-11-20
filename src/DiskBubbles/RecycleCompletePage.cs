using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DiskBubbles
{
    internal sealed class RecycleCompletePage : Grid
    {
        private readonly RecycleCelebration _scene = new RecycleCelebration();
        private readonly TextBlock _size = new TextBlock();
        private readonly TextBlock _detail = new TextBlock();
        public event Action BackRequested;
        public event Action HomeRequested;

        public RecycleCompletePage()
        {
            var content = new Grid { MaxWidth = 680, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Stretch };
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 160, MaxHeight = 350 });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Children.Add(content);
            content.Children.Add(_scene);
            AutomationProperties.SetName(_scene, "A bubble circles an open Recycle Bin and dissolves into it");
            var summary = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var title = new TextBlock { Text = "Deleted", FontSize = 22, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            summary.Children.Add(title);
            _size.FontSize = 58;
            _size.FontWeight = FontWeights.Bold;
            _size.TextAlignment = TextAlignment.Center;
            _size.Margin = new Thickness(0, 4, 0, 0);
            _size.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            summary.Children.Add(_size);
            _detail.FontSize = 14;
            _detail.TextAlignment = TextAlignment.Center;
            _detail.Margin = new Thickness(0, 7, 0, 0);
            _detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            summary.Children.Add(_detail);
            Grid.SetRow(summary, 1);
            content.Children.Add(summary);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 16) };
            var back = new Button { Content = "Back to selection", MinWidth = 176, Margin = new Thickness(4) };
            var home = new Button { Content = "Home screen", MinWidth = 150, Margin = new Thickness(4) };
            back.Click += (sender, args) => BackRequested?.Invoke();
            home.Click += (sender, args) => HomeRequested?.Invoke();
            actions.Children.Add(back);
            actions.Children.Add(home);
            Grid.SetRow(actions, 2);
            content.Children.Add(actions);
        }

        public void ShowResult(long bytes, int count)
        {
            _size.Text = DiskEntry.FormatSize(bytes);
            _detail.Text = $"{count:N0} {(count == 1 ? "item" : "items")} moved to Recycle Bin";
            AutomationProperties.SetName(this, "Deleted " + _size.Text + ". " + _detail.Text);
            _scene.Restart();
        }

        public void UpdateAppearance() => _scene.InvalidateVisual();
    }
}
