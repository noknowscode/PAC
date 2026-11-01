# HANDOFF: PacConsole audit, Retry, theme, minors
Updated: 2026-11-01

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
- **Apply right after typing:** clicking Apply while the NumberBox still has focus applies the previous draft (the two-way binding commits on lost focus, after the command has read its parameter). Seen at runtime under Xvfb; Tab or Enter first works. Not fixed here.
- **Window hidden:** `Window.VisibilityChanged` stops the ambient clock, but Uno's X11 host under Xvfb (no window manager) never raised it, so that path is unverified.

## Where we are
This session ran `uno-audit` and `uno-completeness-audit`, then fixed what they found. Branch `claude/jolly-planck-idsow1`, pushed. The work, in order:
- **Audit:** hygiene, an unbound `Pose` DP set every frame, per-frame allocations, and an Auto orbit automation name.
- **Fault switch and Retry:** a Debug fault switch, `PAC_TELEMETRY_FAULT=<n>`. Retry was dead because `Feed.AsyncEnumerable` ignores refresh; the snapshot feed now uses `Feed.Create` with a `RetryTelemetry` command.
- **Dead code:** the wireframe renderer, `PlantGeometry` and their projection helpers are deleted (SPEC.md phase 7).
- **Platform theme** (`docs/SPEC-THEME.md`):
  - `Styles/Palette.xaml` holds the ThemeDictionaries.
  - Simple's roles are set per theme through `ThemeColors` (`Styles/ColorOverride.xaml`).
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
| uno-scaffolding semantic ladder: role overrides per theme, tokens in ThemeDictionaries | `Styles/ColorOverride.xaml`, `Styles/Palette.xaml` |
| gotcha (Theming and color): converter brushes from a live registry | `Styles/ThemeBrushes.cs`, `Presentation/Converters/ToneConverter.cs` |
| uno-toolkit: `{utu:Responsive}` with custom layouts, no breakpoints in code | `Styles/Tokens.xaml` (`ConsoleLayout`, `PlantHeaderLayout`), `Controls/PlantViewport.xaml` |
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
