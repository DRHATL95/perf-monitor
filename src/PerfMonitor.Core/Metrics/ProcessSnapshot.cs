namespace PerfMonitor.Core.Metrics;

/// <summary>
/// Aggregated per-process-name stats. Multiple OS processes sharing a name
/// (e.g. the dozens of svchost.exe instances) collapse into a single snapshot
/// with <see cref="Count"/> set accordingly.
/// </summary>
public record ProcessSnapshot(
    string Name,
    int Count,
    float CpuPercent,
    long WorkingSetBytes);
