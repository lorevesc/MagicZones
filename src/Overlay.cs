using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MagicZones
{
    internal sealed class OverlayState
    {
        public HashSet<Zone> Hover = new HashSet<Zone>();
        public Zone ThrowTarget;
    }

    /// <summary>One click-through overlay per monitor, shown while a window is being dragged.</summary>
    internal sealed class OverlayManager : IDisposable
    {
        private readonly AppConfig config;
        private readonly ZoneManager zones;
        private readonly List<OverlayWindow> windows = new List<OverlayWindow>();
        private readonly Timer fadeTimer = new Timer { Interval = 15 };
        private DateTime fadeStart;
        private bool fadingIn;

        public bool Visible { get; private set; }

        public OverlayManager(AppConfig config, ZoneManager zones)
        {
            this.config = config;
            this.zones = zones;
            fadeTimer.Tick += (s, e) => FadeStep();
        }

        public void Rebuild()
        {
            HideNow();
            foreach (var w in windows) w.Dispose();
            windows.Clear();
            foreach (var m in zones.Monitors)
                windows.Add(new OverlayWindow(m, config, zones));
        }

        public void Show(OverlayState state)
        {
            foreach (var w in windows) w.Update(state, force: true);
            if (Visible) return;
            Visible = true;
            foreach (var w in windows)
            {
                w.Opacity = 0;
                w.Show();
            }
            fadingIn = true;
            fadeStart = DateTime.UtcNow;
            fadeTimer.Start();
        }

        public void Update(OverlayState state)
        {
            if (!Visible) return;
            foreach (var w in windows) w.Update(state, force: false);
        }

        public void Hide()
        {
            if (!Visible) return;
            Visible = false;
            fadingIn = false;
            fadeStart = DateTime.UtcNow;
            fadeTimer.Start();
        }

        public void HideNow()
        {
            Visible = false;
            fadeTimer.Stop();
            foreach (var w in windows) w.Hide();
        }

        private void FadeStep()
        {
            double t = (DateTime.UtcNow - fadeStart).TotalMilliseconds / (fadingIn ? 110.0 : 140.0);
            double a = fadingIn ? Geometry.EaseOutCubic(t) : 1 - Geometry.EaseOutCubic(t);
            foreach (var w in windows) w.Opacity = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, a)));
            if (t < 1) return;
            fadeTimer.Stop();
            if (!fadingIn) foreach (var w in windows) w.Hide();
        }

        public void Dispose()
        {
            fadeTimer.Dispose();
            foreach (var w in windows) w.Dispose();
            windows.Clear();
        }
    }

    internal sealed class OverlayWindow : LayeredWindow
    {
        private readonly MonitorInfo monitor;
        private readonly AppConfig config;
        private readonly ZoneManager zones;
        private string lastSignature;

        public OverlayWindow(MonitorInfo monitor, AppConfig config, ZoneManager zones)
            : base(monitor.Bounds, clickThrough: true, topmost: true)
        {
            this.monitor = monitor;
            this.config = config;
            this.zones = zones;
        }

        public void Update(OverlayState state, bool force)
        {
            var mine = zones.ZonesOn(monitor).ToList();
            // Redraw only when something on *this* monitor changed: full-screen GDI+ isn't free.
            string sig = string.Join(",", mine.Where(z => state.Hover.Contains(z)).Select(z => z.Number))
                         + "|" + (state.ThrowTarget != null && state.ThrowTarget.Monitor == monitor ? state.ThrowTarget.Number : 0);
            if (!force && sig == lastSignature) return;
            lastSignature = sig;
            Render(g => Draw(g, mine, state));
        }

        private void Draw(Graphics g, List<Zone> mine, OverlayState state)
        {
            float s = monitor.Scale;
            var accent = Geometry.ParseColor(config.AccentColor, Color.FromArgb(59, 130, 246));
            var throwC = Geometry.ParseColor(config.ThrowColor, Color.FromArgb(249, 115, 22));
            var origin = monitor.Bounds.Location;

            // Ctrl-span: draw the union as one big highlighted zone.
            var hovered = mine.Where(z => state.Hover.Contains(z)).ToList();
            bool spanning = hovered.Count > 1;

            using (var numberFont = new Font("Segoe UI Semibold", 30 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var smallFont = new Font("Segoe UI", 13 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var labelFont = new Font("Segoe UI Semibold", 12 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                foreach (var z in mine)
                {
                    var r = zones.TargetRect(z);
                    r.Offset(-origin.X, -origin.Y);
                    bool isThrow = state.ThrowTarget == z;
                    bool isHover = state.Hover.Contains(z) && !spanning;
                    DrawZone(g, r, z, s, isThrow ? throwC : accent, isHover || isThrow, isThrow, numberFont, smallFont, labelFont);
                }

                if (spanning)
                {
                    var r = zones.SpanRect(hovered);
                    r.Offset(-origin.X, -origin.Y);
                    DrawHighlight(g, r, s, accent);
                    DrawCenteredText(g, $"{hovered.Count} zone unite", numberFont, Color.FromArgb(235, 255, 255, 255), r);
                    var dim = new RectangleF(r.X, r.Y + r.Height / 2f + 22 * s, r.Width, 24 * s);
                    DrawCenteredText(g, $"{r.Width} × {r.Height}", smallFont, Color.FromArgb(200, 255, 255, 255), dim);
                }
            }
        }

        private static void DrawZone(Graphics g, Rectangle r, Zone z, float s, Color accent, bool active, bool isThrow,
            Font numberFont, Font smallFont, Font labelFont)
        {
            float radius = 12 * s;
            if (active)
            {
                DrawHighlight(g, r, s, accent);
            }
            else
            {
                using (var path = Geometry.RoundedRect(r, radius))
                using (var fill = new SolidBrush(Color.FromArgb(105, 18, 22, 32)))
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.5f * s))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
            }

            string number = z.Number.ToString();
            string kind = z.Kind == ZoneKind.Snap ? null : ZoneDef.KindLabel(z.Kind);
            if (isThrow) kind = "LANCIO";
            var textColor = active ? Color.FromArgb(240, 255, 255, 255) : Color.FromArgb(130, 255, 255, 255);

            // Number scales with the zone: readable at a glance on big monitors, still fits small zones.
            float numPx = Math.Max(26 * s, Math.Min(96 * s, Math.Min(r.Width, r.Height) * 0.11f));
            float cy = r.Y + r.Height / 2f;
            float below = cy + numPx * 0.62f;
            using (var bigFont = new Font(numberFont.FontFamily, numPx, FontStyle.Regular, GraphicsUnit.Pixel))
                DrawCenteredText(g, number, bigFont, textColor, new RectangleF(r.X, cy - numPx * 0.85f, r.Width, numPx * 1.4f));
            if (kind != null)
            {
                DrawPill(g, kind, labelFont, new PointF(r.X + r.Width / 2f, below + 14 * s), s,
                    active ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255),
                    active ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(60, 255, 255, 255));
                below += 30 * s;
            }
            if (active && z.Kind == ZoneKind.Snap)
                DrawCenteredText(g, $"{r.Width} × {r.Height}", smallFont, Color.FromArgb(190, 255, 255, 255),
                    new RectangleF(r.X, below + 4 * s, r.Width, 24 * s));
        }

        private static void DrawHighlight(Graphics g, Rectangle r, float s, Color accent)
        {
            float radius = 12 * s;
            // Soft outer glow: a few widening translucent strokes.
            for (int i = 3; i >= 1; i--)
            {
                var gr = RectangleF.Inflate(r, i * 3 * s, i * 3 * s);
                using (var path = Geometry.RoundedRect(gr, radius + i * 3 * s))
                using (var pen = new Pen(Geometry.WithAlpha(accent, 34 - i * 8), 3 * s))
                    g.DrawPath(pen, path);
            }
            using (var path = Geometry.RoundedRect(r, radius))
            using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(r,
                       Geometry.WithAlpha(accent, 150), Geometry.WithAlpha(accent, 95), 90f))
            using (var pen = new Pen(Geometry.WithAlpha(accent, 255), 3 * s))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        private static void DrawPill(Graphics g, string text, Font font, PointF center, float s, Color fg, Color bg)
        {
            var size = g.MeasureString(text, font);
            var rect = new RectangleF(center.X - size.Width / 2 - 10 * s, center.Y - size.Height / 2 - 3 * s,
                size.Width + 20 * s, size.Height + 6 * s);
            using (var path = Geometry.RoundedRect(rect, rect.Height / 2))
            using (var brush = new SolidBrush(bg))
                g.FillPath(brush, path);
            DrawCenteredText(g, text, font, fg, rect);
        }
    }
}
