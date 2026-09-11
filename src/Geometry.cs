using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MagicZones
{
    internal static class Geometry
    {
        public static double DistanceToRect(Point p, Rectangle r)
        {
            int dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - (r.Right - 1));
            int dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - (r.Bottom - 1));
            return Math.Sqrt((double)dx * dx + (double)dy * dy);
        }

        public static Point Center(Rectangle r) => new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);

        public static Rectangle Lerp(Rectangle a, Rectangle b, double t)
        {
            int L(int x, int y) => (int)Math.Round(x + (y - x) * t);
            return Rectangle.FromLTRB(L(a.Left, b.Left), L(a.Top, b.Top), L(a.Right, b.Right), L(a.Bottom, b.Bottom));
        }

        public static double EaseOutCubic(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            double u = 1 - t;
            return 1 - u * u * u;
        }

        /// <summary>Slight overshoot, feels like the window "lands" in the zone.</summary>
        public static double EaseOutBack(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            const double c1 = 1.20158, c3 = c1 + 1;
            double u = t - 1;
            return 1 + c3 * u * u * u + c1 * u * u;
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                hex = hex.Trim().TrimStart('#');
                if (hex.Length == 6) hex = "FF" + hex;
                if (hex.Length != 8) return fallback;
                return Color.FromArgb(Convert.ToInt32(hex, 16));
            }
            catch
            {
                return fallback;
            }
        }

        public static Color WithAlpha(Color c, int a) => Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
    }
}
