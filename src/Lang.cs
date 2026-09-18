using System.Globalization;

namespace MagicZones
{
    /// <summary>
    /// UI strings in Italian and English. "auto" follows the Windows display language:
    /// Italian when it is Italian, English otherwise. Strings are read at draw time, so a
    /// switch applies to the next popup/overlay/editor without restarting.
    /// </summary>
    internal static class Lang
    {
        public static readonly string[] Codes = { "auto", "it", "en" };

        public static bool Italian { get; private set; } = Detect();

        /// <summary>Windows display language (not the regional format): <c>it</c> → Italian.</summary>
        public static bool Detect() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";

        /// <summary>Normalize a config value: "it", "en" or "auto".</summary>
        public static string Normalize(string code)
        {
            code = (code ?? "").Trim().ToLowerInvariant();
            return code == "it" || code == "en" ? code : "auto";
        }

        public static void Apply(string code)
        {
            code = Normalize(code);
            Italian = code == "auto" ? Detect() : code == "it";
        }

        /// <summary>Pick the string for the current language (for one-off messages not listed here).</summary>
        public static string T(string it, string en) => Italian ? it : en;

        // ---- Tray menu ---------------------------------------------------------------------
        public static string TitleElevated => T("MagicZones (amministratore)", "MagicZones (administrator)");
        public static string Enabled => T("Attivo", "Enabled");
        public static string EditZones => T("Modifica zone…", "Edit zones…");
        public static string Mode => T("Modalità", "Mode");
        public static string ModePopup => T("Popup sopra la finestra (mini-mappa)", "Popup above the window (mini-map)");
        public static string ModeOverlay => T("Zone a tutto schermo", "Full-screen zones");
        public static string WhenDragging => T("Quando trascini", "When dragging");
        public static string ActivationAlways => T("Sempre (tieni Shift per ignorare)", "Always (hold Shift to skip)");
        public static string ActivationShift => T("Solo tenendo premuto Shift", "Only while holding Shift");
        public static string FlickThrow => T("Lancio a scatto (solo tutto schermo)", "Flick throw (full-screen only)");
        public static string Animations => T("Animazioni", "Animations");
        public static string Gap => T("Spaziatura tra finestre", "Gap between windows");
        public static string GapNone => T("Nessuna", "None");
        public static string StartWithWindows => T("Avvia con Windows", "Start with Windows");
        public static string RestartElevated => T("Riavvia come amministratore", "Restart as administrator");
        public static string RestartElevatedTip => T("Serve per spostare le finestre delle app avviate come amministratore",
            "Needed to move windows of apps running as administrator");
        public static string Restart => T("Riavvia MagicZones", "Restart MagicZones");
        public static string RestartTip => T("Carica la versione appena compilata, con gli stessi privilegi",
            "Loads the freshly built version, with the same privileges");
        public static string OpenConfigFolder => T("Apri cartella configurazione", "Open config folder");
        public static string ReloadConfig => T("Ricarica configurazione", "Reload config");
        /// <summary>Bilingual on purpose: findable whatever language is showing.</summary>
        public const string Language = "Lingua / Language";
        public static string LanguageAuto => T("Automatica (lingua di Windows)", "Automatic (Windows language)");
        public const string LanguageItalian = "Italiano";
        public const string LanguageEnglish = "English";
        public static string Exit => T("Esci", "Exit");
        public static string TrayOn => T("MagicZones — attivo", "MagicZones — enabled");
        public static string TrayOff => T("MagicZones — in pausa", "MagicZones — paused");

        // ---- Tray balloons -----------------------------------------------------------------
        public static string ConfigInvalidTitle => T("MagicZones: config non valida", "MagicZones: invalid config");
        public static string ConfigInvalidText(string error) => T("Uso le impostazioni di default. Errore: ", "Using default settings. Error: ") + error;
        public static string WelcomeTitle => T("MagicZones è attivo", "MagicZones is running");
        public static string WelcomeText => T(
            "Trascina una finestra: sopra appare la mini-mappa. Rilascia su una zona e la finestra ci vola, " +
            "anche sull'altro monitor. Doppio clic sull'icona per disegnare le tue zone.",
            "Drag a window: a mini-map appears above it. Release on a zone and the window flies there, " +
            "even to another monitor. Double-click the icon to draw your own zones.");
        public static string SaveFailed(string error) => T("Impossibile salvare la configurazione: ", "Could not save the configuration: ") + error;
        public static string ReloadFailed(string error) => T("Config non valida, non ricaricata: ", "Invalid config, not reloaded: ") + error;
        public static string Reloaded => T("Configurazione ricaricata", "Configuration reloaded");
        public static string ZonesSaved => T("Zone salvate", "Zones saved");
        public static string RestartFailed(string error) => T("Riavvio non riuscito: ", "Restart failed: ") + error;
        public static string StartupFailed(string error) => T("Impossibile modificare l'avvio automatico: ", "Could not change autostart: ") + error;

