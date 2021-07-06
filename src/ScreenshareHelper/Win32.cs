using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ScreenshareHelper
{
    public class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT Rect);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, IntPtr pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

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
    }
}
