using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ScreenshareHelper.Properties;
using ScreenshareHelper.VirtualMonitor;

namespace ScreenshareHelper
{
    /// <summary>
    /// The areas are absolute desktop coordinates, which only fit the monitor layout they were set on (another dock,
    /// lid closed, other primary monitor shift them). So they are remembered per layout: the settings hold the areas of
    /// the current layout (<see cref="Settings.AreaLayout"/>), the areas of the other layouts are kept in
    /// <see cref="Settings.AreaProfiles"/>. A new layout keeps the areas that still lie on a monitor.
    /// </summary>
    internal static class AreaProfiles
    {
        private const int MaxProfiles = 20;

        public enum Change
        {
            /// <summary>Same layout as before (or no monitor at all, a transient state).</summary>
            None,
            /// <summary>Known layout, its areas were restored.</summary>
            Restored,
            /// <summary>New layout, the areas still lie on a monitor and were kept.</summary>
            Kept,
            /// <summary>New layout, the area of the current mode was off-screen and moved to the primary monitor: ask for a new one.</summary>
            Moved
        }

        private sealed class Profile
        {
            public string Layout { get; set; }
            public DateTime LastUsed { get; set; }
            public string Window { get; set; }
            public string Capture { get; set; }
            public string VirtualMonitor { get; set; }
        }

        /// <summary>Checks the monitor layout and switches the areas in the settings if it changed since the last call.</summary>
        public static Change Update(AppMode mode)
        {
            var physical = PhysicalMonitors();
            if (physical.Count == 0)
                return Change.None;

            var s = Settings.Default;
            var monitors = physical.Select(m => m.bounds).ToList();
            var layout = LayoutKey(physical);
            var previous = s.AreaLayout;
            if (layout == previous)
                return Change.None;

            var profiles = LoadProfiles();
            if (!string.IsNullOrEmpty(previous))
                Remember(profiles, previous);
            s.AreaLayout = layout;

            Change change;
            var known = profiles.FirstOrDefault(p => p.Layout == layout);
            if (known != null)
            {
                Apply(known);
                known.LastUsed = DateTime.UtcNow;
                change = Change.Restored;
            }
            else
            {
                bool windowMoved = s.HasSetDefaults && FitWindow(monitors);
                bool captureMoved = FitArea(monitors, s.CaptureLocation, s.CaptureSize, (l, z) => { s.CaptureLocation = l; s.CaptureSize = z; });
                bool vmonMoved = FitArea(monitors, s.VirtualMonitorLocation, s.VirtualMonitorSize, (l, z) => { s.VirtualMonitorLocation = l; s.VirtualMonitorSize = z; });
                bool activeMoved = mode switch
                {
                    AppMode.Normal => captureMoved || windowMoved,
                    AppMode.Camera => captureMoved,
                    _ => vmonMoved,
                };
                // first start with this feature: just take over the current areas
                change = activeMoved ? Change.Moved : string.IsNullOrEmpty(previous) ? Change.None : Change.Kept;
            }

            s.AreaProfiles = JsonSerializer.Serialize(profiles
                .OrderByDescending(p => p.LastUsed)
                .Take(MaxProfiles)
                .ToList());
            s.Save();
            Debug.WriteLine($"Monitor layout {layout}: {change}");
            return change;
        }

        private static bool FitWindow(List<Rectangle> monitors)
        {
            var s = Settings.Default;
            return FitArea(monitors, s.Location, s.Size, (l, z) => { s.Location = l; s.Size = z; });
        }

