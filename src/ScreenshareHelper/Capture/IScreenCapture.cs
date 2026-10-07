using System;
using System.Drawing;

namespace ScreenshareHelper.Capture
{
    /// <summary>
    /// Copies a screen area (physical pixels, virtual desktop coordinates) into a bitmap.
    /// </summary>
    public interface IScreenCapture : IDisposable
    {
        string Name { get; }

        /// <summary>
        /// Captures <paramref name="area"/> into <paramref name="target"/> (same size as area, 32bpp).
        /// Returns false if there is no new content; <paramref name="target"/> then keeps the previous frame.
        /// </summary>
        bool Capture(Rectangle area, Bitmap target);
    }

    public enum CaptureMethod
    {
        Gdi,
        Dxgi,
        Wgc
    }

    public static class ScreenCaptureFactory
    {
        public static IScreenCapture Create(CaptureMethod method) => method switch
        {
            CaptureMethod.Dxgi => new DxgiScreenCapture(),
            CaptureMethod.Wgc => new WgcScreenCapture(),
            _ => new GdiScreenCapture()
        };
    }
}
