using System.Drawing;

namespace ScreenshareHelper.Capture
{
    /// <summary>
    /// Classic GDI BitBlt capture (Graphics.CopyFromScreen). Works everywhere, but is CPU bound
    /// and can show black areas for some GPU-rendered / hardware-accelerated content.
    /// </summary>
    public sealed class GdiScreenCapture : IScreenCapture
    {
        public string Name => "GDI";

        public bool Capture(Rectangle area, Bitmap target)
        {
            using var g = Graphics.FromImage(target);
            g.CopyFromScreen(area.X, area.Y, 0, 0, area.Size);
            return true;
        }

        public void Dispose() { }
    }
}
