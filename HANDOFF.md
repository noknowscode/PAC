# HANDOFF — PacConsole solid plant rendering port
Updated: 2026-10-15 13:30

## Where we are
SPEC phases 1–3 are done and committed. `PacConsole.Core/Solids/` ports the Anatomy kit (extrude, slab, cylinder, lathe/disc/turned, prism, tube, four-tone shading), checked point for point against a kit-exported fixture. `PlantSolids.Default.All` is the whole plant as 139 solids. `DepthOrder` is an exact separating-plane painter's order, verified by a ray-cast z-buffer at 72 cameras and with a unit lifted. `Rendering/SolidRenderer.cs` paints them, still in the figure palette. The app on `main` still shows the wireframe. The running dev build shows the whole solid plant through an **uncommitted scratch** `SolidSpikeCanvas` swapped into `PlantViewport`; phase 4 replaces it with `PlantCanvas`. Results and two decisions (flat sort; separating planes over support graph) are recorded in SPEC.md → *Phase 1 result*.

## Last verified state
- Build: pass, `net10.0-desktop` Debug, 3 pre-existing Uno0001 warnings (Pulse `AutoReverse`, 2× `CharacterSpacing`).
- Tests: 139/139 green (`dotnet test PacConsole.Tests`); the z-buffer test takes ~16 s.
- Runtime: App MCP (`uno_app_start`, rooted session). TKA-02 at 8 yaws, then the whole plant orbiting with CND-05 lit; element screenshots no longer crash the app.
- Git: `main`, last commit `e4acd45 docs: record phases 2 and 3 in the spec`. Uncommitted on purpose: `PacConsole/Controls/PlantViewport.xaml(.cs)` (scratch swap), `PacConsole/Rendering/SolidSpikeCanvas.cs`. Untracked leftovers: `.mcp.json`, `docs/spike/` (empty), figure screenshots under `docs/anatomy/plant/shots/` and `docs/hairline/*.png`.
- Lint: CARD 7 · HEX 18 · TOKENTHEME 20 · CODEBEHIND 8 · ICON 10 (all pre-existing, unchanged).

## Guidance applied
| Skill / rule / decision | Applied at (file:line) |
|---|---|
| `skiasharp-uno`: cache paints/paths, reuse per frame | `PacConsole/Rendering/SolidRenderer.cs:46` |
| Renderer lifetime follows Loaded/Unloaded (crash fix) | `PacConsole/Rendering/WireframeCanvas.cs:26`, `WireframeThumb.cs:22` |
| Decision: separating-plane order, silhouette-overlap constraints | `PacConsole.Core/Solids/DepthOrder.cs:66`, `:108` |
| Decision: tapered hulls for cones | `PacConsole.Core/Solids/DepthOrder.cs:26` |
Read, not applied: `skiasharp-uno` `SKCanvasElement.IsSupportedOnCurrentPlatform()` guard (add with `PlantCanvas`), `uno-verify`, `uno-runtime-gotchas.md`, `capability-coverage.md`.

## Next actions (in order)
1. Phase 4: load `xaml-design-polish` (motion) first. Add `SolidShade0..3Color`, `SolidTopColor`, `SolidHaloColor`, `SolidStroke*` to `Styles/Tokens.xaml`; build `PlantCanvas` (`SKCanvasElement`, guarded, Loaded/Unloaded renderer) replacing `WireframeCanvas` and the scratch canvas; retune `OrbitProjection.ForViewport` for the 320 × 220 plate; tag anchors from `PlantSolids`; lift (τ 0.28 s) + halo + alert outline/dashed crease; hover outline + click-to-select hit test; fan blades; line decorations. Measure frame cost (< 4 ms). `uno-verify`, commit.
2. Phases 5–7 per SPEC.md (`SolidThumb`, `UnitPreview` + `SelectedVib`/`VibrationLimit`, cleanup), `uno-verify` after 5 and 6.
3. Then delete `SolidSpikeCanvas.cs` and `docs/spike/` (the user's call; deletion was declined this session).

## Open questions
- Release path: no git remote or CI (a hook asks for it; SPEC → Decisions keeps it out of scope until the user creates a remote).
- Pre-existing lint counts and the 3 Uno0001 warnings.
- Figure palette on `#131313` reads very dark in the card; measure shade steps against `CardBrush` in phase 4.
- The Unloaded-dispose crash is a Skia case of the existing re-parenting gotcha in `uno-runtime-gotchas.md` (Layout: "anything Unloaded lets go of must be taken back in Loaded"). Optional: add the native-AV symptom (`sk_path_reset` after an App MCP element screenshot) to that bullet.

## Relaunch
```
cd C:\Users\Platform006\UnoProjects\PacConsole
dotnet build PacConsole/PacConsole.csproj -f net10.0-desktop
dotnet test PacConsole.Tests/PacConsole.Tests.csproj
# rooted session: uno_app_start (projectPath PacConsole\PacConsole.csproj, net10.0-desktop)
# viewport refs: Reset ^3q, Rotate right ^3e, PlantViewport ^26 (take a fresh snapshot first)
```
