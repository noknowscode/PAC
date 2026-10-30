# SPEC · Console page v3 (equipment inspector and synchronized trends)

Source: the user's mock, two screenshots of one page (2026-10-15 20:07). Built in the same session after this spec. `SPEC.md` remains the plant-drawing spec.

**Decisions (the user, 2026-10-15):**
- **Equipment-specific readings.** Each unit has its own simulated readings: electrical for the generator, steam for the turbine, and so on.
- **A Control tab** in the inspector, beside Overview and Condition, holds mode, setpoints and the valves that belong to the selected equipment.
- **The mock's naming.** The fan cell becomes CLG-05 Cooling bank, and a water-cooled condenser (CND-07) is added as its own unit beside the turbine.

## Architecture Brief

- **Targets:** `net10.0-desktop` (Skia, Win32), Uno.Sdk 6.7.30. Unchanged. *Superseded 2026-10-30:* the app now targets five heads (Android, iOS, WinAppSDK, WebAssembly and `net10.0-desktop`) on Uno.Sdk 7.0.0-dev.115 with SkiaSharp 4; see `README.md`.
- **UnoFeatures:** unchanged (MVUX, Toolkit, Simple theme, SkiaSharp).
- **MVUX stays.**
  - Decision: extend `ConsoleModel` and the pure `ConsoleView.Compose`.
  - Reason: the page is one async view over telemetry plus operator states, and composing in Core keeps the rules testable.
  - Tradeoff: `ConsoleView` grows; its nested view records stay `class`, or are bound by leaf, because MVUX proxies nested records in place (see HANDOFF).
- **Routes:** unchanged, one page. `uno-navigation` doesn't apply.
- **Core model, new:**
  - `Equipment` (7 units) with `ReadingSpec`s (id, label, unit, format, base, swing, seed, group Overview or Condition, optional limit or target).
  - `Simulation.Reading(unit, readingId, tick, setpoints)` and `ReadingHistory(…, range)`.
  - `PlantSystem` (All, SteamWater, Cooling, FuelFlue, Electrical) with unit and part membership.
- **Model states, new:**
  - `IState<PlantSystem> System`
  - `IState<TrendRange> Range` (15 min, 1 h, 8 h)
  - `IState<ImmutableArray<string>> Compared` (reading keys in the trends, at most 3)
  - `IState<double> Cursor`: the slider binds two-way to it; `SetCursorLatest` moves it to the last sample. *(Updated 2026-10-28; first drafted as `IState<int>` with -1 for latest.)*
  - Commands: `SelectSystem`, `SelectRange`, `ToggleCompare`, `SetCursorLatest`; existing `SelectUnit`, `SetMode`, `ToggleValve` and the setpoints stay.
- **Testing:**
  - Core tests: catalog, every reading has a spec and a simulated value, alerts, systems membership, trend sampling, cursor, events.
  - Plant tests: counts, hulls, the z-buffer, the condenser inside its pad.
  - Runtime via the App MCP.

**Capability inventory**

| Capability (from the mock) | Status | Note |
|---|---|---|
| Header: product mark, plant, station type | implemented | |
| Alert pill selects the first alerting unit | superseded | the Active alarms system chip (2026-10-19) isolates the units in alarm and lists them in the inspector |
| Overflow menu (…) | implemented | MenuFlyout: System / Light / Graphite theme (2026-10-28; Reset view lives on the plant panel only) |
| Expand (full screen) | implemented | `AppWindow.SetPresenter(FullScreen)` toggle; verified on Win32 in the gotchas |
| Status strip: net output, gap to target, mode, snapshot time | implemented | |
| Systems filter | implemented | dims parts outside the system; titles the plant panel |
| Plant panel: view switcher, legend, equipment picker, rotate, reset | implemented | live view thumbnails from 960 px, a segmented switcher below (thumbnails returned after the perf pass, 2026-10-19); the systems filter takes its own row below 1280 px |
| Callouts with code, name and state | implemented | |
| Inspector: Overview, Condition, Control tabs | implemented | the Control tab is the user's decision |
| Equipment-specific readings | implemented | simulated; the footer says so |
| Reading selection compared in trends | implemented | up to 3, plus net generation |
| Connected equipment chips | implemented | from catalog connections |
| Synchronized trends: range, shared cursor, event markers | implemented | events are alert onsets in the range |
| Unit preview thumbnail | omitted | not in the mock; the plant callout and inspector carry the unit |
| Hourly generation columns | omitted | the 8 h range covers the long view |

## Design Brief

- **Direction:** the mock is an evolution of Direction 2 (.impeccable.md). It's a light page in one bordered container on the grey ground, with bands: a header, a grey status strip, a systems bar, the plant panel and inspector, the trends, and a grey footer.
- **Colour change from the mock:**
  - Selection uses deep slate-teal: `AccentBrush` #2F5D73 light / #7FB3C8 graphite.
  - Alerts use burnt orange: `AlertBrush` #C2410C light / #F0793A graphite, with an amber tint for the alert pill.
  - Status is always paired with words ("Alert", "1 active alert").
