using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MagicZones
{
    /// <summary>
    /// Headless checks for the pure logic (JSON, zone math, throw physics) against a synthetic
    /// two-monitor desktop: portrait 1080×1920 on the left, 2560×1440 primary on the right.
    /// Run: MagicZones.exe --selftest | Out-String   (exit code = number of failures)
    /// </summary>
    internal static class SelfTest
    {
        private static int failures;

        public static int Run()
        {
            failures = 0;
            JsonRoundTrip();
            ZoneMath();
            Throws();
            Neighbours();
            PopupTiles();
            StartupPaths();
            Languages();
            Console.WriteLine(failures == 0 ? "SELFTEST OK" : $"SELFTEST: {failures} FALLITI");
            return failures;
        }

        private static void Check(bool ok, string name, string detail = "")
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name} {detail}");
            if (!ok) failures++;
        }

        private static ZoneManager Synthetic(out AppConfig cfg)
        {
            var portrait = new MonitorInfo
            {
                Id = "P", DeviceName = "P", Number = 1,
                Bounds = new Rectangle(-1080, 0, 1080, 1920), WorkArea = new Rectangle(-1080, 0, 1080, 1872),
            };
            var main = new MonitorInfo
            {
                Id = "M", DeviceName = "M", Number = 2, Primary = true,
                Bounds = new Rectangle(0, 0, 2560, 1440), WorkArea = new Rectangle(0, 0, 2560, 1392),
            };
            cfg = new AppConfig { Gap = 8 };
            cfg.Layouts.Add(new MonitorLayout { MonitorId = "P", DeviceName = "P", Zones = Presets.Rows(2) });
            cfg.Layouts.Add(new MonitorLayout { MonitorId = "M", DeviceName = "M", Zones = Presets.Columns(3) });
            var zm = new ZoneManager(cfg);
            zm.RebuildFrom(new List<MonitorInfo> { portrait, main });
            return zm;
        }

        private static string Name(Zone z) => z == null ? "null" : $"M{z.Monitor.Number}Z{z.Number}";

        private static void JsonRoundTrip()
        {
            var obj = new Dictionary<string, object>
            {
                ["s"] = "a\"b\\c\nè", ["n"] = 0.33333333, ["b"] = true, ["nil"] = null,
                ["list"] = new List<object> { 1.0, "x", new Dictionary<string, object> { ["k"] = 2.0 } },
            };
            var back = Json.Parse(Json.Write(obj)) as Dictionary<string, object>;
            Check(back != null && (string)back["s"] == "a\"b\\c\nè", "json: stringhe con escape");
            Check(back != null && Math.Abs((double)back["n"] - 0.33333) < 1e-9, "json: numeri arrotondati a 5 decimali");
            Check(back != null && (bool)back["b"] && back["nil"] == null, "json: bool/null");
            var parsed = Json.Parse("// commento\n{ \"a\": [1, 2,], }") as Dictionary<string, object>;
            Check(parsed != null && ((List<object>)parsed["a"]).Count == 2, "json: commenti e virgole finali tollerati");
        }

        private static void ZoneMath()
        {
            var zm = Synthetic(out _);
            Check(zm.Zones.Count == 5, "zone: 2 + 3 zone create", zm.Zones.Count.ToString());
            Check(Name(zm.HitTest(new Point(100, 100))) == "M2Z1", "zone: hit test monitor principale");
            Check(Name(zm.HitTest(new Point(-500, 1500))) == "M1Z2", "zone: hit test monitor verticale");
            Check(zm.HitTest(new Point(100, 1420)) == null, "zone: barra applicazioni esclusa");

            var z1 = zm.Zones.First(z => z.Monitor.Number == 2 && z.Number == 1);
            var t = zm.TargetRect(z1);
            Check(t == Rectangle.FromLTRB(8, 8, 853 - 4, 1392 - 8), "zone: gap esterni 8 / interni 4", t.ToString());

            var span = zm.SpanRect(zm.Zones.Where(z => z.Monitor.Number == 2 && z.Number <= 2).ToList());
            Check(span.Left == 8 && span.Right == 1707 - 4, "zone: unione Ctrl di 2 zone", span.ToString());
        }

        private static void Throws()
        {
            var zm = Synthetic(out var cfg);
            var from = new Point(400, 700);
            Check(Name(zm.ThrowTarget(from, 4000, 0, cfg.ThrowMomentum, out _)) == "M2Z3", "lancio: forte a destra = zona 3");
            Check(Name(zm.ThrowTarget(from, 2800, 0, cfg.ThrowMomentum, out _)) == "M2Z2", "lancio: medio a destra = zona 2");
            Check(Name(zm.ThrowTarget(from, -4000, -300, cfg.ThrowMomentum, out _)) == "M1Z1", "lancio: a sinistra su monitor verticale (alto)");
            Check(Name(zm.ThrowTarget(from, -3000, 2600, cfg.ThrowMomentum, out _)) == "M1Z2", "lancio: in diagonale giù-sinistra (basso)");
            Check(Name(zm.ThrowTarget(new Point(2400, 700), 20000, 0, cfg.ThrowMomentum, out _)) == "M2Z3", "lancio: contro il muro resta sull'ultima zona");
            Check(Name(zm.ThrowTarget(new Point(-540, 400), 9000, 0, cfg.ThrowMomentum, out _)) == "M2Z3", "lancio: verticale -> fondo del principale");
        }

        private static void PopupTiles()
        {
            var zm = Synthetic(out var cfg);
            using (var popup = new PopupWindow(cfg, zm))
            {
                // Window high on the main monitor: no room above, popup must float below the cursor.
                popup.Layout(new Point(1200, 30), new Rectangle(700, 14, 1100, 700));
                var b = popup.Bounds;
                var wa = zm.Monitors[1].WorkArea;
                Check(b.Top > 30 && b.Bottom <= wa.Bottom + 20 && b.Left >= wa.Left - 20 && b.Right <= wa.Right + 20,
                    "popup: senza spazio sopra finisce sotto il cursore, dentro lo schermo", b.ToString());

                // Window lower down: popup sits above its top edge.
                popup.Layout(new Point(1200, 616), new Rectangle(700, 600, 1100, 700));
                Check(popup.Bounds.Bottom <= 600 + 20, "popup: appare sopra la finestra", popup.Bounds.ToString());

                var centers = popup.TileCenters().ToList();
                Check(centers.Count == 5, "popup: una tessera per zona", centers.Count.ToString());
                bool allHit = centers.All(c => popup.HitTest(c.Value) == c.Key);
                Check(allHit, "popup: il centro di ogni tessera colpisce la sua zona");
                Check(popup.HitTest(new Point(popup.Bounds.X + 2, popup.Bounds.Y + 2)) == null, "popup: bordo del pannello = nessuna zona");

                // Monitor without zones: the whole monitor becomes one tile.
                cfg.Layouts[0].Zones.Clear();
                zm.RebuildFrom(zm.Monitors);
                popup.Layout(new Point(1200, 616), new Rectangle(700, 600, 1100, 700));
                var whole = popup.TileCenters().Select(c => c.Key).FirstOrDefault(z => z.Number == 0);
                Check(whole != null && zm.TargetRect(whole) == Rectangle.FromLTRB(-1072, 8, -8, 1864),
                    "popup: monitor senza zone = tessera schermo intero");
            }
        }

        private static void StartupPaths()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            Check(Startup.IsStableLocation(System.IO.Path.Combine(Startup.InstallDir, "MagicZones.exe")),
                "avvio: %LOCALAPPDATA%\\Programs\\MagicZones è stabile");
            Check(Startup.IsStableLocation(System.IO.Path.Combine(pf, "MagicZones", "MagicZones.exe")), "avvio: Program Files è stabile");
            Check(!Startup.IsStableLocation(System.IO.Path.Combine(desktop, "MagicZones.exe")), "avvio: Desktop rifiutato");
            Check(!Startup.IsStableLocation(System.IO.Path.Combine(profile, "Downloads", "MagicZones.exe")), "avvio: Download rifiutato");
            Check(!Startup.IsStableLocation(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MagicZones.exe")), "avvio: Temp rifiutato");
            Check(!Startup.IsStableLocation(System.IO.Path.Combine(local, "ProgramsX", "MagicZones.exe")), "avvio: cartella simile ma diversa rifiutata");
            string od = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(od))
                Check(!Startup.IsStableLocation(System.IO.Path.Combine(od, "Desktop", "MagicZones.exe")), "avvio: OneDrive rifiutato");

            Check(Startup.ExePathOf("\"C:\\a b\\MagicZones.exe\" --x") == "C:\\a b\\MagicZones.exe", "avvio: valore Run tra virgolette con argomenti");
            Check(Startup.ExePathOf("C:\\x\\MagicZones.exe") == "C:\\x\\MagicZones.exe", "avvio: valore Run senza virgolette");
            Check(Startup.ExePathOf("  ") == null, "avvio: valore Run vuoto");
        }

        private static void Languages()
        {
            Check(Lang.Normalize("EN ") == "en" && Lang.Normalize("it") == "it", "lingua: codici normalizzati");
            Check(Lang.Normalize("de") == "auto" && Lang.Normalize(null) == "auto", "lingua: sconosciuta = auto");
            Lang.Apply("en");
            Check(!Lang.Italian && ZoneDef.KindLabel(ZoneKind.Maximize) == "MAXIMIZE", "lingua: en forzato");
            Lang.Apply("it");
            Check(Lang.Italian && ZoneDef.KindLabel(ZoneKind.Maximize) == "MASSIMIZZA", "lingua: it forzato");
            Lang.Apply("auto");
            Check(Lang.Italian == Lang.Detect(), "lingua: auto segue Windows", System.Globalization.CultureInfo.CurrentUICulture.Name);
        }

        private static void Neighbours()
        {
            var zm = Synthetic(out _);
            var inZ1 = Rectangle.FromLTRB(8, 8, 849, 1384);
            Check(Name(zm.Neighbour(inZ1, 1, 0)) == "M2Z2", "tasti: Ctrl+Alt+Win+→ da zona 1");
            Check(Name(zm.Neighbour(inZ1, -1, 0)) == "M1Z1", "tasti: Ctrl+Alt+Win+← passa al monitor verticale");
            var inP2 = Rectangle.FromLTRB(-1072, 940, -8, 1864);
            Check(Name(zm.Neighbour(inP2, 0, -1)) == "M1Z1", "tasti: ↑ nel monitor verticale");
        }
    }
}
