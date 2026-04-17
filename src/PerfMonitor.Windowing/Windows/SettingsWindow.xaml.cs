using PerfMonitor.Windowing.ViewModels;
using System.Windows;

namespace PerfMonitor.Windowing.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnApply(object sender, RoutedEventArgs e) => _vm.Apply();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
