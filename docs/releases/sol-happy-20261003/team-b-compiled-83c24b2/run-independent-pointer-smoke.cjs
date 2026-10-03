const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { chromium } = require('/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const bundle = fs.realpathSync(process.argv[2]);
const output = path.resolve(process.argv[3]);
const commit = process.argv[4];
const port = 18764;
fs.mkdirSync(output, { recursive: true });
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
assert(/^[0-9a-f]{40}$/.test(commit), 'Exact source commit required');
assert(fs.existsSync(path.join(bundle, 'index.html')) && fs.existsSync(path.join(bundle, '_framework/dotnet.js')));
const evidence = { reviewer: 'B6 independent validation', commit, bundle, port, runnerSha256: hash(fs.readFileSync(__filename)), claims: 'Actual published anonymous CUI Home bootstrap only; no hosted provider or app parity acceptance.', productParityVerified: false, assertions: [], observations: [], consoleCounts: {}, consoleSamples: [], pageErrorCount: 0, managedRuntimeErrorCount: 0, pageErrors: [], failedRequests: [], httpErrors: [], serverRequests: [], visibleContentReview: 'PENDING_B6_SCREENSHOT_INSPECTION' };
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const server = http.createServer((req, res) => {
  let file;
  try { const url = new URL(req.url, `http://127.0.0.1:${port}`); file = path.resolve(bundle, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname)); }
  catch { res.writeHead(400); return res.end(); }
  const status = !file.startsWith(bundle + path.sep) || !['GET', 'HEAD'].includes(req.method) ? 403 : !fs.existsSync(file) || !fs.statSync(file).isFile() ? 404 : 200;
  evidence.serverRequests.push({ path: req.url, status });
  res.writeHead(status, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  if (status !== 200 || req.method === 'HEAD') return res.end();
  fs.createReadStream(file).pipe(res);
});
function check(id, passed, observed) { evidence.assertions.push({ id, passed, observed }); }
(async () => {
  let context;
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });
  try {
    const profile = fs.mkdtempSync('/tmp/b6-actual-cui-'); evidence.freshProfile = profile;
    context = await chromium.launchPersistentContext(profile, { executablePath: '/usr/bin/chromium', headless: true, viewport: { width: 1440, height: 1000 }, args: ['--no-sandbox', '--disable-dev-shm-usage', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
    evidence.browserVersion = context.browser().version();
    await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
    const page = context.pages()[0];
    page.on('console', m => { const text = m.text(); const key = `${m.type()}:${text.slice(0, 200)}`; evidence.consoleCounts[key] = (evidence.consoleCounts[key] || 0) + 1; if (m.type() === 'error' && /MONO_WASM|9to1 browser startup failed|runtime.*exited/i.test(text)) evidence.managedRuntimeErrorCount++; if (evidence.consoleSamples.length < 100) evidence.consoleSamples.push({ type: m.type(), text: text.slice(0, 4000), textSha256: hash(text), originalLength: text.length }); });
    page.on('pageerror', e => { evidence.pageErrorCount++; if (evidence.pageErrors.length < 100) evidence.pageErrors.push(e.message.slice(0, 4000)); });
    page.on('requestfailed', r => evidence.failedRequests.push({ url: r.url(), failure: r.failure() }));
    page.on('response', r => { if (r.status() >= 400) evidence.httpErrors.push({ url: r.url(), status: r.status() }); });
    async function snapshot(id) {
      const state = await page.evaluate(() => ({ hash: location.hash, status: document.querySelector('#browser-status')?.dataset.code, message: document.querySelector('#browser-status')?.textContent, canvases: [...document.querySelectorAll('canvas')].map(c => ({ width: c.width, height: c.height })), body: document.body.innerHTML }));
      state.aria = await page.locator('body').ariaSnapshot();
      fs.writeFileSync(path.join(output, id + '.html'), state.body);
      await page.screenshot({ path: path.join(output, id + '.png'), fullPage: true });
      evidence.observations.push({ id, ...state }); console.log(id, state.status, state.hash); return state;
    }
    async function awaitStatus(code) { await page.waitForFunction(code => document.querySelector('#browser-status')?.dataset.code === code, code, { timeout: 45000 }); await page.waitForTimeout(500); }
    await page.goto(`http://127.0.0.1:${port}/`, { waitUntil: 'domcontentloaded' });
    await awaitStatus('HomeServiceUnavailable');
    const start = await snapshot('cold-home'); check('cold-home-startup', start.status === 'HomeServiceUnavailable' && start.canvases.some(c => c.width > 0 && c.height > 0), { status: start.status, canvasCount: start.canvases.length, limitation: 'Visible canonical content requires separate screenshot inspection.' });
    // Actual pointer input targets coordinates visibly established by B6 screenshots at this viewport.
    await page.mouse.click(447, 62); await page.waitForTimeout(700);
    const disabled = await snapshot('pointer-disabled-open-studio');
    check('disabled-open-studio-does-not-navigate', disabled.hash === '' && disabled.status === 'HomeServiceUnavailable', { hash: disabled.hash, status: disabled.status, coordinates: [447, 62] });
    await page.mouse.click(70, 192); await page.waitForFunction(() => location.hash === '#/home.library', undefined, { timeout: 10000 }); await awaitStatus('HomeServiceUnavailable');
    const library = await snapshot('pointer-enabled-library');
    check('enabled-library-pointer-dispatch', library.hash === '#/home.library' && library.status === 'HomeServiceUnavailable', { hash: library.hash, status: library.status, coordinates: [70, 192], visibleContent: 'Pending screenshot inspection' });
    await page.mouse.click(70, 222); await page.waitForFunction(() => location.hash === '#/home.events', undefined, { timeout: 10000 }); await awaitStatus('HomeServiceUnavailable');
    const events = await snapshot('pointer-enabled-events');
    check('enabled-events-pointer-dispatch', events.hash === '#/home.events' && events.status === 'HomeServiceUnavailable', { hash: events.hash, status: events.status, coordinates: [70, 222], visibleContent: 'Pending screenshot inspection' });
    check('no-managed-or-page-runtime-errors', evidence.managedRuntimeErrorCount === 0 && evidence.pageErrorCount === 0 && evidence.failedRequests.length === 0, { managed: evidence.managedRuntimeErrorCount, pageErrors: evidence.pageErrorCount, failedRequests: evidence.failedRequests.length });
  } catch (e) { evidence.runnerOrRuntimeFailure = { message: e.message, stack: e.stack }; check('runtime-smoke-completed', false, e.message); }
  finally {
    if (context) { try { await context.tracing.stop({ path: path.join(output, 'independent-production-trace.zip') }); } catch (e) { evidence.traceFailure = e.message; } await context.close(); }
    await new Promise(resolve => server.close(resolve));
    evidence.counts = { executed: evidence.assertions.length, passed: evidence.assertions.filter(x => x.passed).length, failed: evidence.assertions.filter(x => !x.passed).length };
    fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(evidence, null, 2));
    if (!evidence.assertions.length || evidence.counts.failed) process.exitCode = 1;
  }
})().catch(e => { console.error(e); process.exitCode = 1; server.close(); });
