using System.Runtime.InteropServices;

namespace PerfMonitor.Windowing.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct WinRECT { public int Left, Top, Right, Bottom; }

internal static class User32Fullscreen
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out WinRECT lpRect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfo
    {
        public uint cbSize;
        public WinRECT rcMonitor;
        public WinRECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
    public const uint MONITOR_DEFAULTTONEAREST = 2;
}
