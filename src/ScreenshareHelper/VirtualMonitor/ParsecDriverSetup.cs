using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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

        public static bool IsInstalled => ParsecVirtualDisplay.IsDriverInstalled();

        /// <summary>True if the driver (or at least its uninstaller) is there.</summary>
        public static bool CanUninstall => IsInstalled || FindUninstallCommand() != null;

        /// <summary>Downloads and runs the driver installer. Returns true if the driver is available afterwards, false if cancelled.</summary>
        public static async Task<bool> InstallAsync()
        {
            var file = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(ParsecVirtualDisplay.DriverDownloadUrl).LocalPath));
            using (var http = new HttpClient())
                await File.WriteAllBytesAsync(file, await http.GetByteArrayAsync(ParsecVirtualDisplay.DriverDownloadUrl));

            if (!await RunAsync(file, ""))
                return false;

            // the driver comes up a moment after the installer exits
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
            return await RunAsync(command.Value.file, command.Value.arguments);
        }

        private static async Task<bool> RunAsync(string file, string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, Verb = "runas" });
                await process.WaitForExitAsync();
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC prompt declined
            {
                return false;
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
    }
}
