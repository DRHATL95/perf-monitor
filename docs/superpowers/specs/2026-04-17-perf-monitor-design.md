# PerfMonitor — Design Spec

**Date:** 2026-04-17
**Status:** Approved for implementation planning
**Owner:** David Howard

## 1. Summary

**PerfMonitor** is a lightweight, always-on desktop widget for Windows 11 (with Windows 10 fallback) that displays live system performance metrics — CPU, RAM, GPU load, network throughput, and CPU/GPU temperatures — without needing Task Manager open. The widget is transparent and glassmorphic, operates in either a floating draggable mode or an edge-docked bar mode, and mirrors key metrics as live-rendered system-tray icons so the data remains visible when the widget is hidden or when a fullscreen app has focus.

## 2. Goals

- Glanceable, low-distraction display of the most-useful performance metrics
- Always-on, unobtrusive, and respectful of gaming / fullscreen sessions
- Near-zero resource footprint (≤0.3% CPU, ≤80 MB RAM at steady state)
- Matches user's existing desktop aesthetic (Win11 Mica/Acrylic, glassmorphic pills)
- Survives Windows Update / reboot via auto-start

## 3. Non-Goals (YAGNI)

- Per-process CPU/RAM breakdown (Task Manager already does this)
- History charts / time-series graphs beyond the live bar indicator
- Metric logging, CSV export, cloud sync
- Cross-platform (Linux/macOS)
- Game framerate / PresentMon-style overlay
- Plugin system or user-defined custom metrics
- Theme variants beyond the glass aesthetic (reserved JSON field; no UI)

## 4. Metrics Displayed

| Metric | Source | Notes |
|---|---|---|
| CPU load % (aggregate) | LibreHardwareMonitorLib | EMA-smoothed α=0.3 |
| CPU temperature °C | LibreHardwareMonitorLib | Requires admin elevation |
| RAM used / total GB | LibreHardwareMonitorLib or `GlobalMemoryStatusEx` | |
| GPU load % | LibreHardwareMonitorLib (NVAPI/ADL/IGCL) | |
| GPU temperature °C | LibreHardwareMonitorLib | Requires admin elevation |
| GPU VRAM used / total GB | LibreHardwareMonitorLib | |
| Network throughput (down / up MB/s) | `IPv4InterfaceTable` / LHM | EMA-smoothed |
| Fan speeds (RPM) | LibreHardwareMonitorLib | Requires admin; surfaced in tooltip only |

## 5. Window Behavior

The application supports two mutually-exclusive **display modes**:

