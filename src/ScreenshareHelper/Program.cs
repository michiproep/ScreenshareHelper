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
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        public static bool AutoSetOnFocusLoss = true;

        [STAThread]
        static void Main(string[] args)
        {
            // this is a WinExe with no console of its own, so attach to the caller's console and rebind stdio for --help/--version/error output.
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new System.IO.StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }

            var parserResult = Parser.Default.ParseArguments<Options>(args);
            if (parserResult.Tag == ParserResultType.NotParsed)
                return;

            parserResult.WithParsed(o =>
                {
                    if (!string.IsNullOrEmpty(o.Process))
                        SnapToProcess(o.Process);
                    else if (o.ProcessID.HasValue)
                        SnapToProcess(o.ProcessID.Value);
                    Settings.Default.CopyMouse = !o.NoMouse;
                    AutoSetOnFocusLoss = !o.NoAutoSet;

                    if (!string.IsNullOrEmpty(o.Color))
                    {
                        if (TryParseColor(o.Color, out var color))
                            Settings.Default.BackgroundColor = color;
                        else
                            Console.Error.WriteLine($"Unrecognized --color value '{o.Color}'. Use a named color (e.g. Black, DodgerBlue, Transparent) or a hex code RRGGBB/AARRGGBB (optionally prefixed with '#' or '0x').");
                    }
                }
                );

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        #region Color parsing
        private static bool TryParseColor(string input, out System.Drawing.Color color)
        {
            color = default;
            var s = input.Trim();
            var hex = s.StartsWith("#") ? s.Substring(1)
                : s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s.Substring(2)
                : s;

            if ((hex.Length == 6 || hex.Length == 8) && hex.All(Uri.IsHexDigit))
            {
                byte a = 255;
                int offset = 0;
                if (hex.Length == 8)
                {
                    a = Convert.ToByte(hex.Substring(0, 2), 16);
                    offset = 2;
                }
                byte r = Convert.ToByte(hex.Substring(offset, 2), 16);
                byte g = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
                color = System.Drawing.Color.FromArgb(a, r, g, b);
                return true;
            }

            // named .NET color, e.g. "Black", "DodgerBlue", "Transparent" (case-insensitive)
            if (Enum.TryParse<System.Drawing.KnownColor>(s, true, out var known))
            {
                color = System.Drawing.Color.FromKnownColor(known);
                return true;
            }

            return false;
        }
        #endregion


        #region SnapToProcess
        private static void SnapToProcess(int processID)
        {
            System.Diagnostics.Process p = null;

            try
            {
                p = System.Diagnostics.Process.GetProcessById(processID);
            }
            catch 
            {
                //nothing to do
            }
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
