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
