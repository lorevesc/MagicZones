using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace MagicZones
{
    internal sealed class MonitorInfo
    {
        public IntPtr Handle;
        public string DeviceName;   // \\.\DISPLAY1 (can change between reboots)
        public string Id;           // device interface path, stable per physical monitor + port
        public Rectangle Bounds;    // physical pixels, virtual-screen coordinates
        public Rectangle WorkArea;  // Bounds minus taskbar
        public bool Primary;
        public int Dpi = 96;
        public int Number;          // 1-based, ordered left to right

        public float Scale => Dpi / 96f;
        public bool IsPortrait => WorkArea.Height > WorkArea.Width;

        public override string ToString() => $"Monitor {Number} · {Bounds.Width}×{Bounds.Height} · {Dpi * 100 / 96}%";
    }

    internal static class Monitors
    {
        public static List<MonitorInfo> Enumerate()
        {
            var list = new List<MonitorInfo>();
            Native.MonitorEnumProc proc = (IntPtr hMon, IntPtr hdc, ref Native.RECT r, IntPtr data) =>
            {
                var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX)) };
                if (!Native.GetMonitorInfo(hMon, ref mi)) return true;
                var info = new MonitorInfo
                {
                    Handle = hMon,
                    DeviceName = mi.szDevice,
                    Bounds = mi.rcMonitor.ToRectangle(),
                    WorkArea = mi.rcWork.ToRectangle(),
                    Primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                };
                try
                {
                    if (Native.GetDpiForMonitor(hMon, 0, out uint dx, out _) == 0 && dx > 0) info.Dpi = (int)dx;
                }
                catch { }
                info.Id = MonitorDeviceId(mi.szDevice) ?? mi.szDevice;
                list.Add(info);
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);

            list = list.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
            for (int i = 0; i < list.Count; i++) list[i].Number = i + 1;
            return list;
        }

        private static string MonitorDeviceId(string adapterDevice)
        {
            var dd = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE)) };
            if (Native.EnumDisplayDevices(adapterDevice, 0, ref dd, Native.EDD_GET_DEVICE_INTERFACE_NAME)
                && !string.IsNullOrEmpty(dd.DeviceID))
                return dd.DeviceID;
            return null;
        }

        public static MonitorInfo FromPoint(IList<MonitorInfo> monitors, Point p)
        {
            foreach (var m in monitors)
                if (m.Bounds.Contains(p)) return m;
            // Outside every monitor: pick the closest one.
            MonitorInfo best = null;
            double bestD = double.MaxValue;
            foreach (var m in monitors)
            {
                double d = Geometry.DistanceToRect(p, m.Bounds);
                if (d < bestD) { bestD = d; best = m; }
            }
            return best;
        }
    }
}
