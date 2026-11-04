# SPEC · Theme (light and graphite, done the platform way)

Source: the user, 2026-10-28: "we need proper theme implementation". Today the graphite theme works by repainting the light brushes in place (`ConsoleTheme.Apply`, `Palette.Dark.xaml`). It runs, but every token has one value as far as XAML and the lint can tell (TOKENTHEME 55, HEX 18). Nothing follows the OS theme, nothing persists the choice, and the switch is page code-behind.

## Architecture Brief

- **Targets:** unchanged (Uno.Sdk 6.7.30, `net10.0-desktop`, SimpleTheme + Toolkit, MVUX, Skia). *Superseded 2026-10-30:* the app now targets five heads (Android, iOS, WinAppSDK, WebAssembly and `net10.0-desktop`) on Uno.Sdk 7.0.0-dev.115 with SkiaSharp 4; see `README.md`.
- **Source of truth: ThemeDictionaries.**
  - Decision: every theme-varying token lives once per theme in `Styles/Palette.xaml`, under `ThemeDictionaries` keys `Light` and `Dark`. This covers the brushes and the Skia `Solid*` colours. `Palette.Dark.xaml` and `ConsoleTheme` go.
  - Reason: this is the WinUI theme model. `{ThemeResource}` follows the element theme with no app code, and the lint can see both values.
  - Tradeoff: tokens read from code must ask for a theme explicitly (below).
- **Simple roles per theme.**
  - Decision: the colour-role overrides move from `App.xaml` to `Styles/ColorOverride.xaml`, with Light and Dark dictionaries, set through `SimpleToolkitTheme.Colors` → `ThemeColors.OverrideSource`. (The first build used `ColorOverrideSource`, which Uno.Themes 7.1.1 marks obsolete in favour of `ThemeColors`; XAML does not surface the warning.)
  - Reason: stock controls (NumberBox, ComboBox, TextBox, tooltips) get the graphite palette instead of Simple's stock dark. HEX is then 0, because the lint exempts palette files.
- **Switching: `IThemeService`.**
  - Decision: `host.UseThemeSwitching()` (in `Uno.Extensions.Hosting.WinUI` 7.3.6, already referenced, so no new package). `ConsoleModel` gets an `IState<AppTheme> Theme` and a `SetTheme` command. The page's `OnThemeClick` code-behind is deleted.
  - Reason: the scaffolding rule ("theme switch → model command calling `IThemeService`"). The service sets `RequestedTheme` on the window root and saves the choice in app settings (key `CurrentTheme`).
  - Tradeoff: the theme choice persists across launches. That is the service's built-in behaviour, separate from the operator-data persistence deferred to later.
- **Brushes from converters and code.**
  - A converter or `Application.Current.Resources[...]` resolves against the app theme, never the root element's, and is never re-asked after a switch (gotcha: "An IValueConverter that resolves a brush through app resources snapshots the theme").
  - Decision: one small `ThemeBrushes` registry hands out one live `SolidColorBrush` per token key, a copy and never the dictionary's own brush. It repaints every brush's `Color` from `Palette.xaml[ActualTheme]` when the window root's `ActualThemeChanged` fires.
  - `ToneConverter`, `PlantViewport` tag plates and `RangeBar` take their brushes from it.
  - This is the gotcha's measured fix, and it is the only code-side theme mechanism left.
- **Skia palettes.**
  - Decision: `SolidPalette.For(ElementTheme)` and the trend chart's colours read `Palette.xaml` for the element's `ActualTheme`.
  - `PlantCanvas`, `PlantThumb` and `TrendChart` rebuild on their own `ActualThemeChanged` instead of the static `ConsoleTheme.Changed` event.
- **App theme.** `App.xaml` drops `RequestedTheme="Light"`, so "System" can follow the OS. The first-launch default is an open question.
- **Data flow:** menu → `SetTheme(AppTheme)` → `IThemeService.SetThemeAsync` → root `RequestedTheme` → (a) `{ThemeResource}` re-resolves, (b) the root's `ActualThemeChanged` repaints the registry brushes, (c) each Skia element's `ActualThemeChanged` rebuilds its palette.
- **Testing and validation:**
  - Lint: TOKENTHEME 0, HEX 0.
  - Build: 0 warnings. Core tests stay green.
  - Runtime on X11, in both themes: the header, inspector tiles, tag plates, range bars, trends, the plant, thumbnails, NumberBox and ComboBox.
  - Switch twice. Relaunch and confirm the theme came back.

## Design Brief

- **Visual direction:** unchanged. Both palettes keep today's values (Tokens.xaml light, Palette.Dark.xaml graphite) and their stated contrast ratios.
- **New colours:** the Simple roles get a graphite set derived from the same tokens:
  - Background and surface: `#16181B` / `#33373D`
  - On-surface: `#E8EAEC`; on-surface-variant: `#A0A7AF`
  - Outline and outline-variant: `#6E767F` / `#2E3237`
  - Primary and on-primary: `#7FB3C8` / `#0E1A20`
  - Error: `#F38B55`
- **Layout, type, spacing:** unchanged.
- **Theme-invariant tokens:** stroke widths, radii and type styles stay single-valued. No brush is invariant today.

