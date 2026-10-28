# Completeness audit: PacConsole (2026-10-28, Uno.Sdk 6.7.30, heads: net10.0-desktop)

Verdict: **Complete with gaps** (0 Blockers, 5 Majors, 12 Minors)

Safe to show: the CLG-05 alarm story, select-before-operate, typed setpoints, the systems filter and unit selection, trends (range, cursor, Latest), shift notes, Hold, the graphite theme, the stacked layout below 960 px. These are all `R` on desktop (X11).

Keep off screen: stream failure and Retry (the simulator never fails, so this path has not run), relaunching after operator actions (nothing persists), and the Active alarms view after every alarm has cleared (the equipment picker goes blank).

Runtime method: `net10.0-desktop` Debug, run on Linux X11 under Xvfb (1600×1000), driven with XTest pointer and keyboard input, with post-state read from screen captures. The App MCP (`uno-app`) was not available in this cloud session. Evidence captures are in `docs/audit/2026-10-28/`.

## Inventory

- **Screens:** 1 intended, 1 present, 0 unreachable, 0 missing. Source: SPEC-CONSOLE.md and SPEC-OPERATIONS.md.
  - One page, `ConsolePage`.
  - In-page surfaces: header; plant panel with 3 view thumbnails or the segmented switcher; inspector (Active alarms list, alarm response, and the Overview, Condition and Control tabs); trends; operator log; footer; the More flyout.
  - States: progress, none, error and value.
- **Routes:** 1 registered (`Console`, `App.xaml.cs:141`), 0 navigation triggers, 0 dead references.
- **Interactions:** about 75 controls.
  - 0 dead by construction.
  - 0 simulated. Every command writes `PlantOps` or a state.
  - Retry (in the error and none templates) can't be reached, because nothing produces the error.
- **Critical workflows** (derived from the SPEC-OPERATIONS story, so please confirm):
  1. Alarm → acknowledge → the response is shown → the alarm clears
  2. Select-before-operate: select a device command → confirm → the device runs
  3. Typed setpoint → Apply → the value ramps
  4. Systems filter and unit selection (chip, tag, picker)
  5. Trends: range, shared cursor, Latest
  6. Shift note → operator log
  7. Plant mode Hold → resume

## Completeness by area

| # | Area | Status | Evidence | B | M | m | Note |
|---|---|---|---|---|---|---|---|
| 1 | Screens | Complete | R | 0 | 0 | 0 | the one page renders at launch, at 1440×900, 900×900 and 1300×560 |
| 2 | Navigation | Complete | R | 0 | 0 | 1 | single route; benign startup warning `Region '' has no children` |
| 3 | Interactions | Partial | R / C | 0 | 0 | 2 | 7 workflows driven; Shelve, Ack all, Cancel, valves, compare tiles and drag-orbit checked in code and Core tests only |
| 4 | Logic | Complete | R (tests) | 0 | 0 | 0 | 195/195 Core tests: SBO, permissives, ramps, alarm lifecycle, the CLG story scenario |
| 5 | Data | Simulated | R | 0 | 1 | 0 | by design and disclosed in the footer and README; please confirm |
| 6 | States | Partial | R / X | 0 | 1 | 1 | progress, value and the log's empty state are present; Error/Retry never run |
| 7 | Forms | Complete | R | 0 | 0 | 0 | NumberBox drafts and the note TextBox write back through `Parent.*`, proven on X11 |
| 8 | Authentication | N/A | C | 0 | 0 | 0 | fixed "Operator 04", substituted in SPEC-OPERATIONS; please confirm |
| 9 | Responsive/adaptive | Partial | R | 0 | 0 | 2 | stacking works; system chips overflow below 960 px; short height pushes the plant footer below the fold |
| 10 | Platform behavior | Partial | R / X | 0 | 1 | 0 | X11 host proven; Win32 and macOS hosts not run this session |
| 11 | Failure handling | Missing | X | 0 | 1 | 0 | no fault path, and Retry recovery is unproven |
| 12 | Accessibility | Complete (fixed) | C | 0 | 0 | 0 | Auto orbit had no automation name (fixed); keyboard Tab order reaches the inspector controls (R) |
| 13 | Performance | Partial | C / X | 0 | 0 | 1 | per-frame allocations trimmed; the lift cache from `997788e` is still unmeasured |
| 14 | Security/privacy | Complete | C | 0 | 0 | 0 | no network, no secrets; one Debug-only temp-file log |
| 15 | Visual quality | Partial | R | 0 | 0 | 3 | blank picker; duplicate glyph; type ramp break |
| 16 | Testing | Partial | R | 0 | 0 | 1 | Core tests are strong; no automated UI test |
| — | Persistence | Missing | R | 0 | 1 | 0 | log, acknowledgements, shelves, setpoints and theme are lost on relaunch |

