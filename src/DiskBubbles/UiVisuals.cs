using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DiskBubbles
{
    internal static class UiVisuals
    {
        public const string Back = "M 13,4 L 5,12 13,20 M 5,12 H 20";
        public const string Forward = "M 11,4 L 19,12 11,20 M 19,12 H 4";
        public const string Up = "M 4,13 L 12,5 20,13 M 12,5 V 20";
        public const string Refresh = "M 19,8 A 8,8 0 1 0 19,16 M 19,3 V 8 H 14";
        public const string Folder = "M 3,6 Q 3,4 5,4 H 10 L 13,7 H 20 Q 22,7 22,9 V 18 Q 22,20 20,20 H 5 Q 3,20 3,18 Z";
        public const string Drive = "M 5,6 H 19 L 22,15 V 18 Q 22,20 20,20 H 4 Q 2,20 2,18 V 15 Z M 2,15 H 22 M 17,18 H 19";
        public const string Apps = "M 3,3 H 10 V 10 H 3 Z M 14,3 H 21 V 10 H 14 Z M 3,14 H 10 V 21 H 3 Z M 14,14 H 21 V 21 H 14 Z";
        public const string Bin = "M 4,7 H 20 M 9,7 V 4 H 15 V 7 M 6,7 L 7,20 H 17 L 18,7 M 10,10 V 17 M 14,10 V 17";
        public const string Sun = "M 12,8 A 4,4 0 1 1 11.99,8 M 12,2 V 4 M 12,20 V 22 M 2,12 H 4 M 20,12 H 22 M 5,5 L 6.5,6.5 M 17.5,17.5 L 19,19 M 5,19 L 6.5,17.5 M 17.5,6.5 L 19,5";
        public const string Moon = "M 19.5,15 A 8,8 0 1 1 9,4.5 A 6.5,6.5 0 0 0 19.5,15 Z";

        public static FrameworkElement Icon(string geometry, string color, double size = 16)
        {
            var icon = new Path
            {
                Data = Geometry.Parse(geometry), Width = size, Height = size, Stretch = Stretch.Uniform,
                StrokeThickness = 1.35, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
            icon.SetResourceReference(Shape.StrokeProperty, color);
            return icon;
        }

        public static FrameworkElement WithIcon(string label, string geometry, string color)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icon(geometry, color);
            icon.Margin = new Thickness(0, 0, 8, 0);
            group.Children.Add(icon);
            group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return group;
        }

        public static Border Surface(Border border, string fill, string edge = null)
        {
            border.SetResourceReference(Border.BackgroundProperty, fill);
            if (edge != null)
                border.SetResourceReference(Border.BorderBrushProperty, edge);
            return border;
        }

        public static Border FrostedPane(UIElement content, string fill = "GlassPane")
        {
            var layers = new Grid();
            var sheen = new Border { CornerRadius = new CornerRadius(17), IsHitTestVisible = false };
            sheen.SetResourceReference(Border.BackgroundProperty, "GlassSheen");
            layers.Children.Add(sheen);
            layers.Children.Add(content);
            var inner = Surface(new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = layers },
                fill, "GlassInner");
            return Surface(new Border { CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1), Child = inner },
                "Chart", "GlassEdge");
        }

        public static void Emphasize(Button button)
        {
            button.SetResourceReference(Control.BackgroundProperty, "AccentFill");
            button.SetResourceReference(Control.BorderBrushProperty, "Accent");
            button.SetResourceReference(Control.ForegroundProperty, "AccentInk");
        }

        public static void ThemeCaption(Window window) => window.SourceInitialized += (sender, args) => UpdateCaption(window);

        public static void UpdateCaption(Window window)
        {
            if (SystemParameters.HighContrast)
                return;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            int dark = Appearance.IsLight ? 0 : 1;
            int caption = NativeColor(((SolidColorBrush)window.FindResource("Base")).Color);
            int text = NativeColor(((SolidColorBrush)window.FindResource("Ink")).Color);
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        }

        private static int NativeColor(Color color) => color.R | color.G << 8 | color.B << 16;

        public static Window Notice(Window owner, string heading, string detail)
        {
            var dialog = new Window
            {
                Owner = owner, Title = heading, Width = 480, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FontFamily = owner.FontFamily, UseLayoutRounding = true,
            };
            dialog.SetResourceReference(Control.BackgroundProperty, "Panel");
            ThemeCaption(dialog);
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = heading, FontSize = 24, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)owner.FindResource("Ink"), TextWrapping = TextWrapping.Wrap,
            });
            var message = new TextBlock
            {
                Text = detail, FontSize = 13, Foreground = (Brush)owner.FindResource("Muted"), TextWrapping = TextWrapping.Wrap,
            };
            body.Children.Add(new ScrollViewer
            {
                Content = message, Margin = new Thickness(0, 12, 0, 22), MaxHeight = 224,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            var close = new Button
            {
                Content = "Close", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right,
            };
            Emphasize(close);
            body.Children.Add(close);
            var content = Surface(new Border { Child = body, Padding = new Thickness(24) }, "Panel");
            dialog.Content = content;
            var motion = new DialogMotion(dialog, content);
            close.Click += (sender, args) => motion.Close(dialog.Close);
            return dialog;
        }

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    internal sealed class GlassOrb
    {
        private static GlassOrb _light;
        private static GlassOrb _dark;

        private readonly Brush _shadow;
        private readonly Brush _body;
        private readonly Brush _frost;
        private readonly Brush _tint;
        private readonly Brush _bevel;
        private readonly Brush _sheen;
        private readonly Brush _glint;
        private readonly Pen _edge;

        private GlassOrb(bool light)
        {
            var center = new Point(0.5, 0.5);
            var cloudySide = new Point(0.26, 0.24);
            Color tint = ((SolidColorBrush)Application.Current.FindResource("BubbleTint")).Color;
            uint rgb = (uint)(tint.R << 16 | tint.G << 8 | tint.B);
            _tint = Radial(new Point(0.54, 0.93), 0.65, 0.38,
                (0, rgb | (light ? 0x64000000u : 0x57000000u)), (0.44, rgb | 0x38000000u),
                (0.73, rgb | 0x15000000u), (1, rgb));

            if (light)
            {
                _shadow = Radial(center, 0.5, 0.5, (0, 0x304C453D), (0.6, 0x184C453D), (1, 0x004C453D));
                _sheen = Radial(new Point(0.36, 0.22), 0.42, 0.28, (0, 0xC8FFFFFF), (0.55, 0x4DFFFFFF), (1, 0x00FFFFFF));
                _glint = Radial(center, 0.5, 0.5, (0, 0xF2FFFFFF), (1, 0x00FFFFFF));
                _body = Linear(new Point(0.18, 0), new Point(0.65, 1),
                    (0, 0xD6FEFCF9), (0.38, 0xA4F9F6F1), (0.71, 0x82EEEAE6), (1, 0xABFFFDF9));
                _frost = Radial(cloudySide, 0.73, 0.64,
                    (0, 0x9EFFFFFF), (0.38, 0x61FFFFFF), (0.78, 0x13FFFFFF), (1, 0x00FFFFFF));
                _bevel = Radial(new Point(0.51, 0.48), 0.50, 0.51,
                    (0.69, 0x00FFFCF9), (0.85, 0x1BFFFCF9), (0.94, 0x54FFFCF9), (1, 0xA2FFFCF9));
                _edge = Edge((0, 0xF4FFFFFF), (0.42, 0xBAFFFDF8), (0.73, 0x988D857D), (1, 0x60B3AAA4));
            }
            else
            {
                _shadow = Radial(center, 0.5, 0.5, (0, 0x66000209), (0.6, 0x2E000209), (1, 0x00000209));
                _sheen = Radial(new Point(0.36, 0.22), 0.42, 0.28, (0, 0x52FFFFFF), (0.55, 0x17FFFFFF), (1, 0x00FFFFFF));
                _glint = Radial(center, 0.5, 0.5, (0, 0xCCFFFFFF), (1, 0x00FFFFFF));
                _body = Linear(new Point(0.15, 0), new Point(0.67, 1),
                    (0, 0xCC515353), (0.37, 0xB6383A3A), (0.7, 0xA5282A2A), (1, 0xC43D3F3F));
                _frost = Radial(cloudySide, 0.68, 0.63,
                    (0, 0x54EEEEEE), (0.43, 0x2CEEEEEE), (0.77, 0x0AEEEEEE), (1, 0x00EEEEEE));
                _bevel = Radial(new Point(0.51, 0.48), 0.50, 0.51,
                    (0.71, 0x00EEEEEE), (0.85, 0x13EEEEEE), (0.95, 0x32EEEEEE), (1, 0x58EEEEEE));
                _edge = Edge((0, 0xB7EEEEEE), (0.40, 0x71C1C3C3), (0.74, 0x356D7070), (1, 0x7A9B9E9E));
            }
        }

        public static void Draw(DrawingContext drawing, Point center, double radius)
        {
            bool light = Appearance.IsLight;
            var glass = light ? (_light ??= new GlassOrb(true)) : (_dark ??= new GlassOrb(false));
            if (radius >= 14)
                drawing.DrawEllipse(glass._shadow, null, new Point(center.X, center.Y + radius * 0.10), radius * 1.04, radius * 1.0);

            drawing.DrawEllipse(glass._body, null, center, radius, radius);
            drawing.DrawEllipse(glass._frost, null, center, radius, radius);
            if (!SystemParameters.HighContrast)
                drawing.DrawEllipse(glass._tint, null, center, radius, radius);
            drawing.DrawEllipse(glass._bevel, glass._edge, center, radius, radius);
            drawing.DrawEllipse(glass._sheen, null, center, radius * 0.97, radius * 0.97);
            if (radius >= 16)
                drawing.DrawEllipse(glass._glint, null, new Point(center.X - radius * 0.38, center.Y - radius * 0.52), radius * 0.09, radius * 0.055);
        }

        private static Pen Edge(params (double Offset, uint Argb)[] stops)
        {
            var rim = new Pen(Linear(new Point(0.10, 0), new Point(0.83, 1), stops), 1.15);
            rim.Freeze();
            return rim;
        }

        private static Brush Linear(Point from, Point to, params (double Offset, uint Argb)[] stops) =>
            Paint(new LinearGradientBrush { StartPoint = from, EndPoint = to }, stops);

        private static Brush Radial(Point center, double x, double y, params (double Offset, uint Argb)[] stops) =>
            Paint(new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = x, RadiusY = y }, stops);

        private static Brush Paint(GradientBrush brush, (double Offset, uint Argb)[] stops)
        {
            foreach (var (offset, color) in stops)
                brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color), offset));
            brush.Freeze();
            return brush;
        }
    }

    internal sealed class StorageIllustration : FrameworkElement
    {
        private static readonly Geometry FolderGlyph = Geometry.Parse(UiVisuals.Folder);

        protected override void OnRender(DrawingContext drawing)
        {
            const double width = 460;
            const double height = 174;
            var center = new Point(width / 2, height / 2);
            double scale = Math.Min(ActualWidth / width, ActualHeight / height);
            if (scale <= 0)
                return;

            drawing.PushTransform(new TranslateTransform((ActualWidth - width * scale) / 2, (ActualHeight - height * scale) / 2));
            drawing.PushTransform(new ScaleTransform(scale, scale));

            GlassOrb.Draw(drawing, center, 52);
            GlassOrb.Draw(drawing, center + new Vector(-73, -23), 19);
            GlassOrb.Draw(drawing, center + new Vector(73, -23), 19);
            GlassOrb.Draw(drawing, center + new Vector(-67, 26), 14);
            GlassOrb.Draw(drawing, center + new Vector(67, 26), 14);
            drawing.PushTransform(new TranslateTransform(center.X - 20, center.Y - 19.2));
            drawing.PushTransform(new ScaleTransform(1.6, 1.6));
            var ink = (Brush)FindResource("Ink");
            drawing.DrawGeometry(null, new Pen(ink, 0.75) { LineJoin = PenLineJoin.Round }, FolderGlyph);
            drawing.Pop();
            drawing.Pop();
            drawing.Pop();
            drawing.Pop();
        }

    }
}
