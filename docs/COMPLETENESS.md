# Completeness audit: PacConsole (2026-11-04, Uno.Sdk 7.0.0-dev.115, heads: desktop, wasm, android, ios, winappsdk)

Audited head: `feat/production-readiness` at `9fdaae1`. Report mode: no code changed.

Verdict: **Complete with gaps** (0 Blockers, 3 functional Majors, 4 head Majors, 6 Minors)

Safe to show (`R` on desktop, X11): the CLG-05 alarm story end to end, select-before-operate with Confirm, Cancel, refusal and expiry, typed setpoints with Apply (Auto ramp, Manual at once, out-of-range clamped), Hold and resume, Acknowledge all, Shelve and Unshelve, the systems filter, picker, plant views and orbit, trends (range, compare, shared cursor, Latest), shift notes, a telemetry fault with Retry, the theme kept across a relaunch, and the 900 px and 390 px layouts.
Keep off screen: pressing Esc on a pending command (it does nothing); relaunching after operator actions (the log, acknowledgements, shelves and setpoints are gone).

Runtime method: `net10.0-desktop` Debug on Linux X11 under Xvfb with `PAC_X11_RENDERER=software` and an isolated `XDG_DATA_HOME`. Driven with xdotool (150 ms presses, Tab and keyboard), the `--pac-*` hooks (`VERIFY` lines) and `PAC_TELEMETRY_FAULT=5`; post-state read from captures, the operator log and the settings file. WebAssembly: Release publish served locally and driven in headless Chromium (Playwright, 150 ms presses). The App MCP has no tools in this container. Tests: 294/294 (`dotnet test PacConsole.Tests`). Build: 0 warnings, 0 errors.

## Inventory

Screens: 1 page intended, 1 present, 0 unreachable, 0 missing (source: SPEC-CONSOLE, SPEC-OPERATIONS). The page has 3 feed states (connecting, no telemetry, telemetry stopped), 4 bands (header, plant and inspector, trends, operator log), the alarm response, the pending-command bar, 3 inspector tabs, the theme menu and the equipment picker.
Routes: 1 registered (`Console`, default), 0 navigation triggers, 0 dead references.
Interactions: 58 command and click sites; 26 distinct model commands plus the viewport's view presets, auto orbit, rotate, reset, tags, drag and arrow keys. 0 dead by construction, 0 simulated. 1 promised interaction missing (Esc cancels select-before-operate).
Critical workflows (derived, please confirm):
1. CLG-05 story: alarm → acknowledge → response → start cell 6 → stop cell 3 → returns to normal → every step and a shift note in the log
2. Select-before-operate: select, Confirm, Cancel, permissive refusal, expiry, Esc
3. Typed setpoints and plant mode: Apply (Auto ramp, Manual at once, out-of-range clamp), Hold → resume
4. Alarm list: Acknowledge all, Shelve 1 h, Unshelve
5. Plant and trends: systems filter, picker, views, orbit, compare, range, shared cursor, Latest
6. Telemetry fault → Retry
7. Theme: System / Light / Graphite, kept across a relaunch

## Completeness by area

| # | Area | Status | Evidence | Blockers | Majors | Minors | Note |
|---|---|---|---|---|---|---|---|
| 1 | Screens | Complete | R | 0 | 0 | 0 | the page, its 3 feed states, every band and tab seen |
| 2 | Navigation | Complete | R | 0 | 0 | 0 | one route by design; tab context kept across unit changes |
| 3 | Interactions | Missing | R | 0 | 1 | 0 | Esc on a pending command does nothing; every other driven control works |
| 4 | Logic | Complete | R | 0 | 0 | 1 | permissives, ramps, valve travel, clamps proven; log times mix two clocks |
| 5 | Data | Simulated | R | 0 | 2 | 0 | telemetry simulated (disclosed, unconfirmed); operator data not persisted |
| 6 | States | Complete | R | 0 | 0 | 0 | error + Retry, no active alarms (chip count gone), refused, pending, expired, held, returned-unacknowledged |
| 7 | Forms | Complete | R | 0 | 0 | 0 | 150 ms click applies the typed value; keyboard Apply; 120 bar clamped to 140; empty note disables Add note |
| 8 | Authentication | N/A | C | 0 | 0 | 0 | fixed "Operator 04" (SPEC-OPERATIONS, substituted); please confirm |
| 9 | Responsive/adaptive UI | Partial | R | 0 | 0 | 1 | 1440, 900 and 390 px run; header gap text clips at 390 px |
| 10 | Platform behavior | Partial | R / X | 0 | 4 | 0 | X11 desktop run; WebAssembly launched and partly driven; Android, iOS, WinAppSDK built by CI only |
| 11 | Failure handling | Complete | R | 0 | 0 | 0 | fault → error → Retry → stream resumes |
| 12 | Accessibility | Partial | R / C | 0 | 0 | 1 | Tab order and keyboard activation work; row buttons lack their target |
| 13 | Performance | Complete (unproven) | C | 0 | 0 | 0 | off-screen pause and lift cache not re-measured |
| 14 | Security/privacy | Complete | C | 0 | 0 | 0 | no secrets, no network, no personal data |
| 15 | Visual quality | Partial | R | 0 | 0 | 2 | stale text colour after a live theme switch; a ghost faceplate after a unit change |
| 16 | Testing | Partial | R / C | 0 | 0 | 1 | 294 Core tests; no model, command or UI tests |