## Critical workflows by head

`net10.0-desktop` is the only TFM (`PacConsole/PacConsole.csproj:4`). Android, iOS and WebAssembly are out of scope by spec (SPEC.md line 7).

| Workflow | desktop (X11) | desktop (Win32) | desktop (macOS) |
|---|---|---|---|
| 1. Alarm → ack → response → clear | R: "Unacknowledged" → "Acknowledged" (`ack.jpg`); later "No standing alarms" | X | X |
| 2. Select-before-operate | R: Cell 6 Start → confirm bar (`sbo-pending.jpg`) → Confirm → "Running 0.5 mm/s"; log "Start Cell 6 (standby) on CLG-05" | X | X |
| 3. Typed setpoint → Apply | R: GEN-03 reactive 120 → 150, header "122 → 150 MVAr · 1 min" (`setpoint-apply.jpg`) | X | X |
| 4. Systems filter, unit selection | R: Electrical → GEN-03 selected; STK-01 tag → All systems + STK-01 lifted | X | X |
| 5. Trends | R: 1 hour axis 11:34–12:34; cursor to 12:24:38, values follow; Latest returns | X | X |
| 6. Shift note → log | R: "Audit note ok" logged as Note (`note-log.jpg`) | X | X |
| 7. Hold → resume | R: "Data held 5 s" (`hold.jpg`) → "Generating · Automatic" | X | X |

The Win32 and macOS columns are `X` because this session ran in a Linux container. HANDOFF records a manual Win32 run on 2026-10-19 (a walkthrough video, no App MCP).

## Findings

### Blockers

None.

### Majors

- **[Failure handling] Error and Retry have never run, and Retry may not recover.**
  - Where: `ConsolePage.xaml:185-193`, `ConsoleModel.cs:25,76`, `SimulatedTelemetry.cs:26`.
  - Evidence: X.
  - `SimulatedTelemetry.Stream` never throws, so the ErrorTemplate can't be reached.
  - Retry calls `FeedView.Refresh` on `View`, which is a `Feed.Combine` over the `Feed.AsyncEnumerable` snapshot stream. The gotcha "FeedView.Refresh does not recover a failure thrown upstream of the feed it is bound to" applies to this shape.
  - Fix: a Debug-only fault switch in `SimulatedTelemetry` (an env var or a menu item), then prove Retry. If it doesn't recover, retry through a model command that restarts the stream.
  - Owner: `mvux`, `uno-component-states`.
- **[Persistence] Operator state is lost on relaunch.**
  - Evidence: R (`relaunch-state-lost.jpg`).
  - Lost: the shift note, the acknowledgement, the Cell 6 start and the theme. CLG-05 shows as unacknowledged again.
  - SPEC-OPERATIONS calls actions, acknowledgements and the log "durable data", yet nothing writes to disk (`grep File.|ApplicationData` finds only the Debug theme log).
  - This needs a product decision (see Unresolved Questions).
  - Owner: `uno-extensions-services`.
- **[Data] Telemetry is simulated.**
  - Where: `App.xaml.cs:106` registers `SimulatedTelemetry`.
  - Evidence: R.
  - This is disclosed in the footer ("Readings, trends and sensor assignments are simulated") and in the README, and it is the product's premise. It stays a Major until you confirm it as a product reason.
- **[States] Retry in the none state is unproven too.**
  - Where: `ConsolePage.xaml:173-181`.
  - Evidence: X. Same mechanism and fix as the first Major.
- **[Platform] The Win32 and macOS desktop hosts were not run this session.**
  - Where: `Platforms/Desktop/Program.cs:11-14`.
  - Evidence: X.
  - Fix: run the 7 workflows through `uno-verify` on Windows.

### Minors

