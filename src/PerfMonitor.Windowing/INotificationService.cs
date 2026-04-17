namespace PerfMonitor.Windowing;

/// <summary>
/// Abstraction over the system toast / tray balloon surface so UI layer
/// code doesn't have to know about the tray implementation.
/// </summary>
public interface INotificationService
{
    void Show(string title, string message);
}
