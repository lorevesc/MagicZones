using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MagicZones
{
    /// <summary>Full-screen zone editor across all monitors. Works on a copy; Save commits to config.</summary>
    internal sealed class EditorSession : IDisposable
    {
        private readonly AppConfig config;
        private readonly List<EditorWindow> windows = new List<EditorWindow>();
        private readonly Stack<Dictionary<MonitorInfo, List<ZoneDef>>> undo = new Stack<Dictionary<MonitorInfo, List<ZoneDef>>>();
        private readonly Action<bool> closed;
        private bool escArmed, closing;

        public Dictionary<MonitorInfo, List<ZoneDef>> Work { get; } = new Dictionary<MonitorInfo, List<ZoneDef>>();
        public bool Dirty { get; private set; }
        public string Toast { get; private set; }
        public MonitorInfo SelectedMonitor;
        public int SelectedIndex = -1;
        public bool ToolbarHidden;

        public void ToggleToolbar()
        {
            ToolbarHidden = !ToolbarHidden;
            RedrawAll();
        }

        public IReadOnlyList<LayeredWindow> Windows => windows;

        public EditorSession(AppConfig config, IList<MonitorInfo> monitors, Action<bool> closed, bool show = true)
        {
            this.config = config;
            this.closed = closed;
            foreach (var m in monitors)
                Work[m] = (config.FindLayout(m)?.Zones ?? new List<ZoneDef>()).Select(z => z.Clone()).ToList();

            var cursor = Native.CursorPos();
            EditorWindow focus = null;
            foreach (var m in monitors)
            {
                var w = new EditorWindow(this, m, config);
                windows.Add(w);
                if (m.Bounds.Contains(cursor)) focus = w;
            }
            foreach (var w in windows) w.Redraw();
            if (!show) return;
            foreach (var w in windows) w.Show(activate: w == focus);
            if (focus == null && windows.Count > 0) windows[0].Show(activate: true);
        }

        public void PushUndo()
        {
            undo.Push(Work.ToDictionary(kv => kv.Key, kv => kv.Value.Select(z => z.Clone()).ToList()));
            if (undo.Count > 100)
            {
                var keep = undo.Take(100).Reverse().ToList();
                undo.Clear();
                foreach (var k in keep) undo.Push(k);
            }
        }

        /// <summary>A gesture ended without changing anything: drop its undo snapshot.</summary>
        public void DiscardUndo() { if (undo.Count > 0) undo.Pop(); }

        public void MarkDirty()
        {
            Dirty = true;
            escArmed = false;
            Toast = null;
        }

        public void Undo()
        {
            if (undo.Count == 0) { ShowToast("Niente da annullare"); return; }
            var snap = undo.Pop();
            foreach (var kv in snap) Work[kv.Key] = kv.Value;
            SelectedIndex = -1;
            Dirty = true;
            RedrawAll();
        }

        public void ApplyPreset(MonitorInfo m, List<ZoneDef> zones)
        {
            PushUndo();
            Work[m] = zones;
            SelectedMonitor = m;
            SelectedIndex = -1;
            MarkDirty();
            RedrawAll();
        }

        public void DeleteSelected()
        {
            if (SelectedMonitor == null || SelectedIndex < 0 || SelectedIndex >= Work[SelectedMonitor].Count) return;
            PushUndo();
            Work[SelectedMonitor].RemoveAt(SelectedIndex);
            SelectedIndex = -1;
            MarkDirty();
            RedrawAll();
        }

        public void CycleKindSelected()
        {
            if (SelectedMonitor == null || SelectedIndex < 0 || SelectedIndex >= Work[SelectedMonitor].Count) return;
            PushUndo();
            var z = Work[SelectedMonitor][SelectedIndex];
            z.Kind = (ZoneKind)(((int)z.Kind + 1) % 3);
            MarkDirty();
            ShowToast("Zona " + (SelectedIndex + 1) + ": " + ZoneDef.KindLabel(z.Kind));
        }

        public void ShowToast(string text)
        {
            Toast = text;
            RedrawAll();
        }

        public void Save()
        {
            foreach (var kv in Work)
                config.GetOrCreateLayout(kv.Key).Zones = kv.Value.Select(z => z.Clone()).ToList();
            Close(saved: true);
        }

        public void Escape()
        {
            if (Dirty && !escArmed)
            {
                escArmed = true;
                ShowToast("Modifiche non salvate — Esc di nuovo per uscire senza salvare, Invio per salvare");
                return;
            }
            Close(saved: false);
        }

        public void RedrawAll()
        {
            foreach (var w in windows) w.Redraw();
        }

        private void Close(bool saved)
        {
            if (closing) return;
            closing = true;
            // We're usually inside one of our own windows' WndProc here: hide now, destroy on the next tick.
            foreach (var w in windows) w.Hide();
            var t = new Timer { Interval = 1 };
            t.Tick += (s, e) =>
            {
                t.Dispose();
                foreach (var w in windows) w.Dispose();
                windows.Clear();
                closed(saved);
            };
            t.Start();
        }

        public void Dispose()
        {
            foreach (var w in windows) w.Dispose();
            windows.Clear();
        }
    }

    internal sealed class EditorWindow : LayeredWindow
    {
        private const int WM_SETCURSOR = 0x0020, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
            WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104,
            WM_CAPTURECHANGED = 0x0215, WM_MOUSEACTIVATE = 0x0021;

        private enum Grab { None, Draw, Move, L, T, R, B, TL, TR, BL, BR }

        private sealed class Button
        {
            public string Text;
            public Rectangle Rect;
            public Action Click;
            public bool Primary;
        }

        private readonly EditorSession session;
        private readonly MonitorInfo monitor;
        private readonly AppConfig config;
        private readonly List<Button> buttons = new List<Button>();
        private readonly float s;

        private Grab grab = Grab.None;
        private Point grabStart;          // client px
        private Rectangle grabRect;       // zone px rect at grab start (work-area relative)
        private Rectangle liveRect;       // zone being drawn/edited
        private int grabIndex = -1;
        private bool changed;
        private Point mouse;
        private Button hoverButton;
        private readonly List<int> guidesX = new List<int>(), guidesY = new List<int>();

        public EditorWindow(EditorSession session, MonitorInfo monitor, AppConfig config)
            : base(monitor.Bounds, clickThrough: false, topmost: true)
        {
            this.session = session;
            this.monitor = monitor;
            this.config = config;
            s = monitor.Scale;
            BuildButtons();
        }

        private List<ZoneDef> Zones => session.Work[monitor];

        /// <summary>Work area relative to this window's client area.</summary>
        private Rectangle Area
        {
            get
            {
                var wa = monitor.WorkArea;
                wa.Offset(-monitor.Bounds.X, -monitor.Bounds.Y);
                return wa;
            }
        }

        private Rectangle ZonePx(ZoneDef z) => z.ToPixels(Area);

        // ---- Toolbar ---------------------------------------------------------------------
        private void BuildButtons()
        {
            buttons.Clear();
            var items = new List<Button>
            {
                new Button { Text = "2 colonne", Click = () => session.ApplyPreset(monitor, Presets.Columns(2)) },
                new Button { Text = "3 colonne", Click = () => session.ApplyPreset(monitor, Presets.Columns(3)) },
                new Button { Text = "25 | 50 | 25", Click = () => session.ApplyPreset(monitor, Presets.Priority()) },
                new Button { Text = "Main + 2", Click = () => session.ApplyPreset(monitor, Presets.MainAndStack()) },
                new Button { Text = "2 × 2", Click = () => session.ApplyPreset(monitor, Presets.Grid(2, 2)) },
                new Button { Text = "2 righe", Click = () => session.ApplyPreset(monitor, Presets.Rows(2)) },
                new Button { Text = "3 righe", Click = () => session.ApplyPreset(monitor, Presets.Rows(3)) },
                new Button { Text = "Svuota", Click = () => session.ApplyPreset(monitor, new List<ZoneDef>()) },
                new Button { Text = "↶ Annulla", Click = () => session.Undo() },
                new Button { Text = "✓ Salva", Click = () => session.Save(), Primary = true },
                new Button { Text = "✕ Esci", Click = () => session.Escape() },
            };

            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var font = ButtonFont())
            {
                int h = (int)(34 * s), padX = (int)(14 * s), gapX = (int)(6 * s), gapY = (int)(6 * s);
                int maxW = Area.Width - (int)(40 * s);
                var rows = new List<List<Button>> { new List<Button>() };
                int rowW = 0;
                foreach (var b in items)
                {
                    int w = (int)Math.Ceiling(g.MeasureString(b.Text, font).Width) + padX * 2;
                    b.Rect = new Rectangle(0, 0, w, h);
                    if (rowW > 0 && rowW + gapX + w > maxW) { rows.Add(new List<Button>()); rowW = 0; }
                    rowW += (rowW > 0 ? gapX : 0) + w;
                    rows[rows.Count - 1].Add(b);
                }
                int y = Area.Top + (int)(18 * s);
                foreach (var row in rows)
                {
                    int total = row.Sum(b => b.Rect.Width) + gapX * (row.Count - 1);
                    int x = Area.Left + (Area.Width - total) / 2;
                    foreach (var b in row)
                    {
                        b.Rect = new Rectangle(x, y, b.Rect.Width, h);
                        x += b.Rect.Width + gapX;
                    }
                    y += h + gapY;
                }
            }
            buttons.AddRange(items);
        }

        private Font ButtonFont() => new Font("Segoe UI Semibold", 13 * s, FontStyle.Regular, GraphicsUnit.Pixel);

        private Button ButtonAt(Point p) => session.ToolbarHidden ? null : buttons.FirstOrDefault(b => b.Rect.Contains(p));

        private bool InToolbar(Point p) => !session.ToolbarHidden && ToolbarBounds().Contains(p);

        private Rectangle ToolbarBounds()
        {
            var r = buttons[0].Rect;
            foreach (var b in buttons) r = Rectangle.Union(r, b.Rect);
            r.Inflate((int)(10 * s), (int)(10 * s));
            r.Height += (int)(26 * s); // hint line
            return r;
        }

        // ---- Rendering --------------------------------------------------------------------
        public void Redraw() => Render(Draw);

        private void Draw(Graphics g)
        {
            var accent = Geometry.ParseColor(config.AccentColor, Color.FromArgb(59, 130, 246));
            var area = Area;
            var full = new Rectangle(0, 0, monitor.Bounds.Width, monitor.Bounds.Height);

            // Dimmed backdrop. Must be non-zero alpha everywhere, or clicks fall through.
            using (var dim = new SolidBrush(Color.FromArgb(150, 8, 10, 16)))
                g.FillRectangle(dim, full);
            using (var outside = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            using (var region = new Region(full))
            {
                region.Exclude(area);
                g.FillRegion(outside, region);
            }

            using (var numberFont = new Font("Segoe UI Semibold", 34 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 13 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var label = new Font("Segoe UI Semibold", 12 * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                for (int i = 0; i < Zones.Count; i++)
                {
                    var r = (grab != Grab.None && grab != Grab.Draw && i == grabIndex) ? liveRect : ZonePx(Zones[i]);
                    bool selected = session.SelectedMonitor == monitor && session.SelectedIndex == i;
                    DrawZone(g, r, i + 1, Zones[i].Kind, selected, accent, numberFont, small, label);
                }
                if (grab == Grab.Draw && liveRect.Width > 2 && liveRect.Height > 2)
                    DrawZone(g, liveRect, Zones.Count + 1, ZoneKind.Snap, true, accent, numberFont, small, label);

                // Snap guides
                using (var pen = new Pen(Color.FromArgb(200, 250, 204, 21), 1.5f * s) { DashPattern = new[] { 4f, 3f } })
                {
                    foreach (int x in guidesX) g.DrawLine(pen, x, area.Top, x, area.Bottom);
                    foreach (int y in guidesY) g.DrawLine(pen, area.Left, y, area.Right, y);
                }

                DrawToolbar(g, accent, small);
            }
        }

        private void DrawZone(Graphics g, Rectangle r, int number, ZoneKind kind, bool selected, Color accent,
            Font numberFont, Font small, Font label)
        {
            float radius = 10 * s;
            var inner = Rectangle.Inflate(r, -(int)(3 * s), -(int)(3 * s));
            if (inner.Width < 4 || inner.Height < 4) inner = r;
            using (var path = Geometry.RoundedRect(inner, radius))
            using (var fill = new SolidBrush(Geometry.WithAlpha(accent, selected ? 125 : 70)))
            using (var pen = new Pen(Geometry.WithAlpha(selected ? Color.White : accent, selected ? 240 : 200), (selected ? 2.5f : 1.5f) * s))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            float numPx = Math.Max(24 * s, Math.Min(96 * s, Math.Min(inner.Width, inner.Height) * 0.11f));
            float cy = inner.Y + inner.Height / 2f;
            using (var bigFont = new Font(numberFont.FontFamily, numPx, FontStyle.Regular, GraphicsUnit.Pixel))
                DrawCenteredText(g, number.ToString(), bigFont, Color.FromArgb(235, 255, 255, 255),
                    new RectangleF(inner.X, cy - numPx * 0.85f, inner.Width, numPx * 1.4f));
            var kindColor = kind == ZoneKind.Snap ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(255, 250, 204, 21);
            DrawCenteredText(g, ZoneDef.KindLabel(kind) + "  ·  " + r.Width + " × " + r.Height, small,
                kindColor, new RectangleF(inner.X, cy + numPx * 0.62f + 4 * s, inner.Width, 22 * s));

            if (selected)
            {
                using (var brush = new SolidBrush(Color.White))
                {
                    float h = 7 * s;
                    foreach (var p in HandlePoints(inner))
                        g.FillRectangle(brush, p.X - h / 2, p.Y - h / 2, h, h);
                }
            }
        }

        private static IEnumerable<PointF> HandlePoints(Rectangle r)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            yield return new PointF(r.Left, r.Top); yield return new PointF(cx, r.Top); yield return new PointF(r.Right, r.Top);
            yield return new PointF(r.Left, cy); yield return new PointF(r.Right, cy);
            yield return new PointF(r.Left, r.Bottom); yield return new PointF(cx, r.Bottom); yield return new PointF(r.Right, r.Bottom);
        }

        private void DrawToolbar(Graphics g, Color accent, Font small)
        {
            if (session.ToolbarHidden)
            {
                const string tip = "H: mostra barra";
                var ts = g.MeasureString(tip, small);
                var tr = new RectangleF(Area.Left + (Area.Width - ts.Width - 24 * s) / 2, Area.Top + 8 * s, ts.Width + 24 * s, ts.Height + 8 * s);
                using (var path = Geometry.RoundedRect(tr, tr.Height / 2))
                using (var bg = new SolidBrush(Color.FromArgb(200, 22, 25, 34)))
                    g.FillPath(bg, path);
                DrawCenteredText(g, tip, small, Color.FromArgb(200, 255, 255, 255), tr);
                return;
            }

            var tb = ToolbarBounds();
            using (var path = Geometry.RoundedRect(tb, 12 * s))
            using (var bg = new SolidBrush(Color.FromArgb(235, 22, 25, 34)))
            using (var border = new Pen(Color.FromArgb(60, 255, 255, 255), 1 * s))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            using (var font = ButtonFont())
            {
                foreach (var b in buttons)
                {
                    bool hot = b == hoverButton;
                    var fillC = b.Primary ? Geometry.WithAlpha(accent, hot ? 255 : 215)
                                          : Color.FromArgb(hot ? 70 : 34, 255, 255, 255);
                    using (var path = Geometry.RoundedRect(b.Rect, 7 * s))
                    using (var fill = new SolidBrush(fillC))
                        g.FillPath(fill, path);
                    DrawCenteredText(g, b.Text, font, Color.FromArgb(245, 255, 255, 255), b.Rect);
                }
            }

            string hint = session.Toast ??
                "Trascina sul vuoto: nuova zona  ·  Bordi: ridimensiona  ·  Doppio clic / T: tipo  ·  Tasto dx / Canc: elimina  ·  Shift: niente magnete  ·  H: nascondi barra  ·  Invio: salva";
            var hintRect = new RectangleF(tb.Left + 8 * s, tb.Bottom - 30 * s, tb.Width - 16 * s, 24 * s);
            DrawCenteredText(g, hint, small, session.Toast != null ? Color.FromArgb(255, 250, 204, 21) : Color.FromArgb(150, 255, 255, 255), hintRect);

            // Monitor badge bottom-left of the work area.
            var badge = monitor.ToString() + (monitor.Primary ? " · principale" : "");
            var size = g.MeasureString(badge, small);
            var br = new RectangleF(Area.Left + 16 * s, Area.Bottom - 16 * s - size.Height - 10 * s, size.Width + 24 * s, size.Height + 10 * s);
            using (var path = Geometry.RoundedRect(br, br.Height / 2))
            using (var bg = new SolidBrush(Color.FromArgb(220, 22, 25, 34)))
                g.FillPath(bg, path);
            DrawCenteredText(g, badge, small, Color.FromArgb(220, 255, 255, 255), br);
        }

        // ---- Input ----------------------------------------------------------------------
        protected override void WndProc(ref Message m)
        {
            try
            {
                switch (m.Msg)
                {
                    case WM_MOUSEACTIVATE:
                        m.Result = (IntPtr)1; // MA_ACTIVATE
                        return;
                    case WM_SETCURSOR:
                        if (((int)m.LParam & 0xFFFF) == 1) // HTCLIENT
                        {
                            Native.SetCursor(Native.LoadCursor(IntPtr.Zero, CursorFor(mouse)));
                            m.Result = (IntPtr)1;
                            return;
                        }
                        break;
                    case WM_MOUSEMOVE: OnMouseMove(Pt(m.LParam)); return;
                    case WM_LBUTTONDOWN: OnMouseDown(Pt(m.LParam)); return;
                    case WM_LBUTTONUP: OnMouseUp(Pt(m.LParam)); return;
                    case WM_LBUTTONDBLCLK: OnDoubleClick(Pt(m.LParam)); return;
                    case WM_RBUTTONUP: OnRightClick(Pt(m.LParam)); return;
                    case WM_KEYDOWN:
                    case WM_SYSKEYDOWN:
                        OnKey((Keys)(int)m.WParam);
                        return;
                    case WM_CAPTURECHANGED:
                        if (grab != Grab.None && m.LParam != Handle) CancelGrab();
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Write("Editor: " + e);
            }
            base.WndProc(ref m);
        }

        private static Point Pt(IntPtr lParam)
        {
            int v = unchecked((int)(long)lParam);
            return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        private int HitZone(Point p)
        {
            // Selected zone first (so its handles win), then topmost (last drawn).
            if (session.SelectedMonitor == monitor && session.SelectedIndex >= 0 && session.SelectedIndex < Zones.Count
                && Rectangle.Inflate(ZonePx(Zones[session.SelectedIndex]), Tol, Tol).Contains(p))
                return session.SelectedIndex;
            for (int i = Zones.Count - 1; i >= 0; i--)
                if (Rectangle.Inflate(ZonePx(Zones[i]), Tol / 2, Tol / 2).Contains(p)) return i;
            return -1;
        }

        private int Tol => (int)(8 * s);

        private Grab GrabAt(Rectangle r, Point p)
        {
            bool l = Math.Abs(p.X - r.Left) <= Tol, rr = Math.Abs(p.X - r.Right) <= Tol;
            bool t = Math.Abs(p.Y - r.Top) <= Tol, b = Math.Abs(p.Y - r.Bottom) <= Tol;
            if (t && l) return Grab.TL;
            if (t && rr) return Grab.TR;
            if (b && l) return Grab.BL;
            if (b && rr) return Grab.BR;
            if (l) return Grab.L;
            if (rr) return Grab.R;
            if (t) return Grab.T;
            if (b) return Grab.B;
            return Grab.Move;
        }

        private int CursorFor(Point p)
        {
            var g = grab;
            if (g == Grab.None)
            {
                if (ButtonAt(p) != null) return Native.IDC_HAND;
                if (InToolbar(p)) return Native.IDC_ARROW;
                int i = HitZone(p);
                if (i < 0) return Native.IDC_CROSS;
                g = GrabAt(ZonePx(Zones[i]), p);
            }
            switch (g)
            {
                case Grab.L: case Grab.R: return Native.IDC_SIZEWE;
                case Grab.T: case Grab.B: return Native.IDC_SIZENS;
                case Grab.TL: case Grab.BR: return Native.IDC_SIZENWSE;
                case Grab.TR: case Grab.BL: return Native.IDC_SIZENESW;
                case Grab.Move: return Native.IDC_SIZEALL;
                default: return Native.IDC_CROSS;
            }
        }

        private void OnMouseDown(Point p)
        {
            var btn = ButtonAt(p);
            if (btn != null) { btn.Click(); return; }
            if (InToolbar(p)) return;

            int i = HitZone(p);
            grabStart = p;
            changed = false;
            session.PushUndo();
            if (i < 0)
            {
                grab = Grab.Draw;
                grabIndex = -1;
                liveRect = new Rectangle(p, Size.Empty);
                session.SelectedMonitor = monitor;
                session.SelectedIndex = -1;
            }
            else
            {
                grabIndex = i;
                grabRect = ZonePx(Zones[i]);
                liveRect = grabRect;
                grab = GrabAt(grabRect, p);
                session.SelectedMonitor = monitor;
                session.SelectedIndex = i;
            }
            Native.SetCapture(Handle);
            session.RedrawAll();
        }

        private void OnMouseMove(Point p)
        {
            mouse = p;
            if (grab == Grab.None)
            {
                var hot = ButtonAt(p);
                if (hot != hoverButton) { hoverButton = hot; Redraw(); }
                return;
            }

            int dx = p.X - grabStart.X, dy = p.Y - grabStart.Y;
            if (!changed && Math.Abs(dx) < 3 && Math.Abs(dy) < 3) return;
            changed = true;

            bool magnet = !Native.IsKeyDown(Native.VK_SHIFT);
            guidesX.Clear();
            guidesY.Clear();
            var area = Area;
            int minSize = (int)(60 * s);

            if (grab == Grab.Draw)
            {
                int x1 = Snap(grabStart.X, true, magnet), y1 = Snap(grabStart.Y, false, magnet);
                int x2 = Snap(p.X, true, magnet), y2 = Snap(p.Y, false, magnet);
                liveRect = Rectangle.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
                liveRect.Intersect(area);
            }
            else if (grab == Grab.Move)
            {
                var r = grabRect;
                r.Offset(dx, dy);
                // Snap whichever edge is closer to a guide.
                int sl = Snap(r.Left, true, magnet), sr = Snap(r.Right, true, magnet);
                int offX = Math.Abs(sl - r.Left) <= Math.Abs(sr - r.Right) ? sl - r.Left : sr - r.Right;
                int st = Snap(r.Top, false, magnet), sb = Snap(r.Bottom, false, magnet);
                int offY = Math.Abs(st - r.Top) <= Math.Abs(sb - r.Bottom) ? st - r.Top : sb - r.Bottom;
                r.Offset(offX, offY);
                guidesX.Clear(); guidesY.Clear();
                if (offX != 0) guidesX.Add(sl - r.Left == 0 ? r.Left : r.Right);
                if (offY != 0) guidesY.Add(st - r.Top == 0 ? r.Top : r.Bottom);
                r.X = Math.Max(area.Left, Math.Min(area.Right - r.Width, r.X));
                r.Y = Math.Max(area.Top, Math.Min(area.Bottom - r.Height, r.Y));
                liveRect = r;
            }
            else
            {
                int l = grabRect.Left, t = grabRect.Top, r = grabRect.Right, b = grabRect.Bottom;
                if (grab == Grab.L || grab == Grab.TL || grab == Grab.BL) l = Math.Min(Snap(l + dx, true, magnet), r - minSize);
                if (grab == Grab.R || grab == Grab.TR || grab == Grab.BR) r = Math.Max(Snap(r + dx, true, magnet), l + minSize);
                if (grab == Grab.T || grab == Grab.TL || grab == Grab.TR) t = Math.Min(Snap(t + dy, false, magnet), b - minSize);
                if (grab == Grab.B || grab == Grab.BL || grab == Grab.BR) b = Math.Max(Snap(b + dy, false, magnet), t + minSize);
                liveRect = Rectangle.FromLTRB(Math.Max(area.Left, l), Math.Max(area.Top, t), Math.Min(area.Right, r), Math.Min(area.Bottom, b));
            }
            Redraw();
        }

        private void OnMouseUp(Point p)
        {
            if (grab == Grab.None) return;
            var g = grab;
            grab = Grab.None;
            Native.ReleaseCapture();
            guidesX.Clear();
            guidesY.Clear();

            int minSize = (int)(40 * s);
            if (!changed)
            {
                session.DiscardUndo();
            }
            else if (g == Grab.Draw)
            {
                if (liveRect.Width >= minSize && liveRect.Height >= minSize)
                {
                    Zones.Add(ZoneDef.FromPixels(liveRect, Area, ZoneKind.Snap));
                    session.SelectedMonitor = monitor;
                    session.SelectedIndex = Zones.Count - 1;
                    session.MarkDirty();
                }
                else session.DiscardUndo();
            }
            else if (grabIndex >= 0 && grabIndex < Zones.Count)
            {
                var kind = Zones[grabIndex].Kind;
                Zones[grabIndex] = ZoneDef.FromPixels(liveRect, Area, kind);
                session.MarkDirty();
            }
            grabIndex = -1;
            session.RedrawAll();
        }

        private void CancelGrab()
        {
            grab = Grab.None;
            grabIndex = -1;
            guidesX.Clear();
            guidesY.Clear();
            if (changed) session.DiscardUndo();
            Redraw();
        }

        private void OnDoubleClick(Point p)
        {
            int i = HitZone(p);
            if (i < 0) return;
            session.SelectedMonitor = monitor;
            session.SelectedIndex = i;
            session.CycleKindSelected();
        }

        private void OnRightClick(Point p)
        {
            if (grab != Grab.None) return;
            int i = HitZone(p);
            if (i < 0) return;
            session.SelectedMonitor = monitor;
            session.SelectedIndex = i;
            session.DeleteSelected();
        }

        private void OnKey(Keys key)
        {
            bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
            switch (key)
            {
                case Keys.Escape:
                    if (grab != Grab.None) { Native.ReleaseCapture(); CancelGrab(); }
                    else session.Escape();
                    break;
                case Keys.Enter: session.Save(); break;
                case Keys.S when ctrl: session.Save(); break;
                case Keys.Z when ctrl: session.Undo(); break;
                case Keys.Delete:
                case Keys.Back: session.DeleteSelected(); break;
                case Keys.T: session.CycleKindSelected(); break;
                case Keys.H: session.ToggleToolbar(); break;
            }
        }

        /// <summary>Magnet: work-area edges, other zones' edges, and halves/thirds/quarters.</summary>
        private int Snap(int v, bool horizontal, bool magnet)
        {
            if (!magnet) return v;
            var area = Area;
            int lo = horizontal ? area.Left : area.Top, len = horizontal ? area.Width : area.Height;
            var cands = new List<int> { lo, lo + len };
            foreach (var f in new[] { 0.25, 1 / 3.0, 0.5, 2 / 3.0, 0.75 }) cands.Add(lo + (int)Math.Round(len * f));
            for (int i = 0; i < Zones.Count; i++)
            {
                if (i == grabIndex) continue;
                var r = ZonePx(Zones[i]);
                cands.Add(horizontal ? r.Left : r.Top);
                cands.Add(horizontal ? r.Right : r.Bottom);
            }
            int threshold = (int)(12 * s);
            int best = v, bestD = threshold + 1;
            foreach (int c in cands)
            {
                int d = Math.Abs(c - v);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (bestD <= threshold) (horizontal ? guidesX : guidesY).Add(best);
            return best;
        }
    }
}
