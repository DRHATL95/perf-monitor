using System.Runtime.InteropServices;

namespace PerfMonitor.Tray.Interop;

internal static class User32
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
