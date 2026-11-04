# Uno audit: PacConsole

Date: 2026-11-04. Branch `feat/production-readiness`, from `8a5d9da` to the commit that adds this file.
Scope: the `uno-audit` pass only (hygiene, pattern conformance, the semantic lint gate, performance, the build gate
and a runtime re-check). Functional gaps stay in `docs/COMPLETENESS.md` for the next pass.

## Guidance applied

| Skill | Pattern it governs here | Result |
|---|---|---|
| uno-platform-agent | Single project, Uno.Sdk, `Uno0001` as an error, `Platforms/` code, Skia renderer | Holds; template leftovers removed |
| uno-scaffolding (+ `uno-runtime-gotchas.md`) | `{Binding}` on MVUX, semantic-first colour ladder, code-behind budget, breakpoints in `{utu:Responsive}`, the gotchas list | Checked item by item below |
| mvux | Feeds and states, Retry through `ElementName`, feed parameters in commands, no `await` on an AsyncEnumerable feed | Holds (`ConsoleModel.cs:32`, `ConsolePage.xaml:48,63`) |
| uno-navigation | One `RouteMap` in `App.xaml.cs`, region navigation, no code-behind navigation | Holds (one route) |
| uno-toolkit | `{utu:Responsive}` with custom layouts, `ResponsiveView`, `CommandExtensions` | Holds (89 Responsive values, 0 AdaptiveTriggers) |
| Theme (Simple, no dedicated skill) | Roles per theme through `ThemeColors`, tokens in ThemeDictionaries | Holds; 4 unused tokens removed |
| winui-xaml | Virtualization, unsubscribe on Unloaded, no UI-thread blocking | Lists are bounded (Slots panels), handlers paired with Unloaded |
| skiasharp-uno | `SKCanvasElement`, render-thread snapshots, cached paints and effects | Holds; per-frame work reduced |
| dotnet-csharp | Async faults observed, IDisposable ownership | Two fire-and-forget workers fixed |

## Semantic lint gate

`xaml-semantic-lint.ps1 -Path PacConsole`, before (`8a5d9da`) and after:

| Rule | Before | After |
|---|---|---|
| CARD | 0 | 0 |
| HEX | 0 | 0 |
| TOKENTHEME | 0 | 0 |
| BACKBAR | 0 | 0 |
| CODEBEHIND | 0 | 0 |
| OVERLAY | 0 | 0 |
| RESPONSIVE | 0 | 0 |
| BUILTIN | 0 | 0 |
| ICON | 0 | 0 |
| TOKEN (info) | 88 | 88 |
| ADAPTIVE (info) | 0 | 0 |
| TEMPLATE (info) | 6 | 6, now with the reason in `Styles/Controls.xaml` |
| WORKAROUND (info) | 0 | 0 |

Exit code 0 both times. The surviving allowances carry their reason at the site: 15 `allow codebehind` in
`Controls/PlantViewport.xaml.cs` (camera tools, frame loop, drag, keys) and 1 in `ConsolePage.xaml.cs` (full screen),
3 `allow builtin` (`RangeBar`, `Slots`, `FlowPanel`, each naming the control it ruled out) and 5 `allow card`
(segment tracks, the alarm callout, the confirm bar, the thumbnail frame).

## Findings and fixes

### Stack currency (report only)

- Uno.Sdk `7.0.0-dev.115` (owner decision). Newest dev `7.0.0-dev.132`, newest stable `6.7.30`.
- Resolved through the SDK: Uno.WinUI `7.0.0-dev.2509`, Uno.Extensions `8.0.0-dev.150`, Uno.Toolkit `11.0.0-dev.157`,
  Uno.Themes `9.0.0-dev.61`, SkiaSharp `4.151.1` (stable `4.154.0` exists; it comes with the SDK, so it moves with it).
