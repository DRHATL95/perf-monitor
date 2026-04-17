using PerfMonitor.Windowing.Interop;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PerfMonitor.Windowing.Behaviors;

public sealed class FullscreenDetector : IDisposable
{
    private readonly Window _target;
    private readonly DispatcherTimer _timer;
    private bool _isHidden;

    public FullscreenDetector(Window target, TimeSpan? interval = null)
    {
        _target = target;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        var fg = User32Fullscreen.GetForegroundWindow();
        if (fg == IntPtr.Zero) return;

        if (!User32Fullscreen.GetWindowRect(fg, out var wr)) return;
        var mon = User32Fullscreen.MonitorFromWindow(fg, User32Fullscreen.MONITOR_DEFAULTTONEAREST);
        var mi = new User32Fullscreen.MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<User32Fullscreen.MonitorInfo>() };
        if (!User32Fullscreen.GetMonitorInfo(mon, ref mi)) return;

        var isFullscreen =
            wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top &&
            wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;

        if (isFullscreen && !_isHidden) Fade(0);
        else if (!isFullscreen && _isHidden) Fade(1);
        _isHidden = isFullscreen;
    }

    private void Fade(double to)
    {
        // Animate the inner content's opacity rather than the Window's — animating
        // Window.Opacity with AllowsTransparency=True can flash the layered window's
        // backdrop (appears white on light themes) mid-transition.
        var target = _target.Content as UIElement ?? (UIElement)_target;
        var anim = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(250) };
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public void Dispose() => _timer.Stop();
}
