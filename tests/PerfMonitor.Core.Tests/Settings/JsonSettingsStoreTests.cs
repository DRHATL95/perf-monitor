using FluentAssertions;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.Core.Tests.Settings;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pm-test-{Guid.NewGuid():N}");

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(_dir);
        var s = store.Load();
        s.Display.Mode.Should().Be(DisplayMode.Floating);
        s.Display.RefreshIntervalMs.Should().Be(1000);
        s.Tray.Enabled.Should().BeTrue();
        s.Behavior.StartWithWindows.Should().BeTrue();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllProperties()
    {
        var store = new JsonSettingsStore(_dir);
        var original = new AppSettings
        {
            Display = new DisplaySettings { Mode = DisplayMode.DockedTop, Opacity = 0.5, RefreshIntervalMs = 500 }
        };
        store.Save(original);
        var loaded = store.Load();
        loaded.Display.Mode.Should().Be(DisplayMode.DockedTop);
        loaded.Display.Opacity.Should().Be(0.5);
        loaded.Display.RefreshIntervalMs.Should().Be(500);
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{{not-json");
        var store = new JsonSettingsStore(_dir);
        var s = store.Load();
        s.Display.Mode.Should().Be(DisplayMode.Floating);
        File.Exists(Path.Combine(_dir, "settings.json.bak")).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
