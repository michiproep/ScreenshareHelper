using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScreenshareHelper.Capture;
using ScreenshareHelper.Properties;
using ScreenshareHelper.VirtualMonitor;

namespace ScreenshareHelper
{
    public partial class Form1 : Form
    {
        readonly Color transKey = Color.SaddleBrown;
        private bool isActive = true;

        private AppMode mode;
        private TrayIcon tray;
        private ResilientCapture capture;
        private Bitmap frame;
        private readonly System.Windows.Forms.Timer captureTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer virtualMonitorWatchdog = new System.Windows.Forms.Timer();
        private Point lastCursorPos;
        private string shownError;
        private VirtualCameraOutput virtualCamera;
        private VirtualMonitorForm virtualMonitor;
        private VirtualMonitorForm closingVirtualMonitor;
        private string cameraError;
        private bool closing;
        private bool exiting;
        private bool busy; // installer running
        private IntPtr windowBeforeSetArea;

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

            comboVirtualMonitorResolution.Items.AddRange(VirtualMonitorForm.ResolutionChoices);
            comboVirtualMonitorResolution.SelectedItem = Settings.Default.VirtualMonitorResolution;
            if (comboVirtualMonitorResolution.SelectedIndex < 0)
                comboVirtualMonitorResolution.SelectedItem = "1920x1080";
            this.SizeChanged += Form1_SizeChanged;

            // camera / virtual monitor mode: no window at start (it is shown invisibly once, then hidden in OnShown)
            if (!Enum.TryParse(Settings.Default.Mode, out mode))
                mode = AppMode.Normal;
            if (mode != AppMode.Normal)
                Opacity = 0;
        }

        public AppMode Mode => mode;

