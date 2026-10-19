# PacConsole art set: how it was made

Two sets, for social posts and launch material:

- **The app prints**, the main set:
  - `media/art/app-*.jpg`: eight captioned prints;
  - `media/art/clean/app-*.jpg`: the same without type;
  - `media/art/poster.jpg`: the launch sheet.
- **The plant prints**, companions: `media/art/plant/` holds fifteen pieces of the plant drawing alone, captioned and clean.

Nothing is painted over, generated or mocked up. UI pixels are captures of the running app; plant pixels come from the product's own renderer.

## The app prints (`ui_art2.py`)

**Capture.** A scratch copy of the app, never committed, adds a capture-only `--capture <outDir>` switch (`Capture/CaptureRunner.cs`, outside this repo). It runs the operator story from SPEC-OPERATIONS.md through the page's own generated commands:

1. Active alarms;
2. Acknowledge CLG-05;
3. the Control tab;
4. start cell 6 and confirm;
5. stop cell 3 and confirm;
6. wait for the alarm to clear;
7. switch to graphite.

At each step it saves named elements with `RenderTargetBitmap` into `media/_source/ui/`. Resolution is real: Uno Platform's internal `RootScale` test override makes the whole visual tree rasterize at 2× (the console, plant, trends and faceplates) or 3× (the alarm list and response, the confirm bar, the log and the header). `RenderTargetBitmap`'s scaled overload only resamples a 1× render, so it isn't used. The window is 1676 × 1080, so the console uses its widest layout, and sits mostly off screen. No pointer or keyboard input is used.

**Composition.**

- **Cards:** each capture sits on the app's card colour (most sections are transparent) with a soft shadow.
- **Grounds:** the app's fill grey on paper, graphite on dark. The graphite pieces put an offscreen plant render behind the cards, dimmed, with a ramp of the ground behind the type.
- **Type:** headlines are IBM Plex Sans. Body lines come from the app's copy:
  - section subtitles;
  - the confirm bar's "confirm within 10 s";
  - the alarm guidance;
  - the operator story in SPEC-OPERATIONS.md.

**Every state is real.** The alarm is the app's simulated CLG-05 fan vibration, and the log rows are the actions the capture run took, with their real timestamps.

To regenerate:

1. Rebuild the scratch copy with `CaptureRunner.cs` and the three-line `--capture` hook in `App.xaml.cs`.
2. Run `PacConsole.exe --capture media/_source/ui`. It takes about 2.5 min, then the app closes itself.
3. Run `python media/_source/ui_art2.py`.

| No. | File | What it shows |
|---|---|---|
| 01 | app-01-console | the console in Active alarms, at 2×, bleeding off the sheet |
| 02 | app-02-alarm-to-resolution | the alarm response, the confirm bar and the resolved log over the breathing CLG-05 render |
| 03 | app-03-select-before-operate | the Control faceplate with its pending "start Cell 6" confirm bar |
| 04 | app-04-trends | the synchronized trends while the alarm stands |
| 05 | app-05-active-alarms | the plant panel in Active alarms with the alarm list over it |
| 06 | app-06-graphite | the resolved console in graphite over the dark plan |
| 07 | app-07-alarm-response | the alarm response card over the cooling plumes |
| 08 | app-08-operator-log | the log of the shift: alarm, acknowledge, start, stop, return to normal |

# The plant prints

Fifteen pieces of the plant drawing alone are in `media/art/plant/` (captioned and clean). The sections below describe how they were made.

## The rendering path

The app draws the plant with `PacConsole/Rendering/SolidRenderer.cs`: `Draw(canvas, parts, projection, frame)` on a SkiaSharp canvas. `capture/` is a small console tool that drives that call offscreen.

- **The product's renderer, unchanged.** `capture/PacArt.csproj` compiles `SolidRenderer.cs` as a linked file, against `PacConsole.Core`, on the SkiaSharp the app runs on (3.119.2). The product code is untouched.
- **The product's palette.** The tool builds the palette with the product's own `SolidPalette.FromResources`. It reads the keys from `Styles/Tokens.xaml` for paper, and from that file overlaid with `Styles/Palette.Dark.xaml` for graphite. `capture/Shim.cs` stands in for the two XAML types that method reads.
- **Offscreen and repeatable.** Each frame is drawn into a raster surface: no window, no app chrome, no screen capture. Every frame uses a fresh renderer, so the app's background layer caching never touches these images. The same shot file gives the same pixels.
- **Long exposure.** A long exposure renders `exposure` frames at a fixed 1/60 s step from `seconds`. It lightens them together on graphite and darkens them on paper. The motion is the app's own: smoke from the stacks, vapour over the fan stacks, steam wisps, energy beads on the conductors, the alarm halo's 1.6 s breath, and the moving flow marks.
- **Captions from the code.** The tool writes `renders/<id>.meta.json` from the product's catalogs:
  - units: `UnitCatalog` code and name, plus the `EquipmentCatalog` description;
  - systems: `PlantSystems.Title`, plus the units the system holds;
  - the alarm: `AlarmBook.Title`, plus the alarm's `Guidance.Cause`;
  - views: the `CameraPresets` label and detail, plus the header's "Plant 02 · Steam-cycle station".
