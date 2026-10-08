using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ScreenshareHelper.VirtualMonitor
{
    /// <summary>
    /// One virtual monitor provided by the Parsec Virtual Display Driver (signed IddCx driver, installed separately).
    /// The driver removes all displays about a second after the last ping, so the monitor disappears on its own
    /// when the app exits or crashes. Control protocol: https://github.com/nomi-san/parsec-vdd (core/parsec-vdd.h).
    /// </summary>
    public sealed unsafe class ParsecVirtualDisplay : IDisposable
    {
        public const string DriverDownloadUrl = "https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe";

        // device interface of the "Parsec Virtual Display Adapter"
        private static readonly Guid AdapterGuid = new Guid("00b41627-04c4-429e-a26e-0265cf50c8fa");
        // monitor hardware id, part of DISPLAY_DEVICE.DeviceID of the monitors it creates
        private const string MonitorId = "PSCCDD0";

        private const uint IOCTL_ADD = 0x0022e004;
        private const uint IOCTL_REMOVE = 0x0022a008;
        private const uint IOCTL_UPDATE = 0x0022a00c;

        private readonly SafeFileHandle device;
        private readonly object ioLock = new object();
        private readonly Thread pingThread;
        private volatile bool disposed;
        private readonly int index;

        /// <summary>GDI device name of the monitor, e.g. \\.\DISPLAY5.</summary>
        public string DeviceName { get; }

        public static bool IsDriverInstalled()
        {
            using var handle = OpenDevice();
            return handle != null;
        }

        /// <summary>Plugs in a new virtual monitor and waits until Windows has attached it to the desktop.</summary>
        public ParsecVirtualDisplay()
        {
            device = OpenDevice() ?? throw new InvalidOperationException("The Parsec Virtual Display Driver is not installed or not running.");

            var before = ParsecDisplayNames();
            index = IoControl(IOCTL_ADD);
            if (index < 0)
            {
                device.Dispose();
                throw new InvalidOperationException("The Parsec Virtual Display Driver could not add a display.");
            }
            IoControl(IOCTL_UPDATE);

            pingThread = new Thread(PingLoop) { IsBackground = true, Name = "Parsec VDD keep-alive" };
            pingThread.Start();

            // Windows needs a moment to attach the new monitor
            var sw = Stopwatch.StartNew();
            while (DeviceName == null && sw.ElapsedMilliseconds < 10000)
            {
                Thread.Sleep(100);
                DeviceName = ParsecDisplayNames().Except(before).FirstOrDefault();
            }
            if (DeviceName == null)
            {
                Dispose();
                throw new InvalidOperationException("The virtual display was added, but Windows did not attach it to the desktop.");
            }
        }

        /// <summary>Monitor bounds in virtual desktop coordinates (physical pixels).</summary>
        public Rectangle Bounds
        {
            get
            {
                var dm = NewDevMode();
                if (!EnumDisplaySettings(DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                    return Rectangle.Empty;
                return new Rectangle(dm.dmPositionX, dm.dmPositionY, dm.dmPelsWidth, dm.dmPelsHeight);
            }
        }

        /// <summary>
        /// Switches the monitor to <paramref name="resolution"/> (or the closest offered mode), or with null to the mode
        /// that shows <paramref name="content"/> best, and moves it to a corner of the desktop so the mouse does not wander onto it.
        /// </summary>
        public void Configure(Size content, Size? resolution)
        {
            var modes = new List<DEVMODE>();
            var dm = NewDevMode();
            for (int i = 0; EnumDisplaySettings(DeviceName, i, ref dm); i++)
            {
                if (dm.dmBitsPerPel == 32)
                    modes.Add(dm);
                dm = NewDevMode();
            }
            if (modes.Count == 0)
                return;

            // never upscale (adds no detail, costs CPU, Teams scales down anyway): among the modes that fit into
            // the area take the one with the least black bars, then the largest; if none fits, the smallest
            var fitting = modes.Where(m => m.dmPelsWidth <= content.Width && m.dmPelsHeight <= content.Height).ToList();
            var best = resolution.HasValue
                ? modes
                    .OrderBy(m => Math.Abs(m.dmPelsWidth - resolution.Value.Width) + Math.Abs(m.dmPelsHeight - resolution.Value.Height))
                    .ThenBy(m => Math.Abs(m.dmDisplayFrequency - 60))
                    .First()
                : fitting.Count > 0
                ? fitting
                    .OrderByDescending(m => Math.Round(Coverage(content, m.dmPelsWidth, m.dmPelsHeight), 2))
                    .ThenByDescending(m => m.dmPelsWidth * m.dmPelsHeight)
                    .ThenBy(m => Math.Abs(m.dmDisplayFrequency - 60))
                    .First()
                : modes
                    .OrderBy(m => m.dmPelsWidth * m.dmPelsHeight)
                    .ThenByDescending(m => Coverage(content, m.dmPelsWidth, m.dmPelsHeight))
                    .ThenBy(m => Math.Abs(m.dmDisplayFrequency - 60))
                    .First();

            best.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
            var corner = FindCornerPosition(new Size(best.dmPelsWidth, best.dmPelsHeight));
            if (corner.HasValue)
            {
                best.dmPositionX = corner.Value.X;
                best.dmPositionY = corner.Value.Y;
                best.dmFields |= DM_POSITION;
            }

            int result = ChangeDisplaySettingsEx(DeviceName, ref best, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (result != DISP_CHANGE_SUCCESSFUL)
                Debug.WriteLine($"ChangeDisplaySettingsEx({DeviceName}, {best.dmPelsWidth}x{best.dmPelsHeight}) failed: {result}");
        }

        /// <summary>Fraction of the monitor that is covered by the content when scaled to fit (1 = no black bars).</summary>
        private static double Coverage(Size content, int width, int height)
        {
            double scale = Math.Min((double)width / content.Width, (double)height / content.Height);
            return Math.Round(content.Width * scale * content.Height * scale / ((double)width * height), 3);
        }

        /// <summary>
        /// Position touching the most bottom-right monitor only at its corner: Windows keeps the layout, but the
        /// mouse can't cross a corner, so it does not get lost on the invisible monitor. Null if that spot is taken.
        /// </summary>
        private Point? FindCornerPosition(Size size)
        {
            var others = AttachedDisplays()
                .Where(d => !string.Equals(d.name, DeviceName, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.bounds)
                .ToList();
            if (others.Count == 0)
                return null;

            var anchor = others.OrderByDescending(r => r.Right + r.Bottom).First();
            var candidate = new Rectangle(anchor.Right, anchor.Bottom, size.Width, size.Height);
            return others.Any(r => r.IntersectsWith(candidate)) ? null : candidate.Location;
        }

        private void PingLoop()
        {
            // the driver unplugs the displays if it does not hear from us for ~1 s, parsec-vdd.h says ping < 100 ms
            while (!disposed)
            {
                IoControl(IOCTL_UPDATE);
                Thread.Sleep(50);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            pingThread?.Join(500);
            if (!device.IsInvalid && !device.IsClosed)
            {
                // the driver expects the index as 16-bit big endian
                IoControl(IOCTL_REMOVE, (byte)(index >> 8), (byte)index);
                IoControl(IOCTL_UPDATE);
            }
            device.Dispose();
        }

        #region Driver I/O
        private int IoControl(uint code, params byte[] data)
        {
            lock (ioLock)
            {
                if (device.IsInvalid || device.IsClosed)
                    return -1;

                byte* input = stackalloc byte[32];
                new Span<byte>(input, 32).Clear();
                data.AsSpan(0, Math.Min(data.Length, 32)).CopyTo(new Span<byte>(input, 32));
                uint output = 0;
                using var completed = new ManualResetEvent(false);
                var overlapped = new NativeOverlapped { EventHandle = completed.SafeWaitHandle.DangerousGetHandle() };

                DeviceIoControl(device, code, input, 32, &output, sizeof(uint), null, &overlapped);
                if (!GetOverlappedResultEx(device, &overlapped, out _, 5000, false))
                {
                    // don't leave the driver writing into this stack frame
                    CancelIoEx(device, &overlapped);
                    GetOverlappedResult(device, &overlapped, out _, true);
                    return -1;
                }
                return (int)output;
            }
        }

        private static SafeFileHandle OpenDevice()
        {
            var guid = AdapterGuid;
            var devInfo = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (devInfo == INVALID_HANDLE_VALUE)
                return null;
            try
            {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref guid, i, ref iface); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(devInfo, ref iface, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    if (size <= 0)
                        continue;
                    var detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize + WCHAR[1], padded to 8 bytes on x64
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref iface, detail, size, out _, IntPtr.Zero))
                            continue;
                        var path = Marshal.PtrToStringUni(detail + 4);
                        var handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_NO_BUFFERING | FILE_FLAG_OVERLAPPED | FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
                        if (!handle.IsInvalid)
                            return handle;
                        Debug.WriteLine($"Opening {path} failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                        handle.Dispose();
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
                return null;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }
        }
        #endregion

        #region Display enumeration
        private static IEnumerable<(string name, Rectangle bounds, string monitorId)> AttachedDisplays()
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref adapter, 0); i++)
            {
                if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                    var monitorId = EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0) ? monitor.DeviceID : "";
                    var dm = NewDevMode();
                    var bounds = EnumDisplaySettings(adapter.DeviceName, ENUM_CURRENT_SETTINGS, ref dm)
                        ? new Rectangle(dm.dmPositionX, dm.dmPositionY, dm.dmPelsWidth, dm.dmPelsHeight)
                        : Rectangle.Empty;
                    yield return (adapter.DeviceName, bounds, monitorId ?? "");
                }
                adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            }
        }

        private static HashSet<string> ParsecDisplayNames() => AttachedDisplays()
            .Where(d => d.monitorId.Contains(MonitorId, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static DEVMODE NewDevMode() => new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        #endregion

        #region Win32
        private const int DIGCF_PRESENT = 0x2;
        private const int DIGCF_DEVICEINTERFACE = 0x10;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x1;
        private const uint FILE_SHARE_WRITE = 0x2;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
        private const int DM_POSITION = 0x20;
        private const int DM_PELSWIDTH = 0x80000;
        private const int DM_PELSHEIGHT = 0x100000;
        private const int DM_DISPLAYFREQUENCY = 0x400000;
        private const int CDS_UPDATEREGISTRY = 0x1;
        private const int DISP_CHANGE_SUCCESSFUL = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, void* inBuffer, int inBufferSize, void* outBuffer, int outBufferSize, uint* bytesReturned, NativeOverlapped* overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResultEx(SafeFileHandle file, NativeOverlapped* overlapped, out uint bytesTransferred, uint milliseconds, bool alertable);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeFileHandle file, NativeOverlapped* overlapped, out uint bytesTransferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle file, NativeOverlapped* overlapped);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr param);
        #endregion
    }
}
