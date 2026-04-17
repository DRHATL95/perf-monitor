using CommunityToolkit.Mvvm.ComponentModel;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Core.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private float cpuLoadPercent;
    [ObservableProperty] private float cpuTempC;
    [ObservableProperty] private bool cpuTempAvailable;
    [ObservableProperty] private float ramUsedGb;
    [ObservableProperty] private float ramTotalGb;
    [ObservableProperty] private float gpuLoadPercent;
    [ObservableProperty] private float gpuTempC;
    [ObservableProperty] private bool gpuTempAvailable;
    [ObservableProperty] private float gpuVramUsedGb;
    [ObservableProperty] private float gpuVramTotalGb;
    [ObservableProperty] private float netDownMBps;
    [ObservableProperty] private float netUpMBps;
    [ObservableProperty] private MetricsHealth health;
    [ObservableProperty] private MetricStatus cpuStatus;
    [ObservableProperty] private MetricStatus cpuTempStatus;
    [ObservableProperty] private MetricStatus gpuTempStatus;

    public float CpuWarnPercent { get; set; } = 85f;
    public float CpuCritPercent { get; set; } = 95f;
    public float CpuTempWarnC { get; set; } = 80f;
    public float CpuTempCritC { get; set; } = 90f;
    public float GpuTempWarnC { get; set; } = 80f;
    public float GpuTempCritC { get; set; } = 88f;

    public void Apply(MetricsSnapshot s)
    {
        CpuLoadPercent    = s.CpuLoadPercent;
        CpuTempAvailable  = s.CpuTempC.HasValue;
        CpuTempC          = s.CpuTempC ?? 0f;
        RamUsedGb         = s.RamUsedGb;
        RamTotalGb        = s.RamTotalGb;
        GpuLoadPercent    = s.GpuLoadPercent;
        GpuTempAvailable  = s.GpuTempC.HasValue;
        GpuTempC          = s.GpuTempC ?? 0f;
        GpuVramUsedGb     = s.GpuVramUsedGb;
        GpuVramTotalGb    = s.GpuVramTotalGb;
        NetDownMBps       = s.NetDownMBps;
        NetUpMBps         = s.NetUpMBps;
        Health            = s.Health;
        CpuStatus         = Classify(s.CpuLoadPercent, CpuWarnPercent, CpuCritPercent);
        CpuTempStatus     = s.CpuTempC is { } ct ? Classify(ct, CpuTempWarnC, CpuTempCritC) : MetricStatus.Ok;
        GpuTempStatus     = s.GpuTempC is { } gt ? Classify(gt, GpuTempWarnC, GpuTempCritC) : MetricStatus.Ok;
    }

    private static MetricStatus Classify(float value, float warn, float crit) =>
        value >= crit ? MetricStatus.Crit :
        value >= warn ? MetricStatus.Warn : MetricStatus.Ok;
}
