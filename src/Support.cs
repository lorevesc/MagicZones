using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MagicZones
{
    internal static class Log
    {
        private static readonly object gate = new object();

        public static string FilePath => Path.Combine(AppConfig.Folder, "log.txt");

        /// <summary>Drag lifecycle tracing ("debugLog": true in config.json).</summary>
        public static bool Verbose;

        public static void Debug(string msg)
        {
            if (Verbose) Write("[dbg] " + msg);
        }

        public static void Write(string msg)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(AppConfig.Folder);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }

    /// <summary>The app icon, drawn in code: three zones, one lit, with a "throw" streak.</summary>
    internal static class IconArt
    {
        public static Bitmap Render(int size, bool enabled = true)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float u = size / 32f;
                var bgRect = new RectangleF(1 * u, 1 * u, 30 * u, 30 * u);
                using (var bgPath = Geometry.RoundedRect(bgRect, 7 * u))
                using (var bg = new LinearGradientBrush(bgRect, Color.FromArgb(255, 30, 36, 52), Color.FromArgb(255, 14, 17, 26), 90f))
                    g.FillPath(bg, bgPath);

                var accent = enabled ? Color.FromArgb(255, 59, 130, 246) : Color.FromArgb(255, 110, 116, 128);
                var hot = enabled ? Color.FromArgb(255, 249, 115, 22) : Color.FromArgb(255, 150, 150, 150);
                var zonesR = new[]
                {
                    new RectangleF(5 * u, 6 * u, 7 * u, 20 * u),
                    new RectangleF(13 * u, 6 * u, 7 * u, 20 * u),
                    new RectangleF(21 * u, 6 * u, 6 * u, 20 * u),
                };
                for (int i = 0; i < zonesR.Length; i++)
                {
                    using (var p = Geometry.RoundedRect(zonesR[i], 1.8f * u))
                    using (var b = new SolidBrush(i == 2 ? hot : Color.FromArgb(i == 0 ? 170 : 110, accent)))
                        g.FillPath(b, p);
                }
                if (size >= 24)
                {
                    // Motion streaks towards the lit zone.
                    using (var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 1.6f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(pen, 7 * u, 12 * u, 17 * u, 12 * u);
                        g.DrawLine(pen, 9 * u, 16 * u, 18.5f * u, 16 * u);
                        g.DrawLine(pen, 7 * u, 20 * u, 17 * u, 20 * u);
                    }
                }
            }
            return bmp;
        }

        public static Icon CreateIcon(int size, bool enabled)
        {
            using (var bmp = Render(size, enabled))
            {
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        /// <summary>Writes a multi-size PNG-compressed .ico (used for the .exe icon at build time).</summary>
        public static void ExportIco(string path)
        {
            var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int sz in sizes)
                using (var bmp = Render(sz))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }
    }
}
