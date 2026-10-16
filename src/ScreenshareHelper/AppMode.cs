namespace ScreenshareHelper
{
    /// <summary>What the app shares; switched in the tray menu and remembered.</summary>
    public enum AppMode
    {
        /// <summary>The window mirrors the capture area, share the window.</summary>
        Normal,
        /// <summary>No window, the capture area is the virtual camera image.</summary>
        Camera,
        /// <summary>No window, the virtual monitor area is shown on a virtual monitor.</summary>
        VirtualMonitor
    }
}
