using PerfMonitor.Windowing.ViewModels;
using System.Windows;

namespace PerfMonitor.Windowing.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly INotificationService _notify;

    public SettingsWindow(SettingsViewModel vm, INotificationService notify)
    {
        InitializeComponent();
        _vm = vm;
        _notify = notify;
        DataContext = vm;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.Apply();
            _notify.Show("PerfMonitor", "Settings applied.");
        }
        catch (Exception ex)
        {
            _notify.Show("PerfMonitor", "Failed to apply settings: " + ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
