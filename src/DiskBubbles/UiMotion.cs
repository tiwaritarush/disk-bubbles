using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DiskBubbles
{
    internal static class UiMotion
    {
        public const int HoverMs = 125;
        public const int ZoomMs = 300;
        public const int ZoomFadeMs = 225;
        public const int RevealMs = 210;
        public const int DialogEnterMs = 225;
        public const int DialogExitMs = 170;

        private sealed class SplineEase : EasingFunctionBase
        {
            private readonly KeySpline _spline;
            private readonly bool _leaving;

            public SplineEase(bool leaving)
            {
                _leaving = leaving;
                _spline = leaving ? new KeySpline(0.55, 0, 1, 0.45) : new KeySpline(0.32, 0.72, 0, 1);
            }

            protected override double EaseInCore(double time) => _spline.GetSplineProgress(time);
            protected override Freezable CreateInstanceCore() => new SplineEase(_leaving);
        }

        public static DoubleAnimation Ease(double from, double to, int milliseconds, bool leaving = false) =>
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new SplineEase(leaving),
                FillBehavior = FillBehavior.HoldEnd,
            };
    }

    internal sealed class DialogMotion
    {
        private readonly Window _window;
        private readonly FrameworkElement _content;
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _offset = new TranslateTransform();
        private bool _closing;
        private bool _finishing;

        public DialogMotion(Window window, FrameworkElement content)
        {
            _window = window;
            _content = content;
            var transform = new TransformGroup();
            transform.Children.Add(_scale);
            transform.Children.Add(_offset);
            _content.RenderTransform = transform;
            _content.RenderTransformOrigin = new Point(0.5, 0.5);
            window.Loaded += (sender, args) =>
            {
                if (!SystemParameters.ClientAreaAnimation)
                    return;
                _content.BeginAnimation(UIElement.OpacityProperty, UiMotion.Ease(0, 1, UiMotion.DialogEnterMs));
                _offset.BeginAnimation(TranslateTransform.YProperty, UiMotion.Ease(8, 0, UiMotion.DialogEnterMs));
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, UiMotion.Ease(0.97, 1, UiMotion.DialogEnterMs));
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, UiMotion.Ease(0.97, 1, UiMotion.DialogEnterMs));
            };
            window.PreviewKeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape)
                    Close(window.Close);
                if (_closing || args.Key == Key.Escape)
                    args.Handled = true;
            };
            window.Closing += (sender, args) =>
            {
                if (_finishing || !CanAnimateClose)
                    return;
                args.Cancel = true;
                Close(window.Close);
            };
        }

        private bool CanAnimateClose => _window.IsLoaded && ComponentDispatcher.IsThreadModal && SystemParameters.ClientAreaAnimation;

        public void Close(Action finish)
        {
            if (_closing)
                return;
            _closing = true;
            if (!CanAnimateClose)
            {
                _finishing = true;
                finish();
                return;
            }

            _content.IsHitTestVisible = false;
            var fade = UiMotion.Ease(_content.Opacity, 0, UiMotion.DialogExitMs, leaving: true);
            fade.Completed += (sender, args) =>
            {
                _finishing = true;
                finish();
            };
            _content.BeginAnimation(UIElement.OpacityProperty, fade);
            _offset.BeginAnimation(TranslateTransform.YProperty, UiMotion.Ease(_offset.Y, 8, UiMotion.DialogExitMs, leaving: true));
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, UiMotion.Ease(_scale.ScaleX, 0.98, UiMotion.DialogExitMs, leaving: true));
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, UiMotion.Ease(_scale.ScaleY, 0.98, UiMotion.DialogExitMs, leaving: true));
        }
    }
}
