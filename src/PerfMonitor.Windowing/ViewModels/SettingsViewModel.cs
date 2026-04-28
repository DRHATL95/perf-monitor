using CommunityToolkit.Mvvm.ComponentModel;
using PerfMonitor.Core.Settings;
using System.Reflection;

namespace PerfMonitor.Windowing.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    /// <summary>
    /// Human-friendly version string (e.g. "0.1.6"). Read once at
    /// construction from the executing assembly's InformationalVersion
    /// (or AssemblyVersion as fallback). Stamped by CI from the git tag.
    /// </summary>
    public string AppVersion { get; }

    /// <summary>
    /// Direct URL to this version's release page on GitHub. Bound to a
    /// Hyperlink in the About tab so the user can jump straight to the
    /// release notes for the build they're running.
    /// </summary>
    public string ReleaseUrl => $"https://github.com/DRHATL95/perf-monitor/releases/tag/v{AppVersion}";

    [ObservableProperty] private DisplayMode mode;
    [ObservableProperty] private double opacity;
    [ObservableProperty] private int refreshIntervalMs;
    [ObservableProperty] private bool autoHideOnFullscreen;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool clickThroughByDefault;
    [ObservableProperty] private float cpuWarnPercent;
    [ObservableProperty] private float cpuCritPercent;
    [ObservableProperty] private float cpuTempWarnC;
    [ObservableProperty] private float cpuTempCritC;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        AppVersion = ResolveVersion();
        var s = store.Load();
        Mode = s.Display.Mode;
        Opacity = s.Display.Opacity;
        RefreshIntervalMs = s.Display.RefreshIntervalMs;
        AutoHideOnFullscreen = s.Behavior.AutoHideOnFullscreen;
        StartWithWindows = s.Behavior.StartWithWindows;
        ClickThroughByDefault = s.Behavior.ClickThroughByDefault;
        CpuWarnPercent = s.Thresholds.CpuWarnPercent;
        CpuCritPercent = s.Thresholds.CpuCritPercent;
        CpuTempWarnC = s.Thresholds.CpuTempWarnC;
        CpuTempCritC = s.Thresholds.CpuTempCritC;
    }

    public void Apply()
    {
        var current = _store.Load();
        var updated = current with
        {
            Display = current.Display with { Mode = Mode, Opacity = Opacity, RefreshIntervalMs = RefreshIntervalMs },
            Behavior = current.Behavior with
            {
                AutoHideOnFullscreen = AutoHideOnFullscreen,
                StartWithWindows = StartWithWindows,
                ClickThroughByDefault = ClickThroughByDefault
            },
            Thresholds = current.Thresholds with
            {
                CpuWarnPercent = CpuWarnPercent,
                CpuCritPercent = CpuCritPercent,
                CpuTempWarnC = CpuTempWarnC,
                CpuTempCritC = CpuTempCritC
            }
        };
        _store.Save(updated);
    }

    /// <summary>
    /// Pull the version off the entry assembly (App), preferring
    /// InformationalVersion (preserves -beta/-rc suffixes if present) and
    /// falling back to AssemblyVersion. Strips trailing ".0" so a stamped
    /// 0.1.6.0 displays as the cleaner "0.1.6".
    /// </summary>
    private static string ResolveVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // InformationalVersion sometimes includes a "+commitsha" suffix
            // from SourceLink — strip it for display.
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        var v = asm.GetName().Version;
        if (v is null) return "unknown";
        return v.Build > 0
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : $"{v.Major}.{v.Minor}";
    }
}
