using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenshareHelper.Capture
{
    /// <summary>
    /// Publishes the capture area as a Windows 11 virtual camera ("ScreenshareHelper").
    /// The camera itself is a Media Foundation source in ScreenshareHelper.VirtualCamera.dll that the Windows
    /// camera service loads; frames are handed over through shared memory (layout: see Shared.h).
    /// </summary>
    public sealed unsafe class VirtualCameraOutput : IDisposable
    {
        public const string CameraName = "ScreenshareHelper";
        private const string NativeDll = "ScreenshareHelper.VirtualCamera.dll";
        private const string SharedMemoryName = @"Global\ScreenshareHelperVirtualCamera";
        private const int Width = 1920, Height = 1080, HeaderSize = 64, SlotSize = Width * Height * 4;
        private const uint Magic = 0x43564853;

        private readonly Bitmap canvas = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        private MemoryMappedFile map;
        private MemoryMappedViewAccessor view;
        private byte* basePtr;
        private long nextOpenAttempt;

        /// <summary>True once a camera consumer (Teams, Camera app, ...) opened the camera.</summary>
        public bool Connected => basePtr != null;

        private VirtualCameraOutput() { }

        /// <summary>Registers the session camera; returns null and an error text if that is not possible.</summary>
        public static VirtualCameraOutput TryStart(out string error)
        {
            error = null;
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                error = "Virtual camera needs Windows 11";
                return null;
            }

            try
            {
                if (VirtualCamera_IsRegistered() != 0)
                {
                    error = $"Virtual camera not registered - run once as admin: regsvr32 \"{Path.Combine(AppContext.BaseDirectory, NativeDll)}\"";
                    return null;
                }

                int hr = VirtualCamera_Start(CameraName);
                if (hr < 0)
                {
                    error = $"Virtual camera failed to start (0x{hr:X8})";
                    return null;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                error = $"Virtual camera DLL not available ({ex.GetType().Name}); x64 only";
                return null;
            }

            return new VirtualCameraOutput();
        }

        /// <summary>
        /// Scales <paramref name="frame"/> (the capture area) into the 1920x1080 camera image with black bars
        /// and hands it to the camera. Does nothing while no app uses the camera.
        /// </summary>
        public void Write(Bitmap frame, Rectangle area, bool drawCursor)
        {
            if (!EnsureOpen())
                return;

            float scale = Math.Min((float)Width / frame.Width, (float)Height / frame.Height);
            var dest = new Rectangle((int)((Width - frame.Width * scale) / 2), (int)((Height - frame.Height * scale) / 2),
                                     (int)(frame.Width * scale), (int)(frame.Height * scale));

            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = scale < 1 ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(frame, dest, new Rectangle(Point.Empty, frame.Size), GraphicsUnit.Pixel);
                if (drawCursor)
                    DrawCursor(g, area, dest, scale);
            }

            var header = (Header*)basePtr;
            int slot = header->LatestSlot == 0 ? 1 : 0;
            var bits = canvas.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte* dst = basePtr + HeaderSize + (long)slot * SlotSize;
                for (int y = 0; y < Height; y++)
                    Buffer.MemoryCopy((byte*)bits.Scan0 + (long)y * bits.Stride, dst + (long)y * Width * 4, Width * 4, Width * 4);
            }
            finally
            {
                canvas.UnlockBits(bits);
            }

            Volatile.Write(ref header->LatestSlot, slot);
            Interlocked.Increment(ref header->FrameCounter);
        }

        private bool EnsureOpen()
        {
            if (basePtr != null)
                return true;

            // the camera service creates the shared memory when an app opens the camera
            long now = Environment.TickCount64;
            if (now < nextOpenAttempt)
                return false;
            nextOpenAttempt = now + 1000;

            try
            {
                map = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.ReadWrite);
                view = map.CreateViewAccessor(0, HeaderSize + 2L * SlotSize, MemoryMappedFileAccess.ReadWrite);
                byte* ptr = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                ptr += view.PointerOffset;
                if (((Header*)ptr)->Magic != Magic)
                {
                    view.SafeMemoryMappedViewHandle.ReleasePointer();
                    CloseMap();
                    return false;
                }
                basePtr = ptr;
                return true;
            }
            catch (FileNotFoundException)
            {
                CloseMap();
                return false;
            }
        }

        private static void DrawCursor(Graphics g, Rectangle area, Rectangle dest, float scale)
        {
            var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref info) || info.flags != CURSOR_SHOWING)
                return;
            int x = dest.X + (int)((info.ptScreenPos.X - area.X) * scale);
            int y = dest.Y + (int)((info.ptScreenPos.Y - area.Y) * scale);
            if (!dest.Contains(x, y))
                return;
            var hdc = g.GetHdc();
            try
            {
                DrawIcon(hdc, x, y, info.hCursor);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        private void CloseMap()
        {
            view?.Dispose();
            map?.Dispose();
            view = null;
            map = null;
        }

        public void Dispose()
        {
            if (basePtr != null)
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
                basePtr = null;
            }
            CloseMap();
            canvas.Dispose();
            try
            {
                VirtualCamera_Stop();
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Header
        {
            public uint Magic;
            public uint Width;
            public uint Height;
            public int LatestSlot;
            public long FrameCounter;
        }

        #region Native

        [DllImport(NativeDll)]
        private static extern int VirtualCamera_IsRegistered();

        [DllImport(NativeDll, CharSet = CharSet.Unicode)]
        private static extern int VirtualCamera_Start(string friendlyName);

        [DllImport(NativeDll)]
        private static extern int VirtualCamera_Stop();

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public Point ptScreenPos;
        }

        private const int CURSOR_SHOWING = 1;

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        private static extern bool DrawIcon(IntPtr hDC, int x, int y, IntPtr hIcon);

        #endregion
    }
}
