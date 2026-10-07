# Todo

Collection of improvement ideas. Move items to "Done" (or delete them) once implemented.
Priority: 🔴 high · 🟡 medium · 🟢 nice to have

## Bugs / Robustness
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
- [ ] 🟡 **Capture follow-ups**: exe grew 0.6 MB → 28 MB, almost entirely the WinRT projection for WGC – drop WGC (DXGI alone is small) or keep it optional; DXGI/WGC only capture the monitor under the area center (no areas spanning two monitors) and ignore rotated monitors; double-buffered staging texture to cut the ~5 ms readback stall; WinForms timer caps at ~20–30 ticks/s.
- [ ] 🟡 **Virtual camera follow-ups**: autostart with Windows (Run key) + tray icon so the camera is always available; feedback from the pre-release (Teams image quality, other apps); code signing for the camera DLL.
- [ ] 🟢 **Tray icon** with menu (Set, mouse on/off, color, exit).
- [ ] 🟢 **Highlight mouse clicks** in the mirrored image (helpful in presentations).
- [ ] 🟢 **Profiles**: save/restore several named capture areas (e.g. `--profile left-half`).

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
- [x] Virtual camera (`--virtual-camera`, Windows 11 x64): native Media Foundation source, one-time setup to Program Files via UAC (`--install-camera` / `--uninstall-camera`), built in CI
- [x] GDI leak in render loop (timer-driven rendering instead of endless paint thread)
- [x] Configurable frame rate (`--fps`, default 30)
- [x] Modern capture API: `--capture dxgi|gdi|wgc` (default DXGI Desktop Duplication, automatic fallback to GDI), `--stats` overlay
- [x] Claude workspace (`CLAUDE.md`) and automatic issue triage
- [x] Release workflow: one-click release, version in exe, release notes from commits since the last release
