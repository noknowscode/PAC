# HANDOFF — PacConsole solid plant rendering port
Updated: 2026-10-16 02:45

## Operations (SPEC-OPERATIONS.md), 2026-10-16
The console now handles an alarm from start to finish, as described in SPEC-OPERATIONS.md:

- an alarm band with acknowledge, acknowledge all and shelve
- the alarm response in the inspector (cause, steps, consequence)
- a faceplate per unit, with select-before-operate and permissives
- range bars on reading tiles
- the operator log with shift notes

The simulation responds to actions (`PlantOps`, `PlantEffects`). Alarms are derived by replaying 15 min (`AlarmBook`), with deadbands.

- **Commits:** `c52f64e`, then the deadband fix. **Tests:** 193. **Build:** 0 warnings. **Lint:** gating rules 0, apart from the allowed brand colours (HEX) and the theme repaint (TOKENTHEME).
- **Verified via the App MCP:** see SPEC-OPERATIONS.md, "Build status".
- **Open:**
  - per-cell fan animation in the drawing (the fans do not stop when a cell stops)
  - alarm history beyond 15 min
  - operator identity is a fixed "Operator 04"
  - feed-parameter commands are disabled for the first seconds after launch, until the snapshot feed delivers (expected MVUX behaviour)
  - graphite was checked only at 900 px with no alarm rows