### 5.1 Floating mode (default)
- WPF window with `AllowsTransparency=true`, `WindowStyle=None`, `Topmost=true`
- Per-pixel alpha via DWM; Win11 Mica backdrop via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`
- Draggable from any non-interactive pixel (`ReleaseCapture` + `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)`)
- Global hotkey **Ctrl+Alt+M** toggles `WS_EX_TRANSPARENT` for true click-through
- Fades out when a fullscreen app is detected on the same monitor; fades in when fullscreen ends

### 5.2 Edge-docked mode
- Registers as a Win32 AppBar via `SHAppBarMessage(ABM_NEW / ABM_SETPOS)`, reserving screen work area along top/bottom/left/right
- Same glassmorphic pill layout, stretched along the docked edge
- Reliably unregisters (`ABM_REMOVE`) on exit and on mode switch — a ghost appbar is a P0 bug

Mode is toggled in Settings; position/opacity/orientation persist.

## 6. Visual Style

**Glassmorphic pill row.** Heavy-blur translucent backdrop with subtle white border; data is organized into color-accented rounded pills (CPU orange, RAM cyan, GPU violet, Net green). Pills desaturate and shift toward red/amber as their metric crosses configurable warn/crit thresholds — passive peripheral awareness without explicit alert UI.

Target baseline: Windows 11 with Mica/Acrylic. On Windows 10, fall back to flat 85%-opaque dark fill (no dynamic blur).

## 6a. Brand Icon & Theme

PerfMonitor ships with a single **brand mark** used consistently across:

- The app's executable icon (`PerfMonitor.exe` file icon, multi-resolution .ico: 16/20/24/32/40/48/64/96/128/256)
- The window title-bar / taskbar icon (when in Floating mode and the window is visible to the alt-tab/taskbar peek system)
- The system-tray **context-menu header** (the 16×16 icon beside "PerfMonitor — Settings" etc.)
- The installer/readme/about dialog

The live-data tray icons (Section 7) are distinct from the brand mark — they render numeric values and are regenerated each tick. The brand mark is static.

**Chosen mark — "Pill Cluster":** a compressed rendering of the widget itself placed on a soft-radial dark disc with a subtle white-border ring. Four accent pills (CPU orange, RAM cyan, GPU violet, Net green) arranged in a 2×2 grid, with a thin cyan progress bar underneath. The mark is literal (the app *is* this widget), self-documenting, and distinguishable at 16 px. Rendered as a vector (`app-icon.svg`) and rasterized to a Windows multi-resolution `.ico` (16/20/24/32/40/48/64/96/128/256) during the build via [ImageMagick](https://imagemagick.org) or the `ResourceHacker` CLI.

**Theme system:** a `theme.json` file under `src/PerfMonitor.App/Assets/` defines the palette, fonts, corner radii, and icon accent tints in one place. WPF resources (`App.xaml`) are generated from it at build time, so the widget, settings window, and icon tints stay visually coherent. Future theme variants (light-on-white, etc.) drop into `Assets/themes/` and are hot-swappable at runtime via the Settings → Display → Theme dropdown (reserved JSON field already exists).

Icon assets live at:

```
src/PerfMonitor.App/Assets/
  app-icon.svg          source of truth
  app-icon.ico          generated multi-resolution
  themes/
    glass.json          default (chosen)
    <future>.json
```

## 7. System Tray Representation

Four live-rendered `NotifyIcon`s (via H.NotifyIcon.Wpf):

- CPU load %
- RAM used %
- GPU load %
- CPU temperature °C (hidden when unelevated)

Each icon is a 16×16 `HICON` regenerated only when its underlying metric crosses a quantization threshold (1% for loads, 1°C for temps) — saves ~90% of GDI work. **Every icon swap must call `DestroyIcon` on the previous handle** to avoid a GDI handle leak. Tooltip on hover shows the full readout including fan speeds. Right-click opens the app context menu.

## 8. Architecture

```
┌──────────────────────────────────────────────────────┐
│  UI Layer (WPF)                                      │
│   • MainWidgetWindow   (transparent, topmost)        │
│   • DockedBarWindow    (AppBar-registered)           │
│   • TrayIconHost       (4× live-rendered NotifyIcon) │
│   • SettingsWindow                                   │
├──────────────────────────────────────────────────────┤
│  Services Layer                                      │
│   • MetricsSampler     (UI-thread consumer)          │
│   • FullscreenDetector (foreground window polling)   │
│   • HotkeyService      (global click-through toggle) │
│   • AppBarService      (SHAppBarMessage wrapper)     │
│   • SettingsStore      (JSON + file watcher)         │
│   • StartupRegistrar   (Task Scheduler)              │
├──────────────────────────────────────────────────────┤
│  Hardware Layer                                      │
│   • HardwareMonitor                                  │
│     (LibreHardwareMonitorLib, background thread,     │
│      publishes MetricsSnapshot via Channel)          │
└──────────────────────────────────────────────────────┘
```

A single `MainViewModel` is shared by all three render paths (widget, docked bar, tray). INotifyPropertyChanged drives reactive updates.

## 9. Project Structure

```
PerfMonitor.sln
src/
  PerfMonitor.App/           WPF entry, App.xaml, DI composition root
  PerfMonitor.Core/          MainViewModel, MetricsSnapshot, settings model
  PerfMonitor.Hardware/      HardwareMonitor, LHM wrapper
  PerfMonitor.Windowing/     Windows, AppBar interop, hotkeys
  PerfMonitor.Tray/          TrayIconHost, icon rendering
  PerfMonitor.Startup/       Task Scheduler registration
