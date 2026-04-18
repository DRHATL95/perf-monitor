using PerfMonitor.Windowing.ViewModels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PerfMonitor.Windowing.Windows;

public partial class SettingsWindow : Window
{
    private static readonly Brush SavedBrush  = new SolidColorBrush(Color.FromRgb(0x5A, 0xFF, 0xAA));
    private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D));
    private static readonly TimeSpan StatusHoldDuration = TimeSpan.FromSeconds(2.5);

    private readonly SettingsViewModel _vm;
    private readonly INotificationService _notify;
    private readonly DispatcherTimer _statusFadeTimer;

    public SettingsWindow(SettingsViewModel vm, INotificationService notify)
    {
        InitializeComponent();
        _vm = vm;
        _notify = notify;
        DataContext = vm;

        _statusFadeTimer = new DispatcherTimer { Interval = StatusHoldDuration };
        _statusFadeTimer.Tick += (_, _) =>
        {
            _statusFadeTimer.Stop();
            FadeStatusOut();
        };
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.Apply();
            ShowStatus("Saved ✓", SavedBrush);
            // Toast is best-effort — may be suppressed by Focus Assist or
            // unregistered AUMID on Windows 10/11. The inline status label
            // is the reliable fallback.
            _notify.Show("PerfMonitor", "Settings applied.");
        }
        catch (Exception ex)
        {
            ShowStatus("Failed — " + ex.Message, FailedBrush);
            _notify.Show("PerfMonitor", "Failed to apply settings: " + ex.Message);
        }
    }

    private void ShowStatus(string text, Brush color)
    {
        _statusFadeTimer.Stop();
        StatusLabel.Text = text;
        StatusLabel.Foreground = color;
        // Fade in quickly so the eye tracks it.
        StatusLabel.BeginAnimation(OpacityProperty,
            new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(120) });
        _statusFadeTimer.Start();
    }

    private void FadeStatusOut()
    {
        StatusLabel.BeginAnimation(OpacityProperty,
            new DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(400) });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
