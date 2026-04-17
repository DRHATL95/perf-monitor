using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Hardware;

public interface IHardwareSource : IDisposable
{
    MetricsSnapshot Poll();
    bool TempsAvailable { get; }
}
