# HANDOFF: PacConsole A pass 2, pre-transfer cleanup, showcase info file
Updated: 2026-11-04

## 2026-11-04: A pass 2, cleanup and `.studiolive` (branch `feat/production-readiness`, `d5bfdb0` → `c5b71b9` + this file)
Three briefs, in order: the code review's closed list (A pass 2), the pre-transfer cleanup, the showcase info file.

A pass 2, done (each item one commit; `docs/COMPLETENESS.md`, "Re-check after A pass 2", has the table):
- **Structure:** `PlantSolids` (998 lines) is the plant model (71); `PartKit`, `UnitSolids`, `CycleSolids` build it;
  `PlantFraming`, `UnitPicker`, `PartSystems` read it; pinned first by `Fixtures/framing.txt`. `StillFrameCache` (390)
  is `StillRunCache` (235), `PartImageCache` (148) and `LayerRaster` (38). Full screen goes through `IFullScreen`,
  injected into `ConsoleModel` from the window `IApplicationBuilder.Configure` hands over; `App.Shell` is gone.
- **Quality:** the capture tool builds again (SPEC.md, Decisions, A pass 2); one `MotionPreference`; `Compose` reads
  `ops.Setpoints`; no `null!` in the renderer; the journal starts fresh on any storage fault and the log band says the
  record is not saved; one summary on `SelectDevice`.
- **Docs:** SPEC.md's capability table and the model test counts match the code.
- **Runtime:** Debug desktop under Xvfb `:616` (click and hover hit tests, full screen 1440×900 → 1920×1200 and back,
  select-before-operate with Confirm and Esc, the log across a relaunch, Graphite from the menu, a storage fault at
  launch, 390 px) and Release WebAssembly in headless Chromium (click selection, Confirm, Esc).
  Evidence: `docs/evidence/2026-11-04-a-pass2/`.
- Full screen under Xvfb needs a window manager to honour `_NET_WM_STATE`; the check used a 40-line watcher that
  applies it (`desktop-fullscreen-requests.txt`).

Cleanup, done: six unused usings (IDE0005), `AlarmBook.ShelvedCount` (test-only) removed, `DepthOrder.Separable` made an
internal test seam, `coverlet.collector` removed, the template's `.run/Readme.md` removed, banner decoration and
restating comments trimmed, 70 design-spike files nothing linked (screenshots and a hairline page) removed from `docs/`, `media/INVENTORY.md`
written (media itself untouched: the owner decides what transfers).

Showcase info file, done: `.studiolive/` (sample.json, agent-context.json, user-conversation.json, icon.png, four
screenshots); the validator prints OK. A custom icon replaces the template's (SPEC.md, Decisions, pre-transfer,
proposed), as do the absolute URLs (question 1 in uno-private#2444).

The PR had a conflict with `main` (central package management in the capture tool): merged `main` into the branch in
`c5b71b9`, the tool keeps central versions.

Open:
- Owner decisions: the proposed decisions in SPEC.md (A pass 2; pre-transfer) and SPEC-OPERATIONS.md; the phone tags
  at 360 px; which of `media/` transfers (`media/INVENTORY.md`).
- iOS and WinAppSDK are CI builds only; WebAssembly workflows 3, 4, 6 and 7 not driven in a browser.

## Last verified state (2026-11-04, this session)
- **Build:** desktop and WebAssembly Debug, Android Debug, the tests' net10.0 build: 0 warnings. Release WebAssembly
  publish: trim warnings only (Uno's and the MVUX generator's). The capture tool builds.
- **Tests:** 364/364 (`dotnet test PacConsole.Tests -p:PacTargetFrameworks=net10.0`).
- **Lint:** every gating rule 0; 77 automation names in 2,240 XAML lines (34.4 per 1,000).

## 2026-11-04: A pass (branch `feat/production-readiness`, `d522a09` → `40bae01` + docs)
Brief: the shared A-pass brief (uno-lab `A-GRADE-PLAN.md`), plus the Android emulator smoke job. Done:
- **Refactors:** `SolidRenderer` split into `SolidGeometry`, `PartPainter`, `StillFrameCache`, `SystemMotion` and an
  orchestrator, pinned by ten reference frames (`PacConsole.Tests/Rendering`, `Fixtures/render`). `PlantViewport`'s camera
  rules moved to `Core/Geometry/CameraRig` (tested); `FrameClock` and `TagOverlay` beside it; the switcher, thumbnails and
  orbit toggles bind `RestingPreset`/`IsAutoOrbit` two-way, the camera tools bind commands.
- **Model tests:** the test project references the app's plain `net10.0` build (`PacTargetFrameworks=net10.0`); 33 tests of
  `ConsoleModel` over a fake clock (25 page-model, 8 persistence). They found a real race (a new limit composed before the change that dates it), fixed by
  keeping the applied setpoints in `PlantOps`.
- **Functional Majors built:** Esc cancels select-before-operate (KeyboardAccelerator on Cancel); operator data persists
  (`operator/operator.json`, atomic writes, restored before the first navigation, the simulated plant resumes at the saved
  tick plus the time closed). Decisions recorded as proposed in `SPEC-OPERATIONS.md` and `SPEC.md` (Decisions, A pass).
- **Accessibility:** row, Apply, Confirm/Cancel, Retry buttons name their target (34.3 names per 1,000 XAML lines);
  "Nominal" repaints on a live theme switch; the 390 px header wraps; the log reads one clock (commands act from the next
  scan); no unnamed Tab stops (Tab walk in `docs/evidence/2026-11-04-a-pass/desktop-tab-walk.txt`).
- **Performance:** orbit geometry allocations halved (scratch buffers); the plant model builds off the UI thread;
  `FallbackValue=Collapsed` on converter Visibility bindings.
- **WebAssembly:** the "Confirm did nothing" was the page reflowing when the IBM Plex faces arrived after the first frames
  (presses landed on the wrong element). Fonts now load before the first navigation; a late Confirm logs "Lapsed". Proven in
  headless Chromium: Confirm, Esc, the log after a reload. Unused ChakraPetch and Plex Mono fonts removed.
- **Android:** `android-emulator` CI job from the shared recipe; green on runs 14 and 15 (marker "Net output" after 20 s, 76
  app nodes, not blank, no crash). The page now keeps to the safe area (the dump had the header under the status bar).
- **CI:** test job uses `-p:PacTargetFrameworks=net10.0`; `concurrency` cancels superseded runs (owner decision).

Open:
- iOS and WinAppSDK are still CI builds only; WebAssembly workflows 3, 4, 6 and 7 not driven in a browser.
- Headless Chromium with software GL draws about one frame a second (rAF 1.0–1.7/s); a GPU browser was not tried.
- Proposed decisions await the owner (SPEC-OPERATIONS, SPEC.md: Decisions, A pass).

## Verified state after the A pass (2026-11-04)
- **Build:** desktop Debug and the tests' net10.0 build, 0 warnings; Release WebAssembly publish, trim warnings only (Uno's).
- **Tests:** 360/360 (`dotnet test PacConsole.Tests -p:PacTargetFrameworks=net10.0`).
- **Lint:** every gating rule 0.
- **Runtime:** X11 under Xvfb (`:472`, `PAC_X11_RENDERER=software`, isolated `XDG_DATA_HOME`); Chromium via Playwright.
  Evidence: `docs/evidence/2026-11-04-a-pass/`.