## Since the v3 build (2026-10-15 evening to 2026-10-16)
- **Commits:** `e33820b` full-bleed page + trend gradient fill; `60caedc` system inks, glow and motion; `719b24f` electrical pulses; `ef094c0` audit fixes; `390c482` dead-code removal, slower net output, inspector 340/400.
- **Systems:** isolating a system outlines and glows its parts in its ink and runs its motion (steam flow and vent, cooling mist, flue smoke, electrical beads and rings). Inks live in Tokens.xaml / Palette.Dark.xaml as `System*Brush`.
- **Audit (uno-audit):** lint gating rules all 0 except HEX 18 (Simple role overrides, allowed) and TOKENTHEME 53 (in-place repaint, allowed). Uno0001 is an error. Uno.Sdk 6.7.30 is the latest stable (checked 2026-10-16). The LiveCharts package is gone.
- **Tests:** 158/158 (20 v2 tests removed with their code). One overnight run read 2 h 35 min of wall clock across a sleep; a normal run is about a minute.
- **Open:**
  - graphite not re-checked since the system inks
  - `WireframeThumb.cs` and the legacy `PlantGeometry` remain (phase 7 cleanup needs the user's OK)
  - FluentAssertions stays on 6.x (8.x is a commercial licence)
  - a perf re-measure of the v3 page with motion

## Console page v3 (SPEC-CONSOLE.md), built this session
The page now follows the user's mock: header with the alert pill, status strip, systems bar, the plant panel beside the equipment inspector (Overview / Condition / Control), synchronized trends, and the footer. Selection is slate-teal and alerts are burnt orange.
- **Commits:** `d9dcd43` (core + CND-07 condenser + system dimming), `854a99b` (page, TrendChart, tokens).
- **Verified via the App MCP** (shots in `docs/spike/v3-*.png`, untracked):
  - wide 1424 light and graphite
  - 1100 (side by side) and 900 (inspector stacked)
  - Electrical isolation dims parts and tags
  - callout and picker selection
  - compare toggles add trend rows
  - the shared cursor moves every row
  - 15 min / 1 h / 8 h
  - the Control tab shows only that unit's valves
- **Tests:** 178/178. **Build:** 0 warnings (Debug).
- **Behaviour changes to know:**
  - CLG-05 now drifts in and out of alarm, so the trends have events. The opening selection is pinned to CLG-05.
  - Trend samples are bucket means plus a 1-2-3-2-1 smoothing.
  - `ReadingContext.Around` is now mostly a ~50 min wave.
- **Open:**
  - Not yet run: the impeccable/lint pass and a perf re-measure. The page dropped the 4 SolidThumb canvases and 2 LiveCharts charts, the largest measured costs.
  - `OutputChart`, `MetricChart`, `SolidThumb` and the v2 `ConsoleView` fields are now unused. Deleting them needs the user's OK.
  - The MenuFlyout uses the stock Simple type size.
  - Auto orbit was kept as a footer toggle; the mock has none.

## Where we are (before v3)
The console is a quiet instrument (light base, graphite dark, IBM Plex Sans). The plant illustration now follows the user's reference (Andrew Kras, Isometric Swimming Pool):
- inked silhouettes and flat ground shadows
- a closed thermal cycle: boiler bay, fuel, steam, feedwater and ash runs with fittings and valves, a transformer yard and cabinets
- pipes tinted by circuit, with a legend

The plant opens still. The readings are interactive: picking one plots it in the presentation that suits the metric, and every chart shares one frame with a pointer and keyboard readout. Audit and fixes: `docs/audit/2026-10-15-interface.md`; plan: SPEC.md "Illustration fidelity".

## Last verified state
- Build: pass, `net10.0-desktop`, 0 warnings (Debug and Release).
- Tests: 161/161 (`dotnet test PacConsole.Tests`). Explicit timing tests: `FrameCostTests` (Core work 1.78 ms/frame in Release).
- Runtime (App MCP + direct Release runs): click-to-select, the light/dark round trip including charts, the 1100 × 720 stacked layout and the 1424 × 861 wide layout. Frame timing (Release, orbiting): render p50 2.3 ms; frame interval p50 8 ms, **p90 45 ms**.
- Git (before v3): `main` at `ac8e03f`. Lint: CARD 0, ICON 0, HEX 18 (Simple role overrides in App.xaml), TOKENTHEME 35 (allowed, in-place repaint), CODEBEHIND 7 (allowed glue).
- Redesign verified via App MCP: wide 1424 x 861 light and graphite, the round trip, and the 1100 x 720 stack (tree bounds). Shots: docs/shots-fukasawa-light.png, docs/shots-fukasawa-graphite.png, docs/shots-before-fukasawa.png.

## Next actions (in order)
1. **Performance (open; Debug Checkpoint, 2026-10-15 evening).** Release, Intel Iris Xe at 59 Hz, measured with per-thread OS CPU, `dotnet-trace` and `dotnet-gcdump`. Scripts are in the session scratchpad (`perframe.ps1`, `run10.ps1`, `threadmods.ps1`).
   - **Committed build:** about 150% CPU and about 5 plant frames per second. The plant's own render is 1.6 ms, about 0.5% of a frame's CPU.
   - **Where the CPU goes:**

     | Thread | CPU | Activity |
     |---|---|---|
     | Uno render thread | about 110% | `SKCanvas.Flush` / `GRContext.Flush` and `DrawPicture`, replaying every element's recording every frame |
     | Native driver thread | about 95% | No managed frames |
     | UI thread | about 18% | Layout, bindings, our C# drawing |

   - **Ruled out:**
     - the rendering backend (OpenGL, Vulkan and Software all about 110%)
     - `Win32SystemThemeHelper` (blocked in a wait, not spinning)
     - GC and allocation (2.5 MB/s, about 0.2 gen0/s)
     - the shadow path
     - the picture cache
     - invalidating from inside Rendering
   - **Blank page:** 0% CPU.
   - **Tried and reverted:** rasterising static plant layers into `SKImage`s (copy at `scratchpad/SolidRenderer.images.cs`). It made things worse: `RenderOverride` receives a picture-recording canvas (no `GRContext`), so the images were CPU rasters.
   - **Kept** (`ac8e03f`): one shared 20 fps ambient clock, and redraws skipped on unchanged data.
   - **Next options:**
     - (a) Render the four plant thumbnails once into bitmaps shown by XAML `Image` controls, so the compositor owns the textures.
     - (b) Pause ambient motion (fans, flow, pulse) when the window is idle or unfocused.
     - (c) Reduce per-frame vector work (fewer paths per part).
     - (d) A reduced repro for Uno: flush cost proportional to SKCanvasElement content on every frame.
   - **Measurements were contaminated by other load:** a `dotnet build -f net10.0-android` running six `aapt2` daemons at about 100% each, Flatpack at 138% and qemu at 50%.
2. **Record runtime gotchas with evidence** in `~/.claude/rules/uno-runtime-gotchas.md` (handoff skill: export transcript excerpts to `docs/evidence/`):
   1. A keyed ResourceDictionary nested in a merged dictionary stops the window from appearing.
   2. Simple's implicit TextBlock foreground beats inheritance inside buttons.
   3. LiveCharts paint replacement freezes a chart.
   4. A collapsed LiveCharts chart disposes a shared label typeface and blanks every chart.
   5. MVUX proxies nested records in place, so a dependency property bound to the whole record never changes.
   6. ItemsRepeater over a new array each tick recycles buttons: stale visual states and misrouted clicks.
   7. `ImmutableArray` passed as `IReadOnlyList` boxes per call, which defeats reference-equality caches.
3. **Remaining SPEC items.** The unit preview gauge, then phase 7 cleanup (deleting `Wireframe*` needs the user's OK).

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
