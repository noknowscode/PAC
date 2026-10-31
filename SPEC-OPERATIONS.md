# SPEC · Operations (alarm to resolution)

Source: the user, 2026-10-16: "could a real plant operator use this? ... there aren't many actions". Decision: build the full loop, CLG-05 first as the vertical slice, then every unit. Builds on SPEC-CONSOLE.md (v3 page).

**The story the slice must play end to end:** CLG-05 alarms on fan vibration → the operator acknowledges it → the inspector shows the response (likely cause, steps, consequence) → in Control, the operator starts standby cell 6, then stops cell 3, each with select-before-operate → vibration falls over about 30 s, the alarm returns to normal and clears, and the trend shows it → the log holds every step, with time and operator.

## Architecture Brief

- **Targets and features:** unchanged (Uno.Sdk 6.7.30, `net10.0-desktop`, MVUX, Simple + Toolkit, Skia). *Superseded 2026-10-30:* the app now targets five heads (Android, iOS, WinAppSDK, WebAssembly and `net10.0-desktop`) on Uno.Sdk 7.0.0-dev.115 with SkiaSharp 4; see `README.md`.
- **MVUX stays.**
  - Decision: one new state, `IState<PlantOps> Ops`. It is the operator-side plant, and every operator action is a pure `PlantOps.Apply(command, tick, now)`.
  - Reason: actions, acknowledgements and the log are durable data; one record keeps them consistent and testable.
  - Tradeoff: `Compose` takes one more input; the View combine is now exactly 8 sources.
- **Alarms are derived, not stored.**
  - Decision: `AlarmBook.Compose` replays the last 450 ticks (15 min) through the ops-aware simulation, finds each alarm's onset, clear and active state, and joins acknowledgements and shelves from `PlantOps`.
  - Reason: the simulation is deterministic, so a replay is exact and needs no per-tick memory in the model, which MVUX does not offer for stream feeds.
  - Tradeoff: the replay uses current setpoints, so a limit change re-reads history. About 450 × 7 unit evaluations a second.
- **Alarm rules join the readings.** A `ReadingSpec` may carry an `Alarm` (limit, direction, priority, guidance). `AlertRules` gives way to `AlarmRules.Evaluate`, so tiles, trends, events and the alarm list all use one definition.
- **Select-before-operate (SBO).** Every device command and valve move is first selected (`PlantOps.Pending`, which expires after 5 ticks or 10 s), then confirmed or cancelled from one confirm bar in the faceplate. Permissives can refuse a command with a reason, and the reason is shown.
- **Setpoints are typed and applied.** Each setpoint uses a `NumberBox` bound two-way to a draft state, plus Apply. Load and reactive power ramp at fixed rates; the alarm limit and damper apply at once. The global sliders go.
- **Model:**
  - **New states:** `Ops`; drafts `LoadDraft`, `LimitDraft`, `DamperDraft`, `FanSpeedDraft`, `ReactiveDraft`, `NoteDraft`.
  - **New commands:** `Select(command)`, `Confirm`, `Cancel`, `Acknowledge(key)`, `Shelve(key)`, `AcknowledgeAll`, `ApplyLoad`, `ApplyLimit`, `ApplyDamper`, `ApplyFanSpeed`, `ApplyReactive`, `AddNote`.
  - Commands read the latest snapshot and view as feed parameters.
- **Testing:** Core tests for commands, permissives, SBO expiry, ramps, every effect on readings, alarm states and transitions, the CLG story as a scenario test, and the log. Runtime check of the story via the App MCP.

**Capability inventory**

