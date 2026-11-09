// The PAC capture driver: one browser session kept open, driven step by step over a local control port, so every
// capture comes from one running app (one plant clock, one set of data) whatever the viewport or theme.
//
//   node drive.js <url> <outdir> <WxH> <dpr> <scheme> <controlPort> [timezone]
//   curl -s localhost:<controlPort> -d '[{"click":[x,y]},{"wait":1500},{"shot":"name","clip":{...}}]'
//
// Steps (run in order; every step is appended to <outdir>/steps.jsonl, the record of the session in tools/sessions/):
//   {click:[x,y]}            mouse press and release at CSS px    {move:[x,y]}           park the pointer
//   {tap:[x,y]}              a touch tap (no hover state, no mouse scroll indicator left behind)
//   {swipe:[x1,y1,x2,y2]}    a touch pan, held still before release so it scrolls without a fling
//   {drag:[x1,y1,x2,y2]}     press, move in 12 steps, release     {wheel:[dx,dy], at:[x,y]}
//   {key:"Escape"}           keyboard                             {wait:ms}
//   {scheme:"light"|"dark"}  prefers-color-scheme (the app follows it while its theme is System)
//   {viewport:[w,h]}         resize the window (same app instance, same plant clock)
//   {shot:"name", clip:{x,y,width,height}}   screenshot at the context's device pixel ratio
const { chromium } = require('playwright');
const fs = require('fs');
const http = require('http');

(async () => {
  const [url, out, size, dpr, scheme, port, tz] = process.argv.slice(2);
  const [w, h] = size.split('x').map(Number);
  fs.mkdirSync(out, { recursive: true });
  const log = fs.createWriteStream(`${out}/steps.jsonl`, { flags: 'a' });
  const b = await chromium.launch({ args: ['--hide-scrollbars'] });
  const ctx = await b.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: Number(dpr), colorScheme: scheme,
    timezoneId: tz || 'UTC', hasTouch: true });
  const p = await ctx.newPage();
  const errors = [];
  p.on('pageerror', e => errors.push(String(e)));
  p.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  const t0 = Date.now();
  await p.goto(url, { waitUntil: 'load', timeout: 300000 });
  log.write(JSON.stringify({ open: url, size, dpr: Number(dpr), scheme, tz: tz || 'UTC', at: new Date().toISOString() }) + '\n');
  console.log('loaded after', Math.round((Date.now() - t0) / 1000), 's');
  await p.mouse.move(w - 4, h - 4);

  async function run(steps) {
    const res = [];
    for (const s of steps) {
      log.write(JSON.stringify({ ...s, at: new Date().toISOString() }) + '\n');
      if (s.viewport) await p.setViewportSize({ width: s.viewport[0], height: s.viewport[1] });
      if (s.scheme) await p.emulateMedia({ colorScheme: s.scheme });
      if (s.click) { await p.mouse.move(...s.click); await p.waitForTimeout(150); await p.mouse.down(); await p.waitForTimeout(90); await p.mouse.up(); }
      if (s.tap) await p.touchscreen.tap(...s.tap);
      if (s.swipe) {
        const [x1, y1, x2, y2] = s.swipe, n = 16;
        const cdp = await ctx.newCDPSession(p);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: x1, y: y1 }] });
        for (let i = 1; i <= n; i++) {
          await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: x1 + (x2 - x1) * i / n, y: y1 + (y2 - y1) * i / n }] });
          await p.waitForTimeout(25);
        }
        await p.waitForTimeout(120);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        await cdp.detach();
      }
      if (s.drag) {
        const [x1, y1, x2, y2] = s.drag;
        await p.mouse.move(x1, y1); await p.mouse.down();
        for (let i = 1; i <= 12; i++) { await p.mouse.move(x1 + (x2 - x1) * i / 12, y1 + (y2 - y1) * i / 12); await p.waitForTimeout(30); }
        await p.mouse.up();
      }
      if (s.move) await p.mouse.move(...s.move);
      if (s.wheel) { await p.mouse.move(...(s.at || [w / 2, h / 2])); await p.mouse.wheel(...s.wheel); }
      if (s.key) await p.keyboard.press(s.key);
      if (s.wait) await p.waitForTimeout(s.wait);
      if (s.shot) {
        const t = Date.now();
        await p.screenshot({ path: `${out}/${s.shot}.png`, clip: s.clip, animations: 'disabled', timeout: 240000 });
        res.push(`shot ${s.shot} ${Date.now() - t}ms`);
      }
    }
    if (errors.length) { res.push('errors: ' + errors.slice(0, 3).join(' | ')); errors.length = 0; }
    return res;
  }

  http.createServer((req, resp) => {
    let body = '';
    req.on('data', c => body += c);
    req.on('end', async () => {
      try {
        if (body.trim() === 'quit') { resp.end('bye\n'); await b.close(); process.exit(0); }
        const r = await run(JSON.parse(body));
        resp.end(r.join('\n') + '\nok\n');
      } catch (e) { resp.statusCode = 500; resp.end(String(e) + '\n'); }
    });
  }).listen(Number(port), '127.0.0.1', () => console.log('control on', port));
})();
