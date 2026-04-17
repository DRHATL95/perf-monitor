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

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32Hotkey.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Toggle();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Toggle()
    {
        var hwnd = new WindowInteropHelper(_target).Handle;
        var ex = User32Hotkey.GetWindowLong(hwnd, User32Hotkey.GWL_EXSTYLE);
        _clickThrough = !_clickThrough;
        var updated = _clickThrough
            ? ex | User32Hotkey.WS_EX_TRANSPARENT | User32Hotkey.WS_EX_LAYERED
            : ex & ~User32Hotkey.WS_EX_TRANSPARENT;
        User32Hotkey.SetWindowLong(hwnd, User32Hotkey.GWL_EXSTYLE, updated);
    }

    public void Dispose()
    {
        var hwnd = new WindowInteropHelper(_target).Handle;
        User32Hotkey.UnregisterHotKey(hwnd, HotkeyId);
        _source.RemoveHook(Hook);
    }
}
