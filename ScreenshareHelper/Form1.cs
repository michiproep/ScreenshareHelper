using ScreenshareHelper.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenshareHelper
{
    public partial class Form1 : Form
    {
        readonly Color transKey = Color.SaddleBrown;
        private bool isActive = true;

        public Form1()
        {
            InitializeComponent();
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.TransparencyKey = transKey;
            RestoreWindowPosition();
        }
        
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (isActive)
                e.Graphics.Clear(transKey);
            else
                paint(e.Graphics);
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
        private void paint(Graphics graphics, bool withMouse = true)
        {
            graphics.CopyFromScreen(Settings.Default.CaptureLocation.X, Settings.Default.CaptureLocation.Y, 0, 0, Settings.Default.CaptureSize);
            if (withMouse)
            {
                CURSORINFO pci;
                pci.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(CURSORINFO));

                if (GetCursorInfo(out pci))
                {
                    if (pci.flags == CURSOR_SHOWING)
                    {
                        const int offset = 5;
                        DrawIcon(graphics.GetHdc(), 
                            pci.ptScreenPos.x - Settings.Default.CaptureLocation.X - offset, 
                            pci.ptScreenPos.y - Settings.Default.CaptureLocation.Y - offset, 
                            pci.hCursor);
                        graphics.ReleaseHdc();
                    }
                }
            }
        }
      
        private void buttonSetCaptureArea_Click(object sender, EventArgs e)
        {
            Settings.Default.CaptureLocation = this.Location;
            Settings.Default.CaptureSize = this.Size;
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

        private void Form1_Activated(object sender, EventArgs e)
        {
            isActive = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            buttonSetCaptureArea.Visible = isActive;
        }
        private void Form1_Deactivate(object sender, EventArgs e)
        {
            FormBorderStyle = FormBorderStyle.None;
            this.Size = Settings.Default.CaptureSize;
            isActive = false;
            buttonSetCaptureArea.Visible = isActive;
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveWindowPosition();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            Thread t = new Thread(() =>
            {
                while (true)
                {
                    if (!isActive)
                        Invalidate();
                    Thread.Sleep(10);
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
