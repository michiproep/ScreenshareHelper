using System;
using System.Drawing;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenshareHelper.Capture
{
    /// <summary>
    /// DXGI Desktop Duplication: the compositor hands out the monitor image as a GPU texture,
    /// only when something changed. Fast, no capture border, includes GPU-rendered content.
    /// Limitations: one monitor per capture (area is clipped to the monitor under its center),
    /// rotated monitors are not handled, may fail on some hybrid-GPU laptops.
    /// </summary>
    public sealed class DxgiScreenCapture : IScreenCapture
    {
        private ID3D11Device device;
        private ID3D11DeviceContext context;
        private IDXGIOutputDuplication duplication;
        private D3DRegionReader reader;
        private Rectangle outputBounds;

        public string Name => "DXGI Desktop Duplication";

        public bool Capture(Rectangle area, Bitmap target)
        {
            var center = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
            if (duplication == null || !outputBounds.Contains(center))
                CreateDuplication(center);

            var result = duplication.AcquireNextFrame(0, out var info, out IDXGIResource resource);
            if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
                return reader.Read(area, outputBounds, target); // nothing new on screen, but the area might have changed

            if (result.Code == Vortice.DXGI.ResultCode.AccessLost.Code)
            {
                // desktop switch (UAC, lock screen), resolution change, ... -> recreate on next call
                ReleaseDuplication();
                return false;
            }
            result.CheckError();

            try
            {
                if (info.LastPresentTime != 0) // 0 = only the mouse moved
                {
                    using var texture = resource.QueryInterface<ID3D11Texture2D>();
                    reader.StoreFrame(texture);
                }
            }
            finally
            {
                resource.Dispose();
                duplication.ReleaseFrame();
            }

            return reader.Read(area, outputBounds, target);
        }

        private void CreateDuplication(Point point)
        {
            ReleaseDuplication();

            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                using (adapter)
                {
                    for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                    {
                        using (output)
                        {
                            var r = output.Description.DesktopCoordinates;
                            var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                            if (!bounds.Contains(point))
                                continue;

                            // the device has to live on the adapter that owns the output
                            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, null, out device, out context).CheckError();
                            using var output1 = output.QueryInterface<IDXGIOutput1>();
                            duplication = output1.DuplicateOutput(device);
                            outputBounds = bounds;
                            reader = new D3DRegionReader(device, context);
                            return;
                        }
                    }
                }
            }

            throw new InvalidOperationException($"No monitor found at {point}.");
        }

        private void ReleaseDuplication()
        {
            reader?.Dispose();
            duplication?.Dispose();
            context?.Dispose();
            device?.Dispose();
            reader = null;
            duplication = null;
            context = null;
            device = null;
        }

        public void Dispose() => ReleaseDuplication();
    }
}
