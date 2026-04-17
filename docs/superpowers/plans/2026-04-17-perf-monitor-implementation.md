# PerfMonitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a lightweight always-on desktop performance widget for Windows 11/10 (transparent glassmorphic pill-row + live tray icons) per the approved spec at [`docs/superpowers/specs/2026-04-17-perf-monitor-design.md`](../specs/2026-04-17-perf-monitor-design.md).

**Architecture:** .NET 8 / WPF single-process app. Three layers — UI (windows + tray), Services (sampler, detectors, storage), Hardware (LibreHardwareMonitorLib wrapper). One `MainViewModel` shared across all render paths. Sensor polling on dedicated background thread, snapshots published via `Channel<T>(capacity=1, DropOldest)` to UI dispatcher.

**Tech Stack:** .NET 8, WPF, LibreHardwareMonitorLib, H.NotifyIcon.Wpf, CommunityToolkit.Mvvm, Microsoft.Win32.TaskScheduler, Microsoft.Extensions.DependencyInjection, xUnit, FluentAssertions, CentralPackageManagement.

**Parallel Dispatch Graph:**
```
Phase 1 (Core) ──▶ Phase 2 (Hardware) ──┬──▶ Phase 3 (Widget) ──┬──▶ Phase 5 (Dock) ──┐
                                        │                       │                     │
                                        └──▶ Phase 4 (Tray) ────┤                     ├──▶ Phase 7 (Settings) ──▶ Phase 8 (Polish)
                                                                │                     │
                                                                └──▶ Phase 6 (Behav.)─┘
```
Phases 3⟂4 run in parallel after Phase 2. Phases 5⟂6 run in parallel after Phase 3.

---

## Phase 1 — Core Skeleton

### Task 1: Solution scaffold + Central Package Management

**Files:**
- Create: `PerfMonitor.sln`
- Create: `Directory.Packages.props`
- Create: `Directory.Build.props`
- Create: `.editorconfig`

- [ ] **Step 1: Create the solution file**

Run from `D:\Code\Projects\perf-monitor`:
```bash
dotnet new sln -n PerfMonitor
```
Expected: creates `PerfMonitor.sln` in repo root.

- [ ] **Step 2: Create `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.3.2" />
    <PackageVersion Include="FluentAssertions" Version="6.12.2" />
    <PackageVersion Include="H.NotifyIcon.Wpf" Version="2.2.0" />
    <PackageVersion Include="LibreHardwareMonitorLib" Version="0.9.4" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Debug" Version="8.0.1" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="TaskScheduler" Version="2.11.0" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors>nullable</WarningsAsErrors>
    <Version>0.1.0</Version>
    <Authors>David Howard</Authors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Create `.editorconfig`**

```ini
root = true
[*.{cs,xaml}]
indent_style = space
indent_size = 4
charset = utf-8
end_of_line = crlf
insert_final_newline = true
csharp_style_namespace_declarations = file_scoped:error
dotnet_diagnostic.CA1416.severity = none
```

- [ ] **Step 5: Commit**

```bash
git add PerfMonitor.sln Directory.Packages.props Directory.Build.props .editorconfig
git commit -m "chore: scaffold solution with central package management"
```

---

### Task 2: PerfMonitor.Core project + `MetricsSnapshot`

**Files:**
- Create: `src/PerfMonitor.Core/PerfMonitor.Core.csproj`
- Create: `src/PerfMonitor.Core/Metrics/MetricsSnapshot.cs`
- Create: `src/PerfMonitor.Core/Metrics/MetricsHealth.cs`
- Create: `src/PerfMonitor.Core/Metrics/FanReading.cs`
- Create: `tests/PerfMonitor.Core.Tests/PerfMonitor.Core.Tests.csproj`
- Create: `tests/PerfMonitor.Core.Tests/Metrics/MetricsSnapshotTests.cs`

- [ ] **Step 1: Create the Core project**

Run:
```bash
dotnet new classlib -n PerfMonitor.Core -o src/PerfMonitor.Core -f net8.0
dotnet sln add src/PerfMonitor.Core/PerfMonitor.Core.csproj
rm src/PerfMonitor.Core/Class1.cs
```

- [ ] **Step 2: Create the test project**

Run:
```bash
dotnet new xunit -n PerfMonitor.Core.Tests -o tests/PerfMonitor.Core.Tests -f net8.0
dotnet sln add tests/PerfMonitor.Core.Tests/PerfMonitor.Core.Tests.csproj
dotnet add tests/PerfMonitor.Core.Tests reference src/PerfMonitor.Core
dotnet add tests/PerfMonitor.Core.Tests package FluentAssertions
rm tests/PerfMonitor.Core.Tests/UnitTest1.cs
```

- [ ] **Step 3: Write the failing test**

Create `tests/PerfMonitor.Core.Tests/Metrics/MetricsSnapshotTests.cs`:
```csharp
using FluentAssertions;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Core.Tests.Metrics;

public class MetricsSnapshotTests
{
    [Fact]
    public void Snapshot_WithTempsUnavailable_HasNullCpuTemp()
    {
        var s = new MetricsSnapshot(
            Timestamp: DateTime.UtcNow,
            CpuLoadPercent: 42f,
            CpuTempC: null,
            RamUsedGb: 18.2f, RamTotalGb: 32f,
            GpuLoadPercent: 71f,
            GpuTempC: null,
            GpuVramUsedGb: 6f, GpuVramTotalGb: 12f,
            NetDownMBps: 4.2f, NetUpMBps: 0.8f,
            Fans: Array.Empty<FanReading>(),
            Health: MetricsHealth.TempsUnavailable);

        s.CpuTempC.Should().BeNull();
        s.Health.Should().Be(MetricsHealth.TempsUnavailable);
    }

