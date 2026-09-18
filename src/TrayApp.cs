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

        private readonly AppConfig config;
        private readonly ZoneManager zones;
        private readonly OverlayManager overlay;
        private readonly WindowMover mover;
        private readonly PopupWindow popup;
        private readonly DragTracker tracker;
        private readonly NotifyIcon tray;
        private readonly MessageWindow messages;
        private readonly SynchronizationContext ui;
        private readonly System.Windows.Forms.Timer rebuildTimer = new System.Windows.Forms.Timer { Interval = 700 };
        private readonly System.Windows.Forms.Timer pruneTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        private EditorSession editor;
        private Icon iconOn, iconOff;

        private ToolStripMenuItem miEnabled, miAlways, miShift, miThrow, miAnimate, miStartup, miGap, miPopup, miOverlay, miLanguage;

        public TrayApp()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            bool firstRun = !File.Exists(AppConfig.FilePath);
            config = AppConfig.Load(out string loadError);
            Lang.Apply(config.Language);
            Log.Verbose = config.DebugLog;
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
            popup = new PopupWindow(config, zones);
            tracker = new DragTracker(config, zones, overlay, mover, popup);
            tracker.Blocked += OnBlocked;
            mover.AccessDenied += OnBlocked;
            tracker.Start();

            iconOn = IconArt.CreateIcon(32, true);
            iconOff = IconArt.CreateIcon(32, false);
            tray = new NotifyIcon { Icon = config.Enabled ? iconOn : iconOff, Text = "MagicZones", Visible = true };
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenEditor(); };

            messages = new MessageWindow(this);
            RegisterHotkeys();

            rebuildTimer.Tick += (s, e) => { rebuildTimer.Stop(); RebuildMonitors(); };
            pruneTimer.Tick += (s, e) => { mover.Prune(); Integrity.ClearCache(); };
            pruneTimer.Start();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            if (loadError != null)
                tray.ShowBalloonTip(6000, Lang.ConfigInvalidTitle, Lang.ConfigInvalidText(loadError), ToolTipIcon.Warning);
            else if (firstRun)
                tray.ShowBalloonTip(6000, Lang.WelcomeTitle, Lang.WelcomeText, ToolTipIcon.Info);
        }

        // ---- Menu -------------------------------------------------------------------------
        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            var title = new ToolStripMenuItem(Integrity.IsElevated ? Lang.TitleElevated : "MagicZones") { Enabled = false };
            title.Font = new Font(title.Font, FontStyle.Bold);
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());

            miEnabled = new ToolStripMenuItem(Lang.Enabled, null, (s, e) => SetEnabled(!config.Enabled)) { Checked = config.Enabled };
            menu.Items.Add(miEnabled);
            menu.Items.Add(new ToolStripMenuItem(Lang.EditZones, null, (s, e) => OpenEditor())
                { ShortcutKeyDisplayString = "Ctrl+Alt+Win+Z", Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());

            var mode = new ToolStripMenuItem(Lang.Mode);
            miPopup = new ToolStripMenuItem(Lang.ModePopup, null, (s, e) => SetMode("popup"));
            miOverlay = new ToolStripMenuItem(Lang.ModeOverlay, null, (s, e) => SetMode("overlay"));
            mode.DropDownItems.Add(miPopup);
            mode.DropDownItems.Add(miOverlay);
            menu.Items.Add(mode);

            var show = new ToolStripMenuItem(Lang.WhenDragging);
            miAlways = new ToolStripMenuItem(Lang.ActivationAlways, null, (s, e) => SetActivation("always"));
            miShift = new ToolStripMenuItem(Lang.ActivationShift, null, (s, e) => SetActivation("shift"));
            show.DropDownItems.Add(miAlways);
            show.DropDownItems.Add(miShift);
            menu.Items.Add(show);

            miThrow = new ToolStripMenuItem(Lang.FlickThrow, null, (s, e) =>
            {
                config.ThrowEnabled = !config.ThrowEnabled;
                TrySave();
                RefreshMenu();
            });
            menu.Items.Add(miThrow);

            miAnimate = new ToolStripMenuItem(Lang.Animations, null, (s, e) =>
            {
                config.Animate = !config.Animate;
                TrySave();
                RefreshMenu();
            });
            menu.Items.Add(miAnimate);

            miGap = new ToolStripMenuItem(Lang.Gap);
            foreach (int gap in new[] { 0, 4, 8, 12, 16, 24 })
            {
                int g = gap;
                miGap.DropDownItems.Add(new ToolStripMenuItem(g == 0 ? Lang.GapNone : g + " px", null, (s, e) =>
                {
                    config.Gap = g;
                    TrySave();
                    RefreshMenu();
                    overlay.Rebuild();
                }) { Tag = g });
            }
            menu.Items.Add(miGap);

            miLanguage = new ToolStripMenuItem(Lang.Language);
            foreach (string code in Lang.Codes)
            {
                string c = code;
                string text = c == "it" ? Lang.LanguageItalian : c == "en" ? Lang.LanguageEnglish : Lang.LanguageAuto;
                miLanguage.DropDownItems.Add(new ToolStripMenuItem(text, null, (s, e) => SetLanguage(c)) { Tag = c });
            }
            menu.Items.Add(miLanguage);
            menu.Items.Add(new ToolStripSeparator());

            miStartup = new ToolStripMenuItem(Lang.StartWithWindows, null, (s, e) => ToggleStartup());
            menu.Items.Add(miStartup);
            if (!Integrity.IsElevated)
                menu.Items.Add(new ToolStripMenuItem(Lang.RestartElevated, null, (s, e) => RestartElevated())
                    { ToolTipText = Lang.RestartElevatedTip });
            menu.Items.Add(new ToolStripMenuItem(Lang.Restart, null, (s, e) => Restart())
                { ToolTipText = Lang.RestartTip });
            menu.Items.Add(new ToolStripMenuItem(Lang.OpenConfigFolder, null, (s, e) =>
            {
                Directory.CreateDirectory(AppConfig.Folder);
                // Let the shell open the folder (no explicit explorer.exe child process).
                Process.Start(new ProcessStartInfo(AppConfig.Folder) { UseShellExecute = true });
            }));
            menu.Items.Add(new ToolStripMenuItem(Lang.ReloadConfig, null, (s, e) => ReloadConfig()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(Lang.Exit, null, (s, e) => ExitThread()));

            menu.Opening += (s, e) => RefreshMenu();
            RefreshMenu();
            return menu;
        }

        private void RefreshMenu()
        {
            miEnabled.Checked = config.Enabled;
            miPopup.Checked = config.Mode != "overlay";
            miOverlay.Checked = config.Mode == "overlay";
            miThrow.Enabled = config.Mode == "overlay";
            miAlways.Checked = config.Activation != "shift";
            miShift.Checked = config.Activation == "shift";
            miThrow.Checked = config.ThrowEnabled;
            miAnimate.Checked = config.Animate;
            miStartup.Checked = Startup.IsEnabledFor(Application.ExecutablePath);
            foreach (ToolStripMenuItem item in miGap.DropDownItems) item.Checked = (int)item.Tag == config.Gap;
            foreach (ToolStripMenuItem item in miLanguage.DropDownItems) item.Checked = (string)item.Tag == config.Language;
            tray.Icon = config.Enabled ? iconOn : iconOff;
            tray.Text = config.Enabled ? Lang.TrayOn : Lang.TrayOff;
        }

        private void SetLanguage(string code)
        {
            config.Language = Lang.Normalize(code);
            TrySave();
            ApplyLanguage();
        }

        /// <summary>Menu texts are fixed at build time: rebuild it. Everything else reads Lang when drawing.</summary>
        private void ApplyLanguage()
        {
            Lang.Apply(config.Language);
            var old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = BuildMenu();
            // We may be inside a click handler of the old menu: dispose it once that has unwound.
            ui.Post(_ => old?.Dispose(), null);
        }

        private void SetEnabled(bool on)
        {
            config.Enabled = on;
            if (!on) overlay.HideNow();
            TrySave();
            RefreshMenu();
        }

        private void SetMode(string mode)
        {
            config.Mode = mode;
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
                tray?.ShowBalloonTip(4000, "MagicZones", Lang.SaveFailed(e.Message), ToolTipIcon.Error);
            }
        }

        private void ReloadConfig()
        {
            var fresh = AppConfig.Load(out string err);
            if (err != null)
            {
                tray.ShowBalloonTip(5000, "MagicZones", Lang.ReloadFailed(err), ToolTipIcon.Warning);
                return;
            }
            // Copy into the live instance: every component holds a reference to it.
            config.Enabled = fresh.Enabled;
            bool languageChanged = config.Language != fresh.Language;
            config.Language = fresh.Language;
            config.Mode = fresh.Mode;
            config.PopupWidth = fresh.PopupWidth;
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
            config.DebugLog = fresh.DebugLog;
            Log.Verbose = fresh.DebugLog;
            config.ExcludedProcesses = fresh.ExcludedProcesses;
            config.Layouts = fresh.Layouts;
            RegisterHotkeys();
            RebuildMonitors();
            if (languageChanged) ApplyLanguage();
            RefreshMenu();
            tray.ShowBalloonTip(2000, "MagicZones", Lang.Reloaded, ToolTipIcon.Info);
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
                if (saved) tray.ShowBalloonTip(1500, "MagicZones", Lang.ZonesSaved, ToolTipIcon.Info);
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
            if (Integrity.CanControl(hwnd) == false) { OnBlocked(hwnd); return; }
            mover.Apply(hwnd, target.Kind, zones.TargetRect(target), animate: true, rect.Size);
        }

        // ---- Admin windows ----------------------------------------------------------------------
        private readonly System.Collections.Generic.Dictionary<string, DateTime> blockedNotified =
            new System.Collections.Generic.Dictionary<string, DateTime>();

        private void OnBlocked(IntPtr hwnd)
        {
            string app = Integrity.ProcessName(hwnd);
            if (blockedNotified.TryGetValue(app, out var last) && (DateTime.UtcNow - last).TotalSeconds < 20) return;
            blockedNotified[app] = DateTime.UtcNow;
            if (Integrity.IsElevated)
                tray.ShowBalloonTip(6000, Lang.BlockedSystemTitle(app), Lang.BlockedSystemText(app), ToolTipIcon.Warning);
            else
                tray.ShowBalloonTip(8000, Lang.BlockedAdminTitle(app), Lang.BlockedAdminText, ToolTipIcon.Warning);
        }

        /// <summary>Relaunch with the same privileges (the child inherits our token, no UAC).</summary>
        private void Restart()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--replace " + Process.GetCurrentProcess().Id)
                    { UseShellExecute = false });
            }
            catch (Exception e)
            {
                tray.ShowBalloonTip(4000, "MagicZones", Lang.RestartFailed(e.Message), ToolTipIcon.Error);
                return;
            }
            ExitThread();
        }

        private void RestartElevated()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--replace " + Process.GetCurrentProcess().Id)
                {
                    Verb = "runas",
                    UseShellExecute = true,
                };
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return; // UAC prompt cancelled: keep running as we are
            }
            ExitThread();
        }

        // ---- Startup ------------------------------------------------------------------------
        /// <summary>
        /// Toggle the HKCU Run value. Only from an installed copy: the app never copies itself around
        /// or writes autostart for a Desktop/OneDrive/Downloads path; installing is the setup's job.
        /// </summary>
        private void ToggleStartup()
        {
            string exe = Application.ExecutablePath;
            try
            {
                if (Startup.IsEnabledFor(exe))
                {
                    Startup.Disable();
                }
                else if (Startup.IsStableLocation(exe))
                {
                    Startup.Enable(exe); // also replaces a stale entry pointing at an old copy
                }
                else
                {
                    string stale = Startup.Target;
                    string text = Lang.StartupNotInstalled(Path.GetDirectoryName(exe), Startup.InstallDir);
                    if (stale != null)
                    {
                        text += Lang.StartupStale(stale);
                        if (MessageBox.Show(text, "MagicZones", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            Startup.Disable();
                    }
                    else
                    {
                        MessageBox.Show(text, "MagicZones", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception e)
            {
                tray.ShowBalloonTip(5000, "MagicZones", Lang.StartupFailed(e.Message), ToolTipIcon.Error);
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
            popup.Dispose();
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
                // When elevated, still accept "open editor" from a normal second instance (UIPI filters it otherwise).
                if (OpenEditorMessage != 0) Native.ChangeWindowMessageFilterEx(Handle, OpenEditorMessage, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);
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
