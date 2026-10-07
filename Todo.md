# Todo

Collection of improvement ideas. Move items to "Done" (or delete them) once implemented.
Priority: 🔴 high · 🟡 medium · 🟢 nice to have

## Bugs / Robustness
- [ ] 🔴 **GDI leak in render loop**: `Form1_Load` creates `Graphics.FromHwnd(h)` every 100 ms and never disposes it. Use `using`, or better: render via a `System.Windows.Forms.Timer` + `Invalidate()`/double buffering on the UI thread instead of an endless background thread.
- [ ] 🟡 **Mouse pointer offset**: `CopyMousePointer` ignores the cursor hotspot (I-beam, hand, resize cursors appear shifted) and uses a hard-coded border offset. Use `GetIconInfo` for the hotspot and compute the offset from the actual capture origin; check with per-monitor DPI.
- [ ] 🟡 **Capture area only persisted on close**: `SetCaptureArea` doesn't call `Settings.Default.Save()`, so a crash/kill loses the area. Save right after "Set".
- [ ] 🟡 **SnapToProcess error handling**: `GetWindowBounds` can throw `Win32Exception` (crash at startup), leaks the `AllocHGlobal` buffer on error, and has dead code (`var tmp = GetProcesses()`, redundant `GetWindowRect`). Use `out RECT` overload of `DwmGetWindowAttribute`, handle failures gracefully.
- [ ] 🟢 **Silent catch blocks** in `paint` / `UpdateSizeDisplay`: at least `Debug.WriteLine` so problems are visible during development.
- [ ] 🟢 **Mixed-DPI multi-monitor setups**: verify capture coordinates when window and capture area are on monitors with different scaling.

## Features
- [ ] 🔴 **#4 Bring snapped process to front** when using `-n`/`-i` (open GitHub issue).
- [ ] 🟡 **Follow window**: optionally keep tracking the snapped process window when it moves/resizes (instead of a one-time snap).
- [ ] 🟡 **Visible capture border**: show a thin frame around the capture area (not captured itself) so you can see what is shared.
- [ ] 🟡 **Global hotkey** to (re)set the capture area or toggle mouse mirroring without focusing the window.
- [ ] 🟢 **Configurable frame rate** (`--fps`), currently fixed at ~10 fps.
- [ ] 🟢 **Tray icon** with menu (Set, mouse on/off, color, exit).
- [ ] 🟢 **Highlight mouse clicks** in the mirrored image (helpful in presentations).
- [ ] 🟢 **Profiles**: save/restore several named capture areas (e.g. `--profile left-half`).
- [ ] 🟢 **Modern capture API**: evaluate `Windows.Graphics.Capture` / DXGI Desktop Duplication for better performance and GPU-rendered content.
  - Prototype on branch `experiment/modern-capture` (`--capture gdi|dxgi|wgc --stats`). Measured 1600×900 area on 5120×1440 monitor:
    GDI ~26 ms/frame always; DXGI/WGC ~0.1 ms when nothing changed, ~5.5 ms when the area changed (GPU readback).
  - Exe size (framework-dependent): 0.6 MB → 28 MB, almost entirely the WinRT projection needed for WGC. DXGI alone (Vortice) is much smaller.
  - Open: real-world test (videos, browsers, games, multi-monitor, mixed DPI, rotated monitors, laptop with hybrid GPU), yellow WGC border on Windows 10, double-buffered staging to cut readback stalls.

## Code quality
- [ ] 🟡 **Test project**: unit tests for `TryParseColor` and option parsing; re-enable `dotnet test` in CI.
- [ ] 🟢 Rename `Form1` → `CaptureForm`, group Win32 interop into a `NativeMethods` class.
- [ ] 🟢 Update `CommandLineParser` 2.8.0 → 2.9.x.
- [ ] 🟢 Enable nullable reference types and fix warnings.

## Repo / Distribution
- [ ] 🟡 **Issue templates** (bug report / feature request) – gives the Claude triage better input (Windows version, monitor setup, DPI).
- [ ] 🟡 **Dependabot** for GitHub Actions and NuGet packages.
- [ ] 🟢 **Self-contained build** as additional release asset (no .NET runtime needed, ~70 MB).
- [ ] 🟢 **winget** package for easy install/update.
- [ ] 🟢 **Code signing** to avoid SmartScreen warnings.
- [ ] 🟢 README: fix typos, add a GIF/screenshot of the tool in action.

## Done
- [x] Claude workspace (`CLAUDE.md`) and automatic issue triage
- [x] Release workflow: one-click release, version in exe, generated release notes
