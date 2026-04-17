using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PerfMonitor.Windowing.Behaviors;

/// <summary>
/// Periodically re-asserts the target window's topmost state so apps that
/// briefly push ahead in the Z-order (borderless-fullscreen games, full-screen
/// installers, etc.) can't permanently cover the widget.
/// </summary>
public sealed class TopmostGuard : IDisposable
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private readonly Window _target;
    private readonly DispatcherTimer _timer;

    public TopmostGuard(Window target, TimeSpan? interval = null)
    {
        _target = target;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Reassert();
        _timer.Start();
    }

    private void Reassert()
    {
        var hwnd = new WindowInteropHelper(_target).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void Dispose() => _timer.Stop();
}
