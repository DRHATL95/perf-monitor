using PerfMonitor.Core.ViewModels;
using PerfMonitor.Windowing.Interop;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Windows;

public partial class MainWidgetWindow : Window
{
    private static readonly Brush CpuAccent = (Brush)new BrushConverter().ConvertFromString("#FF8A5A")!;
    private static readonly Brush RamAccent = (Brush)new BrushConverter().ConvertFromString("#5AD0FF")!;
    private static readonly Brush GpuAccent = (Brush)new BrushConverter().ConvertFromString("#A58AFF")!;
    private static readonly Brush NetAccent = (Brush)new BrushConverter().ConvertFromString("#5AFFAA")!;
    private static readonly Brush AlertAccent = (Brush)new BrushConverter().ConvertFromString("#FF4D6D")!;

    private readonly MainViewModel _vm;

    public MainWidgetWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        CpuPill.AccentBrush = CpuAccent;
        RamPill.AccentBrush = RamAccent;
        GpuPill.AccentBrush = GpuAccent;
        NetPill.AccentBrush = NetAccent;

        _vm.PropertyChanged += OnVmChanged;
        SourceInitialized += (_, _) => Dwm.TryEnableMica(this);
        RefreshAll();
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.CpuLoadPercent):
                case nameof(MainViewModel.CpuStatus):
                    CpuPill.Value = $"{_vm.CpuLoadPercent:F0}";
                    CpuPill.AccentBrush = _vm.CpuStatus == MetricStatus.Crit ? AlertAccent : CpuAccent;
                    break;
                case nameof(MainViewModel.RamUsedGb):
                case nameof(MainViewModel.RamTotalGb):
                    RamPill.Value = _vm.RamTotalGb > 0 ? $"{(_vm.RamUsedGb / _vm.RamTotalGb * 100):F0}" : "—";
                    break;
                case nameof(MainViewModel.GpuLoadPercent):
                    GpuPill.Value = $"{_vm.GpuLoadPercent:F0}";
                    break;
                case nameof(MainViewModel.NetDownMBps):
                    NetPill.Value = $"{_vm.NetDownMBps:F1}";
                    break;
            }
        });
    }

    private void RefreshAll()
    {
        CpuPill.Value = $"{_vm.CpuLoadPercent:F0}";
        RamPill.Value = _vm.RamTotalGb > 0 ? $"{(_vm.RamUsedGb / _vm.RamTotalGb * 100):F0}" : "—";
        GpuPill.Value = $"{_vm.GpuLoadPercent:F0}";
        NetPill.Value = $"{_vm.NetDownMBps:F1}";
    }

    private void OnDragTrigger(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
