using FluentAssertions;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Core.Tests.Metrics;

public class MetricsSnapshotTests
{
    [Fact]
    public void Snapshot_WithTempsUnavailable_HasNullCpuTemp()
    {
        var s = new MetricsSnapshot(
            Timestamp: DateTime.UtcNow,
            CpuLoadPercent: 42f,
            CpuTempC: null,
            RamUsedGb: 18.2f, RamTotalGb: 32f,
            GpuLoadPercent: 71f,
            GpuTempC: null,
            GpuVramUsedGb: 6f, GpuVramTotalGb: 12f,
            NetDownMBps: 4.2f, NetUpMBps: 0.8f,
            Fans: Array.Empty<FanReading>(),
            Health: MetricsHealth.TempsUnavailable);

        s.CpuTempC.Should().BeNull();
        s.Health.Should().Be(MetricsHealth.TempsUnavailable);
    }

    [Fact]
    public void Snapshot_IsImmutableRecord()
    {
        var a = new MetricsSnapshot(DateTime.UnixEpoch, 10, 50, 8, 16, 20, 55, 2, 8, 1, 0.5f, Array.Empty<FanReading>(), MetricsHealth.Ok);
        var b = a with { CpuLoadPercent = 15 };
        a.CpuLoadPercent.Should().Be(10);
        b.CpuLoadPercent.Should().Be(15);
    }
}
