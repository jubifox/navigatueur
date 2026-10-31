using System.Runtime.InteropServices;

namespace Navigatueur.App.Interop;

/// <summary>
/// The Win32 surface the shell needs: hosting panels as child windows over
/// WebView2, reading the cursor while it's over the page (whose input goes to
/// another process), and the window-frame tricks a chrome-less window needs.
/// </summary>
internal static class NativeMethods
{
    internal const int WS_CHILD = 0x40000000;
    internal const int WS_VISIBLE = 0x10000000;
    internal const int WS_CLIPCHILDREN = 0x02000000;
    internal const int WS_CLIPSIBLINGS = 0x04000000;

    internal const int WM_GETMINMAXINFO = 0x0024;
    internal const int WM_NCHITTEST = 0x0084;
    internal const int WM_NCLBUTTONDOWN = 0x00A1;
    internal const int WM_ENTERSIZEMOVE = 0x0231;
    internal const int WM_EXITSIZEMOVE = 0x0232;

    internal const int HTTRANSPARENT = -1;
    internal const int HTCAPTION = 2;

    internal const int VK_LBUTTON = 0x01;
    internal const int VK_RBUTTON = 0x02;
    internal const int VK_MBUTTON = 0x04;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOCOPYBITS = 0x0100;
    internal const uint SWP_ASYNCWINDOWPOS = 0x4000;

    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;

    internal const uint GA_ROOT = 2;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
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
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal static bool IsAnyMouseButtonDown() =>
        (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
        || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
        || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;

    internal static bool TryGetMonitorInfo(IntPtr hwnd, out MONITORINFO info)
    {
        info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info);
    }
}
