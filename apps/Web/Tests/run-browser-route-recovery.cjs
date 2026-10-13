// Runs the actual published CUI application. No provider, route owner, document
// state, authentication result or application module is substituted.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || '/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const bundle = path.resolve(process.argv[2] || '');
const output = path.resolve(process.argv[3] || '');
const unavailableRoute = process.argv[4] || 'app.route-recovery-unregistered';
assert(fs.existsSync(path.join(bundle, 'index.html')), 'A real published wwwroot is required');
assert(!fs.existsSync(output), 'Use a fresh evidence directory');
assert(/^[a-zA-Z0-9._-]+$/.test(unavailableRoute));
fs.mkdirSync(output, { recursive: true });
fs.copyFileSync(__filename, path.join(output, 'runner.cjs'));
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const report = { startedAt: new Date().toISOString(), runnerSha256: hash(fs.readFileSync(__filename)),
    scope: 'Published browser cold unavailable-route recovery and warm draft preservation; controlled real private-reset boundary. Not account/provider or full Spaces acceptance.',
    checks: [], errors: [], requestsFailed: [] };
let server, browser, context, page, origin, before;
function save() { fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2)); }
function inventory() {
    const rows = [];
    function walk(directory) {
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            const file = path.join(directory, entry.name);
            assert(!entry.isSymbolicLink(), 'Published assets cannot alias mutable files');
            if (entry.isDirectory()) walk(file);
            else { const bytes = fs.readFileSync(file); rows.push({ path: path.relative(bundle, file), bytes: bytes.length, sha256: hash(bytes) }); }
        }
    }
    walk(bundle); return rows.sort((a, b) => a.path.localeCompare(b.path));
}
async function owner(operation) {
    return page.evaluate(async operation => {
        const runtime = getDotnetRuntime(0);
        const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
        return exports.NineToOne.Web.Program[operation]();
    }, operation);
}
async function snapshot() { return JSON.parse(await owner('ReadAccessibility')); }
async function ready(code) {
    await page.waitForFunction(code => typeof getDotnetRuntime === 'function'
        && getDotnetRuntime(0)?.getConfig()?.mainAssemblyName
        && document.querySelector('#browser-status')?.dataset.code === code, code, { timeout: 90000 });
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
}
async function capture(label) {
    const actual = await snapshot();
    fs.writeFileSync(path.join(output, label + '.json'), JSON.stringify(actual, null, 2));
    await page.screenshot({ path: path.join(output, label + '.png'), fullPage: true });
    return actual;
}
function peer(actual, role, name) {
    const peers = actual.elements.filter(item => item.role === role && item.name === name);
    assert.equal(peers.length, 1, `Expected one actual ${name} ${role}`);
    return peers[0];
}
async function stablePeer(role, name) {
    const deadline = Date.now() + 15000;
    let prior, consecutive = 0;
    while (Date.now() < deadline) {
        const actual = await snapshot();
        const matches = actual.elements.filter(item => item.role === role && item.name === name && item.enabled && item.bounds);
        const item = matches.length === 1 ? matches[0] : null;
        const identity = item && JSON.stringify([actual.generation, item.id, item.bounds]);
        consecutive = identity && identity === prior ? consecutive + 1 : 0;
        if (consecutive >= 2) return item;
        prior = identity;
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    }
    assert.fail(`Actual ${name} ${role} did not settle in the native presentation`);
}
async function click(role, name) {
    const item = await stablePeer(role, name);
    assert(item.enabled && item.bounds, name + ' is physically available');
    const canvas = await page.locator('#nine-to-one-root canvas').evaluateAll(nodes => {
        const node = nodes.find(node => node.width && node.height);
        if (!node) return null;
        const rect = node.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const b = item.bounds;
    assert(canvas && b.x >= 0 && b.y >= 0 && b.x + b.width <= canvas.width + 1 && b.y + b.height <= canvas.height + 1,
        name + ' is inside the actual viewport');
    const point = { x: canvas.x + b.x + b.width / 2, y: canvas.y + b.y + b.height / 2 };
    assert(await page.evaluate(p => !!document.elementFromPoint(p.x, p.y)?.closest('#nine-to-one-root'), point));
    await page.mouse.click(point.x, point.y);
}
async function check(name, run) {
    try { report.checks.push({ name, status: 'PASS', observed: await run() }); save(); }
    catch (error) { report.checks.push({ name, status: 'FAIL', error: error.stack || String(error) }); save(); throw error; }
}
(async () => {
    try {
        before = inventory();
        fs.writeFileSync(path.join(output, 'artifact-manifest.json'), JSON.stringify(before, null, 2));
        report.artifactManifestSha256 = hash(fs.readFileSync(path.join(output, 'artifact-manifest.json')));
        const mime = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css', '.ttf': 'font/ttf' };
        server = http.createServer((request, response) => {
            try {
                const url = new URL(request.url, 'http://localhost');
                const file = path.resolve(bundle, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
                if (!file.startsWith(bundle + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
                    response.writeHead(404); response.end(); return;
                }
                response.writeHead(200, { 'content-type': mime[path.extname(file)] || 'application/octet-stream', 'cache-control': 'no-store' });
                fs.createReadStream(file).pipe(response);
            } catch { response.writeHead(500); response.end(); }
        });
        await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
        origin = 'http://127.0.0.1:' + server.address().port;
        browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE || '/usr/bin/chromium', headless: true,
            args: ['--no-sandbox', '--disable-dev-shm-usage', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
        report.browserVersion = browser.version();
        context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
        page = await context.newPage();
        page.on('pageerror', error => report.errors.push(String(error)));
        page.on('console', event => { if (event.type() === 'error') report.errors.push(event.text()); });
        page.on('requestfailed', request => report.requestsFailed.push({ url: request.url(), error: request.failure()?.errorText }));
        const fragment = '#/' + unavailableRoute + '?entityType=space&entityId=recovery-context&action=open&deepLink=keep%2Fthis';
        await check('Cold unavailable link keeps its exact address and renders usable Home', async () => {
            await page.goto(origin + '/' + fragment, { waitUntil: 'domcontentloaded' });
            await ready('HomeServiceUnavailable');
            const actual = await capture('cold-unavailable-1280');
            assert.equal(new URL(page.url()).hash, fragment);
            assert(peer(actual, 'button', 'Home').enabled);
            assert(peer(actual, 'button', 'Write').enabled);
            assert.match(await page.locator('#browser-status').textContent(), /unavailable.*link has been preserved/i);
            return { fragment, nativeElements: actual.elements.length };
        });
        await check('Resize and reload preserve navigation and truthful unavailable state', async () => {
            await page.setViewportSize({ width: 800, height: 700 }); await ready('HomeServiceUnavailable');
            assert(peer(await capture('unavailable-800'), 'button', 'Home').enabled);
            await page.reload({ waitUntil: 'domcontentloaded' }); await ready('HomeServiceUnavailable');
            assert(peer(await capture('reloaded-unavailable-800'), 'button', 'Home').enabled);
            assert.equal(new URL(page.url()).hash, fragment);
            return { width: 800, height: 700, sameLink: true };
        });
        await check('Keyboard Home navigation leaves the unavailable address and clears its status', async () => {
            let focused = false;
            for (let count = 0; count < 80; count++) {
                await page.keyboard.press('Tab');
                focused = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') === 'Home');
                if (focused) break;
            }
            assert(focused, 'Actual browser Tab order reaches Home');
            await page.keyboard.press('Enter'); await ready('Ready');
            assert.equal(new URL(page.url()).hash, '#/home.dashboard');
            assert(peer(await capture('keyboard-home-recovery'), 'button', 'Home').enabled);
            return { keyboard: 'Tab, Enter', route: '#/home.dashboard' };
        });
        await check('Warm unavailable link preserves the existing Home native peers', async () => {
            const prior = await snapshot();
            await page.goto(origin + '/' + fragment, { waitUntil: 'domcontentloaded' }); await ready('HomeServiceUnavailable');
            const after = await capture('warm-home-retained');
            assert.equal(after.generation, prior.generation);
            assert.equal(peer(after, 'button', 'Home').id, peer(prior, 'button', 'Home').id);
            await click('button', 'Home'); await ready('Ready');
            return { sameNativeGeneration: after.generation };
        });
        await check('Warm unavailable link preserves a real unsaved Write draft and owner peer', async () => {
            await page.setViewportSize({ width: 1280, height: 900 }); await ready('Ready');
            await click('button', 'Write'); await ready('Ready');
            await click('button', 'New document');
            await click('textbox', 'Document title');
            await page.keyboard.press('Control+A'); await page.keyboard.type('Route recovery retained draft');
            const prior = await snapshot(), title = peer(prior, 'textbox', 'Document title');
            assert.equal(title.value, 'Route recovery retained draft');
            assert.equal(await owner('HasUnsavedChanges'), true);
            await page.goto(origin + '/' + fragment, { waitUntil: 'domcontentloaded' }); await ready('HomeServiceUnavailable');
            const after = await capture('warm-write-draft-retained'), retained = peer(after, 'textbox', 'Document title');
            assert.equal(after.generation, prior.generation); assert.equal(retained.id, title.id); assert.equal(retained.value, title.value);
            assert.equal(await owner('HasUnsavedChanges'), true);
            return { sameNativeGeneration: after.generation, sameTitlePeer: true, unsavedDraftRetained: true };
        });
        await check('A controlled actual private reset cannot resurrect the retired presentation', async () => {
            await owner('RevokePrivateContext'); assert.equal((await snapshot()).elements.length, 0);
            await page.goto(origin + '/#/app.route-recovery-after-reset', { waitUntil: 'domcontentloaded' }); await ready('HomeServiceUnavailable');
            assert.equal((await capture('private-reset-remains-empty')).elements.length, 0);
            return { retiredPresentationAbsent: true, realAccountSessionNotClaimed: true };
        });
        assert.deepEqual(report.errors, []); assert.deepEqual(report.requestsFailed, []);
    } catch (error) { report.failure = error.stack || String(error); process.exitCode = 1; }
    finally {
        try { await context?.close(); await browser?.close(); if (server) await new Promise(resolve => server.close(resolve)); report.cleanedUp = true; }
        catch (error) { report.cleanupError = String(error); process.exitCode = 1; }
        try { report.artifactUnchanged = JSON.stringify(before) === JSON.stringify(inventory()); assert(report.artifactUnchanged); }
        catch (error) { report.integrityError = String(error); process.exitCode = 1; }
        report.finishedAt = new Date().toISOString(); save();
        console.log(JSON.stringify({ checks: report.checks, errors: report.errors, requestsFailed: report.requestsFailed,
            artifactUnchanged: report.artifactUnchanged, cleanedUp: report.cleanedUp }, null, 2));
    }
})();