    [Fact]
    public void Snapshot_IsImmutableRecord()
    {
        var a = new MetricsSnapshot(DateTime.UnixEpoch, 10, 50, 8, 16, 20, 55, 2, 8, 1, 0.5f, Array.Empty<FanReading>(), MetricsHealth.Ok);
        var b = a with { CpuLoadPercent = 15 };
        a.CpuLoadPercent.Should().Be(10);
        b.CpuLoadPercent.Should().Be(15);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/PerfMonitor.Core.Tests`
Expected: BUILD ERROR — `MetricsSnapshot`, `MetricsHealth`, `FanReading` not found.

- [ ] **Step 5: Implement the types**

Create `src/PerfMonitor.Core/Metrics/MetricsHealth.cs`:
```csharp
namespace PerfMonitor.Core.Metrics;

public enum MetricsHealth
{
    Ok,
    TempsUnavailable,
    SensorError
}
```

Create `src/PerfMonitor.Core/Metrics/FanReading.cs`:
```csharp
namespace PerfMonitor.Core.Metrics;

public record FanReading(string Name, int Rpm);
```

Create `src/PerfMonitor.Core/Metrics/MetricsSnapshot.cs`:
```csharp
namespace PerfMonitor.Core.Metrics;

public record MetricsSnapshot(
    DateTime Timestamp,
    float CpuLoadPercent,
    float? CpuTempC,
    float RamUsedGb,
    float RamTotalGb,
    float GpuLoadPercent,
    float? GpuTempC,
    float GpuVramUsedGb,
    float GpuVramTotalGb,
    float NetDownMBps,
    float NetUpMBps,
    IReadOnlyList<FanReading> Fans,
    MetricsHealth Health);
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/PerfMonitor.Core.Tests`
Expected: 2 passed.

- [ ] **Step 7: Commit**

```bash
git add src/PerfMonitor.Core tests/PerfMonitor.Core.Tests PerfMonitor.sln
git commit -m "feat(core): add MetricsSnapshot record with health enum"
```

---

### Task 3: `MainViewModel` with INPC + threshold classifier

**Files:**
- Create: `src/PerfMonitor.Core/ViewModels/MetricStatus.cs`
- Create: `src/PerfMonitor.Core/ViewModels/MainViewModel.cs`
- Create: `src/PerfMonitor.Core/PerfMonitor.Core.csproj` (modify: add CommunityToolkit.Mvvm)
- Create: `tests/PerfMonitor.Core.Tests/ViewModels/MainViewModelTests.cs`

- [ ] **Step 1: Add Mvvm package to Core**

Run: `dotnet add src/PerfMonitor.Core package CommunityToolkit.Mvvm`

- [ ] **Step 2: Write the failing test**

Create `tests/PerfMonitor.Core.Tests/ViewModels/MainViewModelTests.cs`:
```csharp
using FluentAssertions;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.ViewModels;
using System.ComponentModel;

namespace PerfMonitor.Core.Tests.ViewModels;

public class MainViewModelTests
{
    private static MetricsSnapshot Snap(float cpu = 10, float? cpuTemp = 50, float gpu = 20, float? gpuTemp = 55) =>
        new(DateTime.UnixEpoch, cpu, cpuTemp, 8, 16, gpu, gpuTemp, 2, 8, 1, 0.5f, Array.Empty<FanReading>(), MetricsHealth.Ok);

    [Fact]
    public void Apply_UpdatesCpuLoad_RaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Apply(Snap(cpu: 42));

        vm.CpuLoadPercent.Should().Be(42);
        raised.Should().Contain(nameof(MainViewModel.CpuLoadPercent));
    }

    [Fact]
    public void CpuStatus_AboveWarn_ReturnsWarn()
    {
        var vm = new MainViewModel { CpuWarnPercent = 85, CpuCritPercent = 95 };
        vm.Apply(Snap(cpu: 88));
        vm.CpuStatus.Should().Be(MetricStatus.Warn);
    }

    [Fact]
    public void CpuStatus_AboveCrit_ReturnsCrit()
    {
        var vm = new MainViewModel { CpuWarnPercent = 85, CpuCritPercent = 95 };
        vm.Apply(Snap(cpu: 97));
        vm.CpuStatus.Should().Be(MetricStatus.Crit);
    }

    [Fact]
    public void Apply_NullCpuTemp_SetsCpuTempUnavailable()
    {
        var vm = new MainViewModel();
        vm.Apply(Snap(cpuTemp: null));
        vm.CpuTempAvailable.Should().BeFalse();
        vm.CpuTempC.Should().Be(0);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/PerfMonitor.Core.Tests --filter FullyQualifiedName~MainViewModelTests`
Expected: BUILD ERROR — `MainViewModel`, `MetricStatus` not found.

- [ ] **Step 4: Implement `MetricStatus`**

Create `src/PerfMonitor.Core/ViewModels/MetricStatus.cs`:
```csharp
namespace PerfMonitor.Core.ViewModels;

public enum MetricStatus
{
    Ok,
    Warn,
    Crit
}
```

- [ ] **Step 5: Implement `MainViewModel`**

Create `src/PerfMonitor.Core/ViewModels/MainViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Core.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private float cpuLoadPercent;
    [ObservableProperty] private float cpuTempC;
    [ObservableProperty] private bool cpuTempAvailable;
    [ObservableProperty] private float ramUsedGb;
    [ObservableProperty] private float ramTotalGb;
    [ObservableProperty] private float gpuLoadPercent;
    [ObservableProperty] private float gpuTempC;
    [ObservableProperty] private bool gpuTempAvailable;
    [ObservableProperty] private float gpuVramUsedGb;
    [ObservableProperty] private float gpuVramTotalGb;
    [ObservableProperty] private float netDownMBps;
    [ObservableProperty] private float netUpMBps;
    [ObservableProperty] private MetricsHealth health;
    [ObservableProperty] private MetricStatus cpuStatus;
    [ObservableProperty] private MetricStatus cpuTempStatus;
    [ObservableProperty] private MetricStatus gpuTempStatus;

    public float CpuWarnPercent { get; init; } = 85f;
    public float CpuCritPercent { get; init; } = 95f;
    public float CpuTempWarnC { get; init; } = 80f;
    public float CpuTempCritC { get; init; } = 90f;
    public float GpuTempWarnC { get; init; } = 80f;
    public float GpuTempCritC { get; init; } = 88f;

    public void Apply(MetricsSnapshot s)
    {
        CpuLoadPercent    = s.CpuLoadPercent;
        CpuTempAvailable  = s.CpuTempC.HasValue;
        CpuTempC          = s.CpuTempC ?? 0f;
        RamUsedGb         = s.RamUsedGb;
        RamTotalGb        = s.RamTotalGb;
        GpuLoadPercent    = s.GpuLoadPercent;
        GpuTempAvailable  = s.GpuTempC.HasValue;
        GpuTempC          = s.GpuTempC ?? 0f;
        GpuVramUsedGb     = s.GpuVramUsedGb;
        GpuVramTotalGb    = s.GpuVramTotalGb;
        NetDownMBps       = s.NetDownMBps;
        NetUpMBps         = s.NetUpMBps;
        Health            = s.Health;
        CpuStatus         = Classify(s.CpuLoadPercent, CpuWarnPercent, CpuCritPercent);
        CpuTempStatus     = s.CpuTempC is { } ct ? Classify(ct, CpuTempWarnC, CpuTempCritC) : MetricStatus.Ok;
        GpuTempStatus     = s.GpuTempC is { } gt ? Classify(gt, GpuTempWarnC, GpuTempCritC) : MetricStatus.Ok;
    }

    private static MetricStatus Classify(float value, float warn, float crit) =>
        value >= crit ? MetricStatus.Crit :
        value >= warn ? MetricStatus.Warn : MetricStatus.Ok;
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/PerfMonitor.Core.Tests --filter FullyQualifiedName~MainViewModelTests`
Expected: 4 passed.

- [ ] **Step 7: Commit**

```bash
git add src/PerfMonitor.Core tests/PerfMonitor.Core.Tests
git commit -m "feat(core): add MainViewModel with INPC and threshold classifier"
```

---

### Task 4: Settings model + JSON `SettingsStore`

**Files:**
- Create: `src/PerfMonitor.Core/Settings/AppSettings.cs`
- Create: `src/PerfMonitor.Core/Settings/ISettingsStore.cs`
- Create: `src/PerfMonitor.Core/Settings/JsonSettingsStore.cs`
- Create: `tests/PerfMonitor.Core.Tests/Settings/JsonSettingsStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/PerfMonitor.Core.Tests/Settings/JsonSettingsStoreTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PerfMonitor.Core.Tests --filter FullyQualifiedName~JsonSettingsStoreTests`
Expected: BUILD ERROR — types not found.

- [ ] **Step 3: Implement `AppSettings`**

Create `src/PerfMonitor.Core/Settings/AppSettings.cs`:
```csharp
namespace PerfMonitor.Core.Settings;

public enum DisplayMode { Floating, DockedTop, DockedBottom, DockedLeft, DockedRight }

public record Position(int X, int Y);

public record DisplaySettings
{
    public DisplayMode Mode { get; init; } = DisplayMode.Floating;
    public Position Position { get; init; } = new(20, 20);
    public double Opacity { get; init; } = 0.85;
    public int RefreshIntervalMs { get; init; } = 1000;
    public string Theme { get; init; } = "Glass";
    public string[] ShowPills { get; init; } = ["Cpu", "Ram", "Gpu", "Net"];
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
```

- [ ] **Step 4: Implement `ISettingsStore`**

Create `src/PerfMonitor.Core/Settings/ISettingsStore.cs`:
```csharp
namespace PerfMonitor.Core.Settings;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
    event EventHandler<AppSettings>? SettingsChanged;
}
```

- [ ] **Step 5: Implement `JsonSettingsStore`**

Create `src/PerfMonitor.Core/Settings/JsonSettingsStore.cs`:
```csharp
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
    }

    public void Dispose() => _watcher?.Dispose();
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/PerfMonitor.Core.Tests --filter FullyQualifiedName~JsonSettingsStoreTests`
Expected: 3 passed.

- [ ] **Step 7: Commit**

```bash
git add src/PerfMonitor.Core tests/PerfMonitor.Core.Tests
git commit -m "feat(core): add AppSettings model and JsonSettingsStore"
```

---

### Task 5: PerfMonitor.App WPF project + DI composition

**Files:**
- Create: `src/PerfMonitor.App/PerfMonitor.App.csproj`
- Create: `src/PerfMonitor.App/App.xaml`
- Create: `src/PerfMonitor.App/App.xaml.cs`
- Create: `src/PerfMonitor.App/app.manifest`
- Modify: `PerfMonitor.sln` (add project)

- [ ] **Step 1: Create the WPF project**

Run:
```bash
dotnet new wpf -n PerfMonitor.App -o src/PerfMonitor.App -f net8.0
dotnet sln add src/PerfMonitor.App/PerfMonitor.App.csproj
dotnet add src/PerfMonitor.App reference src/PerfMonitor.Core
dotnet add src/PerfMonitor.App package Microsoft.Extensions.Hosting
dotnet add src/PerfMonitor.App package Microsoft.Extensions.Logging.Debug
rm src/PerfMonitor.App/MainWindow.xaml src/PerfMonitor.App/MainWindow.xaml.cs
```

- [ ] **Step 2: Set target to `net8.0-windows` and add manifest**

Edit `src/PerfMonitor.App/PerfMonitor.App.csproj` — replace contents with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>
    <AssemblyName>PerfMonitor</AssemblyName>
    <StartupObject>PerfMonitor.App.Program</StartupObject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\PerfMonitor.Core\PerfMonitor.Core.csproj" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Logging.Debug" />
  </ItemGroup>
  <ItemGroup>
    <Resource Include="Assets\app-icon.svg" />
    <Resource Include="Assets\themes\glass.json" />
    <None Update="Assets\app-icon.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `app.manifest`**

Create `src/PerfMonitor.App/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="0.1.0.0" name="PerfMonitor.App"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
      <!-- Windows 11 -->
      <supportedOS Id="{54ca1ea1-b343-43fe-b5f2-13a9f5ab5b9a}"/>
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 4: Rewrite `App.xaml`**

Replace `src/PerfMonitor.App/App.xaml` with:
```xml
<Application x:Class="PerfMonitor.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
  <Application.Resources/>
</Application>
```

- [ ] **Step 5: Create `App.xaml.cs` and a `Program` entry point**

Replace `src/PerfMonitor.App/App.xaml.cs` with:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.ViewModels;
using System.IO;
using System.Windows;

namespace PerfMonitor.App;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services => _host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PerfMonitor");

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(lb => lb.AddDebug())
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(appDataDir));
                services.AddSingleton<MainViewModel>(sp =>
                {
                    var s = sp.GetRequiredService<ISettingsStore>().Load();
                    return new MainViewModel
                    {
                        CpuWarnPercent = s.Thresholds.CpuWarnPercent,
                        CpuCritPercent = s.Thresholds.CpuCritPercent,
                        CpuTempWarnC   = s.Thresholds.CpuTempWarnC,
                        CpuTempCritC   = s.Thresholds.CpuTempCritC,
                        GpuTempWarnC   = s.Thresholds.GpuTempWarnC,
                        GpuTempCritC   = s.Thresholds.GpuTempCritC
                    };
                });
            })
            .Build();

        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        base.OnExit(e);
    }
}

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
```

- [ ] **Step 6: Remove the `<ApplicationIcon>` line until Task 23 generates the real `.ico`**

Edit `src/PerfMonitor.App/PerfMonitor.App.csproj` and delete this line:
```xml
<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>
```

Task 23 will generate the real multi-resolution `.ico` from the SVG and re-add the line.

- [ ] **Step 7: Build to verify**

Run: `dotnet build`
Expected: BUILD SUCCEEDED, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/PerfMonitor.App PerfMonitor.sln
git commit -m "feat(app): scaffold WPF app with DI host and manifest"
```

---

## Phase 2 — Real Hardware

### Task 6: `PerfMonitor.Hardware` project + `IHardwareSource` abstraction

**Files:**
- Create: `src/PerfMonitor.Hardware/PerfMonitor.Hardware.csproj`
- Create: `src/PerfMonitor.Hardware/IHardwareSource.cs`
- Create: `src/PerfMonitor.Hardware/FakeHardwareSource.cs`
- Create: `tests/PerfMonitor.Hardware.Tests/PerfMonitor.Hardware.Tests.csproj`
- Create: `tests/PerfMonitor.Hardware.Tests/FakeHardwareSourceTests.cs`

- [ ] **Step 1: Scaffold projects**

Run:
```bash
dotnet new classlib -n PerfMonitor.Hardware -o src/PerfMonitor.Hardware -f net8.0
dotnet sln add src/PerfMonitor.Hardware/PerfMonitor.Hardware.csproj
dotnet add src/PerfMonitor.Hardware reference src/PerfMonitor.Core
rm src/PerfMonitor.Hardware/Class1.cs

dotnet new xunit -n PerfMonitor.Hardware.Tests -o tests/PerfMonitor.Hardware.Tests -f net8.0
dotnet sln add tests/PerfMonitor.Hardware.Tests/PerfMonitor.Hardware.Tests.csproj
dotnet add tests/PerfMonitor.Hardware.Tests reference src/PerfMonitor.Hardware
dotnet add tests/PerfMonitor.Hardware.Tests package FluentAssertions
rm tests/PerfMonitor.Hardware.Tests/UnitTest1.cs
```

- [ ] **Step 2: Write the failing test**

Create `tests/PerfMonitor.Hardware.Tests/FakeHardwareSourceTests.cs`:
```csharp
using FluentAssertions;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Hardware;

namespace PerfMonitor.Hardware.Tests;

public class FakeHardwareSourceTests
{
    [Fact]
    public void Poll_ReturnsDeterministicSnapshot()
    {
        var src = new FakeHardwareSource(seed: 42);
        var s = src.Poll();
        s.CpuLoadPercent.Should().BeInRange(0, 100);
        s.RamTotalGb.Should().Be(32);
        s.Health.Should().Be(MetricsHealth.Ok);
    }

    [Fact]
    public void Poll_WithTempsDisabled_ReturnsNullTemps()
    {
        var src = new FakeHardwareSource(seed: 42, tempsAvailable: false);
        var s = src.Poll();
        s.CpuTempC.Should().BeNull();
        s.GpuTempC.Should().BeNull();
        s.Health.Should().Be(MetricsHealth.TempsUnavailable);
    }
}
```

- [ ] **Step 3: Implement `IHardwareSource`**

Create `src/PerfMonitor.Hardware/IHardwareSource.cs`:
```csharp
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Hardware;

public interface IHardwareSource : IDisposable
{
    MetricsSnapshot Poll();
    bool TempsAvailable { get; }
}
```

