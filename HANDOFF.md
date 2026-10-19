# HANDOFF — PacConsole perf pass, Active alarms view, art set
Updated: 2026-10-19

## Where we are
The perf pass is done and committed. Idle render-thread time went from 6.6–8.9 s to 0.3–2.2 s per 10 s, and the lift sort from 2.21 to 0.57 ms per frame. The console page now has:
- one header band;
- a fixed-height plant (500 / 620) beside a scrolling inspector;
- an **Active alarms** system chip that pulses, isolates the units in alarm with a breathing red halo, and puts the alarm list at the top of the inspector;
- view thumbnails (iso / elevation / plan) beside the plant from 960 px.

After the code, the session made a launch art set: eight app prints from real UI captures, fifteen plant prints from the product renderer, a poster, and `README.md` with a hero image. The art and the README are **untracked**: the brief said to commit only to a named branch, and no branch was named.

## Last verified state
- **Build:** Debug `net10.0-desktop` passes. The last Release build was blocked by the running app holding the DLLs.
- **Tests:** 195/195 (`dotnet test PacConsole.Tests`).
- **Runtime:** with the user's OK, the app was launched and the alarms view and thumbnails were checked. The walkthrough video is `~/Videos/PacConsole-walkthrough-v2.mp4`. The App MCP was not used this session.
- **Git:** `main` at `d0d9135`, no remote. Untracked:
  - `README.md` and `media/` (art and sources);
  - `.mcp.json`;
  - `docs/spike/`, `docs/anatomy/plant/shots/`;
  - `docs/hairline/*.png`.
- **Lint:** CARD 0 · CODEBEHIND 0 · ICON 0 · RESPONSIVE 0 · HEX 18 (Simple role overrides in App.xaml, allowed) · TOKENTHEME 55 (in-place theme repaint, allowed).

## Guidance applied
| Skill / rule / decision | Applied at (file:line) |
|---|---|
| uno-toolkit: `{utu:Responsive}` with a custom `ResponsiveLayout` | `PacConsole/Styles/Tokens.xaml:7`, `PacConsole/Controls/PlantViewport.xaml:28,83` |
| uno-toolkit: lint allow for the thumbnail tile frame | `PacConsole/Styles/Controls.xaml:421` |
| mvux: `Feed.Combine` view composition, `IState` setpoints | `PacConsole/Presentation/ConsoleModel.cs:70-78`, `:35` |
| skiasharp-uno: `SKCanvasElement` thumbnails, raster still layers built off the render path | `PacConsole/Rendering/PlantThumb.cs:15`, `PacConsole/Rendering/SolidRenderer.cs:807` |
| Decision: incremental depth sort (same order as the full sort) | `PacConsole.Core/Solids/DepthOrder.cs:275` (`DepthCache`), test `PacConsole.Tests/DepthCacheTests.cs` |

Read, not applied: none.

## Next actions (in order)
1. **Commit the README and art** once the user names a branch. `README.md` needs `media/art/readme-hero.jpg`, so commit them together. Ask before committing the other untracked folders.
2. **Measure the per-part lift cache** (in `997788e`, never measured). The baseline was 8 lifts in 14 s costing about 11 s of render-thread time. Use the trace method: per-frame spans, A/B alternation, Release. Ask before launching the app.
3. **Accessibility:** the Auto orbit button has no `AutomationProperties.Name`.
4. **Record the runtime gotchas with evidence** (`docs/evidence/`, then `~/.claude/rules/uno-runtime-gotchas.md`):
   - SKCanvasElement content is replayed every frame. `SKImage.FromPicture` re-renders each frame; `SKSurface.Snapshot()` rasters don't.
   - `RenderTargetBitmap`'s scaled overload resamples. Real hi-res needs `RootScale.SetTestOverride`.
   - UIA `SelectionItem.Select` blocks about 2 s.
   - PostMessage clicks don't reach Uno Platform.
   - Stale `Uno.UI.RemoteControl.Host` processes lock NuGet package files.
5. **Older open items:**
   - the gotchas list in git history (`HANDOFF.md` at `d0d9135`);
   - phase 7 cleanup (`Wireframe*`, unused charts), which needs the user's OK.

## Open questions
- Which branch should the art and README go to?
- bin/obj and the NuGet cache were deleted externally twice this session (about 14:08 and 14:40). What is doing that?
- No git remote or CI yet.
- The app was left in graphite with Electrical selected after the recording.
- The gotchas above have no exported evidence yet.

## Relaunch
```
cd C:\Users\Platform006\UnoProjects\PacConsole
dotnet build PacConsole/PacConsole.csproj -f net10.0-desktop
dotnet test PacConsole.Tests
# Ask the user before launching any window (hard rule).
# Demo / timing: dotnet run --project PacConsole -f net10.0-desktop -c Release
# NuGet "access denied" on restore: stop Uno.UI.RemoteControl.Host processes first.
# Art: media/_source/README.md (capture tool, ui_art2.py, finish.py).
```
