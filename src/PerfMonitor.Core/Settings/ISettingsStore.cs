namespace PerfMonitor.Core.Settings;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
    event EventHandler<AppSettings>? SettingsChanged;
}
