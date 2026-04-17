using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Hardware;

public sealed class FakeHardwareSource : IHardwareSource
{
    private readonly Random _rng;
    private readonly bool _tempsAvailable;

    public FakeHardwareSource(int seed = 0, bool tempsAvailable = true)
    {
        _rng = new Random(seed);
        _tempsAvailable = tempsAvailable;
    }

    public bool TempsAvailable => _tempsAvailable;

    public MetricsSnapshot Poll() => new(
        Timestamp: DateTime.UtcNow,
        CpuLoadPercent: (float)(_rng.NextDouble() * 100),
        CpuTempC: _tempsAvailable ? 40f + (float)(_rng.NextDouble() * 40) : null,
        RamUsedGb: 8f + (float)(_rng.NextDouble() * 16),
        RamTotalGb: 32f,
        GpuLoadPercent: (float)(_rng.NextDouble() * 100),
        GpuTempC: _tempsAvailable ? 45f + (float)(_rng.NextDouble() * 30) : null,
        GpuVramUsedGb: (float)(_rng.NextDouble() * 12),
        GpuVramTotalGb: 12f,
        NetDownMBps: (float)(_rng.NextDouble() * 50),
        NetUpMBps: (float)(_rng.NextDouble() * 5),
        Fans: Array.Empty<FanReading>(),
        Health: _tempsAvailable ? MetricsHealth.Ok : MetricsHealth.TempsUnavailable);

    public void Dispose() { }
}
