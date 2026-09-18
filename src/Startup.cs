using System;
using System.IO;
using Microsoft.Win32;

namespace MagicZones
{
    /// <summary>
    /// Autostart through the per-user Run key, nothing else: no scheduled task, no child processes.
    /// MagicZones doesn't need admin rights for its job (it only moves windows of normal apps), so
    /// HKCU\...\Run is enough. The Run value is only ever written on an explicit user action, and only
    /// for a stable install location (see <see cref="IsStableLocation"/>).
    /// </summary>
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string Name = "MagicZones";

        /// <summary>Where the installer puts the app (per-user, no admin): %LOCALAPPDATA%\Programs\MagicZones.</summary>
        public static string InstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MagicZones");

        /// <summary>The exe the Run value points at (unquoted, without arguments), or null.</summary>
        public static string Target
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return ExePathOf(k?.GetValue(Name) as string);
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>Exe path of a Run value: <c>"C:\a b\x.exe" --arg</c> → <c>C:\a b\x.exe</c>.</summary>
        public static string ExePathOf(string runValue)
        {
            if (string.IsNullOrWhiteSpace(runValue)) return null;
            var v = runValue.Trim();
            if (!v.StartsWith("\"")) return v;
            int end = v.IndexOf('"', 1);
            return end > 1 ? v.Substring(1, end - 1) : v.Trim('"');
        }

        public static bool Enabled => Target != null;

        public static bool IsEnabledFor(string exe) => Target is string t && SamePath(t, exe);

        /// <summary>Register autostart for <paramref name="exe"/>. Refuses synced/temporary folders.</summary>
        public static void Enable(string exe)
        {
            if (!IsStableLocation(exe))
                throw new InvalidOperationException(Lang.StartupUnstable);
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
                k.SetValue(Name, "\"" + exe + "\"", RegistryValueKind.String);
        }

        /// <summary>Remove autostart (whatever path it points to).</summary>
        public static void Disable()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                k?.DeleteValue(Name, false);
        }

        /// <summary>
        /// Only install folders count: %LOCALAPPDATA%\Programs (per-user installs) or Program Files.
        /// Desktop, Documents, OneDrive, Downloads and Temp roam, sync or get cleaned, so an autostart
        /// entry pointing there breaks, and they're the worst places for an exe's reputation.
        /// </summary>
        public static bool IsStableLocation(string exe)
        {
            string full;
            try { full = Path.GetFullPath(exe); }
            catch { return false; }

            foreach (var bad in new[]
                     {
                         Environment.GetEnvironmentVariable("OneDrive"),
                         Environment.GetEnvironmentVariable("OneDriveConsumer"),
                         Environment.GetEnvironmentVariable("OneDriveCommercial"),
                     })
                if (IsUnder(full, bad)) return false;

            return IsUnder(full, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"))
                || IsUnder(full, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                || IsUnder(full, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        }

        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            root = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
    }
}
