# Completeness audit: PacConsole (2026-10-28, Uno.Sdk 6.7.30, heads: net10.0-desktop)

Verdict: **Complete with gaps** (0 Blockers, 3 Majors, 4 Minors)

Revision 3, the same day.

- Revision 2 fixed Retry (with a Debug fault switch), removed the dead wireframe code and made the theme platform-native.
- Revision 3 fixes:
  - the blank picker
  - the renderer-disposal race against the desktop render thread
  - the systems-filter overflow
  - the off-ramp type sizes
  - the hardcoded slider maximum
  - the duplicate Reset glyph and menu item
- Revision 3 also moves Simple's overrides off an obsolete Uno.Themes property.

Safe to show, all `R` on desktop (X11):

- the CLG-05 alarm story, select-before-operate, typed setpoints, the systems filter and unit selection, trends, shift notes, Hold
- a telemetry fault with Retry (`PAC_TELEMETRY_FAULT=<n>`, Debug)
- the theme switch and its persistence
- the Active alarms view with nothing in alarm
- every width from 900 to 1440 px

Keep off screen: relaunching after operator actions (operator data does not persist yet).

Runtime method: `net10.0-desktop` Debug, run on Linux X11 under Xvfb, driven with XTest pointer and keyboard input, with post-state read from captures. The App MCP was not available in this cloud session. Evidence captures are in `docs/audit/2026-10-28/`; notes are in `docs/evidence/2026-10-28-*.md`.

## Inventory

- **Screens:** 1 intended, 1 present, 0 unreachable, 0 missing. Source: SPEC-CONSOLE.md and SPEC-OPERATIONS.md.
- **Routes:** 1 registered (`Console`), 0 navigation triggers, 0 dead references.
- **Interactions:** about 76 controls, 0 dead by construction, 0 simulated. The "Reset plant view" menu duplicate is gone.
- **Critical workflows** (derived, please confirm):
  1. Alarm → acknowledge → the response is shown → the alarm clears
  2. Select-before-operate
  3. Typed setpoint → Apply
  4. Systems filter and unit selection, including the picker
  5. Trends: range, shared cursor, Latest
  6. Shift note → operator log
  7. Hold → resume
  8. Telemetry fault → Retry
  9. Theme: System / Light / Graphite, kept across a relaunch

## Completeness by area

| # | Area | Status | Evidence | B | M | m | Note |
|---|---|---|---|---|---|---|---|
| 1 | Screens | Complete | R | 0 | 0 | 0 | |
| 2 | Navigation | Complete | R | 0 | 0 | 0 | a startup log warning `Region '' has no children` is benign: the page has no nested regions. Removing `IsDefault` did not change it, so that change was reverted |
| 3 | Interactions | Partial | R / C | 0 | 0 | 1 | 9 workflows driven, plus Shelve; Ack all, Cancel, valves, compare tiles and drag-orbit checked in code and Core tests only |
| 4 | Logic | Complete | R (tests) | 0 | 0 | 0 | 191/191 Core tests |
| 5 | Data | Simulated | R | 0 | 1 | 0 | by design and disclosed; please confirm |
| 6 | States | Complete | R | 0 | 0 | 0 | error and Retry proven; the none state shares the Retry command (`C`) |
| 7 | Forms | Complete | R | 0 | 0 | 0 | |
| 8 | Authentication | N/A | C | 0 | 0 | 0 | fixed "Operator 04" (SPEC-OPERATIONS); please confirm |
| 9 | Responsive/adaptive | Complete | R | 0 | 0 | 0 | 900, 1100 and 1440 px proven (`header-1100.jpg`, `header-1440-vs-900.png`). Plant height on windows under ~700 px is accepted: fixed by design, and the page scrolls |
| 10 | Platform behavior | Partial | R / X | 0 | 1 | 0 | X11 host proven; Win32 and macOS hosts not run |
| 11 | Failure handling | Complete | R | 0 | 0 | 0 | |
| 12 | Accessibility | Complete | C / R | 0 | 0 | 0 | Reset view now has a tooltip |
| 13 | Performance | Partial | C / X | 0 | 0 | 1 | renderer swap is now render-thread safe; the lift cache (`997788e`) is still unmeasured |
| 14 | Security/privacy | Complete | C | 0 | 0 | 0 | |
| 15 | Visual quality | Complete | R | 0 | 0 | 0 | picker, glyph and type ramp fixed |
| 16 | Testing | Partial | R | 0 | 0 | 1 | no automated UI test |
| — | Theming | Complete | R | 0 | 0 | 0 | Light and Graphite on every surface; 9 rapid switches with no exception; the choice persists; overrides go through `ThemeColors` |
| — | Persistence | Missing | R | 0 | 1 | 0 | operator data; deferred by the user |

