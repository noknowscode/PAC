// PAC plant 02 as an Anatomy figure: six units on one slab, plus ISO, elevation and plan views.
// node build.mjs [--light] [--audit]
import { writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const SKILL = join(homedir(), ".claude", "skills", "anatomy");
const kitUrl = (path) => pathToFileURL(join(SKILL, path)).href;
const k = await import(kitUrl("kit/iso-kit.mjs"));
const G = await import(kitUrl("kit/lathe.mjs"));
const A = await import(kitUrl("kit/audit.mjs"));
const { kitScript } = await import(kitUrl("scripts/inline-kit.mjs"));

const HERE = dirname(fileURLToPath(import.meta.url));
const theme = process.argv.includes("--light") ? "light" : "dark";

// Facts from PacConsole.Core/Plant/UnitId.cs (UnitCatalog), in the same order as UnitId.
const UNITS = [
  { code: "STK-01", name: "Exhaust stacks", uptime: "412 d", cx: -95, cy: -48 },
  { code: "TKA-02", name: "Fuel tank farm", uptime: "1,208 d", cx: 0, cy: -48 },
  { code: "GEN-03", name: "Generator hall", uptime: "287 d", cx: 95, cy: -48 },
  { code: "TRB-04", name: "Turbine house", uptime: "287 d", cx: 0, cy: 48 },
  { code: "CND-05", name: "Condenser bank", uptime: "96 d", cx: 95, cy: 48 },
  { code: "SIL-06", name: "Ash silo", uptime: "640 d", cx: -95, cy: 48 },
];
const ALERT = 4;
const TOUR = [4, 0, 1, 2, 3, 5];

const PLATE = { x: -160, y: -110, w: 320, d: 220, r: 10 };
const PLATE_H = 10, FOOT_H = 6, PAD = 36, PAD_H = 1.2;
const UP = [0, 0, 1], X = [1, 0, 0], Y = [0, 1, 0];
const vertical = (cx, cy) => G.frameOf([cx, cy, 0], UP, X, Y);

/** Every part of the plant, recorded with its true shape, for camera P. */
function scene(P) {
  const R = A.recorder(P);
  const at = (point) => k.iso(point, P);
  const L = (d, style) => k.lineSvg(d, style);
  const D = (points, style) => k.dotsSvg(points.map(at), style);
  const put = (unit, centre, svg, shapes) => R.put({ at: centre, svg, unit, name: shapes[0].name, shapes: shapes.map((shape) => R.solid(shape)) });
  const main = (unit) => (unit >= 0 ? "u-main" : undefined);

  function slab(unit, name, plan, z, h, { tone = "mid", steps = 4, bevel = 0.5, crease, lit = false, inner = "", extra = "" } = {}) {
    const svg = k.solidSvg(k.slabOf(plan, z, h, P, steps, bevel), { tone, crease, inner, className: lit ? main(unit) : undefined }) + extra;
    put(unit, [plan.x + plan.w / 2, plan.y + plan.d / 2, z + h / 2], svg, [A.slab(plan, z, h, { name: R.nameFor(name) }, steps)]);
  }
  function post(unit, name, cx, cy, r, z, h, { tone = "mid", steps = 28, bevel = 0, lit = false, inner = "", extra = "", owner } = {}) {
    const svg = k.solidSvg(k.cylinder(cx, cy, r, z, h, P, steps, bevel), { tone, inner, className: lit ? main(unit) : undefined }) + extra;
    put(unit, [cx, cy, z + h / 2], svg, [A.cylinder(cx, cy, r, z, h, { name: R.nameFor(name), owner })]);
  }
  function turned(unit, name, F, profile, centre, { tone = "mid", lit = false, inner = "", owner } = {}) {
    const svg = k.solidSvg(G.solidOf(profile, F, P), { tone, inner, className: lit ? main(unit) : undefined });
    put(unit, centre, svg, [A.solid(F, profile, { name: R.nameFor(name), owner })]);
  }

  // Base plate on feet, with a groove, corner screws, a ruler strip and a label plate.
  for (const [x, y] of k.corners(PLATE, 24)) {
    post(-1, "stand.foot", x, y, 8, -PLATE_H - FOOT_H, 1.6, { tone: "lo" });
    post(-1, "stand.foot", x, y, 6, -PLATE_H - FOOT_H + 1.6, FOOT_H - 1.6, { tone: "mid" });
  }
  const screws = k.corners(PLATE, 10);
  const strip = { x: -140, y: PLATE.y + PLATE.d - 13, w: 196, d: 7, r: 1 };
  const ticks = k.topTicks(strip.x + 4, strip.x + strip.w - 4, 4, 5, strip.y + 0.8, 0.6, [2, 3.6], P);
  const label = { x: 72, y: PLATE.y + PLATE.d - 18, w: 60, d: 12, r: 1.2 };
  slab(-1, "stand.plate", PLATE, -PLATE_H, PLATE_H, {
    tone: "lo", steps: 8, bevel: 1.6,
    extra:
      L(k.planOutline(k.insetPlan(PLATE, 5), 0, P), { tone: "faint" }) +
      L(screws.map(([x, y]) => k.ring(x, y, 2.2, 0, P, 16)).join(""), { tone: "lo" }) +
      D(screws.map(([x, y]) => [x, y, 0]), { size: 0.5 }),
  });
  slab(-1, "stand.strip", strip, 0, 0.6, {
    tone: "lo", steps: 3, bevel: 0, crease: "none",
    extra: L(ticks.minor, { tone: "lo" }) + L(ticks.major, { tone: "mid" }) + D([[strip.x + 2, strip.y + 3.5, 0.6], [strip.x + strip.w - 2, strip.y + 3.5, 0.6]], { size: 0.45 }),
  });
  slab(-1, "stand.label", label, 0, 0.8, {
    tone: "lo", steps: 4, bevel: 0,
    extra:
      L(k.planOutline(k.insetPlan(label, 1.6), 0.8, P, 4), { tone: "faint" }) +
      [[5, 4, 38], [5, 6.5, 30], [5, 9, 22]].map(([dx, dy, w]) => L(k.lineOnTop([label.x + dx, label.y + dy], [label.x + dx + w, label.y + dy], 0.8, P), { tone: "lo", free: true })).join("") +
      D([[label.x + label.w - 6, label.y + 4, 0.8], [label.x + label.w - 6, label.y + 8, 0.8]], { size: 0.45 }),
  });

  // A pad under each unit, with anchor bolts and a halo that shows when the unit lifts.
  UNITS.forEach(({ cx, cy }, unit) => {
    const pad = { x: cx - PAD, y: cy - PAD, w: PAD * 2, d: PAD * 2, r: 4 };
    slab(-1, "stand.pad", pad, 0, PAD_H, {
      tone: "lo", bevel: 0.4,
      extra:
        D(k.corners(pad, 4).map(([x, y]) => [x, y, PAD_H]), { size: 0.45 }) +
        `<path class="iso-halo" data-halo="${unit}" style="opacity:0;filter:blur(4px)" d="${k.haloOf(k.insetPlan(pad, 10), PAD_H, 0.1, P)}"/>`,
    });
  });

  // 0 · Exhaust stacks: two stacks on a foundation, a flue duct between them, a gallery on each.
  {
    const { cx, cy } = UNITS[0];
    slab(0, "stk.foundation", { x: cx - 24, y: cy - 16, w: 48, d: 32, r: 3 }, PAD_H, 4.8, { bevel: 0.6 });
    const z0 = PAD_H + 4.8;
    slab(0, "stk.duct", { x: cx - 5, y: cy - 5, w: 11, d: 10, r: 1.5 }, z0 + 2, 10, { inner: L(k.sideSeam({ x: cx - 5, y: cy - 5, w: 11, d: 10, r: 1.5 }, z0 + 7, P, 4), { tone: "faint" }) });
    for (const [x, r, deck, top] of [[cx - 12, 7, 72, 100], [cx + 12, 6, 58, 80]]) {
      post(0, "stk.flange", x, cy, r + 1.6, z0, 2, { bevel: 0.4, extra: D(Array.from({ length: 12 }, (_, i) => [x + Math.cos(i * Math.PI / 6) * (r + 0.9), cy + Math.sin(i * Math.PI / 6) * (r + 0.9), z0 + 2]), { size: 0.4 }) });
      post(0, "stk.shaft", x, cy, r, z0 + 2, deck - z0 - 2, { tone: "mid", lit: true, steps: 48, inner: L([deck - 40, deck - 20].map((z) => k.sideArc(x, cy, r, z, P, 32)).join(""), { tone: "faint" }) });
      post(0, "stk.gallery", x, cy, r + 3.6, deck, 1.6, { extra: L(k.ring(x, cy, r + 3, deck + 1.6, P, 40), { tone: "lo" }) + D(Array.from({ length: 16 }, (_, i) => [x + Math.cos(i * Math.PI / 8) * (r + 3), cy + Math.sin(i * Math.PI / 8) * (r + 3), deck + 1.6]), { size: 0.45 }) });
      post(0, "stk.shaft", x, cy, r, deck + 1.6, top - deck - 1.6, { tone: "mid", lit: true, steps: 48 });
      post(0, "stk.lip", x, cy, r + 0.8, top, 3, { extra: L(k.ring(x, cy, r - 0.6, top + 3, P, 40), { tone: "lo" }) });
    }
  }

  // 1 · Fuel tank farm: a bund wall round two cone-roofed tanks with vents.
  {
    const { cx, cy } = UNITS[1];
    const t = 2.4, h = 6;
    for (const plan of [
      { x: cx - 32, y: cy - 32, w: 64, d: t, r: 0.6 },
      { x: cx - 32, y: cy - 32 + t, w: t, d: 64 - 2 * t, r: 0.6 },
      { x: cx + 32 - t, y: cy - 32 + t, w: t, d: 64 - 2 * t, r: 0.6 },
      { x: cx - 32, y: cy + 32 - t, w: 64, d: t, r: 0.6 },
    ]) slab(1, "tka.bund", plan, PAD_H, h, { steps: 2, bevel: 0.3 });
    // Steps over the front wall, and a pump skid: motor, coupling and pump on one base.
    [[34.4, 36, 2], [33.2, 34.4, 4], [32, 33.2, h]].forEach(([y0, y1, z]) => slab(1, "tka.step", { x: cx - 24, y: cy + y0, w: 6, d: y1 - y0, r: 0.2 }, PAD_H, z, { steps: 2, bevel: 0 }));
    const skid = { x: cx + 6, y: cy + 16, w: 18, d: 9, r: 0.8 };
    slab(1, "tka.skid", skid, PAD_H, 2, { steps: 2, bevel: 0.3, extra: D([[skid.x + 1.5, skid.y + 1.5, PAD_H + 2], [skid.x + skid.w - 1.5, skid.y + 1.5, PAD_H + 2]], { size: 0.4 }) });
    const FP = G.frameOf([0, skid.y + 4.5, PAD_H + 5], X, Y, UP);
    for (const [name, s0, s1, r] of [["tka.motor", cx + 7.5, cx + 15, 3], ["tka.coupling", cx + 15, cx + 16.5, 1.4], ["tka.pump", cx + 16.5, cx + 22, 3]])
      put(1, [(s0 + s1) / 2, skid.y + 4.5, PAD_H + 5], k.solidSvg(G.disc(s0, s1, r, FP, P), { tone: "mid", inner: name === "tka.motor" ? L([10, 12.5].map((s) => G.arcOf(FP, cx + s, r, P)).join(""), { tone: "faint" }) : "" }), [A.disc(FP, s0, s1, r, { name: R.nameFor(name), owner: "tka.pumpset" })]);
    for (const [x, y] of [[cx + 12, cy - 10], [cx - 12, cy + 10]]) {
      const top = 30;
      post(1, "tka.tank", x, y, 13, PAD_H, top - PAD_H, { tone: "mid", lit: true, steps: 56, bevel: 0.6, inner: L([11, 21].map((z) => k.sideArc(x, y, 13, z, P, 40)).join(""), { tone: "faint" }) });
      turned(1, "tka.roof", vertical(x, y), [[top, 13], [top + 7, 2.5]], [x, y, top + 3], { lit: true });
      post(1, "tka.vent", x, y, 1.6, top + 7, 3);
      post(1, "tka.cap", x, y, 2.4, top + 10, 1.2);
    }
  }

  // 2 · Generator hall: a gabled hall with a roller door and louvres, and a transformer outside.
  {
    const { cx, cy } = UNITS[2];
    const hall = { x: cx - 30, y: cy - 24, w: 60, d: 36, r: 1.5 };
    slab(2, "gen.hall", hall, PAD_H, 30 - PAD_H, { tone: "mid", lit: true, bevel: 0.6, inner: L([10, 20].map((z) => k.sideSeam(hall, z, P, 4)).join(""), { tone: "faint" }) });
    const F = G.frameOf([cx - 31, cy - 6, 30], X, Y, UP);
    const gable = [[-19, 0], [19, 0], [0, 9]];
    put(2, [cx, cy - 6, 33], k.solidSvg(G.prismOf(F, gable, 0, 62, P), { tone: "mid", className: "u-main" }), [A.prism(F, gable, 0, 62, { name: "gen.roof" })]);
    const door = { x: cx - 22, y: cy + 12, w: 18, d: 1, r: 0.3 };
    slab(2, "gen.door", door, PAD_H, 19, { steps: 2, bevel: 0, inner: L([4, 7, 10, 13, 16].map((z) => k.sideSeam(door, PAD_H + z, P, 2)).join(""), { tone: "lo" }) });
    for (const y of [cy - 19, cy - 10, cy - 1]) slab(2, "gen.louvre", { x: cx + 30, y, w: 0.8, d: 6, r: 0.3 }, 18, 6, { steps: 2, bevel: 0 });
    const tx = { x: cx + 4, y: cy + 18, w: 20, d: 12, r: 1.5 };
    slab(2, "gen.transformer", tx, PAD_H, 12, {
      bevel: 0.5,
      extra: L(Array.from({ length: 7 }, (_, i) => k.segment([tx.x + 2.5 + i * 2.5, tx.y + tx.d, PAD_H], [tx.x + 2.5 + i * 2.5, tx.y + tx.d, PAD_H + 12], P)).join(""), { tone: "lo" }),
    });
    for (const x of [tx.x + 5, tx.x + 10, tx.x + 15]) {
      post(2, "gen.bushing", x, tx.y + 6, 1.2, PAD_H + 12, 5, { steps: 16 });
      post(2, "gen.bushing.cap", x, tx.y + 6, 1.8, PAD_H + 17, 1, { steps: 16 });
    }
    // Conservator tank on two brackets along the transformer's back edge.
    for (const x of [tx.x + 2, tx.x + 17]) slab(2, "gen.bracket", { x, y: tx.y + 1, w: 1, d: 2, r: 0.2 }, PAD_H + 12, 2, { steps: 2, bevel: 0 });
    const FC = G.frameOf([0, tx.y + 2, PAD_H + 15.8], X, Y, UP);
    put(2, [tx.x + 10, tx.y + 2, PAD_H + 15.8], k.solidSvg(G.disc(tx.x + 1, tx.x + 19, 1.8, FC, P), { tone: "mid" }), [A.disc(FC, tx.x + 1, tx.x + 19, 1.8, { name: "gen.conservator" })]);
  }

  // 3 · Turbine house: HP, IP and LP casings and the generator on one shaft, on pedestals on a deck.
  {
    const { cx, cy } = UNITS[3];
    const deck = { x: cx - 30, y: cy - 12, w: 60, d: 24, r: 2 };
    for (const x of [cx - 28, cx - 2, cx + 24]) for (const y of [cy - 10, cy + 6]) slab(3, "trb.column", { x, y, w: 4, d: 4, r: 0.6 }, PAD_H, 12 - PAD_H, { steps: 2, bevel: 0.3 });
    slab(3, "trb.deck", deck, 12, 4, { bevel: 0.6, inner: L(k.sideSeam(deck, 14, P, 4), { tone: "faint" }) });
    // Steps up to the deck, and the lube oil tank beside it.
    [[16.5, 18, 5.2], [15, 16.5, 9.2], [13.5, 15, 13.2], [12, 13.5, 16]].forEach(([y0, y1, z]) => slab(3, "trb.step", { x: cx + 8, y: cy + y0, w: 6, d: y1 - y0, r: 0.2 }, PAD_H, z - PAD_H, { steps: 2, bevel: 0 }));
    const oil = { x: cx - 28, y: cy + 18, w: 14, d: 10, r: 1.2 };
    slab(3, "trb.oil", oil, PAD_H, 6, { bevel: 0.4, inner: L(k.sideSeam(oil, PAD_H + 3, P, 4), { tone: "faint" }) });
    post(3, "trb.filler", oil.x + 10, oil.y + 5, 1.4, PAD_H + 6, 1.6, { steps: 16 });
    const Z0 = 28, F = G.frameOf([cx, cy, Z0], X, Y, UP);
    for (const [s0, s1] of [[-30, -27], [-15.5, -13.5], [-0.5, 1.5], [18.5, 20.5], [28.5, 30]])
      slab(3, "trb.pedestal", { x: cx + s0, y: cy - 5, w: s1 - s0, d: 10, r: 0.8 }, 16, Z0 - 2 - 16, { steps: 2, bevel: 0.3 });
    for (const [s0, s1] of [[-28, -26], [-16, -13], [-1, 2], [18, 20.5], [28.5, 29.5]])
      put(3, [cx + (s0 + s1) / 2, cy, Z0], k.solidSvg(G.disc(s0, s1, 2, F, P), { tone: "mid" }), [A.disc(F, s0, s1, 2, { name: R.nameFor("trb.shaft"), owner: "trb" })]);
    for (const [name, profile, mid] of [
      ["trb.hp", [[-26, 5], [-25, 6], [-17, 6], [-16, 5]], -21],
      ["trb.ip", [[-13, 6.5], [-12, 8], [-2, 8], [-1, 6.5]], -7],
      ["trb.lp", [[2, 9], [3.5, 11], [16.5, 11], [18, 9]], 10],
      ["trb.gen", [[20.5, 7.5], [28.5, 7.5]], 24.5],
    ]) {
      const r = Math.max(...profile.map(([, r]) => r));
      turned(3, name, F, profile, [cx + mid, cy, Z0], { lit: true, owner: "trb", inner: L(G.arcOf(F, mid, r, P), { tone: "faint" }) });
    }
  }

  // 4 · Condenser bank: a mechanical-draft cell on its basin, louvred air inlets, six fan stacks.
  {
    const { cx, cy } = UNITS[4];
    const basin = { x: cx - 30, y: cy - 24, w: 60, d: 48, r: 2 };
    slab(4, "cnd.basin", basin, PAD_H, 4, { bevel: 0.5 });
    const cell = { x: cx - 28, y: cy - 22, w: 56, d: 44, r: 2 };
    slab(4, "cnd.cell", cell, PAD_H + 4, 25, {
      tone: "mid", lit: true, bevel: 0.6,
      extra: D(Array.from({ length: 13 }, (_, i) => [cell.x + 2 + i * 4.33, cell.y + cell.d - 1.2, PAD_H + 29]).concat(Array.from({ length: 10 }, (_, i) => [cell.x + cell.w - 1.2, cell.y + 2 + i * 4.44, PAD_H + 29])), { size: 0.4 }),
      inner: L([8, 10, 12, 14, 16].map((z) => k.sideSeam(cell, PAD_H + 4 + z - 4, P, 4)).join(""), { tone: "lo" }) + L(k.sideSeam(cell, PAD_H + 19, P, 4), { tone: "faint" }),
    });
    const top = PAD_H + 29;
    for (const x of [cx - 18, cx, cx + 18]) for (const y of [cy - 11, cy + 11])
      post(4, "cnd.stack", x, y, 7.5, top, 6, {
        tone: "mid", lit: true, steps: 40, bevel: 0.5,
        extra: L(k.ring(x, y, 6.3, top + 6, P, 40), { tone: "lo" }) + L(k.ring(x, y, 1.4, top + 6, P, 16), { tone: "mid" }) + `<path class="iso-line" data-tone="mid" data-fan="${x},${y},${top + 6}" d="${blades([x, y, top + 6], 0, P)}"/>`,
      });
  }

  // 5 · Ash silo: a body on four legs over a hopper and chute, a cone roof with a vent filter.
  {
    const { cx, cy } = UNITS[5];
    for (const [dx, dy] of [[-10, -10], [10, -10], [-10, 10], [10, 10]]) slab(5, "sil.leg", { x: cx + dx - 1.5, y: cy + dy - 1.5, w: 3, d: 3, r: 0.6 }, PAD_H, 26 - PAD_H, { steps: 2, bevel: 0.3 });
    for (const plan of [{ x: cx - 8.5, y: cy - 11.5, w: 17, d: 3, r: 0.3 }, { x: cx - 11.5, y: cy - 8.5, w: 3, d: 17, r: 0.3 }, { x: cx + 8.5, y: cy - 8.5, w: 3, d: 17, r: 0.3 }, { x: cx - 8.5, y: cy + 8.5, w: 17, d: 3, r: 0.3 }])
      slab(5, "sil.brace", plan, 12, 2, { steps: 2, bevel: 0 });
    post(5, "sil.chute", cx, cy, 2, 8, 6, { steps: 20 });
    turned(5, "sil.hopper", vertical(cx, cy), [[14, 3], [26, 10.5]], [cx, cy, 20], { lit: true });
    post(5, "sil.body", cx, cy, 13, 26, 36, { tone: "mid", lit: true, steps: 56, bevel: 0.6, inner: L([38, 50].map((z) => k.sideArc(cx, cy, 13, z, P, 40)).join(""), { tone: "faint" }) });
    turned(5, "sil.roof", vertical(cx, cy), [[62, 13], [68, 3]], [cx, cy, 65], { lit: true });
    post(5, "sil.vent", cx, cy, 2, 68, 3, { steps: 16 });
    post(5, "sil.filter", cx, cy, 3.2, 71, 5, { steps: 24, bevel: 0.4 });
  }

  A.settle(R, P);
  R.items.sort((a, b) => a.key - b.key);
  const body = R.items.map((item) => (item.unit >= 0 ? `<g data-unit="${item.unit}">${item.svg}</g>` : item.svg)).join("");
  return { R, body };
}

/** Five fan blades on a stack's top at angle a. */
function blades([x, y, z], a, P) {
  let d = "";
  for (let b = 0; b < 5; b++) {
    const t = a + (b * 2 * Math.PI) / 5;
    d += k.lineOnTop([x + Math.cos(t) * 1.4, y + Math.sin(t) * 1.4], [x + Math.cos(t) * 6.3, y + Math.sin(t) * 6.3], z, P);
  }
  return d;
}

const tops = [104, 42, 40, 40, 37, 77];
function camera(width, height, azimuth, elevation, pad) {
  return k.fitProjection([...k.boxCorners(PLATE, -PLATE_H - FOOT_H, 0), [-107, -48, 104], [-95, 48, 77]], width, height, { pad, azimuth, elevation });
}

// The hero, at the kit's default camera.
const W = 640, H = 420;
const P = camera(W, H, 45, 30, 30);
const hero = scene(P);
A.auditOrExit(hero.R, P);

// Screen hit areas: each unit's rest box, as a hull in viewBox units.
const hulls = UNITS.map(({ cx, cy }, unit) =>
  G.hullOf([[-PAD, -PAD], [PAD, -PAD], [PAD, PAD], [-PAD, PAD]].flatMap(([dx, dy]) => [k.iso([cx + dx, cy + dy, 0], P), k.iso([cx + dx * 0.6, cy + dy * 0.6, tops[unit]], P)])),
);
const depths = UNITS.map(({ cx, cy }) => k.depthOf([cx, cy, 0], P));

const heroSvg = k.figureSvg({
  width: W,
  height: H,
  label:
    "Plant 02 on one base plate: two exhaust stacks with galleries, a fuel tank farm inside its bund wall, a gabled generator hall with its transformer, an ash silo on legs, a turbine of three casings and a generator on one shaft, and a condenser bank with six fan stacks. The selected unit lifts off its pad.",
  body: [hero.body],
});

// Three camera views of the same plant, as in the console's thumbnail strip.
const VIEWS = [
  { fig: "Fig 2", title: "ISO · NE", azimuth: 35, elevation: 28 },
  { fig: "Fig 3", title: "Elevation · S", azimuth: 90, elevation: 6 },
  { fig: "Fig 4", title: "Plan", azimuth: 90, elevation: 86 },
];
const thumbs = VIEWS.map((view) => {
  const PV = camera(300, 220, view.azimuth, view.elevation, 16);
  const { body } = scene(PV);
  const svg = k.figureSvg({ width: 300, height: 220, label: `The same plant seen from yaw ${view.azimuth}°, elevation ${view.elevation}°.`, body: [body] });
  return k.plateHtml({ fig: view.fig, title: view.title, hint: `yaw ${view.azimuth}°`, readout: `el ${view.elevation}°`, body: `<div class="iso-stage pc-view">${svg}</div>` });
});

const live = `
const P = ${JSON.stringify(P)};
const UNITS = ${JSON.stringify(UNITS.map(({ code, name, uptime }) => ({ code, name, uptime })))};
const HULLS = ${JSON.stringify(hulls)};
const DEPTHS = ${JSON.stringify(depths)};
const TOUR = ${JSON.stringify(TOUR)}, ALERT = ${ALERT}, LIFT = 10;
const stage = document.querySelector(".pc-hero");
const svg = stage.querySelector("svg");
const readout = document.querySelector("[data-readout]");
const still = matchMedia("(prefers-reduced-motion: reduce)");
const groups = UNITS.map((_, u) => [...svg.querySelectorAll('[data-unit="' + u + '"]')]);
const halos = UNITS.map((_, u) => svg.querySelector('[data-halo="' + u + '"]'));
const fans = [...svg.querySelectorAll("[data-fan]")].map((el) => ({ el, at: el.dataset.fan.split(",").map(Number) }));
const blades = ${blades.toString().replace("k.lineOnTop", "lineOnTop")};
const inside = (poly, [x, y]) => { let s = 0; for (let i = 0; i < poly.length; i++) { const a = poly[i], b = poly[(i + 1) % poly.length]; const c = Math.sign((b[0] - a[0]) * (y - a[1]) - (b[1] - a[1]) * (x - a[0])); if (c && s && c !== s) return false; if (c) s = c; } return true; };
const lift = UNITS.map(() => 0);
let picked = ALERT, touring = !still.matches, clock = 0, spin = 0, last = 0, frame = 0, visible = false, idle = 0;
function pick(u) {
  if (u === picked) return;
  picked = u;
  document.querySelectorAll(".u-main").forEach((el) => el.toggleAttribute("data-lit", el.closest("[data-unit]").dataset.unit === String(u)));
  const unit = UNITS[u];
  readout.textContent = unit.code.toLowerCase() + " · " + unit.name.toLowerCase() + " · up " + unit.uptime;
  stage.setAttribute("aria-valuenow", String(u));
  stage.setAttribute("aria-valuetext", unit.code + ", " + unit.name);
}
function tick(now) {
  const dt = last ? Math.min((now - last) / 1000, 1 / 30) : 1 / 60;
  last = now;
  const calm = still.matches;
  if (touring) { clock += dt; pick(TOUR[Math.floor(clock / 2.6) % TOUR.length]); }
  let moving = touring;
  UNITS.forEach((_, u) => {
    const want = u === picked ? LIFT : 0;
    lift[u] = calm ? want : lift[u] + (want - lift[u]) * (1 - Math.exp(-dt / 0.28));
    if (Math.abs(want - lift[u]) < 0.02) lift[u] = want; else moving = true;
    const t = translateAlong(P, [0, 0, lift[u]]);
    for (const g of groups[u]) g.setAttribute("transform", t);
    halos[u].style.opacity = String((lift[u] / LIFT) * 0.8);
  });
  if (!calm) { spin += dt * 1.6; for (const f of fans) f.el.setAttribute("d", blades(f.at, spin, P)); moving = true; }
  frame = visible && moving ? requestAnimationFrame(tick) : 0;
}
const run = () => { if (!frame && visible) { last = 0; frame = requestAnimationFrame(tick); } };
const steer = (u) => { touring = false; clearTimeout(idle); idle = setTimeout(() => { touring = !still.matches; clock = 0; run(); }, 3600); if (u >= 0) pick(u); run(); };
new IntersectionObserver((entries) => { visible = entries[entries.length - 1].isIntersecting; if (visible) run(); }, { rootMargin: "120px 0px" }).observe(stage);
stage.addEventListener("pointermove", (event) => {
  const m = svg.getScreenCTM().inverse(), p = [m.a * event.clientX + m.c * event.clientY + m.e, m.b * event.clientX + m.d * event.clientY + m.f];
  let best = -1;
  HULLS.forEach((hull, u) => { if (inside(hull, p) && (best < 0 || DEPTHS[u] > DEPTHS[best])) best = u; });
  steer(best);
});
stage.addEventListener("keydown", (event) => {
  const here = TOUR.indexOf(picked);
  let to = null;
  if (event.key === "ArrowRight" || event.key === "ArrowDown") to = TOUR[(here + 1) % TOUR.length];
  if (event.key === "ArrowLeft" || event.key === "ArrowUp") to = TOUR[(here + TOUR.length - 1) % TOUR.length];
  if (to === null) return;
  event.preventDefault();
  steer(to);
});
picked = -1;
pick(ALERT);
run();
`;

const heroPlate = k.plateHtml({
  fig: "Fig 1",
  title: "PAC plant 02",
  hint: "Hover a unit to lift it",
  readout: "cnd-05 · condenser bank · up 96 d",
  keys: [
    { mark: "lit", label: "The selected unit, lifted off its pad" },
    { mark: "raised", label: "Six units: stacks, tank farm, generator hall, turbine, condenser bank, ash silo" },
  ],
  caption:
    "Plant 02 as the console shows it: six units on one base plate. CND-05, the condenser bank, is the unit in alert and the tour starts there. Hover or use the arrow keys to pick a unit; the readout gives its code, name and uptime from the console's unit catalog.",
  body: `<div class="iso-stage pc-hero" tabindex="0" role="slider" aria-label="Plant units: arrow keys pick a unit" aria-valuemin="0" aria-valuemax="5" aria-valuenow="${ALERT}">${heroSvg}</div>`,
});

const css = `<style>.pc-views{display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-top:24px}.pc-views .iso-figure{margin:0}@media (max-width:720px){.pc-views{grid-template-columns:1fr}}</style>`;
const page = k.pageHtml({ title: "PAC plant 02", theme, width: 960, body: `${css}${heroPlate}<div class="pc-views">${thumbs.join("")}</div>`, script: kitScript() + live });
const out = theme === "light" ? "plant-light.html" : "plant.html";
writeFileSync(join(HERE, out), page);
console.log("wrote", out, "·", hero.R.items.length, "items,", hero.R.solids.length, "solids");
