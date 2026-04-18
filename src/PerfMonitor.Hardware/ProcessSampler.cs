using PerfMonitor.Core.Metrics;
using System.Diagnostics;

namespace PerfMonitor.Hardware;

// IProcessSampler lives in PerfMonitor.Core.Metrics so UI projects (which
// can't reference Hardware) can consume snapshots via the same contract.

/// <summary>
/// Enumerates running processes at a configurable interval, calculates
/// per-process CPU% via TotalProcessorTime deltas, and groups by process
/// name (so e.g. the dozens of svchost.exe instances aggregate to a
/// single row).
///
/// CPU% is a two-sample computation, so the first emitted list after
/// <see cref="Start"/> will have CpuPercent=0 for every process. The
/// second tick onward has real numbers.
/// </summary>
public sealed class ProcessSampler : IProcessSampler
{
    private readonly int _cpuCount = Math.Max(1, Environment.ProcessorCount);
    private readonly Dictionary<int, (TimeSpan cpu, DateTime ts)> _lastByPid = new();
    private readonly TimeSpan _interval;
    private readonly GpuEnginePerProcess _gpu = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event EventHandler<IReadOnlyList<ProcessSnapshot>>? Updated;
    public bool IsRunning => _cts is not null;

    public ProcessSampler(TimeSpan? interval = null)
    {
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
        _loop = null;
        _lastByPid.Clear();
    }

    public void Dispose() => Stop();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = Poll();
                Updated?.Invoke(this, snapshot);
            }
            catch
            {
                // Never let a transient enumeration failure end the loop;
                // next tick tries again.
            }
            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private IReadOnlyList<ProcessSnapshot> Poll()
    {
        var now = DateTime.UtcNow;
        var processes = Process.GetProcesses();
        // Snapshot GPU utilization by PID first; the dict may be empty on
        // pre-1903 Windows or where the PDH category is unavailable.
        var gpuByPid = _gpu.Read();
        try
        {
            // Per-name aggregation buckets.
            var grouped = new Dictionary<string, (int count, double cpu, long ws, double gpu)>(StringComparer.OrdinalIgnoreCase);
            var seenPids = new HashSet<int>(processes.Length);

            foreach (var p in processes)
            {
                try
                {
                    seenPids.Add(p.Id);
                    var name = p.ProcessName;
                    // Skip pseudo-processes we can't meaningfully report on.
                    if (name is "Idle" or "System" or "Registry" or "Memory Compression") continue;

                    TimeSpan currentCpu;
                    long ws;
                    try
                    {
                        currentCpu = p.TotalProcessorTime;
                        ws = p.WorkingSet64;
                    }
                    catch
                    {
                        // Access denied on cross-user processes when unelevated.
                        continue;
                    }

                    double cpuPct = 0;
                    if (_lastByPid.TryGetValue(p.Id, out var last))
                    {
                        var cpuDelta = (currentCpu - last.cpu).TotalMilliseconds;
                        var wallDelta = (now - last.ts).TotalMilliseconds;
                        if (wallDelta > 0)
                            cpuPct = (cpuDelta / wallDelta) / _cpuCount * 100.0;
                    }
                    _lastByPid[p.Id] = (currentCpu, now);

                    var gpuPct = gpuByPid.TryGetValue(p.Id, out var g) ? g : 0f;

                    if (grouped.TryGetValue(name, out var existing))
                    {
                        grouped[name] = (existing.count + 1, existing.cpu + cpuPct, existing.ws + ws, existing.gpu + gpuPct);
                    }
                    else
                    {
                        grouped[name] = (1, cpuPct, ws, gpuPct);
                    }
                }
                finally { p.Dispose(); }
            }

            // Prune stale PIDs to keep the dictionary from growing unboundedly.
            if (_lastByPid.Count > seenPids.Count * 2)
            {
                foreach (var pid in _lastByPid.Keys.Where(k => !seenPids.Contains(k)).ToList())
                    _lastByPid.Remove(pid);
            }

            var list = new List<ProcessSnapshot>(grouped.Count);
            foreach (var kv in grouped)
                list.Add(new ProcessSnapshot(
                    Name: kv.Key,
                    Count: kv.Value.count,
                    CpuPercent: (float)Math.Min(kv.Value.cpu, 100.0 * _cpuCount),
                    WorkingSetBytes: kv.Value.ws,
                    GpuPercent: (float)Math.Min(kv.Value.gpu, 100.0)));
            return list;
        }
        finally
        {
            // Safety: if an exception escaped the loop above, still dispose any
            // processes we didn't reach.
            foreach (var p in processes) { try { p.Dispose(); } catch { } }
        }
    }
}
