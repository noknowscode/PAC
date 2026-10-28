# Completeness audit: PacConsole (2026-10-28, Uno.Sdk 6.7.30, heads: net10.0-desktop)

Verdict: **Complete with gaps** (0 Blockers, 3 Majors, 9 Minors)

Revision 2, the same day. Since revision 1:

- Retry is fixed and proven, using a Debug fault switch.
- The dead wireframe code is gone.
- The theme is now platform-native: System, Light and Graphite, with a saved choice.

Safe to show:

- the CLG-05 alarm story, select-before-operate, typed setpoints, the systems filter and unit selection, trends, shift notes, Hold
- a telemetry fault with Retry (`PAC_TELEMETRY_FAULT=<n>`, Debug)
- the theme switch, and a relaunch that keeps the theme
- the stacked layout below 960 px

All of these are `R` on desktop (X11).

Keep off screen:

- relaunching after operator actions (operator data does not persist yet)
- the Active alarms view after every alarm has cleared (the equipment picker goes blank)

Runtime method: `net10.0-desktop` Debug, run on Linux X11 under Xvfb, driven with XTest pointer and keyboard input, with post-state read from captures. The App MCP was not available in this cloud session. Evidence captures are in `docs/audit/2026-10-28/`.

## Inventory

- **Screens:** 1 intended, 1 present, 0 unreachable, 0 missing. Source: SPEC-CONSOLE.md and SPEC-OPERATIONS.md.
- **Routes:** 1 registered (`Console`), 0 navigation triggers, 0 dead references.
- **Interactions:** about 77 controls, 0 dead by construction, 0 simulated.
  - The Retry buttons in the error and none states now bind to `RetryTelemetry`.
  - The theme menu now has 3 radio items.
- **Critical workflows** (derived, please confirm):
  1. Alarm → acknowledge → the response is shown → the alarm clears
  2. Select-before-operate: select a device command → confirm → the device runs
  3. Typed setpoint → Apply → the value ramps
  4. Systems filter and unit selection
  5. Trends: range, shared cursor, Latest
  6. Shift note → operator log
  7. Plant mode Hold → resume
  8. Telemetry fault → Retry → live again
  9. Theme: System / Light / Graphite, kept across a relaunch

## Completeness by area

| # | Area | Status | Evidence | B | M | m | Note |
|---|---|---|---|---|---|---|---|
| 1 | Screens | Complete | R | 0 | 0 | 0 | |
| 2 | Navigation | Complete | R | 0 | 0 | 1 | benign startup warning `Region '' has no children` |
| 3 | Interactions | Partial | R / C | 0 | 0 | 2 | 9 workflows driven; Shelve, Ack all, Cancel, valves, compare tiles and drag-orbit checked in code and Core tests only |
| 4 | Logic | Complete | R (tests) | 0 | 0 | 0 | 190/190 Core tests |
| 5 | Data | Simulated | R | 0 | 1 | 0 | by design and disclosed; please confirm |
| 6 | States | Complete | R | 0 | 0 | 1 | error and Retry proven; the none state can't be produced by the simulator (same command, `C`) |
| 7 | Forms | Complete | R | 0 | 0 | 0 | |
| 8 | Authentication | N/A | C | 0 | 0 | 0 | fixed "Operator 04" (SPEC-OPERATIONS); please confirm |
| 9 | Responsive/adaptive | Partial | R | 0 | 0 | 2 | chip overflow below 960 px; plant footer below the fold at 560 px |
| 10 | Platform behavior | Partial | R / X | 0 | 1 | 0 | X11 host proven; Win32 and macOS hosts not run |
| 11 | Failure handling | Complete | R | 0 | 0 | 0 | fault → "Telemetry stopped" → Retry → snapshots resume (`retry-recovered.jpg`) |
| 12 | Accessibility | Complete | C / R | 0 | 0 | 0 | Auto orbit named; theme radios carry their text |
| 13 | Performance | Partial | C / X | 0 | 0 | 1 | the lift cache from `997788e` is still unmeasured |
| 14 | Security/privacy | Complete | C | 0 | 0 | 0 | |
| 15 | Visual quality | Partial | R | 0 | 0 | 2 | blank picker; duplicate Reset/Full screen glyph |
| 16 | Testing | Partial | R | 0 | 0 | 1 | no automated UI test |
| — | Theming | Complete | R | 0 | 0 | 0 | Light and Graphite on every surface (XAML, converters, tag plates, range bars, Skia plant, thumbnails, trends, Simple controls, error state); the choice persists |
| — | Persistence | Missing | R | 0 | 1 | 0 | operator data lost on relaunch; deferred by the user ("persistence later") |

