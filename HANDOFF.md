# HANDOFF — PacConsole solid plant rendering port
Updated: 2026-10-15 18:15

## Where we are
The console is a quiet instrument (light base, graphite dark, IBM Plex Sans). The plant illustration now follows the user's reference (Andrew Kras, Isometric Swimming Pool):
- inked silhouettes and flat ground shadows
- a closed thermal cycle: boiler bay, fuel, steam, feedwater and ash runs with fittings and valves, a transformer yard and cabinets
- pipes tinted by circuit, with a legend

The plant opens still. The readings are interactive: picking one plots it in the presentation that suits the metric, and every chart shares one frame with a pointer and keyboard readout. Audit and fixes: `docs/audit/2026-10-15-interface.md`; plan: SPEC.md "Illustration fidelity".

## Last verified state
- Build: pass, `net10.0-desktop`, 0 warnings (Debug and Release).
- Tests: 161/161 (`dotnet test PacConsole.Tests`). Explicit timing tests: `FrameCostTests` (Core work 1.78 ms/frame in Release).
- Runtime (App MCP + direct Release runs): click-to-select, the light/dark round trip including charts, the 1100 × 720 stacked layout and the 1424 × 861 wide layout. Frame timing (Release, orbiting): render p50 2.3 ms; frame interval p50 8 ms, **p90 45 ms**.
- Git: `main` at `3222aa9`. Lint: CARD 0, ICON 0, HEX 18 (Simple role overrides in App.xaml), TOKENTHEME 35 (allowed, in-place repaint), CODEBEHIND 7 (allowed glue).
- Redesign verified via App MCP: wide 1424 x 861 light and graphite, the round trip, and the 1100 x 720 stack (tree bounds). Shots: docs/shots-fukasawa-light.png, docs/shots-fukasawa-graphite.png, docs/shots-before-fukasawa.png.

## Next actions (in order)
1. **Still-frame interval, about 90 ms, unexplained** (Release probe).
   - Render cost is 0.6 ms after the boxed-parts fix and the picture cache, and the UI thread idles 94% of the time.
   - Ruled out: the shadow path, the picture cache, invalidation from inside Rendering (a kick timer didn't help) and the finalizer load (present in the morning trace too).
   - Next: trace the render thread and GPU (GL present), and compare against commit `a73d94c`, which predates today, in still mode.
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
