using System;
using System.Diagnostics;
using System.Drawing;

namespace ScreenshareHelper.Capture
{
    /// <summary>
    /// DXGI Desktop Duplication with GDI as stand-in. Capture errors are usually short-lived (display layout change,
    /// lock screen, UAC, sleep), so a failing method is recreated and retried instead of being given up for good:
    /// while DXGI fails, GDI delivers the frames and DXGI is retried every second. Only if DXGI never worked in this
    /// session (e.g. some hybrid-GPU laptops) it is given up after a few attempts.
    /// </summary>
    public sealed class ResilientCapture : IScreenCapture
    {
        private const int RetryIntervalMs = 1000;
        private const int ShowErrorAfterMs = 2000;
        private const int GiveUpAfterFailures = 3;

        private DxgiScreenCapture dxgi = new DxgiScreenCapture();
        private GdiScreenCapture gdi;
        private IScreenCapture current;
        private bool dxgiWorked;
        private int dxgiFailures;
        private long dxgiRetryAt; // 0 = don't retry
        private long failingSince; // 0 = the last capture worked
        private string lastError;

        public string Name => current?.Name ?? "-";

        /// <summary>The last error while capturing keeps failing (no frames for a while), otherwise null.</summary>
        public string Error => failingSince != 0 && Environment.TickCount64 - failingSince > ShowErrorAfterMs ? lastError : null;

        public bool Capture(Rectangle area, Bitmap target)
        {
            long now = Environment.TickCount64;
            if (dxgi == null && dxgiRetryAt != 0 && now >= dxgiRetryAt)
                dxgi = new DxgiScreenCapture();

            if (dxgi != null)
            {
                if (TryCapture(dxgi, area, target, out bool changed))
                {
                    dxgiWorked = true;
                    dxgiFailures = 0;
                    return changed;
                }
                Release(dxgi);
                dxgi = null;
                dxgiFailures++;
                dxgiRetryAt = dxgiWorked || dxgiFailures < GiveUpAfterFailures ? now + RetryIntervalMs : 0;
            }

            gdi ??= new GdiScreenCapture();
            if (TryCapture(gdi, area, target, out bool gdiChanged))
                return gdiChanged;
            return false;
        }

        private bool TryCapture(IScreenCapture capture, Rectangle area, Bitmap target, out bool changed)
        {
            try
            {
                changed = capture.Capture(area, target);
                current = capture;
                failingSince = 0;
                lastError = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Capture method {capture.Name} failed: {ex}");
                lastError = $"{capture.Name} failed ({ex.GetType().Name}: {ex.Message})";
                if (failingSince == 0)
                    failingSince = Environment.TickCount64;
                changed = false;
                return false;
            }
        }

        private static void Release(IScreenCapture capture)
        {
            try
            {
                capture.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public void Dispose()
        {
            if (dxgi != null)
                Release(dxgi);
            if (gdi != null)
                Release(gdi);
            dxgi = null;
            gdi = null;
            current = null;
        }
    }
}
