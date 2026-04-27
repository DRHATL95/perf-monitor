using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
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
    private readonly INotificationService? _notify;
    private readonly ISettingsStore? _settingsStore;

    private ProcessMetric? _expandedMetric;
    private IReadOnlyList<ProcessSnapshot>? _latestProcessSnapshots;

    // Edge-trigger tracking for auto-expand on Crit. We only open when
    // status transitions INTO Crit — not on every tick while it remains Crit,
    // and not on the first sample we receive (which has no "previous" to
    // transition from). Nullable sentinel: null = no sample observed yet,
    // so launching into an already-Crit state does NOT auto-expand.
    private MetricStatus? _lastCpuStatus;

    public MainWidgetWindow(
        MainViewModel vm,
        IProcessSampler? processSampler = null,
        INotificationService? notify = null,
        ISettingsStore? settingsStore = null)
    {
        InitializeComponent();
        _vm = vm;
        _processSampler = processSampler;
        _notify = notify;
        _settingsStore = settingsStore;
        DetailPanel.ProcessKilled += (_, result) =>
            _notify?.Show("PerfMonitor — End task", result.ToToastMessage());

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
        RestorePanelStateFromSettings();
    }

    /// <summary>
    /// On launch, re-open the panel on whichever metric was open at last
    /// close. Calling TogglePanel with persist=false avoids a redundant
    /// settings save (the value being restored is already on disk).
    /// </summary>
    private void RestorePanelStateFromSettings()
    {
        var saved = _settingsStore?.Load().Display.LastExpandedMetric;
        if (saved.HasValue)
            TogglePanel(saved.Value, persist: false);
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
                    // Edge-triggered auto-expand: only fire on non-Crit -> Crit
                    // transition after at least one prior sample. At boot
                    // (_lastCpuStatus is null) we refuse to auto-expand even if
                    // the very first sample reports Crit, so launching during
                    // an already-pegged system doesn't ambush the user with
                    // an expanded panel.
                    if (_vm.CpuStatus == MetricStatus.Crit
                        && _lastCpuStatus is { } prev
                        && prev != MetricStatus.Crit
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
    /// <param name="persist">
    /// When true (default), saves the new state to settings so the panel
    /// reopens to the same metric on next launch. Pass false during
    /// boot-time restore to avoid writing the value we just read.
    /// </param>
    private void TogglePanel(ProcessMetric metric, bool persist = true)
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
            // Start sampler only if the panel needs live data. NET is
            // still deferred so no sampler needed for that one.
            if (metric is ProcessMetric.Cpu or ProcessMetric.Ram or ProcessMetric.Gpu)
                _processSampler?.Start();
            else
                _processSampler?.Stop();
        }

        if (persist) SavePanelState();
    }

    /// <summary>
    /// Persist the current expanded-metric state (or null for collapsed)
    /// so the next launch can restore it. Called from every user-driven
    /// TogglePanel; cheap because JsonSettingsStore.Save is a single
    /// File.WriteAllText.
    /// </summary>
    private void SavePanelState()
    {
        if (_settingsStore is null) return;
        try
        {
            var current = _settingsStore.Load();
            if (current.Display.LastExpandedMetric == _expandedMetric) return;
            _settingsStore.Save(current with
            {
                Display = current.Display with { LastExpandedMetric = _expandedMetric }
            });
        }
        catch { /* persistence is best-effort — never fail a click */ }
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