        /// <summary>Moves an area that does not lie fully on one monitor to the middle of the primary monitor. True if moved.</summary>
        private static bool FitArea(List<Rectangle> monitors, Point location, Size size, Action<Point, Size> set)
        {
            var area = new Rectangle(location, size);
            if (area.Width <= 0 || area.Height <= 0 || monitors.Any(m => m.Contains(area)))
                return false;

            // the primary monitor is at (0,0)
            var primary = monitors.FirstOrDefault(m => m.Location == Point.Empty);
            if (primary.IsEmpty)
                primary = monitors[0];
            var newSize = new Size(Math.Min(size.Width, primary.Width), Math.Min(size.Height, primary.Height));
            var newLocation = new Point(primary.X + (primary.Width - newSize.Width) / 2, primary.Y + (primary.Height - newSize.Height) / 2);
            set(newLocation, newSize);
            return true;
        }

        /// <summary>Stores the areas currently in the settings as the profile of <paramref name="layout"/>.</summary>
        private static void Remember(List<Profile> profiles, string layout)
        {
            var s = Settings.Default;
            var profile = profiles.FirstOrDefault(p => p.Layout == layout);
            if (profile == null)
                profiles.Add(profile = new Profile { Layout = layout });
            profile.LastUsed = DateTime.UtcNow;
            profile.Window = s.HasSetDefaults ? Format(s.Location, s.Size) : null;
            profile.Capture = Format(s.CaptureLocation, s.CaptureSize);
            profile.VirtualMonitor = Format(s.VirtualMonitorLocation, s.VirtualMonitorSize);
        }

        private static void Apply(Profile profile)
        {
            var s = Settings.Default;
            if (TryParse(profile.Window, out var window))
            {
                s.Location = window.Location;
                s.Size = window.Size;
            }
            if (TryParse(profile.Capture, out var capture))
            {
                s.CaptureLocation = capture.Location;
                s.CaptureSize = capture.Size;
            }
            if (TryParse(profile.VirtualMonitor, out var vmon))
            {
                s.VirtualMonitorLocation = vmon.Location;
                s.VirtualMonitorSize = vmon.Size;
            }
        }

        private static List<Profile> LoadProfiles()
        {
            try
            {
                if (!string.IsNullOrEmpty(Settings.Default.AreaProfiles))
                    return JsonSerializer.Deserialize<List<Profile>>(Settings.Default.AreaProfiles) ?? new List<Profile>();
            }
            catch (JsonException ex)
            {
                Debug.WriteLine(ex);
            }
            return new List<Profile>();
        }

        /// <summary>Bounds of the real monitors; the virtual monitor comes and goes with the mode, so it is not part of the layout.</summary>
        private static List<(string id, Rectangle bounds)> PhysicalMonitors() => ParsecVirtualDisplay.AttachedDisplays()
            .Where(d => !ParsecVirtualDisplay.IsVirtualMonitor(d.monitorId) && !d.bounds.IsEmpty)
            .Select(d => (HardwareId(d.monitorId), d.bounds))
            .ToList();

        /// <summary>
        /// Identifies the layout by monitor model and position/resolution, e.g. "DEL40F4@0,0,3440x1440;...".
        /// The same monitors on another dock give the same key, another arrangement or resolution a new one.
        /// </summary>
        private static string LayoutKey(List<(string id, Rectangle bounds)> monitors) => string.Join(";", monitors
            .Select(m => $"{m.id}@{Format(m.bounds.Location, m.bounds.Size)}")
            .OrderBy(k => k, StringComparer.Ordinal));

        /// <summary>"MONITOR\DEL40F4\{4d36e96e-...}\0003" -> "DEL40F4" (the instance number depends on the port).</summary>
        private static string HardwareId(string monitorId)
        {
            var parts = monitorId.Split('\\');
            return parts.Length > 1 ? parts[1] : monitorId;
        }

        private static string Format(Point location, Size size) =>
            string.Create(CultureInfo.InvariantCulture, $"{location.X},{location.Y},{size.Width}x{size.Height}");

        private static bool TryParse(string text, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            var parts = (text ?? "").Split(',', 'x');
            if (parts.Length != 4 || !parts.All(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                return false;
            var v = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            rect = new Rectangle(v[0], v[1], v[2], v[3]);
            return true;
        }
    }
}
