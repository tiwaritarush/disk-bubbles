using System;
using System.Collections.Generic;
using System.Windows;

namespace DiskBubbles
{
    /// <summary>
    /// Front-chain circle packing (Wang et al., as used by D3's packSiblings and CleanMyMac's Space Lens):
    /// each circle is placed tangent to two circles on the current outer chain, giving a tight, gap-free
    /// cluster with the largest circles at its heart. Coordinates are relative; callers scale to pixels.
    /// </summary>
    internal static class CirclePacker
    {
        private sealed class Node
        {
            public int Index;
            public Node Next;
            public Node Previous;
        }

        /// <summary>
        /// Positions circles of the given radii and returns their centres. <paramref name="aspect"/> (width over
        /// height) stretches the distance used to choose where the chain grows, so the cluster becomes an ellipse
        /// that fills a wide or tall viewport instead of a circle stranded in the middle of it.
        /// </summary>
        public static Point[] Pack(IReadOnlyList<double> radii, double aspect = 1)
        {
            double squash = 1 / Math.Max(0.4, Math.Min(2.5, aspect));
            squash *= squash;
            squash *= squash;
            int count = radii.Count;
            var x = new double[count];
            var y = new double[count];
            var centers = new Point[count];
            if (count == 0)
                return centers;
            if (count == 1)
                return centers;

            x[0] = -radii[1];
            x[1] = radii[0];
            if (count > 2)
            {
                Place(1, 0, 2);
                var a = new Node { Index = 0 };
                var b = new Node { Index = 1 };
                var c = new Node { Index = 2 };
                a.Next = c.Previous = b;
                b.Next = a.Previous = c;
                c.Next = b.Previous = a;

                for (int i = 3; i < count; i++)
                {
                    Place(a.Index, b.Index, i);
                    var placed = new Node { Index = i };
                    Node j = b.Next, k = a.Previous;
                    double sj = radii[b.Index], sk = radii[a.Index];
                    bool restarted = false;
                    do
                    {
                        if (sj <= sk)
                        {
                            if (Intersects(j.Index, i))
                            {
                                b = j;
                                a.Next = b;
                                b.Previous = a;
                                i--;
                                restarted = true;
                                break;
                            }
                            sj += radii[j.Index];
                            j = j.Next;
                        }
                        else
                        {
                            if (Intersects(k.Index, i))
                            {
                                a = k;
                                a.Next = b;
                                b.Previous = a;
                                i--;
                                restarted = true;
                                break;
                            }
                            sk += radii[k.Index];
                            k = k.Previous;
                        }
                    }
                    while (j != k.Next);
                    if (restarted)
                        continue;

                    placed.Previous = a;
                    placed.Next = b;
                    a.Next = b.Previous = b = placed;

                    double best = Score(a);
                    for (Node node = placed.Next; node != b; node = node.Next)
                    {
                        double score = Score(node);
                        if (score < best)
                        {
                            a = node;
                            best = score;
                        }
                    }
                    b = a.Next;
                }
            }

            for (int i = 0; i < count; i++)
                centers[i] = new Point(x[i], y[i]);
            return centers;

            void Place(int bi, int ai, int ci)
            {
                double dx = x[bi] - x[ai], dy = y[bi] - y[ai];
                double d2 = dx * dx + dy * dy;
                if (d2 <= 0)
                {
                    x[ci] = x[ai] + radii[ci];
                    y[ci] = y[ai];
                    return;
                }
                double a2 = radii[ai] + radii[ci];
                a2 *= a2;
                double b2 = radii[bi] + radii[ci];
                b2 *= b2;
                if (a2 > b2)
                {
                    double along = (d2 + b2 - a2) / (2 * d2);
                    double across = Math.Sqrt(Math.Max(0, b2 / d2 - along * along));
                    x[ci] = x[bi] - along * dx - across * dy;
                    y[ci] = y[bi] - along * dy + across * dx;
                }
                else
                {
                    double along = (d2 + a2 - b2) / (2 * d2);
                    double across = Math.Sqrt(Math.Max(0, a2 / d2 - along * along));
                    x[ci] = x[ai] + along * dx - across * dy;
                    y[ci] = y[ai] + along * dy + across * dx;
                }
            }

            bool Intersects(int first, int second)
            {
                double reach = radii[first] + radii[second] - 1e-6;
                double dx = x[second] - x[first], dy = y[second] - y[first];
                return reach > 0 && reach * reach > dx * dx + dy * dy;
            }

            double Score(Node node)
            {
                int first = node.Index, second = node.Next.Index;
                double total = radii[first] + radii[second];
                double dx = (x[first] * radii[second] + x[second] * radii[first]) / total;
                double dy = (y[first] * radii[second] + y[second] * radii[first]) / total;
                return dx * dx * squash + dy * dy;
            }
        }

        /// <summary>
        /// Packs circles and scales the cluster to fill <paramref name="area"/>, keeping a constant pixel gap
        /// between neighbours. Returns pixel centres and radii (gap already removed from the radii).
        /// </summary>
        public static (Point[] Centers, double[] Radii) Fit(IReadOnlyList<double> radii, Rect area, double gap, double maxRadius)
        {
            int count = radii.Count;
            var resultRadii = new double[count];
            if (count == 0 || area.Width <= 0 || area.Height <= 0)
                return (new Point[count], resultRadii);

            // First pass estimates the scale, second pass pads each circle by half the gap at that scale.
            double aspect = area.Width / area.Height;
            double scale = Scale(Pack(radii, aspect), radii, 0);
            double pad = gap / 2 / Math.Max(scale, 1e-9);
            var padded = new double[count];
            for (int i = 0; i < count; i++)
                padded[i] = radii[i] + pad;
            Point[] centers = Pack(padded, aspect);
            scale = Scale(centers, padded, pad);

            Rect bounds = Bounds(centers, padded);
            double offsetX = area.X + area.Width / 2 - (bounds.Left + bounds.Width / 2) * scale;
            double offsetY = area.Y + area.Height / 2 - (bounds.Top + bounds.Height / 2) * scale;
            for (int i = 0; i < count; i++)
            {
                centers[i] = new Point(centers[i].X * scale + offsetX, centers[i].Y * scale + offsetY);
                resultRadii[i] = Math.Max(0, radii[i] * scale);
            }
            return (centers, resultRadii);

            double Scale(Point[] points, IReadOnlyList<double> sizes, double padding)
            {
                Rect box = Bounds(points, sizes);
                double fit = Math.Min(area.Width / Math.Max(box.Width, 1e-9), area.Height / Math.Max(box.Height, 1e-9));
                double largest = 0;
                for (int i = 0; i < sizes.Count; i++)
                    largest = Math.Max(largest, sizes[i] - padding);
                return largest <= 0 ? fit : Math.Min(fit, maxRadius / largest);
            }
        }

        private static Rect Bounds(IReadOnlyList<Point> centers, IReadOnlyList<double> radii)
        {
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            for (int i = 0; i < centers.Count; i++)
            {
                left = Math.Min(left, centers[i].X - radii[i]);
                top = Math.Min(top, centers[i].Y - radii[i]);
                right = Math.Max(right, centers[i].X + radii[i]);
                bottom = Math.Max(bottom, centers[i].Y + radii[i]);
            }
            return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
    }
}