## Critical workflows by head

| Workflow | desktop (X11) | wasm | android | ios | winappsdk |
|---|---|---|---|---|---|
| 1. CLG-05 story + note | R (log: Alarm 01:29:16, Ack 01:32:18, Start Cell 6 01:33:52, Stop Cell 3 01:34:14, Normal 01:34:18, Note 01:35:20) | Partial: R for launch and Acknowledge ("Medium priority · Acknowledged"); rest X | X (CI build only) | X (CI build only) | X (CI build only) |
| 2. Select-before-operate | Partial: R for Confirm (cell 6 "Starting…"), Cancel (V-103 stays Shut), refusal ("Cannot stop CW pump A … loses condenser vacuum"), expiry (bar gone after 10 s); Esc does nothing | Partial: R for select ("Confirm: stop Cell 1 on CLG-05?"), and the bar stayed after Esc; Confirm X (not applied within 2.5 s in headless Chromium, two tries) | X | X | X |
| 3. Setpoints and mode | R (load 640: "620 → 640 MW · 4 min", target 640; limit 120 → 140 bar raises the TRB-04 High alarm; Manual fan 75 % "at once, Manual"; Hold froze the snapshot 9 s, Auto resumed it) | X (the Control-tab press was lost on the first try; not repeated) | X | X | X |
| 4. Alarm list | R (2 unacknowledged → Acknowledge all → 2 acknowledged; Shelve → "until 02:39:15"; Unshelve → listed again; all logged) | X | X | X | X |
| 5. Plant and trends | R (Electrical → "Electrical generation"; picker GEN-03; Elevation preset; drag orbit; Reactive power compared; 1 hour range; cursor 01:10:45 → Latest 01:41:15) | Partial: R for TRB-04 by its tag, two readings compared, the Control tab; rest X | X | X | X |
| 6. Fault → Retry | R (`PAC_TELEMETRY_FAULT=5`: "Telemetry stopped" → Retry → snapshots 01:41:58, 01:42:02) | X | X | X | X |
| 7. Theme persists | R (Graphite chosen, `Settings/Local.dat` written, relaunch opens in Graphite) | X | X | X | X |

## Findings

### Blockers

None.

### Majors (functional)

- [Interactions] Esc does not cancel a pending command - promised in SPEC-OPERATIONS ("It expires in 10 s; Esc cancels"); no Escape handling anywhere in `PacConsole/` (the only `KeyDown` is `PlantViewport.xaml.cs:595`, arrows and Home) - evidence R - with "Confirm: start Cell 6 (standby) on CLG-05?" showing, Esc left the bar in place; Cancel works - owner: mvux (a `KeyboardAccelerator` or page `KeyDown` calling `CancelPending`)
- [Data] Operator data is lost on relaunch - `ConsoleModel.Ops` is `State.Value(this, () => PlantOps.Initial)` (`PacConsole/Presentation/ConsoleModel.cs:58`) - evidence R - after acknowledging, applying 640 MW and adding a note, a relaunch showed CLG-05 Unacknowledged, target 620 and an empty log. SPEC-OPERATIONS calls actions, acknowledgements and the log "durable data"; deferred by the user ("persistence later") - owner: uno-extensions-services
- [Data] Telemetry is simulated - `SimulatedTelemetry` is the only `ITelemetryService` (`App.xaml.cs` ConfigureServices) - evidence R - disclosed in the footer ("Readings, trends and sensor assignments are simulated") and the README; stays a Major until confirmed as the product (HANDOFF open question) - owner: product decision

