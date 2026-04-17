using PerfMonitor.Windowing.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PerfMonitor.Windowing.Windows;

public partial class SettingsWindow : Window
{
    private static readonly Brush SavedBrush  = new SolidColorBrush(Color.FromRgb(0x5A, 0xFF, 0xAA));
    private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D));

    private readonly SettingsViewModel _vm;
    private readonly DispatcherTimer _revertTimer;
    private object? _originalContent;
    private Brush? _originalForeground;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _revertTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _revertTimer.Tick += RevertApplyButton;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        try
        {
            _vm.Apply();
            FlashApplyButton(btn, "Saved", SavedBrush);
        }
        catch (Exception ex)
        {
            FlashApplyButton(btn, "Failed", FailedBrush);
            System.Diagnostics.Debug.WriteLine($"Settings.Apply failed: {ex}");
        }
    }

    private void FlashApplyButton(Button btn, string text, Brush color)
    {
        if (_revertTimer.IsEnabled) _revertTimer.Stop();
        _originalContent ??= btn.Content;
        _originalForeground ??= btn.Foreground;
        btn.Content = text;
        btn.Foreground = color;
        btn.Tag = btn; // smuggle reference to the button into the tick handler
        _revertTimer.Tag = btn;
        _revertTimer.Start();
    }

    private void RevertApplyButton(object? sender, EventArgs e)
    {
        _revertTimer.Stop();
        if (_revertTimer.Tag is Button btn && _originalContent is not null)
        {
            btn.Content = _originalContent;
            btn.Foreground = _originalForeground ?? Brushes.Black;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
