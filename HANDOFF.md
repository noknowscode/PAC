# HANDOFF — PacConsole solid plant rendering port
Updated: 2026-10-15 14:30

## Where we are
SPEC phases 1–3 are done, and most of phases 4–6 landed early at the user's request. Every plant drawing (viewport, ISO / elevation / plan presets, unit preview) now renders as shaded solids in the console palette (`Solid*` tokens). Tags are pinned to their units through one shared projection (`PlantSolids.ViewProjection`) with leader lines. The plant has the figure's line work and motion: the selected unit lifts with a shadow under it, the condenser fans turn, and flow dashes run along the pipes. Alerting units get a dashed coral outline. A ui-craft / impeccable audit / polish pass ran on the whole console: safe fixes are committed, and the larger findings wait on the user (see Open questions). Design context is recorded in `.impeccable.md`.

## Last verified state
- Build: pass, `net10.0-desktop`, **0 warnings** (Pulse AutoReverse and the Control.CharacterSpacing setters removed).
- Tests: 144/144 (`dotnet test PacConsole.Tests`, ~15 s, mostly the z-buffer order test).
- Runtime (App MCP): tags stay on their units through orbit and rotate steps. Selecting TKA moves the lift, and CND drops to the dashed alert outline. All five drawings are solid. Not verified: keyboard focus order (`uno_app_key_press` failed), narrow or short windows (the MCP cannot resize), frame cost.
- Git: `main` at `3bd340b`. Untracked: `.mcp.json`, figure screenshots, an empty `docs/spike/`.

## Next actions (in order)
1. Measure the per-frame cost (139 shapes + details rebuilt at 30–60 fps). Budget < 4 ms; cache static parts per pose if over.
2. Remaining phase 4 items: hover outline + click-to-select hit test on the drawing; the unit preview's vibration gauge plus the `SelectedVib`/`VibrationLimit` fields (phase 6); phase 7 cleanup (delete `Wireframe*` and the `PlantGeometry` segment builders; the user approves deletions).
3. Walk the user through the audit's open findings below, then decide whether to run `xaml-art-direction` (they said they are open to a new direction).

## Open questions
- Six of the seven rail sections open "NOT IN THIS BUILD". In a demo that impersonates capability: remove them, or disable them with a reason?
- The selected unit is shown five times (tag, tile, SELECTED overlay, preview, telemetry header), and vibration twice (VIB tile + VIBRATION card).
- Identity: cyan-on-dark with neon amber, monospace caps everywhere and pulsing dots read as generic "sci-fi dashboard". The user is open to a new direction.
- Values use proportional digits (Chakra Petch), so live numbers shift width; tabular numerals were not verified on Uno.
- Release path: no remote or CI (kept out of scope by SPEC → Decisions).

## Relaunch
```
cd C:\Users\Platform006\UnoProjects\PacConsole
dotnet build PacConsole/PacConsole.csproj -f net10.0-desktop
# rooted session: uno_app_start (PacConsole\PacConsole.csproj, net10.0-desktop); close + Stop-Process before rebuilding
```
