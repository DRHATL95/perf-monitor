namespace PerfMonitor.Windowing.Theming;

public record ThemePalette(
    string Surface,
    string AccentCpu,
    string AccentRam,
    string AccentGpu,
    string AccentNet,
    string AccentPulse,
    string AccentAlert);

public record ThemeGeometry(int PillRadius, int ContainerRadius, int PillGap);

public record Theme(string Name, ThemePalette Palette, ThemeGeometry Geometry);
