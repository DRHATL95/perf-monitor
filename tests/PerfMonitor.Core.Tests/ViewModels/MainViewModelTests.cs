using FluentAssertions;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.ViewModels;
using System.ComponentModel;

namespace PerfMonitor.Core.Tests.ViewModels;

public class MainViewModelTests
{
    private static MetricsSnapshot Snap(float cpu = 10, float? cpuTemp = 50, float gpu = 20, float? gpuTemp = 55) =>
        new(DateTime.UnixEpoch, cpu, cpuTemp, 8, 16, gpu, gpuTemp, 2, 8, 1, 0.5f, Array.Empty<FanReading>(), MetricsHealth.Ok);

    [Fact]
    public void Apply_UpdatesCpuLoad_RaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Apply(Snap(cpu: 42));

        vm.CpuLoadPercent.Should().Be(42);
        raised.Should().Contain(nameof(MainViewModel.CpuLoadPercent));
    }

    [Fact]
    public void CpuStatus_AboveWarn_ReturnsWarn()
    {
        var vm = new MainViewModel { CpuWarnPercent = 85, CpuCritPercent = 95 };
        vm.Apply(Snap(cpu: 88));
        vm.CpuStatus.Should().Be(MetricStatus.Warn);
    }

    [Fact]
    public void CpuStatus_AboveCrit_ReturnsCrit()
    {
        var vm = new MainViewModel { CpuWarnPercent = 85, CpuCritPercent = 95 };
        vm.Apply(Snap(cpu: 97));
        vm.CpuStatus.Should().Be(MetricStatus.Crit);
    }

    [Fact]
    public void Apply_NullCpuTemp_SetsCpuTempUnavailable()
    {
        var vm = new MainViewModel();
        vm.Apply(Snap(cpuTemp: null));
        vm.CpuTempAvailable.Should().BeFalse();
        vm.CpuTempC.Should().Be(0);
    }
}
