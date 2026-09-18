using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MagicZones
{
    /// <summary>
    /// Mini-map of the whole desktop (every monitor, every zone) that pops up just above the window
    /// being dragged. Release the drag on a tile and the window is launched there, however far.
    /// Click-through: hit-testing is done against the cursor, the OS move loop owns the mouse.
    /// </summary>
    internal sealed class PopupWindow : LayeredWindow
    {
        private readonly AppConfig config;
        private readonly ZoneManager zones;
        private readonly Timer fadeTimer = new Timer { Interval = 15 };
        private readonly List<Tile> tiles = new List<Tile>();
        private readonly List<RectangleF> monitorRects = new List<RectangleF>();

        private MonitorInfo host;
        private float s = 1;
        private int margin;               // transparent border for the drop shadow
        private RectangleF panel;         // client coords
        private RectangleF map;           // client coords
        private double k;                 // virtual px -> popup px
        private Point virtOrigin;
        private string lastSig;
        private DateTime lastRender;
        private DateTime fadeStart;
        private bool fadingIn;
        private double fadeLevel, distLevel = 1;

        private sealed class Tile
        {
            public Zone Zone;
            public RectangleF Rect;     // client coords
            public RectangleF Hit;      // slightly inflated, so the gaps between tiles don't flicker
        }

        public bool IsOpen { get; private set; }

        public PopupWindow(AppConfig config, ZoneManager zones)
            : base(new Rectangle(0, 0, 16, 16), clickThrough: true, topmost: true)
        {
            this.config = config;
            this.zones = zones;
            fadeTimer.Tick += (sender, e) => FadeStep();
        }

        // ---- Layout -----------------------------------------------------------------------------
        /// <summary>Size the popup for the host monitor and park it just above the dragged window.</summary>
        public void Layout(Point cursor, Rectangle windowRect)
        {
            host = Monitors.FromPoint(zones.Monitors, cursor);
            s = host.Scale;
            var virt = zones.VirtualBounds();
            virtOrigin = virt.Location;

            margin = (int)(12 * s);
            float pad = 12 * s, header = 34 * s;
            double maxW = config.PopupWidth * s - 2 * pad;
            double maxH = config.PopupWidth * 0.55 * s;
            k = Math.Min(maxW / virt.Width, maxH / virt.Height);
            float mapW = (float)(virt.Width * k), mapH = (float)(virt.Height * k);

            int w = (int)Math.Ceiling(mapW + 2 * pad + 2 * margin);
            int h = (int)Math.Ceiling(header + mapH + pad + 2 * margin);

            var wa = host.WorkArea;
            int edge = (int)(6 * s);
            int x = cursor.X - w / 2;
            // Above the window's top edge; if there's no room (window near the top), float over the window.
            int y = windowRect.Top - (int)(10 * s) - h + margin;
            if (y < wa.Top + edge) y = cursor.Y + (int)(44 * s) - margin;
            x = Math.Max(wa.Left + edge - margin, Math.Min(wa.Right - edge - w + margin, x));
            y = Math.Max(wa.Top + edge - margin, Math.Min(wa.Bottom - edge - h + margin, y));
            SetBounds(new Rectangle(x, y, w, h));

            panel = new RectangleF(margin, margin, w - 2 * margin, h - 2 * margin);
            map = new RectangleF(panel.X + pad, panel.Y + header, mapW, mapH);
            BuildTiles();
            lastSig = null;
        }

        private RectangleF ToMap(Rectangle r) => new RectangleF(
            map.X + (float)((r.X - virtOrigin.X) * k),
            map.Y + (float)((r.Y - virtOrigin.Y) * k),
            (float)(r.Width * k),
            (float)(r.Height * k));

        private void BuildTiles()
        {
            tiles.Clear();
            monitorRects.Clear();
            float inset = 2.5f * s, tileGap = 1.5f * s;
            foreach (var m in zones.Monitors)
            {
                var mr = ToMap(m.Bounds);
                mr.Inflate(-inset, -inset);
                monitorRects.Add(mr);

                var mine = zones.ZonesOn(m).ToList();
                if (mine.Count == 0) mine.Add(zones.WholeMonitor(m));
                foreach (var z in mine)
                {
                    var tr = ToMap(z.Rect);
                    tr.Intersect(mr);
                    tr.Inflate(-tileGap, -tileGap);
                    if (tr.Width < 2 || tr.Height < 2) continue;
                    var hit = tr;
                    hit.Inflate(tileGap + 0.5f, tileGap + 0.5f);
                    tiles.Add(new Tile { Zone = z, Rect = tr, Hit = hit });
                }
            }
        }

        /// <summary>Zone under a screen point, or null if the cursor isn't on a tile.</summary>
        public Zone HitTest(Point screen)
        {
            if (!IsOpen && tiles.Count == 0) return null;
            var p = new PointF(screen.X - Bounds.X, screen.Y - Bounds.Y);
            Tile best = null;
            foreach (var t in tiles)
                if (t.Hit.Contains(p) && (best == null || t.Rect.Width * t.Rect.Height < best.Rect.Width * best.Rect.Height))
                    best = t;
            return best?.Zone;
        }

        /// <summary>Screen-space centre of each tile (used by the self-test).</summary>
        public IEnumerable<KeyValuePair<Zone, Point>> TileCenters() => tiles.Select(t => new KeyValuePair<Zone, Point>(t.Zone,
            new Point(Bounds.X + (int)(t.Rect.X + t.Rect.Width / 2), Bounds.Y + (int)(t.Rect.Y + t.Rect.Height / 2))));

        // ---- Show / hide ---------------------------------------------------------------------------
        /// <summary>Set while dragging a window we can't move (admin/SYSTEM app): name of that app.</summary>
        public string LockedApp { get; set; }

        public void Open(Point cursor, Rectangle windowRect, OverlayState state, string lockedApp = null)
        {
            LockedApp = lockedApp;
            Layout(cursor, windowRect);
            Update(state, windowRect, cursor, force: true);
            IsOpen = true;
            fadeLevel = 0;
            distLevel = 1;
            ApplyOpacity();
            Show();
            fadingIn = true;
            fadeStart = DateTime.UtcNow;
            fadeTimer.Start();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            fadingIn = false;
            fadeStart = DateTime.UtcNow;
            fadeTimer.Start();
        }

        private void FadeStep()
        {
            double t = (DateTime.UtcNow - fadeStart).TotalMilliseconds / (fadingIn ? 120.0 : 130.0);
            fadeLevel = fadingIn ? Geometry.EaseOutCubic(t) : Math.Min(fadeLevel, 1 - Geometry.EaseOutCubic(t));
            ApplyOpacity();
            if (t < 1) return;
            fadeTimer.Stop();
            if (!fadingIn) Hide();
        }

        private void ApplyOpacity() => Opacity = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, fadeLevel * distLevel)));

        // ---- Per-tick update -----------------------------------------------------------------------
        public void Update(OverlayState state, Rectangle windowRect, Point cursor, bool force = false)
        {
            // Fade out as the cursor drags away from the popup, so it never gets in the way.
            if (state.Hover.Count > 0) distLevel = 1;
            else
            {
                var pr = new Rectangle(Bounds.X + (int)panel.X, Bounds.Y + (int)panel.Y, (int)panel.Width, (int)panel.Height);
                double d = Geometry.DistanceToRect(cursor, pr);
                double near = 120 * s, far = 520 * s;
                distLevel = d <= near ? 1 : d >= far ? 0.3 : 1 - 0.7 * (d - near) / (far - near);
            }
            if (IsOpen && !fadeTimer.Enabled) fadeLevel = 1;
            ApplyOpacity();

            var win = ToMap(windowRect);
            string sig = string.Join(",", state.Hover.Select(z => z.Monitor.Number * 100 + z.Number).OrderBy(n => n))
                         + $"|{(int)win.X},{(int)win.Y},{(int)win.Width},{(int)win.Height}";
            if (!force && sig == lastSig) return;
            // Window-indicator-only changes are throttled; hover changes render immediately.
            bool hoverChanged = lastSig == null || sig.Split('|')[0] != lastSig.Split('|')[0];
            if (!force && !hoverChanged && (DateTime.UtcNow - lastRender).TotalMilliseconds < 30) return;
            lastSig = sig;
            lastRender = DateTime.UtcNow;
            Render(g => Draw(g, state, win));
        }

        // ---- Drawing ---------------------------------------------------------------------------------
        private void Draw(Graphics g, OverlayState state, RectangleF win)
        {
            var accent = Geometry.ParseColor(config.AccentColor, Color.FromArgb(59, 130, 246));
            float radius = 14 * s;

            // Drop shadow
            for (int i = 1; i <= 4; i++)
            {
                var sr = panel;
                sr.Inflate(i * 2.5f * s, i * 2.5f * s);
                sr.Offset(0, 2 * s);
                using (var path = Geometry.RoundedRect(sr, radius + i * 2.5f * s))
                using (var b = new SolidBrush(Color.FromArgb(22 - i * 4, 0, 0, 0)))
                    g.FillPath(b, path);
            }
            using (var path = Geometry.RoundedRect(panel, radius))
            using (var bg = new LinearGradientBrush(panel, Color.FromArgb(246, 30, 34, 46), Color.FromArgb(246, 18, 21, 30), 90f))
            using (var border = new Pen(Color.FromArgb(55, 255, 255, 255), 1f * s))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            // Header
            using (var title = new Font("Segoe UI Semibold", 13 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var hint = new Font("Segoe UI", 11 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var titleBrush = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            using (var hintBrush = new SolidBrush(Color.FromArgb(125, 255, 255, 255)))
            using (var dot = new SolidBrush(accent))
            {
                float hy = panel.Y + 17 * s;
                var sfL = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                var sfR = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far };
                if (LockedApp != null)
                {
                    using (var warn = new SolidBrush(Color.FromArgb(255, 250, 204, 21)))
                    {
                        g.FillEllipse(warn, panel.X + 14 * s, hy - 4 * s, 8 * s, 8 * s);
                        string why = Integrity.IsElevated
                            ? $"{LockedApp}: Windows non la lascia spostare"
                            : $"{LockedApp}: serve MagicZones da amministratore";
                        g.DrawString(why, title, warn, new RectangleF(panel.X + 28 * s, hy - 10 * s, panel.Width - 40 * s, 20 * s), sfL);
                    }
                }
                else
                {
                    g.FillEllipse(dot, panel.X + 14 * s, hy - 4 * s, 8 * s, 8 * s);
                    string text = state.Hover.Count > 1 ? $"Unisci {state.Hover.Count} zone" : "Lancia su…";
                    g.DrawString(text, title, titleBrush, new PointF(panel.X + 28 * s, hy), sfL);
                    g.DrawString("Ctrl unisci · Shift chiudi", hint, hintBrush,
                        new RectangleF(panel.X, hy - 10 * s, panel.Width - 14 * s, 20 * s), sfR);
                }
            }

            // Monitors
            foreach (var mr in monitorRects)
                using (var path = Geometry.RoundedRect(mr, 5 * s))
                using (var b = new SolidBrush(Color.FromArgb(255, 12, 14, 20)))
                using (var p = new Pen(Color.FromArgb(45, 255, 255, 255), 1f * s))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }

            // Tiles
            using (var numFontBase = new Font("Segoe UI Semibold", 12 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tagFont = new Font("Segoe UI Semibold", 9 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                foreach (var t in tiles)
                {
                    bool hot = state.Hover.Contains(t.Zone);
                    var idle = LockedApp != null ? Color.FromArgb(255, 30, 33, 42) : Color.FromArgb(255, 44, 50, 66);
                    using (var path = Geometry.RoundedRect(t.Rect, 4 * s))
                    using (var fill = new SolidBrush(hot ? Geometry.WithAlpha(accent, 245) : idle))
                    using (var pen = new Pen(hot ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(40, 255, 255, 255), (hot ? 1.6f : 1f) * s))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path);
                    }
                    float numPx = Math.Max(10 * s, Math.Min(22 * s, Math.Min(t.Rect.Width, t.Rect.Height) * 0.28f));
                    string label = t.Zone.Number > 0 ? t.Zone.Number.ToString() : "M" + t.Zone.Monitor.Number;
                    using (var f = new Font(numFontBase.FontFamily, numPx, FontStyle.Regular, GraphicsUnit.Pixel))
                        DrawCenteredText(g, label, f, hot ? Color.White : Color.FromArgb(170, 255, 255, 255), t.Rect);
                    if (t.Zone.Kind != ZoneKind.Snap && t.Rect.Height > 40 * s)
                    {
                        string tag = t.Zone.Kind == ZoneKind.Maximize ? "MAX" : "MIN";
                        DrawCenteredText(g, tag, tagFont, hot ? Color.White : Color.FromArgb(255, 250, 204, 21),
                            new RectangleF(t.Rect.X, t.Rect.Y + t.Rect.Height / 2 + numPx * 0.55f, t.Rect.Width, 12 * s));
                    }
                }
            }

            // Where the window is right now
            if (win.Width > 1 && win.Height > 1)
            {
                using (var fill = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.3f * s) { DashPattern = new[] { 3f, 2f } })
                {
                    g.FillRectangle(fill, win);
                    g.DrawRectangle(pen, win.X, win.Y, win.Width, win.Height);
                }
            }

            // Launch trajectory: from the window to the chosen destination
            if (state.Hover.Count > 0 && win.Width > 1)
            {
                var dest = state.Hover.Select(z => tiles.FirstOrDefault(t => t.Zone == z)).Where(t => t != null).ToList();
                if (dest.Count > 0)
                {
                    var r = dest[0].Rect;
                    foreach (var d in dest.Skip(1)) r = RectangleF.Union(r, d.Rect);
                    var a = new PointF(win.X + win.Width / 2, win.Y + win.Height / 2);
                    var b = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
                    if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) > 12 * s)
                    {
                        // Gentle arc: control point lifted perpendicular to the path.
                        var mid = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                        float dx = b.X - a.X, dy = b.Y - a.Y;
                        var ctrl = new PointF(mid.X + dy * 0.18f, mid.Y - dx * 0.18f);
                        if (ctrl.Y > mid.Y) ctrl = new PointF(mid.X - dy * 0.18f, mid.Y + dx * 0.18f);
                        // Stop short of the tile centre so the arrow doesn't cover the zone number.
                        float ex = ctrl.X - b.X, ey = ctrl.Y - b.Y, el = (float)Math.Sqrt(ex * ex + ey * ey);
                        float back = Math.Min(Math.Min(r.Width, r.Height) * 0.36f, el * 0.5f);
                        if (el > 0.1f) b = new PointF(b.X + ex / el * back, b.Y + ey / el * back);
                        using (var glow = new Pen(Color.FromArgb(90, 0, 0, 0), 4.5f * s))
                        using (var pen = new Pen(Color.FromArgb(255, 255, 255, 255), 2f * s) { DashPattern = new[] { 2.5f, 1.8f } })
                        {
                            pen.CustomEndCap = new AdjustableArrowCap(3.2f, 3.2f, true);
                            g.DrawBezier(glow, a, ctrl, ctrl, b);
                            g.DrawBezier(pen, a, ctrl, ctrl, b);
                        }
                    }
                }
            }
        }

        public override void Dispose()
        {
            fadeTimer.Dispose();
            base.Dispose();
        }
    }
}