| Capability | Status | Note |
|---|---|---|
| Alarm summary: priority, time, value / limit, state | implemented | an alarm band under the status strip, 4 fixed rows and a "+N more" line. *Moved 2026-10-19: the list now opens at the top of the inspector under the Active alarms view.* |
| Acknowledge, acknowledge all, shelve | implemented | shelve is 1 h with the reason "Shelved by operator"; notes carry more |
| Alarm states: unacknowledged, acknowledged, returned unacknowledged, shelved | implemented | ISA-18.2 lifecycle, reduced |
| Alarm response guidance | implemented | per rule: cause, steps, consequence; in the inspector while the unit is in alarm |
| Per-equipment faceplates | implemented | CLG cells + fan speed; TRB load + alarm limit; GEN reactive power; CND CW pumps A/B; TKA transfer pumps; STK damper; SIL unloading; each unit's valves |
| Select-before-operate, permissives | implemented | one confirm bar per faceplate |
| Commanded vs actual, travel and ramps | implemented | devices take 2–5 ticks to start or stop; valves travel 5 ticks; load ramps at 5 MW/min |
| A plant that responds | implemented | effects listed under Interaction Brief |
| Range bars on reading tiles | implemented | normal band, alarm limit, value marker |
| Event log and shift notes | implemented | operator entries plus alarm transitions in the window; the last 8, fixed rows |
| Operator identity and permissions | substituted | a fixed "Operator 04"; no login |
| Fans stop turning in the drawing | omitted | the drawing's fan animation is not per cell |
| Alarm history beyond 15 min | omitted | the replay window bounds it |

## Design Brief

- **Same tokens:**
  - Alarm priority is a word (High, Medium, Low) plus weight. Unacknowledged rows use the alert ink and semibold; acknowledged rows use muted ink; returned rows read "Returned".
  - A pending command bar uses the accent tint with the command in words.
  - Range bars use a 4 px hairline track, a 4 px normal band in `FillStrongBrush`, a limit tick in `AlertBrush` and a 2 px value marker in ink. The marker turns alert when out of band.
- **Layout:**
  - The alarm band sits under the status strip. *(Since 2026-10-19 it is the Active alarms list at the top of the inspector.)*
  - The faceplate replaces the Control tab's contents.
  - The operator log is a section under the trends.
  - Nothing new floats; all panels are in the page flow.

## Interaction Brief

- **Flows:**
  - **Story (CLG-05):** as above.
  - **TRB load change:** Control → type 640 → Apply → the ramp reads "620 → 640 MW, 4 min" → output follows → logged.
  - **CND pump swap:** start B, stop A. Stopping the last running pump is refused: "Stopping the last cooling-water pump loses condenser vacuum".
- **Effects (pure, in ticks):**
  - **CLG:**
    - Unit vibration is the largest cell vibration. Cell 3 carries the fault, scaled by (speed / 100)².
    - A stopped cell's contribution decays with τ = 15 ticks.
    - Cooling capacity is the sum of running cells × speed. Each missing cell of capacity raises basin and return temperatures 1.5 °C and condenser absolute pressure 0.8 kPa.
    - Fewer than 4 running cells is refused.
  - **CND:**
    - With no CW pump running, absolute pressure rises toward 18 kPa (τ = 30); the High alarm is at 12 kPa.
    - Net output loses up to 40 MW as vacuum degrades.
  - **TKA:** with no transfer pump, transfer flow falls to 0 (τ = 5); the Medium alarm is below 20 t/h. Stopping the last pump is refused.
  - **SIL:** the level rises 0.02 %/tick and falls 0.1 %/tick while unloading; the Low alarm is above 85 %.
  - **TRB / GEN:** output follows the load ramp (setpoint − 20 + drift); reactive power follows its ramp.
  - **Valves:** output counts valves that are actually open, after their travel.
- **SBO:** the confirm bar reads "Confirm: stop cell 3 on CLG-05" with Confirm and Cancel. It expires in 10 s; Esc cancels.
- **Accessibility:**
  - Alarm rows read "High, unacknowledged, CLG-05 fan vibration 5.2 mm/s, limit 4.5, since 01:56".
  - Every button names its action and target.
- **Verification (uno-verify):**
  - the story end to end
  - a TRB load ramp
  - a refused CND stop
  - acknowledge all
  - shelve
  - a shift note
  - graphite theme
  - widths 1424 and 900

## Spec Graph Brief

