# Audit: PacConsole interface (2026-10-15)

Scope: `ConsolePage.xaml`, `PlantViewport`, the three charts, `Controls.xaml` and the tokens, after the instrument redesign (`.impeccable.md`, Direction 2) and the illustration fidelity work. Evidence: source, App MCP screenshots at 1424 × 861 and 1100 × 720 in both themes, and Release frame probes.

## Anti-patterns verdict

**Pass, with two tells left.** The command-center tells are gone: cyan glow, outlined cards, letterspaced capitals, the grid backdrop. Two generated-dashboard habits remain:

- **Hero metric with a sparkline under it** (net output). It's the one display figure, which is intended, but the plot under it is the stock "big number, small chart" pairing, with no axis meaning (no time labels, no units on the axis).
- **Charts that ignore context.** Three fixed plots, whatever unit is selected. Only the vibration plot follows the selection, and it repeats the vibration reading shown 40 px above it.

## Executive summary

- **Issues:** 0 critical, 4 high, 6 medium, 3 low.
- **Top three:**
  1. The unit readings are static numbers with no history.
  2. The charts speak three different visual dialects (axes, tooltips, heights).
  3. Spacing and radii drift between sections.
- **Next:** a chart system with one frame, a metric-driven unit plot, then a spacing and radius pass.

## High

**H1. The unit readings can't be explored.**
- Location: `ConsolePage.xaml` readings grid; `ConsoleView.Tiles`.
- Problem: six readings show a current value only, and the one unit plot is fixed to vibration.
- Impact: an operator can't see whether a temperature or pressure is drifting, which is the main reason to have telemetry at all.
- Fix: make each reading selectable, and plot the selected reading in a chart chosen for that metric:
  - zone bands for vibration
  - a limit line for turbine pressure
  - a 0–100% area for load
  - a narrow-range line for efficiency
  - a trend with its normal band for the rest
- Commands: /clarify, /arrange.

**H2. Three chart dialects.**
- Location: `ConsoleCharts.cs`.
- Problem:
  - The output plot has a left value axis and LiveCharts' stock white tooltip.
  - The hourly plot has a two-label axis and a stock tooltip.
  - The vibration plot has no axis and no tooltip.
  - Plot heights are 72, 64 and 64.
  - None of the plots has a time axis or an axis unit.
- Impact: each plot must be learned separately, and the stock tooltip is the one element on the page that ignores the theme (wrong font, white in graphite).
- Fix: one chart frame for all three:
  - the same value axis (right-aligned labels, 3 gridlines, unit in the header)
  - the same time axis ("−48 s" to "now", or hours)
  - one height scale
  - one hover readout in the section header instead of LiveCharts tooltips
- Commands: /normalize, /typeset.

**H3. No pointer or keyboard reading of chart values.**
- Location: all charts.
- Problem: the only way to read a past value is the stock tooltip, which is mouse-only and missing on vibration.
- Fix: the shared hover readout (a vertical marker plus "at −12 s: 612 MW" in the header), with the accessible help text kept current.
- Commands: /harden.

**H4. Still frames arrive about every 90 ms (Release probe).**
- Location: the plant loop.
- Evidence:
  - Render cost is 0.6 ms after this session's renderer fixes.
  - The UI thread idles 94% of the time.
  - It isn't the shadow path or the picture cache (both probed).
- Impact: fans and flow animate at about 11 fps.
- Status: open; see HANDOFF.
- Commands: /optimize.

## Medium

**M1. Spacing scale drift.** Gaps in use: 0, 2, 4, 5, 6, 8, 10, 12, 14, 16, 20, 24, 28, 32, 56. Readings sections mix spacing 16, 12 and 10, and header-to-content gaps of 0, 2, 6 and 10. The fix is one scale (4, 8, 12, 16, 24, 32, 56) with one header-to-content gap (8) and one gap between sections (24 around each rule). /normalize.

**M2. Radius drift.**
- In use: 4 (tags), 6 (mode segments), 8 (controls), 9 (segment track), 12 (cards), 16 (valves and alert pill).
- The track (9 = 6 + 3 inset) is correctly concentric.
- Problems:
  - The view thumbnails use 8 while the cards they sit beside use 12.
  - The valves are pills at 16 on a 32 px height, while the unit buttons are 8.
