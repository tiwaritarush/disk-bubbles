using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace DiskBubbles
{
    internal sealed class RecycleCelebration : FrameworkElement
    {
        internal const double Duration = 4.6;
        // The scene is authored over 4.6 s; playing it 1.7x faster keeps every pose and ends in about 2.7 s.
        private const double Speed = 1.7;
        private static readonly Geometry Body = Shape("M 280,159 L 440,159 426,280 Q 424,294 409,294 L 311,294 Q 296,294 294,280 Z");
        private static readonly Geometry Smile = Shape("M -11,8 C -5,17 5,17 11,8");
        private static readonly Geometry Side = Shape("M 280,159 L 307,172 320,294 311,294 Q 296,294 294,280 Z");
        private static readonly Geometry Lip = Shape("M 280,158 Q 360,189 440,158 L 439,167 Q 360,196 281,167 Z");
        private readonly Stopwatch _clock = new Stopwatch();
        private bool _running;
        private bool _light;
        private double _time;
        private Brush _body, _side, _lid, _rim, _inside, _bubble, _green, _pink, _eye, _shine, _shadow;
        private Pen _edge, _groove, _face, _gleam;
        internal bool IsRunning => _running;

        public RecycleCelebration()
        {
            IsHitTestVisible = false;
            IsVisibleChanged += (sender, args) => { if (IsVisible) Restart(); else Stop(); };
            Unloaded += (sender, args) => Stop();
        }

        internal void Restart()
        {
            Stop();
            _time = SystemParameters.ClientAreaAnimation ? 0 : 1.35;
            InvalidateVisual();
            if (!IsVisible || !SystemParameters.ClientAreaAnimation)
                return;
            _clock.Restart();
            _running = true;
            CompositionTarget.Rendering += Advance;
        }

        private void Stop()
        {
            CompositionTarget.Rendering -= Advance;
            _clock.Stop();
            _running = false;
        }

        private void Advance(object sender, EventArgs args)
        {
            _time = Math.Min(Duration, _clock.Elapsed.TotalSeconds * Speed);
            InvalidateVisual();
            if (_time >= Duration || !IsVisible)
                Stop();
        }

        internal void RenderAt(double seconds)
        {
            Stop();
            _time = Math.Max(0, Math.Min(Duration, seconds));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            if (_body == null || _light != Appearance.IsLight)
                Palette();
            if (ActualWidth < 1 || ActualHeight < 1)
                return;
            double scale = Math.Min(ActualWidth / 720, ActualHeight / 350);
            drawing.PushTransform(new TranslateTransform((ActualWidth - 720 * scale) / 2, (ActualHeight - 350 * scale) / 2));
            drawing.PushTransform(new ScaleTransform(scale, scale));
            DrawScene(drawing, _time);
            drawing.Pop();
            drawing.Pop();
        }

        private void DrawScene(DrawingContext dc, double time)
        {
            double vanish = Smooth((time - 3.88) / 0.58);
            double drop = time < 0.58 ? -330 * (1 - Math.Pow(time / 0.58, 2)) :
                time < 1.02 ? -34 * Math.Sin((time - 0.58) / 0.44 * Math.PI) : 0;
            double impact = Math.Exp(-Math.Pow((time - 0.59) / 0.065, 2));
            dc.PushOpacity((1 - vanish) * Smooth(time / 0.58));
            dc.DrawEllipse(_shadow, null, new Point(360, 307), 104 - 20 * Math.Min(1, -drop / 330), 13);
            dc.Pop();

            double orbit = Clamp((time - 1.25) / 1.85);
            double angle = Math.PI + orbit * Math.PI * 3.5;
            Point mascot = time < 1.25
                ? new Point(232, 420 - 146 * EaseOut((time - 0.78) / 0.47))
                : new Point(360 + 128 * Math.Cos(angle), 274 - 176 * orbit + 18 * Math.Sin(angle));
            bool behind = Math.Sin(angle) < 0 && time >= 1.25;
            if (behind && time < 3.1)
                DrawMascot(dc, mascot, time, orbit);

            dc.PushOpacity(1 - vanish);
            dc.PushTransform(new ScaleTransform(1 - vanish, 1 - vanish, 360, 307));
            dc.PushTransform(new TranslateTransform(0, drop));
            dc.PushTransform(new ScaleTransform(1 + 0.06 * impact, 1 - 0.09 * impact, 360, 295));
            dc.DrawEllipse(_rim, _edge, new Point(360, 159), 84, 23);
            dc.DrawEllipse(_inside, null, new Point(360, 157), 74, 16);
            dc.DrawGeometry(_body, _edge, Body);
            dc.DrawGeometry(_side, null, Side);
            for (int i = -1; i <= 1; i++)
            {
                double x = 360 + i * 34;
                dc.DrawLine(_groove, new Point(x - i * 3, 194), new Point(x - i * 6, 265));
            }
            dc.DrawEllipse(_inside, null, new Point(360, 158), 74, 16);
            double open = 67.5 * EaseOut((time - 0.6) / 0.48);
            if (time > 0.76 && time < 1.26)
                open += 7 * Math.Sin((time - 0.76) * 18) * (1 - Clamp((time - 0.76) / 0.5));
            dc.PushTransform(new RotateTransform(open, 438, 151));
            dc.DrawRoundedRectangle(_lid, _edge, new Rect(275, 132, 168, 24), 11, 11);
            dc.DrawRoundedRectangle(_lid, _edge, new Rect(340, 116, 42, 21), 9, 9);
            dc.DrawLine(_gleam, new Point(289, 137), new Point(426, 137));
            dc.Pop();
            dc.DrawEllipse(_rim, _edge, new Point(438, 151), 7, 7);
            if (time >= 3.1 && time < 3.9)
                DrawParticles(dc, (time - 3.1) / 0.8);
            dc.DrawGeometry(_rim, null, Lip);
            dc.Pop();
            dc.Pop();
            dc.Pop();

            if (!behind && time < 3.1)
                DrawMascot(dc, mascot, time, orbit);
            if (time >= 3.1 && time < 3.33)
            {
                double pop = Clamp((time - 3.1) / 0.23);
                dc.PushOpacity(1 - pop);
                dc.DrawEllipse(null, new Pen(_shine, 2 * (1 - pop)), new Point(360, 116), 44 + 24 * pop, 44 + 24 * pop);
                dc.Pop();
            }
            if (time >= 4.26 && time < 4.6)
            {
                dc.PushOpacity(Math.Sin((time - 4.26) / 0.34 * Math.PI));
                dc.DrawEllipse(_shine, null, new Point(360, 307), 2, 2);
                dc.Pop();
            }
        }

        private void DrawMascot(DrawingContext dc, Point center, double time, double orbit)
        {
            double entry = EaseOut((time - 0.78) / 0.36);
            if (entry <= 0)
                return;
            double anticipation = time > 2.94 ? 1 + 0.16 * Smooth((time - 2.94) / 0.16) : 1;
            double size = entry * anticipation * (0.9 + 0.1 * Math.Sin(Math.PI + orbit * Math.PI * 3.5));
            dc.PushTransform(new TranslateTransform(center.X, center.Y));
            dc.PushTransform(new ScaleTransform(size, size));
            dc.PushTransform(new RotateTransform(9 * Math.Sin(orbit * Math.PI * 3.5)));
            dc.DrawEllipse(_bubble, _edge, new Point(), 43, 43);
            dc.DrawEllipse(_green, null, new Point(), 42, 42);
            dc.DrawEllipse(_pink, null, new Point(), 42, 42);
            dc.DrawEllipse(_shine, null, new Point(-17, -21), 9, 4);
            dc.DrawEllipse(_eye, null, new Point(-13, -1), 4.6, 7.5);
            dc.DrawEllipse(_eye, null, new Point(13, -1), 4.6, 7.5);
            dc.DrawEllipse(_shine, null, new Point(-14, -3), 1.4, 2);
            dc.DrawEllipse(_shine, null, new Point(12, -3), 1.4, 2);
            dc.DrawGeometry(null, _face, Smile);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private void DrawParticles(DrawingContext dc, double progress)
        {
            for (int i = 0; i < 24; i++)
            {
                double t = Clamp(progress * 1.16 - i * 0.006);
                double spread = Math.Sin(i * 2.399);
                Point start = new Point(360 + 12 * Math.Cos(i * 2.399), 116 + 9 * Math.Sin(i * 2.399));
                Point control = new Point(360 + spread * 77, 55 - (i % 5) * 7);
                Point end = new Point(360 + spread * 54, 157 + (i % 3 - 1) * 3);
                double x = (1 - t) * (1 - t) * start.X + 2 * t * (1 - t) * control.X + t * t * end.X;
                double y = (1 - t) * (1 - t) * start.Y + 2 * t * (1 - t) * control.Y + t * t * end.Y;
                dc.PushOpacity(1 - Smooth((t - 0.86) / 0.14));
                dc.DrawEllipse(i % 3 == 0 ? _pink : i % 3 == 1 ? _green : _shine, null,
                    new Point(x, y), 2.6 + i % 3, 2.6 + i % 3);
                dc.Pop();
            }
        }

        private void Palette()
        {
            _light = Appearance.IsLight;
            _body = Linear(_light ? "#EFE8DE" : "#9AA6A1", _light ? "#C6B7A7" : "#4C5853");
            _side = Solid(_light ? "#25A58D7B" : "#35212D27");
            _lid = Linear(_light ? "#FAF4EB" : "#C4CDC8", _light ? "#C7B8A8" : "#697870");
            _rim = Linear(_light ? "#E4D5C7" : "#A5B3AB", _light ? "#AA9B8C" : "#495B51");
            _inside = Solid(_light ? "#6B5C54" : "#273B30");
            _bubble = Linear("#F0FFF8", _light ? "#E8D1DE" : "#A5CCB5");
            _green = Glow(Color.FromArgb(_light ? (byte)95 : (byte)175, 43, 240, 138), new Point(0.24, 0.8));
            _pink = Glow(Color.FromArgb(_light ? (byte)160 : (byte)85, 230, 91, 196), new Point(0.86, 0.42));
            _eye = Solid("#23362C");
            _shine = Solid("#D9FFFFFF");
            _shadow = Glow(Color.FromArgb(_light ? (byte)48 : (byte)72, 0, 0, 0), new Point(0.5, 0.5));
            _edge = new Pen(Solid(_light ? "#7FFFFFFF" : "#7ADDEBE2"), 1.4);
            _groove = new Pen(Solid(_light ? "#727C6C5D" : "#67404C45"), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _face = new Pen(_eye, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _gleam = new Pen(_shine, 2);
            _edge.Freeze(); _groove.Freeze(); _face.Freeze(); _gleam.Freeze();
        }

        private static Brush Solid(string color) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush; }
        private static Brush Linear(string top, string bottom)
        {
            var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 35);
            brush.Freeze(); return brush;
        }
        private static Brush Glow(Color color, Point center)
        {
            var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B))
                { Center = center, GradientOrigin = center, RadiusX = 0.82, RadiusY = 0.82 };
            brush.Freeze(); return brush;
        }
        private static Geometry Shape(string data) { Geometry geometry = Geometry.Parse(data); geometry.Freeze(); return geometry; }
        private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
        private static double Smooth(double value) { value = Clamp(value); return value * value * (3 - 2 * value); }
        private static double EaseOut(double value) => 1 - Math.Pow(1 - Clamp(value), 3);
    }
}
