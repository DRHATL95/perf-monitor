# PerfMonitor

Lightweight always-on desktop performance widget for Windows 11 (Windows 10 fallback).
Displays live CPU / RAM / GPU load, network throughput, and CPU/GPU temperatures via
a transparent glassmorphic pill-row widget and four live-rendered system-tray icons.
Click a pill to drill into the top processes driving that metric.

## Quick start

    git clone <repo>
    cd perf-monitor
    dotnet run --project src/PerfMonitor.App

## Install

Download the latest `PerfMonitor-v*-setup.exe` from [Releases](../../releases)
and run it. Installs per-user to `%LOCALAPPDATA%\Programs\PerfMonitor`; no
admin required. Self-contained — no .NET runtime install needed.

> The installer isn't code-signed yet, so Windows SmartScreen will show
> "Windows protected your PC" on first download. Click **More info → Run
> anyway**. Code signing is on the v0.2 roadmap.

## Build locally

    ./build.ps1               # default: multi-file self-contained (AV-safe)
    ./build.ps1 -SingleFile   # single ~160 MB bundle (may trigger antivirus)
    ./build.ps1 -FrameworkDependent  # tiny exe, requires .NET 8 runtime installed

Default output: `publish/PerfMonitor.exe` + sibling DLLs (~165 MB folder, no
runtime install needed on target). To build the installer locally:

    ./build.ps1
    & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DMyAppVersion=0.1.3 scripts\installer.iss

## Features

- **Floating widget** — transparent, draggable, glassmorphic pill row (CPU / RAM /
  GPU / NET). Position and monitor persist across restarts.
- **OnTop mode** — always-visible over fullscreen apps with periodic topmost
  re-assert for games that briefly push ahead in Z-order. Combine with
  click-through for a gaming overlay.
- **Click-through** — mouse events fall through the widget to apps underneath.
  Toggle with `Ctrl+Alt+M` or set as default in Settings.
- **Live tray icons** — four 16×16 icons render current values directly in the
  system tray; right-click any icon for Show/Hide, Settings, or Exit.
- **Process drill-down** — click any pill to expand a panel showing the top
  processes by that metric. Right-click a row for "End task". Auto-opens on
  CPU when load crosses the Crit threshold.
- **Auto-hide on fullscreen** — Floating mode fades the widget out when another
  app goes fullscreen (toggleable).
- **Per-monitor safety** — if a saved position becomes invalid (disconnected
  monitor), the widget falls back to the primary display instead of opening
  off-screen.

## Settings

JSON file at `%APPDATA%\PerfMonitor\settings.json`. All changes apply live on
Apply — no restart required. Corrupt file → backed up to `.bak` and defaults
restored. A crash log (unhandled exceptions) writes to
`%APPDATA%\PerfMonitor\crash.log` if anything goes wrong.

## Admin elevation

Required only for CPU/GPU temperatures (LibreHardwareMonitor loads a kernel
driver for MSR/SMBus access). App starts unelevated; temps display `—`.
Per-process "End task" on elevated targets also needs admin.

## Hotkeys

- **Ctrl+Alt+M** — toggle click-through (widget stops catching mouse clicks)

## Project layout

    src/
      PerfMonitor.Core/        records, view models, settings
      PerfMonitor.Hardware/    LibreHardwareMonitor + process + GPU-engine samplers
      PerfMonitor.Windowing/   WPF windows, controls, behaviors
      PerfMonitor.Tray/        system-tray icons + context menu
      PerfMonitor.Startup/     Task Scheduler autostart
      PerfMonitor.App/         composition root + DI
    tests/                     xUnit + FluentAssertions

See [`docs/superpowers/specs/2026-04-17-perf-monitor-design.md`](docs/superpowers/specs/2026-04-17-perf-monitor-design.md)
for the original design. [`CLAUDE.md`](CLAUDE.md) has the current
source-of-truth conventions.

## Screenshots
<img width="329" height="69" alt="PerfMonitorOverlay" src="https://github.com/user-attachments/assets/d5fb9487-5edd-4996-bb1d-bf1c038b3df8" />

<img width="71" height="95" alt="PerfMonitorTray" src="https://github.com/user-attachments/assets/317b8b3c-0072-47d9-9e91-5f30a6c97ce2" />

<img width="420" height="352" alt="PerfMonitorSettings" src="https://github.com/user-attachments/assets/00411884-43bb-432d-a9fb-f9ebdcf468ed" />



