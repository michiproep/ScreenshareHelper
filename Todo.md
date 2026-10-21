# Todo

Collection of improvement ideas. Move items to "Done" (or delete them) once implemented.
Priority: 🔴 high · 🟡 medium · 🟢 nice to have

## Bugs / Robustness
- [ ] 🟡 **Mouse pointer offset**: `CopyMousePointer` ignores the cursor hotspot (I-beam, hand, resize cursors appear shifted) and uses a hard-coded border offset. Use `GetIconInfo` for the hotspot and compute the offset from the actual capture origin; check with per-monitor DPI.
- [ ] 🟡 **SnapToProcess error handling**: `GetWindowBounds` can throw `Win32Exception` (crash at startup), leaks the `AllocHGlobal` buffer on error, and has dead code (`var tmp = GetProcesses()`, redundant `GetWindowRect`). Use `out RECT` overload of `DwmGetWindowAttribute`, handle failures gracefully.
- [ ] 🟢 **Silent catch blocks** in `paint` / `UpdateSizeDisplay`: at least `Debug.WriteLine` so problems are visible during development.
- [ ] 🟢 **Mixed-DPI multi-monitor setups**: verify capture coordinates when window and capture area are on monitors with different scaling.

## Features
- [ ] 🔴 **#4 Bring snapped process to front** when using `-n`/`-i` (open GitHub issue).
- [ ] 🟡 **Follow window**: optionally keep tracking the snapped process window when it moves/resizes (instead of a one-time snap).
- [ ] 🟡 **Visible capture border**: show a thin frame around the capture area (not captured itself) so you can see what is shared. Done for the virtual monitor area (`AreaFrameForm`), could be reused for the normal capture area.
- [ ] 🟡 **Global hotkey** to (re)set the capture area or toggle mouse mirroring without focusing the window.
- [ ] 🟡 **Capture follow-ups**: DXGI only captures the monitor under the area center (no areas spanning two monitors) and ignore rotated monitors; double-buffered staging texture to cut the ~5 ms readback stall; WinForms timer caps at ~20–30 ticks/s.
- [ ] 🟡 **Virtual camera follow-ups**: feedback from the pre-release (Teams image quality, other apps); code signing for the camera DLL.
- [ ] 🟡 **Virtual monitor follow-ups**: render on the GPU (D3D11 swap chain, the DXGI frame is already a texture) instead of GDI+ scaling on the CPU; exact resolutions via custom modes (`HKLM\SOFTWARE\Parsecdd`, needs admin); silent driver install (`/S`, verify the Parsec signature) instead of running the interactive installer; "give control" in Teams maps to the virtual monitor, not the area; feedback from the pre-release.
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
- [x] Areas remembered per monitor layout (dock, lid closed, other primary monitor): known layouts restore their areas, a new one keeps them if they still lie on a monitor, otherwise asks to set the area (#23)
- [x] Tray icon with modes Normal / Virtual camera / Virtual monitor (remembered, no window in camera/vmon mode, "Set area..."), red frame and mouse pointer toggles, Start with Windows, Setup (install/uninstall camera and Parsec driver) (#22)
- [x] Robust capture: errors are retried instead of a permanent GDI fallback, error message cleared once capture works again; WGC dropped (exe 28 MB → 1.9 MB); CLI reduced to `-n`, `-i`, `--color` (#22)
- [x] Capture area saved right after "Set"
- [x] Virtual monitor (Parsec Virtual Display Driver): "Set for virtual monitor" with its own area, resolution drop-down (default 1920x1080, Auto), "16:9" button, monitor placed corner-to-corner so the mouse can't get lost; red 1px click-through frame around the shared area (excluded from capture)
- [x] Virtual camera (Windows 11 x64): native Media Foundation source, one-time setup to Program Files via UAC, built in CI
- [x] GDI leak in render loop (timer-driven rendering instead of endless paint thread)
- [x] Modern capture API: DXGI Desktop Duplication with automatic fallback to GDI
- [x] Claude workspace (`CLAUDE.md`) and automatic issue triage
- [x] Release workflow: one-click release, version in exe, release notes from commits since the last release
