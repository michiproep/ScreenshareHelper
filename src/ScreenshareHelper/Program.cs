using CommandLine;
using ScreenshareHelper.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenshareHelper
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).
                WithParsed(o =>
                {
                    if (!string.IsNullOrEmpty(o.Process))
                        SnapToProcess(o.Process);
                    else if (o.ProcessID.HasValue)
                        SnapToProcess(o.ProcessID.Value);
                }
                );

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }


        #region SnapToProcess
        private static void SnapToProcess(int processID)
        {
            var p = System.Diagnostics.Process.GetProcessById(processID);
            if(p != null && p.MainWindowHandle != IntPtr.Zero)
            {
                if(GetWindowRect(p.MainWindowHandle, out RECT r))
                {
                    r = GetWindowBounds(p.MainWindowHandle);
                    Settings.Default.CaptureLocation = new System.Drawing.Point(r.left, r.top);
                    Settings.Default.CaptureSize = new System.Drawing.Size(r.right -r.left, r.bottom -r.top);
                    Settings.Default.Save();
                }
            }
        }

        private static void SnapToProcess(string process)
        {
            //find process
            var tmp = System.Diagnostics.Process.GetProcesses();
            var p = System.Diagnostics.Process.GetProcessesByName(process).FirstOrDefault();
            if (p != null)
                SnapToProcess(p.Id);
        }
        #endregion

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT Rect);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, IntPtr pvAttribute, int cbAttribute);

        public static RECT GetWindowBounds(IntPtr handle)
        {
            RECT rect;
            if (Environment.OSVersion.Version.Major < 6)
            {
                //Is Below Vista (exclusive)
                if (!GetWindowRect(handle, out rect))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return rect;
            }
            //Vista (inclusive) and above will include shadows in GetWindowRect.
            IntPtr ptrFrame = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(RECT)));
            int ret = DwmGetWindowAttribute(handle, /*(int)DWMWA.EXTENDED_FRAME_BOUNDS*/ 9, ptrFrame, Marshal.SizeOf(typeof(RECT)));
            if (ret != 0)
                throw new Win32Exception(ret);
            rect = (RECT)Marshal.PtrToStructure(ptrFrame, typeof(RECT));
            Marshal.FreeHGlobal(ptrFrame);
            return rect;
        }

        #endregion
    }
}
