# HANDOFF — PacConsole solid plant rendering port
Updated: 2026-10-15 16:45

## Where we are
The console was redirected from a dark command center to a quiet instrument (.impeccable.md, Direction 2: Fukasawa calm plus a journal figure plate). Light is the base theme and graphite the dark one, with one family (IBM Plex Sans, tabular digits). The plant is Fig. 1, drawn on the ground with no frame; readings sit in one datasheet column with hairline rules and no cards; orange marks only the selected unit and live data. The plant drawings, selection, lift, motion and the runtime theme switch are unchanged in behaviour. A tail of slow frames during auto-orbit remains (see Next actions 1).

## Last verified state
- Build: pass, `net10.0-desktop`, 0 warnings (Debug and Release).
- Tests: 149/149 (`dotnet test PacConsole.Tests`). Explicit timing tests: `FrameCostTests` (Core work 1.78 ms/frame in Release).
- Runtime (App MCP + direct Release runs): click-to-select, the light/dark round trip including charts, the 1100 × 720 stacked layout and the 1424 × 861 wide layout. Frame timing (Release, orbiting): render p50 2.3 ms; frame interval p50 8 ms, **p90 45 ms**.
- Git: `main` at `9b143d9` (redesign). Lint: CARD 0, ICON 0, HEX 18 (Simple role overrides in App.xaml), TOKENTHEME 35 (allowed, in-place repaint), CODEBEHIND 7 (allowed glue).
- Redesign verified via App MCP: wide 1424 x 861 light and graphite, the round trip, and the 1100 x 720 stack (tree bounds). Shots: docs/shots-fukasawa-light.png, docs/shots-fukasawa-graphite.png, docs/shots-before-fukasawa.png.

## Next actions (in order)
1. Stutter tail, traced (`dotnet-trace`, `dotnet-sampled-thread-time`, Release, orbiting): our managed work is about 2.7 ms per frame. About two thirds of UI-thread time is native: Skia drawing (about 1,100 `DrawPath` calls per frame plus the halo blur) and Uno's flush, present and damage regions (`SKPath.Op`). Orbiting rebuilds all 139 shapes per frame, allocating about 74 MB/s (GC about 17 ms/s). Result: p50 7 ms, p90 about 38 ms (missed refreshes). Waiting on the user's choice: (a) auto-orbit off by default (static camera = cached plant, smooth), (b) fewer draw calls per part and no blur while orbiting, or (c) a steady 30 fps orbit.
2. Record three runtime gotchas in `~/.claude/rules/uno-runtime-gotchas.md`, with evidence excerpts per the handoff skill:
   - A keyed `ResourceDictionary` nested in a merged dictionary stops the desktop window from ever appearing, silently (process alive, no window).
   - Simple's implicit `TextBlock` style sets Foreground locally, so an explicit `TextBlock` inside a Button or ToggleButton ignores the control's Foreground (fix: an explicit empty style).
   - LiveCharts2 2.0.5: replacing a paint on a drawn chart disposes the shared label `SKTypeface` and freezes that chart (fix: recolour paints in place).
3. Remaining SPEC items: the unit preview's vibration gauge (`SelectedVib`, `VibrationLimit`), then phase 7 cleanup (delete `Wireframe*` and the `PlantGeometry` segment builders; deletions need the user's OK).

## Open questions
- Chakra Petch and IBM Plex Mono font files are no longer referenced; deleting them needs the user's OK.
- Uno Platform sign-in had lapsed (Hot Reload status asked to sign in), so XAML edits needed a rebuild.
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
