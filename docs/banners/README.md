# PAC (PAC Control Console) gallery banners

Banners for the Uno Platform Studio samples gallery, made from real captures of **PAC**, the PAC Control Console
(`/home/user/pac`, branch `studio`, Uno.Sdk 7.0.0-dev.132), published Release for WebAssembly and driven in headless
Chromium. Nothing in them is redrawn: the UI, the plant drawing, the data and the copy are the app's own, from its
simulated plant on a first launch (CLG-05 Cooling bank in its seeded fan-vibration alarm). Styling is only around and on
top of the captures: CSS device frames, crop, perspective, falloff masks, blur, light, grain and a drafting grid.

## Finals

All in `finals/`: every banner at 1x (1200 × 630) and 2x (2400 × 1260), and two heroes at 1920 × 1080.

| Banner | Family | What it shows | Source captures (state · theme · viewport · DPR · session) |
|---|---|---|---|
| `showcase-desk` | showcase | The console in a desktop window (Light) between a Graphite phone (the plant, CLG-05 in alarm) and a Light phone (select-before-operate on CLG-05) | console, CLG-05 selected · Light · 1440×900 · 2 · A ; phone top · Graphite · 390×844 · 2 · A ; Control tab, Start Cell 6 pending · Light · 390×844 · 2 · A |
| `showcase-phones` | showcase | Three phones: the alarm response (Graphite), the plant (Light, centre, raised), the synchronized trends with fan vibration over its limit (Graphite), on slate-teal | inspector · Graphite · 390×844 · 2 · A ; phone top · Light · 390×844 · 2 · A ; trends, fan vibration compared, 1 hour · Graphite · 390×844 · 2 · A |
| `showcase-daynight` | showcase | Two tablets, the one-column tablet layout (header, plant, inspector with the alarm response) in Light and Graphite | console · Light and Graphite, 12 s apart · 834×1194 · 2 · A |
| `focus-plant` (+ hero) | focused | The Skia plant drawing in Graphite: TRB-04 Steam turbine selected and lit, CLG-05 in alarm with its dashed outline and callout, every unit tagged; laid back 9° and let go into the Graphite ground | plant panel, TRB-04 selected · Graphite · 1440×900 · 3 · B, crop 740×430 CSS px |
| `focus-alarm` | focused | Active alarms isolated (Light): CLG-05 glowing in the alarm colour with its callout, the rest of the plant ghosted; beside it the alarm list (Medium, onset, 5.3 / 4.5 mm/s, Acknowledge, Shelve 1 h) and the alarm response | "Active alarms" chip · Light · 1440×900 · 3 · B: drawing crop 500×240, alarm list 339×160, response 339×437 CSS px |
| `focus-sbo` | focused | Select-before-operate: the CLG-05 faceplate with "Confirm: start Cell 6 (standby) on CLG-05?", Confirm and Cancel, and the six cells (Cell 3 running hot); a loupe lifts the pending bar out; the plant blurred behind | Control tab after Start on Cell 6 · Light · 1440×900 · 3 · C: inspector 350×598 CSS px ; plant panel (blurred) · Light · 1440×900 · 3 · C |
| `focus-trends` | focused | Synchronized trends, 1 hour: net generation, basin temperature and fan vibration crossing its 4.5 mm/s limit, the shared cursor on a vibration peak, the alarm onset marker | Condition tab, fan vibration compared, 1 hour, cursor dragged to 15:49:47 · Graphite · 1440×900 · 3 · C, band 1440×470 CSS px (stored at 2x) |
| `stylized-split` | stylized | One console, Light and Graphite, cut on the drawing's own 30° isometric angle through window and backdrop | console · Light then Graphite, 17 s apart · 1440×900 · 2 · A, top 868 CSS px |
| `stylized-systems` (+ hero) | stylized | The four process systems as a control-room wall: the plant with Steam and water, Cooling, Fuel and flue gas, and Electrical isolated in turn, each tile lit in its system colour | each system chip on · Graphite · 1440×900 · 3 · B, crop 720×408 CSS px of the drawing (stored at 2x) |
| `stylized-views` | stylized | Plan, Elevation and Isometric of one plant laid out like a drawing sheet | view switcher Plan, Elevation, Isometric, STK-01 selected · Light · 1440×900 · 3 · B, drawing crops |
| `focus-plant-titled` | titled (**optional**) | `focus-plant` with "PAC / Control Console" in IBM Plex Sans SemiBold, the app's own face | as `focus-plant` |

Strongest: **`focus-plant`** and **`stylized-systems`** (both have heroes).
- `focus-plant` is the sample's signature at a glance: the inked isometric plant drawn with SkiaSharp, one unit selected and lit
  in the slate-teal accent, one in alarm in burnt orange, and every tag readable at 1x. It holds up at any crop and on
  any card size.
- `stylized-systems` is the one only PAC could make: the same plant four times, each process system isolated by the
  app and glowing in its own colour, framed like a control-room wall. It stops the scroll and says "operator HMI"
  without a word.

