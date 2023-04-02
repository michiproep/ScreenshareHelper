using System;
using System.Drawing;
using System.Windows.Forms;
using ScreenshareHelper.Properties;

namespace ScreenshareHelper
{
    public partial class Form1 : Form
    {
        readonly Color transKey = Color.SaddleBrown;
        private bool isActive = true;
        RedBorder display;
        public Form1()
        {
            InitializeComponent();
            FormBorderStyle = FormBorderStyle.None;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.TransparencyKey = transKey;
            this.BackColor = transKey;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;

            RestoreWindowPosition();

            this.MouseDown += Form1_MouseDown;
            updateVirtualDisplay();
            //WindowState = FormWindowState.Minimized;
            Pen pen = new Pen(Brushes.Green, 3);
            this.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.Clear(transKey);
                var rect = this.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;
                g.DrawRectangle(pen, rect);
            };
            this.Resize += (s, e) => { Invalidate(); };
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

        #region Window Events

        private void buttonSetCaptureArea_Click(object sender, EventArgs e)
        {
            Settings.Default.CaptureLocation = this.Location;
            Settings.Default.CaptureSize = this.Size;
            this.WindowState = FormWindowState.Minimized;
            updateVirtualDisplay();
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
                Settings.Default.Size = new Size(this.Size.Width + this.DefaultMargin.Right + this.DefaultMargin.Left, this.Height + this.DefaultMargin.Top + this.DefaultMargin.Bottom);
            }
            else
            {
                Settings.Default.Location = this.RestoreBounds.Location;
                Settings.Default.Size = this.RestoreBounds.Size;
            }

            Settings.Default.HasSetDefaults = true;
            Settings.Default.Save();
        }



        private void updateVirtualDisplay()
        {
            if (this.display == null)
                this.display = new RedBorder();


            var rectangle = GetCaptureAreaAsRectangle();
            int offset = 7;
            Rectangle borderSize = new Rectangle(rectangle.X + offset, rectangle.Y + offset, rectangle.Width - (2*offset), rectangle.Height- (2*offset));

            display.DesktopBounds = borderSize;
            display.Show();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveWindowPosition();
        }


        private void buttonCloseApp_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void buttonMinimizeApp_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }

        #endregion

        private Rectangle GetCaptureAreaAsRectangle()
        {
            return new Rectangle(Settings.Default.CaptureLocation, Settings.Default.CaptureSize);
        }
    }
}
