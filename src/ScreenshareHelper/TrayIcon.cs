using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using ScreenshareHelper.Capture;
using ScreenshareHelper.VirtualMonitor;

namespace ScreenshareHelper
{
    /// <summary>Tray icon with the mode switch, "Set area", options and the setup of camera and virtual monitor driver.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "ScreenshareHelper";

        private readonly Form1 owner;
        private readonly NotifyIcon icon;
        private readonly ToolStripMenuItem normal, camera, virtualMonitor, setArea, areaFrame, mouse, autostart;
        private readonly ToolStripMenuItem installCamera, uninstallCamera, installDriver, uninstallDriver;
        private Action balloonAction; // what clicking the current notification does
        private bool driverWarning;
        private Icon warningIcon;

        public TrayIcon(Form1 owner)
        {
            this.owner = owner;

            normal = new ToolStripMenuItem("Normal (share the window)", null, (s, e) => owner.ApplyMode(AppMode.Normal));
            camera = new ToolStripMenuItem("Virtual camera", null, (s, e) => owner.ApplyMode(AppMode.Camera));
            virtualMonitor = new ToolStripMenuItem("Virtual monitor", null, (s, e) => owner.ApplyMode(AppMode.VirtualMonitor));
            setArea = new ToolStripMenuItem("Set area...", null, (s, e) => owner.BeginSetArea());
            areaFrame = new ToolStripMenuItem("Red frame around the area", null, (s, e) => owner.ShowAreaFrame = !owner.ShowAreaFrame);
            mouse = new ToolStripMenuItem("Mouse pointer", null, (s, e) => owner.CopyMouse = !owner.CopyMouse);
            autostart = new ToolStripMenuItem("Start with Windows", null, (s, e) => Autostart = !Autostart);

            installCamera = new ToolStripMenuItem("Install virtual camera", null, (s, e) => owner.InstallCamera());
            uninstallCamera = new ToolStripMenuItem("Uninstall virtual camera", null, (s, e) => owner.UninstallCamera());
            installDriver = new ToolStripMenuItem("Install virtual monitor driver (Parsec)", null, async (s, e) =>
            {
                if (owner.DriverNeedsRepair)
                    await owner.RepairDriverAsync(switchToVirtualMonitor: false);
                else
                    await owner.InstallDriverAsync(switchToVirtualMonitor: false);
            });
            uninstallDriver = new ToolStripMenuItem("Uninstall virtual monitor driver (Parsec)", null, async (s, e) => await owner.UninstallDriverAsync());
            var setup = new ToolStripMenuItem("Setup");
            setup.DropDownItems.AddRange(new ToolStripItem[] { installCamera, uninstallCamera, new ToolStripSeparator(), installDriver, uninstallDriver });

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[]
            {
                normal, camera, virtualMonitor, new ToolStripSeparator(),
                setArea, areaFrame, mouse, new ToolStripSeparator(),
                autostart, setup, new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (s, e) => owner.ExitApp())
            });
            menu.Opening += (s, e) => UpdateItems();

            icon = new NotifyIcon
            {
                Icon = owner.Icon,
                Text = "ScreenshareHelper",
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.DoubleClick += (s, e) =>
            {
                menu.Close(); // opened by the first click
                owner.BeginSetArea();
            };
            icon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ShowMenu();
            };
            icon.BalloonTipClicked += (s, e) =>
            {
                var action = balloonAction;
                balloonAction = null;
                action?.Invoke();
            };
        }

        /// <summary>Opens the menu like a right click does (positioned at the tray, closes when clicking elsewhere).</summary>
        private void ShowMenu() =>
            typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(icon, null);

        private void UpdateItems()
        {
            var mode = owner.Mode;
            normal.Checked = mode == AppMode.Normal;
            camera.Checked = mode == AppMode.Camera;
            virtualMonitor.Checked = mode == AppMode.VirtualMonitor;
            areaFrame.Checked = owner.ShowAreaFrame;
            areaFrame.Enabled = mode == AppMode.VirtualMonitor;
            mouse.Checked = owner.CopyMouse;
            autostart.Checked = Autostart;

            var cameraStatus = VirtualCameraSetup.GetStatus();
            camera.Enabled = cameraStatus != VirtualCameraSetupStatus.NotSupported;
            installCamera.Text = cameraStatus == VirtualCameraSetupStatus.NeedsUpdate ? "Update virtual camera" : "Install virtual camera";
            installCamera.Enabled = cameraStatus is VirtualCameraSetupStatus.NotInstalled or VirtualCameraSetupStatus.NeedsUpdate;
            uninstallCamera.Enabled = !string.IsNullOrEmpty(VirtualCameraSetup.RegisteredDllPath);

            bool busy = owner.Busy;
            normal.Enabled &= !busy;
            camera.Enabled &= !busy;
            virtualMonitor.Enabled = !busy;
            bool needsRepair = owner.DriverNeedsRepair;
            installDriver.Text = needsRepair ? "Repair virtual monitor driver (Parsec)" : "Install virtual monitor driver (Parsec)";
            installDriver.Enabled = !busy && (needsRepair || !ParsecDriverSetup.IsInstalled);
            uninstallDriver.Enabled = !busy && ParsecDriverSetup.CanUninstall;
        }

        public void ShowInfo(string text)
        {
            balloonAction = null;
            icon.ShowBalloonTip(5000, "ScreenshareHelper", text, ToolTipIcon.Info);
        }

        /// <summary>A notification that runs <paramref name="onClick"/> when clicked: unlike a dialog it doesn't interrupt a presentation.</summary>
        public void ShowAction(string text, Action onClick)
        {
            // not cleared on BalloonTipClosed: that arrives late, also for a balloon replaced by this one
            balloonAction = onClick;
            icon.ShowBalloonTip(15000, "ScreenshareHelper", text, ToolTipIcon.Warning);
        }

        /// <summary>Marks the tray icon with a warning sign while the virtual monitor driver is not working.</summary>
        public void SetDriverWarning(bool on)
        {
            if (!on)
                balloonAction = null;
            if (on == driverWarning)
                return;
            driverWarning = on;
            warningIcon ??= CreateWarningIcon(owner.Icon);
            icon.Icon = on ? warningIcon : owner.Icon;
            icon.Text = on ? "ScreenshareHelper - Parsec driver not working" : "ScreenshareHelper";
        }

        private static Icon CreateWarningIcon(Icon appIcon)
        {
            var size = SystemInformation.SmallIconSize;
            using var bitmap = new Bitmap(size.Width, size.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var app = new Icon(appIcon, size))
            using (var warning = SystemIcons.GetStockIcon(StockIconId.Warning, StockIconOptions.SmallIcon))
            {
                graphics.DrawIcon(app, new Rectangle(Point.Empty, size));
                int badge = size.Width * 5 / 8;
                graphics.DrawIcon(warning, new Rectangle(size.Width - badge, size.Height - badge, badge, badge));
            }
            var handle = bitmap.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        private static bool Autostart
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) != null;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                    key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
                else
                    key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.ContextMenuStrip?.Dispose();
            icon.Dispose();
            warningIcon?.Dispose();
        }

        #region Win32
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
        #endregion
    }
}
