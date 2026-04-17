using Microsoft.Extensions.Hosting;
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Hardware;
using System.Windows;

namespace PerfMonitor.App.Services;

public sealed class MetricsSampler : BackgroundService
{
    private readonly HardwareMonitor _monitor;
    private readonly MainViewModel _vm;

    public MetricsSampler(HardwareMonitor monitor, MainViewModel vm)
    {
        _monitor = monitor;
        _vm = vm;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _monitor.Start();
        await foreach (var snap in _monitor.Reader.ReadAllAsync(stoppingToken))
        {
            var captured = snap;
            Application.Current.Dispatcher.Invoke(() => _vm.Apply(captured));
        }
    }
}
