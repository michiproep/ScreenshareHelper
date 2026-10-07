using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ScreenshareHelper.Capture;
using ScreenshareHelper.Properties;

namespace ScreenshareHelper
{
    public partial class Form1 : Form
    {
        readonly Color transKey = Color.SaddleBrown;
        private bool isActive = true;

        private IScreenCapture capture;
        private Bitmap frame;
        private readonly System.Windows.Forms.Timer captureTimer = new System.Windows.Forms.Timer();
        private Point lastCursorPos;
        private string captureError;
        private readonly CaptureStats stats = new CaptureStats();

        public Form1()
        {
            InitializeComponent();
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.TransparencyKey = transKey;
            RestoreWindowPosition();
            var configuredColor = Settings.Default.BackgroundColor;
            // TransparencyKey must stay an opaque sentinel (alpha=0 breaks GDI+ text rendering), so map the
            // user-facing 'Transparent' choice onto that same sentinel instead of a real alpha=0 color.
            this.BackColor = configuredColor == Color.Transparent ? transKey : configuredColor;
            this.labelSize.BackColor = this.BackColor;

            this.MouseDown += Form1_MouseDown;
            this.SizeChanged += Form1_SizeChanged;
        }

        #region Drag/Move the form
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private void Form1_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Cursor.Current = Cursors.Cross;
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (isActive)
                    cp.Style |= 0x40000; //WS_SIZEBOX;  
                else
                    cp.Style &= ~0x40000; //WS_SIZEBOX;  
                return cp;
            }
        }
        #endregion
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (isActive || frame == null)
            {
                base.OnPaintBackground(e);
                return;
            }

            // the window can be larger than the capture area: keep the background (usually transparent)
            // there like before, but don't clear below the frame to avoid flicker
            using var outside = new Region(ClientRectangle);
            outside.Exclude(new Rectangle(Point.Empty, frame.Size));
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillRegion(brush, outside);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!isActive)
                paint(e.Graphics);
        }

        private void Form1_SizeChanged(object sender, EventArgs e)
        {
            UpdateSizeDisplay();
        }

        private void UpdateSizeDisplay()
        {
            try
            {
                var sizeText = $"{this.Width} × {this.Height}";
                if (this.labelSize != null)
                    this.labelSize.Text = sizeText;
            }
            catch (Exception) { }
        }

        #region Cursor
        [StructLayout(LayoutKind.Sequential)]
        struct CURSORINFO
        {
            public Int32 cbSize;
            public Int32 flags;
            public IntPtr hCursor;
            public POINTAPI ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINTAPI
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll")]
        static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

        const Int32 CURSOR_SHOWING = 0x00000001;
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);
        private const int SRCCOPY = 0x00CC0020;
        #endregion Cursor


        #region Capture
        private void StartCapture()
        {
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

        private void FallBackToGdi(string failedMethod, Exception ex)
        {
            Debug.WriteLine($"Capture method {failedMethod} failed: {ex}");
            captureError = $"{failedMethod} failed ({ex.GetType().Name}: {ex.Message}) - using GDI";
            capture?.Dispose();
            capture = new GdiScreenCapture();
        }

        private void CaptureTimer_Tick(object sender, EventArgs e)
        {
            if (isActive)
                return;

            var area = new Rectangle(Settings.Default.CaptureLocation, Settings.Default.CaptureSize);
            if (area.Width <= 0 || area.Height <= 0)
                return;

            if (frame == null || frame.Size != area.Size)
            {
                frame?.Dispose();
                frame = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            }

            bool changed;
            long start = Stopwatch.GetTimestamp();
            try
            {
                changed = capture.Capture(area, frame);
            }
            catch (Exception ex)
            {
                FallBackToGdi(capture.Name, ex);
                return;
            }
            stats.Add(Stopwatch.GetElapsedTime(start), changed);

            // only repaint if the image or the mouse pointer changed
            var cursorPos = Cursor.Position;
            bool cursorMoved = Program.CopyMouse && cursorPos != lastCursorPos;
            lastCursorPos = cursorPos;
            if (changed || cursorMoved || Program.ShowStats || captureError != null)
                Invalidate();
        }

        private void paint(Graphics graphics)
        {
            try
            {
                if (frame != null)
                {
                    // draw pixel by pixel: DrawImageUnscaled would scale by the bitmap's DPI (= system DPI) vs. the window DPI
                    var rect = new Rectangle(Point.Empty, frame.Size);
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    graphics.DrawImage(frame, rect, rect, GraphicsUnit.Pixel);
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                }
                if (Program.CopyMouse)
                {
                    CopyMousePointer(graphics);
                }
                if (Program.ShowStats || captureError != null)
                    DrawStats(graphics);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void DrawStats(Graphics graphics)
        {
            var text = Program.ShowStats ? $"{capture?.Name} | {stats}" : "";
            if (captureError != null)
                text += (text.Length > 0 ? Environment.NewLine : "") + captureError;

            using var font = new Font(FontFamily.GenericMonospace, 10f);
            var size = graphics.MeasureString(text, font);
            graphics.FillRectangle(Brushes.Black, 0, 0, size.Width + 6, size.Height + 4);
            graphics.DrawString(text, font, Brushes.Yellow, 3, 2);
        }
        #endregion

        private static void CopyMousePointer(Graphics graphics)
        {
            CURSORINFO pci;
            pci.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(CURSORINFO));
            int offsetX = SystemInformation.FrameBorderSize.Width + SystemInformation.BorderSize.Width;
            int offsetY = SystemInformation.FrameBorderSize.Height + SystemInformation.BorderSize.Height;
            if (GetCursorInfo(out pci))
            {
                if (pci.flags == CURSOR_SHOWING)
                {
                    DrawIcon(graphics.GetHdc(),
                        pci.ptScreenPos.x - Settings.Default.CaptureLocation.X - offsetX,
                        pci.ptScreenPos.y - Settings.Default.CaptureLocation.Y - offsetY,
                        pci.hCursor);
                    graphics.ReleaseHdc();
                }
            }
        }

        



        #region Window Events
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private void buttonSetCaptureArea_Click(object sender, EventArgs e)
        {
            SetCaptureArea();
        }

        private void SetCaptureArea()
        {
            Settings.Default.CaptureLocation = this.Location;
            Settings.Default.CaptureSize = this.Size;

            setWindowToBackground();
        }

        private void setWindowToBackground()
        {
            SetWindowPos(this.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        private void RestoreWindowPosition()
        {
            if (Settings.Default.HasSetDefaults)
            {
                this.Location = Settings.Default.Location;
                this.Size = Settings.Default.Size;
            }
        }
        private void SaveWindowPosition()
        {
            if (this.WindowState == FormWindowState.Normal)
            {
                Settings.Default.Location = this.Location;
                Settings.Default.Size = this.Size;
            }
            else
            {
                Settings.Default.Location = this.RestoreBounds.Location;
                Settings.Default.Size = this.RestoreBounds.Size;
            }

            Settings.Default.HasSetDefaults = true;
            Settings.Default.Save();
        }
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        

        /// <summary>
        /// Adds/removes the sizing border (see CreateParams) without changing the window bounds;
        /// otherwise WinForms keeps the client size and the window no longer matches the capture area.
        /// </summary>
        private void UpdateSizeBox()
        {
            var bounds = Bounds;
            FormBorderStyle = FormBorderStyle.None; //update CreateParams
            Bounds = bounds;
        }

        private void Form1_Activated(object sender, EventArgs e)
        {
            isActive = true;
            UpdateSizeBox();
            buttonSetCaptureArea.Visible = buttonCloseApp.Visible = labelSize.Visible = isActive;
            Invalidate();
        }
        private void Form1_Deactivate(object sender, EventArgs e)
        {
            isActive = false;
            UpdateSizeBox();
            if (Program.AutoSetOnFocusLoss)
                SetCaptureArea();
            else if (Settings.Default.CaptureSize.Width > 0 && Settings.Default.CaptureSize.Height > 0)
                Size = Settings.Default.CaptureSize; // show exactly the capture area, whether the window was resized larger or smaller

            buttonSetCaptureArea.Visible = buttonCloseApp.Visible = labelSize.Visible = isActive;
            Invalidate();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveWindowPosition();
            captureTimer.Stop();
            capture?.Dispose();
            frame?.Dispose();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.BeginInvoke(new Action(() =>
            {
                IntPtr currentWindow = GetForegroundWindow(); // Get the current active window
                SetForegroundWindow(currentWindow); // Re-focus it, removing focus from our window
            }));

            StartCapture();
            UpdateSizeDisplay();
            
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            isActive = true;
            setWindowToBackground();
        }

        private void buttonCloseApp_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        #endregion
    }
}
