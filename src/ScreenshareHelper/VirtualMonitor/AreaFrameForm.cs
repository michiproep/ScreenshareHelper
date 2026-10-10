using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenshareHelper.VirtualMonitor
{
    /// <summary>
    /// 1px red frame just outside the virtual monitor area, so you can see what is shared while the main window
    /// is somewhere else. Click-through, never activated, always on top and excluded from screen capture.
    /// </summary>
    public sealed class AreaFrameForm : Form
    {
        private Rectangle frameBounds;

        public AreaFrameForm(Rectangle area)
        {
            Text = "ScreenshareHelper virtual monitor area";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Red;
            SetArea(area);
        }

        /// <summary>Moves the frame around another area.</summary>
        public void SetArea(Rectangle area)
        {
            frameBounds = Rectangle.Inflate(area, 1, 1);
            Bounds = frameBounds;

            // only the 1px border belongs to the window, inside it there is no window at all
            var region = new Region(new Rectangle(Point.Empty, frameBounds.Size));
            region.Exclude(new Rectangle(1, 1, frameBounds.Width - 2, frameBounds.Height - 2));
            var old = Region;
            Region = region;
            old?.Dispose();
        }

        /// <summary>Puts the frame back on top, e.g. after another topmost window came to the front. Doesn't activate it.</summary>
        public void BringToTop()
        {
            if (IsHandleCreated)
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // never take the focus: keyboard input stays with the presenter's app
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // layered + transparent: mouse input goes through the line to the window below
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // a layered window is invisible until its attributes are set
            SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
            // don't show up in any capture (the main window's mirror, sharing the whole screen); Windows 10 2004+
            SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            // WinForms suggests a DPI-scaled size, but the frame must stay exactly around the area
            Bounds = frameBounds;
        }

        #region Win32
        private const int WS_EX_TOPMOST = 0x8;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x8000000;
        private const int LWA_ALPHA = 0x2;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        private const int WM_MOUSEACTIVATE = 0x21;
        private const int MA_NOACTIVATE = 3;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        #endregion
    }
}
