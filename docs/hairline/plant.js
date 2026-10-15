/**
 * Plant: PAC's six units on one rounded slab, each drawn by what gives it
 * away: two tapered stacks with lips, a bunded pair of domed tanks, a coned
 * silo on legs over its hopper, a hall with a pitched roof and a ridge vent,
 * a turbine of three stepped casings, and an air-cooled condenser deck on legs
 * with six fan shrouds. The unit under the pointer lifts and takes the bright
 * edge; the others part away from it, nearest most, staggered by grid
 * distance. At rest the condenser (the live alert) is lifted a little, its
 * neighbours slightly parted. The slider is the parting distance.
 *
 * The pattern: discrete items, like Riffle. Tweens, a stagger by distance, and
 * a hit test on each unit's home silhouette, which never moves.
 */
const {
  Cam, fit, proj, facing, rrect, prism, ringAt, run, hull, poly, open,
  tween, tset, tval, tdone, mk, solid, put, register, pointer, disposer,
} = HL;

const CELL = 50, LIFT = 12, REST_LIFT = 5, REST_PART = 0.35, STEP = 45, ALERT = 5;
const SX0 = -8, SY0 = -8, SX1 = 158, SY1 = 108, SH = 5;

const R = (x0, y0, x1, y1, r, n = 6) => rrect(x0, y0, x1, y1, r, n);
const ins = (x0, y0, x1, y1, r, b) => R(x0 + b, y0 + b, x1 - b, y1 - b, Math.max(0.5, r - b));
/** An upright rounded solid. */
const box = (x0, y0, x1, y1, r, z0, z1, b = 1.2) => ({ foot: R(x0, y0, x1, y1, r), inner: ins(x0, y0, x1, y1, r, b), z0, z1 });
const cyl = (cx, cy, a, z0, z1, b = 1) => box(cx - a, cy - a, cx + a, cy + a, a, z0, z1, b);
/** A solid whose top is not its foot: a roof, a cone, a hopper. `top` is [x0, y0, x1, y1, r]. */
const taper = (foot, top, z0, z1, b = 1) => ({ foot, top: R(...top), inner: ins(...top, b), z0, z1 });
const disc = (cx, cy, a) => [cx - a, cy - a, cx + a, cy + a, a];

/** Back to front within each unit: lower and farther parts first. */
const UNITS = [
  { code: "stk 01", i: 0, j: 0, parts: (x, y) => [
    box(x - 12, y - 13, x + 11, y + 12, 6, 0, 4),
    taper(R(...disc(x + 4, y - 6, 5.5)), disc(x + 4, y - 6, 3.6), 4, 66), cyl(x + 4, y - 6, 4.3, 64, 70),
    taper(R(...disc(x - 5, y + 5, 5.5)), disc(x - 5, y + 5, 3.8), 4, 52), cyl(x - 5, y + 5, 4.5, 50, 55),
  ] },
  { code: "tka 02", i: 1, j: 0, parts: (x, y) => [
    box(x - 18, y - 18, x + 18, y + 18, 7, 0, 3),
    cyl(x + 6, y - 7, 8.5, 3, 18), taper(R(...disc(x + 6, y - 7, 8.5)), disc(x + 6, y - 7, 4.5), 18, 23),
    cyl(x - 7, y + 6, 8.5, 3, 18), taper(R(...disc(x - 7, y + 6, 8.5)), disc(x - 7, y + 6, 4.5), 18, 23),
  ] },
  { code: "sil 06", i: 0, j: 1, parts: (x, y) => [
    cyl(x - 6, y - 6, 1.3, 0, 12, 0.4),
    taper(R(...disc(x, y, 3)), disc(x, y, 10), 8, 16),
    ...[[6, -6], [-6, 6], [6, 6]].map(([u, v]) => cyl(x + u, y + v, 1.3, 0, 12, 0.4)),
    cyl(x, y, 10, 16, 44),
    taper(R(...disc(x, y, 10)), disc(x, y, 2.5), 44, 50),
  ] },
  { code: "gen 03", i: 2, j: 0, parts: (x, y) => [
    box(x - 17, y - 12, x + 17, y + 12, 3, 0, 18),
    taper(R(x - 17, y - 12, x + 17, y + 12, 3), [x - 17, y - 3, x + 17, y + 3, 2], 18, 26),
    box(x - 4, y - 2.5, x + 4, y + 2.5, 1.5, 26, 30, 0.8),
  ] },
  { code: "trb 04", i: 1, j: 1, parts: (x, y) => [
    box(x - 18, y - 12, x + 18, y + 12, 4, 0, 5),
    taper(R(x - 16, y - 4.5, x - 9.5, y + 4.5, 2.5), [x - 16, y - 2.5, x - 9.5, y + 2.5, 1.5], 5, 11),
    taper(R(x - 7.5, y - 6.5, x, y + 6.5, 3), [x - 7.5, y - 3.5, x, y + 3.5, 2], 5, 15),
    taper(R(x + 2, y - 9, x + 16, y + 9, 4), [x + 2, y - 5, x + 16, y + 5, 3], 5, 20),
  ] },
  { code: "cnd 05", i: 2, j: 1, parts: (x, y) => [
    ...[[-15, -11], [15, -11], [-15, 11], [15, 11]].map(([u, v]) => cyl(x + u, y + v, 1.4, 0, 10, 0.4)),
    box(x - 19, y - 14, x + 19, y + 14, 3, 10, 15),
    ...[[-11, -6], [-11, 6], [0, -6], [0, 6], [11, -6], [11, 6]]
      .sort((p, q) => p[0] + p[1] - q[0] - q[1])
      .map(([u, v]) => cyl(x + u, y + v, 4.2, 15, 18)),
  ] },
];

