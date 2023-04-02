using ScreenshareHelper.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenshareHelper
{
    internal class VirtualDisplay :Form
    {
        private Rectangle _rectangle;
        private RedBorder redBorder = new RedBorder();
        public VirtualDisplay(Rectangle rectangle, string name)
        {
            _rectangle = rectangle;
            this.Text = name;
            // Set the form properties
            FormBorderStyle = FormBorderStyle.None;
            //BackColor = Color.Black;
            //TransparencyKey = Color.Black;
            //ShowInTaskbar = false;
            TopMost = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(0, 0);
            //ShowInTaskbar = false;
            this.Load += VirtualDisplay_Load;
            this.FormClosing += VirtualDisplay_FormClosing;
            //this.Activated += VirtualDisplay_Activated;
        }

        //private void VirtualDisplay_Activated(object sender, EventArgs e)
        //{
        //    this.Invoke(new Action(() => { SetWindowPos(this.Handle, HWND_BOTTOM, 0, 0, _rectangle.Width, _rectangle.Height, SWP_FRAMECHANGED); }));
        //}

        protected override void OnPaint(PaintEventArgs e)
        {
            paint(e.Graphics);
        }
        private void VirtualDisplay_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.redBorder.Close();
            this.redBorder = null;
        }
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const int SWP_NOZORDER = 0x0004;
        const int SWP_NOSIZE = 0x0001;
        const int SWP_NOACTIVATE = 0x0010;
        const int SWP_NOMOVE = 0x0002;


        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        const uint WM_PAINT = 0x000F;
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateWindow(IntPtr hWnd);
        private void VirtualDisplay_Load(object sender, EventArgs e)
        {
            var h = this.Handle;
            UpdateCaptureSize(_rectangle);
            

            Thread t = new Thread(() =>
            {
                while (true)
                {
                    //SendMessage(h, WM_PAINT, IntPtr.Zero, IntPtr.Zero);
                    //UpdateWindow(h);
                    try
                    {
                        
                        
                        this.paint(Graphics.FromHwnd(h));
                        //this.OnPaintBackground(Graphics.FromHwnd(h));
                        Thread.Sleep(100);
                    }
                    finally { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }
        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        const int SWP_FRAMECHANGED = 0x0020;
        public void UpdateCaptureSize(Rectangle rectangle)
        {
            this._rectangle = rectangle;
            //SetWindowPos(this.Handle, HWND_BOTTOM, 0, 0, rectangle.Width, rectangle.Height, SWP_FRAMECHANGED);
            //Location = new Point(-10000, -10000);
            Size = rectangle.Size;
            int offset = 1;
            Rectangle borderSize = new Rectangle(rectangle.X - offset, rectangle.Y - offset, rectangle.Width+(2*offset),rectangle.Height+(2*offset));

            //redBorder.Bounds = borderSize;
            //redBorder.Show();
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style &= ~0x40000; //WS_SIZEBOX;  
                return cp;
            }
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
        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        public static extern IntPtr SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int Y, int cx, int cy, int wFlags);


        const Int32 CURSOR_SHOWING = 0x00000001;
        #endregion Cursor

        private void paint(Graphics graphics, bool withMouse = true)
        {
            try
            {
                graphics.CopyFromScreen(Settings.Default.CaptureLocation.X, Settings.Default.CaptureLocation.Y, 0, 0, Settings.Default.CaptureSize);
                if (withMouse)
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
            }
            catch (Exception)
            { }
        }
    }
}
