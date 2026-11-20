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
        public int MoreCount;
        public long MoreBytes;
        public bool IsMore => Entry == null;
        public List<Bubble> Inner;
    }

    /// <summary>
    /// The storage map. Like Space Lens, bubble area is exactly proportional to size within a page, the largest
    /// items sit at the heart of a tightly packed cluster, and folders preview their own contents inside them.
    /// A page holds as many items as stay legible; the rest are gathered into a "+N more" bubble that opens the
    /// next page, so nothing is shrunk into an unreadable dot or inflated beyond its true size.
    /// </summary>
    internal sealed class BubbleMap : System.Windows.Controls.Grid
    {
        private const double MinRadius = 15;
        private const int MaxPerPage = 40;
        private const double Gap = 6;
        private const double InnerMinRadius = 2.6;

        private sealed class Page
        {
            public long Largest;
            public int Start;
            public int End;
            public List<Bubble> Bubbles;
            public DrawingGroup Artwork;
            public bool Light;
        }

        private sealed class FolderPages
        {
            public Size Viewport;
            public double PixelsPerDip;
            public readonly List<Page> Pages = new List<Page>();
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

        private static readonly Typeface NameFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface SizeFont = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Geometry FolderBack = Geometry.Parse("M 1,11 Q 1,8 4,8 L 12,8 16,12 29,12 Q 31,12 31,15 L 31,25 Q 31,28 28,28 L 4,28 Q 1,28 1,25 Z");
        private static readonly Geometry FolderFront = Geometry.Parse("M 1,16 Q 1,13 4,13 L 28,13 Q 31,13 31,16 L 31,25 Q 31,28 28,28 L 4,28 Q 1,28 1,25 Z");
        private static readonly Geometry FileShape = Geometry.Parse("M 7,4 L 19,4 25,10 25,26 Q 25,28 23,28 L 9,28 Q 7,28 7,26 Z");
        private static readonly Geometry FileFold = Geometry.Parse("M 19,4 L 19,10 25,10 Z");
        private static readonly Geometry CheckMark = Geometry.Parse(UiVisuals.Check);
        private static readonly Brush DarkReflection = Glow(Color.FromArgb(46, 255, 255, 255));
        private static readonly Brush LightReflection = Glow(Color.FromArgb(110, 255, 255, 255));

        private readonly ConditionalWeakTable<DiskEntry, FolderPages> _cache = new ConditionalWeakTable<DiskEntry, FolderPages>();
        private DiskEntry _folder;
        private FolderPages _pages;
        private int _page;
        private List<Bubble> _bubbles = new List<Bubble>();
        private DiskEntry _selected;
        private ICollection<DiskEntry> _marked;
        private Bubble _hovered;
        private Point _hoverPoint;
        private DiskEntry _rowHovered;
        private Bubble _pressed;
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
            SizeChanged += (sender, args) => Resize();
            Unloaded += (sender, args) => Settle();
        }

        public event Action<DiskEntry> Activated;
        public event Action<DiskEntry> ContextRequested;
        public event Action<DiskEntry> HoverChanged;
        public event Action BackgroundActivated;
        public event Action PageChanged;

        public int VisibleCount => _bubbles.Count(bubble => !bubble.IsMore);
        public int PageStart => CurrentPage?.Start ?? 0;
        public int PageEnd => CurrentPage?.End ?? 0;
        public int ItemCount => _folder?.Children.Count ?? 0;
        public bool HasPrevious => _page > 0;
        public bool HasNext => CurrentPage != null && CurrentPage.End < ItemCount;

        /// <summary>How many times larger than page one this page draws the same size (area ratio).</summary>
        public double PageZoom => _pages == null || _page == 0 || CurrentPage == null || CurrentPage.Largest <= 0 || _pages.Pages[0].Largest <= 0
            ? 1 : (double)_pages.Pages[0].Largest / CurrentPage.Largest;
        public DiskEntry Inspected => _hovered?.Entry ?? _rowHovered;
        private Page CurrentPage => _pages != null && _page < _pages.Pages.Count ? _pages.Pages[_page] : null;

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
            var reveal = UiMotion.Ease(0.2, 1, UiMotion.RevealMs + 80);
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

        /// <summary>Shows a folder, opening the page that contains <paramref name="firstItem"/>.</summary>
        public void ShowEntries(DiskEntry folder, int firstItem, DiskEntry origin, bool returning, bool animate)
        {
            _folder = folder;
            _pages = null;
            _page = 0;
            EnsurePages();
            while (CurrentPage != null && CurrentPage.End <= firstItem && CurrentPage.End < ItemCount)
            {
                _page++;
                EnsurePages();
            }
            if (CurrentPage == null)
                _page = 0;
            ResetPointer();
            Reflow(origin, returning, animate: animate);
        }

        /// <summary>Opens the page that contains the given item, if it is in this folder.</summary>
        public void Reveal(DiskEntry entry)
        {
            int index = _folder?.Children.IndexOf(entry) ?? -1;
            if (index < 0 || (index >= PageStart && index < PageEnd))
                return;
            int direction = index < PageStart ? -1 : 1;
            _page = 0;
            EnsurePages();
            while (CurrentPage != null && CurrentPage.End <= index && CurrentPage.End < ItemCount)
            {
                _page++;
                EnsurePages();
            }
            ResetPointer();
            Reflow(null, false, animate: true, pageDirection: direction);
            PageChanged?.Invoke();
        }

        public void NextPage() => ChangePage(_page + 1);

        public void PreviousPage() => ChangePage(_page - 1);

        private void ChangePage(int page)
        {
            if (page < 0 || page == _page || (page > _page && !HasNext))
                return;
            int direction = Math.Sign(page - _page);
            _page = page;
            EnsurePages();
            ResetPointer();
            Reflow(null, false, animate: true, pageDirection: direction);
            PageChanged?.Invoke();
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

        private void Resize()
        {
            if (_folder == null)
                return;
            int first = PageStart;
            _pages = null;
            _page = 0;
            EnsurePages();
            while (CurrentPage != null && CurrentPage.End <= first && CurrentPage.End < ItemCount)
            {
                _page++;
                EnsurePages();
            }
            _front.Show(PrepareArtwork());
            UpdateHighlight();
            PageChanged?.Invoke();
        }

        /// <summary>Builds page boundaries up to the current page; pages depend on the viewport, so they are lazy.</summary>
        private void EnsurePages()
        {
            if (_folder == null || ActualWidth <= 0 || ActualHeight <= 0)
                return;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (_pages == null)
            {
                if (!_cache.TryGetValue(_folder, out _pages) || _pages.Viewport != RenderSize || _pages.PixelsPerDip != pixelsPerDip)
                {
                    _pages = new FolderPages { Viewport = RenderSize, PixelsPerDip = pixelsPerDip };
                    _cache.Remove(_folder);
                    _cache.Add(_folder, _pages);
                }
            }
            while (_pages.Pages.Count <= _page)
            {
                int start = _pages.Pages.Count == 0 ? 0 : _pages.Pages[_pages.Pages.Count - 1].End;
                if (_pages.Pages.Count > 0 && start >= ItemCount)
                {
                    _page = _pages.Pages.Count - 1;
                    return;
                }
                _pages.Pages.Add(BuildPage(start));
            }
        }

        private Page BuildPage(int start)
        {
            Page page = BuildPage(start, reservePager: start > 0);
            // A paged map keeps a strip clear at the bottom so the pager never covers a bubble.
            return page.End < ItemCount && start == 0 ? BuildPage(start, reservePager: true) : page;
        }

        private Page BuildPage(int start, bool reservePager)
        {
            List<DiskEntry> items = _folder.Children;
            var page = new Page { Start = start, End = start, Bubbles = new List<Bubble>() };
            int remaining = items.Count - start;
            if (remaining <= 0)
                return page;
            page.Largest = items[start].Bytes;

            double margin = Math.Max(14, Math.Min(30, Math.Min(ActualWidth, ActualHeight) * 0.05));
            double bottom = reservePager ? Math.Max(margin, 58) : margin;
            var area = new Rect(margin, margin * 0.6, Math.Max(1, ActualWidth - 2 * margin), Math.Max(1, ActualHeight - margin * 0.6 - bottom));
            double largest = items[start].Bytes;
            // Empty items have no size to show, so they get small equal bubbles rather than a misleading giant.
            double maxRadius = largest > 0 ? Math.Min(area.Width, area.Height) * 0.46 : Math.Max(MinRadius * 2.2, 40);
            (int Count, Point[] Centers, double[] Radii) best = (0, null, null);
            long restBytes = 0;
            for (int i = start; i < items.Count; i++)
                restBytes = DiskScanner.AddBytes(restBytes, items[i].Bytes);
            long shownBytes = 0;

            for (int count = 1; count <= Math.Min(MaxPerPage, remaining); count++)
            {
                if (largest > 0 && items[start + count - 1].Bytes == 0)
                    break;
                shownBytes = DiskScanner.AddBytes(shownBytes, items[start + count - 1].Bytes);
                var radii = new List<double>(count + 1);
                for (int i = 0; i < count; i++)
                    radii.Add(largest > 0 ? Math.Sqrt((double)items[start + i].Bytes / largest) : 1);
                if (start + count < items.Count)
                    radii.Add(MoreRadius(Math.Max(0, restBytes - shownBytes)));

                var fit = CirclePacker.Fit(radii, area, Gap, maxRadius);
                if (count > 1 && fit.Radii[count - 1] < MinRadius * (largest > 0 ? 1 : 1.9))
                    break;
                best = (count, fit.Centers, fit.Radii);
            }

            // Empty items left over at the end join this page as small bubbles instead of getting a page of their own.
            int tail = items.Count - (start + best.Count);
            if (largest > 0 && tail > 0 && tail <= 12 && items[start + best.Count].Bytes == 0)
            {
                var radii = new List<double>(best.Count + tail);
                for (int i = 0; i < best.Count; i++)
                    radii.Add(Math.Sqrt((double)items[start + i].Bytes / largest));
                double scale = best.Radii[0] / radii[0];
                double empty = Math.Max(radii[best.Count - 1] * 0.6, 9 / scale);
                for (int i = 0; i < tail; i++)
                    radii.Add(empty);
                var fit = CirclePacker.Fit(radii, area, Gap, maxRadius);
                if (fit.Radii[best.Count - 1] >= MinRadius * 0.85)
                    best = (best.Count + tail, fit.Centers, fit.Radii);
            }

            page.End = start + best.Count;
            for (int i = 0; i < best.Count; i++)
            {
                var bubble = new Bubble { Entry = items[start + i], Center = best.Centers[i], Radius = best.Radii[i] };
                bubble.Inner = InnerPreview(bubble);
                page.Bubbles.Add(bubble);
            }
            if (page.End < items.Count)
            {
                long rest = 0;
                for (int i = page.End; i < items.Count; i++)
                    rest = DiskScanner.AddBytes(rest, items[i].Bytes);
                page.Bubbles.Add(new Bubble
                {
                    Center = best.Centers[best.Count], Radius = best.Radii[best.Count],
                    MoreCount = items.Count - page.End, MoreBytes = rest,
                });
            }
            return page;

            double MoreRadius(long rest)
            {
                double proportional = largest > 0 ? Math.Sqrt(rest / largest) : 1;
                // A navigation affordance, not data: big enough to read and click, never dominating the page.
                return Math.Max(0.30, Math.Min(0.62, proportional));
            }
        }

        /// <summary>Packs a folder's largest children inside its bubble, each sized by its true share of the folder.</summary>
        private static List<Bubble> InnerPreview(Bubble bubble)
        {
            DiskEntry folder = bubble.Entry;
            if (!folder.IsFolder || bubble.Radius < 46 || folder.Bytes <= 0 || folder.Children.Count == 0)
                return null;

            var top = new List<DiskEntry>(14);
            foreach (DiskEntry child in folder.Children)
            {
                if (child.Bytes <= 0)
                    continue;
                if (top.Count < 14)
                {
                    top.Add(child);
                    continue;
                }
                int smallest = 0;
                for (int i = 1; i < top.Count; i++)
                    if (top[i].Bytes < top[smallest].Bytes)
                        smallest = i;
                if (child.Bytes > top[smallest].Bytes)
                    top[smallest] = child;
            }
            if (top.Count == 0)
                return null;
            top.Sort(DiskScanner.CompareEntries);

            double inner = bubble.Radius * 0.80;
            double reach = inner * 0.74;
            var radii = top.Select(entry => Math.Sqrt((double)entry.Bytes / folder.Bytes)).ToList();
            var area = new Rect(bubble.Center.X - reach, bubble.Center.Y - reach, reach * 2, reach * 2);
            var fit = CirclePacker.Fit(radii, area, Math.Max(1.5, bubble.Radius * 0.025), inner * radii[0]);
            var bubbles = new List<Bubble>();
            for (int i = 0; i < top.Count; i++)
            {
                if (fit.Radii[i] < InnerMinRadius)
                    break;
                bubbles.Add(new Bubble { Entry = top[i], Center = fit.Centers[i], Radius = fit.Radii[i] });
            }
            // One child tells nothing a label doesn't; previews are for showing how a folder is made up.
            return bubbles.Count < 2 ? null : bubbles;
        }

        private DrawingGroup PrepareArtwork()
        {
            EnsurePages();
            Page page = CurrentPage;
            if (page == null)
            {
                _bubbles = new List<Bubble>();
                return null;
            }

            if (page.Artwork == null || page.Light != Appearance.IsLight)
            {
                page.Light = Appearance.IsLight;
                page.Artwork = new DrawingGroup();
                using (DrawingContext drawing = page.Artwork.Open())
                    foreach (Bubble bubble in page.Bubbles)
                        DrawBubble(drawing, bubble);
                page.Artwork.Freeze();
            }
            _bubbles = page.Bubbles;
            return page.Artwork;
        }

        private void Reflow(DiskEntry origin, bool returning, bool animate = false, int pageDirection = 0)
        {
            if (_folder == null)
                return;

            List<Bubble> previous = _bubbles;
            DrawingGroup artwork = PrepareArtwork();
            Bubble anchor = (returning ? _bubbles : previous).Find(bubble => bubble.Entry != null && bubble.Entry == origin);
            if (animate && SystemParameters.ClientAreaAnimation && IsVisible && _front.Artwork != null && artwork != null)
                Animate(artwork, anchor, returning, pageDirection);
            else
            {
                _front.Show(artwork);
                Settle();
            }
            UpdateHighlight();
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
                var slide = new Vector(36 * pageDirection, 0);
                incomingStart = Pose(center, 0.96, slide);
                outgoingEnd = Pose(center, 0.96, -slide);
            }
            else
            {
                double entryScale = anchor == null ? 0.91 : Math.Max(0.55, Math.Min(0.83, anchor.Radius / Math.Max(1, center.Y) + 0.28));
                incomingStart = Pose(returning ? center : pivot, returning ? 1.14 : entryScale, offset * (returning ? -0.20 : 0.15));
                outgoingEnd = Pose(returning ? center : pivot, returning ? 0.73 : 1.45, offset * (returning ? 0.86 : -0.5));
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

        protected override void OnRender(DrawingContext drawing) =>
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        private static Brush Glow(Color color)
        {
            var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B)) { RadiusX = 0.5, RadiusY = 0.5 };
            brush.Freeze();
            return brush;
        }

        private Brush Resource(string key) => (Brush)FindResource(key);

        private void UpdateHighlight()
        {
            using (DrawingContext light = _pointerLight.Open())
            using (DrawingContext drawing = _highlight.Open())
            {
                if (_transitioning)
                    return;

                foreach (Bubble bubble in _bubbles)
                {
                    bool marked = bubble.Entry != null && _marked != null && _marked.Contains(bubble.Entry);
                    bool selected = bubble.Entry != null && bubble.Entry == _selected;
                    bool hovered = bubble == _hovered || (bubble.Entry != null && bubble.Entry == _rowHovered);
                    bool pressed = bubble == _pressed;
                    if (!marked && !selected && !hovered && !pressed)
                        continue;

                    if (bubble == _hovered && !pressed && bubble.Radius >= 17 && SystemParameters.ClientAreaAnimation)
                    {
                        light.PushClip(new EllipseGeometry(bubble.Center, bubble.Radius - 1, bubble.Radius - 1));
                        light.PushTransform(_reflectionPosition);
                        light.DrawEllipse(Appearance.IsLight ? LightReflection : DarkReflection, null, new Point(),
                            bubble.Radius * 0.80, bubble.Radius * 0.62);
                        light.Pop();
                        light.Pop();
                    }

                    double rim = bubble.Radius + 1;
                    if (hovered || pressed)
                    {
                        Color tint = bubble.IsMore ? UiVisuals.KindColor("Other") : UiVisuals.KindColor(bubble.Entry.Kind);
                        var glow = new SolidColorBrush(Color.FromArgb(Appearance.IsLight ? (byte)200 : (byte)220, tint.R, tint.G, tint.B));
                        drawing.DrawEllipse(pressed ? new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)) : null, new Pen(glow, 2), bubble.Center, rim, rim);
                    }
                    if (selected && !marked)
                        drawing.DrawEllipse(null, new Pen(Resource("Ink"), 1.6) { DashStyle = DashStyles.Solid }, bubble.Center, rim + 2.5, rim + 2.5);
                    if (marked)
                    {
                        drawing.DrawEllipse(null, new Pen(Resource("Accent"), 2.6), bubble.Center, rim + 2, rim + 2);
                        DrawMarkBadge(drawing, bubble);
                    }
                }
            }
        }

        private void DrawMarkBadge(DrawingContext drawing, Bubble bubble)
        {
            double size = Math.Max(8, Math.Min(13, bubble.Radius * 0.17));
            double angle = -Math.PI / 4;
            var at = new Point(bubble.Center.X + Math.Cos(angle) * (bubble.Radius + 1), bubble.Center.Y + Math.Sin(angle) * (bubble.Radius + 1));
            drawing.DrawEllipse(Resource("AccentFill"), new Pen(Resource("Base"), 2), at, size, size);
            drawing.PushTransform(new TranslateTransform(at.X - size * 0.62, at.Y - size * 0.62));
            drawing.PushTransform(new ScaleTransform(size * 1.24 / 24, size * 1.24 / 24));
            drawing.DrawGeometry(null, new Pen(Resource("AccentInk"), 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, CheckMark);
            drawing.Pop();
            drawing.Pop();
        }

        private void DrawBubble(DrawingContext drawing, Bubble bubble)
        {
            if (bubble.IsMore)
            {
                DrawMore(drawing, bubble);
                return;
            }

            GlassOrb.For(UiVisuals.KindColor(bubble.Entry.Kind)).Draw(drawing, bubble.Center, bubble.Radius, quiet: false);
            if (bubble.Inner != null)
                foreach (Bubble inner in bubble.Inner)
                    GlassOrb.For(UiVisuals.KindColor(inner.Entry.Kind)).Draw(drawing, inner.Center, inner.Radius, quiet: true);

            if (bubble.Radius < 27)
            {
                if (bubble.Radius >= 9)
                    DrawGlyph(drawing, bubble, centered: true);
                return;
            }

            double nameSize = Math.Min(24, Math.Max(12, bubble.Radius * 0.17));
            double sizeSize = Math.Min(17, Math.Max(11, nameSize * 0.78));
            bool system = bubble.Entry.IsSystem && bubble.Radius >= 49;
            bool showGlyph = bubble.Inner == null && bubble.Radius >= 40;
            double lift = system ? 8 : 0;
            double baseline = (showGlyph ? Math.Min(22, bubble.Radius * 0.13) : -nameSize * 0.55) - lift;

            if (bubble.Inner != null)
            {
                // A soft lens of the pane colour behind the label keeps it readable over the nested bubbles.
                Color scrim = ((SolidColorBrush)Resource("Workspace")).Color;
                var lens = new RadialGradientBrush(Color.FromArgb(Appearance.IsLight ? (byte)170 : (byte)150, scrim.R, scrim.G, scrim.B),
                    Color.FromArgb(0, scrim.R, scrim.G, scrim.B));
                lens.GradientStops.Insert(1, new GradientStop(Color.FromArgb(Appearance.IsLight ? (byte)110 : (byte)95, scrim.R, scrim.G, scrim.B), 0.5));
                lens.Freeze();
                drawing.DrawEllipse(lens, null, new Point(bubble.Center.X, bubble.Center.Y + baseline + nameSize * 0.55),
                    bubble.Radius * 0.72, nameSize * 2.0);
            }
            if (showGlyph)
                DrawGlyph(drawing, bubble, lift: lift);
            DrawLabel(drawing, bubble, bubble.Entry.Name, NameFont, nameSize, Resource("Ink"), baseline);
            double sizeOffset = baseline + nameSize * 1.18;
            DrawLabel(drawing, bubble, bubble.Entry.SizeLabel, SizeFont, sizeSize, Resource("Muted"), sizeOffset);
            if (system)
                DrawSystemBadge(drawing, bubble, sizeOffset + sizeSize + 7);
        }

        private void DrawMore(DrawingContext drawing, Bubble bubble)
        {
            Color tint = UiVisuals.KindColor("Other");
            GlassOrb.For(tint).Draw(drawing, bubble.Center, bubble.Radius, quiet: true);
            var dash = new Pen(Resource("Subtle"), 1.4) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            drawing.DrawEllipse(null, dash, bubble.Center, bubble.Radius - 3, bubble.Radius - 3);
            double nameSize = Math.Min(20, Math.Max(11, bubble.Radius * 0.21));
            if (bubble.Radius < 26)
            {
                DrawLabel(drawing, bubble, "+" + Compact(bubble.MoreCount), NameFont, nameSize, Resource("Ink"), 0);
                return;
            }
            DrawLabel(drawing, bubble, $"+{bubble.MoreCount:N0} more", NameFont, nameSize, Resource("Ink"), -nameSize * 0.65);
            DrawLabel(drawing, bubble, DiskEntry.FormatSize(bubble.MoreBytes), SizeFont, Math.Max(11, nameSize * 0.78), Resource("Muted"), nameSize * 0.55);
            if (bubble.Radius >= 44)
                DrawLabel(drawing, bubble, "Next page  ›", NameFont, Math.Max(11, nameSize * 0.66), Resource("Accent"), nameSize * 1.75);
        }

        private static string Compact(int count) => count < 1000 ? count.ToString(CultureInfo.CurrentCulture) :
            (count / 1000.0).ToString(count < 10000 ? "0.#" : "0", CultureInfo.CurrentCulture) + "k";

        private void DrawSystemBadge(DrawingContext drawing, Bubble bubble, double offset)
        {
            var bounds = new Rect(bubble.Center.X - 22, bubble.Center.Y + offset - 8.5, 44, 17);
            drawing.DrawRoundedRectangle(Resource("BadgeFill"), new Pen(Resource("BadgeEdge"), 1), bounds, 8.5, 8.5);
            DrawLabel(drawing, bubble, "System", NameFont, 10, Resource("BadgeInk"), offset);
        }

        private void DrawGlyph(DrawingContext drawing, Bubble bubble, bool centered = false, double lift = 0)
        {
            double width = centered ? bubble.Radius * 0.95 : Math.Min(46, bubble.Radius * 0.40);
            double anchor = centered ? (bubble.Entry.IsFolder ? 0.56 : 0.5) : 0.93;
            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(width / 32, width / 32));
            transform.Children.Add(new TranslateTransform(bubble.Center.X - width / 2, bubble.Center.Y - width * anchor - lift));
            drawing.PushTransform(transform);
            Color tint = UiVisuals.KindColor(bubble.Entry.Kind);
            Color ink = ((SolidColorBrush)Resource("Ink")).Color;
            var front = new SolidColorBrush(Mix(ink, tint, Appearance.IsLight ? 0.35 : 0.25)) { Opacity = 0.82 };
            var back = new SolidColorBrush(Mix(ink, tint, 0.55)) { Opacity = 0.55 };
            if (bubble.Entry.IsFolder)
            {
                drawing.DrawGeometry(back, null, FolderBack);
                drawing.DrawGeometry(front, null, FolderFront);
            }
            else
            {
                drawing.DrawGeometry(front, null, FileShape);
                drawing.DrawGeometry(back, null, FileFold);
            }
            drawing.Pop();
        }

        private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));

        private void DrawLabel(DrawingContext drawing, Bubble bubble, string value, Typeface font, double fontSize, Brush brush, double yOffset)
        {
            double textWidth = bubble.Radius * 1.58;
            var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                font, fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = textWidth,
                MaxLineCount = 1,
                TextAlignment = TextAlignment.Center,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            drawing.DrawText(text, new Point(bubble.Center.X - textWidth / 2, bubble.Center.Y + yOffset - text.Height / 2));
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
            Bubble bubble = Hit(pointer);
            if (bubble == _hovered)
            {
                if (bubble != null && SystemParameters.ClientAreaAnimation && (pointer - _hoverPoint).LengthSquared >= 9)
                {
                    MoveReflection(pointer);
                }
                return;
            }

            _hovered = bubble;
            MoveReflection(pointer);
            _pointerLight.BeginAnimation(DrawingGroup.OpacityProperty, null);
            if (bubble != null && SystemParameters.ClientAreaAnimation)
            {
                var light = UiMotion.Ease(0.3, 1, UiMotion.HoverMs);
                light.FillBehavior = FillBehavior.Stop;
                _pointerLight.BeginAnimation(DrawingGroup.OpacityProperty, light);
            }
            Cursor = bubble == null ? Cursors.Arrow : Cursors.Hand;
            ToolTip = bubble == null ? null : Describe(bubble);
            HoverChanged?.Invoke(bubble?.Entry);
            UpdateHighlight();
        }

        private string Describe(Bubble bubble)
        {
            if (bubble.IsMore)
                return $"{bubble.MoreCount:N0} smaller items · {DiskEntry.FormatSize(bubble.MoreBytes)}\nClick to show the next page";
            DiskEntry entry = bubble.Entry;
            string share = _folder.Bytes == 0 ? "" : "  ·  " + ((double)entry.Bytes / _folder.Bytes).ToString("P1", CultureInfo.CurrentCulture) + " of this folder";
            string hint = entry.IsFolder ? "Click to open · Right-click for more" : "Click to find it in the list · Right-click for more";
            return entry.Name + (entry.IsSystem ? "  ·  System" : "") + "\n" + UiVisuals.KindLabel(entry.Kind) + "  ·  " + entry.SizeLabel + share + "\n" + hint;
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

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
        {
            base.OnMouseLeftButtonUp(args);
            args.Handled = Release(args.GetPosition(this));
        }

        internal bool Press(Point position, int clicks)
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

        internal bool Release(Point position)
        {
            if (_pressed == null)
                return false;
            Bubble pressed = _pressed;
            _pressed = null;
            UpdateHighlight();
            bool stayed = Math.Abs(position.X - _pressedAt.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _pressedAt.Y) <= SystemParameters.MinimumVerticalDragDistance;
            if (pressed == Hit(position) || (_transitioning && stayed))
            {
                if (pressed.IsMore)
                    NextPage();
                else
                    Activated?.Invoke(pressed.Entry);
            }
            return true;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs args)
        {
            base.OnMouseRightButtonUp(args);
            Bubble bubble = Hit(args.GetPosition(this));
            if (bubble == null || bubble.IsMore)
                return;
            _pressed = null;
            _backgroundDown = false;
            ToolTip = null;
            UpdateHighlight();
            ContextRequested?.Invoke(bubble.Entry);
            args.Handled = true;
        }

        private Bubble Hit(Point position)
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
                    return bubble;
            return null;
        }
    }
}
