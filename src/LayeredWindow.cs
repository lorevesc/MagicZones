using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MagicZones
{
    /// <summary>
    /// Borderless per-pixel-alpha window (UpdateLayeredWindow). Everything is in physical pixels,
    /// so it behaves on mixed-DPI setups. Drawing goes straight into a DIB section, no copies.
    /// </summary>
    internal class LayeredWindow : NativeWindow, IDisposable
    {
        private IntPtr memDc, dib, oldBitmap;
        private Bitmap surface;
        private byte alpha = 255;
        private bool disposed;

        public Rectangle Bounds { get; private set; }
        public bool Visible { get; private set; }

        protected LayeredWindow(Rectangle bounds, bool clickThrough, bool topmost)
        {
            Bounds = bounds;
            int ex = Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
            if (topmost) ex |= Native.WS_EX_TOPMOST;
            if (clickThrough) ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;

            var cp = new CreateParams
            {
                Caption = "MagicZones",
                ClassStyle = 0x0008, // CS_DBLCLKS
                Style = Native.WS_POPUP,
                ExStyle = ex,
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
            };
            CreateHandle(cp);
            AllocateSurface();
        }

        private void AllocateSurface()
        {
            var screenDc = Native.GetDC(IntPtr.Zero);
            try
            {
                memDc = Native.CreateCompatibleDC(screenDc);
                var bmi = new Native.BITMAPINFOHEADER
                {
                    biSize = 40,
                    biWidth = Bounds.Width,
                    biHeight = -Bounds.Height, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                };
                dib = Native.CreateDIBSection(screenDc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new OutOfMemoryException("CreateDIBSection fallita");
                oldBitmap = Native.SelectObject(memDc, dib);
                surface = new Bitmap(Bounds.Width, Bounds.Height, Bounds.Width * 4, PixelFormat.Format32bppPArgb, bits);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>Redraw the whole surface and push it to the screen.</summary>
        protected void Render(Action<Graphics> draw)
        {
            if (disposed) return;
            using (var g = Graphics.FromImage(surface))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                draw(g);
                g.Flush(FlushIntention.Sync);
            }
            Push();
        }

        private void Push()
        {
            var screenDc = Native.GetDC(IntPtr.Zero);
            try
            {
                var dst = new Native.POINT(Bounds.X, Bounds.Y);
                var size = new Native.SIZE(Bounds.Width, Bounds.Height);
                var src = new Native.POINT(0, 0);
                var blend = Blend();
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private Native.BLENDFUNCTION Blend() => new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER,
            SourceConstantAlpha = alpha,
            AlphaFormat = Native.AC_SRC_ALPHA,
        };

        /// <summary>Global opacity, cheap (no redraw) — used for fades.</summary>
        public byte Opacity
        {
            get => alpha;
            set
            {
                if (alpha == value || disposed) return;
                alpha = value;
                var blend = Blend();
                Native.UpdateLayeredWindowAlpha(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, Native.ULW_ALPHA);
            }
        }

        public void Show(bool activate = false)
        {
            if (disposed) return;
            Native.ShowWindow(Handle, activate ? 5 /* SW_SHOW */ : Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | (activate ? 0 : Native.SWP_NOACTIVATE));
            if (activate) Native.SetForegroundWindow(Handle);
            Visible = true;
        }

        public void Hide()
        {
            if (disposed) return;
            Native.ShowWindow(Handle, Native.SW_HIDE);
            Visible = false;
        }

        /// <summary>Debug/preview: current surface composited over a neutral backdrop, as PNG.</summary>
        public void SaveSnapshot(string path)
        {
            using (var bmp = new Bitmap(Bounds.Width, Bounds.Height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, Bounds.Width, Bounds.Height),
                           Color.FromArgb(255, 72, 88, 110), Color.FromArgb(255, 120, 96, 130), 35f))
                    g.FillRectangle(bg, 0, 0, Bounds.Width, Bounds.Height);
                g.DrawImageUnscaled(surface, 0, 0);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        public virtual void Dispose()
        {
            if (disposed) return;
            disposed = true;
            surface?.Dispose();
            if (memDc != IntPtr.Zero)
            {
                Native.SelectObject(memDc, oldBitmap);
                Native.DeleteDC(memDc);
            }
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            DestroyHandle();
        }

        // ---- Shared drawing helpers ----------------------------------------------------
        protected static void DrawCenteredText(Graphics g, string text, Font font, Color color, RectangleF area)
        {
            using (var brush = new SolidBrush(color))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(text, font, brush, area, sf);
        }
    }
}