## Interaction Brief

- **Flow:** the More menu (…) holds three `RadioMenuFlyoutItem`s: System, Light, Graphite. The checked item follows `Theme`. This replaces the "Graphite theme" toggle, which could not say "System".
- **Feedback:** the switch is immediate, with no animation, as today.
- **States:** a theme switch while telemetry is in the error or none state also restyles those templates (they use `{ThemeResource}`).
- **Accessibility:** the radio items carry their text as their automation names. Contrast is unchanged.
- **Runtime verification:**
  - Launch, then switch to Graphite and capture the console, the Control tab with NumberBox, and the ComboBox open.
  - Switch to Light and capture again. Relaunch and confirm Graphite or Light came back.
  - Set System and confirm the app follows the host theme.

## Implementation Plan

1. `Styles/Palette.xaml`: ThemeDictionaries Light/Dark holding all 52 tokens. Delete `Palette.Dark.xaml`, and the token block in `Tokens.xaml`.
2. `Styles/ColorOverride.xaml`: Simple role overrides per theme, wired with `ThemeColors.OverrideSource`. Drop `RequestedTheme` from `App.xaml`.
3. XAML: change about 105 `StaticResource` token references to `ThemeResource` (ConsolePage, Controls, PlantViewport, Tokens).
4. `ThemeBrushes` registry. Point `ToneConverter`, `PlantViewport.ApplyTagState` and `RangeBar` at it.
5. Skia: `SolidPalette.For(theme)`; `PlantCanvas`, `PlantThumb` and `TrendChart` move to `ActualThemeChanged`. Delete `ConsoleTheme`.
6. `UseThemeSwitching()`; `ConsoleModel.Theme` + `SetTheme`; the menu radios. Delete the page's `OnThemeClick`.
7. Build, lint, tests, runtime check in both themes, then relaunch. One commit per step that builds.

## Stop Check

Reason:
- This is an architecture change to theming. It brings in a service the app does not use yet (`IThemeService`, already in a referenced package) and changes the switch from a code-behind toggle to a model command.

Risk:
- Converter brushes are the known trap. If the registry misses one, that element keeps the old theme after a switch, and a light-only screenshot won't show it.
- The Skia palettes currently dispose and rebuild their renderer on a switch; the move to `ActualThemeChanged` keeps that path.

Options:
1. **Full platform theme (this spec):** ThemeDictionaries, `{ThemeResource}`, `IThemeService` with System/Light/Graphite, the converter registry, persisted choice.
2. **Same, without `IThemeService`:** set the root `RequestedTheme` from the model through a tiny service of our own. No persisted choice and no "System".
3. **ThemeDictionaries only:** keep the existing toggle and code-behind, and fix only the token source.

Recommendation:
- Option 1. It is the Uno Extensions path the project rules name, it adds no package, and it ends with TOKENTHEME and HEX at 0.

## Decisions (the user, 2026-10-28)

- Option 1, the full platform theme.
- First launch follows the OS theme (`AppTheme.System`).
- The theme choice is saved by `IThemeService`. Operator-data persistence stays deferred.
- The menu offers System / Light / Graphite radios.

## Unresolved Questions

- None open for this spec.

## Who does what: Uno.Themes, Uno.Toolkit, Uno.Extensions

Checked 2026-10-28 against the Uno docs MCP and the shipped assemblies (Uno.Themes 7.1.1, Toolkit 9.1.3, Extensions 7.3.6).

- **Uno.Themes (SimpleTheme, Material, Cupertino) owns the colours, not the switch.** Each theme ships a colour system "for both Light and Dark themes" as ThemeDictionaries. Its styles use `{ThemeResource}`, so they follow whatever theme the element tree is in, with no extra code. Brand colours go in per theme through `Colors` (`ThemeColors.OverrideSource` / `OverrideDictionary`, or a seed colour). That is `Styles/ColorOverride.xaml` here.
  - Uno.Themes also has `SemanticThemeHelper` (Primary, Secondary and Tertiary seeds), which regenerates the *palette* at runtime. That changes brand colours, not light/dark. Its public API has no light/dark setter.
- **The switch is `RequestedTheme` on the app's root element.** Two shipped APIs set it:
  - `Uno.Toolkit.UI.SystemThemeHelper.SetApplicationTheme(XamlRoot or Window, ElementTheme)` / `SetRootTheme(...)`. This is the low-level call; it persists nothing and has no "System" state.
  - `Uno.Extensions.Toolkit.IThemeService` (`UseThemeSwitching()`): sets the same root theme from a model, saves the choice (`CurrentTheme` setting), restores it at launch, and supports `AppTheme.System`. This app uses it.
- **What neither does:** re-run a converter or a code lookup that picked a brush earlier. That is why `ThemeBrushes` exists (uno-runtime-gotchas, Theming and color).
- **Popups:** the Toolkit docs warn that popup-root content (flyouts, ContentDialog, tooltips) may not pick up a root theme. On this Skia desktop head, the ComboBox dropdown and the More menu followed Graphite (checked 2026-10-28). Tooltips were not checked.
