# SPEC · Solid plant rendering (port of the Anatomy plant figure)

Status: draft for approval · 2026-10-15 · reference figure `docs/anatomy/plant/` (commits `8ad26ec`, `f08fc42`), published at https://claude.ai/artifact/3ZUDmNh8CpXUqVtdbw5nyy

Goal: replace the console's wireframe plant (main viewport, three preset thumbnails, unit preview) with the solid, shaded, depth-ordered look of the reference figure, keep every current behaviour (orbit, drag, presets, tags, selection, alert), and turn the unit preview into a close-up with a live vibration gauge.

Out of scope: new routes, new telemetry, theme switching (the app is dark-only, `RequestedTheme="Dark"`), WebAssembly/mobile heads (the app ships `net10.0-desktop` only today).

## Architecture Brief

**Targets.** `net10.0-desktop` (Skia, Win32 host), Uno.Sdk **6.7.30** pinned in `global.json`. Bundled SkiaSharp 3.119.2; no opt-in to SkiaSharp 4 (nothing here needs a v4-only API).

**UnoFeatures.** Unchanged: `SimpleTheme; Hosting; Toolkit; Logging; MVUX; Navigation; SkiaRenderer`. `SkiaRenderer` already brings `Uno.WinUI.Graphics2DSK` (`SKCanvasElement`).

**Presentation pattern.** MVUX, unchanged.
- Decision: keep camera, orbit, hover, lift and fan-spin state inside the controls (`PlantViewport`, `PlantCanvas`), and add only telemetry-derived values to `ConsoleView`.
- Reason: that state changes every frame and is view state; the existing class comment in `PlantViewport.xaml.cs` already draws this line, and `ConsoleModel` says the same.
- Tradeoff: lift and spin are not testable through the model; they are tested through the Core geometry and the runtime check instead.

**Rendering pipeline.** Decision, reason, tradeoff:
- Decision: port the subset of the Anatomy kit the figure uses into **`PacConsole.Core/Solids/`** as pure C# (no Skia types): solid builders (rounded slab, cylinder, turned profile on any axis, polygon prism, straight tube), the four-tone facing shade, silhouette/crease/top extraction, and a depth sorter. Output is 2D polygons and polylines in screen space plus a tone tag. **`PacConsole/Rendering/SolidRenderer.cs`** turns them into cached `SKPath`s and paints.
- Reason: Core stays unit-testable (the repo already tests geometry and tag layout in `PacConsole.Tests`); the reference figure's shapes and numbers carry over 1:1; the Skia side is a thin painter, which `skiasharp-uno` asks for (no graph-building in `RenderOverride`).
- Tradeoff: we own a small geometry kernel (~600 lines) instead of reusing a library; there is no .NET package with the kit's tone and painter rules.

