namespace PerfMonitor.Core.Metrics;

/// <summary>
/// Which headline metric the user is drilling into on the widget.
/// </summary>
public enum ProcessMetric
{
    Cpu,
    Ram,
    Gpu,   // deferred — no per-process source wired yet
    Net    // deferred — needs ETW or GetExtendedTcpTable + PID correlation
}
