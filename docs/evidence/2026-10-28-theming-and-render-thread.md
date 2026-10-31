# Gotchas: obsolete Simple colour overrides, and the desktop render thread

Measured 2026-10-28. Uno.Sdk 6.7.30 (Uno 6.7.135), Uno.Themes 7.1.1, `net10.0-desktop` on Linux X11.

## `BaseTheme.ColorOverrideSource` is obsolete, and XAML does not warn

- **Evidence:** in decompiled `Uno.Themes.BaseTheme` (7.1.1), `ColorOverrideSource` carries `[Obsolete("Use Colors.OverrideSource on ThemeColors instead. This property will be removed in a future version.")]`. `ColorOverrideDictionary` carries the matching note.
  - Setting the property in App.xaml built with 0 warnings, because the XAML generator does not report `[Obsolete]`.
  - The setter just forwards to `EnsureColors().OverrideSource`.
- **Use instead:**

  ```xml
  <utus:SimpleToolkitTheme>
    <utus:SimpleToolkitTheme.Colors>
      <ut:ThemeColors OverrideSource="ms-appx:///Styles/ColorOverride.xaml" />   <!-- xmlns:ut="using:Uno.Themes" -->
    </utus:SimpleToolkitTheme.Colors>
  </utus:SimpleToolkitTheme>
  ```

- **Verified:** the Graphite ComboBox dropdown samples `#33373D` surface and `#7FB3C8` primary, which are the override values.
- **Related:** `BaseTheme.DefaultPrimarySeed`, when non-null, turns seed-based palette generation on. Explicit overrides still win, as the samples above show.

## `SKCanvasElement.RenderOverride` runs on the host's render thread

- **Evidence:** the desktop hosts start a dedicated thread. `Uno.UI.Runtime.Skia.Win32` names it "Uno Render Thread" (`InitializeRenderThread`, `_renderThread`); `Uno.UI.Runtime.Skia.X11` has `X11RenderThread`.
  - `SKCanvasElement` (Uno.WinUI.Graphics2DSK 6.7.135) hands `RenderOverride` to an `SKCanvasVisualBase` paint callback, which the compositor calls when it renders.
- **Consequence:** disposing Skia objects that `RenderOverride` uses, from `Unloaded` or a theme handler on the UI thread, can free them mid-draw.
- **Fix used:** `PlantCanvas` and `PlantThumb` draw under a lock, and the UI thread swaps the renderer under the same lock, then disposes the old one after releasing it.
  - Nine rapid Light/Graphite switches ran with no exception.
  - Never reproduced as a crash; this is a source-read finding with a cheap guard.