- **Tokens:** existing ramp (12/13/16/22/56; the inspector title is 28 SemiBold, the only new size), radii 4/8/12, spacing 4/8/12/16/24/32.
- **Layout:**
  - The page fills the window edge to edge (the user, 2026-10-15); the plant panel takes the width beside the 340 inspector.
  - Main row: plant panel `*` and inspector 340.
  - Below 960 px (`{utu:Responsive}`), the inspector stacks under the plant.
  - The page scrolls vertically; the trends sit below the fold, as in the mock.

## Interaction Brief

- **Flows:**
  - Pick a system: the plant dims parts outside it, and the title and callouts follow.
  - Pick equipment: by clicking the plant, a callout, the picker, or a connected-equipment chip.
  - Toggle a reading: it joins or leaves the trends; the ↗ mark and a fill show it's compared.
  - Drag the shared cursor: every trend shows a hairline and its value at that time. "Latest" returns the cursor to the end.
  - Pick a range: 15 min, 1 hour or 8 hours.
- **FeedView:** the existing Progress and Error templates (Retry by ElementName).
- **Accessibility:** every control is named; the trends read their values at the cursor; tabs and segments are RadioButtons with group names.
- **Motion:** a range change eases charts over 220 ms (the existing chart frame); reduced motion snaps.
- **Verification (uno-verify):**
  - every system
  - each of the 7 units
  - compare toggles
  - cursor, ranges, tabs
  - theme
  - widths 1424 and 1100

## Spec Graph Brief

```text
ConsolePage  ← ConsoleViewModel
└─ FeedView(View) {Value, Progress, Error}
   ├─ Header        ← Data.AlertText (→ SelectFirstAlert) · Operator · Overflow(theme, reset) · Expand
   ├─ StatusStrip   ← Data.OutputNow, Data.TargetGap, Data.TargetText, Data.ModeLine, Data.Snapshot
   ├─ SystemsBar    RadioButtons ⇒ System (SelectSystem)
   ├─ PlantPanel    ← Data.SystemTitle, Data.Selected, Data.AlertingUnits, Data.System
   │  ├─ ViewSwitch    (view-only: camera presets)
   │  ├─ PlantViewport (callouts → SelectUnit)
   │  └─ Footer        Equipment picker ⇒ SelectUnit · rotate · reset
   ├─ Inspector     ← Data.Inspector (code, name, description, tiles[Overview|Condition], connections)
   │  ├─ Tabs       Overview | Condition | Control (view-only)
   │  ├─ Tiles      → ToggleCompare(key)
   │  └─ Control    Mode ⇒ SetMode · Setpoints ⇒ TargetMw/TurbineLimitBar/DamperPct · Valves → ToggleValve
   └─ Trends        ← Data.Trends (series[], times[], events[]), Data.CursorIndex
      ├─ Range      ⇒ Range (SelectRange)
      ├─ TrendChart ×n  (shared cursor index)
      └─ Cursor     Slider ⇒ Cursor · Latest → SetCursorLatest
```

Data flow: `Snapshot`, `CurrentSetpoints`, `Valves`, `Mode`, `Selection`, `Clock`, `System`, `Range`, `Compared` and `Cursor` are combined by `ConsoleView.Compose` (two combines nested, since `Feed.Combine` takes at most 8).

**Gate:** routes n/a (one page). Single writer: each new state has one command or one two-way slider. Colour cue: alert pairs with "Alert" text and an outline; compared pairs with the ↗ mark. All pass.

## Implementation Plan

1. **Core:** equipment catalog (7 units, specs), simulation, alerts, systems, trends and events, `ConsoleView` v3; tests.
2. **Plant:**
   - Rename the fan cell to CLG-05.
   - Add the CND-07 condenser (pad, shell, water boxes, hotwell, condensate pump, exhaust and cooling-water runs).
   - Part→system membership; the renderer dims parts outside the system.
   - Tests.
3. **Page XAML** per the page tree; tokens updated (selection slate-teal, alert orange).
4. **TrendChart** small multiples with a shared cursor.
5. **Verify** (uno-verify), lint, commit, handoff.

## Quality Gates

| Phase | Gate (skill) | Done when |
|---|---|---|
| every phase | build + tests | 0 warnings, all tests green |
| 3, 4 | uno-verify (App MCP) | the flows in the Interaction Brief exercised; screenshots read at both widths and both themes |
| 3 | uno-component-states | Progress and Error templates present; Retry by ElementName |
| 5 | handoff | HANDOFF.md current |

## Risks

- `Feed.Combine` arity is above 8: nest two combines.
- `ComboBox` selection inside a FeedView template: bind `SelectedIndex` one way and route `SelectionChanged` to `SelectUnit` (Toolkit `CommandExtensions` on a ComboBox is unverified; fall back to marked view glue).
- Frame cost: the page drops the four thumbnail canvases, the largest measured cost; re-measure after phase 3.

## Unresolved Questions

- **Omitted (accepted):** unit preview thumbnail; hourly generation columns.
- **Simulated:** readings, trends and events are simulated; the footer says so.