/** True when the screen point lies inside the convex screen polygon. */
function inside(pts, [x, y]) {
  let s = 0;
  for (let n = 0; n < pts.length; n++) {
    const a = pts[n], b = pts[(n + 1) % pts.length];
    const c = Math.sign((b[0] - a[0]) * (y - a[1]) - (b[1] - a[1]) * (x - a[0]));
    if (c && s && c !== s) return false;
    if (c) s = c;
  }
  return true;
}

function mount({ stage, svg, read }, value) {
  const bag = disposer();
  let gap = value, act = -1;

  // Fitted with the far stack lifted and the slab grown by the widest parting, so no pose leaves the frame.
  const C = Cam(45, 0.5, 1.42), M = 16;
  fit(C, [[SX0 - M, SY0 - M, -SH], [SX1 + M, SY1 + M, -SH], [SX1 + M, SY0 - M, -SH], [SX0 - M, SY1 + M, -SH], [29, 19, 70 + LIFT]], 200, 166);
  const P = proj(C), front = facing(C);
  const shape = (Q, p) => p.top
    ? { sil: poly(hull(ringAt(Q, p.foot, p.z0).concat(ringAt(Q, p.top, p.z1)))), crease: open(ringAt(Q, run(p.inner, front), p.z1)) }
    : prism(Q, front, p.foot, p.inner, p.z0, p.z1);

  const g = mk("g", {}, svg);
  const slab = box(SX0, SY0, SX1, SY1, 12, -SH, 0, 2);
  put(solid(g), prism(P, front, slab.foot, slab.inner, -SH, 0));

  const units = UNITS.map((u) => {
    const cx = (u.i + 0.5) * CELL, cy = (u.j + 0.5) * CELL;
    return { ...u, cx, cy, parts: u.parts(cx, cy) };
  });
  // Each unit's pad, dashed on the slab before any unit: covered at home, the trace of where a moved unit stands.
  for (const u of units) mk("path", { d: poly(ringAt(P, R(u.cx - 20, u.cy - 20, u.cx + 20, u.cy + 20, 6), 0)), class: "dash nf" }, g);

  /** Where every unit goes when unit `a` is chosen (-1: rest, which leans on the alert). */
  function goal(a) {
    const f = a < 0 ? ALERT : a, c = units[f], part = a < 0 ? gap * REST_PART : gap;
    return units.map((u, k) => {
      const dx = u.cx - c.cx, dy = u.cy - c.cy, d = Math.hypot(dx, dy), s = d ? (part * Math.min(1, CELL / d)) / d : 0;
      return [dx * s, dy * s, k === f ? (a < 0 ? REST_LIFT : LIFT) : 0];
    });
  }
  const rest = goal(-1);
  units.forEach((u, k) => {
    u.g = mk("g", {}, g);
    u.solids = u.parts.map(() => solid(u.g));
    u.tw = rest[k].map((v) => tween(v));
    u.drawn = "";
    u.solids.forEach((s) => s.sil.classList.toggle("hi", k === ALERT));
  });

  // Hit areas: each unit's silhouette at home, which never moves (rule 01).
  const home = units.map((u) => hull(u.parts.flatMap((p) => ringAt(P, p.foot, p.z0).concat(ringAt(P, p.top || p.foot, p.z1)))));
  const mid = units.map((u) => P(u.cx, u.cy, 10)), slabTop = ringAt(P, slab.foot, 0);
  function hit(p) {
    for (let k = units.length - 1; k >= 0; k--) if (inside(home[k], p)) return k;
    if (!inside(slabTop, p)) return -1;
    let best = 0;
    mid.forEach((m, k) => { if (Math.hypot(m[0] - p[0], m[1] - p[1]) < Math.hypot(mid[best][0] - p[0], mid[best][1] - p[1])) best = k; });
    return best;
  }

  function drawUnit(u, now) {
    const [ox, oy, oz] = u.tw.map((t) => tval(t, now)), key = `${ox.toFixed(2)},${oy.toFixed(2)},${oz.toFixed(2)}`;
    if (key === u.drawn) return;
    u.drawn = key;
    const Q = (x, y, z) => P(x + ox, y + oy, z + oz);
    u.parts.forEach((p, n) => put(u.solids[n], shape(Q, p)));
  }

  const B = register(stage, (_dt, now) => {
    let moving = false;
    for (const u of units) { drawUnit(u, now); if (u.tw.some((t) => !tdone(t, now))) moving = true; }
    return moving;
  });
  bag.add(B.unregister);

  /** Chooses unit a (-1 for rest). The stagger spreads out from the unit chosen, or the one let go. */
  function aim(a, force) {
    if (a === act && !force) return;
    const now = performance.now(), from = units[a >= 0 ? a : act >= 0 ? act : ALERT], to = goal(a), f = a < 0 ? ALERT : a;
    act = a;
    units.forEach((u, k) => {
      const delay = Math.hypot(u.i - from.i, u.j - from.j) * STEP;
      u.tw.forEach((t, n) => tset(t, to[k][n], now, delay));
      u.solids.forEach((s) => s.sil.classList.toggle("hi", k === f));
    });
    read.textContent = a < 0 ? "rest" : units[a].code;
    B.wake();
  }

  read.textContent = "rest";
  bag.add(pointer(stage, { move: (p) => aim(hit(p)), leave: () => aim(-1) }));
  bag.add(() => svg.replaceChildren());

  return {
    set: (v) => { gap = v; aim(act, true); },
    destroy: bag.dispose,
  };
}

hairline({
  name: "plant",
  means: "A thermal plant on its slab: the unit under the pointer lifts, and the others part away from it.",
  rules: [1, 2, 5, 9],
  range: [3, 8, 14],
  mount,
});