- **[Visual] The equipment picker is blank in Active alarms with no alarms.**
  - Where: `ConsolePage.xaml:334`, `ConsoleView.V3.cs:229-230`.
  - Evidence: R (`alarms-empty-picker.jpg`).
  - The choices list is empty, so `SelectedIndex` is -1 while STK-01 stays selected.
- **[Responsive] Below 960 px, the "Electrical" and "Active alarms" chips sit past the right edge.**
  - Where: `ConsolePage.xaml:281`.
  - Evidence: R (`narrow-900.jpg`).
  - The scrollbar is hidden (`HorizontalScrollBarVisibility="Hidden"`), so the chips are reachable only by keyboard focus or by shift+wheel.
- **[Responsive] At 560 px height, the plant footer (picker, camera tools) is below the fold.**
  - Evidence: R.
  - The plant is a fixed 620 px tall by design.
- **[Interactions] The log keeps 200 entries and shows the last 8.**
  - Where: `PlantOps.cs:247-250`.
  - Older entries can't be reached. This is documented in SPEC-OPERATIONS. Status: Partial.
- **[Interactions] The cursor slider hardcodes `Maximum="89"`.**
  - Where: `ConsolePage.xaml:721`.
  - It is coupled to `TrendSet.Samples = 90` (`Trends.cs:50`).
- **[Visual] The "Caps" text style uses 11 px, outside the closed type ramp (12/13/16/22/56) declared in `Tokens.xaml`.**
  - Where: `ConsolePage.xaml:88`.
  - The chip style and 4 trend unit runs also carry a literal 12 (`:83`, `:686`, `:694`, `:702`, `:710`).
- **[Visual] "Reset view" uses glyph E740, the same glyph as Full screen.**
  - Where: `PlantViewport.xaml:134`, `ConsolePage.xaml:256`.
- **[Interactions] "Reset plant view" in the More menu duplicates the panel's Reset view button.**
  - Where: `ConsolePage.xaml:249`.
  - It costs a code-behind hook that grabs the viewport on `Loaded` (`ConsolePage.xaml.cs`).
- **[Performance] Renderer disposal may race the render path.**
  - Where: `PlantCanvas.cs:45-74`.
  - Evidence: I.
  - `Unloaded` and theme changes dispose `SolidRenderer` from the UI thread. If `RenderOverride` runs on a separate render thread on any host, a draw can touch freed native paints.
  - One theme switch ran clean (R), but that doesn't rule out the race.
- **[Performance] The per-part lift cache (`997788e`) is still unmeasured.**
  - Evidence: X (HANDOFF item 2). It needs a Windows Release trace.
- **[Testing] There is no automated UI or runtime test.**
  - The 3 timing tests are `[Explicit]` (`FrameCostTests.cs:13,38,56`).
- **[Navigation] A benign startup warning is logged:** `FrameNavigator: Region '' has no children to forward request to`.

## Not checked (X)

- **Error and none states, and Retry:** no fault source. Add a fault switch, then drive Retry.
- **Shelve, Acknowledge all, Cancel pending, SBO expiry (10 s), permissive refusal, valve moves, compare tiles, the Condition tab, the equipment picker, drag-orbit, the rotate buttons, Full screen:** checked in code and by Core tests only; not driven at runtime.
- **Mouse-wheel scrolling:** XTest wheel events did not reach the Uno X11 host, so I used the keyboard instead. Re-check with the App MCP or a real mouse.
- **Win32 and macOS hosts:** not available in this container.

## Noted for other skills

- **uno-audit:** run in this session. See the audit section of the session summary.
- **gold-standard-pass:** blank picker, chip overflow below 960 px, duplicate Reset/Full screen glyph, 11 px caps.

## Unresolved Questions

- **Data `Simulated`:** confirm that simulated telemetry is the product, so Data can be graded N/A.
- **Authentication `N/A`:** confirm that a fixed "Operator 04" with no login is final.
- **Android, iOS and WebAssembly `N/A`:** confirm that desktop-only stays the v1 scope.
- **Persistence:** should the log, acknowledgements, shelves, setpoints and theme survive a relaunch?
- **Win32 and macOS `X`:** run the 7 workflows on Windows through `uno-verify`.
- **Derived workflows 1–7:** confirm that these are the critical paths.
