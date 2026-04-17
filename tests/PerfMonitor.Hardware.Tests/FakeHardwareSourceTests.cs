using FluentAssertions;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Hardware;

namespace PerfMonitor.Hardware.Tests;

public class FakeHardwareSourceTests
{
    [Fact]
    public void Poll_ReturnsDeterministicSnapshot()
    {
        var src = new FakeHardwareSource(seed: 42);
        var s = src.Poll();
        s.CpuLoadPercent.Should().BeInRange(0, 100);
        s.RamTotalGb.Should().Be(32);
        s.Health.Should().Be(MetricsHealth.Ok);
    }

    [Fact]
    public void Poll_WithTempsDisabled_ReturnsNullTemps()
    {
        var src = new FakeHardwareSource(seed: 42, tempsAvailable: false);
        var s = src.Poll();
        s.CpuTempC.Should().BeNull();
        s.GpuTempC.Should().BeNull();
        s.Health.Should().Be(MetricsHealth.TempsUnavailable);
    }
}
