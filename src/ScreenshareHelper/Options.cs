using CommandLine;

namespace ScreenshareHelper
{
    public class Options
    {
        [Option('n', "processname", Required = false, HelpText = "Process name to snap to at startup.", SetName = "process")]
        public string Process { get; set; }

        [Option('i', "pid", Required = false, HelpText = "Process ID to snap to at startup.", SetName = "process")]
        public int? ProcessID { get; set; }

        [Option("no-mouse", Required = false, HelpText = "Disable mirroring the mouse pointer.")]
        public bool NoMouse { get; set; }

        [Option("color", Required = false, HelpText = "Background color: a named color (e.g. Black, DodgerBlue), a hex code RRGGBB or AARRGGBB (with optional '#' or '0x' prefix), or 'Transparent' for a fully see-through window.")]
        public string Color { get; set; }

        [Option("auto-set", Required = false, HelpText = "Automatically set the capture area (like clicking 'Set') when the window loses focus.")]
        public bool AutoSet { get; set; }

        [Option("capture", Required = false, Default = Capture.CaptureMethod.Dxgi, HelpText = "Capture method: Dxgi (Desktop Duplication, default), Gdi (classic) or Wgc (Windows.Graphics.Capture).")]
        public Capture.CaptureMethod CaptureMethod { get; set; }

        [Option("fps", Required = false, Default = 30, HelpText = "Target frames per second (1-120).")]
        public int Fps { get; set; }

        [Option("virtual-camera", Required = false, HelpText = "Also publish the capture area as virtual camera 'ScreenshareHelper' (Windows 11, x64, one-time registration with regsvr32 as admin).")]
        public bool VirtualCamera { get; set; }

        [Option("stats", Required = false, HelpText = "Show capture method, frame rate and capture time in the window.")]
        public bool Stats { get; set; }
    }
}
