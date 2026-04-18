namespace PerfMonitor.Core.Metrics;

/// <summary>
/// Publishes periodic per-process snapshots. Consumers subscribe via
/// <see cref="Updated"/> and call <see cref="Start"/> when they need
/// data (e.g. when the widget's detail panel opens) and <see cref="Stop"/>
/// when they don't.
/// </summary>
public interface IProcessSampler : IDisposable
{
    event EventHandler<IReadOnlyList<ProcessSnapshot>>? Updated;
    void Start();
    void Stop();
    bool IsRunning { get; }
}