## Relaunch
```
dotnet build PacConsole/PacConsole.csproj -f net10.0-desktop -p:PacTargetFrameworks=net10.0-desktop
dotnet test PacConsole.Tests -p:PacTargetFrameworks=net10.0
# Debug hooks: --pac-unit=clg --pac-tab=control --pac-section=log --pac-theme=graphite --pac-focus=log; PAC_TELEMETRY_FAULT=5
# Fresh state: an isolated XDG_DATA_HOME, or delete LocalState/operator/operator.json.
# Renderer references: PAC_RENDER_GOLDEN=write redraws PacConsole.Tests/Fixtures/render (only for an intended change).
```

## 2026-11-01: code-review fixes (branch `feat/production-readiness`)
From `CODE-REVIEW-2026-11-01` (uno-lab). Done:
- **Build gate:** `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` in `Directory.Build.props` (ILLink trim warnings, which come from Uno's packages and MVUX's generated code, stay warnings).
- **Alarm history:** the book stays a 15-minute replay (owner's call), but limit and damper changes are now kept with their tick (`PlantOps.SetpointChanges`), so a new limit no longer back-dates alarms.
- **Log and HOLD:** the log is newest first by tick and write order; held trends keep the snapshot's times; the live dot stops while data is held.
- **Page:** `ConsolePage.xaml` is split into `Presentation/Sections` user controls; every list fills its panel through `Controls/Slots` (keeps each row's element across recompositions). Screenshot matrix unchanged.
- **Controls:** Manual holds the ramps and applies setpoints at once; "Show all N" opens the alarm list; shelved alarms are listed with Unshelve.
- **Rendering:** the plant pauses off screen and while the window is hidden; frames reach the render thread as immutable records; the trend chart frees its Skia objects; tags are sized by their measured label.

Open (owner decisions):
- **Phone tags:** 44 px tags do not fit seven to a 360 px drawing (the layout test shows 80 of 82 orbit poses touching; 36 of 82 even at 24 px). Fewer tags on phones, or the picker as the touch route?
- **Apply right after typing (not a bug):** a person's click applies the typed value: 620 → 655 with a 150 ms press and 620 → 665 with 400 ms, checked under Xvfb on 2026-11-01. Only xdotool's instant click, press and release in the same frame, runs Apply before the NumberBox commits on losing focus, so scripted checks should press Tab or Enter (or hold the click) before Apply.
- **Window hidden:** `Window.VisibilityChanged` stops the ambient clock, but Uno's X11 host under Xvfb (no window manager) never raised it, so that path is unverified.

## Where we are
This session ran `uno-audit` and `uno-completeness-audit`, then fixed what they found. Branch `claude/jolly-planck-idsow1`, pushed. The work, in order:
- **Audit:** hygiene, an unbound `Pose` DP set every frame, per-frame allocations, and an Auto orbit automation name.
- **Fault switch and Retry:** a Debug fault switch, `PAC_TELEMETRY_FAULT=<n>`. Retry was dead because `Feed.AsyncEnumerable` ignores refresh; the snapshot feed now uses `Feed.Create` with a `RetryTelemetry` command.
- **Dead code:** the wireframe renderer, `PlantGeometry` and their projection helpers are deleted (SPEC.md phase 7).
- **Platform theme** (`docs/SPEC-THEME.md`):
  - *Superseded 2026-11-06 (theme pass):* the palette is `Styles/ColorPaletteOverride.xaml` (all 33 Simple roles, Light and Default) and the no-role colours are in `App.xaml`'s theme dictionaries; `Styles/Palette.xaml` and `Styles/Tokens.xaml` are gone (README, Built with).
  - `IThemeService` gives System / Light / Graphite and saves the choice.
  - `ThemeBrushes` serves the brushes chosen by converters and code.
  - The Skia palettes follow `ActualThemeChanged`.
- **Minors:**
  - the blank equipment picker
  - a render-thread-safe renderer swap
  - the systems filter on its own row below 1280 px
  - type-size tokens
  - the slider max bound to the view
  - the Reset glyph, and the duplicate menu item removed

## Last verified state
- **Build:** Release `net10.0-desktop`, 0 warnings, 0 errors, warnings as errors (2026-11-01).
- **Tests:** 294/294 (`dotnet test PacConsole.Tests`, 2026-11-01).
- **Lint:** every gating rule 0 (CARD, HEX, TOKENTHEME, BACKBAR, CODEBEHIND, OVERLAY, RESPONSIVE, BUILTIN, ICON).
- **Runtime:** Linux X11 under Xvfb, driven with XTest. The App MCP was not available in the cloud session.
  - All 9 critical workflows were driven, including fault → Retry and theme persistence across a relaunch.
  - Evidence: `docs/audit/2026-10-28/`. Verdict: `docs/COMPLETENESS.md` (Complete with gaps).
- **Not run:** the Win32 and macOS hosts.

## Guidance applied
| Skill / rule / decision | Applied at |
|---|---|
| mvux: errors arrive as feed state; Retry must re-run the source | `PacConsole/Presentation/ConsoleModel.cs` (`Snapshot`, `Telemetry`, `RetryTelemetry`) |
| uno-scaffolding: theme switch is a model command calling `IThemeService` | `ConsoleModel.SetTheme`, `App.xaml.cs` `UseThemeSwitching()` |
| uno-themes: the palette is the theme's, no-role colours in App.xaml (2026-11-06) | `Styles/ColorPaletteOverride.xaml`, `App.xaml` |
| gotcha (Theming and color): converter brushes from a live registry | `Styles/ThemeBrushes.cs`, `Presentation/Converters/ToneConverter.cs` |
| uno-toolkit: `{utu:Responsive}` with custom layouts, no breakpoints in code | `Styles/Layout.xaml` (`ConsoleLayout`, `PlantHeaderLayout`), `Controls/PlantViewport.xaml` |
| skiasharp-uno: renderer use confined to the render path | `Rendering/PlantCanvas.cs`, `Rendering/PlantThumb.cs` (`Swap`, `_drawGate`) |

## Next actions (in order)
1. **Run on Windows:** run workflows 1–9 (`docs/COMPLETENESS.md`) with `uno-verify` and the App MCP. Check tooltips in Graphite.
2. **Persistence:** the operator log, acknowledgements, shelves and setpoints. The user deferred this ("persistence later"); the theme already persists.
3. **Record the gotchas in the shared rules file:** `docs/evidence/2026-10-28-*.md` (AsyncEnumerable refresh, obsolete `ColorOverrideSource`, render thread).
4. **Measure the per-part lift cache** (`997788e`): a Release trace on Windows.
5. **Open design questions:**
   - a full operator log view (the model keeps 200 entries, the page shows 8, by spec)
   - plant height on windows under ~700 px tall
   - an automated UI test project (new dependency, needs a decision)

## Open questions
- Simulated telemetry, the fixed "Operator 04" and desktop-only scope: confirm these as product decisions.
- CI (`.github/workflows/ci.yml`) runs the tests and builds every head on pull requests to `main`.

## Relaunch
```
dotnet build PacConsole/PacConsole.csproj -f net10.0-desktop
dotnet test PacConsole.Tests
# Ask the user before launching a window on their desktop.
# Fault demo (Debug): PAC_TELEMETRY_FAULT=5 dotnet run --project PacConsole -f net10.0-desktop
# Reset the saved theme: delete the app's local settings (Linux: ~/.local/share/PacConsole/).
```
