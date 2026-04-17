# PerfMonitor

Lightweight always-on desktop performance widget for Windows 11 (Windows 10 fallback).
Displays live CPU / RAM / GPU load, network throughput, and CPU/GPU temperatures via
a transparent glassmorphic pill-row widget and four live-rendered system-tray icons.

## Quick start

    git clone <repo>
    cd perf-monitor
    dotnet run --project src/PerfMonitor.App

## Publish a single-file exe

    dotnet publish src/PerfMonitor.App -c Release -r win-x64 --self-contained \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

Output: `src/PerfMonitor.App/bin/Release/net8.0-windows/win-x64/publish/PerfMonitor.exe`.

## Settings

JSON file at `%APPDATA%\PerfMonitor\settings.json`. Corrupt file → backed up to `.bak` and defaults restored.

## Admin elevation

Required only for CPU/GPU temperatures. App starts unelevated; temps show `—`.

## Hotkeys

- **Ctrl+Alt+M** — toggle click-through (widget stops catching mouse clicks)

See `docs/superpowers/specs/2026-04-17-perf-monitor-design.md` for full design.