```text
ConsolePage ← ConsoleViewModel
└─ FeedView(View)
   ├─ Header, StatusStrip (as v3)
   ├─ AlarmBand     ← Data.Alarm0..3, AlarmSummary · Ack → Acknowledge(key) · Shelve → Shelve(key) · AckAll
   ├─ SystemsBar, PlantPanel (as v3)
   ├─ Inspector
   │  ├─ AlarmResponse  ← Data.Response (visible while the unit is in alarm) · Ack
   │  ├─ Overview / Condition tiles + range bars
   │  └─ Control: PendingBar ← Data.Pending (Confirm / Cancel)
   │     └─ Faceplate per unit (CLG cells + speed · TRB load + limit · GEN reactive · CND pumps · TKA pumps · STK damper · SIL unload) + valves
   ├─ Trends (as v3; events from AlarmRules)
   └─ OperatorLog   ← Data.Log0..7 · NoteDraft ⇒ AddNote
```

Data flow: `Ops` joins the View combine (8 sources). Commands write `Ops` (single writer per action). Drafts are written by their NumberBox only, and read by their Apply.

**Gate:**

- Single writer: pass. Each draft has one two-way writer; `Ops` has commands only.
- Colour cue: pass. Priority is a word, state is a word, pending is a sentence.
- FeedView states: unchanged.

## Implementation Plan

1. **Core ops:** `PlantOps` with devices, ramps, valves travel, acknowledgements, shelves, log, pending; commands, permissives and SBO; tests.
2. **Core effects and alarms:**
   - the ops-aware simulation and reading context
   - alarm specs on readings
   - `AlarmRules`, `AlarmBook` replay, guidance catalogue
   - trend events from `AlarmRules`
   - `ConsoleView` fields
   - tests, including the CLG story scenario
3. **Slice UI (CLG-05):** alarm band, alarm response, pending bar, CLG faceplate, operator log and notes; verify the story.
4. **Every unit:** the other faceplates, valve travel, range bars; verify the TRB ramp and the refused CND stop.
5. **Gates:** build, tests, lint, uno-verify across both themes and widths, handoff.

## Build status (2026-10-16)

Phases 1 to 5 done: `c52f64e` (operations), the deadband fix commit, verified via the App MCP.

**Changes made during the build, decided on evidence:**

- **Alarm deadbands (ISA-18.2).** The first run logged the cooling bank's alarm in and out every few seconds. Each alarm now clears only once back inside its limit by a deadband:

  | Reading | Deadband |
  |---|---|
  | Vibration | 0.5 mm/s |
  | Turbine inlet pressure | 1 bar |
  | Condenser absolute pressure | 0.5 kPa |
  | Fuel transfer flow | 5 t/h |
  | Silo level | 1 % |
  | NOx | 5 mg/Nm³ |

- **Cell 3's fault.** Its drift now centres on 4.5 mm/s with a ±0.7 swing, so the alarm clears in each 10-minute cycle. The response's speed option is 75 % (at 80 % the peak stays above the 4.0 mm/s clear threshold).
- **SampleMemo.** Composing the view measured 41 ms; it now remembers per-tick samples while the ops and setpoints hold, and composes in 4 ms (Release).
- **Valve buttons.** Valves are plain buttons: a ToggleButton flips its own check state before select-before-operate confirms anything.

**Verified in the app:**

- the story: alarm, acknowledge, start cell 6, stop cell 3, the vibration falls on the trend, the alarm returns to normal, and the log shows every step
- the TRB load ramp (620 → 640 MW, 4 min)
- V-103 confirm and travel (Shut → Opening… → Open)
- the refused CND pump stop, with its reason
- shelve, and a shift note
- graphite at 900 px

## Quality Gates

| Phase | Gate (skill) | Done when |
|---|---|---|
| every | build + tests | 0 warnings; all green |
| 3, 4 | uno-verify | the flows above exercised through the App MCP |
| 3 | uno-component-states | band and log have empty states; the pending bar has expiry |
| 5 | uno-audit lint | gating rules 0 or allowed with reasons |
| 5 | handoff | HANDOFF.md current |

## Risks

- **Replay cost per recomposition:** the Compose budget is under 5 ms; measure in Core.
- **NumberBox two-way inside the FeedView template:** bind `Parent.X` like the sliders do (verified for sliders in this app).
- **Many fixed slots:** keep each slot a record MVUX updates in place.

## Unresolved Questions

- **Substituted:** operator identity is fixed ("Operator 04"); no login or permission levels.
- **Omitted:** per-cell fan animation; alarm history beyond 15 min.
