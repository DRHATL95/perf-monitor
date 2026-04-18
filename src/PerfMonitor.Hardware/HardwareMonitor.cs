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

    public int IntervalMs { get; private set; }
    public ChannelReader<MetricsSnapshot> Reader => _channel.Reader;

    public void SetInterval(int ms) => IntervalMs = Math.Clamp(ms, 250, 5000);

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
            try
            {
                var raw = _source.Poll();
                var smoothed = raw with
                {
                    NetDownMBps = _netDown.Smooth(raw.NetDownMBps),
                    NetUpMBps   = _netUp.Smooth(raw.NetUpMBps),
                    GpuLoadPercent = _gpuLoad.Smooth(raw.GpuLoadPercent)
                };
                await _channel.Writer.WriteAsync(smoothed, ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Transient poll/write failure — don't let it tear down the
                // loop. Next tick tries again.
            }

            try { await Task.Delay(IntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
        // Complete the channel so any consumer (MetricsSampler.await-foreach)
        // unblocks promptly instead of hanging on shutdown.
        _channel.Writer.TryComplete();
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        try { _cts?.Dispose(); } catch { }
        _channel.Writer.TryComplete();
        // Source.Dispose() has its own bounded timeout; no need to double-wrap.
        try { _source.Dispose(); } catch { }
    }
}