tests/
  PerfMonitor.Core.Tests/
  PerfMonitor.Hardware.Tests/
  PerfMonitor.Tray.Tests/
  PerfMonitor.Windowing.Tests/
docs/
  superpowers/specs/
  qa/manual-test-plan.md
```

DI via `Microsoft.Extensions.DependencyInjection`. Each subproject is independently testable.

## 10. Data Model

```csharp
public record MetricsSnapshot(
    DateTime Timestamp,
    float CpuLoadPercent,
    float? CpuTempC,            // null when unelevated
    float RamUsedGb, float RamTotalGb,
    float GpuLoadPercent,
    float? GpuTempC,
    float GpuVramUsedGb, float GpuVramTotalGb,
    float NetDownMBps, float NetUpMBps,
    IReadOnlyList<FanReading> Fans,
    MetricsHealth Health        // Ok | TempsUnavailable | SensorError
);

public record FanReading(string Name, int Rpm);
public enum MetricsHealth { Ok, TempsUnavailable, SensorError }
```

## 11. Threading Model

- **Sensor thread (background):** owns the LHM `Computer` instance, blocks on polling (~5–50 ms per tick), writes snapshots to `Channel<MetricsSnapshot>(capacity=1, FullMode=DropOldest)`. Never touches UI.
- **UI dispatcher:** `async` consumer reads the channel, applies EMA smoothing, updates `MainViewModel` properties.
- **Tray redraw:** triggered by property-changed events on quantized metric values; executes on UI thread (required for `NotifyIcon.Icon` assignment).

Channel with capacity=1 + DropOldest ensures the UI always sees the latest snapshot and never backs up if the UI thread stalls.

## 12. Settings Schema

Location: `%APPDATA%\PerfMonitor\settings.json`. Hot-reloaded via `FileSystemWatcher`. Corrupt file → backed up to `settings.json.bak` and defaults restored.

```json
{
  "display": {
    "mode": "Floating",
    "position": { "x": 20, "y": 20 },
    "opacity": 0.85,
    "refreshIntervalMs": 1000,
    "theme": "Glass",
    "showPills": ["Cpu","Ram","Gpu","Net"]
  },
  "tray": {
    "enabled": true,
    "icons": ["Cpu","Ram","Gpu","CpuTemp"]
  },
  "behavior": {
    "autoHideOnFullscreen": true,
    "fullscreenWhitelist": [],
    "clickThroughHotkey": "Ctrl+Alt+M",
    "startWithWindows": true
  },
  "thresholds": {
    "cpuWarnPercent": 85, "cpuCritPercent": 95,
    "cpuTempWarnC": 80,   "cpuTempCritC": 90,
    "gpuTempWarnC": 80,   "gpuTempCritC": 88
  }
}
```

## 13. Error Handling & Degradation

| Condition | Behavior |
|---|---|
| Process is not elevated | Temps render as `—`; tooltip explains; Settings tab offers "Elevate" relaunch button |
| LibreHardwareMonitor fails to init | App still starts; all pills show `—`; Settings status bar shows error + Retry |
| GPU sensor missing | GPU pill and tray icon auto-hidden |
| `SHAppBarMessage` registration fails | Falls back to Floating mode; balloon-tip notifies user |
| Settings file corrupt | Back up to `.bak`, load defaults, log warning |
| Hotkey already registered by another app | Surface a non-blocking warning in Settings; user can pick another chord |
| Fullscreen detector false-positive (e.g., borderless chrome) | Whitelist path in settings; user adds exe basename |

## 14. Operational Defaults

- **Refresh rate:** 1000 ms (configurable 250–5000)
- **Auto-start:** Task Scheduler entry for current user, trigger "At log on", delay 10 s to let shell settle (avoids race with `explorer.exe`). Chosen over `HKCU\Run` for UAC resilience.
- **Fullscreen auto-hide:** enabled by default
- **Admin elevation:** not required at launch; temps-disabled mode is first-class
- **Click-through hotkey:** `Ctrl+Alt+M`

## 15. Testing Strategy

| Layer | What we test | How |
|---|---|---|
| Core | `MainViewModel` state, EMA math, threshold classifier, settings round-trip/defaults | xUnit, pure unit tests |
| Hardware | `HardwareMonitor` with fake `IComputer`; channel drop-oldest semantics; null-temps when unelevated | xUnit + handwritten fakes |
| Tray | Deterministic icon rendering (pixel-buffer hash); `DestroyIcon` called exactly once per swap | xUnit + headless `System.Drawing` |
| Windowing | AppBar state-machine; hotkey registration idempotency | xUnit; Win32 calls wrapped behind interfaces for fakes |
| App | DI container resolves; settings file round-trip | xUnit |
| Manual QA | Transparency on Win11 + Win10; click-through; fullscreen auto-hide with a real game; multi-monitor drag; DPI 100/125/150/200%; 72-hour handle-leak soak (Process Explorer) | `docs/qa/manual-test-plan.md` checklist |

**Non-goals for v1 testing:** automated WPF UI tests (FlaUI/Appium) — disproportionate investment for a widget of this size.

## 16. Performance Budget

Enforced by a startup self-check that logs violations to debug output.

- Steady-state CPU: ≤ 0.3% of one core (measured over 30 s)
- Working set: ≤ 80 MB
- GDI handles: ≤ 50
- USER handles: ≤ 100
- Sensor polling latency: median ≤ 30 ms

## 17. Build & Packaging

- **TargetFramework:** `net8.0-windows`, `UseWPF=true`, `RuntimeIdentifier=win-x64`
- **Publish:** `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
- **Output:** single `PerfMonitor.exe` (~80 MB), no runtime install required
- **No installer** in v1 — portable exe in `%LOCALAPPDATA%\PerfMonitor\`. Uninstall = delete folder + `%APPDATA%\PerfMonitor\`.
- **Application manifest:** Windows 10/11 compat GUIDs, PerMonitorV2 DPI awareness, `requestedExecutionLevel=asInvoker`
- **CI:** GitHub Actions `build.yml` — `dotnet test` + `dotnet publish` on push; release artifacts on tag
- **Versioning:** SemVer via `Directory.Build.props`

## 18. Phased Build Order

Each phase is independently shippable; phases marked ⟂ can be executed in parallel by sub-agents.

1. **Core skeleton** — solution/project scaffolding, DI, `MetricsSnapshot`, `MainViewModel`, fake hardware source, settings JSON
2. **Real hardware** — LHM integration, elevation detection, EMA smoothing
3. **⟂ Floating widget** — transparent WPF window, glassmorphic pill UI, drag, Mica
4. **⟂ Tray icons** — 4-icon render loop, `DestroyIcon` discipline, tooltips, context menu
5. **⟂ Docked bar mode** — `SHAppBarMessage` integration, mode toggle
6. **⟂ Behaviors** — fullscreen auto-hide, click-through hotkey, startup registration
7. **Settings window** — tabbed UI, threshold editors, whitelist, About
8. **Polish & QA** — 72-hour soak, DPI sweeps, Win10 fallback verification, README

Phases 3 + 4 can run in parallel; phases 5 + 6 can run in parallel after 3 lands.

## 19. Open Questions Deferred

- Whether to also support a **widget-style mini graph** (60-sec sparkline per metric) in v2
- Whether to integrate **PresentMon** for game framerate in a future release
- Whether to publish as an **MSIX package** in the Microsoft Store once stabilized
