using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        public const string Bin = "M 4,7 H 20 M 9,7 V 4 H 15 V 7 M 6,7 L 7,20 H 17 L 18,7 M 10,10 V 17 M 14,10 V 17";
        public const string Sun = "M 12,8 A 4,4 0 1 1 11.99,8 M 12,2 V 4 M 12,20 V 22 M 2,12 H 4 M 20,12 H 22 M 5,5 L 6.5,6.5 M 17.5,17.5 L 19,19 M 5,19 L 6.5,17.5 M 17.5,6.5 L 19,5";
        public const string Moon = "M 19.5,15 A 8,8 0 1 1 9,4.5 A 6.5,6.5 0 0 0 19.5,15 Z";

        private static Brush _darkBlush;
        private static Brush _lightBlush;

        public static void DrawBlush(DrawingContext drawing, Rect bounds)
        {
            if (SystemParameters.HighContrast || bounds.Width <= 0 || bounds.Height <= 0)
                return;

            Brush blush = Appearance.IsLight ? (_lightBlush ??= BlushBrush(38)) : (_darkBlush ??= BlushBrush(49));
            drawing.DrawEllipse(blush, null, new Point(bounds.X + bounds.Width * 0.5, bounds.Y + bounds.Height * 0.5),
                bounds.Width * 0.46, bounds.Height * 0.47);
        }

        private static Brush BlushBrush(byte strength)
        {
            Color tint = ((SolidColorBrush)Application.Current.FindResource("Atmosphere")).Color;
            var brush = new RadialGradientBrush { RadiusX = 0.5, RadiusY = 0.5 };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(strength, tint.R, tint.G, tint.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(strength / 2), tint.R, tint.G, tint.B), 0.46));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, tint.R, tint.G, tint.B), 1));
            brush.Freeze();
            return brush;
        }

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

    internal static class GlassOrb
    {
        private static readonly Brush Shadow = new RadialGradientBrush(Color.FromArgb(141, 7, 7, 9), Color.FromArgb(0, 7, 7, 9));
        private static readonly Brush LightShadow = FrostGradient(new Point(0.5, 0.5), 0.5, 0.5,
            (0, 77, 70, 66, 53), (0.57, 46, 70, 66, 53), (0.83, 18, 70, 66, 53), (1, 0, 70, 66, 53));
        private static readonly Brush LightBody = FrostGradient(new Point(0.3, 0.17), 0.82, 0.94,
            (0, 218, 255, 255, 255), (0.47, 109, 255, 255, 253), (0.73, 75, 255, 255, 251), (1, 131, 255, 255, 254));
        private static readonly Brush LightRimFrost = FrostGradient(new Point(0.5, 0.5), 0.5, 0.5,
            (0.62, 0, 255, 255, 252), (0.83, 30, 255, 255, 252), (0.95, 111, 255, 255, 252), (1, 167, 255, 255, 252));
        private static readonly Brush LightGlint = FrostGradient(new Point(0.25, 0.13), 0.49, 0.28,
            (0, 197, 255, 255, 255), (0.42, 91, 255, 255, 255), (1, 0, 255, 255, 255));
        private static readonly Lazy<Brush> FrostGrain = new Lazy<Brush>(CreateFrostGrain);

        public static void Draw(DrawingContext drawing, Point center, double radius, Color tone)
        {
            if (Appearance.IsLight)
            {
                DrawLight(drawing, center, radius, tone);
                return;
            }
            if (radius >= 14)
                drawing.DrawEllipse(Shadow, null, new Point(center.X + 2, center.Y + 5), radius * 1.16, radius * 1.14);

            var body = new RadialGradientBrush
            {
                Center = new Point(0.45, 0.49), GradientOrigin = new Point(0.29, 0.18), RadiusX = 0.72, RadiusY = 0.82,
            };
            body.GradientStops.Add(new GradientStop(Tint(tone, 0.36, 216), 0));
            body.GradientStops.Add(new GradientStop(Tint(tone, 0.15, 186), 0.47));
            body.GradientStops.Add(new GradientStop(Tint(tone, 0.09, 178), 0.75));
            body.GradientStops.Add(new GradientStop(Tint(tone, 0.22, 211), 1));
            drawing.DrawEllipse(body, null, center, radius, radius);

            if (radius >= 20)
            {
                var refractedLight = new RadialGradientBrush
                {
                    Center = new Point(0.66, 0.95), GradientOrigin = new Point(0.71, 1.01), RadiusX = 0.53, RadiusY = 0.31,
                };
                refractedLight.GradientStops.Add(new GradientStop(Alpha(tone, 113), 0));
                refractedLight.GradientStops.Add(new GradientStop(Alpha(tone, 36), 0.54));
                refractedLight.GradientStops.Add(new GradientStop(Alpha(tone, 0), 1));
                drawing.DrawEllipse(refractedLight, null, center, radius, radius);

                var sheen = new RadialGradientBrush
                {
                    Center = new Point(0.28, 0.16), GradientOrigin = new Point(0.24, 0.08), RadiusX = 0.47, RadiusY = 0.32,
                };
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(91, 246, 246, 252), 0));
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(22, 246, 246, 252), 0.55));
                sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 246, 246, 252), 1));
                drawing.DrawEllipse(sheen, null, center, radius, radius);

                var innerLight = new RadialGradientBrush { RadiusX = 0.5, RadiusY = 0.5 };
                innerLight.GradientStops.Add(new GradientStop(Alpha(tone, 0), 0.76));
                innerLight.GradientStops.Add(new GradientStop(Alpha(tone, 23), 0.93));
                innerLight.GradientStops.Add(new GradientStop(Alpha(tone, 44), 1));
                drawing.DrawEllipse(innerLight, null, center, radius, radius);
            }

            var edge = new LinearGradientBrush { StartPoint = new Point(0.18, 0), EndPoint = new Point(0.72, 1) };
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(206, 245, 245, 250), 0));
            edge.GradientStops.Add(new GradientStop(Alpha(tone, 154), 0.32));
            edge.GradientStops.Add(new GradientStop(Alpha(tone, 61), 0.69));
            edge.GradientStops.Add(new GradientStop(Alpha(tone, 209), 1));
            var rim = new Pen(edge, radius > 50 ? 1.35 : 1.05);
            drawing.DrawEllipse(null, rim, center, radius, radius);

            if (radius >= 35)
            {
                double inner = radius - Math.Min(3, radius * 0.04);
                DrawArc(drawing, center, inner, -142, -79, Color.FromArgb(136, 245, 245, 250), Alpha(tone, 30), 0.95);
                DrawArc(drawing, center, inner, 33, 107, Alpha(tone, 153), Alpha(tone, 28), 1.15);
            }
        }

        private static void DrawLight(DrawingContext drawing, Point center, double radius, Color tone)
        {
            if (radius >= 14)
                drawing.DrawEllipse(LightShadow, null, new Point(center.X + 4, center.Y + 8), radius * 1.15, radius * 1.14);

            drawing.DrawEllipse(LightBody, null, center, radius, radius);
            if (radius >= 17)
            {
                var refraction = new RadialGradientBrush
                {
                    Center = new Point(0.53, 0.88), GradientOrigin = new Point(0.71, 1.04), RadiusX = 0.64, RadiusY = 0.38,
                };
                refraction.GradientStops.Add(new GradientStop(Alpha(tone, 139), 0));
                refraction.GradientStops.Add(new GradientStop(Alpha(tone, 48), 0.52));
                refraction.GradientStops.Add(new GradientStop(Alpha(tone, 0), 1));
                drawing.DrawEllipse(refraction, null, center, radius, radius);
                drawing.DrawEllipse(LightRimFrost, null, center, radius, radius);
                drawing.DrawEllipse(LightGlint, null, center, radius, radius);
                drawing.DrawEllipse(FrostGrain.Value, null, center, radius - 0.9, radius - 0.9);
            }

            var edge = new LinearGradientBrush { StartPoint = new Point(0.13, 0), EndPoint = new Point(0.85, 1) };
            edge.GradientStops.Add(new GradientStop(Colors.White, 0));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(190, 255, 254, 251), 0.28));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(125, 145, 136, 119), 0.64));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(177, 122, 132, 130), 1));
            var rim = new Pen(edge, radius >= 50 ? 1.55 : 1.2);
            drawing.DrawEllipse(null, rim, center, radius, radius);
            if (radius >= 23)
            {
                double inner = radius - Math.Min(3.2, radius * 0.062);
                DrawArc(drawing, center, inner, -156, -68, Colors.White, Color.FromArgb(13, 255, 255, 255), 2.15);
                DrawArc(drawing, center, inner, 26, 116, Color.FromArgb(202, 255, 255, 251), Color.FromArgb(13, 255, 255, 251), 1.65);
            }
        }

        private static RadialGradientBrush FrostGradient(Point origin, double radiusX, double radiusY,
            params (double Offset, byte Alpha, byte Red, byte Green, byte Blue)[] stops)
        {
            var brush = new RadialGradientBrush { GradientOrigin = origin, RadiusX = radiusX, RadiusY = radiusY };
            foreach (var stop in stops)
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(stop.Alpha, stop.Red, stop.Green, stop.Blue), stop.Offset));
            brush.Freeze();
            return brush;
        }

        private static Brush CreateFrostGrain()
        {
            const int size = 96;
            var random = new Random(84);
            var pixels = new byte[size * size * 4];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                byte shade = random.Next(3) == 0 ? (byte)118 : (byte)255;
                pixels[index] = shade;
                pixels[index + 1] = shade;
                pixels[index + 2] = shade;
                pixels[index + 3] = (byte)random.Next(1, 11);
            }
            var tile = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            var grain = new ImageBrush(tile)
            {
                Viewport = new Rect(0, 0, size, size), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile,
            };
            grain.Freeze();
            return grain;
        }

        private static void DrawArc(DrawingContext drawing, Point center, double radius, double fromAngle, double toAngle,
            Color from, Color to, double stroke)
        {
            var arc = new StreamGeometry();
            using (StreamGeometryContext geometry = arc.Open())
            {
                geometry.BeginFigure(AtAngle(center, radius, fromAngle), false, false);
                geometry.ArcTo(AtAngle(center, radius, toAngle), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            drawing.DrawGeometry(null, new Pen(new LinearGradientBrush(from, to, 25), stroke), arc);
        }

        private static Point AtAngle(Point center, double radius, double degrees)
        {
            double angle = degrees * Math.PI / 180;
            return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        }

        private static Color Tint(Color tone, double amount, byte alpha) => Color.FromArgb(alpha,
            (byte)(6 + (tone.R - 6) * amount), (byte)(12 + (tone.G - 12) * amount), (byte)(23 + (tone.B - 23) * amount));

        private static Color Alpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    internal sealed class StorageIllustration : FrameworkElement
    {
        private static readonly Geometry FolderGlyph = Geometry.Parse(UiVisuals.Folder);

        protected override void OnRender(DrawingContext drawing)
        {
            const double width = 280;
            const double height = 174;
            var center = new Point(width / 2, height / 2);
            double scale = Math.Min(ActualWidth / width, ActualHeight / height);
            if (scale <= 0)
                return;

            drawing.PushTransform(new TranslateTransform((ActualWidth - width * scale) / 2, (ActualHeight - height * scale) / 2));
            drawing.PushTransform(new ScaleTransform(scale, scale));

            UiVisuals.DrawBlush(drawing, new Rect(0, 0, width, height));
            Color lens = Appearance.IsLight ? Color.FromRgb(189, 174, 174) : Color.FromRgb(159, 186, 201);
            Color smallLens = Appearance.IsLight ? Color.FromRgb(184, 176, 165) : Color.FromRgb(162, 175, 191);
            GlassOrb.Draw(drawing, center, 52, lens);
            GlassOrb.Draw(drawing, center + new Vector(-73, -23), 19, smallLens);
            GlassOrb.Draw(drawing, center + new Vector(73, -23), 19, smallLens);
            GlassOrb.Draw(drawing, center + new Vector(-67, 26), 14, smallLens);
            GlassOrb.Draw(drawing, center + new Vector(67, 26), 14, smallLens);
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
