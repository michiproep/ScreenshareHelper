using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ScreenshareHelper.Capture
{
    /// <summary>
    /// Windows.Graphics.Capture (the API used by Teams, OBS, Snipping Tool): captures the monitor
    /// under the capture area as GPU frames. Includes GPU-rendered content and also works across
    /// GPUs. Windows may draw a yellow border around the captured monitor where removing it is not allowed.
    /// </summary>
    public sealed class WgcScreenCapture : IScreenCapture
    {
        private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

        private ID3D11Device device;
        private ID3D11DeviceContext context;
        private IDirect3DDevice winrtDevice;
        private D3DRegionReader reader;

        private IntPtr monitor;
        private Rectangle monitorBounds;
        private GraphicsCaptureItem item;
        private Direct3D11CaptureFramePool framePool;
        private GraphicsCaptureSession session;
        private Windows.Graphics.SizeInt32 frameSize;

        public string Name => "Windows.Graphics.Capture";

        public WgcScreenCapture()
        {
            if (!GraphicsCaptureSession.IsSupported())
                throw new PlatformNotSupportedException("Windows.Graphics.Capture is not supported on this system.");

            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null, out device, out context).CheckError();
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable));
            try
            {
                winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
            reader = new D3DRegionReader(device, context);

            try
            {
                // allow capturing without the yellow border (Windows 11); ignored if not available
                _ = GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            }
            catch { }
        }

        public bool Capture(Rectangle area, Bitmap target)
        {
            var center = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
            var hmon = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
            if (session == null || hmon != monitor)
                StartSession(hmon);

            // drain the pool, only the newest frame is interesting
            Direct3D11CaptureFrame frame = null;
            for (var next = framePool.TryGetNextFrame(); next != null; next = framePool.TryGetNextFrame())
            {
                frame?.Dispose();
                frame = next;
            }

            if (frame != null)
            {
                using (frame)
                {
                    if (frame.ContentSize.Width != frameSize.Width || frame.ContentSize.Height != frameSize.Height)
                    {
                        // resolution changed
                        frameSize = frame.ContentSize;
                        framePool.Recreate(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, frameSize);
                        monitorBounds = GetMonitorBounds(monitor);
                        return false;
                    }

                    using var texture = new ID3D11Texture2D(GetDxgiInterface(frame.Surface, typeof(ID3D11Texture2D).GUID));
                    reader.StoreFrame(texture);
                }
            }

            return reader.Read(area, monitorBounds, target);
        }

        private void StartSession(IntPtr hmon)
        {
            StopSession();

            monitor = hmon;
            monitorBounds = GetMonitorBounds(hmon);
            item = CreateItemForMonitor(hmon);
            frameSize = item.Size;
            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, frameSize);
            session = framePool.CreateCaptureSession(item);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                session.IsCursorCaptureEnabled = false; // the form draws the cursor itself
            try
            {
                session.IsBorderRequired = false;
            }
            catch { } // not supported before Windows 11 / not allowed
            session.StartCapture();
        }

        private void StopSession()
        {
            session?.Dispose();
            framePool?.Dispose();
            session = null;
            framePool = null;
            item = null;
        }

        public void Dispose()
        {
            StopSession();
            reader?.Dispose();
            winrtDevice?.Dispose();
            context?.Dispose();
            device?.Dispose();
        }

        #region Interop

        private static unsafe GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon)
        {
            var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in IID_IGraphicsCaptureItemInterop, out var interop));
            try
            {
                // IGraphicsCaptureItemInterop: IUnknown (0-2), CreateForWindow (3), CreateForMonitor (4)
                var createForMonitor = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(void***)interop)[4];
                var iid = IID_IGraphicsCaptureItem;
                IntPtr itemPtr;
                Marshal.ThrowExceptionForHR(createForMonitor(interop, hmon, &iid, &itemPtr));
                try
                {
                    return GraphicsCaptureItem.FromAbi(itemPtr);
                }
                finally
                {
                    Marshal.Release(itemPtr);
                }
            }
            finally
            {
                Marshal.Release(interop);
            }
        }

        private static unsafe IntPtr GetDxgiInterface(IDirect3DSurface surface, Guid iid)
        {
            var unknown = ((IWinRTObject)surface).NativeObject.ThisPtr;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in IID_IDirect3DDxgiInterfaceAccess, out var access));
            try
            {
                // IDirect3DDxgiInterfaceAccess: IUnknown (0-2), GetInterface (3)
                var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(void***)access)[3];
                IntPtr result;
                Marshal.ThrowExceptionForHR(getInterface(access, &iid, &result));
                return result;
            }
            finally
            {
                Marshal.Release(access);
            }
        }

        private static Rectangle GetMonitorBounds(IntPtr hmon)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(hmon, ref info);
            return Rectangle.FromLTRB(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public Program.RECT rcMonitor;
            public Program.RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        #endregion
    }
}
