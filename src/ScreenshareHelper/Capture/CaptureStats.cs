using System;
using System.Diagnostics;

namespace ScreenshareHelper.Capture
{
    /// <summary>
    /// Rolling one-second statistics for the --stats overlay.
    /// </summary>
    public sealed class CaptureStats
    {
        private long windowStart = Stopwatch.GetTimestamp();
        private int ticks, newFrames;
        private TimeSpan captureTime;
        private string summary = "measuring...";

        public void Add(TimeSpan elapsed, bool newFrame)
        {
            ticks++;
            if (newFrame)
                newFrames++;
            captureTime += elapsed;

            var window = Stopwatch.GetElapsedTime(windowStart);
            if (window.TotalSeconds >= 1)
            {
                summary = $"{ticks / window.TotalSeconds:0} ticks/s | {newFrames / window.TotalSeconds:0} new frames/s | {captureTime.TotalMilliseconds / ticks:0.0} ms/capture";
                windowStart = Stopwatch.GetTimestamp();
                ticks = newFrames = 0;
                captureTime = TimeSpan.Zero;
            }
        }

        public override string ToString() => summary;
    }
}