- Test packages: `FluentAssertions` 6.12.0, stable is 8.11.0. Version 8 is under a commercial licence for commercial
  use, so the bump is a licence decision, not a version bump. Every other pin in `Directory.Packages.props`
  (coverlet.collector 10.1.0, Microsoft.NET.Test.Sdk 18.11.0, NUnit 5.0.0, NUnit3TestAdapter 6.3.1,
  Microsoft.Extensions.TimeProvider.Testing 10.11.0) is the newest stable.
- TFMs are .NET 10 on all five heads.

### Code hygiene

| Finding | Where | Fix |
|---|---|---|
| Template leftovers | `PacConsole/ReadMe.md`, `Assets/SharedAssets.md`, `Strings/en/Resources.resw` (only `ApplicationName`, read by nothing), `Assets/Backdrop/grid.png` (the grid backdrop the Fukasawa direction removed), `Platforms/Android/Assets/AboutAssets.txt` (shipped inside the APK), `Platforms/Android/Resources/AboutResources.txt`, the "Hello World" Android string | Removed, `93a6483` |
| Unused usings (IDE0005 run on desktop, Android and WebAssembly) | `App.xaml.cs:3` (`Uno.Resizetizer`: `SetWindowIcon` is generated in the app namespace), `PlantViewport.xaml.cs:3`, `ConsolePage.xaml.cs:1`, `ConsoleModel.cs:5` (implicit global usings), `GlobalUsings.cs:6` (template alias), `Platforms/Android/Main.Android.cs` (10 of 12), `MainActivity.Android.cs` (2) | Removed, `f864251`, `c799266` |
| Dead members | `CameraPose.YawDegrees`, `ElevationDegrees` (`Core/Geometry/Projection.cs:28,30`), `ConsoleView.HasAlarmRows` (`Core/Console/ConsoleView.Ops.cs:39`) | Removed, `f864251` |
| Unused parameters (IDE0060) | `AlarmBook.Entry(..., long tick)` (`Core/Operations/Alarms.cs:162`), test helper `Exit(Volume v, ...)` (`Tests/PlantSolidsTests.cs:225`) | Removed, `f864251` |
| Unused palette tokens | `AccentTintBrush`, `AccentBorderBrush`, `BorderBrushStrong`, `CircuitElectricalBrush` (both themes, `Styles/Palette.xaml`) | Removed, `f864251` |
| Comments | Checked for narration and change history: the comments are constraint and measurement notes; the section labels in `ConsoleView.V3.cs` and `PlantViewport` group members. Nothing removed | None needed |

### Pattern conformance

- MVUX pages bind with `{Binding}`; no `x:Bind` anywhere.
- Retry in the FeedView's Error and None templates reaches the model through `ElementName=ConsoleFeed`, and the snapshot
  feed is `Feed.Create` with a `RetryTelemetry` command, so Retry really restarts the stream (gotchas: `{Binding Refresh}`
  in ErrorTemplate, AsyncEnumerable refresh).
- Commands read the latest snapshot, view and mode as feed parameters; the model never awaits a feed.
- No literal hex outside `Palette.xaml` and `ColorOverride.xaml`; font sizes come from tokens (`IconSize`, `TypeSize*`).
- Navigation: one `ViewMap`/`RouteMap` in `App.xaml.cs`; no code-behind navigation. The page is
  `NavigationCacheMode="Required"` with `{utu:Responsive}`, which the gotchas list flags, but the app has one route, so
  the page never unloads and the Toolkit reconnect bug cannot occur.
- Theme switch is a model command over `IThemeService`; converter brushes come from the live `ThemeBrushes` registry
  (gotcha: converters snapshot the app theme).
- `UseStudio()` is gated by `APP_NO_HOTDESIGN` and by `NO_UNO_STUDIO` while the SDK's Hot Design client crashes.
- No storyboards: the pulse runs on the shared 20 fps `AmbientClock` (gotchas: Forever storyboards, `AutoReverse` is
  Uno0001). `EffectiveViewportChanged` reads only `EffectiveViewport` (gotcha: Uno0001 members stop later events).