        /// <summary>The window shows the mirrored capture area (normal mode, inactive); otherwise it shows the setup buttons.</summary>
        private bool Mirroring => mode == AppMode.Normal && !isActive;

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
                if (!Mirroring)
                    cp.Style |= 0x40000; //WS_SIZEBOX;
                else
                    cp.Style &= ~0x40000; //WS_SIZEBOX;
                return cp;
            }
        }
        #endregion
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!Mirroring || frame == null)
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
            if (Mirroring)
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
        #endregion Cursor


        #region Capture
        private void StartCapture()
        {
            capture = new ResilientCapture();
            captureTimer.Interval = 1000 / Program.Fps;
            captureTimer.Tick += CaptureTimer_Tick;
            captureTimer.Start();
        }

        private void CaptureTimer_Tick(object sender, EventArgs e)
        {
            // the window only needs frames while it mirrors, the virtual camera always
            if (!(Mirroring || virtualCamera != null) || closing || capture == null)
                return;

            var area = new Rectangle(Settings.Default.CaptureLocation, Settings.Default.CaptureSize);
            if (area.Width <= 0 || area.Height <= 0)
                return;

            if (frame == null || frame.Size != area.Size)
            {
                frame?.Dispose();
                frame = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            }

            bool changed = capture.Capture(area, frame);
            var error = capture.Error;
            bool errorChanged = error != shownError;
            shownError = error;

            // only repaint if the image or the mouse pointer changed
            var cursorPos = Cursor.Position;
            bool cursorMoved = Program.CopyMouse && cursorPos != lastCursorPos;
            lastCursorPos = cursorPos;

            if (virtualCamera != null && (changed || cursorMoved || !virtualCamera.Connected))
            {
                try
                {
                    virtualCamera.Write(frame, area, Program.CopyMouse);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    cameraError = $"Virtual camera: {ex.Message}";
                }
            }

            if (Mirroring && (changed || cursorMoved || errorChanged))
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
                if (shownError != null)
                    DrawError(graphics, shownError);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static void DrawError(Graphics graphics, string text)
        {
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

        #region Modes
        /// <summary>Switches between the normal window, the virtual camera and the virtual monitor.</summary>
        public void ApplyMode(AppMode newMode)
        {
            if (busy)
                return;
            if (mode == AppMode.Normal && newMode != AppMode.Normal && Visible)
                SaveWindowPosition();

            if (newMode != AppMode.Camera)
                StopCamera();
            if (newMode != AppMode.VirtualMonitor)
                CloseVirtualMonitor();

            if (newMode == AppMode.Camera && !StartCamera())
                newMode = AppMode.Normal;
            if (newMode == AppMode.VirtualMonitor && !ParsecDriverSetup.IsInstalled)
            {
                if (VirtualMonitorForm.AskToInstallDriver())
                    _ = InstallDriverAsync(switchToVirtualMonitor: true);
                newMode = AppMode.Normal;
            }

            mode = newMode;
            Settings.Default.Mode = mode.ToString();
            Settings.Default.Save();
            // the setup window must not show up in the camera or on the virtual monitor; in normal mode the window itself is shared
            SetWindowDisplayAffinity(Handle, mode == AppMode.Normal ? WDA_NONE : WDA_EXCLUDEFROMCAPTURE);
            TopMost = mode != AppMode.Normal;

            if (mode == AppMode.Normal)
            {
                RestoreWindowPosition();
                if (Settings.Default.CaptureSize.Width > 0 && Settings.Default.CaptureSize.Height > 0 && !isActive)
                    Size = Settings.Default.CaptureSize;
                UpdateSizeBox();
                Show();
            }
            else
            {
                Hide();
                if (mode == AppMode.VirtualMonitor && !ShowVirtualMonitor())
                    BeginSetArea();
            }
            UpdateControlsVisibility();
            Invalidate();
        }

        /// <summary>Shows the window to place it over the area to share (normal mode: just brings it to the front).</summary>
        public void BeginSetArea()
        {
            if (mode != AppMode.Normal)
            {
                var area = mode == AppMode.Camera
                    ? new Rectangle(Settings.Default.CaptureLocation, Settings.Default.CaptureSize)
                    : new Rectangle(Settings.Default.VirtualMonitorLocation, Settings.Default.VirtualMonitorSize);
                if (area.Width < 100 || area.Height < 100)
                    area = new Rectangle(Settings.Default.Location, Settings.Default.Size);
                if (area.Width < 100 || area.Height < 100)
                    area = new Rectangle(100, 100, 800, 450);
                UpdateSizeBox();
                Bounds = area;
                if (!Visible)
                    windowBeforeSetArea = GetForegroundWindow();
            }
            Show();
            Activate();
            UpdateControlsVisibility();
        }

        private void EndSetArea()
        {
            Hide();
            // otherwise Windows activates the next window in line, the (click-through) red frame
            if (windowBeforeSetArea != IntPtr.Zero)
                SetForegroundWindow(windowBeforeSetArea);
            windowBeforeSetArea = IntPtr.Zero;
            // nothing to share without a virtual monitor area
            if (mode == AppMode.VirtualMonitor && virtualMonitor == null)
                ApplyMode(AppMode.Normal);
        }

        private bool StartCamera()
        {
            if (virtualCamera != null)
                return true;
            if (VirtualCameraSetup.GetStatus() == VirtualCameraSetupStatus.NotSupported)
            {
                MessageBox.Show("The virtual camera needs Windows 11 (x64 package).", "ScreenshareHelper", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (!VirtualCameraSetup.EnsureInstalledInteractive()) // asks once, elevates via UAC
                return false;

            virtualCamera = VirtualCameraOutput.TryStart(out cameraError);
            if (virtualCamera == null)
                MessageBox.Show(cameraError, "ScreenshareHelper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return virtualCamera != null;
        }

        private void StopCamera()
        {
            virtualCamera?.Dispose();
            virtualCamera = null;
            cameraError = null;
        }

        private void CloseVirtualMonitor()
        {
            var closed = virtualMonitor;
            virtualMonitor = null;
            closed?.Close();
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowAreaFrame
        {
            get => Settings.Default.ShowAreaFrame;
            set
            {
                Settings.Default.ShowAreaFrame = value;
                Settings.Default.Save();
                if (virtualMonitor != null)
                    virtualMonitor.ShowAreaFrame = value;
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool CopyMouse
        {
            get => Program.CopyMouse;
            set
            {
                Program.CopyMouse = Settings.Default.CopyMouse = value;
                Settings.Default.Save();
                Invalidate();
            }
        }

        public void InstallCamera()
        {
            if (VirtualCameraSetup.InstallElevated() && mode != AppMode.Camera)
                ApplyMode(AppMode.Camera);
        }

        public void UninstallCamera()
        {
            if (mode == AppMode.Camera)
                ApplyMode(AppMode.Normal);
            if (!VirtualCameraSetup.UninstallElevated())
                MessageBox.Show("The virtual camera was not removed.", "ScreenshareHelper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public bool Busy => busy;

        public async Task InstallDriverAsync(bool switchToVirtualMonitor)
        {
            if (busy)
                return;
            busy = true;
            try
            {
                bool installed = await ParsecDriverSetup.InstallAsync();
                busy = false;
                if (installed && switchToVirtualMonitor)
                    ApplyMode(AppMode.VirtualMonitor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                MessageBox.Show($"The Parsec Virtual Display Driver could not be installed: {ex.Message}", "ScreenshareHelper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                busy = false;
            }
        }

        public async Task UninstallDriverAsync()
        {
            if (busy)
                return;
            if (mode == AppMode.VirtualMonitor)
                ApplyMode(AppMode.Normal);
            busy = true;
            try
            {
                if (!await ParsecDriverSetup.UninstallAsync() && ParsecDriverSetup.CanUninstall)
                    MessageBox.Show("The driver was not removed. You can also remove \"Parsec Virtual Display Driver\" in Windows Settings > Apps.", "ScreenshareHelper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                MessageBox.Show($"The driver could not be removed: {ex.Message}", "ScreenshareHelper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>Closes the app (also from the hidden modes, where closing the window only cancels "Set area").</summary>
        public void ExitApp()
        {
            exiting = true;
            Close();
        }
        #endregion

        #region Window Events
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x0;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private void buttonSetCaptureArea_Click(object sender, EventArgs e)
        {
            switch (mode)
            {
                case AppMode.Normal:
                    SetCaptureArea();
                    break;
                case AppMode.Camera:
                    Settings.Default.CaptureLocation = this.Location;
                    Settings.Default.CaptureSize = this.Size;
                    Settings.Default.Save();
                    EndSetArea();
                    break;
                case AppMode.VirtualMonitor:
                    Settings.Default.VirtualMonitorLocation = this.Location;
                    Settings.Default.VirtualMonitorSize = this.Size;
                    Settings.Default.Save();
                    if (ShowVirtualMonitor())
                        EndSetArea();
                    break;
            }
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            EndSetArea();
        }

        private void SetCaptureArea()
        {
            Settings.Default.CaptureLocation = this.Location;
            Settings.Default.CaptureSize = this.Size;
            Settings.Default.Save();

            setWindowToBackground();
        }

        private void comboVirtualMonitorResolution_SelectionChangeCommitted(object sender, EventArgs e)
        {
            Settings.Default.VirtualMonitorResolution = (string)comboVirtualMonitorResolution.SelectedItem;
            Settings.Default.Save();
            if (virtualMonitor != null)
                ShowVirtualMonitor();
        }

        /// <summary>Makes the window 16:9 (keeps the height), so the area fills a 16:9 virtual monitor without black bars.</summary>
        private void buttonAspect16x9_Click(object sender, EventArgs e)
        {
            Width = (int)Math.Round(Height * 16 / 9.0);
        }

        /// <summary>Adds the virtual monitor (or updates its area) showing the virtual monitor area. False if there is none.</summary>
        private bool ShowVirtualMonitor(bool quiet = false)
        {
            var area = new Rectangle(Settings.Default.VirtualMonitorLocation, Settings.Default.VirtualMonitorSize);
            if (area.Width <= 0 || area.Height <= 0)
                return false;
            var resolution = VirtualMonitorForm.ParseResolution(Settings.Default.VirtualMonitorResolution);
            if (virtualMonitor != null)
            {
                virtualMonitor.SetArea(area, resolution);
                return true;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                virtualMonitor = new VirtualMonitorForm(area, resolution);
                virtualMonitor.FormClosed += VirtualMonitor_FormClosed;
                virtualMonitor.Show();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                virtualMonitor?.ReleaseResources();
                virtualMonitor = null;
                if (!quiet)
                    MessageBox.Show($"The virtual monitor could not be created: {ex.Message}", "ScreenshareHelper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>
        /// The driver loses the virtual monitor on resume from hibernation (and driver resets): add it again.
        /// Failures are silent, the next check retries.
        /// </summary>
        private void VirtualMonitorWatchdog_Tick(object sender, EventArgs e)
        {
            // hidden window = not in "Set area"; busy = installer running
            if (mode != AppMode.VirtualMonitor || busy || Visible || closing)
                return;
            if (virtualMonitor != null && virtualMonitor.IsAlive)
                return;

            var lost = virtualMonitor;
            virtualMonitor = null;
            if (lost != null)
            {
                lost.Close();
                // right away: a late release could remove the new monitor, the driver numbers them from 0
                lost.ReleaseResources();
            }
            ShowVirtualMonitor(quiet: true);
        }

        private void VirtualMonitor_FormClosed(object sender, FormClosedEventArgs e)
        {
            var closed = (VirtualMonitorForm)sender;
            if (closed == virtualMonitor)
                virtualMonitor = null;
            // not while WM_CLOSE is handled, see ReleaseCaptureResources; on app exit that does it instead
            if (!closing)
                BeginInvoke(new Action(closed.ReleaseResources));
        }

        private void UpdateControlsVisibility()
        {
            bool setup = !Mirroring;
            buttonSetCaptureArea.Visible = labelSize.Visible = setup;
            buttonCloseApp.Visible = setup && mode == AppMode.Normal;
            buttonCancel.Visible = setup && mode != AppMode.Normal;
            comboVirtualMonitorResolution.Visible = buttonAspect16x9.Visible = setup && mode == AppMode.VirtualMonitor;
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
            UpdateControlsVisibility();
            Invalidate();
        }
        private void Form1_Deactivate(object sender, EventArgs e)
        {
            isActive = false;
            // camera / virtual monitor: the setup window stays as it is until Set or Cancel
            if (closing || mode != AppMode.Normal)
                return;
            UpdateSizeBox();
            if (Settings.Default.CaptureSize.Width > 0 && Settings.Default.CaptureSize.Height > 0)
                Size = Settings.Default.CaptureSize; // show exactly the capture area, whether the window was resized larger or smaller

            UpdateControlsVisibility();
            Invalidate();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // camera / virtual monitor: closing the setup window (Alt+F4) only cancels, exit is in the tray menu
            if (mode != AppMode.Normal && !exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                EndSetArea();
                return;
            }

            closing = true;
            if (mode == AppMode.Normal)
                SaveWindowPosition();
            captureTimer.Stop();
            virtualMonitorWatchdog.Stop();
            closingVirtualMonitor = virtualMonitor;
            virtualMonitor?.Close();
            tray?.Dispose();
            tray = null;
        }

        /// <summary>
        /// Releases the capture resources. Called by Program after Application.Run returned: COM calls are not
        /// allowed while WM_CLOSE is being handled as part of an input-synchronous call (RPC_E_CANTCALLOUT_ININPUTSYNCCALL).
        /// </summary>
        public void ReleaseCaptureResources()
        {
            try
            {
                capture?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            capture = null;
            virtualCamera?.Dispose();
            virtualCamera = null;
            closingVirtualMonitor?.ReleaseResources();
            closingVirtualMonitor = null;
            frame?.Dispose();
            frame = null;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (mode == AppMode.Normal)
                this.BeginInvoke(new Action(() =>
                {
                    IntPtr currentWindow = GetForegroundWindow(); // Get the current active window
                    SetForegroundWindow(currentWindow); // Re-focus it, removing focus from our window
                }));

            StartCapture();
            UpdateSizeDisplay();
            tray = new TrayIcon(this);

            virtualMonitorWatchdog.Interval = 10000;
            virtualMonitorWatchdog.Tick += VirtualMonitorWatchdog_Tick;
            virtualMonitorWatchdog.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            isActive = true;
            if (mode == AppMode.Normal)
            {
                setWindowToBackground();
                ApplyMode(AppMode.Normal);
            }
            else
            {
                Hide();
                Opacity = 1;
                ApplyMode(mode);
            }
        }

        private void buttonCloseApp_Click(object sender, EventArgs e)
        {
            ExitApp();
        }

        #endregion
    }
}