### Majors (head)

- [Platform] Android, iOS and WinAppSDK have never been run (three heads, three Majors) - evidence X - CI builds them; run workflows 1-7 on a device or Windows host with `uno-verify`.
- [Platform] WebAssembly is only partly proven - evidence R / X - the Release publish loads in Chromium (about 40 s to the first frame in headless Chromium with software GL) with no page errors, and acknowledge, unit selection, compare and select-before-operate work; Confirm and workflows 3, 4, 6 and 7 were not proven there. Run them in a desktop browser.

### Minors

- [Logic] The operator log mixes two clocks, so an effect can be listed before its cause - operator entries carry wall-clock time (`ConsoleModel.cs`, `_time.GetLocalNow()`), alarm transitions carry the tick's snapshot time (`PacConsole.Core/Console/ConsoleView.Ops.cs:93`) - R - "Returned to normal 01:42:30" sits under "Fan speed setpoint 100 → 75 % (at once, Manual) 01:42:32" that caused it - owner: mvux
- [Visual] A converter brush on a `Run` does not repaint on a live theme switch - `EquipmentInspector.xaml:215` (`InspectorState` through `ToneConverter`) - R - after Light → Graphite the word "Nominal" stayed the Light muted ink (about #5A6573 on #33373D) until the selection changed the text - owner: gold-standard-pass
- [Visual] A collapsed faceplate can stay painted - R - after TRB-04 → GEN-03 (picker), TRB-04's "Load setpoint 620 MW / Apply" stayed drawn below the generator's inspector, over the Overview tab too, until the next pointer press there; seen with the software renderer under Xvfb, so check on a GPU host before treating it as the app's - owner: uno-verify
- [Responsive] The header's gap text clips at 390 px - `ConsoleHeader.xaml:41` (`TargetGapText`) - R - "15 MW below targe" at 390×844 - owner: gold-standard-pass
- [Accessibility] Row buttons do not name their target - `EquipmentInspector.xaml:103-116` (Acknowledge, Shelve 1 h, Unshelve) and the five Apply buttons have only their caption; SPEC-OPERATIONS asks that every button name its action and target - C - owner: winui-xaml
- [Testing] The model layer has no tests - `PacConsole.Tests` references Core only; `ConsoleModel` commands and the `Telemetry`/`RetryTelemetry` message loop (`ConsoleModel.cs:262-305`) are covered only by this runtime pass; no UI test project - C - owner: uno-app-ui-testing

## Not checked (X)

- "Show all N" in the alarm list (needs five alarms at once), TKA transfer pumps, STK damper, SIL unloading, the rotate and auto-orbit buttons, arrow keys: code and Core tests only.
- Full screen: Xvfb has no window manager, so `SetPresenter(FullScreen)` left the window at 1440×900.
- Mouse wheel on desktop: XTest wheel presses did not scroll the page or the inspector; the scrollbars and Tab were used instead (the wheel does scroll the inspector in Chromium).
- The window-hidden pause (`Window.VisibilityChanged`) and "System" following an OS theme change: no window manager or OS theme under Xvfb.
- The none state ("No telemetry from plant 02"): the simulator always sends a snapshot.
- Performance: the off-screen pause and the lift cache were not re-measured in this pass.
- The Win32 and macOS desktop hosts.

## Noted for other skills

- uno-audit: build 0 warnings with warnings as errors; `SolidRenderer.cs:575` and `:809` start `Task.Run` rasterizers that are never observed, so a fault there is dropped silently; `SolidRenderer.cs` is 1,190 lines and `PlantViewport.xaml.cs` 622.
- gold-standard-pass: the stale `Run` colour after a theme switch and the 390 px header clip above.

## Unresolved Questions

- Data `Simulated`: confirm that simulated telemetry is the product.
- Authentication `N/A`: confirm that a fixed "Operator 04" with no login is final.
- Persistence: operator data is "durable" in SPEC-OPERATIONS and deferred by you; is it in scope for this sample?
- On a telemetry fault the page replaces every value with "Telemetry stopped / The last values are no longer current." Should the last values stay visible and marked stale instead, as an operator console usually does?
- The log shows the latest 8 of 200 entries, by spec. Do you want a full log view?
- Workflows 1-7 were derived from the specs' flows; confirm them.
- X on supported heads: Android, iOS and WinAppSDK; most of WebAssembly (and the Win32 and macOS desktop hosts).
