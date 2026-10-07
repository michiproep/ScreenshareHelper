# ScreenshareHelper

Small Windows Forms tool (.NET 10, `net10.0-windows`) that mirrors a chosen screen area into its own window, so users can share just that window in Teams/Zoom instead of a huge ultrawide monitor.

## Layout
- `src/ScreenshareHelper.sln` – solution (single project, no tests yet)
- `src/ScreenshareHelper/Program.cs` – entry point, CLI parsing (CommandLineParser), `--color` parsing, SnapToProcess via Win32/DWM
- `src/ScreenshareHelper/Options.cs` – command line options (`-n`, `-i`, `--no-mouse`, `--color`, `--auto-set`, `--capture`, `--fps`, `--stats`)
- `src/ScreenshareHelper/Form1.cs` – the capture window: timer-driven rendering, mouse pointer mirroring, "Set" button, position persistence
- `src/ScreenshareHelper/Capture/` – capture methods behind `IScreenCapture`: DXGI Desktop Duplication (default), GDI, Windows.Graphics.Capture (Vortice.Direct3D11 + WinRT interop)
- `src/ScreenshareHelper/Properties/Settings.settings` – user settings (capture area, window position, background color); keep `Settings.Designer.cs` in sync
- `.github/workflows/` – `build.yml` (CI on push/PR), `release.yml` (publish + GitHub Release on tag), `claude-issue-triage.yml` (Claude comments new issues), `claude.yml` (@claude mentions), `claude-code-review.yml` (PR review)

## Build & run
```
dotnet build src
dotnet run --project src/ScreenshareHelper
dotnet publish src/ScreenshareHelper/ScreenshareHelper.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true
```
Windows only – the app uses WinForms and P/Invoke (user32, gdi32, dwmapi, kernel32).

## Conventions
- Keep it simple: this is a small single-project tool, no extra frameworks.
- Win32 interop lives next to the code that uses it (`#region Win32`).
- New CLI options: add to `Options.cs`, wire up in `Program.Main`, and document in `readme.md` under "Command Line Options".
- Release binaries are built by CI, never commit `bin/` output.
- Improvement ideas are collected in `Todo.md`; tick items off there when implementing them.
