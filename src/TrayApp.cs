using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MagicZones
{
    internal sealed class TrayApp : ApplicationContext
    {
        public static readonly uint OpenEditorMessage = Native.RegisterWindowMessage("MagicZones.OpenEditor");

        private const int HK_EDITOR = 1, HK_LEFT = 2, HK_UP = 3, HK_RIGHT = 4, HK_DOWN = 5;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly AppConfig config;
        private readonly ZoneManager zones;
        private readonly OverlayManager overlay;
        private readonly WindowMover mover;
        private readonly DragTracker tracker;
        private readonly NotifyIcon tray;
        private readonly MessageWindow messages;
        private readonly SynchronizationContext ui;
        private readonly System.Windows.Forms.Timer rebuildTimer = new System.Windows.Forms.Timer { Interval = 700 };
        private readonly System.Windows.Forms.Timer pruneTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        private EditorSession editor;
        private Icon iconOn, iconOff;

        private ToolStripMenuItem miEnabled, miAlways, miShift, miThrow, miAnimate, miStartup, miGap;

        public TrayApp()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            bool firstRun = !File.Exists(AppConfig.FilePath);
            config = AppConfig.Load(out string loadError);
            zones = new ZoneManager(config);
            zones.Rebuild();
            if (config.EnsureDefaults(zones.Monitors))
            {
                TrySave();
                zones.Rebuild();
            }

            overlay = new OverlayManager(config, zones);
            overlay.Rebuild();
            mover = new WindowMover(config);
            tracker = new DragTracker(config, zones, overlay, mover);
            tracker.Start();

            iconOn = IconArt.CreateIcon(32, true);
            iconOff = IconArt.CreateIcon(32, false);
            tray = new NotifyIcon { Icon = config.Enabled ? iconOn : iconOff, Text = "MagicZones", Visible = true };
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenEditor(); };

            messages = new MessageWindow(this);
            RegisterHotkeys();

            rebuildTimer.Tick += (s, e) => { rebuildTimer.Stop(); RebuildMonitors(); };
            pruneTimer.Tick += (s, e) => mover.Prune();
            pruneTimer.Start();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            if (loadError != null)
                tray.ShowBalloonTip(6000, "MagicZones: config non valida",
                    "Uso le impostazioni di default. Errore: " + loadError, ToolTipIcon.Warning);
            else if (firstRun)
                tray.ShowBalloonTip(6000, "MagicZones è attivo",
                    "Trascina una finestra: appaiono le zone. Lanciala veloce per scagliarla in una zona. " +
                    "Doppio clic sull'icona per disegnare le tue zone.", ToolTipIcon.Info);
        }

        // ---- Menu -------------------------------------------------------------------------
        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            var title = new ToolStripMenuItem("MagicZones") { Enabled = false };
            title.Font = new Font(title.Font, FontStyle.Bold);
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());

            miEnabled = new ToolStripMenuItem("Attivo", null, (s, e) => SetEnabled(!config.Enabled)) { Checked = config.Enabled };
            menu.Items.Add(miEnabled);
            menu.Items.Add(new ToolStripMenuItem("Modifica zone…", null, (s, e) => OpenEditor())
                { ShortcutKeyDisplayString = "Ctrl+Alt+Win+Z", Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());

            var show = new ToolStripMenuItem("Mostra zone quando trascini");
            miAlways = new ToolStripMenuItem("Sempre (tieni Shift per ignorarle)", null, (s, e) => SetActivation("always"));
            miShift = new ToolStripMenuItem("Solo tenendo premuto Shift", null, (s, e) => SetActivation("shift"));
            show.DropDownItems.Add(miAlways);
            show.DropDownItems.Add(miShift);
            menu.Items.Add(show);

            miThrow = new ToolStripMenuItem("Lancio delle finestre (fling)", null, (s, e) =>
            {
                config.ThrowEnabled = !config.ThrowEnabled;
                TrySave();
                RefreshMenu();
            });
            menu.Items.Add(miThrow);

            miAnimate = new ToolStripMenuItem("Animazioni", null, (s, e) =>
            {
                config.Animate = !config.Animate;
                TrySave();
                RefreshMenu();
            });
            menu.Items.Add(miAnimate);

            miGap = new ToolStripMenuItem("Spaziatura tra finestre");
            foreach (int gap in new[] { 0, 4, 8, 12, 16, 24 })
            {
                int g = gap;
                miGap.DropDownItems.Add(new ToolStripMenuItem(g == 0 ? "Nessuna" : g + " px", null, (s, e) =>
                {
                    config.Gap = g;
                    TrySave();
                    RefreshMenu();
                    overlay.Rebuild();
                }) { Tag = g });
            }
            menu.Items.Add(miGap);
            menu.Items.Add(new ToolStripSeparator());

            miStartup = new ToolStripMenuItem("Avvia con Windows", null, (s, e) => ToggleStartup());
            menu.Items.Add(miStartup);
            menu.Items.Add(new ToolStripMenuItem("Apri cartella configurazione", null, (s, e) =>
            {
                Directory.CreateDirectory(AppConfig.Folder);
                Process.Start("explorer.exe", "\"" + AppConfig.Folder + "\"");
            }));
            menu.Items.Add(new ToolStripMenuItem("Ricarica configurazione", null, (s, e) => ReloadConfig()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Esci", null, (s, e) => ExitThread()));

            menu.Opening += (s, e) => RefreshMenu();
            RefreshMenu();
            return menu;
        }

        private void RefreshMenu()
        {
            miEnabled.Checked = config.Enabled;
            miAlways.Checked = config.Activation != "shift";
            miShift.Checked = config.Activation == "shift";
            miThrow.Checked = config.ThrowEnabled;
            miAnimate.Checked = config.Animate;
            miStartup.Checked = IsStartupEnabled();
            foreach (ToolStripMenuItem item in miGap.DropDownItems) item.Checked = (int)item.Tag == config.Gap;
            tray.Icon = config.Enabled ? iconOn : iconOff;
            tray.Text = config.Enabled ? "MagicZones — attivo" : "MagicZones — in pausa";
        }

        private void SetEnabled(bool on)
        {
            config.Enabled = on;
            if (!on) overlay.HideNow();
            TrySave();
            RefreshMenu();
        }

        private void SetActivation(string mode)
        {
            config.Activation = mode;
            TrySave();
            RefreshMenu();
        }

        private void TrySave()
        {
            try { config.Save(); }
            catch (Exception e)
            {
                Log.Write("Save: " + e);
                tray?.ShowBalloonTip(4000, "MagicZones", "Impossibile salvare la configurazione: " + e.Message, ToolTipIcon.Error);
            }
        }

        private void ReloadConfig()
        {
            var fresh = AppConfig.Load(out string err);
            if (err != null)
            {
                tray.ShowBalloonTip(5000, "MagicZones", "Config non valida, non ricaricata: " + err, ToolTipIcon.Warning);
                return;
            }
            // Copy into the live instance: every component holds a reference to it.
            config.Enabled = fresh.Enabled;
            config.Activation = fresh.Activation;
            config.ThrowEnabled = fresh.ThrowEnabled;
            config.ThrowMinSpeed = fresh.ThrowMinSpeed;
            config.ThrowMomentum = fresh.ThrowMomentum;
            config.Animate = fresh.Animate;
            config.AnimationMs = fresh.AnimationMs;
            config.Gap = fresh.Gap;
            config.RestoreSizeOnUnsnap = fresh.RestoreSizeOnUnsnap;
            config.Hotkeys = fresh.Hotkeys;
            config.AccentColor = fresh.AccentColor;
            config.ThrowColor = fresh.ThrowColor;
            config.ExcludedProcesses = fresh.ExcludedProcesses;
            config.Layouts = fresh.Layouts;
            RegisterHotkeys();
            RebuildMonitors();
            RefreshMenu();
            tray.ShowBalloonTip(2000, "MagicZones", "Configurazione ricaricata", ToolTipIcon.Info);
        }

        // ---- Editor -----------------------------------------------------------------------
        public void OpenEditor()
        {
            if (editor != null) return;
            overlay.HideNow();
            tracker.Stop();
            zones.Rebuild();
            editor = new EditorSession(config, zones.Monitors, saved =>
            {
                editor = null;
                if (saved) TrySave();
                RebuildMonitors();
                tracker.Start();
                if (saved) tray.ShowBalloonTip(1500, "MagicZones", "Zone salvate", ToolTipIcon.Info);
            });
        }

        // ---- Monitors -----------------------------------------------------------------------
        private void OnDisplayChanged(object sender, EventArgs e) => ui.Post(_ => { rebuildTimer.Stop(); rebuildTimer.Start(); }, null);

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Desktop || e.Category == UserPreferenceCategory.General)
                ui.Post(_ => { rebuildTimer.Stop(); rebuildTimer.Start(); }, null);
        }

        private void RebuildMonitors()
        {
            if (editor != null) return;
            zones.Rebuild();
            if (config.EnsureDefaults(zones.Monitors))
            {
                TrySave();
                zones.Rebuild();
            }
            overlay.Rebuild();
        }

        // ---- Hotkeys ------------------------------------------------------------------------
        private void RegisterHotkeys()
        {
            for (int id = HK_EDITOR; id <= HK_DOWN; id++) Native.UnregisterHotKey(messages.Handle, id);
            if (!config.Hotkeys) return;
            uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_WIN | Native.MOD_NOREPEAT;
            var failed = new System.Collections.Generic.List<string>();
            if (!Native.RegisterHotKey(messages.Handle, HK_EDITOR, mods, 0x5A)) failed.Add("Z");
            if (!Native.RegisterHotKey(messages.Handle, HK_LEFT, mods, 0x25)) failed.Add("←");
            if (!Native.RegisterHotKey(messages.Handle, HK_UP, mods, 0x26)) failed.Add("↑");
            if (!Native.RegisterHotKey(messages.Handle, HK_RIGHT, mods, 0x27)) failed.Add("→");
            if (!Native.RegisterHotKey(messages.Handle, HK_DOWN, mods, 0x28)) failed.Add("↓");
            if (failed.Count > 0) Log.Write("Hotkey già in uso: Ctrl+Alt+Win+" + string.Join(", ", failed));
        }

        internal void OnHotkey(int id)
        {
            if (id == HK_EDITOR) { OpenEditor(); return; }
            if (!config.Enabled || editor != null) return;

            var hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            hwnd = Native.GetAncestor(hwnd, Native.GA_ROOT);
            if (Native.IsIconic(hwnd)) return;

            int dx = id == HK_LEFT ? -1 : id == HK_RIGHT ? 1 : 0;
            int dy = id == HK_UP ? -1 : id == HK_DOWN ? 1 : 0;
            var rect = Native.VisibleRect(hwnd);
            var target = zones.Neighbour(rect, dx, dy);
            if (target == null) return;
            mover.Apply(hwnd, target.Kind, zones.TargetRect(target), animate: true, rect.Size);
        }

        // ---- Startup ------------------------------------------------------------------------
        private static bool IsStartupEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k?.GetValue("MagicZones") is string v && v.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private void ToggleStartup()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (IsStartupEnabled()) k.DeleteValue("MagicZones", false);
                    else k.SetValue("MagicZones", "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception e)
            {
                tray.ShowBalloonTip(4000, "MagicZones", "Impossibile modificare l'avvio automatico: " + e.Message, ToolTipIcon.Error);
            }
            RefreshMenu();
        }

        // ---- Shutdown -----------------------------------------------------------------------
        protected override void ExitThreadCore()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            for (int id = HK_EDITOR; id <= HK_DOWN; id++) Native.UnregisterHotKey(messages.Handle, id);
            editor?.Dispose();
            tracker.Dispose();
            overlay.Dispose();
            mover.Dispose();
            rebuildTimer.Dispose();
            pruneTimer.Dispose();
            messages.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            iconOn.Dispose();
            iconOff.Dispose();
            base.ExitThreadCore();
        }

        /// <summary>Hidden top-level window: hotkeys + "open editor" broadcast from a second instance.</summary>
        private sealed class MessageWindow : NativeWindow
        {
            private const int WM_HOTKEY = 0x0312;
            private readonly TrayApp app;

            public MessageWindow(TrayApp app)
            {
                this.app = app;
                CreateHandle(new CreateParams { Caption = "MagicZones.Messages" });
            }

            protected override void WndProc(ref Message m)
            {
                try
                {
                    if (m.Msg == WM_HOTKEY) { app.OnHotkey((int)m.WParam); return; }
                    if (OpenEditorMessage != 0 && m.Msg == (int)OpenEditorMessage) { app.OpenEditor(); return; }
                }
                catch (Exception e)
                {
                    Log.Write("MessageWindow: " + e);
                }
                base.WndProc(ref m);
            }
        }
    }
}
