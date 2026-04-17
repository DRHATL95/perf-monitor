# CLAUDE.md — PerfMonitor

This file provides guidance to Claude Code (claude.ai/code) when working in this repository.

## Project Overview

**PerfMonitor** is a lightweight always-on desktop performance widget for Windows 11 (Windows 10 fallback). Displays live CPU/RAM/GPU load, network throughput, and CPU/GPU temperatures via a transparent glassmorphic pill-row widget (floating or edge-docked) plus 4 live-rendered system-tray icons.

The authoritative design lives in [`docs/superpowers/specs/2026-04-17-perf-monitor-design.md`](docs/superpowers/specs/2026-04-17-perf-monitor-design.md). Defer to it for every design decision.

## Tech Stack

- **.NET 8 / WPF** (`net8.0-windows`, `UseWPF=true`, `win-x64`)
- **LibreHardwareMonitorLib** — hardware sensors (CPU/GPU/fans/temps). Requires admin for temperatures.
- **H.NotifyIcon.Wpf** — system tray icon hosting
- **Microsoft.Win32.TaskScheduler** — startup registration (preferred over `HKCU\Run`)
- **Microsoft.Extensions.DependencyInjection** — DI composition
- **xUnit** — unit tests
- **`System.Text.Json`** — settings persistence

## Solution Layout

```
PerfMonitor.sln
src/
  PerfMonitor.App/           WPF entry, App.xaml, DI composition root
  PerfMonitor.Core/          MainViewModel, MetricsSnapshot, settings
  PerfMonitor.Hardware/      LibreHardwareMonitor wrapper
  PerfMonitor.Windowing/     WPF windows, AppBar interop, hotkeys
  PerfMonitor.Tray/          Tray icon rendering
  PerfMonitor.Startup/       Task Scheduler registration
tests/
  PerfMonitor.<Layer>.Tests/
docs/
  superpowers/specs/         Design specs (authoritative)
  qa/manual-test-plan.md     Manual QA checklist
```

Each subproject must be independently testable. `PerfMonitor.App` is the only project that wires them together.

## Build & Run

```bash
# Restore & build
dotnet restore
dotnet build

# Run tests (all layers)
dotnet test

# Run the app (debug)
dotnet run --project src/PerfMonitor.App

# Publish single-file self-contained exe (~80 MB)
dotnet publish src/PerfMonitor.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Key Design Constraints

These are non-negotiable — they come from the approved spec:

1. **Single MainViewModel** shared by floating widget, docked bar, and tray icons. One source of truth; three render paths.
2. **Sensor polling runs on a dedicated background thread.** Never on the UI dispatcher. Snapshots published via `Channel<MetricsSnapshot>(capacity=1, FullMode=DropOldest)`.
3. **`DestroyIcon` discipline.** Every tray icon swap MUST destroy the previous `HICON`. Forgetting this leaks GDI handles and kills the process within hours.
4. **AppBar unregister must be bulletproof.** `SHAppBarMessage(ABM_REMOVE)` must fire on exit, mode-switch, crash handler. A leaked appbar permanently shrinks the user's work area until reboot.
5. **Graceful degradation, not hard requirements.** App starts unelevated and shows `—` for temps. Missing GPU sensors hide the GPU pill. Corrupt settings file loads defaults. Never refuse to launch.
6. **Win32 interop goes behind interfaces.** Every Win32 P/Invoke call (`SHAppBarMessage`, `RegisterHotKey`, `SetWindowLong`, `GetForegroundWindow`) must be wrapped in a service interface so tests can fake it.
7. **Performance budget enforced.** Steady-state ≤0.3% CPU, ≤80 MB working set, ≤50 GDI handles, ≤100 USER handles. Startup self-check logs violations.

## Code Conventions

- **File-scoped namespaces** (`namespace PerfMonitor.Core;`)
- **Nullable reference types enabled** solution-wide (`<Nullable>enable</Nullable>`)
- **`record` for value types** (`MetricsSnapshot`, `FanReading`, settings classes)
- **`INotifyPropertyChanged` via `CommunityToolkit.Mvvm`** (`[ObservableProperty]` source generators) — avoids hand-written boilerplate
- **DI via constructor injection**; no service locator
- **Central package versions** in `Directory.Packages.props`
- **No code comments explaining WHAT code does** — only WHY when non-obvious (e.g., "WS_EX_TRANSPARENT set to enable click-through per issue 42" is fine; "set window to transparent" is not)

## Testing Conventions

- `xUnit` + `FluentAssertions`
- Fakes are handwritten; avoid Moq unless a mock is unavoidable
- Name tests `Method_State_ExpectedResult` (e.g., `Smooth_TwoSamples_ReturnsEmaAverage`)
- Each subproject has a parallel `.Tests` project; no cross-layer test projects
- WPF UI is NOT automation-tested in v1 — manual QA checklist covers it

## Git Conventions

- `main` is the release branch; feature work on `feature/<phase-name>` branches
- Conventional Commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`)
- Spec changes require a commit that touches only `docs/superpowers/specs/`
- `.superpowers/` is gitignored (brainstorm session artifacts)

## Platform-Specific Quirks

- **Transparency + Mica:** Win11 supports `DWMWA_SYSTEMBACKDROP_TYPE`. On Win10, fall back to flat 85%-opaque dark fill.
- **Tray icons on Win11:** default to hidden unless the user pins them. First-run prompt must explain this.
- **DPI:** app is PerMonitorV2-aware via manifest. Icon rendering must honor per-monitor DPI or icons look blurry on 150% scaling.
- **Temp sensors:** require elevation because LHM loads a kernel driver for MSR/SMBus access. Unelevated mode is first-class — don't hide it.
