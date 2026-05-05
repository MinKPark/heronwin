using System.Runtime.InteropServices;
using System.Text;

namespace HeronWin.Tools.DesktopAutomation;

internal static class NativeMethods
{
    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    internal const int SwRestore = 9;
    internal const uint InputMouse = 0;
    internal const uint InputKeyboard = 1;
    internal const uint MouseEventFLeftDown = 0x0002;
    internal const uint MouseEventFLeftUp = 0x0004;
    internal const uint MouseEventFRightDown = 0x0008;
    internal const uint MouseEventFRightUp = 0x0010;
    internal const ushort VkA = 0x41;
    internal const ushort VkAlt = 0x12;
    internal const ushort VkApps = 0x5D;
    internal const ushort VkControl = 0x11;
    internal const ushort VkEscape = 0x1B;
    internal const ushort VkF10 = 0x79;
    internal const ushort VkLWin = 0x5B;
    internal const ushort VkReturn = 0x0D;
    internal const ushort VkS = 0x53;
    internal const ushort VkShift = 0x10;
    internal const uint KeyEventFKeyUp = 0x0002;
    internal const uint KeyEventFUnicode = 0x0004;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    internal static string GetWindowText(nint hWnd)
    {
        var length = GetWindowTextLengthW(hWnd);
        var buffer = new StringBuilder(length + 1);
        _ = GetWindowTextW(hWnd, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    internal static string GetClassName(nint hWnd)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassNameW(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
