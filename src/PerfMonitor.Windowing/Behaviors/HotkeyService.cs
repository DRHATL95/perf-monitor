using PerfMonitor.Windowing.Interop;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PerfMonitor.Windowing.Behaviors;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xB000;
    private readonly Window _target;
    private readonly HwndSource _source;
    private bool _clickThrough;

    public HotkeyService(Window target, ModifierKeys modifiers, Key key)
    {
        _target = target;
        var hwnd = new WindowInteropHelper(target).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd)!;
        _source.AddHook(Hook);

        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= User32Hotkey.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt))     mods |= User32Hotkey.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift))   mods |= User32Hotkey.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= User32Hotkey.MOD_WIN;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        User32Hotkey.RegisterHotKey(hwnd, HotkeyId, mods, vk);
    }

    /// <summary>
    /// Current effective click-through state. True = mouse events fall
    /// through the widget to whatever's underneath.
    /// </summary>
    public bool IsClickThrough => _clickThrough;

    /// <summary>
    /// Fires whenever click-through state changes (hotkey OR programmatic).
    /// Listen to this to surface feedback (toast, icon change, etc.).
    /// </summary>
    public event EventHandler<bool>? ClickThroughChanged;

    /// <summary>
    /// Programmatically set click-through state. No-ops if already in that state.
    /// </summary>
    public void SetClickThrough(bool on)
    {
        if (_clickThrough == on) return;
        ApplyClickThrough(on);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32Hotkey.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ApplyClickThrough(!_clickThrough);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ApplyClickThrough(bool on)
    {
        var hwnd = new WindowInteropHelper(_target).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = User32Hotkey.GetWindowLong(hwnd, User32Hotkey.GWL_EXSTYLE);
        var updated = on
            ? ex | User32Hotkey.WS_EX_TRANSPARENT | User32Hotkey.WS_EX_LAYERED
            : ex & ~User32Hotkey.WS_EX_TRANSPARENT;
        User32Hotkey.SetWindowLong(hwnd, User32Hotkey.GWL_EXSTYLE, updated);
        _clickThrough = on;
        ClickThroughChanged?.Invoke(this, on);
    }

    public void Dispose()
    {
        var hwnd = new WindowInteropHelper(_target).Handle;
        User32Hotkey.UnregisterHotKey(hwnd, HotkeyId);
        _source.RemoveHook(Hook);
    }
}