- **Scale.** Shots are drawn at a small logical size × 4, so line weights keep their designed proportions:
  - 640 × 360 → 2560 × 1440 (16:9);
  - 480 × 480 → 1920 (square);
  - 480 × 600 → 1920 × 2400 (4:5).

## Regenerate

```powershell
dotnet build media/_source/capture -c Release
python media/_source/survey.py                     # optional: the contact sheet of every subject (survey/sheet.png)
dotnet run -c Release --no-build --project media/_source/capture -- media/_source/shots.json media/_source/renders
python media/_source/finish.py                     # grade, caption, clean copies, poster
python media/_source/review.py                     # review.png: the pieces as feed-size thumbnails
```

To re-render a few shots only, set `PACART_ONLY=05-electrical,06-alarm` before `dotnet run`. To refinish only those pieces, run `finish.py 05-electrical 06-alarm`. Long exposures take 5 to 30 s each on the CPU.

## Finishing (`finish.py`)

- **Bloom:** graphite pieces only, from pixels brighter than 48/255, at two radii. The ground stays the app's own graphite.
- **Vignette:** soft; 30 % at the corners on graphite, 7 % on paper.
- **Grain:** monochrome, seeded per piece.
- **Caption:** IBM Plex Sans, the app's own face from `PacConsole/Assets/Fonts`, scaled with the image's area. It reads, in order:
  - the catalog number, in the theme's accent;
  - the title;
  - the description, in muted ink;
  - "PAC", the header's wordmark, bottom right.
- **Scrim:** a soft ramp of the ground colour under the caption when the pixels there are busy. Pieces 02, 05 and 06 always get one.
- **Export:** JPEG, quality 95, 4:4:4.
- **Systems grid:** 15 arranges six real renders, three across and two down, each with its system's title.
- **Poster:** a title card and the fifteen pieces, each matted in its own border colour.

## The pieces

| No. | File | Surface | What it is | Beyond the app's defaults |
|---|---|---|---|---|
| 01 | plant-graphite | graphite, 16:9 | the whole plant, isometric | 24-frame exposure (flow marks streak) |
| 02 | flue-gas | graphite, 4:5 | Fuel and flue gas isolated, smoke from the stacks | framed on STK-01 and SIL-06; 150 frames (2.5 s) |
| 03 | steam-water | graphite, 16:9 | Steam and water isolated | 120 frames (2 s) |
| 04 | cooling | graphite, square | Cooling isolated, plumes over the fan stacks | framed on CLG-05 and CND-07; 150 frames |
| 05 | electrical | graphite, 16:9 | Electrical isolated, elevation; energy beads on the conductors | framed on GEN-03, shifted; 66 frames (one bead trip) |
| 06 | alarm | graphite, square | Active alarms: CLG-05 in alarm, selected and lifted (the app's state for that view) | framed on CLG-05, shifted; 96 frames (one breath) |
| 07 | plan-graphite | graphite, 16:9 | the whole plant, plan | shifted right for the caption; 24 frames |
| 08 | plant-paper | paper, 16:9 | the whole plant, isometric | none |
| 09 | elevation-paper | paper, 16:9 | the whole plant, elevation | none |
| 10 | plan-paper | paper, 16:9 | the whole plant, plan | shifted right for the caption |
| 11–14 | stk, trb, clg, sil | paper, 4:5 and square | one unit on its pad, on a large field | none |
| 15 | systems | graphite, 16:9 | the six system views | composed from six renders |

"Framed on" means the camera fits those units. The rest of the plant still draws, cut by the edge. No subject's own parameters (density, speed, colours, data) were changed. The alarm piece shows the app's real alarm (CLG-05 fan vibration) with its real guidance text.

## Left out, and why

- **Hover outline and tag callouts.** They're pointer state and XAML overlays, not part of the renderer, and only make sense under a pointer.
- **Trend charts.** They're drawn inside an Uno Platform control (`TrendChart`), outside this offscreen path. Showing them would mean screenshotting the app.
- **The light-theme alarm view.** The red halo washes out on paper.
- **The electrical plan.** It's too sparse to hold a frame.
- **The other units on paper** (TKA-02, GEN-03, CND-07). They're weaker silhouettes than the four chosen.
