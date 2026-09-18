using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace MagicZones
{
    internal enum ZoneKind { Snap, Maximize, Minimize }

    /// <summary>A zone in fractions (0..1) of its monitor's work area, so it survives resolution changes.</summary>
    internal sealed class ZoneDef
    {
        public double X, Y, W, H;
        public ZoneKind Kind = ZoneKind.Snap;

        public ZoneDef() { }
        public ZoneDef(double x, double y, double w, double h, ZoneKind kind = ZoneKind.Snap)
        {
            X = x; Y = y; W = w; H = h; Kind = kind;
        }

        public ZoneDef Clone() => new ZoneDef(X, Y, W, H, Kind);

        public Rectangle ToPixels(Rectangle area) => Rectangle.FromLTRB(
            area.Left + (int)Math.Round(X * area.Width),
            area.Top + (int)Math.Round(Y * area.Height),
            area.Left + (int)Math.Round((X + W) * area.Width),
            area.Top + (int)Math.Round((Y + H) * area.Height));

        public static ZoneDef FromPixels(Rectangle r, Rectangle area, ZoneKind kind) => new ZoneDef(
            (double)(r.Left - area.Left) / area.Width,
            (double)(r.Top - area.Top) / area.Height,
            (double)r.Width / area.Width,
            (double)r.Height / area.Height,
            kind);

        public static string KindLabel(ZoneKind k)
        {
            switch (k)
            {
                case ZoneKind.Maximize: return Lang.KindMaximize;
                case ZoneKind.Minimize: return Lang.KindMinimize;
                default: return Lang.KindSnap;
            }
        }
    }

    internal sealed class MonitorLayout
    {
        public string MonitorId;
        public string DeviceName;
        public List<ZoneDef> Zones = new List<ZoneDef>();
    }

    internal sealed class AppConfig
    {
        public bool Enabled = true;
        /// <summary>UI language: "auto" (Windows display language), "it" or "en".</summary>
        public string Language = "auto";
        /// <summary>"popup": mini-map popup above the dragged window. "overlay": zones drawn full screen.</summary>
        public string Mode = "popup";
        /// <summary>Popup width in logical px (scaled by the monitor DPI).</summary>
        public int PopupWidth = 460;
        /// <summary>"always": zones appear on every drag (hold Shift to skip). "shift": only while Shift is held.</summary>
        public string Activation = "always";
        public bool ThrowEnabled = true;
        /// <summary>Release speed (physical px/s) above which a drag becomes a throw.</summary>
        public double ThrowMinSpeed = 2600;
        /// <summary>Seconds of "momentum": throw distance = speed × ThrowMomentum.</summary>
        public double ThrowMomentum = 0.35;
        public bool Animate = true;
        public int AnimationMs = 220;
        public int Gap = 8;
        public bool RestoreSizeOnUnsnap = true;
        public bool Hotkeys = true;
        public string AccentColor = "#3B82F6";
        public string ThrowColor = "#F97316";
        public bool DebugLog;
        public List<string> ExcludedProcesses = new List<string>();
        public List<MonitorLayout> Layouts = new List<MonitorLayout>();

        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagicZones");

        public static string FilePath => Path.Combine(Folder, "config.json");

        public static AppConfig Load(out string error)
        {
            error = null;
            var cfg = new AppConfig();
            try
            {
                if (!File.Exists(FilePath)) return cfg;
                var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return cfg;

                cfg.Enabled = Json.Get(root, "enabled", cfg.Enabled);
                cfg.Language = Lang.Normalize(Json.Get(root, "language", cfg.Language));
                cfg.Mode = Json.Get(root, "mode", cfg.Mode) == "overlay" ? "overlay" : "popup";
                cfg.PopupWidth = Math.Max(260, Math.Min(1200, Json.Get(root, "popupWidth", cfg.PopupWidth)));
                cfg.Activation = Json.Get(root, "activation", cfg.Activation);
                cfg.ThrowEnabled = Json.Get(root, "throwEnabled", cfg.ThrowEnabled);
                cfg.ThrowMinSpeed = Json.Get(root, "throwMinSpeed", cfg.ThrowMinSpeed);
                cfg.ThrowMomentum = Json.Get(root, "throwMomentum", cfg.ThrowMomentum);
                cfg.Animate = Json.Get(root, "animate", cfg.Animate);
                cfg.AnimationMs = Json.Get(root, "animationMs", cfg.AnimationMs);
                cfg.Gap = Json.Get(root, "gap", cfg.Gap);
                cfg.RestoreSizeOnUnsnap = Json.Get(root, "restoreSizeOnUnsnap", cfg.RestoreSizeOnUnsnap);
                cfg.Hotkeys = Json.Get(root, "hotkeys", cfg.Hotkeys);
                cfg.AccentColor = Json.Get(root, "accentColor", cfg.AccentColor);
                cfg.ThrowColor = Json.Get(root, "throwColor", cfg.ThrowColor);
                cfg.DebugLog = Json.Get(root, "debugLog", false);

                if (Json.Get<List<object>>(root, "excludedProcesses", null) is List<object> ex)
                    cfg.ExcludedProcesses = ex.OfType<string>().ToList();

                if (Json.Get<List<object>>(root, "layouts", null) is List<object> layouts)
                {
                    foreach (var lo in layouts.OfType<Dictionary<string, object>>())
                    {
                        var layout = new MonitorLayout
                        {
                            MonitorId = Json.Get(lo, "monitorId", ""),
                            DeviceName = Json.Get(lo, "deviceName", ""),
                        };
                        if (Json.Get<List<object>>(lo, "zones", null) is List<object> zones)
                        {
                            foreach (var zo in zones.OfType<Dictionary<string, object>>())
                            {
                                var z = new ZoneDef(
                                    Json.Get(zo, "x", 0.0), Json.Get(zo, "y", 0.0),
                                    Json.Get(zo, "w", 1.0), Json.Get(zo, "h", 1.0));
                                Enum.TryParse(Json.Get(zo, "kind", "Snap"), true, out z.Kind);
                                if (z.W > 0.001 && z.H > 0.001) layout.Zones.Add(z);
                            }
                        }
                        cfg.Layouts.Add(layout);
                    }
                }
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            return cfg;
        }

        public void Save()
        {
            var root = new Dictionary<string, object>
            {
                ["enabled"] = Enabled,
                ["language"] = Language,
                ["mode"] = Mode,
                ["popupWidth"] = PopupWidth,
                ["activation"] = Activation,
                ["throwEnabled"] = ThrowEnabled,
                ["throwMinSpeed"] = ThrowMinSpeed,
                ["throwMomentum"] = ThrowMomentum,
                ["animate"] = Animate,
                ["animationMs"] = AnimationMs,
                ["gap"] = Gap,
                ["restoreSizeOnUnsnap"] = RestoreSizeOnUnsnap,
                ["hotkeys"] = Hotkeys,
                ["accentColor"] = AccentColor,
                ["throwColor"] = ThrowColor,
                ["debugLog"] = DebugLog,
                ["excludedProcesses"] = ExcludedProcesses.Cast<object>().ToList(),
                ["layouts"] = Layouts.Select(l => (object)new Dictionary<string, object>
                {
                    ["monitorId"] = l.MonitorId,
                    ["deviceName"] = l.DeviceName,
                    ["zones"] = l.Zones.Select(z => (object)new Dictionary<string, object>
                    {
                        ["x"] = z.X, ["y"] = z.Y, ["w"] = z.W, ["h"] = z.H, ["kind"] = z.Kind.ToString(),
                    }).ToList(),
                }).ToList(),
            };

            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Json.Write(root), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }

        /// <summary>Layout for a monitor: exact id first, then device name, else null.</summary>
        public MonitorLayout FindLayout(MonitorInfo m)
        {
            return Layouts.FirstOrDefault(l => string.Equals(l.MonitorId, m.Id, StringComparison.OrdinalIgnoreCase))
                ?? Layouts.FirstOrDefault(l => string.Equals(l.DeviceName, m.DeviceName, StringComparison.OrdinalIgnoreCase));
        }

        public MonitorLayout GetOrCreateLayout(MonitorInfo m)
        {
            var layout = FindLayout(m);
            if (layout == null)
            {
                layout = new MonitorLayout { MonitorId = m.Id, DeviceName = m.DeviceName };
                Layouts.Add(layout);
            }
            layout.MonitorId = m.Id;
            layout.DeviceName = m.DeviceName;
            return layout;
        }

        /// <summary>First run: give every monitor a sensible starting layout.</summary>
        public bool EnsureDefaults(IList<MonitorInfo> monitors)
        {
            bool changed = false;
            foreach (var m in monitors)
            {
                if (FindLayout(m) != null) continue;
                var layout = GetOrCreateLayout(m);
                layout.Zones = m.IsPortrait ? Presets.Rows(2) : Presets.Columns(3);
                changed = true;
            }
            return changed;
        }
    }

    internal static class Presets
    {
        public static List<ZoneDef> Columns(int n) =>
            Enumerable.Range(0, n).Select(i => new ZoneDef((double)i / n, 0, 1.0 / n, 1)).ToList();

        public static List<ZoneDef> Rows(int n) =>
            Enumerable.Range(0, n).Select(i => new ZoneDef(0, (double)i / n, 1, 1.0 / n)).ToList();

        public static List<ZoneDef> Grid(int cols, int rows)
        {
            var list = new List<ZoneDef>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    list.Add(new ZoneDef((double)c / cols, (double)r / rows, 1.0 / cols, 1.0 / rows));
            return list;
        }

        public static List<ZoneDef> Priority() => new List<ZoneDef>
        {
            new ZoneDef(0, 0, 0.25, 1),
            new ZoneDef(0.25, 0, 0.5, 1),
            new ZoneDef(0.75, 0, 0.25, 1),
        };

        /// <summary>Big main zone + two stacked side zones.</summary>
        public static List<ZoneDef> MainAndStack() => new List<ZoneDef>
        {
            new ZoneDef(0, 0, 0.62, 1),
            new ZoneDef(0.62, 0, 0.38, 0.5),
            new ZoneDef(0.62, 0.5, 0.38, 0.5),
        };
    }
}
