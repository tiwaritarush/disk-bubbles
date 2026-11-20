using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
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

    internal sealed class BubbleMap : Grid
    {
        public const int PageSize = 12;
        private const double Gap = 8;
        private static readonly Vector[] Directions = Enumerable.Range(0, 24).Select(step =>
        {
            double angle = step * Math.PI / 12;
            return new Vector(Math.Cos(angle), Math.Sin(angle));
        }).ToArray();
        private sealed class Layout
        {
            public Size Viewport;
            public double PixelsPerDip;
            public List<Bubble> Bubbles;
            public DrawingGroup Artwork;
            public bool Light;
            public int Page;
        }

        private sealed class MapLayer : FrameworkElement
        {
            public DrawingGroup Artwork { get; private set; }

            public void Show(DrawingGroup artwork)
            {
                if (Artwork == artwork)
                    return;
                Artwork = artwork;
                InvalidateVisual();
            }

            public void Place(Matrix pose, double opacity)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = opacity;
                RenderTransform = new MatrixTransform(pose);
            }

            public void MoveTo(Matrix target, double opacity, int duration, EventHandler finished = null)
            {
                Matrix start = RenderTransform.Value;
                var scale = new ScaleTransform(start.M11, start.M22);
                var translation = new TranslateTransform(start.OffsetX, start.OffsetY);
                var transform = new TransformGroup();
                transform.Children.Add(scale);
                transform.Children.Add(translation);
                RenderTransform = transform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, UiMotion.MapTween(start.M11, target.M11, duration));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, UiMotion.MapTween(start.M22, target.M22, duration));
                translation.BeginAnimation(TranslateTransform.XProperty, UiMotion.MapTween(start.OffsetX, target.OffsetX, duration));
                translation.BeginAnimation(TranslateTransform.YProperty, UiMotion.MapTween(start.OffsetY, target.OffsetY, duration));
                var fade = UiMotion.MapTween(Opacity, opacity, duration);
                if (finished != null)
                    fade.Completed += finished;
                BeginAnimation(OpacityProperty, fade);
            }

            protected override void OnRender(DrawingContext drawing)
            {
                if (Artwork != null)
                    drawing.DrawDrawing(Artwork);
            }
        }

        private readonly Typeface _nameFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
            FontWeights.SemiBold, FontStretches.Normal);
        private readonly Typeface _sizeFont = new Typeface("Segoe UI");
        private Brush NameInk => (Brush)FindResource("Ink");
        private Brush SizeInk => (Brush)FindResource("Muted");
        private static readonly Geometry FolderBack = Geometry.Parse("M 1,11 Q 1,8 4,8 L 12,8 16,12 29,12 Q 31,12 31,15 L 31,25 Q 31,28 28,28 L 4,28 Q 1,28 1,25 Z");
        private static readonly Geometry FolderFront = Geometry.Parse("M 1,16 Q 1,13 4,13 L 28,13 Q 31,13 31,16 L 31,25 Q 31,28 28,28 L 4,28 Q 1,28 1,25 Z");
        private static readonly Geometry FileShape = Geometry.Parse("M 7,4 L 19,4 25,10 25,26 Q 25,28 23,28 L 9,28 Q 7,28 7,26 Z");
        private static readonly Geometry FileFold = Geometry.Parse("M 19,4 L 19,10 25,10 Z");
        private static readonly Brush FolderFill = new LinearGradientBrush(Color.FromRgb(236, 236, 236), Color.FromRgb(174, 177, 177), 90);
        private static readonly Brush FolderTab = new SolidColorBrush(Color.FromRgb(148, 150, 150));
        private static readonly Brush FileFill = new LinearGradientBrush(Color.FromRgb(236, 236, 236), Color.FromRgb(183, 185, 185), 90);
        private static readonly Brush FoldFill = new SolidColorBrush(Color.FromRgb(151, 153, 153));
        private static readonly Brush LightFolderFill = new LinearGradientBrush(Color.FromRgb(49, 47, 46), Color.FromRgb(5, 5, 5), 90);
        private static readonly Brush LightFileFill = new LinearGradientBrush(Color.FromRgb(65, 62, 59), Color.FromRgb(22, 21, 20), 90);
        private static readonly Brush LightIconTab = new SolidColorBrush(Color.FromRgb(76, 72, 69));
        private static readonly Brush PressShade = new SolidColorBrush(Color.FromArgb(58, 10, 10, 10));
        private static readonly Brush DarkReflection = Glow(Color.FromArgb(38, 238, 238, 238), 0.5, 0.5);
        private static readonly Brush LightReflection = Glow(Color.FromArgb(74, 255, 255, 255), 0.5, 0.5);

        private readonly ConditionalWeakTable<DiskEntry, Layout> _cache = new ConditionalWeakTable<DiskEntry, Layout>();
        private DiskEntry _folder;
        private List<Bubble> _bubbles = new List<Bubble>();
        private DiskEntry _selected;
        private ICollection<DiskEntry> _marked;
        private DiskEntry _hovered;
        private Point _hoverPoint;
        private DiskEntry _rowHovered;
        private DiskEntry _pressed;
        private Point _pressedAt;
        private bool _backgroundDown;
        private MapLayer _front = new MapLayer { IsHitTestVisible = false };
        private MapLayer _rear = new MapLayer { IsHitTestVisible = false, Opacity = 0 };
        private readonly DrawingGroup _pointerLight = new DrawingGroup();
        private readonly DrawingGroup _highlight = new DrawingGroup();
        private readonly TranslateTransform _reflectionPosition = new TranslateTransform();
        private EventHandler _startOnFrame;
        private int _transitionId;
        private bool _transitioning;

        public BubbleMap()
        {
            ClipToBounds = true;
            if ((RenderCapability.Tier >> 16) > 0)
                _front.CacheMode = _rear.CacheMode = new BitmapCache(1.18);
            Children.Add(_rear);
            Children.Add(_front);
            var effects = new MapLayer { IsHitTestVisible = false };
            SetZIndex(effects, 2);
            var artwork = new DrawingGroup();
            artwork.Children.Add(_pointerLight);
            artwork.Children.Add(_highlight);
            effects.Show(artwork);
            Children.Add(effects);
            SizeChanged += (sender, args) => ResizeArtwork();
            Unloaded += (sender, args) => Settle();
        }

        public event Action<DiskEntry> Activated;
        public event Action<DiskEntry> ContextRequested;
        public event Action<DiskEntry> HoverChanged;
        public event Action BackgroundActivated;
        public int VisibleCount => _bubbles.Count;
        public int Page { get; private set; }
        public int PageCount => _folder == null ? 0 : (_folder.Children.Count + PageSize - 1) / PageSize;
        public DiskEntry Inspected => _hovered ?? _rowHovered;

        public void SetSelection(DiskEntry selected, ICollection<DiskEntry> marked)
        {
            _selected = selected;
            _marked = marked;
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
            _front.BeginAnimation(OpacityProperty, reveal);
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

        public void ShowEntries(DiskEntry folder, int page, DiskEntry origin, bool returning, bool animate)
        {
            _folder = folder;
            Page = Math.Max(0, Math.Min(page, PageCount - 1));
            ResetPointer();
            Reflow(origin, returning, animate: animate);
        }

        public void ChangePage(int page)
        {
            if (page < 0 || page >= PageCount || page == Page)
                return;
            int direction = Math.Sign(page - Page);
            Page = page;
            ResetPointer();
            Reflow(null, false, animate: true, pageDirection: direction);
        }

        private void ResetPointer()
        {
            _hovered = null;
            _rowHovered = null;
            _pressed = null;
            _backgroundDown = false;
            HoverChanged?.Invoke(null);
            ToolTip = null;
            Cursor = Cursors.Arrow;
        }

        private void ResizeArtwork()
        {
            if (_folder == null)
                return;

            _front.Show(PrepareArtwork());
            UpdateHighlight();
            InvalidateVisual();
        }

        private DrawingGroup PrepareArtwork()
        {
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (!_cache.TryGetValue(_folder, out Layout layout) || layout.Viewport != RenderSize ||
                layout.PixelsPerDip != pixelsPerDip || layout.Page != Page)
            {
                var items = _folder.Children;
                int start = Page * PageSize;
                layout = new Layout { Viewport = RenderSize, PixelsPerDip = pixelsPerDip, Page = Page,
                    Bubbles = Pack(items.GetRange(start, Math.Min(PageSize, items.Count - start)), ActualWidth, ActualHeight) };
                _cache.Remove(_folder);
                _cache.Add(_folder, layout);
            }

            if (layout.Artwork == null || layout.Light != Appearance.IsLight)
            {
                layout.Light = Appearance.IsLight;
                layout.Artwork = new DrawingGroup();
                using (DrawingContext drawing = layout.Artwork.Open())
                    foreach (Bubble bubble in layout.Bubbles)
                        DrawBubble(drawing, bubble);
                layout.Artwork.Freeze();
            }

            _bubbles = layout.Bubbles;
            return layout.Artwork;
        }

        private void Reflow(DiskEntry origin, bool returning, bool animate = false, int pageDirection = 0)
        {
            if (_folder == null)
                return;

            List<Bubble> previous = _bubbles;
            DrawingGroup artwork = PrepareArtwork();
            Bubble anchor = (returning ? _bubbles : previous).Find(bubble => bubble.Entry == origin);
            if (animate && SystemParameters.ClientAreaAnimation && IsVisible && _front.Artwork != null)
                Animate(artwork, anchor, returning, pageDirection);
            else
            {
                _front.Show(artwork);
                Settle();
            }

            UpdateHighlight();
            InvalidateVisual();
        }

        private void Animate(DrawingGroup artwork, Bubble anchor, bool returning, int pageDirection)
        {
            CancelFrameStart();
            int transitionId = ++_transitionId;
            if (_rear.Opacity > _front.Opacity)
                anchor = null;
            else
                (_front, _rear) = (_rear, _front);

            MapLayer incoming = _front;
            MapLayer outgoing = _rear;
            SetZIndex(outgoing, 0);
            SetZIndex(incoming, 1);
            outgoing.Place(outgoing.RenderTransform.Value, outgoing.Opacity);
            incoming.Show(artwork);
            _transitioning = true;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            Point pivot = anchor == null ? center : returning ? anchor.Center : outgoing.RenderTransform.Value.Transform(anchor.Center);
            Vector offset = pivot - center;
            Matrix incomingStart;
            Matrix outgoingEnd;
            if (pageDirection != 0)
            {
                var slide = new Vector(30 * pageDirection, 0);
                incomingStart = Pose(center, 1, slide);
                outgoingEnd = Pose(center, 1, -slide);
            }
            else
            {
                double entryScale = anchor == null ? 0.91 : Math.Max(0.60, Math.Min(0.83, anchor.Radius / center.Y + 0.31));
                incomingStart = Pose(center, returning ? 1.14 : entryScale, offset * (returning ? -0.20 : 1));
                outgoingEnd = Pose(returning ? center : pivot, returning ? 0.73 : 1.35,
                    offset * (returning ? 0.86 : -0.64));
            }

            incoming.Place(incomingStart, 0);
            _startOnFrame = (sender, args) =>
            {
                CancelFrameStart();
                int duration = pageDirection != 0 ? UiMotion.PageSlideMs : returning ? UiMotion.ZoomBackMs : UiMotion.ZoomMs;
                outgoing.MoveTo(outgoingEnd, 0, duration);
                incoming.MoveTo(Matrix.Identity, 1, duration, (clock, completed) =>
                {
                    if (_transitionId != transitionId)
                        return;
                    Settle();
                    if (IsMouseOver)
                        TrackPointer(Mouse.GetPosition(this));
                    UpdateHighlight();
                });
            };
            CompositionTarget.Rendering += _startOnFrame;
        }

        private void CancelFrameStart()
        {
            if (_startOnFrame == null)
                return;
            CompositionTarget.Rendering -= _startOnFrame;
            _startOnFrame = null;
        }

        private void Settle()
        {
            CancelFrameStart();
            _transitionId++;
            _transitioning = false;
            _front.Place(Matrix.Identity, 1);
            _rear.Place(Matrix.Identity, 0);
            _rear.Show(null);
        }

        private static Matrix Pose(Point pivot, double scale, Vector shift) =>
            new Matrix(scale, 0, 0, scale, pivot.X * (1 - scale) + shift.X, pivot.Y * (1 - scale) + shift.Y);

        internal static List<Bubble> Pack(IReadOnlyList<DiskEntry> entries, double width, double height)
        {
            var bubbles = new List<Bubble>();
            double margin = Math.Max(14, Math.Min(32, Math.Min(width, height) * 0.06));
            if (entries.Count == 0 || width <= margin * 2 || height <= margin * 2)
                return bubbles;

            double aspect = Math.Max(0.68, Math.Min(1.45, (width - 2 * margin) / (height - 2 * margin)));
            double largest = Math.Max(1, entries[0].Bytes);
            Rect bounds = Rect.Empty;

            foreach (DiskEntry entry in entries)
            {
                double radius = entries[0].Bytes == 0 ? 40 : Math.Max(19, 100 * Math.Sqrt(entry.Bytes / largest));
                Point position = bounds.IsEmpty ? new Point() : new Point(bounds.Right + Gap + radius, 0);
                Rect bestBounds = Include(bounds, position, radius);
                if (bubbles.Count > 0)
                {
                    double bestScore = PlacementScore(bestBounds, position, aspect);
                    void Consider(Point point)
                    {
                        if (Overlaps(bubbles, point, radius))
                            return;
                        Rect candidate = Include(bounds, point, radius);
                        double score = PlacementScore(candidate, point, aspect);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            position = point;
                            bestBounds = candidate;
                        }
                    }

                    for (int i = 0; i < bubbles.Count; i++)
                    {
                        Bubble anchor = bubbles[i];
                        double reach = anchor.Radius + radius + Gap;
                        foreach (Vector direction in Directions)
                            Consider(anchor.Center + direction * reach);

                        for (int j = 0; j < i; j++)
                        {
                            Bubble neighbor = bubbles[j];
                            Vector between = neighbor.Center - anchor.Center;
                            double distance = between.Length;
                            double neighborReach = neighbor.Radius + radius + Gap;
                            double along = (reach * reach - neighborReach * neighborReach + distance * distance) / (2 * distance);
                            double heightSquared = reach * reach - along * along;
                            if (heightSquared <= 0)
                                continue;
                            Vector axis = between / distance;
                            Point middle = anchor.Center + axis * along;
                            Vector offset = new Vector(-axis.Y, axis.X) * Math.Sqrt(heightSquared);
                            Consider(middle + offset);
                            Consider(middle - offset);
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
            return Math.Max(width, height) + 0.16 * (width + height) + 0.30 * center.Length + 0.24 * ((Vector)added).Length;
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
        }

        private static Brush Glow(Color color, double radiusX, double radiusY)
        {
            var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B))
                { RadiusX = radiusX, RadiusY = radiusY };
            brush.Freeze();
            return brush;
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
                    {
                        light.PushClip(new EllipseGeometry(bubble.Center, bubble.Radius - 1, bubble.Radius - 1));
                        light.PushTransform(_reflectionPosition);
                        light.DrawEllipse(Appearance.IsLight ? LightReflection : DarkReflection, null, new Point(),
                            bubble.Radius * 0.78, bubble.Radius * 0.58);
                        light.Pop();
                        light.Pop();
                    }
                    Brush outline = selected ? (Brush)FindResource("Accent") : (Brush)FindResource("Subtle");
                    var pen = new Pen(outline, selected ? 2.1 : 1.35);
                    double rim = bubble.Radius + 0.5;
                    drawing.DrawEllipse(pressed ? PressShade : null, pen, bubble.Center, rim, rim);
                }
            }
        }

        private void DrawBubble(DrawingContext drawing, Bubble bubble)
        {
            GlassOrb.Draw(drawing, bubble.Center, bubble.Radius);

            if (bubble.Radius < 28)
            {
                if (bubble.Radius >= 9)
                    DrawGlyph(drawing, bubble, centered: true);
                return;
            }

            double nameSize = Math.Min(26, Math.Max(12, bubble.Radius * 0.171));
            bool showGlyph = bubble.Radius >= 39;
            bool system = bubble.Entry.IsSystem && bubble.Radius >= 49;
            double lift = system ? 7 : 0;
            double baseline = (showGlyph ? Math.Min(24, bubble.Radius * 0.13) : -nameSize * 0.54) - lift;
            if (showGlyph)
                DrawGlyph(drawing, bubble, lift: lift);
            DrawLabel(drawing, bubble, bubble.Entry.Name, _nameFont, nameSize, NameInk, baseline);
            double size = Math.Min(18, Math.Max(11, nameSize * 0.77));
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

        private void DrawGlyph(DrawingContext drawing, Bubble bubble, bool centered = false, double lift = 0)
        {
            double width = centered ? bubble.Radius * 0.97 : Math.Min(57, bubble.Radius * 0.49);
            double anchor = centered ? (bubble.Entry.IsFolder ? 0.56 : 0.5) : 0.93;
            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(width / 32, width / 32));
            transform.Children.Add(new TranslateTransform(bubble.Center.X - width / 2, bubble.Center.Y - width * anchor - lift));
            drawing.PushTransform(transform);
            if (bubble.Entry.IsFolder)
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
                    MoveReflection(pointer);
                return;
            }

            _hovered = entry;
            MoveReflection(pointer);
            _pointerLight.BeginAnimation(DrawingGroup.OpacityProperty, null);
            if (entry != null && SystemParameters.ClientAreaAnimation)
            {
                var light = UiMotion.Ease(0.3, 1, UiMotion.HoverMs);
                light.FillBehavior = FillBehavior.Stop;
                _pointerLight.BeginAnimation(DrawingGroup.OpacityProperty, light);
            }
            Cursor = entry == null ? Cursors.Arrow : Cursors.Hand;
            ToolTip = entry == null ? null : entry.Name + (entry.IsSystem ? "  ·  System" : "") + "\n" + entry.SizeLabel +
                (_folder.Bytes == 0 ? "" : "  ·  " + ((double)entry.Bytes / _folder.Bytes).ToString("P1") + " of this folder");
            HoverChanged?.Invoke(entry);
            UpdateHighlight();
        }

        private void MoveReflection(Point pointer)
        {
            _hoverPoint = pointer;
            _reflectionPosition.X = pointer.X;
            _reflectionPosition.Y = pointer.Y;
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

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs args)
        {
            base.OnMouseRightButtonUp(args);
            DiskEntry entry = Hit(args.GetPosition(this));
            if (entry == null)
                return;
            _pressed = null;
            _backgroundDown = false;
            ToolTip = null;
            UpdateHighlight();
            ContextRequested?.Invoke(entry);
            args.Handled = true;
        }

        private DiskEntry Hit(Point position)
        {
            if (_transitioning)
            {
                if (_front.Opacity < 0.12)
                    return null;
                Matrix inverse = _front.RenderTransform.Value;
                inverse.Invert();
                position = inverse.Transform(position);
            }
            foreach (Bubble bubble in _bubbles)
                if ((position - bubble.Center).LengthSquared <= bubble.Radius * bubble.Radius)
                    return bubble.Entry;
            return null;
        }

    }
}