- [ ] **Step 4: Implement `FakeHardwareSource`**

Create `src/PerfMonitor.Hardware/FakeHardwareSource.cs`:
```csharp
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Hardware;

public sealed class FakeHardwareSource : IHardwareSource
{
    private readonly Random _rng;
    private readonly bool _tempsAvailable;

    public FakeHardwareSource(int seed = 0, bool tempsAvailable = true)
    {
        _rng = new Random(seed);
        _tempsAvailable = tempsAvailable;
    }

    public bool TempsAvailable => _tempsAvailable;

    public MetricsSnapshot Poll() => new(
        Timestamp: DateTime.UtcNow,
        CpuLoadPercent: (float)(_rng.NextDouble() * 100),
        CpuTempC: _tempsAvailable ? 40f + (float)(_rng.NextDouble() * 40) : null,
        RamUsedGb: 8f + (float)(_rng.NextDouble() * 16),
        RamTotalGb: 32f,
        GpuLoadPercent: (float)(_rng.NextDouble() * 100),
        GpuTempC: _tempsAvailable ? 45f + (float)(_rng.NextDouble() * 30) : null,
        GpuVramUsedGb: (float)(_rng.NextDouble() * 12),
        GpuVramTotalGb: 12f,
        NetDownMBps: (float)(_rng.NextDouble() * 50),
        NetUpMBps: (float)(_rng.NextDouble() * 5),
        Fans: Array.Empty<FanReading>(),
        Health: _tempsAvailable ? MetricsHealth.Ok : MetricsHealth.TempsUnavailable);

    public void Dispose() { }
}
```

- [ ] **Step 5: Run tests to verify**

Run: `dotnet test tests/PerfMonitor.Hardware.Tests`
Expected: 2 passed.

- [ ] **Step 6: Commit**

```bash
git add src/PerfMonitor.Hardware tests/PerfMonitor.Hardware.Tests PerfMonitor.sln
git commit -m "feat(hardware): add IHardwareSource with FakeHardwareSource"
```

---

### Task 7: `HardwareMonitor` service with background thread + Channel + EMA

**Files:**
- Create: `src/PerfMonitor.Hardware/EmaSmoother.cs`
- Create: `src/PerfMonitor.Hardware/HardwareMonitor.cs`
- Create: `tests/PerfMonitor.Hardware.Tests/EmaSmootherTests.cs`
- Create: `tests/PerfMonitor.Hardware.Tests/HardwareMonitorTests.cs`

- [ ] **Step 1: Write the EMA test**

Create `tests/PerfMonitor.Hardware.Tests/EmaSmootherTests.cs`:
```csharp
using FluentAssertions;
using PerfMonitor.Hardware;

namespace PerfMonitor.Hardware.Tests;

public class EmaSmootherTests
{
    [Fact]
    public void Smooth_FirstSample_ReturnsSampleUnchanged()
    {
        var ema = new EmaSmoother(alpha: 0.3f);
        ema.Smooth(10f).Should().Be(10f);
    }

    [Fact]
    public void Smooth_SecondSample_BlendsByAlpha()
    {
        var ema = new EmaSmoother(alpha: 0.5f);
        ema.Smooth(10f);
        ema.Smooth(20f).Should().Be(15f);
    }
}
```

- [ ] **Step 2: Implement `EmaSmoother`**

Create `src/PerfMonitor.Hardware/EmaSmoother.cs`:
```csharp
namespace PerfMonitor.Hardware;

public sealed class EmaSmoother
{
    private readonly float _alpha;
    private float? _value;

    public EmaSmoother(float alpha) => _alpha = alpha;

    public float Smooth(float sample)
    {
        _value = _value is null ? sample : _alpha * sample + (1 - _alpha) * _value;
        return _value.Value;
    }
}
```

- [ ] **Step 3: Write the HardwareMonitor test**

Create `tests/PerfMonitor.Hardware.Tests/HardwareMonitorTests.cs`:
```csharp
using FluentAssertions;
using PerfMonitor.Hardware;

namespace PerfMonitor.Hardware.Tests;

public class HardwareMonitorTests
{
    [Fact]
    public async Task Start_PublishesSnapshotsToReader()
    {
        using var src = new FakeHardwareSource(seed: 1);
        using var mon = new HardwareMonitor(src, intervalMs: 20);
        mon.Start();

        var reader = mon.Reader;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var snap = await reader.ReadAsync(cts.Token);
        snap.Should().NotBeNull();
        snap.CpuLoadPercent.Should().BeInRange(0, 100);
    }

    [Fact]
    public void IntervalMs_BelowMin_ClampsTo250()
    {
        using var src = new FakeHardwareSource();
        using var mon = new HardwareMonitor(src, intervalMs: 10);
        mon.IntervalMs.Should().Be(250);
    }
}
```

- [ ] **Step 4: Implement `HardwareMonitor`**

Create `src/PerfMonitor.Hardware/HardwareMonitor.cs`:
```csharp
using PerfMonitor.Core.Metrics;
using System.Threading.Channels;

namespace PerfMonitor.Hardware;

public sealed class HardwareMonitor : IDisposable
{
    private readonly IHardwareSource _source;
    private readonly Channel<MetricsSnapshot> _channel;
    private readonly EmaSmoother _netDown = new(0.3f);
    private readonly EmaSmoother _netUp = new(0.3f);
    private readonly EmaSmoother _gpuLoad = new(0.3f);
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int IntervalMs { get; }
    public ChannelReader<MetricsSnapshot> Reader => _channel.Reader;

    public HardwareMonitor(IHardwareSource source, int intervalMs = 1000)
    {
        _source = source;
        IntervalMs = Math.Clamp(intervalMs, 250, 5000);
        _channel = Channel.CreateBounded<MetricsSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Factory.StartNew(() => LoopAsync(_cts.Token),
            TaskCreationOptions.LongRunning).Unwrap();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var raw = _source.Poll();
            var smoothed = raw with
            {
                NetDownMBps = _netDown.Smooth(raw.NetDownMBps),
                NetUpMBps   = _netUp.Smooth(raw.NetUpMBps),
                GpuLoadPercent = _gpuLoad.Smooth(raw.GpuLoadPercent)
            };
            await _channel.Writer.WriteAsync(smoothed, ct);
            try { await Task.Delay(IntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
        _channel.Writer.TryComplete();
        _source.Dispose();
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/PerfMonitor.Hardware.Tests`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add src/PerfMonitor.Hardware tests/PerfMonitor.Hardware.Tests
git commit -m "feat(hardware): add HardwareMonitor with bounded channel and EMA smoothing"
```

---

### Task 8: LibreHardwareMonitor real integration + elevation detection

**Files:**
- Create: `src/PerfMonitor.Hardware/LhmHardwareSource.cs`
- Create: `src/PerfMonitor.Hardware/ElevationCheck.cs`
- Modify: `src/PerfMonitor.Hardware/PerfMonitor.Hardware.csproj` (add LibreHardwareMonitorLib)

- [ ] **Step 1: Add package**

Run: `dotnet add src/PerfMonitor.Hardware package LibreHardwareMonitorLib`

- [ ] **Step 2: Implement `ElevationCheck`**

Create `src/PerfMonitor.Hardware/ElevationCheck.cs`:
```csharp
using System.Security.Principal;

namespace PerfMonitor.Hardware;

public static class ElevationCheck
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
```

- [ ] **Step 3: Implement `LhmHardwareSource`**

Create `src/PerfMonitor.Hardware/LhmHardwareSource.cs`:
```csharp
using LibreHardwareMonitor.Hardware;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Hardware;

public sealed class LhmHardwareSource : IHardwareSource
{
    private readonly Computer _computer;
    private readonly bool _elevated;

    public bool TempsAvailable => _elevated;

    public LhmHardwareSource()
    {
        _elevated = ElevationCheck.IsElevated();
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsNetworkEnabled = true,
            IsMotherboardEnabled = _elevated,
            IsControllerEnabled = _elevated
        };
        _computer.Open();
    }

    public MetricsSnapshot Poll()
    {
        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();
        }

        float cpuLoad = 0, gpuLoad = 0, gpuVramUsed = 0, gpuVramTotal = 0, netDown = 0, netUp = 0;
        float? cpuTemp = null, gpuTemp = null;
        float ramUsed = 0, ramTotal = 0;
        var fans = new List<FanReading>();

        foreach (var hw in _computer.Hardware)
        {
            foreach (var sensor in hw.Sensors)
            {
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        cpuLoad = sensor.Value ?? 0;
                    if (_elevated && sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        cpuTemp = sensor.Value;
                }
                else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        gpuLoad = sensor.Value ?? 0;
                    if (_elevated && sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        gpuTemp = sensor.Value;
                    if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase))
                        gpuVramUsed = (sensor.Value ?? 0) / 1024f;
                    if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        gpuVramTotal = (sensor.Value ?? 0) / 1024f;
                }
                else if (hw.HardwareType == HardwareType.Memory)
                {
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Used"))
                        ramUsed = sensor.Value ?? 0;
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Available"))
                        ramTotal = ramUsed + (sensor.Value ?? 0);
                }
                else if (hw.HardwareType == HardwareType.Network)
                {
                    if (sensor.SensorType == SensorType.Throughput && sensor.Name.Contains("Download"))
                        netDown += (sensor.Value ?? 0) / (1024f * 1024f);
                    if (sensor.SensorType == SensorType.Throughput && sensor.Name.Contains("Upload"))
                        netUp += (sensor.Value ?? 0) / (1024f * 1024f);
                }
                if (sensor.SensorType == SensorType.Fan)
                    fans.Add(new FanReading(sensor.Name, (int)(sensor.Value ?? 0)));
            }
        }

        return new MetricsSnapshot(
            DateTime.UtcNow, cpuLoad, cpuTemp, ramUsed, ramTotal,
            gpuLoad, gpuTemp, gpuVramUsed, gpuVramTotal,
            netDown, netUp, fans,
            _elevated ? MetricsHealth.Ok : MetricsHealth.TempsUnavailable);
    }

    public void Dispose() => _computer.Close();
}
```

- [ ] **Step 4: Register in DI (modify `App.xaml.cs`)**

In `src/PerfMonitor.App/App.xaml.cs`, add to the `ConfigureServices` lambda after the existing registrations:
```csharp
// LhmHardwareSource handles both elevated and unelevated cases internally
// (returns null temps + Health = TempsUnavailable when unelevated).
services.AddSingleton<IHardwareSource>(_ => new LhmHardwareSource());
services.AddSingleton<HardwareMonitor>(sp =>
{
    var src = sp.GetRequiredService<IHardwareSource>();
    var settings = sp.GetRequiredService<ISettingsStore>().Load();
    return new HardwareMonitor(src, settings.Display.RefreshIntervalMs);
});
```

Add these using directives to the top:
```csharp
using PerfMonitor.Hardware;
```

Add a project reference:
```bash
dotnet add src/PerfMonitor.App reference src/PerfMonitor.Hardware
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/PerfMonitor.Hardware src/PerfMonitor.App
git commit -m "feat(hardware): add LibreHardwareMonitor integration with elevation detection"
```

---

## Phase 3 — Floating Widget (⟂ Phase 4)

### Task 9: `PerfMonitor.Windowing` project + `ThemeLoader`

**Files:**
- Create: `src/PerfMonitor.Windowing/PerfMonitor.Windowing.csproj`
- Create: `src/PerfMonitor.Windowing/Theming/Theme.cs`
- Create: `src/PerfMonitor.Windowing/Theming/ThemeLoader.cs`
- Create: `tests/PerfMonitor.Windowing.Tests/PerfMonitor.Windowing.Tests.csproj`
- Create: `tests/PerfMonitor.Windowing.Tests/Theming/ThemeLoaderTests.cs`

- [ ] **Step 1: Scaffold projects**

Run:
```bash
dotnet new wpflib -n PerfMonitor.Windowing -o src/PerfMonitor.Windowing -f net8.0
dotnet sln add src/PerfMonitor.Windowing/PerfMonitor.Windowing.csproj
dotnet add src/PerfMonitor.Windowing reference src/PerfMonitor.Core
rm src/PerfMonitor.Windowing/Class1.cs

