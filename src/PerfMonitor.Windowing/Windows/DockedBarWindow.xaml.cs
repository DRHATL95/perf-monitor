using PerfMonitor.Core.ViewModels;
using PerfMonitor.Windowing.Docking;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Windows;

public partial class DockedBarWindow : Window, IWidgetWindow
{
    private readonly MainViewModel _vm;
    private readonly IAppBarService _appBar;
    private readonly AppBarEdge _edge;

    public DockedBarWindow(MainViewModel vm, IAppBarService appBar, AppBarEdge edge)
    {
        InitializeComponent();
        _vm = vm;
        _appBar = appBar;
        _edge = edge;

        CpuPill.AccentBrush = new BrushConverter().ConvertFromString("#FF8A5A") as Brush ?? Brushes.White;
        RamPill.AccentBrush = new BrushConverter().ConvertFromString("#5AD0FF") as Brush ?? Brushes.White;
        GpuPill.AccentBrush = new BrushConverter().ConvertFromString("#A58AFF") as Brush ?? Brushes.White;
        NetPill.AccentBrush = new BrushConverter().ConvertFromString("#5AFFAA") as Brush ?? Brushes.White;

        _vm.PropertyChanged += OnVmChanged;
        SourceInitialized += (_, _) => _appBar.Register(this, _edge, thicknessPx: 36);
        Closed += (_, _) => _appBar.Unregister(this);
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e) =>
        Dispatcher.Invoke(() =>
        {
            CpuPill.Value = $"{_vm.CpuLoadPercent:F0}";
            RamPill.Value = _vm.RamTotalGb > 0 ? $"{(_vm.RamUsedGb / _vm.RamTotalGb * 100):F0}" : "—";
            GpuPill.Value = $"{_vm.GpuLoadPercent:F0}";
            NetPill.Value = $"{_vm.NetDownMBps:F1}";
        });
}
