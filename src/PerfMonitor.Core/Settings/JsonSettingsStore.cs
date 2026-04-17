using System.Text.Json;

namespace PerfMonitor.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _dir;
    private readonly string _path;
    private readonly FileSystemWatcher? _watcher;

    public event EventHandler<AppSettings>? SettingsChanged;

    public JsonSettingsStore(string directory)
    {
        _dir = directory;
        _path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(_dir);
        _watcher = new FileSystemWatcher(_dir, "settings.json") { EnableRaisingEvents = true };
        _watcher.Changed += (_, _) => SettingsChanged?.Invoke(this, Load());
    }

    public AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            File.Copy(_path, _path + ".bak", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(_path, json);
        // Fire synchronously for same-process saves so UI can apply instantly.
        // FileSystemWatcher may double-fire — handlers must be idempotent.
        SettingsChanged?.Invoke(this, settings);
    }

    public void Dispose() => _watcher?.Dispose();
}
