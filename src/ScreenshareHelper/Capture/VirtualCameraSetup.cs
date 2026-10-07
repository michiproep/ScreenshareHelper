using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;

namespace ScreenshareHelper.Capture
{
    public enum VirtualCameraSetupStatus
    {
        /// <summary>Not Windows 11, not x64 or the native DLL is missing next to the exe.</summary>
        NotSupported,
        NotInstalled,
        /// <summary>Registered, but another version or from an unprotected location (e.g. a dev build).</summary>
        NeedsUpdate,
        Installed
    }

    /// <summary>
    /// One-time machine-wide setup of the virtual camera. The Windows camera service runs as LocalService and
    /// loads the camera DLL from the path registered in HKLM, so that DLL must live in an admin-only folder
    /// (Program Files): a DLL in a user-writable folder could be replaced by any user process and would then
    /// run inside a system service.
    /// </summary>
    public static unsafe class VirtualCameraSetup
    {
        public const string InstallArgument = "--install-camera";
        public const string UninstallArgument = "--uninstall-camera";

        private const string DllName = "ScreenshareHelper.VirtualCamera.dll";
        private const string ClsidKey = @"SOFTWARE\Classes\CLSID\{D99C4FF7-48EE-4763-99DE-AAA8BE60A97E}";

        public static string LocalDllPath => Path.Combine(AppContext.BaseDirectory, DllName);

        public static string InstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ScreenshareHelper", "VirtualCamera");

        public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        public static string RegisteredDllPath
        {
            get
            {
                using var key = Registry.LocalMachine.OpenSubKey(ClsidKey + @"\InprocServer32");
                return key?.GetValue(null) as string;
            }
        }

        public static VirtualCameraSetupStatus GetStatus()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !Environment.Is64BitProcess || !File.Exists(LocalDllPath))
                return VirtualCameraSetupStatus.NotSupported;

            var registered = RegisteredDllPath;
            if (string.IsNullOrEmpty(registered))
                return VirtualCameraSetupStatus.NotInstalled;

            bool protectedLocation = Path.GetFullPath(registered).StartsWith(InstallDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!protectedLocation || !File.Exists(registered) || Hash(registered) != Hash(LocalDllPath))
                return VirtualCameraSetupStatus.NeedsUpdate;

            return VirtualCameraSetupStatus.Installed;
        }

        /// <summary>Asks the user to set up / update the camera (UAC prompt). Returns true if it is installed afterwards.</summary>
        public static bool EnsureInstalledInteractive()
        {
            var status = GetStatus();
            if (status == VirtualCameraSetupStatus.Installed)
                return true;
            if (status == VirtualCameraSetupStatus.NotSupported)
                return false;

            var text = status == VirtualCameraSetupStatus.NotInstalled
                ? "The virtual camera is not set up yet.\n\nSet it up now? This copies the camera component to Program Files and registers it. It needs administrator rights once."
                : "The installed virtual camera is from another version or location.\n\nUpdate it now? This needs administrator rights.";
            if (System.Windows.Forms.MessageBox.Show(text, "ScreenshareHelper", System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Question) != System.Windows.Forms.DialogResult.Yes)
                return false;

            RunElevated(InstallArgument);
            return GetStatus() == VirtualCameraSetupStatus.Installed;
        }

        /// <summary>Handles --install-camera / --uninstall-camera; returns the process exit code.</summary>
        public static int RunSetupCommand(bool install)
        {
            if (!IsElevated)
                return RunElevated(install ? InstallArgument : UninstallArgument);

            try
            {
                if (install)
                    Install();
                else
                    Uninstall();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Virtual camera {(install ? "setup" : "removal")} failed: {ex.Message}");
                return 1;
            }
        }

        private static void Install()
        {
            if (GetStatus() == VirtualCameraSetupStatus.NotSupported)
                throw new PlatformNotSupportedException($"Needs Windows 11 (x64) and {DllName} next to the exe.");

            Directory.CreateDirectory(InstallDir);
            var hash = Hash(LocalDllPath);
            // versioned file name: the camera service may still have an older copy loaded (and locked)
            var target = Path.Combine(InstallDir, $"ScreenshareHelper.VirtualCamera.{hash[..12]}.dll");
            if (!File.Exists(target) || Hash(target) != hash)
                File.Copy(LocalDllPath, target, true);

            CallExport(target, "DllRegisterServer"); // registers the path of the copy it is loaded from
            DeleteFiles(except: target);
            Console.WriteLine($"Virtual camera installed: {target}");
        }

        private static void Uninstall()
        {
            Registry.LocalMachine.DeleteSubKeyTree(ClsidKey, throwOnMissingSubKey: false);
            DeleteFiles(except: null);
            try
            {
                Directory.Delete(InstallDir);
                Directory.Delete(Path.GetDirectoryName(InstallDir));
            }
            catch (IOException) { } // not empty (locked files are removed after a restart)
            Console.WriteLine("Virtual camera removed.");
        }

        private static void DeleteFiles(string except)
        {
            if (!Directory.Exists(InstallDir))
                return;
            foreach (var file in Directory.GetFiles(InstallDir, "ScreenshareHelper.VirtualCamera.*.dll"))
            {
                if (string.Equals(file, except, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MoveFileEx(file, null, MOVEFILE_DELAY_UNTIL_REBOOT); // still loaded by the camera service
                }
            }
        }

        private static int RunElevated(string argument)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath, argument)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC prompt declined
            {
                return 1;
            }
        }

        private static void CallExport(string dll, string export)
        {
            var library = NativeLibrary.Load(dll);
            try
            {
                var function = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(library, export);
                Marshal.ThrowExceptionForHR(function());
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }

        private static string Hash(string file)
        {
            using var stream = File.OpenRead(file);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);
    }
}
