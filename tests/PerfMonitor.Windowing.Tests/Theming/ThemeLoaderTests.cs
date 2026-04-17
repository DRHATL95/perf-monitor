using FluentAssertions;
using PerfMonitor.Windowing.Theming;

namespace PerfMonitor.Windowing.Tests.Theming;

public class ThemeLoaderTests
{
    private const string GlassJson = """
    {
      "name": "Glass",
      "palette": {
        "surface": "rgba(30, 30, 35, 0.65)",
        "accentCpu": "#ff8a5a",
        "accentRam": "#5ad0ff",
        "accentGpu": "#a58aff",
        "accentNet": "#5affaa",
        "accentPulse": "#00f0ff",
        "accentAlert": "#ff4d6d"
      },
      "geometry": { "pillRadius": 12, "containerRadius": 18, "pillGap": 6 }
    }
    """;

    [Fact]
    public void LoadFromJson_ParsesPaletteAndGeometry()
    {
        var theme = ThemeLoader.FromJson(GlassJson);
        theme.Name.Should().Be("Glass");
        theme.Palette.AccentCpu.Should().Be("#ff8a5a");
        theme.Geometry.PillRadius.Should().Be(12);
    }
}
