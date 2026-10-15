# HANDOFF — PacConsole solid plant rendering port
Updated: 2026-10-15 15:10

## Where we are
Every plant drawing renders as solids with the figure's line work and motion (lift + shadow, turning fans, pipe flow, dashed alert outline). Units select by clicking the drawing as well as tags and tiles. The design-audit findings are resolved: dead rail sections and the selected-unit overlay are removed, the vibration duplication is dropped, live figures use tabular mono digits, control borders pass 3:1, and below 1360 px the telemetry column stacks under the plant and the body scrolls. A **light theme** (slate greys, burnt orange, whites) switches at runtime from the header toggle. Animation stutter was partly fixed; a tail of slow frames remains (see Open questions).

## Last verified state
- Build: pass, `net10.0-desktop`, 0 warnings (Debug and Release).
- Tests: 149/149 (`dotnet test PacConsole.Tests`). Explicit timing tests: `FrameCostTests` (Core work 1.78 ms/frame in Release).
- Runtime (App MCP + direct Release runs): click-to-select, the light/dark round trip including charts, the 1100 × 720 stacked layout and the 1424 × 861 wide layout. Frame timing (Release, orbiting): render p50 2.3 ms; frame interval p50 8 ms, **p90 45 ms**.
- Git: `main` at `a73d94c`.

## Next actions (in order)
1. Stutter tail: capture a Release `dotnet-trace` while orbiting and find what fills the 40–50 ms frames (our code is about 2.4 ms per frame). Then consider reusing shape buffers (allocation is 70–80 MB/s) or a steady 30 fps ambient cap.
2. Record three runtime gotchas in `~/.claude/rules/uno-runtime-gotchas.md`, with evidence excerpts per the handoff skill:
   - A keyed `ResourceDictionary` nested in a merged dictionary stops the desktop window from ever appearing, silently (process alive, no window).
   - Simple's implicit `TextBlock` style sets Foreground locally, so an explicit `TextBlock` inside a Button or ToggleButton ignores the control's Foreground (fix: an explicit empty style).
   - LiveCharts2 2.0.5: replacing a paint on a drawn chart disposes the shared label `SKTypeface` and freezes that chart (fix: recolour paints in place).
3. Remaining SPEC items: the unit preview's vibration gauge (`SelectedVib`, `VibrationLimit`), then phase 7 cleanup (delete `Wireframe*` and the `PlantGeometry` segment builders; deletions need the user's OK).

## Open questions
- The theme choice isn't persisted (it resets to dark on launch).
- The lint still flags TOKENTHEME (theme values live in `Palette.Light.xaml` and are copied into brushes, a deliberate fallback per the converter gotcha), plus the existing HEX, ICON and CODEBEHIND counts (+1 for the theme click handler, marked allow).
- Debug builds (what `uno_app_start` launches) render about 3× slower; demo in Release.
- Release path: no remote or CI.

## Relaunch
```
cd C:\Users\Platform006\UnoProjects\PacConsole
dotnet build PacConsole/PacConsole.csproj -f net10.0-desktop
# App MCP: uno_app_start (Debug). If it never connects: uno_app_select_solution forceRestart, then retry.
# Demo / timing: dotnet run --project PacConsole -f net10.0-desktop -c Release
```
