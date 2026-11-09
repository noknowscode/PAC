// node render.js <html> <out.png> <W> <H> <scale>  : renders a composition at an exact CSS size and device scale
const { chromium } = require('playwright');
(async () => {
  const jobs = [];
  const a = process.argv.slice(2);
  for (let i = 0; i < a.length; i += 5) jobs.push(a.slice(i, i + 5));
  const b = await chromium.launch();
  for (const [html, out, W, H, s] of jobs) {
    const ctx = await b.newContext({ viewport: { width: +W, height: +H }, deviceScaleFactor: +s });
    const p = await ctx.newPage();
    await p.goto('file://' + require('path').resolve(html));
    await p.evaluate(() => document.fonts.ready);
    await p.waitForFunction(() => [...document.images].every(i => i.complete && i.naturalWidth > 0), null, { timeout: 30000 });
    await p.waitForTimeout(300);
    await p.screenshot({ path: out, clip: { x: 0, y: 0, width: +W, height: +H } });
    await ctx.close();
    console.log('rendered', out);
  }
  await b.close();
})();
