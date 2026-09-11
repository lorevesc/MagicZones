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

        public DragTracker(AppConfig config, ZoneManager zones, OverlayManager overlay, WindowMover mover)
        {
            this.config = config;
            this.zones = zones;
            this.overlay = overlay;
            this.mover = mover;
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
                if (evt == Native.EVENT_SYSTEM_MOVESIZESTART) BeginDrag(hwnd);
                else if (evt == Native.EVENT_SYSTEM_MOVESIZEEND && hwnd == dragHwnd) EndDrag(apply: true);
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
            if (!config.Enabled || zones.Zones.Count == 0) return;
            hwnd = Native.GetAncestor(hwnd, Native.GA_ROOT);
            if (hwnd == IntPtr.Zero || IsExcluded(hwnd)) return;

            dragHwnd = hwnd;
            startRect = Native.WindowRect(hwnd);
            startVisibleSize = Native.VisibleRect(hwnd).Size;
            moving = resizing = false;
            state.Hover.Clear();
            state.ThrowTarget = null;
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
                    // Size changed. A real resize keeps one edge fixed on each axis; a maximized (or
                    // OS-snapped) window being dragged out gets restored, so every edge jumps: that's a move.
                    bool hFixed = Math.Abs(r.Left - s0.Left) <= 1 || Math.Abs(r.Right - s0.Right) <= 1;
                    bool vFixed = Math.Abs(r.Top - s0.Top) <= 1 || Math.Abs(r.Bottom - s0.Bottom) <= 1;
                    if (hFixed && vFixed) { resizing = true; overlay.Hide(); return; }
                    moving = true;
                    startVisibleSize = Native.VisibleRect(dragHwnd).Size; // the restored size is the "real" one
                }
                else if (r.Location != s0.Location) moving = true;
                else return;
            }
            if (resizing) return;

            bool shift = Native.IsKeyDown(Native.VK_SHIFT);
            bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
            bool want = config.Activation == "shift" ? shift : !shift;

            if (!want)
            {
                if (overlay.Visible) overlay.Hide();
                state.Hover.Clear();
                state.ThrowTarget = null;
                return;
            }

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

        private void EndDrag(bool apply)
        {
            if (dragHwnd == IntPtr.Zero) return;
            var hwnd = dragHwnd;
            dragHwnd = IntPtr.Zero;
            tick.Stop();
            if (locationHook != IntPtr.Zero) Native.UnhookWinEvent(locationHook);
            locationHook = IntPtr.Zero;

            bool overlayWasOn = overlay.Visible;
            overlay.Hide();
            if (!apply || !moving || resizing || !Native.IsWindow(hwnd)) return;

            var cursor = Native.CursorPos();
            sampler.Add(cursor);
            bool shift = Native.IsKeyDown(Native.VK_SHIFT);
            bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
            bool active = config.Activation == "shift" ? shift : !shift;

            if (!active || !overlayWasOn)
            {
                mover.RestoreIfSnapped(hwnd, cursor);
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
