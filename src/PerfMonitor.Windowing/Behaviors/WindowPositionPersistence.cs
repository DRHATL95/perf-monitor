using System.Windows;
using System.Windows.Threading;

namespace PerfMonitor.Windowing.Behaviors;

/// <summary>
/// Tracks a WPF window's position and calls back with the coalesced X/Y
/// a short while after the user stops dragging. Validates proposed
/// restore positions against currently-connected monitors so the
/// widget never opens on a disconnected display.
/// </summary>
public sealed class WindowPositionPersistence : IDisposable
{
    private readonly Window _target;
    private readonly Action<int, int> _onCoalescedMove;
    private readonly DispatcherTimer _coalesceTimer;

    /// <param name="debounce">How long the stream of LocationChanged events
    /// must be idle before we consider the drag finished and fire the save.</param>
    public WindowPositionPersistence(
        Window target,
        Action<int, int> onCoalescedMove,
        TimeSpan? debounce = null)
    {
        _target = target;
        _onCoalescedMove = onCoalescedMove;
        _coalesceTimer = new DispatcherTimer { Interval = debounce ?? TimeSpan.FromMilliseconds(500) };
        _coalesceTimer.Tick += OnTimerTick;
        _target.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        _coalesceTimer.Stop();
        _coalesceTimer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _coalesceTimer.Stop();
        _onCoalescedMove((int)_target.Left, (int)_target.Top);
    }

    /// <summary>
    /// Applies a saved position to the window, validating against currently
    /// connected monitors. If the saved point is outside every monitor's
    /// working area (e.g. the user's second monitor is unplugged), falls
    /// back to a safe position on the primary monitor.
    /// </summary>
    public static void Restore(Window target, int savedX, int savedY, int fallbackX = 20, int fallbackY = 20)
    {
        if (IsPointOnAnyMonitor(savedX, savedY))
        {
            target.Left = savedX;
            target.Top = savedY;
        }
        else
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
            target.Left = (primary?.Left ?? 0) + fallbackX;
            target.Top = (primary?.Top ?? 0) + fallbackY;
        }
    }

    private static bool IsPointOnAnyMonitor(int x, int y)
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var wa = screen.WorkingArea;
            // Require the top-left corner to be at least 48 px inside the working
            // area so a sliver of widget is reachable even at screen edges.
            if (x >= wa.Left - 1 && x <= wa.Right - 48 &&
                y >= wa.Top  - 1 && y <= wa.Bottom - 48)
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        _coalesceTimer.Stop();
        _target.LocationChanged -= OnLocationChanged;
    }
}
