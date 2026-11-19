using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ScreenshareHelper.VirtualMonitor
{
    /// <summary>
    /// Installs / removes the Parsec Virtual Display Driver with Parsec's own installer and uninstaller.
    /// Both ask for administrator rights themselves (the UAC prompt shows Parsec as publisher).
    /// </summary>
    public static class ParsecDriverSetup
    {
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string EnumRootKey = @"SYSTEM\CurrentControlSet\Enum\ROOT";

        // hardware id of the "Parsec Virtual Display Adapter" device
        private const string HardwareId = @"Root\Parsec\VDA";

        public static bool IsInstalled => ParsecVirtualDisplay.IsDriverInstalled();

        /// <summary>
        /// The driver is installed (Apps entry), but its adapter cannot be opened, e.g. Device Manager error 43
        /// after a driver crash. Reinstalling does not help, restarting the device usually does.
        /// </summary>
        public static bool IsBroken => !IsInstalled && FindUninstallCommand() != null;

        /// <summary>True if the driver (or at least its uninstaller) is there.</summary>
        public static bool CanUninstall => IsInstalled || FindUninstallCommand() != null;

        /// <summary>Downloads and runs the driver installer. Returns true if the driver is available afterwards, false if cancelled.</summary>
        public static async Task<bool> InstallAsync()
        {
            var file = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(ParsecVirtualDisplay.DriverDownloadUrl).LocalPath));
            using (var http = new HttpClient())
                await File.WriteAllBytesAsync(file, await http.GetByteArrayAsync(ParsecVirtualDisplay.DriverDownloadUrl));

            if (await RunAsync(file, "") == null)
                return false;

            // the driver comes up a moment after the installer exits
            return await WaitUntilInstalledAsync();
        }

        /// <summary>Restarts the adapter device (elevated pnputil) and waits until the driver is available.</summary>
        public static async Task<RestartResult> RestartAsync()
        {
            // by instance id: "/deviceid <hardware id>" needs Windows 11 21H2, the instance id works since Windows 10 2004
            var instanceId = FindInstanceId();
            if (instanceId == null)
                return RestartResult.Failed;
            var pnputil = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
            int? exitCode = await RunAsync(pnputil, $"/restart-device \"{instanceId}\"", hidden: true);
            if (exitCode == null)
                return RestartResult.Cancelled;
            if (exitCode == ERROR_SUCCESS_REBOOT_REQUIRED)
                return RestartResult.RebootRequired;
            if (exitCode != 0)
                return RestartResult.Failed;
            return await WaitUntilInstalledAsync() ? RestartResult.Running : RestartResult.Failed;
        }

        public enum RestartResult { Running, Cancelled, RebootRequired, Failed }

        /// <summary>Device instance id of the present adapter, e.g. ROOT\DISPLAY\0000 (readable without administrator rights).</summary>
        private static string FindInstanceId()
        {
            using var root = Registry.LocalMachine.OpenSubKey(EnumRootKey);
            if (root == null)
                return null;
            foreach (var enumerator in root.GetSubKeyNames())
            {
                using var enumeratorKey = root.OpenSubKey(enumerator);
                if (enumeratorKey == null)
                    continue;
                foreach (var instance in enumeratorKey.GetSubKeyNames())
                {
                    using var instanceKey = enumeratorKey.OpenSubKey(instance);
                    if (instanceKey?.GetValue("HardwareID") is not string[] hardwareIds ||
                        !Array.Exists(hardwareIds, id => id.Equals(HardwareId, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    // the registry also keeps devices that were removed
                    var instanceId = $@"ROOT\{enumerator}\{instance}";
                    if (CM_Locate_DevNodeW(out _, instanceId, CM_LOCATE_DEVNODE_NORMAL) == CR_SUCCESS)
                        return instanceId;
                }
            }
            return null;
        }

        private static async Task<bool> WaitUntilInstalledAsync()
        {
            for (int i = 0; i < 20 && !IsInstalled; i++)
                await Task.Delay(500);
            return IsInstalled;
        }

        /// <summary>Runs Parsec's uninstaller. Returns false if there is none or it was cancelled.</summary>
        public static async Task<bool> UninstallAsync()
        {
            var command = FindUninstallCommand();
            if (command == null)
                return false;
            return await RunAsync(command.Value.file, command.Value.arguments) != null;
        }

        /// <summary>Runs <paramref name="file"/> elevated. Returns its exit code, null if the UAC prompt was declined.</summary>
        private static async Task<int?> RunAsync(string file, string arguments, bool hidden = false)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, arguments)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                });
                await process.WaitForExitAsync();
                return process.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC prompt declined
            {
                return null;
            }
        }

        /// <summary>UninstallString of the "Parsec Virtual Display Driver" entry in Apps, split into file and arguments.</summary>
        private static (string file, string arguments)? FindUninstallCommand()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = root.OpenSubKey(UninstallKey);
                if (uninstall == null)
                    continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is not string displayName ||
                        !displayName.Contains("Parsec Virtual Display", StringComparison.OrdinalIgnoreCase) ||
                        entry.GetValue("UninstallString") is not string command || string.IsNullOrWhiteSpace(command))
                        continue;

                    command = command.Trim();
                    if (command.StartsWith('"'))
                    {
                        int end = command.IndexOf('"', 1);
                        if (end > 0)
                            return (command[1..end], command[(end + 1)..].Trim());
                    }
                    // unquoted: the path ends at ".exe"
                    int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    return exe > 0 ? (command[..(exe + 4)], command[(exe + 4)..].Trim()) : (command, "");
                }
            }
            return null;
        }

        #region Win32
        private const int ERROR_SUCCESS_REBOOT_REQUIRED = 3010;
        private const uint CM_LOCATE_DEVNODE_NORMAL = 0;
        private const int CR_SUCCESS = 0;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
        #endregion
    }
}
