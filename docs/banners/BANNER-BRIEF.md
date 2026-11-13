# Gallery banner brief: PAC

Production-quality banners for the Uno Platform Studio samples gallery, made from real captures of **PAC**
(`pac`). This brief is the template the Cargo test run proved (2026-11-06; owner: "I like the option of having all 11
candidates"), with this sample's specifics at the end.

## The set: 11 banners, plus 2 heroes

| Family | Count | What |
|---|---|---|
| Showcase | 3 | The house style: real screens in generic device frames (desktop window, tablet, phone), Light and Dark together, on a backdrop tinted from the app's palette. No text. |
| Focused | 4 | One signature element, isolated or zoomed: captured at high resolution, with a slight perspective or tilt, a soft shadow, background blur or falloff, and generous negative space. |
| Stylized | 3 | Compositions that stop the scroll and stay on brand. Examples: an isometric or perspective stack of screens, a diagonal Light/Dark split of one screen, a tilted mosaic of the app's states, an editorial macro crop. |
| Titled (optional) | 1 | The strongest banner again, with the app's display name in the app's own display font. This is the only text allowed. |

- **Exports:** every banner at 1x (1200×630) and 2x (2400×1260).
- **Heroes:** the two strongest also get a hero version at 1920×1080.
- **Names:** use the Cargo names where they fit (`showcase-desk`, `showcase-phones`, `showcase-daynight`, `focus-<element>`,
  `stylized-split`, `stylized-stack`, `stylized-mosaic`, `focus-<element>-titled`). Rename or replace a composition when
  the app has no material for it: a single-screen control gets more focused and stylized banners, not empty device frames.

## House style to match, then go beyond

These are the 1200×630 PNGs. Look at each before starting:
- `/home/user/uno-lab/SalesDashboard/heatmap-card-gallery-banner.png`: two tablets, Light and Dark, on a soft textured grey.
- `/home/user/uno-lab/Sweather/sweather-gally-banner.png`: phone, tablet and phone on a warm backdrop tinted from the app's palette.
- Cargo's set: `/home/user/banners/cargo/finals/`, with its compositions in `/home/user/banners/cargo/src/`.

Cargo's set is the worked example. Reuse its tooling and adapt it; don't copy its look:
- `src/frames.css` (generic device frames);
- `src/tools/cap.js` (the capture driver);
- the HTML compositions.

## Non-negotiable

- **Real pixels only.**
  - Every piece of UI is a capture of the running app. Never redraw, mock, retouch or invent UI, data, logos or copy.
  - Styling happens only around and on top of real captures: framing, crop, perspective, light, blur, backdrop.
  - Building the app with a different palette seed is real output, and is allowed for a theming composition. Say so in the README.
- **Generic device frames,** drawn with CSS. Never a recognizable trademarked device: no copied notch shapes, logos or camera bumps.
- **Never enlarge a capture beyond its native pixels.**
  - Capture at `deviceScaleFactor` 2 or 3; Uno WebAssembly renders at the device pixel ratio.
  - The 2x export and every zoomed crop must stay sharp at full size.
- **Clean states.**
  - None of these in a frame: loading spinners, half-drawn frames, debug overlays, overlay scroll indicators, focus rings, cursors, the Studio toolbar, or empty data.
  - Use the app's seeded sample data, and let animations settle before capturing.
- **Readable and safe.**
  - Key content stays inside the central 90%.
  - No text smaller than about 10 px at 1x unless it is deliberately out of focus.
- **Palette-true.** Backdrop and accent colours come from the app's palette file and App.xaml. List the values you used in `src/frames.css` or the README.
- **No third-party brands.** No third-party logos, product names or trademarks prominent in a banner, and no real people.
  - If the app's own content includes one, pick states that don't feature it, and report it.
- **Agreement across frames.** In a multi-screen composition, captures come from one session so their data and clocks agree.
  - Where a clock still differs by a minute, say so in the README.

## How

1. **Build once.**
   - Publish the app for WebAssembly (Release) into your scratch folder.
   - Then delete the source's `bin` and `obj` at once; disk is tight, with several agents building.
   - Serve `wwwroot` with `python3 -m http.server` on your assigned port.
   - Prefix dotnet with
     `MSBUILDDISABLENODEREUSE=1 flock -o /tmp/claude-0/-home-user-uno-lab/1f321ef3-f5a7-5f56-b03d-b56c3940c4e2/scratchpad/build.lock`.
2. **Capture.**
   - Use Playwright (`require('playwright')`, `chromium.launch()`), at the viewport each state needs: desktop 1440x900 or 1600x1000, phone 390x844.
   - Settings: `deviceScaleFactor: 2` or `3`, and `colorScheme: 'light' | 'dark'` (the app follows it unless the specifics say otherwise).
   - Drive the app by clicks and keys, as a user would; Uno renders an accessibility tree you can target, or click by position.
   - To isolate an element, screenshot its bounding box or crop.
3. **Compose.**
   - Write HTML/CSS: gradients, a noise texture through an inline SVG `feTurbulence`, `perspective` / `rotateX` / `rotateY`, `filter: blur`, layered shadows.
   - Render with Playwright at the exact output size (`deviceScaleFactor` 1 for 1x, 2 for 2x).
   - Keep one HTML file per banner, so it can be re-rendered.
4. **Review (bounded).**
   1. Render all finals and make a contact sheet.
   2. Look at every image at full size.
   3. Name the three largest defects (file, what is wrong, fix) and fix them.
   4. Re-render, and repeat once.

   Typical defects: blur from enlarging, a clipped edge or label, a loading state, muddy contrast, a backdrop that fights the app, uneven shadows, crowding, a weak focal point.
5. **Leave the app alone.** Do not modify the app's repository: no commits, no tracked-file changes. Only build output, and delete it.
6. **Housekeeping.**
   - Only stop processes you started; other sessions use Xvfb displays.
   - Kill your HTTP server when done, and delete your publish and scratch captures.

## Deliver to `/home/user/banners/pac/`

- `finals/<name>@1x.png` and `finals/<name>@2x.png` for all 11, plus `finals/<name>-hero.png` for the two strongest.
- `src/`:
  - the HTML compositions, `frames.css` and the capture tool;
  - the captures the compositions use, as PNG, at the resolution used;
  - no unused captures.
- `README.md`, with:
  - a table of each banner's family, what it shows, and its source captures (route or state, theme, viewport, DPR);
  - the palette values used;
  - the two strongest and why;
  - anything judged uncertain.
- `manifest.json` (the coordinator builds the full-resolution review page from it), exactly this shape:

```json
{
  "id": "pac",
  "displayName": "PAC",
  "banners": [
    { "name": "showcase-desk", "family": "showcase", "title": "Desk and phones",
      "description": "One line on what it shows.", "pick": false,
      "files": { "1x": "finals/showcase-desk@1x.png", "2x": "finals/showcase-desk@2x.png" } }
  ],
  "notes": [ "Known limits, one line each." ]
}
```

- `family` is one of `showcase`, `focused`, `stylized`, `titled`.
- `pick` is true for the two strongest, and their `files` add `"hero": "finals/<name>-hero.png"`.
- The whole folder stays under about 40 MB: PNG with sensible compression, and no duplicate captures.

## Report back

- The folder, and the finals with one line each.
- The two strongest, and why.
- The defects you fixed, and anything that fell short of production quality.
- Brands or content you had to avoid.
- Disk used: everything deleted except the deliverables.

## This sample

- **What it is:** PacConsole, an industrial plant control console in the style of operator HMIs: plant overview, live telemetry and trends, alarms, select-before-operate commands.
- **Source:** `/home/user/pac`, branch `studio` (Uno.Sdk 7.0.0-dev.132). Project `PacConsole/PacConsole.csproj`.
- **Publish:** `MSBUILDDISABLENODEREUSE=1 flock -o /tmp/claude-0/-home-user-uno-lab/1f321ef3-f5a7-5f56-b03d-b56c3940c4e2/scratchpad/build.lock dotnet publish PacConsole/PacConsole.csproj -f net10.0-browserwasm -c Release -o <scratch>`. If the project has an app-only TargetFrameworks override, add it.
- **Signature elements:**
  - the plant drawing (Skia), with the selected unit lit;
  - an alarm, with its callout;
  - the select-before-operate flow (pending command, Confirm);
  - trend lines;
  - the equipment thumbnails.
- **Look:** Simple. Palette `PacConsole/Styles/ColorPaletteOverride.xaml` (Primary #2F5D73 Light, #7FB3C8 Dark); alarm and process-state colours are no-role colours in `PacConsole/App.xaml`. The dark theme is called "Graphite" in the More menu. Font: IBM Plex Sans.
- **Media:** the repo's `media/` folder holds older art renders. Don't use them; capture the running app.
- **Ports:** HTTP 8880-8889.
