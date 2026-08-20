using CommandLine;

namespace ScreenshareHelper
{
    public class Options
    {
        [Option('n', "processname", Required = false, HelpText = "Process name to snap to at startup.", SetName = "process")]
        public string Process { get; set; }

        [Option('i', "pid", Required = false, HelpText = "Process ID to snap to at startup.", SetName = "process")]
        public int? ProcessID { get; set; }

        [Option('m', "no-mouse", Required = false, HelpText = "Disable mirroring the mouse pointer.")]
        public bool NoMouse { get; set; }
    }
}
