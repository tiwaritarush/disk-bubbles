using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        public const string Check = "M 5,12.5 L 10,17.5 19,7";
        public const string Close = "M 6,6 L 18,18 M 18,6 L 6,18";

        public static FrameworkElement Icon(string geometry, string color, double size = 16, double stroke = 1.5)
        {
            var icon = new Path
            {
                Data = Geometry.Parse(geometry), Width = size, Height = size, Stretch = Stretch.Uniform,
                StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round,
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
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            group.Children.Add(text);
            return group;
        }

        public static Border Surface(Border border, string fill, string edge = null)
        {
            border.SetResourceReference(Border.BackgroundProperty, fill);
            if (edge != null)
                border.SetResourceReference(Border.BorderBrushProperty, edge);
            return border;
        }

        /// <summary>
        /// A liquid-glass pane: a translucent body that lets the ambient light field through, a specular rim that
        /// is brightest where light enters (top left), a soft top sheen, and a faint dark outline for definition.
        /// </summary>
        public static Border FrostedPane(UIElement content, string fill = "GlassPane", double radius = 22)
        {
            var layers = new Grid();
            var sheen = new Border { CornerRadius = new CornerRadius(radius - 1), IsHitTestVisible = false };
            sheen.SetResourceReference(Border.BackgroundProperty, "GlassSheen");
            layers.Children.Add(sheen);
            layers.Children.Add(content);
            var inner = Surface(new Border { CornerRadius = new CornerRadius(radius - 1), BorderThickness = new Thickness(1), Child = layers },
                fill, "GlassInner");
            var outer = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Child = inner };
            outer.SetResourceReference(Border.BorderBrushProperty, "GlassEdge");
            return outer;
        }

        public static void Emphasize(Button button)
        {
            button.SetResourceReference(Control.BackgroundProperty, "AccentFill");
            button.SetResourceReference(Control.BorderBrushProperty, "AccentFill");
            button.SetResourceReference(Control.ForegroundProperty, "AccentInk");
        }

        public static Color KindColor(string kind) =>
            Application.Current.TryFindResource("Kind" + kind) is SolidColorBrush brush ? brush.Color : Colors.Gray;

        public static string KindLabel(string kind)
        {
            switch (kind)
            {
                case "Folder": return "Folder";
                case "Video": return "Video";
                case "Image": return "Image";
                case "Audio": return "Audio";
                case "Archive": return "Archive or disk image";
                case "App": return "App or system file";
                case "Document": return "Document";
                default: return "File";
            }
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
            dialog.SetResourceReference(Control.BackgroundProperty, "Workspace");
            ThemeCaption(dialog);
            var body = new StackPanel();
            var title = new TextBlock { Text = heading, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            body.Children.Add(title);
            var message = new TextBlock { Text = detail, FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
            message.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            body.Children.Add(new ScrollViewer
            {
                Content = message, Margin = new Thickness(0, 10, 0, 22), MaxHeight = 224,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 96 };
            Emphasize(close);
            body.Children.Add(close);
            var content = Surface(new Border { Child = body, Padding = new Thickness(26, 24, 26, 22) }, "Workspace");
            dialog.Content = content;
            var motion = new DialogMotion(dialog, content);
            close.Click += (sender, args) => motion.Close(dialog.Close);
            return dialog;
        }

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    /// <summary>
    /// Draws one liquid-glass bubble: a soft cast shadow, a nearly clear body whose edges thicken (Fresnel), the
    /// content colour pooling at the bottom like light through tinted glass, a coloured refraction ring, a
    /// specular crescent where light enters and a faint exit highlight opposite it.
    /// </summary>
    internal sealed class GlassOrb
    {
        private static readonly Dictionary<(Color Tint, bool Light), GlassOrb> Cache = new Dictionary<(Color, bool), GlassOrb>();

        private readonly Brush _shadow;
        private readonly Brush _body;
        private readonly Brush _pool;
        private readonly Brush _ring;
        private readonly Brush _sheen;
        private readonly Brush _glint;
        private readonly Brush _exit;
        private readonly Brush _quietBody;
        private readonly Color _rimLight;
        private readonly Color _rimShade;
        private readonly Dictionary<int, Pen> _rims = new Dictionary<int, Pen>();

        private GlassOrb(Color tint, bool light)
        {
            uint rgb = (uint)(tint.R << 16 | tint.G << 8 | tint.B);
            if (light)
            {
                _shadow = Radial(new Point(0.5, 0.5), 0.5, 0.5, (0, 0x2E5A3048), (0.6, 0x165A3048), (1, 0x005A3048));
                _body = Radial(new Point(0.5, 0.40), 0.58, 0.60, (0, 0x30FFFFFF), (0.7, 0x70FFFFFF), (1, 0xB0FFFFFF));
                _pool = Radial(new Point(0.52, 0.90), 0.56, 0.30, (0, rgb | 0x8C000000), (0.5, rgb | 0x40000000), (1, rgb));
                _ring = Radial(new Point(0.5, 0.5), 0.5, 0.5, (0.86, rgb), (0.95, rgb | 0x10000000), (1, rgb | 0x24000000));
                _sheen = Radial(new Point(0.36, 0.20), 0.44, 0.30, (0, 0xF2FFFFFF), (0.55, 0x66FFFFFF), (1, 0x00FFFFFF));
                _exit = Radial(new Point(0.66, 0.90), 0.30, 0.10, (0, 0x99FFFFFF), (1, 0x00FFFFFF));
                _quietBody = Radial(new Point(0.5, 0.45), 0.55, 0.55, (0, rgb | 0x1C000000), (1, rgb | 0x4D000000));
                _rimLight = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                _rimShade = Color.FromArgb(0x4D, 0x4A, 0x2F, 0x40);
            }
            else
            {
                _shadow = Radial(new Point(0.5, 0.5), 0.5, 0.5, (0, 0x8C000000), (0.6, 0x45000000), (1, 0x00000000));
                _body = Radial(new Point(0.5, 0.40), 0.58, 0.60, (0, 0x0DFFFFFF), (0.72, 0x14FFFFFF), (1, 0x2EFFFFFF));
                _pool = Radial(new Point(0.52, 0.92), 0.70, 0.46, (0, rgb | 0x5C000000), (0.45, rgb | 0x26000000), (1, rgb));
                _ring = Radial(new Point(0.5, 0.5), 0.5, 0.5, (0.72, rgb), (0.92, rgb | 0x14000000), (1, rgb | 0x3D000000));
                _sheen = Radial(new Point(0.36, 0.20), 0.44, 0.30, (0, 0x6BFFFFFF), (0.55, 0x22FFFFFF), (1, 0x00FFFFFF));
                _exit = Radial(new Point(0.66, 0.90), 0.30, 0.10, (0, 0x4DFFFFFF), (1, 0x00FFFFFF));
                _quietBody = Radial(new Point(0.5, 0.45), 0.55, 0.55, (0, rgb | 0x14000000), (1, rgb | 0x38000000));
                _rimLight = Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF);
                _rimShade = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
            }
            _glint = Radial(new Point(0.5, 0.5), 0.5, 0.5, (0, light ? 0xFFFFFFFFu : 0xE6FFFFFFu), (1, 0x00FFFFFF));
        }

        public static GlassOrb For(Color tint)
        {
            bool light = Appearance.IsLight;
            if (!Cache.TryGetValue((tint, light), out GlassOrb orb))
                Cache[(tint, light)] = orb = new GlassOrb(tint, light);
            return orb;
        }

        public static void Draw(DrawingContext drawing, Point center, double radius) =>
            For(UiVisuals.KindColor("Folder")).Draw(drawing, center, radius, quiet: false);

        public void Draw(DrawingContext drawing, Point center, double radius, bool quiet)
        {
            if (radius <= 0.5)
                return;
            if (quiet)
            {
                // Nested previews: glass without cast shadow or specular so they read as "inside".
                drawing.DrawEllipse(_quietBody, Rim(radius, 0.8), center, radius, radius);
                return;
            }

            if (radius >= 10)
                drawing.DrawEllipse(_shadow, null, new Point(center.X, center.Y + radius * 0.11), radius * 1.06, radius * 1.04);
            drawing.DrawEllipse(_body, null, center, radius, radius);
            if (!SystemParameters.HighContrast)
            {
                drawing.DrawEllipse(_pool, null, center, radius, radius);
                drawing.DrawEllipse(_ring, null, center, radius, radius);
            }
            drawing.DrawEllipse(_sheen, null, center, radius * 0.98, radius * 0.98);
            if (radius >= 16)
            {
                drawing.DrawEllipse(_exit, null, center, radius, radius);
                drawing.DrawEllipse(_glint, null, new Point(center.X - radius * 0.40, center.Y - radius * 0.52), radius * 0.10, radius * 0.06);
            }
            drawing.DrawEllipse(null, Rim(radius, 1), center, radius, radius);
        }

        private Pen Rim(double radius, double strength)
        {
            int width = (int)Math.Round(Math.Max(1, Math.Min(2.2, radius * 0.014)) * 10 * strength);
            if (_rims.TryGetValue(width, out Pen pen))
                return pen;
            var brush = new LinearGradientBrush { StartPoint = new Point(0.15, 0), EndPoint = new Point(0.85, 1) };
            brush.GradientStops.Add(new GradientStop(_rimLight, 0));
            brush.GradientStops.Add(new GradientStop(_rimShade, 0.48));
            brush.GradientStops.Add(new GradientStop(_rimShade, 0.70));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(_rimLight.A * 0.6), _rimLight.R, _rimLight.G, _rimLight.B), 1));
            brush.Freeze();
            pen = new Pen(brush, width / 10.0);
            pen.Freeze();
            _rims[width] = pen;
            return pen;
        }

        private static Brush Radial(Point center, double x, double y, params (double Offset, uint Argb)[] stops)
        {
            var brush = new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = x, RadiusY = y };
            foreach (var (offset, color) in stops)
                brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color), offset));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// Soft coloured light behind every glass surface. Without it there is nothing for the panes to refract and
    /// glass reads as flat grey. A fourth light follows the bubble under the pointer, tinted by its kind.
    /// </summary>
    internal sealed class AmbientField : FrameworkElement
    {
        private readonly RadialGradientBrush _focus = new RadialGradientBrush
            { GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 1) } };
        private Color _focusColor = Colors.Transparent;

        public AmbientField()
        {
            IsHitTestVisible = false;
            SizeChanged += (sender, args) => InvalidateVisual();
        }

        public void Focus(Color? tint)
        {
            Color target = tint.HasValue ? Color.FromArgb(Appearance.IsLight ? (byte)0x80 : (byte)0x66, tint.Value.R, tint.Value.G, tint.Value.B)
                : Colors.Transparent;
            if (target == _focusColor)
                return;
            _focusColor = target;
            var stop = _focus.GradientStops[0];
            if (!SystemParameters.ClientAreaAnimation)
            {
                stop.BeginAnimation(GradientStop.ColorProperty, null);
                stop.Color = target;
                return;
            }
            stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(target, TimeSpan.FromMilliseconds(420))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        protected override void OnRender(DrawingContext drawing)
        {
            double width = ActualWidth, height = ActualHeight;
            if (width <= 0 || height <= 0)
                return;
            drawing.DrawRectangle((Brush)FindResource("Base"), null, new Rect(0, 0, width, height));
            Light("AmbientA", 0.74, 0.58, 0.46, 0.62);
            Light("AmbientB", 0.08, 0.02, 0.38, 0.48);
            Light("AmbientC", 0.30, 1.02, 0.44, 0.36);
            Light("AmbientB", 0.98, 0.04, 0.22, 0.30);
            drawing.DrawEllipse(_focus, null, new Point(width * 0.68, height * 0.54), width * 0.30, height * 0.42);

            void Light(string key, double x, double y, double radiusX, double radiusY)
            {
                Color color = ((SolidColorBrush)FindResource(key)).Color;
                var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B));
                brush.GradientStops.Insert(1, new GradientStop(Color.FromArgb((byte)(color.A * 0.45), color.R, color.G, color.B), 0.5));
                brush.Freeze();
                drawing.DrawEllipse(brush, null, new Point(width * x, height * y), width * radiusX, height * radiusY);
            }
        }
    }

    /// <summary>A glass sheet over the browser that reports long work, so nothing ever looks frozen.</summary>
    internal sealed class WorkSheet : Grid
    {
        private readonly TextBlock _title = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
        private readonly TextBlock _detail = new TextBlock { FontSize = 14, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ProgressBar _meter = new ProgressBar { Height = 6, Width = 300, Maximum = 1, Margin = new Thickness(0, 18, 0, 2) };
        private bool _shown;

        public WorkSheet()
        {
            Visibility = Visibility.Collapsed;
            var scrim = new Border();
            scrim.SetResourceReference(Border.BackgroundProperty, "Scrim");
            Children.Add(scrim);
            _title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            _detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            _detail.Margin = new Thickness(0, 6, 0, 0);
            _meter.SetResourceReference(Control.ForegroundProperty, "Accent");
            _meter.SetResourceReference(Control.BackgroundProperty, "Line");
            _meter.Template = (ControlTemplate)Application.Current.FindResource("WorkMeter");
            AutomationProperties.SetName(_meter, "Progress");
            var body = new StackPanel { Margin = new Thickness(36, 28, 36, 28) };
            body.Children.Add(_title);
            body.Children.Add(_detail);
            body.Children.Add(_meter);
            var card = UiVisuals.FrostedPane(body, "GlassPane");
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.MinWidth = 380;
            card.MaxWidth = 520;
            Children.Add(card);
        }

        public void Show(string title, string detail, double? fraction = null)
        {
            _title.Text = title;
            _detail.Text = detail ?? "";
            _meter.IsIndeterminate = !fraction.HasValue && SystemParameters.ClientAreaAnimation;
            _meter.Value = fraction ?? 0;
            AutomationProperties.SetName(this, title + ". " + detail);
            if (_shown)
                return;
            _shown = true;
            Visibility = Visibility.Visible;
            if (SystemParameters.ClientAreaAnimation)
                BeginAnimation(OpacityProperty, UiMotion.Ease(0, 1, UiMotion.RevealMs));
        }

        public void Hide()
        {
            if (!_shown)
                return;
            _shown = false;
            BeginAnimation(OpacityProperty, null);
            Visibility = Visibility.Collapsed;
            _meter.IsIndeterminate = false;
        }
    }

    internal sealed class StorageIllustration : FrameworkElement
    {
        private static readonly (double X, double Y, double R, string Kind)[] Orbs =
        {
            (0, 0, 54, "Folder"), (-84, -30, 24, "Video"), (80, -34, 20, "Image"), (-70, 34, 15, "Archive"),
            (74, 30, 17, "Document"), (-122, 4, 9, "Audio"), (120, 0, 10, "App"),
        };

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
            foreach (var orb in Orbs)
                GlassOrb.For(UiVisuals.KindColor(orb.Kind)).Draw(drawing, center + new Vector(orb.X, orb.Y), orb.R, quiet: false);
            drawing.Pop();
            drawing.Pop();
        }
    }
}
