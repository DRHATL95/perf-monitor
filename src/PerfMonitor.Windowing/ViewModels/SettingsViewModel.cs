using CommunityToolkit.Mvvm.ComponentModel;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.Windowing.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    [ObservableProperty] private DisplayMode mode;
    [ObservableProperty] private double opacity;
    [ObservableProperty] private int refreshIntervalMs;
    [ObservableProperty] private bool autoHideOnFullscreen;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private float cpuWarnPercent;
    [ObservableProperty] private float cpuCritPercent;
    [ObservableProperty] private float cpuTempWarnC;
    [ObservableProperty] private float cpuTempCritC;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        var s = store.Load();
        Mode = s.Display.Mode;
        Opacity = s.Display.Opacity;
        RefreshIntervalMs = s.Display.RefreshIntervalMs;
        AutoHideOnFullscreen = s.Behavior.AutoHideOnFullscreen;
        StartWithWindows = s.Behavior.StartWithWindows;
        CpuWarnPercent = s.Thresholds.CpuWarnPercent;
        CpuCritPercent = s.Thresholds.CpuCritPercent;
        CpuTempWarnC = s.Thresholds.CpuTempWarnC;
        CpuTempCritC = s.Thresholds.CpuTempCritC;
    }

    public void Apply()
    {
        var current = _store.Load();
        var updated = current with
        {
            Display = current.Display with { Mode = Mode, Opacity = Opacity, RefreshIntervalMs = RefreshIntervalMs },
            Behavior = current.Behavior with { AutoHideOnFullscreen = AutoHideOnFullscreen, StartWithWindows = StartWithWindows },
            Thresholds = current.Thresholds with
            {
                CpuWarnPercent = CpuWarnPercent,
                CpuCritPercent = CpuCritPercent,
                CpuTempWarnC = CpuTempWarnC,
                CpuTempCritC = CpuTempCritC
            }
        };
        _store.Save(updated);
    }
}
