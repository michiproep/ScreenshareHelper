using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenshareHelper.Capture
{
    /// <summary>
    /// Shared GPU -> CPU path for the D3D based capture methods: keeps a GPU copy of the latest
    /// monitor frame and reads back only the requested region via a small staging texture.
    /// </summary>
    internal sealed class D3DRegionReader : IDisposable
    {
        private readonly ID3D11Device device;
        private readonly ID3D11DeviceContext context;
        private ID3D11Texture2D lastFrame;   // full monitor, GPU only
        private ID3D11Texture2D staging;     // capture area size, CPU readable
        private Rectangle lastArea;
        private bool hasNewFrame;

        public D3DRegionReader(ID3D11Device device, ID3D11DeviceContext context)
        {
            this.device = device;
            this.context = context;
        }

        /// <summary>Stores a copy of a freshly captured monitor frame (GPU to GPU, cheap).</summary>
        public void StoreFrame(ID3D11Texture2D frame)
        {
            var desc = frame.Description;
            if (lastFrame == null || lastFrame.Description.Width != desc.Width || lastFrame.Description.Height != desc.Height)
            {
                lastFrame?.Dispose();
                lastFrame = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = desc.Width,
                    Height = desc.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.None
                });
            }
            context.CopyResource(lastFrame, frame);
            hasNewFrame = true;
        }

        /// <summary>
        /// Copies <paramref name="area"/> (virtual desktop coordinates) out of the last frame into <paramref name="target"/>.
        /// <paramref name="monitorBounds"/> is the desktop rectangle the frame covers.
        /// </summary>
        public bool Read(Rectangle area, Rectangle monitorBounds, Bitmap target)
        {
            if (lastFrame == null || (!hasNewFrame && area == lastArea))
                return false;

            EnsureStaging(area.Size);

            var src = Rectangle.Intersect(area, monitorBounds);
            if (!src.IsEmpty)
            {
                var box = new Vortice.Mathematics.Box(src.Left - monitorBounds.Left, src.Top - monitorBounds.Top, 0,
                                  src.Right - monitorBounds.Left, src.Bottom - monitorBounds.Top, 1);
                context.CopySubresourceRegion(staging, 0, (uint)(src.Left - area.Left), (uint)(src.Top - area.Top), 0, lastFrame, 0, box);
            }

            var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var bits = target.LockBits(new Rectangle(Point.Empty, target.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    CopyRows(mapped.DataPointer, (int)mapped.RowPitch, bits.Scan0, bits.Stride, Math.Min(area.Width, target.Width) * 4, Math.Min(area.Height, target.Height));
                }
                finally
                {
                    target.UnlockBits(bits);
                }
            }
            finally
            {
                context.Unmap(staging, 0);
            }

            hasNewFrame = false;
            lastArea = area;
            return true;
        }

        private static unsafe void CopyRows(IntPtr src, int srcPitch, IntPtr dst, int dstStride, int rowBytes, int rows)
        {
            byte* s = (byte*)src;
            byte* d = (byte*)dst;
            for (int y = 0; y < rows; y++)
            {
                Unsafe.CopyBlockUnaligned(d, s, (uint)rowBytes);
                s += srcPitch;
                d += dstStride;
            }
        }

        private void EnsureStaging(Size size)
        {
            if (staging != null && staging.Description.Width == size.Width && staging.Description.Height == size.Height)
                return;

            staging?.Dispose();
            staging = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)size.Width,
                Height = (uint)size.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read
            });
        }

        public void Dispose()
        {
            staging?.Dispose();
            lastFrame?.Dispose();
        }
    }
}
