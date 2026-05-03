using System.Runtime.InteropServices;

namespace HeronWin.Brain;

internal static class DisplayTopology
{
    internal static DisplayTopologySnapshot Capture()
    {
        var monitors = new List<DisplayMonitorSnapshot>();
        var callback = new NativeMethods.MonitorEnumProc((handle, _, _, _) =>
        {
            var info = NativeMethods.CreateMonitorInfo();
            if (!NativeMethods.GetMonitorInfo(handle, ref info))
            {
                return true;
            }

            monitors.Add(new DisplayMonitorSnapshot(
                monitors.Count,
                FormatHandle(handle),
                info.DeviceName,
                (info.Flags & NativeMethods.MonitorInfoPrimary) != 0,
                new DisplayBounds(
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top),
                new DisplayBounds(
                    info.WorkArea.Left,
                    info.WorkArea.Top,
                    info.WorkArea.Right - info.WorkArea.Left,
                    info.WorkArea.Bottom - info.WorkArea.Top)));

            return true;
        });

        var enumerationSucceeded = NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);
        var lastError = enumerationSucceeded ? 0 : Marshal.GetLastWin32Error();

        return new DisplayTopologySnapshot(
            new DisplayBounds(
                NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
                NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
                NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
                NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen)),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCMonitors),
            enumerationSucceeded,
            lastError,
            monitors);
    }

    private static string FormatHandle(nint handle) => $"0x{handle.ToInt64():X8}";

    private static class NativeMethods
    {
        internal const int SmXVirtualScreen = 76;
        internal const int SmYVirtualScreen = 77;
        internal const int SmCxVirtualScreen = 78;
        internal const int SmCyVirtualScreen = 79;
        internal const int SmCMonitors = 80;
        internal const uint MonitorInfoPrimary = 0x00000001;

        internal delegate bool MonitorEnumProc(
            nint monitorHandle,
            nint hdcMonitor,
            nint monitorRect,
            nint applicationData);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(
            nint hdc,
            nint clipRect,
            MonitorEnumProc callback,
            nint applicationData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(
            nint monitorHandle,
            ref MONITORINFOEX monitorInfo);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        internal static MONITORINFOEX CreateMonitorInfo()
        {
            var info = new MONITORINFOEX();
            info.Size = (uint)Marshal.SizeOf<MONITORINFOEX>();
            info.DeviceName = string.Empty;
            return info;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MONITORINFOEX
        {
            public uint Size;
            public RECT Monitor;
            public RECT WorkArea;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }
    }
}

internal sealed record DisplayTopologySnapshot(
    DisplayBounds VirtualScreen,
    int MonitorCount,
    bool EnumerationSucceeded,
    int LastError,
    IReadOnlyList<DisplayMonitorSnapshot> Monitors);

internal sealed record DisplayMonitorSnapshot(
    int Index,
    string Handle,
    string DeviceName,
    bool IsPrimary,
    DisplayBounds Bounds,
    DisplayBounds WorkArea);

internal sealed record DisplayBounds(
    int Left,
    int Top,
    int Width,
    int Height);
