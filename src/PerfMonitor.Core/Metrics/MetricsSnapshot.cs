namespace PerfMonitor.Core.Metrics;

public record MetricsSnapshot(
    DateTime Timestamp,
    float CpuLoadPercent,
    float? CpuTempC,
    float RamUsedGb,
    float RamTotalGb,
    float GpuLoadPercent,
    float? GpuTempC,
    float GpuVramUsedGb,
    float GpuVramTotalGb,
    float NetDownMBps,
    float NetUpMBps,
    IReadOnlyList<FanReading> Fans,
    MetricsHealth Health);
