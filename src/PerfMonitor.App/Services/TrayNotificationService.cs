using PerfMonitor.Tray;
using PerfMonitor.Windowing;

namespace PerfMonitor.App.Services;

/// <summary>
/// Adapts <see cref="TrayIconHost"/> to the Windowing-layer
/// <see cref="INotificationService"/> contract.
/// </summary>
internal sealed class TrayNotificationService : INotificationService
{
    private readonly TrayIconHost _tray;

    public TrayNotificationService(TrayIconHost tray) => _tray = tray;

    public void Show(string title, string message) =>
        _tray.ShowNotification(title, message);
}
