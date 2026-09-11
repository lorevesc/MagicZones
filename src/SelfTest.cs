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