- `SKCanvasElement` ignores XAML opacity: the faded systems are faded in the paint (`SystemFade`).
- Not applied, listed under Remaining: 30 converter-based `Visibility` bindings have no `FallbackValue=Collapsed`
  (`EquipmentInspector.xaml` 26, `OperatorLog.xaml` 2, `ConsolePage.xaml` 2). All sit inside the FeedView's
  ValueTemplate, and desktop shows no flash; the gotcha was measured on Android, which this pass did not run.

### Fault handling

- The still-layer and part-image rasterizers (then in `Rendering/SolidRenderer.cs`) ran as fire-and-forget
  `Task.Run`. An exception was dropped unobserved, and it leaked the images already made (and, for part images, the
  recordings after the failing one). Each worker now catches, frees what it made and logs through the Uno ambient
  logger; the plant keeps drawing those runs as vectors. `6546128`. After the splits the workers are
  `StillRunCache.cs:168` and `PartImageCache.cs:106`, and the report is `LayerRaster.Failed` (`LayerRaster.cs:33`).
  Verified with a fault injected into `Rasterize` (not committed): both workers logged
  `Rasterizing the plant's cached layers failed; they stay vector drawings`, and the plant, a lift included, drew
  (`docs/audit/2026-11-04-uno-audit/injected-raster-fault.png`).

### Performance

Measured on the Debug desktop head, 1440×900, software Skia under Xvfb (`PAC_X11_RENDERER=software`), 4 cores; CPU from
`/proc` over 10 s, allocations from `dotnet-counters` (`dotnet.gc.heap.total_allocated`, 3 × 4 s). Software rendering
makes the CPU figures an upper bound and noisy (±6 points between runs); the allocation rate is stable.

| State | Before (`6546128`) | After (`b3b14ca`) |
|---|---|---|
| Still plant (fans and flow on the 20 fps ambient clock) | 699–710 KB/s, 34–39 % of a core | 578–581 KB/s, 28–40 % |
| Auto orbit | 77.7 MB/s, 143 %, 4 gen0 GCs in 12 s | 86.7 MB/s, 127 %, 9 gen0 GCs (more frames: see below) |

Fixed (`b3b14ca`):
- `Controls/PlantViewport.xaml.cs:446` `Draw` ran the whole tag layout on every ambient frame: seven `Measure` calls,
  `TagLayout.Solve` (2.5 KB a call), the help text and two preset lookups. None of it changes without the camera, a
  lift, the selection, the alerts, the system or the size, so `Draw` now keys that work on those inputs
  (`OverlayInputs`, `:439`) and an ambient frame only hands the drawing its clock. A tag that measures differently (its
  font loaded) or a switcher segment that loads lays out again (`:97`, `OnViewLoaded`). `LiftSettled` (`:324`) no
  longer allocates.
- `Rendering/SolidRenderer.cs:189` read every part's lift into a new array each frame and rebuilt the paint sequence
  each frame; both are reused until something changes.

Measured, not changed (Core geometry, a structural change):
- An orbit frame rebuilds every part's screen geometry: about 2.2 MB in `SolidPart.Build` and 0.44 MB in
  `DepthOrder.Sort`, 4.7 ms of CPU, measured in a Release console over `PacConsole.Core`. That accounts for the
  78–87 MB/s above at about 29–32 frames a second (inferred from the allocation rate; the frame rate itself was not
  counted), so the two orbit rows most likely differ by frame rate, not by work per frame.
- Composing the console view allocates 161 KB per snapshot (every 2 s).
- Building the plant model (`PlantSolids.Default`) costs about 65 ms, cold, on the UI thread when the plant first shows.
- Lists: no `ListView`/`ItemsRepeater`; every list is a bounded `Slots` panel (alarms, 8 log lines, readings), so
  virtualization does not apply.

### Build gate

