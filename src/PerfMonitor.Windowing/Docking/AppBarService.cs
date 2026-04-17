using PerfMonitor.Windowing.Interop;
using System.Windows;
using System.Windows.Interop;

namespace PerfMonitor.Windowing.Docking;

public sealed class AppBarService : IAppBarService
{
    private readonly HashSet<IntPtr> _registered = new();

    public bool Register(Window window, AppBarEdge edge, int thicknessPx)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (_registered.Contains(hwnd)) Unregister(window);

        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd
        };
        if (Shell32.SHAppBarMessage(Shell32.ABM_NEW, ref data) == IntPtr.Zero)
            return false;

        data.uEdge = edge switch
        {
            AppBarEdge.Top => Shell32.ABE_TOP,
            AppBarEdge.Bottom => Shell32.ABE_BOTTOM,
            AppBarEdge.Left => Shell32.ABE_LEFT,
            AppBarEdge.Right => Shell32.ABE_RIGHT,
            _ => Shell32.ABE_TOP
        };

        var screen = SystemParameters.WorkArea;
        data.rc = edge switch
        {
            AppBarEdge.Top    => new RECT { Left = 0, Top = 0, Right = (int)screen.Right, Bottom = thicknessPx },
            AppBarEdge.Bottom => new RECT { Left = 0, Top = (int)screen.Bottom - thicknessPx, Right = (int)screen.Right, Bottom = (int)screen.Bottom },
            AppBarEdge.Left   => new RECT { Left = 0, Top = 0, Right = thicknessPx, Bottom = (int)screen.Bottom },
            AppBarEdge.Right  => new RECT { Left = (int)screen.Right - thicknessPx, Top = 0, Right = (int)screen.Right, Bottom = (int)screen.Bottom },
            _ => default
        };
        Shell32.SHAppBarMessage(Shell32.ABM_QUERYPOS, ref data);
        Shell32.SHAppBarMessage(Shell32.ABM_SETPOS, ref data);

        window.Left = data.rc.Left;
        window.Top = data.rc.Top;
        window.Width = data.rc.Right - data.rc.Left;
        window.Height = data.rc.Bottom - data.rc.Top;
        _registered.Add(hwnd);
        return true;
    }

    public void Unregister(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !_registered.Contains(hwnd)) return;
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd
        };
        Shell32.SHAppBarMessage(Shell32.ABM_REMOVE, ref data);
        _registered.Remove(hwnd);
    }
}