dotnet new xunit -n PerfMonitor.Windowing.Tests -o tests/PerfMonitor.Windowing.Tests -f net8.0-windows
dotnet sln add tests/PerfMonitor.Windowing.Tests/PerfMonitor.Windowing.Tests.csproj
dotnet add tests/PerfMonitor.Windowing.Tests reference src/PerfMonitor.Windowing
dotnet add tests/PerfMonitor.Windowing.Tests package FluentAssertions
rm tests/PerfMonitor.Windowing.Tests/UnitTest1.cs
```

Edit `src/PerfMonitor.Windowing/PerfMonitor.Windowing.csproj` — ensure `<TargetFramework>net8.0-windows</TargetFramework>` and `<UseWPF>true</UseWPF>`.

- [ ] **Step 2: Write the failing test**

Create `tests/PerfMonitor.Windowing.Tests/Theming/ThemeLoaderTests.cs`:
```csharp
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
```

- [ ] **Step 3: Implement `Theme`**

Create `src/PerfMonitor.Windowing/Theming/Theme.cs`:
```csharp
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
```

- [ ] **Step 4: Implement `ThemeLoader`**

Create `src/PerfMonitor.Windowing/Theming/ThemeLoader.cs`:
```csharp
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
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/PerfMonitor.Windowing.Tests`
Expected: 1 passed.

- [ ] **Step 6: Commit**

```bash
git add src/PerfMonitor.Windowing tests/PerfMonitor.Windowing.Tests PerfMonitor.sln
git commit -m "feat(windowing): add Theme and ThemeLoader"
```

---

### Task 10: `PillControl` UserControl (glassmorphic pill with accent + threshold state)

**Files:**
- Create: `src/PerfMonitor.Windowing/Controls/PillControl.xaml`
- Create: `src/PerfMonitor.Windowing/Controls/PillControl.xaml.cs`

- [ ] **Step 1: Create XAML**

Create `src/PerfMonitor.Windowing/Controls/PillControl.xaml`:
```xml
<UserControl x:Class="PerfMonitor.Windowing.Controls.PillControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:c="clr-namespace:PerfMonitor.Windowing.Controls"
             mc:Ignorable="d"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
    <Border Background="#14FFFFFF"
            CornerRadius="12"
            Padding="10,8"
            MinWidth="56">
        <StackPanel>
            <TextBlock Text="{Binding Label, RelativeSource={RelativeSource AncestorType=c:PillControl}}"
                       FontSize="9"
                       Opacity="0.55"
                       Foreground="White"
                       HorizontalAlignment="Center"
                       FontWeight="Medium"/>
            <TextBlock Text="{Binding Value, RelativeSource={RelativeSource AncestorType=c:PillControl}}"
                       FontSize="15"
                       Margin="0,2,0,0"
                       Foreground="{Binding AccentBrush, RelativeSource={RelativeSource AncestorType=c:PillControl}}"
                       HorizontalAlignment="Center"
                       FontWeight="Medium"/>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 2: Create code-behind with dependency properties**

Create `src/PerfMonitor.Windowing/Controls/PillControl.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Controls;

public partial class PillControl : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(PillControl),
            new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(PillControl),
            new PropertyMetadata(""));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(PillControl),
            new PropertyMetadata(Brushes.White));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public PillControl() => InitializeComponent();
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/PerfMonitor.Windowing`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add src/PerfMonitor.Windowing/Controls
git commit -m "feat(windowing): add PillControl with label/value/accent dependency properties"
```

---

### Task 11: `MainWidgetWindow` — transparent, topmost, draggable, Mica

**Files:**
- Create: `src/PerfMonitor.Windowing/Windows/MainWidgetWindow.xaml`
- Create: `src/PerfMonitor.Windowing/Windows/MainWidgetWindow.xaml.cs`
- Create: `src/PerfMonitor.Windowing/Interop/Dwm.cs`

- [ ] **Step 1: Create the DWM interop helper**

Create `src/PerfMonitor.Windowing/Interop/Dwm.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PerfMonitor.Windowing.Interop;

public static class Dwm
{
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;   // Mica

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void TryEnableMica(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int backdrop = DWMSBT_MAINWINDOW;
        try { DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)); }
        catch (DllNotFoundException) { /* pre-Win11 */ }
        catch (EntryPointNotFoundException) { /* pre-Win11 */ }
    }
}
```

- [ ] **Step 2: Create the widget window XAML**

Create `src/PerfMonitor.Windowing/Windows/MainWidgetWindow.xaml`:
```xml
<Window x:Class="PerfMonitor.Windowing.Windows.MainWidgetWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:c="clr-namespace:PerfMonitor.Windowing.Controls"
        Title="PerfMonitor"
        WindowStyle="None"
        ResizeMode="NoResize"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        SizeToContent="WidthAndHeight">
    <Border Background="#A61E1E23"
            BorderBrush="#14FFFFFF"
            BorderThickness="1"
            CornerRadius="18"
            Padding="10"
            MouseLeftButtonDown="OnDragTrigger">
        <StackPanel Orientation="Horizontal">
            <c:PillControl x:Name="CpuPill" Label="CPU" Value="—" Margin="0,0,6,0"/>
            <c:PillControl x:Name="RamPill" Label="RAM" Value="—" Margin="0,0,6,0"/>
            <c:PillControl x:Name="GpuPill" Label="GPU" Value="—" Margin="0,0,6,0"/>
            <c:PillControl x:Name="NetPill" Label="NET" Value="—"/>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 3: Create the code-behind**

Create `src/PerfMonitor.Windowing/Windows/MainWidgetWindow.xaml.cs`:
```csharp
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Windowing.Interop;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Windows;

public partial class MainWidgetWindow : Window
{
    private static readonly Brush CpuAccent = (Brush)new BrushConverter().ConvertFromString("#FF8A5A")!;
    private static readonly Brush RamAccent = (Brush)new BrushConverter().ConvertFromString("#5AD0FF")!;
    private static readonly Brush GpuAccent = (Brush)new BrushConverter().ConvertFromString("#A58AFF")!;
    private static readonly Brush NetAccent = (Brush)new BrushConverter().ConvertFromString("#5AFFAA")!;
    private static readonly Brush AlertAccent = (Brush)new BrushConverter().ConvertFromString("#FF4D6D")!;

    private readonly MainViewModel _vm;

    public MainWidgetWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        CpuPill.AccentBrush = CpuAccent;
        RamPill.AccentBrush = RamAccent;
        GpuPill.AccentBrush = GpuAccent;
        NetPill.AccentBrush = NetAccent;

        _vm.PropertyChanged += OnVmChanged;
        SourceInitialized += (_, _) => Dwm.TryEnableMica(this);
        RefreshAll();
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.CpuLoadPercent):
                case nameof(MainViewModel.CpuStatus):
                    CpuPill.Value = $"{_vm.CpuLoadPercent:F0}";
                    CpuPill.AccentBrush = _vm.CpuStatus == MetricStatus.Crit ? AlertAccent : CpuAccent;
                    break;
                case nameof(MainViewModel.RamUsedGb):
                case nameof(MainViewModel.RamTotalGb):
                    RamPill.Value = _vm.RamTotalGb > 0 ? $"{(_vm.RamUsedGb / _vm.RamTotalGb * 100):F0}" : "—";
                    break;
                case nameof(MainViewModel.GpuLoadPercent):
                    GpuPill.Value = $"{_vm.GpuLoadPercent:F0}";
                    break;
                case nameof(MainViewModel.NetDownMBps):
                    NetPill.Value = $"{_vm.NetDownMBps:F1}";
                    break;
            }
        });
    }

    private void RefreshAll()
    {
        CpuPill.Value = $"{_vm.CpuLoadPercent:F0}";
        RamPill.Value = _vm.RamTotalGb > 0 ? $"{(_vm.RamUsedGb / _vm.RamTotalGb * 100):F0}" : "—";
        GpuPill.Value = $"{_vm.GpuLoadPercent:F0}";
        NetPill.Value = $"{_vm.NetDownMBps:F1}";
    }

    private void OnDragTrigger(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/PerfMonitor.Windowing`
Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Commit**

```bash
git add src/PerfMonitor.Windowing
git commit -m "feat(windowing): add MainWidgetWindow with transparency, drag, and Mica"
```

---

### Task 12: Wire widget into App startup + MetricsSampler

**Files:**
- Create: `src/PerfMonitor.App/Services/MetricsSampler.cs`
- Modify: `src/PerfMonitor.App/App.xaml.cs`

- [ ] **Step 1: Create `MetricsSampler`**

Create `src/PerfMonitor.App/Services/MetricsSampler.cs`:
```csharp
using Microsoft.Extensions.Hosting;
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Hardware;
using System.Windows;

namespace PerfMonitor.App.Services;

public sealed class MetricsSampler : BackgroundService
{
    private readonly HardwareMonitor _monitor;
    private readonly MainViewModel _vm;

    public MetricsSampler(HardwareMonitor monitor, MainViewModel vm)
    {
        _monitor = monitor;
        _vm = vm;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _monitor.Start();
        await foreach (var snap in _monitor.Reader.ReadAllAsync(stoppingToken))
        {
            var captured = snap;
            Application.Current.Dispatcher.Invoke(() => _vm.Apply(captured));
        }
    }
}
```

- [ ] **Step 2: Register in DI and show the widget**

Modify `src/PerfMonitor.App/App.xaml.cs` — inside `ConfigureServices`, add:
```csharp
services.AddSingleton<PerfMonitor.Windowing.Windows.MainWidgetWindow>();
services.AddHostedService<Services.MetricsSampler>();
```

Add project reference:
```bash
dotnet add src/PerfMonitor.App reference src/PerfMonitor.Windowing
```

Add at the end of `OnStartup` in `App.xaml.cs`, after `_host.Start();`:
```csharp
var widget = Services.GetRequiredService<PerfMonitor.Windowing.Windows.MainWidgetWindow>();
widget.Show();
```

