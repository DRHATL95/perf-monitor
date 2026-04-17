using System.IO;
using System.Text.Json;

namespace PerfMonitor.Windowing.Theming;

public static class ThemeLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Theme FromJson(string json) =>
        JsonSerializer.Deserialize<Theme>(json, Options)
            ?? throw new InvalidOperationException("Theme JSON returned null");

    public static Theme FromFile(string path) => FromJson(File.ReadAllText(path));
}
