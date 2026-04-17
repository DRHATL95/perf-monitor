using FluentAssertions;
using PerfMonitor.Hardware;

namespace PerfMonitor.Hardware.Tests;

public class HardwareMonitorTests
{
    [Fact]
    public async Task Start_PublishesSnapshotsToReader()
    {
        using var src = new FakeHardwareSource(seed: 1);
        using var mon = new HardwareMonitor(src, intervalMs: 20);
        mon.Start();

        var reader = mon.Reader;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var snap = await reader.ReadAsync(cts.Token);
        snap.Should().NotBeNull();
        snap.CpuLoadPercent.Should().BeInRange(0, 100);
    }

    [Fact]
    public void IntervalMs_BelowMin_ClampsTo250()
    {
        using var src = new FakeHardwareSource();
        using var mon = new HardwareMonitor(src, intervalMs: 10);
        mon.IntervalMs.Should().Be(250);
    }
}