And at the top:
```csharp
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 3: Build & run — verify widget appears with live data**

Run: `dotnet run --project src/PerfMonitor.App`
Expected: transparent pill-row widget appears top-left, drags freely, shows live values updating every second.

- [ ] **Step 4: Commit**

```bash
git add src/PerfMonitor.App
git commit -m "feat(app): wire MetricsSampler to ViewModel and show widget"
```

---

## Phase 4 — Tray Icons (⟂ Phase 3)

### Task 13: `PerfMonitor.Tray` project + `TrayIconRenderer`

**Files:**
- Create: `src/PerfMonitor.Tray/PerfMonitor.Tray.csproj`
- Create: `src/PerfMonitor.Tray/Rendering/TrayIconRenderer.cs`
- Create: `src/PerfMonitor.Tray/Interop/User32.cs`
- Create: `tests/PerfMonitor.Tray.Tests/PerfMonitor.Tray.Tests.csproj`
- Create: `tests/PerfMonitor.Tray.Tests/Rendering/TrayIconRendererTests.cs`

- [ ] **Step 1: Scaffold projects**

```bash
dotnet new classlib -n PerfMonitor.Tray -o src/PerfMonitor.Tray -f net8.0-windows
dotnet sln add src/PerfMonitor.Tray/PerfMonitor.Tray.csproj
dotnet add src/PerfMonitor.Tray reference src/PerfMonitor.Core
rm src/PerfMonitor.Tray/Class1.cs

dotnet new xunit -n PerfMonitor.Tray.Tests -o tests/PerfMonitor.Tray.Tests -f net8.0-windows
dotnet sln add tests/PerfMonitor.Tray.Tests/PerfMonitor.Tray.Tests.csproj
dotnet add tests/PerfMonitor.Tray.Tests reference src/PerfMonitor.Tray
dotnet add tests/PerfMonitor.Tray.Tests package FluentAssertions
rm tests/PerfMonitor.Tray.Tests/UnitTest1.cs
```

Edit `src/PerfMonitor.Tray/PerfMonitor.Tray.csproj` — add `<UseWindowsForms>true</UseWindowsForms>` (needed for `System.Drawing` on .NET 8).

- [ ] **Step 2: Create `User32` DestroyIcon interop**

Create `src/PerfMonitor.Tray/Interop/User32.cs`:
```csharp
using System.Runtime.InteropServices;

namespace PerfMonitor.Tray.Interop;

internal static class User32
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
```

- [ ] **Step 3: Write the renderer test**

Create `tests/PerfMonitor.Tray.Tests/Rendering/TrayIconRendererTests.cs`:
```csharp
using FluentAssertions;
using PerfMonitor.Tray.Rendering;
using System.Drawing;

namespace PerfMonitor.Tray.Tests.Rendering;

public class TrayIconRendererTests
{
    [Fact]
    public void Render_Load50_ProducesDifferentPixelsThanLoad0()
    {
        using var r = new TrayIconRenderer();
        using var b0 = r.RenderLoadBitmap(0, Color.Orange);
        using var b50 = r.RenderLoadBitmap(50, Color.Orange);
        HashBitmap(b0).Should().NotBe(HashBitmap(b50));
    }

    [Fact]
    public void Render_SameInputTwice_IsDeterministic()
    {
        using var r = new TrayIconRenderer();
        using var a = r.RenderLoadBitmap(42, Color.Cyan);
        using var b = r.RenderLoadBitmap(42, Color.Cyan);
        HashBitmap(a).Should().Be(HashBitmap(b));
    }

    private static string HashBitmap(Bitmap bmp)
    {
        var bytes = new byte[bmp.Width * bmp.Height * 4];
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try { System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length); }
        finally { bmp.UnlockBits(data); }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}
```

- [ ] **Step 4: Implement `TrayIconRenderer`**

Create `src/PerfMonitor.Tray/Rendering/TrayIconRenderer.cs`:
```csharp
using PerfMonitor.Tray.Interop;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PerfMonitor.Tray.Rendering;

public sealed class TrayIconRenderer : IDisposable
{
    public int Size { get; init; } = 16;

    public Bitmap RenderLoadBitmap(float loadPercent, Color accent)
    {
        var bmp = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var bg = new SolidBrush(Color.FromArgb(200, 20, 23, 42));
        g.FillRectangle(bg, 0, 0, Size, Size);

        using var fill = new SolidBrush(accent);
        var filled = (int)Math.Round(Size * Math.Clamp(loadPercent, 0, 100) / 100f);
        g.FillRectangle(fill, 0, Size - filled, Size, filled);

        using var text = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        var label = loadPercent >= 100 ? "MX" : $"{(int)loadPercent}";
        var size = g.MeasureString(label, font);
        g.DrawString(label, font, text,
            (Size - size.Width) / 2,
            (Size - size.Height) / 2);
        return bmp;
    }

    public Icon ToIcon(Bitmap bmp)
    {
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { User32.DestroyIcon(handle); }
    }

    public void Dispose() { }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/PerfMonitor.Tray.Tests`
Expected: 2 passed.

- [ ] **Step 6: Commit**

```bash
git add src/PerfMonitor.Tray tests/PerfMonitor.Tray.Tests PerfMonitor.sln
git commit -m "feat(tray): add TrayIconRenderer with deterministic GDI rendering"
```

---

### Task 14: `TrayIconHost` — 4 live NotifyIcons with quantization

**Files:**
- Create: `src/PerfMonitor.Tray/TrayIconHost.cs`
- Modify: `src/PerfMonitor.Tray/PerfMonitor.Tray.csproj` (add H.NotifyIcon.Wpf)
- Modify: `src/PerfMonitor.App/App.xaml.cs` (register + show)

- [ ] **Step 1: Add package**

```bash
dotnet add src/PerfMonitor.Tray package H.NotifyIcon.Wpf
```

- [ ] **Step 2: Implement `TrayIconHost`**

Create `src/PerfMonitor.Tray/TrayIconHost.cs`:
```csharp
using H.NotifyIcon;
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Tray.Rendering;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;

namespace PerfMonitor.Tray;

public sealed class TrayIconHost : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly TrayIconRenderer _renderer = new();
    private readonly TaskbarIcon _cpuIcon = new();
    private readonly TaskbarIcon _ramIcon = new();
    private readonly TaskbarIcon _gpuIcon = new();
    private readonly TaskbarIcon _cpuTempIcon = new();

    private int _lastCpu = -1, _lastRam = -1, _lastGpu = -1, _lastCpuTemp = -1;

    public TrayIconHost(MainViewModel vm)
    {
        _vm = vm;
        _cpuIcon.ToolTipText = "CPU load";
        _ramIcon.ToolTipText = "RAM used %";
        _gpuIcon.ToolTipText = "GPU load";
        _cpuTempIcon.ToolTipText = "CPU temperature";
        _vm.PropertyChanged += OnVmChanged;
        UpdateAll();
    }

    public void Show()
    {
        _cpuIcon.ForceCreate();
        _ramIcon.ForceCreate();
        _gpuIcon.ForceCreate();
        if (_vm.CpuTempAvailable) _cpuTempIcon.ForceCreate();
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.CpuLoadPercent):
                    UpdateIfChanged(ref _lastCpu, (int)_vm.CpuLoadPercent,
                        v => SetIcon(_cpuIcon, v, Color.FromArgb(255, 255, 138, 90)),
                        v => _cpuIcon.ToolTipText = $"CPU {v}%");
                    break;
                case nameof(MainViewModel.RamUsedGb):
                case nameof(MainViewModel.RamTotalGb):
                    var ram = _vm.RamTotalGb > 0 ? (int)(_vm.RamUsedGb / _vm.RamTotalGb * 100) : 0;
                    UpdateIfChanged(ref _lastRam, ram,
                        v => SetIcon(_ramIcon, v, Color.FromArgb(255, 90, 208, 255)),
                        v => _ramIcon.ToolTipText = $"RAM {v}% ({_vm.RamUsedGb:F1} / {_vm.RamTotalGb:F0} GB)");
                    break;
                case nameof(MainViewModel.GpuLoadPercent):
                    UpdateIfChanged(ref _lastGpu, (int)_vm.GpuLoadPercent,
                        v => SetIcon(_gpuIcon, v, Color.FromArgb(255, 165, 138, 255)),
                        v => _gpuIcon.ToolTipText = $"GPU {v}%");
                    break;
                case nameof(MainViewModel.CpuTempC):
                    if (!_vm.CpuTempAvailable) return;
                    UpdateIfChanged(ref _lastCpuTemp, (int)_vm.CpuTempC,
                        v => SetIcon(_cpuTempIcon, v, Color.FromArgb(255, 255, 77, 109)),
                        v => _cpuTempIcon.ToolTipText = $"CPU {v}°C");
                    break;
            }
        });
    }

    private static void UpdateIfChanged(ref int last, int current,
        Action<int> apply, Action<int> tooltip)
    {
        if (last == current) return;
        last = current;
        apply(current);
        tooltip(current);
    }

    private void SetIcon(TaskbarIcon icon, int value, Color accent)
    {
        using var bmp = _renderer.RenderLoadBitmap(value, accent);
        icon.Icon = _renderer.ToIcon(bmp);
    }

    private void UpdateAll()
    {
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.CpuLoadPercent)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.RamUsedGb)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.GpuLoadPercent)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.CpuTempC)));
    }

    public void Dispose()
    {
        _cpuIcon.Dispose();
        _ramIcon.Dispose();
        _gpuIcon.Dispose();
        _cpuTempIcon.Dispose();
        _renderer.Dispose();
    }
}
```

- [ ] **Step 3: Register in DI and call Show on startup**

Add project ref: `dotnet add src/PerfMonitor.App reference src/PerfMonitor.Tray`

Modify `src/PerfMonitor.App/App.xaml.cs` — in `ConfigureServices`:
```csharp
services.AddSingleton<PerfMonitor.Tray.TrayIconHost>();
```

After `widget.Show();` in `OnStartup`:
```csharp
var tray = Services.GetRequiredService<PerfMonitor.Tray.TrayIconHost>();
tray.Show();
```

- [ ] **Step 4: Build + run**

Run: `dotnet run --project src/PerfMonitor.App`
Expected: 3–4 tray icons appear (4th only if elevated); numbers update every second; hover shows tooltips.

- [ ] **Step 5: Commit**

```bash
git add src/PerfMonitor.Tray src/PerfMonitor.App
git commit -m "feat(tray): add TrayIconHost with 4 quantized live icons"
```

---

### Task 15: Tray context menu

**Files:**
- Modify: `src/PerfMonitor.Tray/TrayIconHost.cs`

- [ ] **Step 1: Add context menu builder**

Add this field and method to `TrayIconHost`:
```csharp
public event EventHandler? ExitRequested;
public event EventHandler? SettingsRequested;
public event EventHandler? ToggleVisibilityRequested;