        public static string BlockedSystemTitle(string app) => T("MagicZones non può spostare ", "MagicZones can't move ") + app;
        public static string BlockedSystemText(string app) => app + T(
            " gira con privilegi di sistema: Windows non permette a nessun'altra app di spostare le sue finestre.",
            " runs with system privileges: Windows doesn't let any other app move its windows.");
        public static string BlockedAdminTitle(string app) => app + T(" è avviato come amministratore", " is running as administrator");
        public static string BlockedAdminText => T(
            "Windows non lascia spostare le sue finestre a un'app normale. Clic destro sull'icona di MagicZones → \"Riavvia come amministratore\".",
            "Windows doesn't let a normal app move its windows. Right-click the MagicZones icon → \"Restart as administrator\".");

        public static string StartupNotInstalled(string runningFrom, string installDir) => T(
            "L'avvio automatico si attiva solo dalla versione installata.\n\n" +
            "Questa copia gira da:\n" + runningFrom + "\n\n" +
            "Installa MagicZones con MagicZones-Setup.exe (va in " + installDir + ", senza diritti di amministratore) " +
            "e spunta \"Avvia con Windows\" durante l'installazione o dal menu della copia installata.",
            "Autostart can only be enabled from the installed version.\n\n" +
            "This copy is running from:\n" + runningFrom + "\n\n" +
            "Install MagicZones with MagicZones-Setup.exe (it goes into " + installDir + ", no administrator rights needed) " +
            "and tick \"Start with Windows\" during setup or from the installed copy's menu.");
        public static string StartupStale(string target) => T(
            "\n\nC'è già un avvio automatico che punta a:\n" + target + "\n\nRimuoverlo?",
            "\n\nThere is already an autostart entry pointing to:\n" + target + "\n\nRemove it?");
        public static string StartupUnstable => T(
            "l'avvio automatico si attiva solo da una cartella di installazione stabile",
            "autostart can only be enabled from a stable install folder");
        public static string ThisApp => T("questa app", "this app");

        // ---- Zones -------------------------------------------------------------------------
        public static string KindSnap => "SNAP";
        public static string KindMaximize => T("MASSIMIZZA", "MAXIMIZE");
        public static string KindMinimize => T("MINIMIZZA", "MINIMIZE");

        // ---- Popup / overlay ---------------------------------------------------------------
        public static string PopupThrowTo => T("Lancia su…", "Throw to…");
        public static string PopupMerge(int n) => T($"Unisci {n} zone", $"Merge {n} zones");
        public static string PopupHint => T("Ctrl unisci · Shift chiudi", "Ctrl merge · Shift close");
        public static string PopupLockedSystem(string app) => app + T(": Windows non la lascia spostare", ": Windows won't let it be moved");
        public static string PopupLockedAdmin(string app) => app + T(": serve MagicZones da amministratore", ": needs MagicZones as administrator");
        public static string OverlayMerged(int n) => T($"{n} zone unite", $"{n} zones merged");
        public static string OverlayThrow => T("LANCIO", "THROW");

        // ---- Editor ------------------------------------------------------------------------
        public static string Columns(int n) => T($"{n} colonne", $"{n} columns");
        public static string Rows(int n) => T($"{n} righe", $"{n} rows");
        public static string Clear => T("Svuota", "Clear");
        public static string Undo => T("↶ Annulla", "↶ Undo");
        public static string Save => T("✓ Salva", "✓ Save");
        public static string Close => T("✕ Esci", "✕ Exit");
        public static string NothingToUndo => T("Niente da annullare", "Nothing to undo");
        public static string ZoneKindToast(int number, string kind) => T("Zona ", "Zone ") + number + ": " + kind;
        public static string UnsavedChanges => T(
            "Modifiche non salvate — Esc di nuovo per uscire senza salvare, Invio per salvare",
            "Unsaved changes — Esc again to exit without saving, Enter to save");
        public static string ShowToolbar => T("H: mostra barra", "H: show toolbar");
        public static string EditorHint => T(
            "Trascina sul vuoto: nuova zona  ·  Bordi: ridimensiona  ·  Doppio clic / T: tipo  ·  Tasto dx / Canc: elimina  ·  Shift: niente magnete  ·  H: nascondi barra  ·  Invio: salva",
            "Drag on empty space: new zone  ·  Edges: resize  ·  Double-click / T: type  ·  Right-click / Del: delete  ·  Shift: no magnet  ·  H: hide toolbar  ·  Enter: save");
        public static string PrimaryMonitor => T(" · principale", " · primary");
    }
}
