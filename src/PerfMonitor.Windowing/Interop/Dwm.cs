using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PerfMonitor.Windowing.Interop;

public static class Dwm
{
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;   // Mica

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void TryEnableMica(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int backdrop = DWMSBT_MAINWINDOW;
        try { DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)); }
        catch (DllNotFoundException) { /* pre-Win11 */ }
        catch (EntryPointNotFoundException) { /* pre-Win11 */ }
    }
}