private System.Windows.Controls.ContextMenu BuildMenu()
{
    var menu = new System.Windows.Controls.ContextMenu();
    var toggle = new System.Windows.Controls.MenuItem { Header = "Show / Hide widget" };
    toggle.Click += (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
    var settings = new System.Windows.Controls.MenuItem { Header = "Settings..." };
    settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    var exit = new System.Windows.Controls.MenuItem { Header = "Exit" };
    exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(toggle);
    menu.Items.Add(settings);
    menu.Items.Add(new System.Windows.Controls.Separator());
    menu.Items.Add(exit);
    return menu;
}
```

In the constructor, after the tooltip assignments:
```csharp
var menu = BuildMenu();
_cpuIcon.ContextMenu = menu;
_ramIcon.ContextMenu = menu;
_gpuIcon.ContextMenu = menu;
_cpuTempIcon.ContextMenu = menu;
```

- [ ] **Step 2: Wire events in `App.xaml.cs` OnStartup**

Replace the `tray.Show();` line with:
```csharp
tray.ExitRequested += (_, _) => Shutdown();
tray.ToggleVisibilityRequested += (_, _) => widget.Visibility = widget.IsVisible ? Visibility.Hidden : Visibility.Visible;
tray.Show();
```

- [ ] **Step 3: Build + run — test right-click menu**

Run: `dotnet run --project src/PerfMonitor.App`
Expected: right-clicking any tray icon shows the menu; "Exit" closes the app; "Show / Hide widget" toggles.

- [ ] **Step 4: Commit**

```bash
git add src/PerfMonitor.Tray src/PerfMonitor.App
git commit -m "feat(tray): add context menu with show/hide, settings, exit"
```

---

## Phase 5 — Docked Bar (⟂ Phase 6)

### Task 16: `AppBarService` wrapping `SHAppBarMessage`

**Files:**
- Create: `src/PerfMonitor.Windowing/Interop/Shell32.cs`
- Create: `src/PerfMonitor.Windowing/Docking/AppBarEdge.cs`
- Create: `src/PerfMonitor.Windowing/Docking/IAppBarService.cs`
- Create: `src/PerfMonitor.Windowing/Docking/AppBarService.cs`

- [ ] **Step 1: Interop**

Create `src/PerfMonitor.Windowing/Interop/Shell32.cs`:
```csharp
using System.Runtime.InteropServices;

namespace PerfMonitor.Windowing.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public int lParam;
}

internal static class Shell32
{
    public const uint ABM_NEW      = 0x00000000;
    public const uint ABM_REMOVE   = 0x00000001;
    public const uint ABM_QUERYPOS = 0x00000002;
    public const uint ABM_SETPOS   = 0x00000003;

    public const uint ABE_LEFT   = 0;
    public const uint ABE_TOP    = 1;
    public const uint ABE_RIGHT  = 2;
    public const uint ABE_BOTTOM = 3;

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
```

- [ ] **Step 2: Types**

Create `src/PerfMonitor.Windowing/Docking/AppBarEdge.cs`:
```csharp
namespace PerfMonitor.Windowing.Docking;

public enum AppBarEdge { Top, Bottom, Left, Right }
```

Create `src/PerfMonitor.Windowing/Docking/IAppBarService.cs`:
```csharp
using System.Windows;

namespace PerfMonitor.Windowing.Docking;

public interface IAppBarService
{
    bool Register(Window window, AppBarEdge edge, int thicknessPx);
    void Unregister(Window window);
}
```

- [ ] **Step 3: Implementation**

Create `src/PerfMonitor.Windowing/Docking/AppBarService.cs`:
```csharp
using PerfMonitor.Windowing.Interop;
using System.Windows;
using System.Windows.Interop;

namespace PerfMonitor.Windowing.Docking;

public sealed class AppBarService : IAppBarService
{
    private readonly HashSet<IntPtr> _registered = new();

    public bool Register(Window window, AppBarEdge edge, int thicknessPx)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (_registered.Contains(hwnd)) Unregister(window);

        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd
        };
        if (Shell32.SHAppBarMessage(Shell32.ABM_NEW, ref data) == IntPtr.Zero)
            return false;

        data.uEdge = edge switch
        {
            AppBarEdge.Top => Shell32.ABE_TOP,
            AppBarEdge.Bottom => Shell32.ABE_BOTTOM,
            AppBarEdge.Left => Shell32.ABE_LEFT,
            AppBarEdge.Right => Shell32.ABE_RIGHT,
            _ => Shell32.ABE_TOP
        };

        var screen = SystemParameters.WorkArea;
        data.rc = edge switch
        {
            AppBarEdge.Top    => new RECT { Left = 0, Top = 0, Right = (int)screen.Right, Bottom = thicknessPx },
            AppBarEdge.Bottom => new RECT { Left = 0, Top = (int)screen.Bottom - thicknessPx, Right = (int)screen.Right, Bottom = (int)screen.Bottom },
            AppBarEdge.Left   => new RECT { Left = 0, Top = 0, Right = thicknessPx, Bottom = (int)screen.Bottom },
            AppBarEdge.Right  => new RECT { Left = (int)screen.Right - thicknessPx, Top = 0, Right = (int)screen.Right, Bottom = (int)screen.Bottom },
            _ => default
        };
        Shell32.SHAppBarMessage(Shell32.ABM_QUERYPOS, ref data);
        Shell32.SHAppBarMessage(Shell32.ABM_SETPOS, ref data);

        window.Left = data.rc.Left;
        window.Top = data.rc.Top;
        window.Width = data.rc.Right - data.rc.Left;
        window.Height = data.rc.Bottom - data.rc.Top;
        _registered.Add(hwnd);
        return true;
    }

    public void Unregister(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !_registered.Contains(hwnd)) return;
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd
        };
        Shell32.SHAppBarMessage(Shell32.ABM_REMOVE, ref data);
        _registered.Remove(hwnd);
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/PerfMonitor.Windowing`
Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Commit**

```bash
git add src/PerfMonitor.Windowing
git commit -m "feat(windowing): add AppBarService for edge docking"
```

---

### Task 17: `DockedBarWindow` + mode switching

**Files:**
- Create: `src/PerfMonitor.Windowing/Windows/DockedBarWindow.xaml`
- Create: `src/PerfMonitor.Windowing/Windows/DockedBarWindow.xaml.cs`
- Create: `src/PerfMonitor.Windowing/Windows/IWidgetWindow.cs`
- Modify: `src/PerfMonitor.App/App.xaml.cs`

- [ ] **Step 1: Extract shared interface**

Create `src/PerfMonitor.Windowing/Windows/IWidgetWindow.cs`:
```csharp
namespace PerfMonitor.Windowing.Windows;

public interface IWidgetWindow
{
    void Show();
    void Hide();
    void Close();
}
```

- [ ] **Step 2: Create docked bar XAML**

Create `src/PerfMonitor.Windowing/Windows/DockedBarWindow.xaml`:
```xml
<Window x:Class="PerfMonitor.Windowing.Windows.DockedBarWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:c="clr-namespace:PerfMonitor.Windowing.Controls"
        Title="PerfMonitor Dock"
        WindowStyle="None"
        ResizeMode="NoResize"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False">
    <Border Background="#BF1E1E23" BorderBrush="#14FFFFFF" BorderThickness="0,0,0,1" Padding="12,8">
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
            <c:PillControl x:Name="CpuPill" Label="CPU" Value="—" Margin="0,0,8,0"/>
            <c:PillControl x:Name="RamPill" Label="RAM" Value="—" Margin="0,0,8,0"/>
            <c:PillControl x:Name="GpuPill" Label="GPU" Value="—" Margin="0,0,8,0"/>
            <c:PillControl x:Name="NetPill" Label="NET" Value="—"/>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 3: Code-behind**

Create `src/PerfMonitor.Windowing/Windows/DockedBarWindow.xaml.cs`:
```csharp
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Windowing.Docking;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Windows;

public partial class DockedBarWindow : Window, IWidgetWindow
{
    private readonly MainViewModel _vm;
    private readonly IAppBarService _appBar;
    private readonly AppBarEdge _edge;

    public DockedBarWindow(MainViewModel vm, IAppBarService appBar, AppBarEdge edge)
    {
        InitializeComponent();
        _vm = vm;
        _appBar = appBar;
        _edge = edge;

        CpuPill.AccentBrush = new BrushConverter().ConvertFromString("#FF8A5A") as Brush ?? Brushes.White;
        RamPill.AccentBrush = new BrushConverter().ConvertFromString("#5AD0FF") as Brush ?? Brushes.White;
        GpuPill.AccentBrush = new BrushConverter().ConvertFromString("#A58AFF") as Brush ?? Brushes.White;
        NetPill.AccentBrush = new BrushConverter().ConvertFromString("#5AFFAA") as Brush ?? Brushes.White;

        _vm.PropertyChanged += OnVmChanged;
        SourceInitialized += (_, _) => _appBar.Register(this, _edge, thicknessPx: 36);
        Closed += (_, _) => _appBar.Unregister(this);
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e) =>
        Dispatcher.Invoke(() =>
        {
            CpuPill.Value = $"{_vm.CpuLoadPercent:F0}";
            RamPill.Value = _vm.RamTotalGb > 0 ? $"{(_vm.RamUsedGb / _vm.RamTotalGb * 100):F0}" : "—";
            GpuPill.Value = $"{_vm.GpuLoadPercent:F0}";
            NetPill.Value = $"{_vm.NetDownMBps:F1}";
        });
}
```

- [ ] **Step 4: Register in DI + pick window based on settings**

In `src/PerfMonitor.App/App.xaml.cs` `ConfigureServices`, replace the widget registration with a factory:
```csharp
services.AddSingleton<IAppBarService, PerfMonitor.Windowing.Docking.AppBarService>();
services.AddSingleton<IWidgetWindow>(sp =>
{
    var vm = sp.GetRequiredService<MainViewModel>();
    var settings = sp.GetRequiredService<ISettingsStore>().Load();
    return settings.Display.Mode switch
    {
        DisplayMode.Floating => new PerfMonitor.Windowing.Windows.MainWidgetWindow(vm),
        DisplayMode.DockedTop => new PerfMonitor.Windowing.Windows.DockedBarWindow(vm, sp.GetRequiredService<IAppBarService>(), AppBarEdge.Top),
        DisplayMode.DockedBottom => new PerfMonitor.Windowing.Windows.DockedBarWindow(vm, sp.GetRequiredService<IAppBarService>(), AppBarEdge.Bottom),
        DisplayMode.DockedLeft => new PerfMonitor.Windowing.Windows.DockedBarWindow(vm, sp.GetRequiredService<IAppBarService>(), AppBarEdge.Left),
        DisplayMode.DockedRight => new PerfMonitor.Windowing.Windows.DockedBarWindow(vm, sp.GetRequiredService<IAppBarService>(), AppBarEdge.Right),
        _ => new PerfMonitor.Windowing.Windows.MainWidgetWindow(vm)
    };
});
```

Add usings:
```csharp
using PerfMonitor.Windowing.Docking;
using PerfMonitor.Windowing.Windows;
```

In `OnStartup`, replace the previous widget retrieval with:
```csharp
var widget = Services.GetRequiredService<IWidgetWindow>();
widget.Show();
```

Ensure `MainWidgetWindow` also implements `IWidgetWindow` — add `: Window, IWidgetWindow` to class declaration (already matches signatures).

- [ ] **Step 5: Build + test both modes**

Run with mode = Floating in settings (default), verify widget behavior. Then edit `%APPDATA%\PerfMonitor\settings.json` to set `"mode": "DockedTop"` and re-run — bar should dock and reserve work area. Close the app and verify no ghost appbar remains.

- [ ] **Step 6: Commit**

```bash
git add src/PerfMonitor.Windowing src/PerfMonitor.App
git commit -m "feat(windowing): add DockedBarWindow with AppBar integration and mode switching"
```

---

## Phase 6 — Behaviors (⟂ Phase 5)

### Task 18: `FullscreenDetector` with fade-out

