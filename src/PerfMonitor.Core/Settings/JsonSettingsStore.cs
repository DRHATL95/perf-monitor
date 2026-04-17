using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerfMonitor.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Serialize enums as strings ("Floating") rather than integers so renaming
        // or removing enum members doesn't silently corrupt saved files.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
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
        _watcher.Changed += OnWatcherChanged;
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        // Threadpool-invoked — any exception here terminates the process on .NET Core.
        // Guard and log rather than propagate.
        try { SettingsChanged?.Invoke(this, Load()); }
        catch { /* swallow — external edit races are non-fatal */ }
    }

    public AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();

        // Retry on sharing violations — FileSystemWatcher can fire Changed
        // while a concurrent writer still has the handle open.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                return Sanitize(loaded);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(25);
            }
            catch (JsonException)
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
                return new AppSettings();
            }
        }
        // Exhausted retries — keep the app up with current defaults.
        return new AppSettings();
    }

    /// <summary>
    /// Coerces any values loaded from older settings files that no longer map
    /// to the current enum / range definitions. Keeps the app starting even
    /// when a stale settings.json references removed options.
    /// </summary>
    private static AppSettings Sanitize(AppSettings s)
    {
        var mode = Enum.IsDefined(s.Display.Mode) ? s.Display.Mode : DisplayMode.Floating;
        return s with
        {
            Display = s.Display with { Mode = mode }
        };
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, Options);
        // Suppress the watcher during our own writes so its Changed handler
        // can't race against the still-open write handle.
        if (_watcher is not null) _watcher.EnableRaisingEvents = false;
        try
        {
            File.WriteAllText(_path, json);
        }
        finally
        {
            if (_watcher is not null) _watcher.EnableRaisingEvents = true;
        }
        // Fire synchronously for same-process saves so UI can apply instantly.
        SettingsChanged?.Invoke(this, settings);
    }

    public void Dispose() => _watcher?.Dispose();
}
