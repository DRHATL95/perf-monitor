using PerfMonitor.Core.Metrics;
using System.Threading.Channels;

namespace PerfMonitor.Hardware;

public sealed class HardwareMonitor : IDisposable
{
    private readonly IHardwareSource _source;
    private readonly Channel<MetricsSnapshot> _channel;
    private readonly EmaSmoother _netDown = new(0.3f);
    private readonly EmaSmoother _netUp = new(0.3f);
    private readonly EmaSmoother _gpuLoad = new(0.3f);
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int IntervalMs { get; }
    public ChannelReader<MetricsSnapshot> Reader => _channel.Reader;

    public HardwareMonitor(IHardwareSource source, int intervalMs = 1000)
    {
        _source = source;
        IntervalMs = Math.Clamp(intervalMs, 250, 5000);
        _channel = Channel.CreateBounded<MetricsSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Factory.StartNew(() => LoopAsync(_cts.Token),
            TaskCreationOptions.LongRunning).Unwrap();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var raw = _source.Poll();
            var smoothed = raw with
            {
                NetDownMBps = _netDown.Smooth(raw.NetDownMBps),
                NetUpMBps   = _netUp.Smooth(raw.NetUpMBps),
                GpuLoadPercent = _gpuLoad.Smooth(raw.GpuLoadPercent)
            };
            await _channel.Writer.WriteAsync(smoothed, ct);
            try { await Task.Delay(IntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
        _channel.Writer.TryComplete();
        _source.Dispose();
    }
}
