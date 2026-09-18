using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MagicZones
{
    /// <summary>
    /// Watches system-wide window drags (EVENT_SYSTEM_MOVESIZESTART/END), drives the overlay,
    /// measures cursor velocity and decides on release: drop into a zone, throw, or restore.
    /// </summary>
    internal sealed class DragTracker : IDisposable
    {
        private readonly AppConfig config;
        private readonly ZoneManager zones;
        private readonly OverlayManager overlay;
        private readonly WindowMover mover;
        private readonly PopupWindow popup;

        // Delegates must stay referenced or the GC eats them while Windows still calls them.
        private readonly Native.WinEventDelegate moveSizeProc;
        private readonly Native.WinEventDelegate locationProc;
        private IntPtr moveSizeHook, locationHook;

        private readonly Timer tick = new Timer { Interval = 8 };
        private readonly VelocitySampler sampler = new VelocitySampler();
        private readonly OverlayState state = new OverlayState();

        private IntPtr dragHwnd;
        private Rectangle startRect;      // GetWindowRect at drag start
        private Size startVisibleSize;
        private bool moving;              // confirmed move (not a resize)
        private bool resizing;
        private bool startZoomed;         // maximized when grabbed
        private bool startOnFrame;        // grabbed on the resize border rather than the title bar
        private bool locked;              // higher-integrity window (admin/SYSTEM): Windows won't let us move it

        /// <summary>User tried to launch a window we're not allowed to move.</summary>
        public event Action<IntPtr> Blocked;

        public DragTracker(AppConfig config, ZoneManager zones, OverlayManager overlay, WindowMover mover, PopupWindow popup)
        {
            this.config = config;
            this.zones = zones;
            this.overlay = overlay;
            this.mover = mover;
            this.popup = popup;
            moveSizeProc = OnMoveSize;
            locationProc = OnLocationChange;
            tick.Tick += (s, e) => Tick();
        }

        public void Start()
        {
            if (moveSizeHook != IntPtr.Zero) return;
            moveSizeHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, moveSizeProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            if (moveSizeHook == IntPtr.Zero) Log.Write("SetWinEventHook(MOVESIZE) fallito");
        }

        public void Stop()
        {
            EndDrag(apply: false);
            if (moveSizeHook != IntPtr.Zero) Native.UnhookWinEvent(moveSizeHook);
            moveSizeHook = IntPtr.Zero;
        }

        private void OnMoveSize(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (idObject != Native.OBJID_WINDOW || hwnd == IntPtr.Zero) return;
                Log.Debug((evt == Native.EVENT_SYSTEM_MOVESIZESTART ? "MOVESIZESTART " : "MOVESIZEEND ") + hwnd + " tracked=" + dragHwnd);
                if (evt == Native.EVENT_SYSTEM_MOVESIZESTART) BeginDrag(hwnd);
                else if (evt == Native.EVENT_SYSTEM_MOVESIZEEND && dragHwnd != IntPtr.Zero &&
                         (hwnd == dragHwnd || Native.GetAncestor(hwnd, Native.GA_ROOT) == dragHwnd))
                    EndDrag(apply: true);
            }
            catch (Exception e)
            {
                Log.Write("OnMoveSize: " + e);
            }
        }

        private void OnLocationChange(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            // Extra, precise velocity samples: fires on every window move during the drag.
            if (hwnd == dragHwnd && idObject == Native.OBJID_WINDOW) sampler.Add(Native.CursorPos());
        }

        private void BeginDrag(IntPtr hwnd)
        {
            EndDrag(apply: false);
            if (!config.Enabled || (zones.Zones.Count == 0 && !PopupMode)) return;
            hwnd = Native.GetAncestor(hwnd, Native.GA_ROOT);
            if (hwnd == IntPtr.Zero || IsExcluded(hwnd)) return;

            // The user grabbed it: stop any flight/settle still positioning this window.
            mover.Release(hwnd);

            dragHwnd = hwnd;
            startRect = Native.WindowRect(hwnd);
            startZoomed = Native.IsZoomed(hwnd);
            startOnFrame = IsOnFrame(hwnd, Native.CursorPos());
            var control = Integrity.CanControl(hwnd);
            locked = control == false;
            Log.Debug($"drag begin {hwnd} rect={startRect} zoomed={startZoomed} onFrame={startOnFrame} control={(control?.ToString() ?? "unknown")} cursor={Native.CursorPos()}");
            startVisibleSize = Native.VisibleRect(hwnd).Size;
            moving = resizing = false;
            state.Hover.Clear();
            state.ThrowTarget = null;
            state.OnlyActive = false;
            sampler.Reset();
            sampler.Add(Native.CursorPos());

            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            locationHook = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, locationProc, pid, 0, Native.WINEVENT_OUTOFCONTEXT);
            tick.Start();
        }

        private void Tick()
        {
            if (dragHwnd == IntPtr.Zero) { tick.Stop(); return; }
            if (!Native.IsWindow(dragHwnd)) { EndDrag(apply: false); return; }

            var cursor = Native.CursorPos();
            sampler.Add(cursor);

            if (!moving && !resizing)
            {
                var r = Native.WindowRect(dragHwnd);
                var s0 = startRect;
                if (Math.Abs(r.Width - s0.Width) > 1 || Math.Abs(r.Height - s0.Height) > 1)
                {
                    // Size changed: a resize only if the drag started on the frame. Grabbed by the title bar,
                    // a size change means Windows restored a maximized/snapped window under the cursor
                    // (it first restores in place, top-left fixed, which looks exactly like a resize).
                    if (startOnFrame && !startZoomed) { resizing = true; overlay.Hide(); Log.Debug("resize detected"); return; }
                    moving = true;
                    startVisibleSize = Native.VisibleRect(dragHwnd).Size; // the restored size is the "real" one
                    Log.Debug($"move detected (restored from max/snap) rect={r}");
                }
                else if (r.Location != s0.Location) { moving = true; Log.Debug($"move detected rect={r}"); }
                else return;
            }
            if (resizing) return;

            bool shift = Native.IsKeyDown(Native.VK_SHIFT);
            bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
            bool want = config.Activation == "shift" ? shift : !shift;

            if (!want)
            {
                if (overlay.Visible) overlay.Hide();
                popup.Close();
                state.Hover.Clear();
                state.ThrowTarget = null;
                return;
            }

            if (PopupMode) { TickPopup(cursor, ctrl); return; }

            var hit = zones.HitTest(cursor);
            if (!ctrl) state.Hover.Clear();
            if (hit != null)
            {
                // Spanning only makes sense on one monitor.
                if (state.Hover.Count > 0 && state.Hover.First().Monitor != hit.Monitor) state.Hover.Clear();
                state.Hover.Add(hit);
            }

            state.ThrowTarget = null;
            if (config.ThrowEnabled && !ctrl)
            {
                var v = sampler.Velocity(80);
                if (v.Speed >= config.ThrowMinSpeed)
                {
                    var target = zones.ThrowTarget(cursor, v.X, v.Y, config.ThrowMomentum, out _);
                    if (target != null && target != hit) state.ThrowTarget = target;
                }
            }

            if (!overlay.Visible) overlay.Show(state);
            else overlay.Update(state);
        }

        private bool PopupMode => config.Mode != "overlay";

        /// <summary>Popup mode: the mini-map sits above the window; hovering a tile previews the destination.</summary>
        private void TickPopup(Point cursor, bool ctrl)
        {
            var windowRect = Native.VisibleRect(dragHwnd);
            state.ThrowTarget = null;
            state.OnlyActive = true;
            if (!popup.IsOpen) popup.Open(cursor, windowRect, state, locked ? Integrity.ProcessName(dragHwnd) : null);
            if (locked)
            {
                // Can't move it: the popup explains why, no highlight or destination preview.
                popup.Update(state, windowRect, cursor);
                return;
            }

            var hit = popup.HitTest(cursor);
            if (!ctrl) state.Hover.Clear();
            if (hit != null)
            {
                if (state.Hover.Count > 0 && state.Hover.First().Monitor != hit.Monitor) state.Hover.Clear();
                state.Hover.Add(hit);
            }

            // Ghost of the destination on the real monitor, so you see where it will land.
            if (state.Hover.Count > 0)
            {
                if (!overlay.Visible) overlay.Show(state);
                else overlay.Update(state);
                popup.BringToFront();
            }
            else if (overlay.Visible) overlay.Hide();

            popup.Update(state, windowRect, cursor);
        }

        private void EndDrag(bool apply)
        {
            if (dragHwnd == IntPtr.Zero) return;
            var hwnd = dragHwnd;
            dragHwnd = IntPtr.Zero;
            tick.Stop();
            if (locationHook != IntPtr.Zero) Native.UnhookWinEvent(locationHook);
            locationHook = IntPtr.Zero;

            bool overlayWasOn = overlay.Visible;
            bool popupWasOpen = popup.IsOpen;
            var cursor = Native.CursorPos();
            var popupHit = popupWasOpen ? popup.HitTest(cursor) : null;
            overlay.Hide();
            popup.Close();
            state.OnlyActive = false;
            Log.Debug($"drag end {hwnd} apply={apply} moving={moving} resizing={resizing} popupHit={(popupHit == null ? "-" : popupHit.Monitor.Number + ":" + popupHit.Number)} cursor={cursor} rect={Native.WindowRect(hwnd)}");
            if (!apply || !moving || resizing || !Native.IsWindow(hwnd)) return;

            sampler.Add(cursor);
            bool shift = Native.IsKeyDown(Native.VK_SHIFT);
            bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
            bool active = config.Activation == "shift" ? shift : !shift;

            if (PopupMode)
            {
                // Launch only when released on a tile; anywhere else it's a normal move.
                if (active && popupHit != null && locked)
                {
                    Blocked?.Invoke(hwnd);
                }
                else if (active && popupHit != null)
                {
                    var targets = ctrl && state.Hover.Count > 1 && state.Hover.Contains(popupHit)
                        ? state.Hover.ToList() : new List<Zone> { popupHit };
                    if (targets.Count > 1) mover.Apply(hwnd, ZoneKind.Snap, zones.SpanRect(targets), animate: true, startVisibleSize);
                    else mover.Apply(hwnd, popupHit.Kind, zones.TargetRect(popupHit), animate: true, startVisibleSize);
                }
                else mover.RestoreIfSnapped(hwnd, cursor);
                return;
            }

            if (!active || !overlayWasOn)
            {
                mover.RestoreIfSnapped(hwnd, cursor);
                return;
            }

            if (locked)
            {
                if (zones.HitTest(cursor) != null) Blocked?.Invoke(hwnd);
                return;
            }

            // 1) Throw: released while still moving fast.
            if (config.ThrowEnabled && !ctrl)
            {
                var v = sampler.Velocity(80);
                if (v.Speed >= config.ThrowMinSpeed)
                {
                    var target = zones.ThrowTarget(cursor, v.X, v.Y, config.ThrowMomentum, out _);
                    if (target != null)
                    {
                        mover.Apply(hwnd, target.Kind, zones.TargetRect(target), animate: true, startVisibleSize);
                        return;
                    }
                }
            }

            // 2) Drop: into the hovered zone(s).
            var hovered = state.Hover.ToList();
            var hit = zones.HitTest(cursor);
            if (hovered.Count == 0 && hit != null) hovered.Add(hit);
            if (hovered.Count > 1)
            {
                mover.Apply(hwnd, ZoneKind.Snap, zones.SpanRect(hovered), animate: true, startVisibleSize);
                return;
            }
            if (hovered.Count == 1)
            {
                var z = hovered[0];
                mover.Apply(hwnd, z.Kind, zones.TargetRect(z), animate: true, startVisibleSize);
                return;
            }

            // 3) Nowhere: plain move; un-snap size if we snapped it before.
            mover.RestoreIfSnapped(hwnd, cursor);
        }

        /// <summary>
        /// Cursor on the resize border? The border is mostly outside the visible frame (invisible
        /// Win10/11 borders) plus a thin band inside it; the top band sits on the title bar's top edge.
        /// </summary>
        private bool IsOnFrame(IntPtr hwnd, Point c)
        {
            var vis = Native.VisibleRect(hwnd);
            var m = Monitors.FromPoint(zones.Monitors, c);
            int band = (int)Math.Round(6 * (m?.Scale ?? 1f));
            if (!vis.Contains(c)) return true;
            return c.X - vis.Left < band || vis.Right - 1 - c.X < band || vis.Bottom - 1 - c.Y < band || c.Y - vis.Top < band;
        }

        private bool IsExcluded(IntPtr hwnd)
        {
            int style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            if ((style & Native.WS_CHILD) != 0) return true;
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (style & Native.WS_CAPTION) == 0) return true;
            if (!Native.IsWindowVisible(hwnd) || Native.IsCloaked(hwnd)) return true;
            if (config.ExcludedProcesses.Count == 0) return false;
            try
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                using (var p = Process.GetProcessById((int)pid))
                {
                    string name = p.ProcessName;
                    return config.ExcludedProcesses.Any(x =>
                        string.Equals(x.Replace(".exe", ""), name, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
            tick.Dispose();
        }
    }

    /// <summary>Ring buffer of timestamped cursor positions → release velocity.</summary>
    internal sealed class VelocitySampler
    {
        private struct Sample { public double T; public Point P; }

        private readonly Sample[] buf = new Sample[128];
        private int count, head;
        private readonly Stopwatch clock = Stopwatch.StartNew();

        public struct Vel
        {
            public double X, Y;
            public double Speed => Math.Sqrt(X * X + Y * Y);
        }

        public void Reset() { count = 0; head = 0; }

        public void Add(Point p)
        {
            double t = clock.Elapsed.TotalMilliseconds;
            // Collapse samples closer than 2ms (the tick and location hook both feed us).
            if (count > 0)
            {
                ref var last = ref buf[(head - 1 + buf.Length) % buf.Length];
                if (t - last.T < 2) { last.P = p; last.T = t; return; }
            }
            buf[head] = new Sample { T = t, P = p };
            head = (head + 1) % buf.Length;
            if (count < buf.Length) count++;
        }

        /// <summary>Average velocity (px/s) over the last <paramref name="windowMs"/> ms up to now.</summary>
        public Vel Velocity(double windowMs)
        {
            if (count < 2) return default;
            double now = clock.Elapsed.TotalMilliseconds;
            var newest = buf[(head - 1 + buf.Length) % buf.Length];
            // If the cursor sat still before release, the newest sample is old → no throw.
            if (now - newest.T > windowMs) return default;

            Sample oldest = newest;
            for (int i = 1; i < count; i++)
            {
                var s = buf[(head - 1 - i + buf.Length * 2) % buf.Length];
                if (now - s.T > windowMs) break;
                oldest = s;
            }
            double dt = (newest.T - oldest.T) / 1000.0;
            if (dt < 0.012) return default;
            return new Vel { X = (newest.P.X - oldest.P.X) / dt, Y = (newest.P.Y - oldest.P.Y) / dt };
        }
    }
}
