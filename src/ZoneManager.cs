using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MagicZones
{
    /// <summary>A zone resolved to physical pixels on a live monitor.</summary>
    internal sealed class Zone
    {
        public MonitorInfo Monitor;
        public int Number;       // 1-based within its monitor
        public ZoneDef Def;
        public Rectangle Rect;   // raw zone rectangle (virtual-screen px)
        public ZoneKind Kind => Def.Kind;
        public long Area => (long)Rect.Width * Rect.Height;
    }

    internal sealed class ZoneManager
    {
        private readonly AppConfig config;

        public List<MonitorInfo> Monitors { get; private set; } = new List<MonitorInfo>();
        public List<Zone> Zones { get; private set; } = new List<Zone>();

        public ZoneManager(AppConfig config)
        {
            this.config = config;
        }

        public void Rebuild() => RebuildFrom(MagicZones.Monitors.Enumerate());

        public void RebuildFrom(List<MonitorInfo> monitors)
        {
            Monitors = monitors;
            var zones = new List<Zone>();
            foreach (var m in Monitors)
            {
                var layout = config.FindLayout(m);
                if (layout == null) continue;
                int n = 1;
                foreach (var def in layout.Zones)
                    zones.Add(new Zone { Monitor = m, Number = n++, Def = def, Rect = def.ToPixels(m.WorkArea) });
            }
            Zones = zones;
            wholeMonitor.Clear();
        }

        public IEnumerable<Zone> ZonesOn(MonitorInfo m) => Zones.Where(z => z.Monitor == m);

        private readonly Dictionary<MonitorInfo, Zone> wholeMonitor = new Dictionary<MonitorInfo, Zone>();

        /// <summary>Pseudo-zone covering a monitor's whole work area (for monitors without zones).</summary>
        public Zone WholeMonitor(MonitorInfo m)
        {
            if (!wholeMonitor.TryGetValue(m, out var z))
                wholeMonitor[m] = z = new Zone { Monitor = m, Number = 0, Def = new ZoneDef(0, 0, 1, 1), Rect = m.WorkArea };
            return z;
        }

        /// <summary>Zone under a point. Overlapping zones: the smallest one wins (most specific).</summary>
        public Zone HitTest(Point p)
        {
            Zone best = null;
            foreach (var z in Zones)
                if (z.Rect.Contains(p) && (best == null || z.Area < best.Area)) best = z;
            return best;
        }

        /// <summary>Where a window should actually go: the zone minus gaps.</summary>
        public Rectangle TargetRect(Zone z) => TargetRect(z.Rect, z.Monitor);

        public Rectangle TargetRect(Rectangle r, MonitorInfo m)
        {
            int g = (int)Math.Round(config.Gap * m.Scale);
            if (g <= 0) return r;
            int half = g / 2;
            var wa = m.WorkArea;
            // Outer edges get the full gap, inner edges half (so neighbours end up g apart).
            int l = r.Left + (r.Left <= wa.Left + 1 ? g : half);
            int t = r.Top + (r.Top <= wa.Top + 1 ? g : half);
            int rr = r.Right - (r.Right >= wa.Right - 1 ? g : half);
            int b = r.Bottom - (r.Bottom >= wa.Bottom - 1 ? g : half);
            if (rr - l < 50 || b - t < 50) return r;
            return Rectangle.FromLTRB(l, t, rr, b);
        }

        /// <summary>Bounding box of several zones (Ctrl-drag spanning). Only meaningful on one monitor.</summary>
        public Rectangle SpanRect(IList<Zone> zones)
        {
            var r = zones[0].Rect;
            foreach (var z in zones.Skip(1)) r = Rectangle.Union(r, z.Rect);
            return TargetRect(r, zones[0].Monitor);
        }

        /// <summary>
        /// Throw physics: from the release point travel along the velocity for speed × momentum px.
        /// The window lands in the last zone the trajectory crossed — so a hard flick "slams" into
        /// the far edge, and gaps between monitors are simply flown over.
        /// </summary>
        public Zone ThrowTarget(Point from, double vx, double vy, double momentum, out Point landing)
        {
            double speed = Math.Sqrt(vx * vx + vy * vy);
            landing = from;
            if (speed < 1 || Zones.Count == 0) return null;

            double dx = vx / speed, dy = vy / speed;
            var virt = VirtualBounds();
            double maxTravel = Math.Sqrt((double)virt.Width * virt.Width + (double)virt.Height * virt.Height);
            double travel = Math.Min(Math.Max(speed * momentum, 120), maxTravel);

            Zone last = null;
            const double step = 10;
            for (double s = 0; s <= travel; s += step)
            {
                var q = new Point((int)(from.X + dx * s), (int)(from.Y + dy * s));
                var z = HitTest(q);
                if (z != null)
                {
                    last = z;
                    landing = q;
                }
                else if (!virt.Contains(q))
                {
                    break; // left the desktop entirely: stop at the wall
                }
            }
            return last;
        }

        /// <summary>Neighbour zone in a direction (used by the keyboard shortcuts).</summary>
        public Zone Neighbour(Rectangle windowRect, int dirX, int dirY)
        {
            var c = Geometry.Center(windowRect);
            var current = Zones
                .Select(z => new { z, overlap = Overlap(z.Rect, windowRect) })
                .Where(x => x.overlap > 0)
                .OrderByDescending(x => x.overlap)
                .Select(x => x.z)
                .FirstOrDefault();
            var origin = current != null ? Geometry.Center(current.Rect) : c;

            Zone best = null;
            double bestScore = double.MaxValue;
            foreach (var z in Zones)
            {
                if (z == current) continue;
                var zc = Geometry.Center(z.Rect);
                double along = (zc.X - origin.X) * dirX + (zc.Y - origin.Y) * dirY;
                if (along <= 4) continue;
                double perp = Math.Abs((zc.X - origin.X) * dirY) + Math.Abs((zc.Y - origin.Y) * dirX);
                if (perp > along * 1.8 + 200) continue;
                double score = along + perp * 2.2;
                if (score < bestScore) { bestScore = score; best = z; }
            }
            return best;
        }

        public Rectangle VirtualBounds()
        {
            if (Monitors.Count == 0) return new Rectangle(0, 0, 1, 1);
            var r = Monitors[0].Bounds;
            foreach (var m in Monitors) r = Rectangle.Union(r, m.Bounds);
            return r;
        }

        private static long Overlap(Rectangle a, Rectangle b)
        {
            var i = Rectangle.Intersect(a, b);
            return i.IsEmpty ? 0 : (long)i.Width * i.Height;
        }
    }
}