**Painter's order with a free orbit.** The reference figure solves order once per fixed camera (`A.settle`). The app orbits continuously, so order is solved per frame:
1. Units sit on disjoint pads, so units are ordered by pad depth (exact for disjoint convex footprints at any yaw).
2. Within a unit, parts are ordered by a precomputed **support graph** (a part drawn after what it stands on: lip after shaft, roof after hall, casing after pedestal) and, among parts at the same support level, by footprint depth at the current yaw.
3. Pipes and piers belong to the base layer and are ordered with the pads.
4. A Core test rasterises every part with a z-buffer at 24 yaws × 3 elevations and compares against the sorted order; any pair drawn wrongly over more than 0.25 px² fails the test (the audit's `depthOrder` check, ported).

**Geometry source.** `PlantGeometry` gains a solid description (`PlantSolids.Default`) built from the reference figure's numbers, converted to the app's axes (app is Y-up with X/Z on the floor; the figure is Z-up). The wireframe `Segment3` data stays until the last phase so tags, anchors and `Radii` keep working, then anchors and radii are derived from the solids and the segment builders are deleted.

**Services and DI.** None added. No routes added; `RegisterRoutes` is untouched.

**Models.** `ConsoleView` (Core record) gains:
- `double SelectedVib` — the selected unit's live `UnitReading.Vib` (the gauge needle).
- `double VibrationLimit` — `AlertRules.VibrationLimit` (4.5), so the gauge's limit mark reads from the same rule that raises the alert.
- `UnitId? SelectedUnitId` exists as `Selected`; reused.

**Platform constraints.**
- `SKCanvasElement` ignores XAML `Opacity` (gotcha G38): the lift halo and any fade bake alpha into paints.
- `UISettings.AnimationsEnabled` is hardcoded true on Skia desktop: reduced motion stays the in-app `AnimationsEnabled()` check already in `PlantViewport`, and the spec adds no new claim of OS reduced-motion support.
- `CompositionTarget.Rendering` pumps frames forever (gotcha): the existing `DispatcherTimer` loop stays, and it stops when nothing moves (fan spin counts as moving only while the preview is visible).
- A whitespace-only or zero-height `Path` draws nothing (gotchas): not applicable, all drawing is Skia.

**Testing / validation.**
- Core unit tests: solid builders (vertex counts, silhouette closure, crease ends on silhouette), shade bucketing, depth order against the z-buffer reference, `ConsoleView.SelectedVib` and `VibrationLimit`.
- Pixel comparison of `WireframeThumb` replacements at the three presets against reference PNGs rendered by the figure build at the same camera (target ≥ 85% structural similarity; the figure is the visual spec).
- Runtime: `uno-verify` in a session rooted in the repo (`.mcp.json` must exist first, see Unresolved Questions).

**Capability inventory.**

| Capability the design implies | Status | Choice |
|---|---|---|
| Solid, shaded, depth-ordered plant | implemented | Core solids + `SKCanvasElement` painter |
| Free orbit at any yaw/elevation with correct overlap | implemented | per-frame unit order + support graph; z-buffer test |
| Selected unit lifts with a halo and lit outline | implemented | per-unit lift offset in `PlantCanvas`, 0.28 s exponential follow |
| Alert shown on the unit | implemented | alert outline + dashed crease (second cue) on alerting units; tag keeps "· ALERT" text |
| Pipework between units (rack, steam, fuel) | implemented | straight tubes on piers, base layer |
| Turning condenser fans | implemented | blade lines rebuilt per frame on CND fan tops, only while visible and animations on |
| Three preset thumbnails | implemented | same renderer, static camera per preset |
| Unit preview close-up | implemented | selected unit's solids, fitted, plus a vibration gauge bound to `SelectedVib` |
| CND fan-cell mechanism (motor, gearbox, sensor, cable) in the preview | **substituted** | the preview shows the selected unit's own solids for every unit; the fan-cell drive train exists only in the reference figure |
| Interactive fan speed (Fig 5) | **omitted** | the console has no fan-speed telemetry or control; showing a speed slider would impersonate a capability |
| Light theme | **omitted** | app is dark-only |
| Hover-to-lift (reference figure) | **substituted** | hover draws an outline; click on a unit (or its tag, or keyboard) selects; lift follows the bound selection |

## Design Brief

**Visual direction.** The reference figure, recoloured into the console's dark palette: paper-filled solids with four close shades, thin hairline strokes, one accent. Units read as machines at the viewport's size and as silhouettes in the 3 thumbnails.

**Token set** (all exist in `Styles/Tokens.xaml` unless marked new):

| Role in the figure | App token | Note |
|---|---|---|
| paper (fill that hides) | `CardBrush` | matches the viewport card, so fills never show as patches |
| shade 0..3 | new `SolidShade0..3Color` | `CardBrush` stepped −6, −4, −2, 0 % luminance; tops `SolidTopColor` +4 % |
| line hi / mid / lo / faint | `TextPrimaryBrush`, `MutedBrush`, `HairlineBrush`, `FaintBrush` | |
| lit (selected) | `AccentBrush` | the console's selection colour; replaces the figure's white |
| alert | `AlertBrush` | outline plus dashed crease |
| halo | new `SolidHaloColor` | `#000000` at 0.55, alpha baked into the paint |
| stroke widths | new `SolidStrokeHairline` 0.6, `SolidStrokeEdge` 0.9, `SolidStrokeLit` 1.3 (doubles, px at 1×) | |

The renderer reads these once from `Application.Current.Resources` on `Loaded` into cached `SKColor`s; it never reads resources in `RenderOverride`.

**ThemeDictionaries.** Not used: dark-only app. New colours go in `Tokens.xaml` beside the existing ones (StaticResource).

**Resource scope.** App-wide (`Tokens.xaml` merged in `App.xaml`).

**Styles/templates.** None new. `PlantViewport.xaml` caption text changes from "PLANT MODEL · WIREFRAME" to "PLANT MODEL".

**Fonts.** None new.

**Layout.** Unchanged grid. The unit preview card adds the gauge beside the unit drawing inside the same `SKCanvasElement` (one canvas, two regions), so no new XAML layout.

## Interaction Brief

**Flows.** No navigation changes. Selection flow as today: tag click or keyboard → `SelectCommand(UnitId)` → `ConsoleModel.Selection` → `ConsoleView.Selected` → viewport, thumbnails, preview.

**Input.** Unchanged: drag to orbit (4 px threshold), arrow keys turn 45°/10°, Home resets, tags are real focusable Buttons.

**FeedView states.** n/a: the viewport, thumbnails and preview sit inside the page's existing `View` FeedView ValueTemplate and read already-resolved values.

**VisualStates.** Tag button states unchanged.

**Animations.**
- Unit lift: target 10 world units for the selected unit, 0 for others, exponential follow with τ = 0.28 s (same constant as the figure). Reduced motion: snap.
- Halo under a lifted unit: alpha = 0.8 × lift/10, baked into the paint.
- Fan blades: 1.6 rad/s on the CND fan tops; reduced motion: static.
- Gauge needle: follows `SelectedVib` with τ = 0.4 s; reduced motion: snap.
- The render loop stops when lift, tween, orbit and visible fans are all at rest.

**Accessibility.**
- `PlantViewport` help text unchanged (units, alerts, selection).
- Unit preview gets `AutomationProperties.Name="Unit preview"` and a help text "<code> vibration <x.x> mm/s, limit 4.5, <OK|HIGH>".
- Alert carries a second cue beyond colour: dashed crease on the unit and "· ALERT" in its tag.

**Runtime verification** (uno-verify): select each unit by tag and confirm the lit outline moves and the lift settles; drag through a full turn and capture at 8 yaws, checking no far part paints over a near one; confirm CND shows the alert outline and dashed crease at rest; confirm the gauge reads 4.8–4.9 with CND selected and the needle sits past the limit mark.

## Spec Graph Brief

**Route tree** (unchanged; listed for the gate):

```text
Shell
└─ Console   ViewMap<ConsolePage, ConsoleModel>   IsDefault
```

**Page tree** (only the changed nodes):

```text
ConsolePage
└─ FeedView(View) {Value, Progress?, Error?}           (existing templates)
   ├─ PlantViewport                                   ← Data.Selected, Data.AlertingUnits, SelectCommand
   │  ├─ PlantCanvas (SKCanvasElement)  [new, replaces WireframeCanvas]
   │  │                                              ← pose, lift, alerting (snapshotted fields)
   │  └─ TagLayer Canvas › TagButton ×6               → SelectCommand(UnitId)
   ├─ SolidThumb PresetKey=iso|elevation|plan ×3  [replaces WireframeThumb]   ← Data.Selected
   └─ UnitPreview (SKCanvasElement)  [replaces WireframeThumb SingleUnit]
                                                      ← Data.Selected, Data.SelectedVib, Data.VibrationLimit, Viewport.Pose
```

**Data-flow graph.**

```text
SimulatedTelemetry ─► ConsoleModel.Snapshot (IFeed<PlantSnapshot>)
ConsoleModel.Selection (IState<UnitSelection>) ⇐ SelectCommand (single writer)
Feed.Combine(Snapshot, CurrentSetpoints, Valves, Mode, Selection, Clock) ─► View (IFeed<ConsoleView>)
   ConsoleView.Selected          ─► PlantViewport.SelectedUnit, SolidThumb.Selected, UnitPreview.Selected
   ConsoleView.AlertingUnits     ─► PlantViewport.AlertingUnits
   ConsoleView.SelectedVib  [new] = reading(Selected).Vib         ─► UnitPreview.Vib
   ConsoleView.VibrationLimit [new] = AlertRules.VibrationLimit  ─► UnitPreview.Limit
PlantViewport.Pose (DP, view state) ─► UnitPreview.Pose
```

**Design graph.**

```text
Tokens.xaml @App
├─ CardBrush            StaticResource   paper fill        ← PlantCanvas, SolidThumb, UnitPreview (cached SKColor)
├─ SolidShade0..3Color  StaticResource   [new]             ← side tones
├─ SolidTopColor        StaticResource   [new]             ← top faces
├─ TextPrimaryBrush / MutedBrush / HairlineBrush / FaintBrush   ← line tones hi/mid/lo/faint
├─ AccentBrush          StaticResource   lit edge          cue: lift + halo + tag fill
├─ AlertBrush           StaticResource   alert edge        cue: dashed crease + "· ALERT" tag text
├─ SolidHaloColor       StaticResource   [new]             ← lift halo (alpha baked)
└─ SolidStroke*         StaticResource   [new] doubles     ← stroke widths
Motion (constants in PlantCanvas, not resources): lift τ 0.28 s, needle τ 0.4 s, fan 1.6 rad/s; reduced: snap/static
```

**Spec gate.**

| Check | Result | Nodes | Resolution |
|---|---|---|---|
| Registration / Region host / Shell safety / Cross-page / Back stack | PASS | Console | no route change |
| Sources | PASS | UnitPreview ← SelectedVib, VibrationLimit | both added to ConsoleView |
| Feed vs state | PASS | Selection stays IState; new values are feed-derived | |
| Single writer | PASS | Selection ⇐ SelectCommand only | |
| FeedView states | PASS (n/a) | canvases inside the existing ValueTemplate | |
| Failure paths | PASS (n/a) | no new commands | |
| Data shape | PASS | `Selected` can be null: preview draws nothing but the gauge at 0 with "—" help text | |
| Tokens | PASS | every colour and width named; motion as constants (see Risks) | |
| Theme-awareness | PASS | dark-only, StaticResource | |
| Scope | PASS | App-wide tokens only | |
| Color cue | PASS | selected: lift + halo; alert: dashed crease + tag text | |
| Contrast | **CHECK** | shade steps of 2 % on `CardBrush` may be invisible on some panels | → Risks (measure at runtime) |
| Encodings | PASS | needle angle = −135° + 270° × clamp(vib, 0, 10)/10; lift = 10 × selection | |
| Motion | PASS | each motion names duration and reduced-motion fallback | |
| Type assets | PASS (n/a) | no fonts | |
| UnoFeatures | PASS | SkiaRenderer present | |
| Targets | PASS | desktop only, stated | |
| Pending | PASS | none | |
| Capabilities | PASS | substituted and omitted rows repeated below | |

## Implementation Plan

Each phase builds, passes tests and is committed before the next.

1. **Spike (time-boxed 45 min).** Port `slabOf`, `cylinder` and the shade bucketing to Core, draw the TKA tanks and bund in a scratch `SKCanvasElement`, orbit a full turn. Exit: tones and silhouettes match the figure's TKA crop at yaw 45°/el 30° within 2 px; order correct at 8 yaws. Fail → Debug Checkpoint before phase 2.
2. **Core solids.** `PacConsole.Core/Solids/`: `Solid` (fill, shades[4], top, crease, outline as `ImmutableArray<Vec2>` lists), builders `Slab`, `Cylinder`, `Turned`, `Prism`, `Tube`; `Shading` (facing → 4 buckets, the kit's light direction); `DepthOrder` (unit order + support graph). Tests for each.
3. **PlantSolids.** The six units, base plate, pads, pipe rack and piers from the figure's `build.mjs`, axes converted to Y-up. Test: part count 135 solids + 4 tubes, every part supported (support-graph root is the plate), z-buffer order test at 24 × 3 cameras.
4. **SolidRenderer + PlantCanvas.** Cached `SKPath`s per part rebuilt only when the pose or a lift changes; paints created once from tokens on `Loaded`. Replace `WireframeCanvas` in `PlantViewport.xaml`. Tags reposition from new anchors. Lift + halo + alert cue.
5. **SolidThumb.** Replace the three `WireframeThumb PresetKey` uses. Pixel comparison against the figure's renders at the same presets.
6. **UnitPreview + ConsoleView fields.** Add `SelectedVib`, `VibrationLimit` (tests in `ConsoleViewTests`); draw the selected unit fitted on the left, the gauge (0–10 mm/s, 270° sweep, 50 minor / 10 major ticks, limit mark at 4.5) on the right; replace the `SingleUnit` thumb.
7. **Cleanup.** Delete `WireframeCanvas`, `WireframeRenderer`, `WireframeThumb`, and the `Segment3` builders in `PlantGeometry` once anchors/radii come from `PlantSolids`. Update caption text. `uno-verify`, then the gates below.

## Quality Gates

From *Build gates* in `~/.claude/rules/uno-scaffolding.md`: this is a change to an existing feature that touches UI and runtime behaviour, with a small data addition.

| Phase | Gate (skill) | Done when |
|---|---|---|
| every phase | build + tests; lint hook clean | 0 warnings on `net10.0-desktop` (the 3 existing Uno0001 warnings fixed or tracked), all tests green |
| 4, 5, 6 | `uno-verify` (rooted session, App MCP) | selection, orbit at 8 yaws, alert cue and gauge exercised through the App MCP; screenshots read |
| 6 | `uno-component-states` | preview shows a defined state with no selection and while the View feed is loading |
| 7 | `uno-build-log` entry | milestone recorded in `docs/fleet/BUILD-LOG.md` |
| 7 | `uno-completeness-audit` scoped to the plant views | gaps listed with evidence; report saved before any "done" claim |
| 7 | `gold-standard-pass` on the viewport and preview cards | findings fixed or listed |

## Risks

- **Per-frame order with free elevation (0.08–1.52 rad).** The support graph may misorder tall parts at plan view or near-flat elevation. Spike in phase 1; the z-buffer test in phase 3 is the gate. Fallback: clamp elevation to 0.15–1.40.
- **Frame cost.** ~140 solids × path rebuild per orbit frame. Budget: < 4 ms per frame on the dev machine at 772 × 540. Measure in phase 4 with a `Stopwatch` around path building; cache static parts per pose and only rebuild lifted units when only a lift changes.
- **Turned parts on any axis** (turbine casings, cones, pump set) need the kit's facet shading without its smooth-shading machinery; the figure uses only hard-knot profiles, so flat facet tones are correct. Verify casings at 4× in phase 4.
- **Contrast of 2 % shade steps** on `CardBrush` (#131313-range): measure luminance in screenshots; widen to 3 % if tones merge.
- **API verify (5 min each):** `SKCanvasElement.IsSupportedOnCurrentPlatform()` on desktop; `SKPath` reuse with `Rewind()` on 3.119.2.

## Decisions (2026-10-15, delegated by the user)

- **Unit preview: one design for all units.** Selected unit's solids plus the vibration gauge. The CND fan-cell drive train stays in the reference figure only; deferred, not planned. Reason: the gauge carries the alert meaning for every unit; the drive train is ~30 solids for one unit of six.
- **Fan speed: omitted.** No fan-speed telemetry exists; a speed control would impersonate a capability (`capability-coverage.md`, step 3).
- **Hover: outline, not selection.** Hovering a unit's solid draws a `MutedBrush` outline on it; clicking it executes `SelectCommand` (tags keep working). Lift follows selection. Reason: hover-to-select would rewrite the telemetry panels whenever the pointer crosses the plant. Adds to phase 4: a per-unit hit test against each unit's rest-pose screen hull (the figure's `HULLS` approach), frontmost by pad depth; drag past 4 px cancels the click as today.
- **Accent: `AccentBrush` (orange).** Matches the tags' selected state; the figure's white is its own palette.
- **Verification: rooted session.** Implementation runs in a session opened with `Open-RootedSession.ps1`, which seeds `.mcp.json`, resuming from `HANDOFF.md`.
- **Release path: out of scope for this port.** Needs a remote the user creates; tracked in the backlog, not a blocker.

## Unresolved Questions

- **Omitted (accepted):** light theme (app is dark-only); interactive fan speed (see Decisions).
- **Substituted (accepted):** fan-cell preview → generic unit preview; hover-to-lift → hover outline + click/lift on selection.
- **Skills not loaded for this spec:** `uno-navigation` (no route changes), `xaml-art-direction` (direction fixed by the reference figure) and `xaml-design-polish` (motion constants copied from the figure). Load `xaml-design-polish` in phase 4 before finalising motion.
