﻿    using System;
    using System.Drawing;
using System.Windows.Forms;
namespace ScreenshareHelper
    {
        public partial class RedBorder : Form
        {
            public RedBorder()
            {

            // Set the form style
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.FormBorderStyle = FormBorderStyle.None;
                this.BackColor = Color.White;
                this.TransparencyKey = Color.White;
                this.TopMost = true;
                ShowInTaskbar = false;
                this.ClickThrough(true);
            //this.HideFromScreenShare(true);
            // Create a red border
            this.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var rect = this.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;
                g.DrawRectangle(pen, rect);
            };
        }
        Pen pen = new Pen(Color.Red, 3);
        //protected override CreateParams CreateParams
        //{
        //    get
        //    {
        //        CreateParams cp = base.CreateParams;
        //            cp.Style |= 0x40000; //WS_SIZEBOX;  
        //        return cp;
        //    }
        //}
        // Enable or disable click-through for the form
        private void ClickThrough(bool enable)
            {
                int exstyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                if (enable)
                    SetWindowLong(this.Handle, GWL_EXSTYLE, exstyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                else
                    SetWindowLong(this.Handle, GWL_EXSTYLE, exstyle & ~WS_EX_TRANSPARENT & ~WS_EX_LAYERED);
            }
        public void HideFromScreenShare(bool hide)
        {
            if (hide)
                SetWindowDisplayAffinity(this.Handle, WDA_EXCLUDEFROMCAPTURE);
            else
                SetWindowDisplayAffinity(this.Handle, WDA_NONE);
        }

        // Win32 API functions

        private const int WDA_NONE = 0x00000000;
        private const int WDA_EXCLUDEFROMCAPTURE = 0x00000001;

        private const int GWL_EXSTYLE = -20;
            private const int WS_EX_TRANSPARENT = 0x20;
            private const int WS_EX_LAYERED = 0x80000;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, int dwAffinity);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern int GetWindowLong(IntPtr hwnd, int index);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        public static extern IntPtr SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int Y, int cx, int cy, int wFlags);

    }
}

