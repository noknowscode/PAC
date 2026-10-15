// Exports the TKA-02 parts of the plant figure, as the kit draws them at the hero camera, for the
// C# port's cross-check test (PacConsole.Tests/Fixtures/tka-reference.json).
// node export-tka.mjs
import { writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const SKILL = join(homedir(), ".claude", "skills", "anatomy");
const k = await import(pathToFileURL(join(SKILL, "kit/iso-kit.mjs")).href);
const G = await import(pathToFileURL(join(SKILL, "kit/lathe.mjs")).href);
const HERE = dirname(fileURLToPath(import.meta.url));

// Same camera as build.mjs's hero.
const PLATE = { x: -160, y: -110, w: 320, d: 220, r: 10 };
const P = k.fitProjection([...k.boxCorners(PLATE, -16, 0), [-107, -48, 104], [-95, 48, 77]], 640, 420, { pad: 30, azimuth: 45, elevation: 30 });

const UP = [0, 0, 1], X = [1, 0, 0], Y = [0, 1, 0];
const PAD_H = 1.2, cx = 0, cy = -48;
const parts = [];
const keep = (name, args, paths) => parts.push({ name, args, fill: paths.fill, outline: paths.outline, crease: paths.crease, top: paths.top, shades: paths.shades, bevel: paths.bevel ?? "" });
const slab = (name, plan, z, h, steps, bevel) => keep(name, { kind: "slab", plan, z, h, steps, bevel }, k.slabOf(plan, z, h, P, steps, bevel));
const post = (name, x, y, r, z, h, steps, bevel) => keep(name, { kind: "cylinder", x, y, r, z, h, steps, bevel }, k.cylinder(x, y, r, z, h, P, steps, bevel));

slab("pad", { x: cx - 36, y: cy - 36, w: 72, d: 72, r: 4 }, 0, PAD_H, 4, 0.4);
const t = 2.4, h = 6;
for (const plan of [
  { x: cx - 32, y: cy - 32, w: 64, d: t, r: 0.6 },
  { x: cx - 32, y: cy - 32 + t, w: t, d: 64 - 2 * t, r: 0.6 },
  { x: cx + 32 - t, y: cy - 32 + t, w: t, d: 64 - 2 * t, r: 0.6 },
  { x: cx - 32, y: cy + 32 - t, w: 64, d: t, r: 0.6 },
]) slab("bund", plan, PAD_H, h, 2, 0.3);
for (const [x, y] of [[cx + 12, cy - 10], [cx - 12, cy + 10]]) {
  post("tank", x, y, 13, PAD_H, 30 - PAD_H, 56, 0.6);
  const F = G.frameOf([x, y, 0], UP, X, Y);
  keep("roof", { kind: "lathe", o: F.o, a: F.a, u: F.u, v: F.v, profile: [[30, 13], [37, 2.5]] }, G.solidOf([[30, 13], [37, 2.5]], F, P));
  post("vent", x, y, 1.6, 37, 3, 28, 0);
  post("cap", x, y, 2.4, 40, 1.2, 28, 0);
}
const skidY = cy + 16;
const FP = G.frameOf([0, skidY + 4.5, PAD_H + 5], X, Y, UP);
keep("motor", { kind: "lathe", o: FP.o, a: FP.a, u: FP.u, v: FP.v, profile: [[cx + 7.5, 0], [cx + 7.5, 3], [cx + 15, 3], [cx + 15, 0]], raw: true }, G.disc(cx + 7.5, cx + 15, 3, FP, P));

const out = join(HERE, "..", "..", "..", "PacConsole.Tests", "Fixtures", "tka-reference.json");
writeFileSync(out, JSON.stringify({ projection: P, parts }, null, 1));
console.log("wrote", out, parts.length, "parts", JSON.stringify(P));
