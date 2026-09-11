using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MagicZones
{
    /// <summary>Moves other apps' windows: snapping, throw animation, restore-on-unsnap.</summary>
    internal sealed class WindowMover : IDisposable
    {
        private readonly AppConfig config;
        private readonly Timer animTimer = new Timer { Interval = 10 };
        private readonly Dictionary<IntPtr, Size> originalSizes = new Dictionary<IntPtr, Size>();
        private readonly List<Settle> settles = new List<Settle>();
        private readonly Timer settleTimer = new Timer { Interval = 40 };
        private Animation anim;

        private sealed class Animation
        {
            public IntPtr Hwnd;
            public Rectangle From, To;
            public Stopwatch Clock;
            public int DurationMs;
            public bool Resizable;
            public Action Done;
        }

        private sealed class Settle
        {
            public IntPtr Hwnd;
            public Rectangle Target;
            public bool Resizable;
            public Stopwatch Clock;
            public int Passes;
        }

        public WindowMover(AppConfig config)
        {
            this.config = config;
            animTimer.Tick += (s, e) => AnimStep();
            settleTimer.Tick += (s, e) => SettleStep();
        }

        public static bool IsResizable(IntPtr hwnd) => (Native.GetWindowLong(hwnd, Native.GWL_STYLE) & Native.WS_THICKFRAME) != 0;

        /// <summary>Remember the pre-snap size once, so dragging the window out can restore it.</summary>
        public void RememberOriginal(IntPtr hwnd, Size visibleSize)
        {
            if (!originalSizes.ContainsKey(hwnd)) originalSizes[hwnd] = visibleSize;
        }

        public void ForgetOriginal(IntPtr hwnd) => originalSizes.Remove(hwnd);

        /// <summary>Put the window into a zone (or zone span). Handles snap / maximize / minimize.</summary>
        public void Apply(IntPtr hwnd, ZoneKind kind, Rectangle target, bool animate, Size preDragSize)
        {
            CancelAnimation(hwnd);
            switch (kind)
            {
                case ZoneKind.Minimize:
                    Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
                    return;

                case ZoneKind.Maximize:
                    // Park it on the right monitor first so it maximizes there (and restores sensibly).
                    if (Native.IsZoomed(hwnd) || Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
                    var parked = CenterIn(target, new Size(Math.Min(preDragSize.Width, target.Width), Math.Min(preDragSize.Height, target.Height)));
                    PlaceVisible(hwnd, parked, resize: true, async: false);
                    Native.ShowWindow(hwnd, Native.SW_MAXIMIZE);
                    return;
            }

            if (Native.IsZoomed(hwnd) || Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
            bool resizable = IsResizable(hwnd);
            var from = Native.VisibleRect(hwnd);
            if (!resizable) target = CenterIn(target, from.Size);
            RememberOriginal(hwnd, preDragSize);

            if (animate && config.Animate && config.AnimationMs > 0)
            {
                anim = new Animation
                {
                    Hwnd = hwnd, From = from, To = target, Resizable = resizable,
                    DurationMs = config.AnimationMs, Clock = Stopwatch.StartNew(),
                    Done = () => Finish(hwnd, target, resizable),
                };
                animTimer.Start();
                AnimStep();
            }
            else
            {
                Finish(hwnd, target, resizable);
            }
        }

        /// <summary>Dropped outside any zone: give a previously snapped window its old size back.</summary>
        public void RestoreIfSnapped(IntPtr hwnd, Point cursor)
        {
            if (!config.RestoreSizeOnUnsnap || !originalSizes.TryGetValue(hwnd, out var size)) return;
            originalSizes.Remove(hwnd);
            if (!IsResizable(hwnd) || Native.IsZoomed(hwnd) || Native.IsIconic(hwnd)) return;

            var cur = Native.VisibleRect(hwnd);
            if (cur.Width <= 0) return;
            // Keep the grab point under the cursor (proportionally horizontally, same offset vertically).
            double fx = (double)(cursor.X - cur.Left) / cur.Width;
            int x = cursor.X - (int)(fx * size.Width);
            int y = cur.Top;
            PlaceVisible(hwnd, new Rectangle(x, y, size.Width, size.Height), resize: true, async: false);
        }

        private void Finish(IntPtr hwnd, Rectangle target, bool resizable)
        {
            PlaceVisible(hwnd, target, resizable, async: false);
            // Crossing a DPI boundary makes apps resize themselves on WM_DPICHANGED; re-apply a few times.
            settles.RemoveAll(x => x.Hwnd == hwnd);
            settles.Add(new Settle { Hwnd = hwnd, Target = target, Resizable = resizable, Clock = Stopwatch.StartNew() });
            settleTimer.Start();
        }

        private void SettleStep()
        {
            foreach (var st in settles.ToList())
            {
                if (!Native.IsWindow(st.Hwnd) || st.Passes >= 3 || st.Clock.ElapsedMilliseconds > 800)
                {
                    settles.Remove(st);
                    continue;
                }
                long due = st.Passes == 0 ? 60 : st.Passes == 1 ? 180 : 450;
                if (st.Clock.ElapsedMilliseconds < due) continue;
                st.Passes++;
                var now = Native.VisibleRect(st.Hwnd);
                if (Math.Abs(now.Left - st.Target.Left) > 2 || Math.Abs(now.Top - st.Target.Top) > 2 ||
                    (st.Resizable && (Math.Abs(now.Width - st.Target.Width) > 2 || Math.Abs(now.Height - st.Target.Height) > 2)))
                    PlaceVisible(st.Hwnd, st.Target, st.Resizable, async: false);
            }
            if (settles.Count == 0) settleTimer.Stop();
        }

        private void AnimStep()
        {
            var a = anim;
            if (a == null) { animTimer.Stop(); return; }
            if (!Native.IsWindow(a.Hwnd)) { anim = null; animTimer.Stop(); return; }

            double t = a.Clock.Elapsed.TotalMilliseconds / a.DurationMs;
            if (t >= 1)
            {
                anim = null;
                animTimer.Stop();
                a.Done();
                return;
            }
            var r = Geometry.Lerp(a.From, a.To, Geometry.EaseOutBack(t));
            PlaceVisible(a.Hwnd, r, a.Resizable, async: true);
        }

        private void CancelAnimation(IntPtr hwnd)
        {
            if (anim == null) return;
            var a = anim;
            anim = null;
            animTimer.Stop();
            if (a.Hwnd != hwnd) a.Done(); // someone else was mid-flight: land it immediately
        }

        /// <summary>SetWindowPos in terms of the *visible* frame, compensating the invisible borders.</summary>
        public static void PlaceVisible(IntPtr hwnd, Rectangle visible, bool resize, bool async)
        {
            var wr = Native.WindowRect(hwnd);
            var vr = Native.VisibleRect(hwnd);
            int ml = vr.Left - wr.Left, mt = vr.Top - wr.Top, mr = wr.Right - vr.Right, mb = wr.Bottom - vr.Bottom;
            // Sanity: margins are small invisible borders; anything weird means DWM gave us garbage.
            if (ml < 0 || mt < 0 || mr < 0 || mb < 0 || ml > 40 || mr > 40 || mb > 40 || mt > 40) ml = mt = mr = mb = 0;

            uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
            if (!resize) flags |= Native.SWP_NOSIZE;
            if (async) flags |= Native.SWP_ASYNCWINDOWPOS;
            Native.SetWindowPos(hwnd, IntPtr.Zero,
                visible.Left - ml, visible.Top - mt,
                visible.Width + ml + mr, visible.Height + mt + mb, flags);
        }

        private static Rectangle CenterIn(Rectangle area, Size size)
        {
            return new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2, size.Width, size.Height);
        }

        /// <summary>Drop bookkeeping for windows that no longer exist.</summary>
        public void Prune()
        {
            foreach (var h in originalSizes.Keys.Where(h => !Native.IsWindow(h)).ToList()) originalSizes.Remove(h);
        }

        public void Dispose()
        {
            animTimer.Dispose();
            settleTimer.Dispose();
        }
    }
}