| Head | Configuration | Result |
|---|---|---|
| Desktop | Release, `--no-incremental`, warnings as errors | 0 warnings, 0 errors |
| WebAssembly | Release, `--no-incremental` | 0 warnings, 0 errors |
| Android | Debug (Release is CI's `dotnet publish`) | 0 warnings, 0 errors |
| Tests | `dotnet test PacConsole.Tests -c Release` | 294 passed, 0 failed |

WinAppSDK and iOS build in CI only (Linux box).

### Runtime verification

Debug desktop under Xvfb with `APP_NO_HOTDESIGN=1`, `PAC_X11_RENDERER=software`, the `--pac-*` hooks and 150 ms xdotool
clicks. Screenshots in `docs/audit/2026-11-04-uno-audit/`:

- Light, 1440×900: a click on the GEN-03 tag selects the generator, its tag grows to "GEN-03 · Generator" over the
  lifted unit and the CLG-05 tag re-lays without overlap (`select-gen.png`); the Elevation thumbnail moves the camera
  and takes the checked frame (`elevation-preset.png`); Reset view returns the check to Isometric.
- Auto orbit: tags and leaders follow the units every frame (`orbiting.png`).
- Graphite, 390×844: the switcher segments in the ResponsiveView template join and show Isometric checked
  (`narrow-graphite.png`); Plan checks Plan, and rotating leaves no segment checked (`narrow-plan-then-rotated.png`).
- Injected rasterizer fault: logged, plant still drawn (`injected-raster-fault.png`).
- No exceptions in any run's log.

## Remaining

Updated after the A pass (2026-11-04, `40bae01`) and A pass 2: every item but the licence decision is done.

| Item | Status |
|---|---|
| Split `Rendering/SolidRenderer.cs` (1,227 lines, five jobs) | Done, `5500a52`: `SolidRenderer` (134 lines) orchestrates `SolidGeometry` (323), `PartPainter` (249), `StillFrameCache` (390) and `SystemMotion` (214), with `SolidPalette.cs` (95). Ten reference frames drawn before the split match after it (`PacConsole.Tests/Rendering`). A pass 2 (`9f9181e`) split `StillFrameCache`'s two caches: `StillRunCache` (235), `PartImageCache` (148) and `LayerRaster` (38); `SolidRenderer` is 129. |
| Split `PlantSolids` (998 lines in two partial files, four jobs) | Done in A pass 2, `a8c7e43`: the model `PlantSolids` (71); `PartKit` (136), `UnitSolids` (324) and `CycleSolids` (276) build it; `PlantFraming` (63), `UnitPicker` (45) and `PartSystems` (39) read it. Pinned by `Fixtures/framing.txt` (`7b9a251`). |
| Split the `PlantViewport` code-behind (654 lines) | Done, `69a98ad`: the camera rules are `Core/Geometry/CameraRig` (206 lines, 11 tests); `FrameClock` (114) and `TagOverlay` (112) sit beside the control (396); the switcher, thumbnails and orbit toggles bind `RestingPreset` and `IsAutoOrbit` two-way and the camera tools bind commands, which removed the six Loaded/Unloaded/Click handlers (4 `allow codebehind` left, from 15). |
| `ConsoleModel` has no tests | Done, `2d5afbb`: the test project references the app's plain `net10.0` build (SPEC.md, Decisions, A pass): 33 page-model tests at the time (25 `ConsoleModelTests`, 8 `OperatorPersistenceTests`), 35 after A pass 2 (26 and 9), plus 12 store tests. |
| Orbit frames rebuild all part geometry | Done, `28b34c6`: scratch buffers for the hull sort, the lathe's rings and silhouette, and the depth sort's pairs and queue: 1.2 MB a frame (from about 2.6 MB), checked by `OrbitAllocationTests`. |
| `FallbackValue=Collapsed` on the 30 converter-based `Visibility` bindings | Done, `0b5d9bb` (31 with the new save-failure line). |
| Pre-build `PlantSolids.Default` off the UI thread | Done, `2d5afbb`: built on a worker while the operator record loads; a fault is logged. |
| FluentAssertions 8 (licence) | Owner decision; unchanged. |
