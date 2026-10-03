const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const http = require('node:http');
const { chromium } = require('/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const bundle = fs.realpathSync(process.argv[2]);
const output = path.resolve(process.argv[3]);
const commit = process.argv[4];
const port = 18764;
if (!/^[0-9a-f]{40}$/.test(commit)) throw new Error('Exact immutable source commit required');
fs.mkdirSync(output, { recursive: true });
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const cases = [ 'AX-Home-button', 'AX-Library-button', 'AX-Events-button', 'AX-labelled-disabled-search', 'AX-disabled-service-actions', 'KEYBOARD-tab-library', 'KEYBOARD-enter-library', 'KEYBOARD-space-events' ];
const report = { reviewer: 'B6 independent validation', commit, bundle, port, runnerSha256: hash(fs.readFileSync(__filename)), scope: 'Actual CUI browser AX and keyboard behavior, no mirrored test controls or injected module/service replacements. Anonymous service-unavailable state only.', requiredCases: cases, outcomes: [], observations: [], errors: [], consoleCounts: {}, productParityVerified: false };
const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css', '.woff2': 'font/woff2' };
const server = http.createServer((req, res) => {
  let file;
  try { const url = new URL(req.url, `http://127.0.0.1:${port}`); file = path.resolve(bundle, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname)); }
  catch { res.writeHead(400); return res.end(); }
  const status = !file.startsWith(bundle + path.sep) || !['GET','HEAD'].includes(req.method) ? 403 : !fs.existsSync(file) || !fs.statSync(file).isFile() ? 404 : 200;
  res.writeHead(status, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  if (status !== 200 || req.method === 'HEAD') return res.end();
  fs.createReadStream(file).pipe(res);
});
const outcome = (id, passed, observed) => report.outcomes.push({ id, outcome: passed ? 'PASS' : 'FAIL', observed });
(async () => {
  let context;
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });
  try {
    report.freshProfile = fs.mkdtempSync('/tmp/b6-home-ax-');
    context = await chromium.launchPersistentContext(report.freshProfile, { executablePath: '/usr/bin/chromium', headless: true, viewport: { width: 1440, height: 1000 }, args: ['--no-sandbox', '--disable-dev-shm-usage', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
    report.browserVersion = context.browser().version();
    await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
    const page = context.pages()[0];
    page.on('pageerror', e => report.errors.push(e.message));
    page.on('console', m => { const key = `${m.type()}: ${m.text().slice(0,200)}`; report.consoleCounts[key] = (report.consoleCounts[key] || 0) + 1; });
    await page.goto(`http://127.0.0.1:${port}/`, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => document.querySelector('#browser-status')?.dataset.code === 'HomeServiceUnavailable', undefined, { timeout: 45000 });
    const cdp = await context.newCDPSession(page); await cdp.send('Accessibility.enable');
    const tree = async () => (await cdp.send('Accessibility.getFullAXTree')).nodes.filter(n => !n.ignored);
    const named = (nodes, role, name) => nodes.filter(n => n.role?.value === role && n.name?.value === name);
    const disabled = node => node.properties?.some(p => p.name === 'disabled' && p.value?.value === true);
    const nodes = await tree();
    fs.writeFileSync(path.join(output,'native-ax-tree.json'), JSON.stringify(nodes,null,2));
    report.observations.push({ id: 'initial', nativeAXNames: nodes.map(n => ({ role: n.role?.value, name: n.name?.value })), aria: await page.locator('body').ariaSnapshot() });
    await page.screenshot({ path: path.join(output,'home.png'), fullPage: true });
    for (const name of ['Home','Library','Events']) { const matches = named(nodes,'button',name); outcome(`AX-${name}-button`, matches.length === 1 && !disabled(matches[0]), { matches: matches.length, disabled: matches[0] ? disabled(matches[0]) : null }); }
    const search = named(nodes,'textbox','Search 9-1'); outcome('AX-labelled-disabled-search', search.length === 1 && disabled(search[0]), { matches: search.length, anonymousServiceState: 'Unavailable', disabled: search[0] ? disabled(search[0]) : null });
    const actions = ['Open Studio','Install apps','Apps'].map(name => ({ name, nodes: named(nodes,'button',name) })); outcome('AX-disabled-service-actions', actions.every(x => x.nodes.length === 1 && disabled(x.nodes[0])), actions.map(x => ({ name: x.name, matches: x.nodes.length, disabled: x.nodes[0] ? disabled(x.nodes[0]) : null })));
    if (report.outcomes.some(x => x.outcome !== 'PASS')) {
      for (const id of cases.slice(5)) report.outcomes.push({ id, outcome: 'NOT_RUN', blockedBy: 'Required actual accessible controls/disabled semantics absent; keyboard cannot be targeted through a fabricated replacement.' });
    } else {
      // Real Tab sequence: no scripted focus, click or programmatic owner action.
      let focusedLibrary = false; const focusSequence = [];
      for (let i = 0; i < 40; i++) {
        await page.keyboard.press('Tab');
        const current = await tree(); const focused = current.find(n => n.properties?.some(p => p.name === 'focused' && p.value?.value === true));
        focusSequence.push({ role: focused?.role?.value, name: focused?.name?.value });
        if (focused?.role?.value === 'button' && focused?.name?.value === 'Library') { focusedLibrary = true; break; }
      }
      outcome('KEYBOARD-tab-library', focusedLibrary && !focusSequence.some(x => ['Open Studio','Install apps','Apps','Search 9-1'].includes(x.name)), { focusedLibrary, focusSequence });
      if (!focusedLibrary) for (const id of cases.slice(6)) report.outcomes.push({ id, outcome: 'NOT_RUN', blockedBy: 'Library is not keyboard reachable' });
      else {
        await page.keyboard.press('Enter');
        try { await page.waitForFunction(() => location.hash === '#/home.library', undefined, { timeout: 10000 }); outcome('KEYBOARD-enter-library',true,{ hash: await page.evaluate(() => location.hash) }); }
        catch (e) { outcome('KEYBOARD-enter-library',false,e.message); }
        let focusedEvents = false;
        for (let i=0;i<40;i++) { await page.keyboard.press('Tab'); const current = await tree(); if (current.some(n => n.role?.value === 'button' && n.name?.value === 'Events' && n.properties?.some(p => p.name === 'focused' && p.value?.value === true))) { focusedEvents=true; break; } }
        if (!focusedEvents) outcome('KEYBOARD-space-events',false,{ focusedEvents });
        else { await page.keyboard.press('Space'); try { await page.waitForFunction(() => location.hash === '#/home.events', undefined, { timeout: 10000 }); outcome('KEYBOARD-space-events',true,{ hash: await page.evaluate(() => location.hash) }); } catch(e) { outcome('KEYBOARD-space-events',false,e.message); } }
      }
    }
  } catch(e) { report.runnerOrRuntimeFailure = { message: e.message, stack: e.stack }; }
  finally {
    for (const id of cases) if (!report.outcomes.some(x => x.id === id)) report.outcomes.push({ id, outcome: 'NOT_RUN', blockedBy: 'Runtime/runner failure' });
    if (context) { try { await context.tracing.stop({ path: path.join(output,'actual-accessibility-trace.zip') }); } catch(e) { report.traceFailure=e.message; } await context.close(); }
    await new Promise(resolve => server.close(resolve));
    report.counts = { discovered: cases.length, executed: report.outcomes.filter(x => x.outcome !== 'NOT_RUN').length, passed: report.outcomes.filter(x => x.outcome === 'PASS').length, failed: report.outcomes.filter(x => x.outcome === 'FAIL').length, notRun: report.outcomes.filter(x => x.outcome === 'NOT_RUN').length };
    fs.writeFileSync(path.join(output,'results.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify(report.counts));
    if (report.runnerOrRuntimeFailure || report.errors.length || report.outcomes.some(x => x.outcome !== 'PASS')) process.exitCode=1;
  }
})().catch(e => { console.error(e); process.exitCode=1; server.close(); });
