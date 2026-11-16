using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DiskBubbles
{
    internal sealed class Bubble
    {
        public DiskEntry Entry;
        public Point Center;
        public double Radius;
    }

    internal sealed class BubbleMap : FrameworkElement
    {
        private const int MaxBubbles = 11;
        private const double Gap = 13;
        private sealed class Layout
        {
            public Size Viewport;
            public double PixelsPerDip;
            public List<Bubble> Bubbles;
            public DrawingGroup Artwork;
            public bool Light;
            public DiskEntry Other;
            public int ListedCount;
        }
        private readonly Typeface _nameFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
            FontWeights.SemiBold, FontStretches.Normal);
        private readonly Typeface _sizeFont = new Typeface("Segoe UI");
        private readonly Brush _dot = new SolidColorBrush(Color.FromArgb(23, 179, 185, 198));
        private readonly Brush _nameInk = new SolidColorBrush(Color.FromRgb(240, 246, 255));
        private readonly Brush _sizeInk = new SolidColorBrush(Color.FromRgb(210, 212, 223));
        private readonly Brush _lightDot = new SolidColorBrush(Color.FromArgb(37, 90, 87, 77));
        private static readonly Brush LightNameInk = new SolidColorBrush(Color.FromRgb(35, 44, 42));
        private static readonly Brush LightSizeInk = new SolidColorBrush(Color.FromRgb(56, 68, 61));
        private Brush NameInk => Appearance.IsLight ? LightNameInk : _nameInk;
        private Brush SizeInk => Appearance.IsLight ? LightSizeInk : _sizeInk;
        private static readonly Geometry FolderBack = Geometry.Parse("M 1,11 Q 1,8 4,8 L 12,8 16,12 29,12 Q 31,12 31,15 L 31,25 Q 31,28 28,28 L 4,28 Q 1,28 1,25 Z");
        private static readonly Geometry FolderFront = Geometry.Parse("M 1,16 Q 1,13 4,13 L 28,13 Q 31,13 31,16 L 31,25 Q 31,28 28,28 L 4,28 Q 1,28 1,25 Z");
        private static readonly Geometry FileShape = Geometry.Parse("M 7,4 L 19,4 25,10 25,26 Q 25,28 23,28 L 9,28 Q 7,28 7,26 Z");
        private static readonly Geometry FileFold = Geometry.Parse("M 19,4 L 19,10 25,10 Z");
        private static readonly Brush FolderFill = new LinearGradientBrush(Color.FromRgb(225, 228, 237), Color.FromRgb(155, 163, 184), 90);
        private static readonly Brush FolderTab = new SolidColorBrush(Color.FromRgb(147, 151, 170));
        private static readonly Brush FileFill = new LinearGradientBrush(Color.FromRgb(237, 239, 246), Color.FromRgb(160, 169, 189), 90);
        private static readonly Brush FoldFill = new SolidColorBrush(Color.FromRgb(129, 138, 159));
        private static readonly Brush LightFolderFill = new LinearGradientBrush(Color.FromRgb(119, 154, 149), Color.FromRgb(45, 85, 87), 90);
        private static readonly Brush LightFileFill = new LinearGradientBrush(Color.FromRgb(120, 149, 147), Color.FromRgb(50, 82, 84), 90);
        private static readonly Brush LightIconTab = new SolidColorBrush(Color.FromRgb(82, 118, 112));
        private static readonly Color FolderGlass = Color.FromRgb(177, 182, 198);
        private static readonly Color FileGlass = Color.FromRgb(170, 173, 186);
        private static readonly Color OtherGlass = Color.FromRgb(139, 141, 154);
        private static readonly Color LightFolderGlass = Color.FromRgb(165, 181, 184);
        private static readonly Color LightFileGlass = Color.FromRgb(191, 184, 171);
        private static readonly Brush PressShade = new SolidColorBrush(Color.FromArgb(58, 9, 10, 13));

        private readonly ConditionalWeakTable<DiskEntry, Layout> _cache = new ConditionalWeakTable<DiskEntry, Layout>();
        private DiskEntry _folder;
        private List<Bubble> _bubbles = new List<Bubble>();
        private DiskEntry _selected;
        private HashSet<DiskEntry> _marked;
        private DiskEntry _hovered;
        private Point _hoverPoint;
        private DiskEntry _rowHovered;
        private DiskEntry _pressed;
        private Point _pressedAt;
        private bool _backgroundDown;
        private DrawingGroup _drawing = new DrawingGroup();
        private DrawingGroup _outgoing;
        private readonly DrawingGroup _pointerLight = new DrawingGroup();
        private readonly DrawingGroup _highlight = new DrawingGroup();
        private bool _transitioning;

        public BubbleMap()
        {
            ClipToBounds = true;
            SizeChanged += (sender, args) => Reflow(null, false);
        }

        public event Action<DiskEntry> Activated;
        public event Action<DiskEntry> HoverChanged;
        public event Action BackgroundActivated;
        public int VisibleCount => ListedCount + (Other == null ? 0 : 1);
        public DiskEntry Other { get; private set; }
        public int ListedCount { get; private set; }
        public DiskEntry Inspected => _hovered ?? _rowHovered;

        public void SetSelection(DiskEntry selected, IEnumerable<DiskEntry> additional)
        {
            if (_selected == selected && _marked == null && additional == null)
                return;
            _selected = selected;
            _marked = additional == null ? null : new HashSet<DiskEntry>(additional.Intersect(_bubbles.Select(bubble => bubble.Entry)));
            UpdateHighlight();
        }

        public void InvalidateLayout(IEnumerable<DiskEntry> folders)
        {
            foreach (DiskEntry folder in folders)
                _cache.Remove(folder);
        }

        public void UpdateAppearance() => Reflow(null, false);

        public void Reveal()
        {
            if (!SystemParameters.ClientAreaAnimation || _bubbles.Count == 0)
                return;
            var reveal = UiMotion.Ease(0.32, 1, UiMotion.RevealMs);
            reveal.FillBehavior = FillBehavior.Stop;
            _drawing.BeginAnimation(DrawingGroup.OpacityProperty, reveal);
        }

        public DiskEntry RowHover
        {
            get => _rowHovered;
            set
            {
                if (value == _rowHovered)
                    return;
                _rowHovered = value;
                UpdateHighlight();
            }
        }

        public void ShowEntries(DiskEntry folder, DiskEntry origin, bool returning)
        {
            _folder = folder;
            _hovered = null;
            _rowHovered = null;
            _pressed = null;
            _backgroundDown = false;
            HoverChanged?.Invoke(null);
            ToolTip = null;
            Cursor = Cursors.Arrow;
            Reflow(origin, returning);
        }

        private void Reflow(DiskEntry origin, bool returning)
        {
            if (_folder == null)
                return;

            List<Bubble> previous = _bubbles;
            DrawingGroup previousDrawing = _drawing;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (!_cache.TryGetValue(_folder, out Layout layout) || layout.Viewport != RenderSize || layout.PixelsPerDip != pixelsPerDip)
            {
                var items = _folder.Children;
                long largest = items.Count == 0 ? 0 : items[0].Bytes;
                var shown = items.Take(MaxBubbles)
                    .TakeWhile(item => item.Bytes > 0 && item.Bytes >= largest * 0.012)
                    .ToList();
                long remaining = _folder.Bytes;
                foreach (DiskEntry item in shown)
                    remaining = Math.Max(0, remaining - item.Bytes);

                if (remaining > 0 && shown.Count == MaxBubbles)
                {
                    remaining += shown[MaxBubbles - 1].Bytes;
                    shown.RemoveAt(MaxBubbles - 1);
                }

                DiskEntry other = layout?.Other;
                layout = new Layout { Viewport = RenderSize, PixelsPerDip = pixelsPerDip, ListedCount = shown.Count };
                if (remaining > 0)
                {
                    layout.Other = other ?? new DiskEntry("Other items", null, remaining);
                    shown.Add(layout.Other);
                    shown.Sort((first, second) => second.Bytes.CompareTo(first.Bytes));
                }

                layout.Bubbles = Pack(shown, ActualWidth, ActualHeight);
                _cache.Remove(_folder);
                _cache.Add(_folder, layout);
            }

            if (layout.Artwork == null || layout.Light != Appearance.IsLight)
            {
                layout.Light = Appearance.IsLight;
                layout.Artwork = new DrawingGroup();
                using (DrawingContext drawing = layout.Artwork.Open())
                    foreach (Bubble bubble in layout.Bubbles)
                        DrawBubble(drawing, bubble, bubble.Entry == layout.Other);
                layout.Artwork.Freeze();
            }

            Other = layout.Other;
            ListedCount = layout.ListedCount;
            _bubbles = layout.Bubbles;
            _outgoing = null;
            _transitioning = false;
            _drawing = new DrawingGroup();
            _drawing.Children.Add(layout.Artwork);

            Bubble anchor = (returning ? _bubbles : previous).Find(bubble => bubble.Entry == origin);
            if (origin != null && anchor != null && SystemParameters.ClientAreaAnimation && _bubbles.Count > 0)
                Animate(previousDrawing, anchor, returning);

            UpdateHighlight();
            InvalidateVisual();
        }

        private void Animate(DrawingGroup previous, Bubble anchor, bool returning)
        {
            _outgoing = previous;
            DrawingGroup incoming = _drawing;
            _transitioning = true;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            Vector delta = anchor.Center - center;
            double scale = Math.Max(0.22, Math.Min(0.75, anchor.Radius / (Math.Min(ActualWidth, ActualHeight) / 2)));
            var zoom = new ScaleTransform(1, 1, center.X, center.Y);
            var position = new TranslateTransform();
            var transform = new TransformGroup();
            transform.Children.Add(zoom);
            transform.Children.Add(position);
            incoming.Transform = transform;
            previous.Transform = previous.Transform?.CloneCurrentValue();

            double from = returning ? Math.Min(1.65, 1 / scale) : scale;
            double direction = returning ? -0.6 : 1;
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, UiMotion.Ease(from, 1, UiMotion.ZoomMs));
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, UiMotion.Ease(from, 1, UiMotion.ZoomMs));
            position.BeginAnimation(TranslateTransform.XProperty, UiMotion.Ease(delta.X * direction, 0, UiMotion.ZoomMs));
            position.BeginAnimation(TranslateTransform.YProperty, UiMotion.Ease(delta.Y * direction, 0, UiMotion.ZoomMs));
            previous.BeginAnimation(DrawingGroup.OpacityProperty, UiMotion.Ease(previous.Opacity, 0, UiMotion.ZoomFadeMs));
            var fade = UiMotion.Ease(0.24, 1, UiMotion.ZoomMs);
            fade.Completed += (sender, args) =>
            {
                if (_drawing != incoming)
                    return;

                _outgoing = null;
                _transitioning = false;
                incoming.BeginAnimation(DrawingGroup.OpacityProperty, null);
                incoming.Transform = Transform.Identity;
                UpdateHighlight();
                InvalidateVisual();
            };
            incoming.BeginAnimation(DrawingGroup.OpacityProperty, fade);
        }

        internal static List<Bubble> Pack(IReadOnlyList<DiskEntry> entries, double width, double height)
        {
            var bubbles = new List<Bubble>();
            double margin = Math.Max(15, Math.Min(38, Math.Min(width, height) * 0.08));
            if (entries.Count == 0 || entries[0].Bytes <= 0 || width <= margin * 2 || height <= margin * 2)
                return bubbles;

            double aspect = Math.Max(0.58, Math.Min(1.95, (width - 2 * margin) / (height - 2 * margin)));
            double largest = entries[0].Bytes;
            Rect bounds = Rect.Empty;

            foreach (DiskEntry entry in entries)
            {
                double radius = 100 * Math.Sqrt(entry.Bytes / largest);
                Point position = bounds.IsEmpty ? new Point() : new Point(bounds.Right + Gap + radius, 0);
                Rect bestBounds = Include(bounds, position, radius);
                if (bubbles.Count > 0)
                {
                    double bestScore = PlacementScore(bestBounds, position, aspect);
                    foreach (Bubble anchor in bubbles)
                    {
                        double distance = anchor.Radius + radius + Gap;
                        for (int step = 0; step < 48; step++)
                        {
                            double angle = (step + 0.5) * Math.PI / 24;
                            var point = new Point(anchor.Center.X + Math.Cos(angle) * distance,
                                anchor.Center.Y + Math.Sin(angle) * distance);
                            if (Overlaps(bubbles, point, radius))
                                continue;

                            Rect candidate = Include(bounds, point, radius);
                            double score = PlacementScore(candidate, point, aspect);
                            if (score < bestScore)
                            {
                                bestScore = score;
                                position = point;
                                bestBounds = candidate;
                            }
                        }
                    }
                }

                bubbles.Add(new Bubble { Entry = entry, Center = position, Radius = radius });
                bounds = bestBounds;
            }

            double scale = Math.Min((width - 2 * margin) / bounds.Width, (height - 2 * margin) / bounds.Height);
            scale = Math.Min(scale, 2.35);
            double offsetX = (width - bounds.Width * scale) / 2 - bounds.Left * scale;
            double offsetY = (height - bounds.Height * scale) / 2 - bounds.Top * scale;

            foreach (Bubble bubble in bubbles)
            {
                bubble.Center = new Point(bubble.Center.X * scale + offsetX, bubble.Center.Y * scale + offsetY);
                bubble.Radius *= scale;
            }

            return bubbles;
        }

        private static double PlacementScore(Rect bounds, Point added, double aspect)
        {
            double width = bounds.Width / aspect;
            double height = bounds.Height;
            var center = new Vector((bounds.Left + bounds.Right) / (2 * aspect), (bounds.Top + bounds.Bottom) / 2);
            return Math.Max(width, height) + 0.12 * (width + height) + 0.38 * center.Length + 0.06 * ((Vector)added).Length;
        }

        private static Rect Include(Rect bounds, Point point, double radius)
        {
            bounds.Union(new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2));
            return bounds;
        }

        private static bool Overlaps(List<Bubble> bubbles, Point point, double radius)
        {
            foreach (Bubble bubble in bubbles)
            {
                double minimum = bubble.Radius + radius + Gap - 0.01;
                if ((point - bubble.Center).LengthSquared < minimum * minimum)
                    return true;
            }
            return false;
        }

        protected override void OnRender(DrawingContext drawing)
        {
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            for (double y = GlassOrb.GridInset; y < ActualHeight; y += GlassOrb.GridStep)
                for (double x = GlassOrb.GridInset; x < ActualWidth; x += GlassOrb.GridStep)
                    drawing.DrawEllipse(Appearance.IsLight ? _lightDot : _dot, null, new Point(x, y), 0.65, 0.65);

            if (_outgoing != null)
                drawing.DrawDrawing(_outgoing);
            drawing.DrawDrawing(_drawing);
            drawing.DrawDrawing(_pointerLight);
            drawing.DrawDrawing(_highlight);
        }

        private void UpdateHighlight()
        {
            using (DrawingContext light = _pointerLight.Open())
            using (DrawingContext drawing = _highlight.Open())
            {
                if (_transitioning)
                    return;

                for (int index = 0; index < _bubbles.Count; index++)
                {
                    Bubble bubble = _bubbles[index];
                    if (bubble.Entry != _selected && bubble.Entry != _hovered && bubble.Entry != _rowHovered && bubble.Entry != _pressed &&
                        (_marked == null || !_marked.Contains(bubble.Entry)))
                        continue;

                    bool selected = bubble.Entry == _selected || (_marked != null && _marked.Contains(bubble.Entry));
                    bool pressed = bubble.Entry == _pressed;
                    bool pointer = bubble.Entry == _hovered;
                    if (pointer && !pressed && bubble.Radius >= 17 && SystemParameters.ClientAreaAnimation)
                        GlassOrb.ReflectPointer(light, bubble.Center, bubble.Radius, _hoverPoint);
                    Color outline = Appearance.IsLight
                        ? (selected ? Color.FromRgb(43, 99, 107) : Color.FromRgb(111, 132, 122))
                        : (selected ? Color.FromRgb(135, 183, 255) : Color.FromArgb(195, 233, 235, 242));
                    var pen = new Pen(new SolidColorBrush(outline), selected ? 2.1 : 1.35);
                    double rim = bubble.Radius + 0.5;
                    drawing.DrawEllipse(pressed ? PressShade : null, pen, bubble.Center, rim, rim);
                }
            }
        }

        private void DrawBubble(DrawingContext drawing, Bubble bubble, bool grouped)
        {
            Color tone = Appearance.IsLight ? (bubble.Entry.IsFolder ? LightFolderGlass : LightFileGlass)
                : grouped ? OtherGlass : bubble.Entry.IsFolder ? FolderGlass : FileGlass;
            GlassOrb.Draw(drawing, bubble.Center, bubble.Radius, tone, grouped);

            if (grouped && bubble.Radius < 39)
            {
                if (bubble.Radius >= 9)
                    DrawGroupDots(drawing, bubble.Center, Math.Min(3.4, bubble.Radius * 0.12));
                return;
            }

            if (bubble.Radius < 28)
            {
                if (bubble.Radius >= 12)
                    DrawGlyph(drawing, bubble, grouped, centered: true);
                return;
            }

            double nameSize = Math.Min(24, Math.Max(11, bubble.Radius * 0.157));
            bool showGlyph = bubble.Radius >= 39;
            bool system = bubble.Entry.IsSystem && bubble.Radius >= 49;
            double lift = system ? 7 : 0;
            double baseline = (showGlyph ? Math.Min(24, bubble.Radius * 0.13) : -nameSize * 0.54) - lift;
            if (showGlyph)
                DrawGlyph(drawing, bubble, grouped, lift: lift);
            DrawLabel(drawing, bubble, bubble.Entry.Name, _nameFont, nameSize, NameInk, baseline);
            double size = Math.Min(16, Math.Max(10, nameSize * 0.77));
            double sizeOffset = baseline + nameSize * 1.19;
            DrawLabel(drawing, bubble, bubble.Entry.SizeLabel, _sizeFont, size, SizeInk, sizeOffset);
            if (system)
                DrawSystemBadge(drawing, bubble, sizeOffset + size + 6);
        }

        private void DrawSystemBadge(DrawingContext drawing, Bubble bubble, double offset)
        {
            var bounds = new Rect(bubble.Center.X - 21, bubble.Center.Y + offset - 8, 42, 16);
            var fill = (Brush)FindResource("BadgeFill");
            var edge = new Pen((Brush)FindResource("BadgeEdge"), 1);
            drawing.DrawRoundedRectangle(fill, edge, bounds, 4, 4);
            DrawLabel(drawing, bubble, "System", _nameFont, 10, (Brush)FindResource("BadgeInk"), offset);
        }

        private void DrawGlyph(DrawingContext drawing, Bubble bubble, bool grouped, bool centered = false, double lift = 0)
        {
            double width = centered ? bubble.Radius * 0.97 : Math.Min(57, bubble.Radius * 0.49);
            double anchor = centered ? (bubble.Entry.IsFolder ? 0.56 : 0.5) : 0.93;
            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(width / 32, width / 32));
            transform.Children.Add(new TranslateTransform(bubble.Center.X - width / 2, bubble.Center.Y - width * anchor - lift));
            drawing.PushTransform(transform);
            if (grouped)
                DrawGroupDots(drawing, new Point(16, 17), 2.8);
            else if (bubble.Entry.IsFolder)
            {
                drawing.DrawGeometry(Appearance.IsLight ? LightIconTab : FolderTab, null, FolderBack);
                drawing.DrawGeometry(Appearance.IsLight ? LightFolderFill : FolderFill, null, FolderFront);
            }
            else
            {
                drawing.DrawGeometry(Appearance.IsLight ? LightFileFill : FileFill, null, FileShape);
                drawing.DrawGeometry(Appearance.IsLight ? LightIconTab : FoldFill, null, FileFold);
            }
            drawing.Pop();
        }

        private void DrawGroupDots(DrawingContext drawing, Point center, double size)
        {
            for (int i = -1; i <= 1; i++)
                drawing.DrawEllipse(SizeInk, null, new Point(center.X + i * size * 3.2, center.Y), size, size);
        }

        private void DrawLabel(DrawingContext drawing, Bubble bubble, string value, Typeface font,
            double fontSize, Brush brush, double yOffset)
        {
            double textWidth = bubble.Radius * 1.61;
            var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                font, fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = textWidth,
                MaxLineCount = 1,
                TextAlignment = TextAlignment.Center,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            drawing.DrawText(text, new Point(bubble.Center.X - textWidth / 2,
                bubble.Center.Y + yOffset - text.Height / 2));
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            TrackPointer(args.GetPosition(this));
        }

        private void TrackPointer(Point pointer)
        {
            if (_transitioning)
                return;
            DiskEntry entry = Hit(pointer);
            if (entry == _hovered)
            {
                if (entry != null && SystemParameters.ClientAreaAnimation && (pointer - _hoverPoint).LengthSquared >= 9)
                {
                    _hoverPoint = pointer;
                    UpdateHighlight();
                }
                return;
            }

            _hovered = entry;
            _hoverPoint = pointer;
            _pointerLight.BeginAnimation(DrawingGroup.OpacityProperty, null);
            if (entry != null && SystemParameters.ClientAreaAnimation)
            {
                var light = UiMotion.Ease(0.3, 1, UiMotion.HoverMs);
                light.FillBehavior = FillBehavior.Stop;
                _pointerLight.BeginAnimation(DrawingGroup.OpacityProperty, light);
            }
            Cursor = entry == null ? Cursors.Arrow : Cursors.Hand;
            ToolTip = entry == null ? null : entry.Name + (entry.IsSystem ? "  ·  System" : "") + "\n" + entry.SizeLabel + "  ·  " +
                ((double)entry.Bytes / _folder.Bytes).ToString("P1") + " of this view";
            HoverChanged?.Invoke(entry);
            UpdateHighlight();
        }

        protected override void OnMouseLeave(MouseEventArgs args)
        {
            base.OnMouseLeave(args);
            _hovered = null;
            _pressed = null;
            _backgroundDown = false;
            HoverChanged?.Invoke(null);
            ToolTip = null;
            Cursor = Cursors.Arrow;
            UpdateHighlight();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
        {
            base.OnMouseLeftButtonDown(args);
            args.Handled = Press(args.GetPosition(this), args.ClickCount);
        }

        private bool Press(Point position, int clicks)
        {
            TrackPointer(position);
            _pressed = Hit(position);
            _pressedAt = position;
            if (_pressed == null && _backgroundDown && clicks == 2)
            {
                _backgroundDown = false;
                BackgroundActivated?.Invoke();
                return true;
            }

            _backgroundDown = _pressed == null;
            UpdateHighlight();
            return _pressed != null;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
        {
            base.OnMouseLeftButtonUp(args);
            args.Handled = Release(args.GetPosition(this));
        }

        private bool Release(Point position)
        {
            if (_pressed == null)
                return false;
            DiskEntry pressed = _pressed;
            _pressed = null;
            UpdateHighlight();
            bool remainedInPlace = Math.Abs(position.X - _pressedAt.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _pressedAt.Y) <= SystemParameters.MinimumVerticalDragDistance;
            if (pressed == Hit(position) || (_transitioning && remainedInPlace))
                Activated?.Invoke(pressed);
            return true;
        }

        private DiskEntry Hit(Point position)
        {
            if (_transitioning && _drawing.Transform.Inverse is GeneralTransform inverse)
                position = inverse.Transform(position);
            foreach (Bubble bubble in _bubbles)
                if ((position - bubble.Center).LengthSquared <= bubble.Radius * bubble.Radius)
                    return bubble.Entry;
            return null;
        }

    }
}
