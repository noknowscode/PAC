# HANDOFF: PacConsole audit, Retry, theme, minors
Updated: 2026-10-28

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
- **Build:** Debug `net10.0-desktop`, 0 warnings, 0 errors.
- **Tests:** 191/191 (`dotnet test PacConsole.Tests`).
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
- No CI yet. The repo has a remote, but no workflow runs the tests.

## Relaunch
```
dotnet build PacConsole/PacConsole.csproj -f net10.0-desktop
dotnet test PacConsole.Tests
# Ask the user before launching a window on their desktop.
# Fault demo (Debug): PAC_TELEMETRY_FAULT=5 dotnet run --project PacConsole -f net10.0-desktop
# Reset the saved theme: delete the app's local settings (Linux: ~/.local/share/PacConsole/).
```
