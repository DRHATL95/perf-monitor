using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Core.Settings;

public enum DisplayMode
{
    /// <summary>Draggable widget. Respects auto-hide-on-fullscreen.</summary>
    Floating,
    /// <summary>Same widget, always-on-top with periodic topmost re-assert. Never fades.</summary>
    OnTop
}

public record Position(int X, int Y);

public record DisplaySettings
{
    public DisplayMode Mode { get; init; } = DisplayMode.Floating;
    public Position Position { get; init; } = new(20, 20);
    public double Opacity { get; init; } = 0.85;
    public int RefreshIntervalMs { get; init; } = 1000;
    public string Theme { get; init; } = "Glass";
    public string[] ShowPills { get; init; } = ["Cpu", "Ram", "Gpu", "Net"];

    /// <summary>
    /// The metric whose process-detail panel was open at last close, or
    /// null if the panel was collapsed. Restored on launch so users
    /// monitoring a specific category don't have to re-open the panel
    /// every session.
    /// </summary>
    public ProcessMetric? LastExpandedMetric { get; init; } = null;
}

public record TraySettings
{
    public bool Enabled { get; init; } = true;
    public string[] Icons { get; init; } = ["Cpu", "Ram", "Gpu", "CpuTemp"];
}

public record BehaviorSettings
{
    public bool AutoHideOnFullscreen { get; init; } = true;
    public string[] FullscreenWhitelist { get; init; } = [];
    public string ClickThroughHotkey { get; init; } = "Ctrl+Alt+M";
    public bool StartWithWindows { get; init; } = true;
    /// <summary>
    /// When true, the widget starts click-through (mouse events pass through
    /// to whatever is underneath). OnTop mode forces this on regardless.
    /// Ctrl+Alt+M still toggles live.
    /// </summary>
    public bool ClickThroughByDefault { get; init; } = false;
}

public record ThresholdSettings
{
    public float CpuWarnPercent { get; init; } = 85f;
    public float CpuCritPercent { get; init; } = 95f;
    public float CpuTempWarnC { get; init; } = 80f;
    public float CpuTempCritC { get; init; } = 90f;
    public float GpuTempWarnC { get; init; } = 80f;
    public float GpuTempCritC { get; init; } = 88f;
}

public record AppSettings
{
    public DisplaySettings Display { get; init; } = new();
    public TraySettings Tray { get; init; } = new();
    public BehaviorSettings Behavior { get; init; } = new();
    public ThresholdSettings Thresholds { get; init; } = new();
}