**Files:**
- Create: `src/PerfMonitor.Windowing/Interop/User32Fullscreen.cs`
- Create: `src/PerfMonitor.Windowing/Behaviors/FullscreenDetector.cs`

- [ ] **Step 1: Interop**

Create `src/PerfMonitor.Windowing/Interop/User32Fullscreen.cs`:
```csharp
using System.Runtime.InteropServices;

namespace PerfMonitor.Windowing.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct WinRECT { public int Left, Top, Right, Bottom; }

internal static class User32Fullscreen
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out WinRECT lpRect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfo
    {
        public uint cbSize;
        public WinRECT rcMonitor;
        public WinRECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
    public const uint MONITOR_DEFAULTTONEAREST = 2;
}
```

- [ ] **Step 2: Detector**

Create `src/PerfMonitor.Windowing/Behaviors/FullscreenDetector.cs`:
```csharp
using PerfMonitor.Windowing.Interop;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PerfMonitor.Windowing.Behaviors;

public sealed class FullscreenDetector : IDisposable
{
    private readonly Window _target;
    private readonly DispatcherTimer _timer;
    private bool _isHidden;

    public FullscreenDetector(Window target, TimeSpan? interval = null)
    {
        _target = target;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        var fg = User32Fullscreen.GetForegroundWindow();
        if (fg == IntPtr.Zero) return;

        if (!User32Fullscreen.GetWindowRect(fg, out var wr)) return;
        var mon = User32Fullscreen.MonitorFromWindow(fg, User32Fullscreen.MONITOR_DEFAULTTONEAREST);
        var mi = new User32Fullscreen.MonitorInfo { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<User32Fullscreen.MonitorInfo>() };
        if (!User32Fullscreen.GetMonitorInfo(mon, ref mi)) return;

        var isFullscreen =
            wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top &&
            wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;

        if (isFullscreen && !_isHidden) Fade(0);
        else if (!isFullscreen && _isHidden) Fade(1);
        _isHidden = isFullscreen;
    }

    private void Fade(double to)
    {
        var anim = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(250) };
        _target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public void Dispose() => _timer.Stop();
}
```

- [ ] **Step 3: Wire in `App.xaml.cs`**

After `widget.Show();`:
```csharp
if (Services.GetRequiredService<ISettingsStore>().Load().Behavior.AutoHideOnFullscreen
    && widget is Window w)
{
    _ = new PerfMonitor.Windowing.Behaviors.FullscreenDetector(w);
}
```

- [ ] **Step 4: Build + test**

Launch a fullscreen video or game. The widget should fade to invisible within 2 seconds and fade back when focus returns.

- [ ] **Step 5: Commit**

```bash
git add src/PerfMonitor.Windowing src/PerfMonitor.App
git commit -m "feat(windowing): add FullscreenDetector with fade-out behavior"
```

---

### Task 19: `HotkeyService` click-through toggle

**Files:**
- Create: `src/PerfMonitor.Windowing/Behaviors/HotkeyService.cs`
- Create: `src/PerfMonitor.Windowing/Interop/User32Hotkey.cs`

- [ ] **Step 1: Interop**

Create `src/PerfMonitor.Windowing/Interop/User32Hotkey.cs`:
```csharp
using System.Runtime.InteropServices;

namespace PerfMonitor.Windowing.Interop;

internal static class User32Hotkey
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;
    public const int WM_HOTKEY = 0x0312;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
```

- [ ] **Step 2: Service**

Create `src/PerfMonitor.Windowing/Behaviors/HotkeyService.cs`:
```csharp
using PerfMonitor.Windowing.Interop;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PerfMonitor.Windowing.Behaviors;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xB000;
    private readonly Window _target;
    private readonly HwndSource _source;
    private bool _clickThrough;

    public HotkeyService(Window target, ModifierKeys modifiers, Key key)
    {
        _target = target;
        var hwnd = new WindowInteropHelper(target).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd)!;
        _source.AddHook(Hook);

        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= User32Hotkey.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt))     mods |= User32Hotkey.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift))   mods |= User32Hotkey.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= User32Hotkey.MOD_WIN;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        User32Hotkey.RegisterHotKey(hwnd, HotkeyId, mods, vk);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32Hotkey.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Toggle();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Toggle()
    {
        var hwnd = new WindowInteropHelper(_target).Handle;
        var ex = User32Hotkey.GetWindowLong(hwnd, User32Hotkey.GWL_EXSTYLE);
        _clickThrough = !_clickThrough;
        var updated = _clickThrough
            ? ex | User32Hotkey.WS_EX_TRANSPARENT | User32Hotkey.WS_EX_LAYERED
            : ex & ~User32Hotkey.WS_EX_TRANSPARENT;
        User32Hotkey.SetWindowLong(hwnd, User32Hotkey.GWL_EXSTYLE, updated);
    }

    public void Dispose()
    {
        var hwnd = new WindowInteropHelper(_target).Handle;
        User32Hotkey.UnregisterHotKey(hwnd, HotkeyId);
        _source.RemoveHook(Hook);
    }
}
```

- [ ] **Step 3: Wire in `App.xaml.cs`**

After the fullscreen detector:
```csharp
if (widget is Window hw)
    _ = new PerfMonitor.Windowing.Behaviors.HotkeyService(hw, ModifierKeys.Control | ModifierKeys.Alt, Key.M);
```

Add `using System.Windows.Input;`.

- [ ] **Step 4: Build + test**

Press **Ctrl+Alt+M** — clicks should pass through the widget. Press again — widget becomes interactive.

- [ ] **Step 5: Commit**

```bash
git add src/PerfMonitor.Windowing src/PerfMonitor.App
git commit -m "feat(windowing): add HotkeyService for click-through toggle"
```

---

### Task 20: `StartupRegistrar` (Task Scheduler)

**Files:**
- Create: `src/PerfMonitor.Startup/PerfMonitor.Startup.csproj`
- Create: `src/PerfMonitor.Startup/StartupRegistrar.cs`

- [ ] **Step 1: Scaffold**

```bash
dotnet new classlib -n PerfMonitor.Startup -o src/PerfMonitor.Startup -f net8.0-windows
dotnet sln add src/PerfMonitor.Startup/PerfMonitor.Startup.csproj
dotnet add src/PerfMonitor.Startup package TaskScheduler
rm src/PerfMonitor.Startup/Class1.cs
```

- [ ] **Step 2: Implement**

Create `src/PerfMonitor.Startup/StartupRegistrar.cs`:
```csharp
using Microsoft.Win32.TaskScheduler;

namespace PerfMonitor.Startup;

public static class StartupRegistrar
{
    private const string TaskName = "PerfMonitor Autostart";

    public static void Register(string exePath)
    {
        using var ts = new TaskService();
        var td = ts.NewTask();
        td.RegistrationInfo.Description = "Launch PerfMonitor at logon";
        td.Principal.RunLevel = TaskRunLevel.LUA;
        td.Triggers.Add(new LogonTrigger { Delay = TimeSpan.FromSeconds(10) });
        td.Actions.Add(new ExecAction(exePath, null, Path.GetDirectoryName(exePath)));
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        ts.RootFolder.RegisterTaskDefinition(TaskName, td);
    }

    public static void Unregister()
    {
        using var ts = new TaskService();
        ts.RootFolder.DeleteTask(TaskName, exceptionOnNotExists: false);
    }

    public static bool IsRegistered()
    {
        using var ts = new TaskService();
        return ts.GetTask(TaskName) is not null;
    }
}
```

- [ ] **Step 3: Wire in App.xaml.cs**

Add reference: `dotnet add src/PerfMonitor.App reference src/PerfMonitor.Startup`

In `OnStartup`, after services resolved:
```csharp
var settings = Services.GetRequiredService<ISettingsStore>().Load();
if (settings.Behavior.StartWithWindows && !PerfMonitor.Startup.StartupRegistrar.IsRegistered())
{
    PerfMonitor.Startup.StartupRegistrar.Register(Environment.ProcessPath!);
}
else if (!settings.Behavior.StartWithWindows && PerfMonitor.Startup.StartupRegistrar.IsRegistered())
{
    PerfMonitor.Startup.StartupRegistrar.Unregister();
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/PerfMonitor.Startup src/PerfMonitor.App PerfMonitor.sln
git commit -m "feat(startup): add Task Scheduler-based autostart registrar"
```

---

## Phase 7 — Settings Window

### Task 21: `SettingsWindow` with tabbed UI

**Files:**
- Create: `src/PerfMonitor.Windowing/Windows/SettingsWindow.xaml`
- Create: `src/PerfMonitor.Windowing/Windows/SettingsWindow.xaml.cs`
- Create: `src/PerfMonitor.Windowing/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: SettingsViewModel**

Create `src/PerfMonitor.Windowing/ViewModels/SettingsViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.Windowing.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    [ObservableProperty] private DisplayMode mode;
    [ObservableProperty] private double opacity;
    [ObservableProperty] private int refreshIntervalMs;
    [ObservableProperty] private bool autoHideOnFullscreen;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private float cpuWarnPercent;
    [ObservableProperty] private float cpuCritPercent;
    [ObservableProperty] private float cpuTempWarnC;
    [ObservableProperty] private float cpuTempCritC;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        var s = store.Load();
        Mode = s.Display.Mode;
        Opacity = s.Display.Opacity;
        RefreshIntervalMs = s.Display.RefreshIntervalMs;
        AutoHideOnFullscreen = s.Behavior.AutoHideOnFullscreen;
        StartWithWindows = s.Behavior.StartWithWindows;
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
            Behavior = current.Behavior with { AutoHideOnFullscreen = AutoHideOnFullscreen, StartWithWindows = StartWithWindows },
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
}
```

- [ ] **Step 2: XAML**

Create `src/PerfMonitor.Windowing/Windows/SettingsWindow.xaml`:
```xml
<Window x:Class="PerfMonitor.Windowing.Windows.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:s="clr-namespace:PerfMonitor.Core.Settings;assembly=PerfMonitor.Core"
        Title="PerfMonitor Settings"
        Width="440" Height="360"
        WindowStartupLocation="CenterScreen">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TabControl>
            <TabItem Header="Display">
                <StackPanel Margin="8">
                    <TextBlock Text="Mode" Margin="0,4,0,2"/>
                    <ComboBox SelectedValue="{Binding Mode}" SelectedValuePath="Content">
                        <ComboBoxItem Content="Floating"/>
                        <ComboBoxItem Content="DockedTop"/>
                        <ComboBoxItem Content="DockedBottom"/>
                        <ComboBoxItem Content="DockedLeft"/>
                        <ComboBoxItem Content="DockedRight"/>
                    </ComboBox>
                    <TextBlock Text="Opacity" Margin="0,10,0,2"/>
                    <Slider Minimum="0.3" Maximum="1.0" Value="{Binding Opacity}" TickFrequency="0.05" IsSnapToTickEnabled="True"/>
                    <TextBlock Text="Refresh (ms)" Margin="0,10,0,2"/>
                    <Slider Minimum="250" Maximum="5000" Value="{Binding RefreshIntervalMs}" TickFrequency="250" IsSnapToTickEnabled="True"/>
                </StackPanel>
            </TabItem>
            <TabItem Header="Behavior">
                <StackPanel Margin="8">
                    <CheckBox Content="Auto-hide when another app goes fullscreen"
                              IsChecked="{Binding AutoHideOnFullscreen}" Margin="0,4"/>
                    <CheckBox Content="Start with Windows"
                              IsChecked="{Binding StartWithWindows}" Margin="0,4"/>
                </StackPanel>
            </TabItem>
            <TabItem Header="Thresholds">
                <StackPanel Margin="8">
                    <TextBlock Text="CPU warn %"/>
                    <Slider Minimum="50" Maximum="100" Value="{Binding CpuWarnPercent}"/>
                    <TextBlock Text="CPU crit %"/>
                    <Slider Minimum="50" Maximum="100" Value="{Binding CpuCritPercent}"/>
                    <TextBlock Text="CPU temp warn °C"/>
                    <Slider Minimum="50" Maximum="110" Value="{Binding CpuTempWarnC}"/>
                    <TextBlock Text="CPU temp crit °C"/>
                    <Slider Minimum="50" Maximum="110" Value="{Binding CpuTempCritC}"/>
                </StackPanel>
            </TabItem>
            <TabItem Header="About">
                <StackPanel Margin="8">
                    <TextBlock Text="PerfMonitor" FontSize="18" FontWeight="Bold"/>
                    <TextBlock Text="Lightweight system performance widget."/>
                    <TextBlock Text="© David Howard" Margin="0,10,0,0"/>
                </StackPanel>
            </TabItem>
        </TabControl>

        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,10,0,0">
            <Button Content="Apply" Width="80" Margin="4,0" Click="OnApply"/>
            <Button Content="Close" Width="80" Click="OnClose"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 3: Code-behind**

