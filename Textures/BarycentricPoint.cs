using System;
using System.Drawing;

namespace Rbx2Source.Textures
{
    public struct BarycentricPoint
    {
        private readonly int ax, ay;
        private readonly int v0x, v0y, v1x, v1y;
        private readonly double d00, d01, d11, denom;

        public BarycentricPoint(params Point[] poly)
        {
            Point a = poly[0],
                  b = poly[1],
                  c = poly[2];

            ax = a.X;
            ay = a.Y;

            v0x = b.X - a.X;
            v0y = b.Y - a.Y;
            v1x = c.X - a.X;
            v1y = c.Y - a.Y;

            int d00i = (v0x * v0x) + (v0y * v0y),
                d01i = (v0x * v1x) + (v0y * v1y),
                d11i = (v1x * v1x) + (v1y * v1y);

            d00 = d00i;
            d01 = d01i;
            d11 = d11i;

            denom = (d00 * d11) - (d01 * d01);
        }

        private void GetWeights(Point p, out double U, out double V, out double W)
        {
            int v2x = p.X - ax,
                v2y = p.Y - ay;

            double d20 = (v2x * v0x) + (v2y * v0y),
                   d21 = (v2x * v1x) + (v2y * v1y);

            V = ((d11 * d20) - (d01 * d21)) / denom;
            W = ((d00 * d21) - (d01 * d20)) / denom;

            U = 1.0 - V - W;
        }

        public bool TryMap(Point p, Point[] poly, out Point mapped)
        {
            double U, V, W;
            GetWeights(p, out U, out V, out W);

            // Use int approximation to avoid floating point errors.
            int u = (int)U * 100000,
                v = (int)V * 100000;

            if (u < 0 || v < 0 || u + v > 100000)
            {
                mapped = default(Point);
                return false;
            }

            Point a = poly[0],
                  b = poly[1],
                  c = poly[2];

            double x = (a.X * U) + (b.X * V) + (c.X * W),
                   y = (a.Y * U) + (b.Y * V) + (c.Y * W);

            int ix = (int)(x + 0.5f),
                iy = (int)(y + 0.5f);

            mapped = new Point(ix, iy);
            return true;
        }

        public bool InBounds(Point p)
        {
            double U, V, W;
            GetWeights(p, out U, out V, out W);

            // Use int approximation to avoid floating point errors.
            int u = (int)U * 100000,
                v = (int)V * 100000;

            return u >= 0 && v >= 0 && u + v <= 100000;
        }

        public Point ToCartesian(Point p, params Point[] poly)
        {
            double U, V, W;
            GetWeights(p, out U, out V, out W);

            Point a = poly[0],
                  b = poly[1],
                  c = poly[2];

            double x = (a.X * U) + (b.X * V) + (c.X * W),
                   y = (a.Y * U) + (b.Y * V) + (c.Y * W);

            int ix = (int)(x + 0.5f),
                iy = (int)(y + 0.5f);

            return new Point(ix, iy);
        }
    }
}