## Critical workflows by head

| Workflow | desktop (X11) | desktop (Win32) | desktop (macOS) |
|---|---|---|---|
| 1–3, 5–7 | R (`ack.jpg`, `sbo-pending.jpg`, `setpoint-apply.jpg`, `note-log.jpg`, `hold.jpg`, `slider-max.png`) | X | X |
| 4. Systems and picker | R: Electrical → GEN-03; STK-01 tag; Active alarms with the only alarm shelved shows "CLG-05 · Cooling bank" in the picker (`picker-fixed-no-alarms.jpg`) | X | X |
| 8. Fault → Retry | R (`retry-dead-before.jpg` → `retry-recovered.jpg`, `fault-error-graphite.jpg`) | X | X |
| 9. Theme | R (`theme-graphite-after-relaunch.jpg`, `theme-menu-final.png`) | X | X |

## Findings

### Blockers

None.

### Majors

- **[Persistence] Operator data is lost on relaunch** (log, acknowledgements, shelves, setpoints). Deferred by the user.
- **[Data] Telemetry is simulated.** It is disclosed in the footer and the README. It stays a Major until you confirm it as the product.
- **[Platform] The Win32 and macOS hosts have not been run.** Run workflows 1–9 through `uno-verify`.

### Minors

- **[Interactions] The log keeps 200 entries and shows the last 8.** This is the spec's design (SPEC-OPERATIONS). A full log view would be a new feature.
- **[Performance] The per-part lift cache is unmeasured.** It needs a Windows Release trace.
- **[Testing] There is no automated UI test.** Adding a UI test project is a new dependency and needs a decision.
- **[Interactions] Some controls were checked in code and Core tests only** (see Not checked).

### Fixed in this revision

- **Blank picker:** fixed in `ConsoleView.V3.cs` `ChoicesFor`, test `Picker_always_shows_the_selected_unit`; R.
- **Renderer disposed mid-draw:** fixed in `PlantCanvas.cs` and `PlantThumb.cs` (`Swap`, `_drawGate`). The evidence for the render thread is in `docs/evidence/2026-10-28-theming-and-render-thread.md`.
- **Chips past the panel edge** (below 960 px and from 960 to ~1245 px): fixed with `PlantHeaderLayout`; R at 900, 1100 and 1440 px.
- **The 11 px caps style and literal sizes:** replaced by the `TypeSize*` ramp tokens.
- **Slider `Maximum="89"`:** now `Data.CursorMax`; R.
- **Reset glyph duplicating Full screen, and the duplicate menu item:** Home glyph with a tooltip; menu item and its code-behind removed.
- **Obsolete `ColorOverrideSource`:** replaced by `ThemeColors.OverrideSource`; R (sampled colours).

## Not checked (X)

- **The none state:** the simulator always sends a first snapshot.
- **"System" following an OS theme change at runtime, and tooltips in Graphite:** Xvfb has no OS theme to flip, and no tooltip was captured.
- **Acknowledge all, Cancel pending, SBO expiry, permissive refusal, valves, compare tiles, the Condition tab, drag-orbit, Full screen:** code and Core tests only.
- **Mouse wheel:** XTest wheel events did not reach the Uno X11 host.
- **Win32 and macOS hosts.**

## Noted for other skills

- **uno-audit:** every gating lint rule is 0; build 0 warnings; 191/191 tests.
- **gold-standard-pass:** nothing open from this audit.

## Unresolved Questions

- **Data `Simulated`:** confirm that simulated telemetry is the product.
- **Authentication `N/A`:** confirm that a fixed "Operator 04" is final.
- **Android, iOS and WebAssembly `N/A`:** confirm that desktop-only stays the scope.
- **Win32 and macOS `X`:** run workflows 1–9 on Windows with `uno-verify`.
- **Log view and UI tests:** do you want a full operator log view, or an automated UI test project? Both are new features.