- Fix: 4 small, 8 control, 12 surface, and pill = half the height, applied by role. /normalize.

**M3. Section headers in three styles.** "Net output" is `TypeSection` ink; "Hourly generation" and "Valves" are `TypeMicro` with an ink run; "CND-05" is `TypeTitle`. The fix is one header pattern: section title (`TypeSection`) left, meta (`TypeMicro`) right, on one baseline. /typeset.

**M4. Uneven plot heights in one row.** Hourly and vibration are 64 tall beside each other, while the output plot is 72. The two-up row cramps the time axis. Fix: 96 for the unit plot (now the main exploration tool) and 72 for the plant plots. /arrange.

**M5. Legend swatches at 1.6:1.** The circuit legend dots (8 px, the deep circuit tones) on the ground. They're non-text and paired with words, so they pass WCAG 1.4.11 only because the word carries the meaning. Fix: 10 px swatches with a 1 px ink ring. /colorize.

**M6. Delta has no unit.** "Δ +7 vs target 620" should read "+7 MW vs target 620 MW". /clarify.

## Low

- **L1.** "7/9 open" reads better as "7 of 9 open".
- **L2.** Hot Reload's toolbar covers "Plant 02" in Debug builds only (not shipped).
- **L3.** Setpoint notes moved to tooltips. The output-target note ("dashed line on the output plot") is worth showing inline again once the output plot has a labelled target.

## Patterns and systemic issues

- Charts are built per control, with hand-set axes and no shared frame. Every new plot drifts.
- Spacing values are typed inline instead of drawn from a scale.

## Positive findings

- **Hierarchy:**
  - One display figure.
  - An uncontained, framed Fig. 1.
  - Orange used for selection and live data only.
  - Statuses always paired with words.
- **Behaviour:**
  - Theme repaint in place, including charts and canvases.
  - Tabular figures from Plex Sans, so values don't jitter.
- **Plant:** depth order verified by a ray-cast z-buffer at 24 yaws × 3 elevations; hulls disjoint with every unit lifted.

## Recommendations by priority

1. **Now:** H1 to H3 (chart system and metric plot), M1 to M4 (spacing, radii, headers, heights).
2. **Next:** M5, M6, L1, L3.
3. **Later:** H4 (frame interval) with a fresh trace; it needs the render thread and GPU timing, not the UI thread.

## Implemented (same day)

- **H1.** Readings are buttons. Picking one plots it with `MetricPlot` (Core, tested):
  - vibration over ISO 10816 zones
  - TRB-04 pressure against the live inlet-limit setpoint
  - load as a 0–100% share
  - efficiency close around its design point
  - the rest inside their normal band (stack flow's band scales with the damper)
- **H2.** One chart frame (`ConsoleChart`):
  - shared draw margins, so plot edges align down the column
  - three hairline gridlines, and axis labels at the step's precision
  - a time axis ending at "now"
  - LiveCharts tooltips off
- **H3.** Pointing at a plot, or arrow keys while it has focus, moves a hairline marker, and the section header reads "−12 s · 612 MW". Accessible help text follows.
- **H4.** Still open; see HANDOFF.
- **M1–M4.**
  - Spacing on 4/8/12/16/24.
  - One header pattern; thumbnail radius 12.
  - Output and hourly share one plot with a "48 s / 24 h" switch, and the unit plot is 104 tall.
- **M5, M6, L1.**
  - Legend swatches are 10 px with a ring.
  - "+7 MW vs target 620 MW" and "7 of 9 open".
- **Transitions.** A change of plot kind or range eases over 220 ms (cubic out). Once-a-second data steps don't tween.
- **Found while verifying, and fixed:**
  - An MVUX nested record is a bindable proxy updated in place, so a dependency property taking the whole plot saw one value. `MetricPlot` is now a class.
  - Collapsing a LiveCharts chart blanked every chart (shared typeface disposal). Charts are never collapsed now, and each label paint has its own typeface.
  - `ItemsRepeater` over a new array each second recycled its buttons. That left stale hover states and misrouted clicks (Load plotted Flow). Units, readings and valves are now fixed buttons bound to records that MVUX updates in place.