Create `src/PerfMonitor.Windowing/Windows/SettingsWindow.xaml.cs`:
```csharp
using PerfMonitor.Windowing.ViewModels;
using System.Windows;

namespace PerfMonitor.Windowing.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnApply(object sender, RoutedEventArgs e) => _vm.Apply();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Wire in `App.xaml.cs`**

Register:
```csharp
services.AddTransient<PerfMonitor.Windowing.ViewModels.SettingsViewModel>();
services.AddTransient<PerfMonitor.Windowing.Windows.SettingsWindow>();
```

Wire the tray event:
```csharp
tray.SettingsRequested += (_, _) =>
{
    var win = Services.GetRequiredService<PerfMonitor.Windowing.Windows.SettingsWindow>();
    win.Show();
};
```

- [ ] **Step 5: Build + test**

Right-click tray icon → Settings... Window opens; change opacity; click Apply; verify `%APPDATA%\PerfMonitor\settings.json` reflects the change.

- [ ] **Step 6: Commit**

```bash
git add src/PerfMonitor.Windowing src/PerfMonitor.App
git commit -m "feat(settings): add SettingsWindow with tabbed UI and 2-way bindings"
```

---

## Phase 8 — Polish & QA

### Task 22: Build self-check + performance budget logging

**Files:**
- Create: `src/PerfMonitor.App/Services/PerfBudgetCheck.cs`
- Modify: `src/PerfMonitor.App/App.xaml.cs`

- [ ] **Step 1: Implement**

Create `src/PerfMonitor.App/Services/PerfBudgetCheck.cs`:
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PerfMonitor.App.Services;

public sealed class PerfBudgetCheck : BackgroundService
{
    private readonly ILogger<PerfBudgetCheck> _log;
    private const long MaxWorkingSetMb = 80;
    private const int MaxGdiHandles = 50;
    private const int MaxUserHandles = 100;

    public PerfBudgetCheck(ILogger<PerfBudgetCheck> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var p = Process.GetCurrentProcess();
            var ws = p.WorkingSet64 / (1024 * 1024);
            var gdi = GetGuiResources(p.Handle, 0);
            var user = GetGuiResources(p.Handle, 1);
            if (ws > MaxWorkingSetMb) _log.LogWarning("Working set {Ws} MB exceeds budget {Max} MB", ws, MaxWorkingSetMb);
            if (gdi > MaxGdiHandles) _log.LogWarning("GDI handles {Gdi} exceed budget {Max}", gdi, MaxGdiHandles);
            if (user > MaxUserHandles) _log.LogWarning("USER handles {User} exceed budget {Max}", user, MaxUserHandles);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
}
```

- [ ] **Step 2: Register**

In `ConfigureServices`:
```csharp
services.AddHostedService<Services.PerfBudgetCheck>();
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PerfMonitor.App
git commit -m "feat(app): add performance budget self-check with handle/memory logging"
```

---

### Task 23: Icon `.ico` generation + README + manual QA plan

**Files:**
- Create: `docs/qa/manual-test-plan.md`
- Create: `README.md`
- Modify: `src/PerfMonitor.App/Assets/app-icon.ico` (real ico generated from SVG)

- [ ] **Step 1: Generate the .ico from the SVG**

Install ImageMagick if not present, then run from repo root:
```bash
magick -background none src/PerfMonitor.App/Assets/app-icon.svg \
  -define icon:auto-resize=16,20,24,32,40,48,64,96,128,256 \
  src/PerfMonitor.App/Assets/app-icon.ico
```

If ImageMagick is unavailable, use PowerShell + .NET:
```powershell
Add-Type -AssemblyName System.Drawing
$svg = "src/PerfMonitor.App/Assets/app-icon.svg"
# Fallback: use an online converter or ResourceHacker; commit the result.
```

Verify the file is non-empty and re-add `<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>` to `PerfMonitor.App.csproj` if it was removed in Task 5.

- [ ] **Step 2: Create README**

Create `README.md`:
```markdown
# PerfMonitor

Lightweight always-on desktop performance widget for Windows 11 (Windows 10 fallback).
Displays live CPU / RAM / GPU load, network throughput, and CPU/GPU temperatures via
a transparent glassmorphic pill-row widget and four live-rendered system-tray icons.

## Quick start

```bash
git clone <repo>
cd perf-monitor
dotnet run --project src/PerfMonitor.App
```

## Publish a single-file exe

```bash
dotnet publish src/PerfMonitor.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src/PerfMonitor.App/bin/Release/net8.0-windows/win-x64/publish/PerfMonitor.exe`.

## Settings

JSON file at `%APPDATA%\PerfMonitor\settings.json`. Corrupt file → backed up to `.bak` and defaults restored.

## Admin elevation

Required only for CPU/GPU temperatures. App starts unelevated; temps show `—`.
Use the tray menu → Settings → Elevate button to relaunch with UAC.

## Hotkeys

- **Ctrl+Alt+M** — toggle click-through (widget stops catching mouse clicks)

See [`docs/superpowers/specs/2026-04-17-perf-monitor-design.md`](docs/superpowers/specs/2026-04-17-perf-monitor-design.md) for full design.
```

- [ ] **Step 3: Create manual QA checklist**

Create `docs/qa/manual-test-plan.md`:
```markdown
# PerfMonitor Manual Test Plan

Run before every release. Log pass/fail with notes.

## Startup
- [ ] Launch unelevated — widget appears, temps show `—`, app does not crash
- [ ] Launch elevated — temps populate
- [ ] First-run on Win11 — tray icons visible or hidden-by-default; verify pin prompt works

## Floating widget
- [ ] Widget is transparent on Win11 with Mica backdrop
- [ ] Widget falls back to flat 85% opacity on Win10
- [ ] Drag with LMB moves the widget smoothly across monitors
- [ ] `Ctrl+Alt+M` toggles click-through; verify a click under the widget reaches the app below
- [ ] Fullscreen a game / video — widget fades out within 2 s; returns on alt-tab back

## Tray icons
- [ ] 4 icons visible (3 if unelevated — no CPU temp)
- [ ] Numbers update every ~1 s
- [ ] Hover tooltips show full readouts including units
- [ ] Right-click menu: Show/Hide, Settings, Exit — all functional

## Docked mode
- [ ] Edit settings `mode: DockedTop` — bar docks at top; maximized windows don't overlap it
- [ ] Switch to `DockedBottom` / `Left` / `Right` — each docks correctly and unregisters cleanly
- [ ] Kill app via Task Manager — verify no ghost AppBar remains (reboot-to-confirm if needed)

## DPI
- [ ] 100 % scale — pills and tray icons sharp
- [ ] 150 % scale — no blur; per-monitor v2 working
- [ ] 200 % scale — icons still legible

## Leak soak
- [ ] Run for 72 hours
- [ ] Process Explorer: GDI handles ≤ 50, USER handles ≤ 100
- [ ] Working set ≤ 80 MB
- [ ] CPU ≤ 0.3 %
```

- [ ] **Step 4: Build + commit**

```bash
dotnet build
git add src/PerfMonitor.App/Assets/app-icon.ico README.md docs/qa/manual-test-plan.md src/PerfMonitor.App/PerfMonitor.App.csproj
git commit -m "docs: add README, manual QA checklist, and generated app icon"
```

---

### Task 24: CI pipeline — GitHub Actions

**Files:**
- Create: `.github/workflows/build.yml`

- [ ] **Step 1: Workflow**

Create `.github/workflows/build.yml`:
```yaml
name: build

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release
      - run: dotnet test --no-build --configuration Release --verbosity normal
      - name: Publish
        if: github.event_name == 'push'
        run: >
          dotnet publish src/PerfMonitor.App
          -c Release -r win-x64 --self-contained
          -p:PublishSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true
          -o publish
      - uses: actions/upload-artifact@v4
        if: github.event_name == 'push'
        with:
          name: PerfMonitor-win-x64
          path: publish/PerfMonitor.exe
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "ci: add GitHub Actions build + test + publish workflow"
```

---

### Task 25: Final end-to-end smoke test

- [ ] **Step 1: Full clean build**

Run:
```bash
dotnet clean
dotnet build --configuration Release
dotnet test --configuration Release
```
Expected: 0 errors; all tests pass.

- [ ] **Step 2: Publish and run the single-file exe**

```bash
dotnet publish src/PerfMonitor.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
./publish/PerfMonitor.exe
```

Expected: one exe file (~80 MB); launches; widget + tray icons appear; numbers update.

- [ ] **Step 3: Execute the manual QA checklist**

Open `docs/qa/manual-test-plan.md` and check every box. File any failures as follow-up issues.

- [ ] **Step 4: Tag the release**

```bash
git tag -a v0.1.0 -m "PerfMonitor v0.1.0 — initial release"
```

---

## Appendix — Spec Coverage Check

| Spec Section | Covered By |
|---|---|
| §1 Summary / §2 Goals | Entire plan |
| §4 Metrics Displayed | T2, T7, T8 |
| §5.1 Floating mode | T11, T12 |
| §5.2 Edge-docked mode | T16, T17 |
| §6 Visual style | T10, T11, T17 |
| §6a Brand icon & theme | T9, T23 |
| §7 Tray | T13, T14, T15 |
| §8 Architecture | T5, T12 |
| §9 Project structure | T1–T5 |
| §10 Data model | T2, T3 |
| §11 Threading model | T7, T12 |
| §12 Settings schema | T4, T21 |
| §13 Error handling & degradation | T4, T6, T8, T16 |
| §14 Operational defaults | T4, T18, T19, T20 |
| §15 Testing strategy | T2–T7, T9, T13 |
| §16 Performance budget | T22 |
| §17 Build & packaging | T5, T23, T24, T25 |
| §18 Phased build order | Matches plan |
