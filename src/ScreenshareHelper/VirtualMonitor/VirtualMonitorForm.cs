using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ScreenshareHelper.Capture;

namespace ScreenshareHelper.VirtualMonitor
{
    /// <summary>
    /// Borderless full-screen window on a virtual monitor that shows the virtual monitor capture area
    /// (scaled to fit, black bars). Share that monitor in Teams/Zoom. Independent of the main window's area.
    /// </summary>
    public sealed class VirtualMonitorForm : Form
    {
        private ParsecVirtualDisplay display;
        private Rectangle area;
        private Size? resolution;
        private IScreenCapture capture;
        private Bitmap frame;
        private readonly Timer captureTimer = new Timer();
        private Point lastCursorPos;
        private string error;

        public const string AutoResolution = "Auto";

        /// <summary>Choices for the resolution drop-down; Teams shares screens with up to 1080p, so that is the default.</summary>
        public static readonly string[] ResolutionChoices = { "1280x720", "1920x1080", "2560x1440", "3840x2160", AutoResolution };

        /// <summary>"1920x1080" -> 1920×1080, "Auto" (or anything else) -> null = pick the mode that fits the area.</summary>
        public static Size? ParseResolution(string text)
        {
            var parts = (text ?? "").Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0
                ? new Size(w, h)
                : null;
        }

        /// <summary>Asks to install the driver if it is missing. Returns false if the virtual monitor can't be used.</summary>
        public static bool EnsureDriverInteractive()
        {
            if (ParsecVirtualDisplay.IsDriverInstalled())
                return true;

            var text = "The virtual monitor needs the free Parsec Virtual Display Driver (signed by Parsec, installed once with administrator rights).\n\n" +
                "Download the installer now? Run it, then click the virtual monitor button again.";
            if (MessageBox.Show(text, "ScreenshareHelper", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(ParsecVirtualDisplay.DriverDownloadUrl) { UseShellExecute = true });
            return false;
        }

        public VirtualMonitorForm(Rectangle area, Size? resolution)
        {
            this.area = area;
            this.resolution = resolution;
            Text = "ScreenshareHelper virtual monitor";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            DoubleBuffered = true;

            display = new ParsecVirtualDisplay();
            display.Configure(area.Size, resolution);
            Bounds = display.Bounds;

            try
            {
                capture = ScreenCaptureFactory.Create(Program.CaptureMethod);
            }
            catch (Exception ex)
            {
                FallBackToGdi(Program.CaptureMethod.ToString(), ex);
            }
            captureTimer.Interval = Math.Max(1, 1000 / Program.Fps);
            captureTimer.Tick += CaptureTimer_Tick;
            captureTimer.Start();
        }

        /// <summary>Shows another area and/or switches the resolution (with Auto also when the area size changed).</summary>
        public void SetArea(Rectangle newArea, Size? newResolution)
        {
            bool reconfigure = newResolution != resolution || (!newResolution.HasValue && newArea.Size != area.Size);
            area = newArea;
            resolution = newResolution;
            if (reconfigure && display != null)
            {
                display.Configure(area.Size, resolution);
                Bounds = display.Bounds;
            }
            Invalidate();
        }

        // don't take the focus from the main window or the presenter's app
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: not in Alt+Tab
                return cp;
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            // WinForms suggests a DPI-scaled size, but the window must cover the monitor exactly
            if (display != null)
                Bounds = display.Bounds;
        }

        private void FallBackToGdi(string failedMethod, Exception ex)
        {
            Debug.WriteLine($"Capture method {failedMethod} failed: {ex}");
            error = $"{failedMethod} failed ({ex.GetType().Name}: {ex.Message}) - using GDI";
            capture?.Dispose();
            capture = new GdiScreenCapture();
        }

        private void CaptureTimer_Tick(object sender, EventArgs e)
        {
            if (capture == null || area.Width <= 0 || area.Height <= 0)
                return;

            // Windows can move the monitor (resolution or layout change): keep covering it
            var bounds = display.Bounds;
            if (!bounds.IsEmpty && bounds != Bounds)
                Bounds = bounds;

            if (frame == null || frame.Size != area.Size)
            {
                frame?.Dispose();
                frame = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            }

            bool changed;
            try
            {
                changed = capture.Capture(area, frame);
            }
            catch (Exception ex)
            {
                FallBackToGdi(capture.Name, ex);
                return;
            }

            var cursorPos = Cursor.Position;
            bool cursorMoved = Program.CopyMouse && cursorPos != lastCursorPos;
            lastCursorPos = cursorPos;
            if (changed || cursorMoved || error != null)
                Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (frame == null)
                return;
            try
            {
                var g = e.Graphics;
                var dest = FitRectangle(frame.Size, ClientSize);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = dest.Size == frame.Size ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(frame, dest, new Rectangle(Point.Empty, frame.Size), GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;

                if (Program.CopyMouse)
                    DrawMousePointer(g, dest);
                if (error != null)
                {
                    using var font = new Font(FontFamily.GenericMonospace, 10f);
                    g.DrawString(error, font, Brushes.Yellow, 3, 2);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static Rectangle FitRectangle(Size content, Size target)
        {
            double scale = Math.Min((double)target.Width / content.Width, (double)target.Height / content.Height);
            int w = (int)Math.Round(content.Width * scale), h = (int)Math.Round(content.Height * scale);
            return new Rectangle((target.Width - w) / 2, (target.Height - h) / 2, w, h);
        }

        private void DrawMousePointer(Graphics g, Rectangle dest)
        {
            var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref info) || info.flags != CURSOR_SHOWING || !area.Contains(info.ptScreenPos.x, info.ptScreenPos.y))
                return;

            int hotX = 0, hotY = 0;
            if (GetIconInfo(info.hCursor, out var icon))
            {
                hotX = icon.xHotspot;
                hotY = icon.yHotspot;
                if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
                if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
            }

            // pointer position scaled like the image, the pointer itself keeps its normal size
            double scale = (double)dest.Width / area.Width;
            int x = dest.X + (int)Math.Round((info.ptScreenPos.x - area.X) * scale) - hotX;
            int y = dest.Y + (int)Math.Round((info.ptScreenPos.y - area.Y) * scale) - hotY;
            var hdc = g.GetHdc();
            try
            {
                DrawIcon(hdc, x, y, info.hCursor);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            captureTimer.Stop();
            base.OnFormClosing(e);
        }

        /// <summary>
        /// Removes the monitor and releases the capture. Not done while closing: WinRT capture can't be closed
        /// during an input-synchronous call (see Form1.ReleaseCaptureResources).
        /// </summary>
        public void ReleaseResources()
        {
            captureTimer.Stop();
            try
            {
                capture?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            capture = null;
            display?.Dispose();
            display = null;
            frame?.Dispose();
            frame = null;
        }

        #region Win32
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        private const int CURSOR_SHOWING = 0x1;

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO iconInfo);

        [DllImport("user32.dll")]
        private static extern bool DrawIcon(IntPtr hDC, int x, int y, IntPtr hIcon);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        #endregion
    }
}
