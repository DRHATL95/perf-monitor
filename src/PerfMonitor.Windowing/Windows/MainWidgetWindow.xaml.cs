using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Windowing.Controls;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Windows;

public partial class MainWidgetWindow : Window, IWidgetWindow
{
    private static readonly Brush CpuAccent   = (Brush)new BrushConverter().ConvertFromString("#FF8A5A")!;
    private static readonly Brush RamAccent   = (Brush)new BrushConverter().ConvertFromString("#5AD0FF")!;
    private static readonly Brush GpuAccent   = (Brush)new BrushConverter().ConvertFromString("#A58AFF")!;
    private static readonly Brush NetAccent   = (Brush)new BrushConverter().ConvertFromString("#5AFFAA")!;
    private static readonly Brush AlertAccent = (Brush)new BrushConverter().ConvertFromString("#FF4D6D")!;

    private readonly MainViewModel _vm;
    private readonly IProcessSampler? _processSampler;

    private ProcessMetric? _expandedMetric;
    private IReadOnlyList<ProcessSnapshot>? _latestProcessSnapshots;

    // Edge-trigger tracking for auto-expand on Crit. We only open when
    // status transitions INTO Crit — not on every tick while it remains Crit.
    private MetricStatus _lastCpuStatus = MetricStatus.Ok;

    public MainWidgetWindow(MainViewModel vm, IProcessSampler? processSampler = null)
    {
        InitializeComponent();
        _vm = vm;
        _processSampler = processSampler;

        CpuPill.AccentBrush = CpuAccent;
        RamPill.AccentBrush = RamAccent;
        GpuPill.AccentBrush = GpuAccent;
        NetPill.AccentBrush = NetAccent;

        CpuPill.Click += (_, _) => TogglePanel(ProcessMetric.Cpu);
        RamPill.Click += (_, _) => TogglePanel(ProcessMetric.Ram);
        GpuPill.Click += (_, _) => TogglePanel(ProcessMetric.Gpu);
        NetPill.Click += (_, _) => TogglePanel(ProcessMetric.Net);

        _vm.PropertyChanged += OnVmChanged;
        if (_processSampler is not null)
            _processSampler.Updated += OnProcessesUpdated;

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
                    CpuPill.Value = $"{_vm.CpuLoadPercent:F0}%";
                    CpuPill.AccentBrush = _vm.CpuStatus == MetricStatus.Crit ? AlertAccent : CpuAccent;
                    // Edge-triggered auto-expand: !Crit -> Crit, panel closed -> open on CPU.
                    if (_vm.CpuStatus == MetricStatus.Crit
                        && _lastCpuStatus != MetricStatus.Crit
                        && _expandedMetric is null)
                    {
                        TogglePanel(ProcessMetric.Cpu);
                    }
                    _lastCpuStatus = _vm.CpuStatus;
                    break;
                case nameof(MainViewModel.RamUsedGb):
                case nameof(MainViewModel.RamTotalGb):
                    RamPill.Value = _vm.RamTotalGb > 0 ? $"{(_vm.RamUsedGb / _vm.RamTotalGb * 100):F0}%" : "—";
                    break;
                case nameof(MainViewModel.GpuLoadPercent):
                    GpuPill.Value = $"{_vm.GpuLoadPercent:F0}%";
                    break;
                case nameof(MainViewModel.NetDownMBps):
                    NetPill.Value = FormatNet(_vm.NetDownMBps);
                    break;
            }
        });
    }

    private void OnProcessesUpdated(object? sender, IReadOnlyList<ProcessSnapshot> list)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _latestProcessSnapshots = list;
            if (_expandedMetric is { } metric)
                DetailPanel.Show(metric, list);
        });
    }

    /// <summary>
    /// Toggles the detail panel:
    /// - Clicked pill matches current metric -> collapse.
    /// - Clicked pill is a different metric -> switch (stays open).
    /// - Panel is closed -> open on the clicked metric.
    /// Starts/stops the process sampler to match visibility so we don't
    /// enumerate processes when no one is looking.
    /// </summary>
    private void TogglePanel(ProcessMetric metric)
    {
        if (_expandedMetric == metric)
        {
            _expandedMetric = null;
            DetailContainer.Visibility = Visibility.Collapsed;
            _processSampler?.Stop();
        }
        else
        {
            _expandedMetric = metric;
            DetailContainer.Visibility = Visibility.Visible;
            DetailPanel.Show(metric, _latestProcessSnapshots);
            // Start sampler only if the panel needs live data. GPU/NET are
            // deferred so there's no data to fetch for them.
            if (metric is ProcessMetric.Cpu or ProcessMetric.Ram)
                _processSampler?.Start();
            else
                _processSampler?.Stop();
        }
    }

    private void RefreshAll()
    {
        CpuPill.Value = $"{_vm.CpuLoadPercent:F0}%";
        RamPill.Value = _vm.RamTotalGb > 0 ? $"{(_vm.RamUsedGb / _vm.RamTotalGb * 100):F0}%" : "—";
        GpuPill.Value = $"{_vm.GpuLoadPercent:F0}%";
        NetPill.Value = FormatNet(_vm.NetDownMBps);
    }

    private static string FormatNet(float mbps) =>
        mbps >= 10 ? $"{mbps:F0} MB/s" : $"{mbps:F1} MB/s";

    private void OnDragTrigger(object sender, MouseButtonEventArgs e)
    {
        // The pill's own click handler marks pill events as handled, so this
        // only fires when the user grabs empty space on the outer border.
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
