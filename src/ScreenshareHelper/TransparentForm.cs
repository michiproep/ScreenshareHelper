using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace ScreenshareHelper
{
    public partial class RedBorder : Form
    {

        Pen pen = new Pen(Color.Red, 3);
        public RedBorder()
        {
            Text = "SCREENSHARE AREA";
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.White;
            this.TransparencyKey = Color.White;
            this.TopMost = true;
            ShowInTaskbar = true;
            this.ClickThrough(true);
            // Create a red border
            this.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var rect = this.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;
                g.DrawRectangle(pen, rect);
            };
            //this.Activated += (s, e) =>
            //{
            //    FormBorderStyle = FormBorderStyle.Sizable;
            //    this.Size = new Size(this.Size.Width, this.Size.Height - captionHeight);
            //};
            //this.Deactivate += (s, e) =>
            //{
            //    FormBorderStyle = FormBorderStyle.None;
            //    this.Size = new Size(this.Size.Width, this.Size.Height + captionHeight);
            //};
        }
        
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x40000; //WS_SIZEBOX;  
                return cp;
            }
        }
        // Enable or disable click-through for the form
        private void ClickThrough(bool enable)
        {
            int exstyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            if (enable)
                SetWindowLong(this.Handle, GWL_EXSTYLE, exstyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            else
                SetWindowLong(this.Handle, GWL_EXSTYLE, exstyle & ~WS_EX_TRANSPARENT & ~WS_EX_LAYERED);
        }



        //[DllImport("user32.dll")]
        //static extern int GetSystemMetrics(int nIndex);

        //const int SM_CYCAPTION = 4;

        //int captionHeight = GetSystemMetrics(SM_CYCAPTION) + 10;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);


    }
}


