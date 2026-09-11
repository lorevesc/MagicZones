using System;
using System.Threading;
using System.Windows.Forms;

namespace MagicZones
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Physical pixels everywhere: required for correct geometry on mixed-DPI multi-monitor setups.
            try { Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }

            if (args.Length == 2 && args[0] == "--export-icon")
            {
                IconArt.ExportIco(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--preview")
            {
                RenderPreviews(args[1]);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--selftest")
                return SelfTest.Run();

            using (var mutex = new Mutex(true, @"Local\MagicZones.SingleInstance", out bool first))
            {
                if (!first)
                {
                    // Already running: launching it again opens the zone editor in the running instance.
                    Native.PostMessage(Native.HWND_BROADCAST, TrayApp.OpenEditorMessage, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("ThreadException: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Unhandled: " + e.ExceptionObject);

                Log.Write("Avvio MagicZones");
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        /// <summary>Renders overlay + editor for every monitor to PNGs (never shown on screen).</summary>
        private static void RenderPreviews(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            var config = AppConfig.Load(out _);
            var zones = new ZoneManager(config);
            zones.Rebuild();
            config.EnsureDefaults(zones.Monitors);
            zones.Rebuild();

            foreach (var m in zones.Monitors)
            {
                var mine = System.Linq.Enumerable.ToList(zones.ZonesOn(m));
                using (var w = new OverlayWindow(m, config, zones))
                {
                    var state = new OverlayState();
                    if (mine.Count > 0) state.Hover.Add(mine[0]);
                    if (mine.Count > 1) state.ThrowTarget = mine[mine.Count - 1];
                    w.Update(state, force: true);
                    w.SaveSnapshot(System.IO.Path.Combine(dir, $"overlay-{m.Number}.png"));

                    if (mine.Count > 1)
                    {
                        var span = new OverlayState();
                        span.Hover.Add(mine[0]);
                        span.Hover.Add(mine[1]);
                        w.Update(span, force: true);
                        w.SaveSnapshot(System.IO.Path.Combine(dir, $"overlay-span-{m.Number}.png"));
                    }
                }
            }

            var session = new EditorSession(config, zones.Monitors, _ => { }, show: false);
            session.SelectedMonitor = zones.Monitors[0];
            session.SelectedIndex = 0;
            session.RedrawAll();
            for (int i = 0; i < session.Windows.Count; i++)
                session.Windows[i].SaveSnapshot(System.IO.Path.Combine(dir, $"editor-{zones.Monitors[i].Number}.png"));
            session.Dispose();
        }
    }
}
