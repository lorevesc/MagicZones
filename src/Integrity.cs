using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MagicZones
{
    /// <summary>
    /// Windows (UIPI) won't let a process move the windows of a higher-integrity process: an app
    /// "run as administrator" (or as SYSTEM) is out of reach unless MagicZones is elevated too.
    /// </summary>
    internal static class Integrity
    {
        public const int High = 0x3000;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x0008;
        private const int TokenIntegrityLevel = 25;

        private static readonly Dictionary<uint, int> cache = new Dictionary<uint, int>();

        public static readonly int Self = LevelOf(Native.GetCurrentProcess());

        public static bool IsElevated => Self >= High && Self != int.MaxValue;

        /// <summary>
        /// Can we move this window? true / false when the target's level is known, null when the
        /// process won't even let us look (some apps lock down their process): then just try,
        /// and let SetWindowPos tell us (<see cref="WindowMover.AccessDenied"/>).
        /// </summary>
        public static bool? CanControl(IntPtr hwnd)
        {
            if (Self == int.MaxValue) return true; // couldn't read our own level: don't block anything
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!cache.TryGetValue(pid, out int level))
            {
                level = LevelOfPid(pid);
                cache[pid] = level;
            }
            if (level == int.MaxValue) return null;
            return level <= Self;
        }

        /// <summary>Processes come and go (and pids get reused): forget what we learned.</summary>
        public static void ClearCache() => cache.Clear();

        public static string ProcessName(IntPtr hwnd)
        {
            try
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                using (var p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch
            {
                return Lang.ThisApp;
            }
        }

        private static int LevelOfPid(uint pid)
        {
            var h = Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return int.MaxValue;
            try { return LevelOf(h); }
            finally { Native.CloseHandle(h); }
        }

        private static int LevelOf(IntPtr process)
        {
            if (!Native.OpenProcessToken(process, TOKEN_QUERY, out var token)) return int.MaxValue;
            try
            {
                Native.GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int len);
                if (len <= 0) return int.MaxValue;
                var buf = Marshal.AllocHGlobal(len);
                try
                {
                    if (!Native.GetTokenInformation(token, TokenIntegrityLevel, buf, len, out len)) return int.MaxValue;
                    var sid = Marshal.ReadIntPtr(buf); // TOKEN_MANDATORY_LABEL.Label.Sid
                    int count = Marshal.ReadByte(Native.GetSidSubAuthorityCount(sid));
                    return Marshal.ReadInt32(Native.GetSidSubAuthority(sid, (uint)(count - 1)));
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            finally
            {
                Native.CloseHandle(token);
            }
        }
    }

}