Capture sessions (one browser and one app instance each, so data and clocks agree within a session; logs of every step
in `src/tools/sessions/`):
- **A** (15:10 to 15:18 UTC, DPR 2): desktop, tablet and phone captures, resized in place in one app instance.
- **B** (15:18 to 15:52 UTC, DPR 3): the alarm, the plant with TRB-04 selected, the four systems, the three views.
- **C** (15:56 to 16:17 UTC, DPR 3): the trends and the select-before-operate faceplate.

## Palette

Backdrops, glows and hairlines use the app's own colours, listed with their roles in `src/frames.css`:
- `PacConsole/Styles/ColorPaletteOverride.xaml`. Light: Primary #2F5D73, PrimaryVariantDark #254A5C, PrimaryContainer
  #E3EEF3, Background #F4F5F6, Surface #FFFFFF, SurfaceVariant #E9ECEF, TertiaryContainer #D5DAE0, OnSurface #1E2833,
  Outline #8A939E, OutlineVariant #D9DDE2. Graphite: Primary #7FB3C8, PrimaryContainer #1A272D, Background #16181B,
  Surface #33373D, SurfaceVariant #24282D, OnSurface #E8EAEC, OnSurfaceVariant #A0A7AF, Outline #6E767F.
- `PacConsole/App.xaml` (no-role colours): AlertColor #C2410C Light / #F0793A Graphite, AlertTint #FCEAD7; the process
  systems in Graphite, SystemSteamWater #E8899C, SystemCooling #56C4B6, SystemFuelFlue #AD90DB, SystemElectrical #E2B652.
- The device bezels are a neutral graphite gradient (#3A3F45 to #1B1E21), not a palette colour.
- No palette was re-seeded: every capture is the app's shipped Light or Graphite theme, chosen through
  `prefers-color-scheme` while the app's theme is System.

## Rules kept

- **Real pixels.** Every screen is a full-viewport or element-box capture of the running app; nothing is retouched.
  The only text added is the title in the optional titled banner.
- **No enlarging.** Captures are taken at DPR 2 or 3 and stored at 2x or 3x; every composition shows them at or below
  their native pixels in the 2x export and in the heroes. Planes laid back with `rotateX` pivot on their bottom edge,
  which stays at 1:1 or below.
- **Clean states.** Seeded data; 35 to 45 s after first render before the first capture and 7 to 30 s after each step.
  The app was driven with touch taps and touch pans, so no hover state, focus ring or mouse scroll indicator shows;
  Chromium's scrollbars are hidden. A capture where the app's overlay scroll indicator was still visible was not used.
- **Generic frames.** Window title bars and phone and tablet bezels are plain CSS: no notch, camera or logo.
- **Safe area.** Key content sits inside the central 90%.

## Rebuild

`src/*.html` are the compositions (one per banner, the heroes as their own files), `src/cap/` the captures they use,
`src/frames.css` the shared frames, grid and palette, `src/fonts/` the app's IBM Plex Sans SemiBold (OFL, licence
beside it). Render everything with `cd src && NODE_PATH=$(npm root -g) sh tools/render-all.sh`, or one composition
with `node tools/render.js <html> <out.png> <W> <H> <scale>`. `src/tools/drive.js` is the capture driver: it keeps one
Playwright page open and runs steps (tap, swipe, click, key, scheme, viewport, shot) sent to a local port, so a whole
session shares one app instance; `src/tools/sessions/*.jsonl` record the steps each session ran.

Build used: `dotnet publish PacConsole/PacConsole.csproj -f net10.0-browserwasm -c Release
-p:PacTargetFrameworks=net10.0-browserwasm`, served with `python3 -m http.server`.

## Uncertain

- **Clocks.** The header's snapshot time is the browser's clock (UTC in these sessions). In `showcase-desk` the window
  reads 15:11:33 and the plant phone 15:12:44 (the confirm phone, captured at 15:16, shows no clock); `stylized-split` reads 15:11:33 (Light) and 15:11:41 (Graphite);
  `showcase-daynight`'s tablets were captured 12 s apart.
- **Selection moves with the system filter.** Each `stylized-systems` tile has a different unit selected (TRB-04, CLG-05,
  STK-01, GEN-03); that is how the app picked them as each system was isolated, not a composite.
- **The alarm glow breathes.** `focus-alarm` catches the CLG-05 glow at a strong phase of its pulse; the app animates it.
- **Small labels in the wall.** The unit tags inside the `stylized-systems` tiles are about 8 px at 1x (16 device px in the 2x
  export, about 13 px in the hero). They are secondary to the system glows; the titles of the other banners stay
  above 10 px.
- **The loupe repeats.** `focus-sbo` shows the pending-command bar twice, once in the faceplate and once in the loupe,
  by design.
- **Tablet cut.** `showcase-daynight`'s tablets end mid-tile at the bottom of the app's own 1194 px viewport.
- **The Skia canvas lagged under load.** With four agents sharing the CPU, the plant canvas sometimes showed the
  previous system's frame for 20 to 40 s after a chip changed; every capture used was checked against its chips and title.
- **Titled.** It uses "PAC" over "Control Console" (the app's display name "PAC Control Console") in IBM Plex Sans
  SemiBold; the app's orange PAC mark is not reproduced.