## Critical workflows by head

| Workflow | desktop (X11) | desktop (Win32) | desktop (macOS) |
|---|---|---|---|
| 1–7 | R (revision 1 proofs: `ack.jpg`, `sbo-pending.jpg`, `setpoint-apply.jpg`, `note-log.jpg`, `hold.jpg`) | X | X |
| 8. Fault → Retry | R: before the fix, Retry greyed out and stayed stopped (`retry-dead-before.jpg`); after it, the console returned and the snapshot time advanced (`retry-recovered.jpg`); also in Graphite (`fault-error-graphite.jpg`) | X | X |
| 9. Theme | R: first launch followed the host (light); Graphite → Light → Graphite; after a relaunch the console opened in Graphite with "Graphite theme" checked (`theme-graphite-after-relaunch.jpg`, `theme-menu.png`) | X | X |

## Findings

### Blockers

None.

### Majors

- **[Persistence] Operator data is lost on relaunch.**
  - Lost: the log, acknowledgements, shelves and setpoints. Evidence: R (`relaunch-state-lost.jpg`).
  - Deferred by the user. The theme choice already persists.
- **[Data] Telemetry is simulated** (`App.xaml.cs` registers `SimulatedTelemetry`).
  - It is disclosed in the footer and the README. It stays a Major until you confirm it as the product.
- **[Platform] The Win32 and macOS desktop hosts have not been run.**
  - Evidence: X. Run workflows 1–9 through `uno-verify` on Windows.

### Minors

- **[Visual] The equipment picker is blank in Active alarms with no alarms.**
  - Where: `ConsolePage.xaml` equipment ComboBox, `ConsoleView.V3.cs` `EquipmentChoices` and `SelectedIndex`. Evidence: R.
- **[Responsive] Below 960 px, the "Electrical" and "Active alarms" chips sit past the right edge** of a hidden-scrollbar strip. Evidence: R.
- **[Responsive] At 560 px height, the plant footer is below the fold.** Evidence: R.
- **[Interactions] The log keeps 200 entries and shows the last 8.** Documented in SPEC-OPERATIONS. Status: Partial.
- **[Interactions] The cursor slider hardcodes `Maximum="89"`**, which is coupled to `TrendSet.Samples = 90`.
- **[Visual] The "Caps" text style is 11 px, outside the closed type ramp;** a literal 12 also sits on the chip style and 4 trend unit runs.
- **[Visual] "Reset view" uses glyph E740, the same as Full screen.** "Reset plant view" in the More menu duplicates the panel button.
- **[Performance] The renderer is disposed on `Unloaded` and on theme change** while `RenderOverride` may run elsewhere.
  - Evidence: I. Theme switches ran clean 6 times in this session (R), which doesn't rule out the race.
- **[Testing] There is no automated UI test**; the 3 timing tests are `[Explicit]`.

## Not checked (X)

- **The none state:** the simulator always sends a first snapshot. Its Retry uses the same command as the proven error Retry.
- **"System" following an OS theme change at runtime:** Xvfb has no OS theme to flip. First launch did follow the host (light).
- **Shelve, Acknowledge all, Cancel pending, SBO expiry, permissive refusal, valves, compare tiles, the Condition tab, drag-orbit, Full screen:** code and Core tests only.
- **Mouse wheel:** XTest wheel events did not reach the Uno X11 host.
- **Win32 and macOS hosts.**

## Noted for other skills

- **uno-audit:** lint gating rules all 0 (HEX 18 → 0, TOKENTHEME 55 → 0); build 0 warnings; 190/190 tests.
- **gold-standard-pass:** blank picker, chip overflow below 960 px, duplicate Reset/Full screen glyph, 11 px caps.

## Unresolved Questions

- **Data `Simulated`:** confirm that simulated telemetry is the product.
- **Authentication `N/A`:** confirm that a fixed "Operator 04" is final.
- **Android, iOS and WebAssembly `N/A`:** confirm that desktop-only stays the scope.
- **Win32 and macOS `X`:** run workflows 1–9 on Windows with `uno-verify`.
- **Derived workflows 1–9:** confirm that these are the critical paths.
