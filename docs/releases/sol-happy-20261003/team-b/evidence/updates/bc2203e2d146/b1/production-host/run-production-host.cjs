const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const crypto = require('node:crypto');
const { chromium } = require('/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

const bundle = fs.realpathSync(process.argv[2]);
const output = path.resolve(process.argv[3]);
const port = 18763;
fs.mkdirSync(output, { recursive: true });
if (!fs.existsSync(path.join(bundle, 'index.html')) || !fs.existsSync(path.join(bundle, '_framework/avalonia.js')))
    throw new Error('Serve only actual published output containing index.html and pinned avalonia.js.');
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
fs.copyFileSync(__filename, path.join(output, 'runner-executed.cjs'));
const files = [];
function inventory(directory) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const file = path.join(directory, entry.name);
        if (entry.isDirectory()) inventory(file);
        else if (entry.isFile()) files.push({ path: path.relative(bundle, file), bytes: fs.statSync(file).size, sha256: hash(file) });
    }
}
inventory(bundle);
fs.writeFileSync(path.join(output, 'published-artifacts.json'), JSON.stringify({ bundle, files }, null, 2));
const requests = [];
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2' };
const server = http.createServer((req, res) => {
    const url = new URL(req.url, `http://127.0.0.1:${port}`);
    const file = path.resolve(bundle, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
    let status = 200;
    if (!file.startsWith(bundle + path.sep) || !['GET', 'HEAD'].includes(req.method)) status = 403;
    else if (!fs.existsSync(file) || !fs.statSync(file).isFile()) status = 404;
    requests.push({ method: req.method, url: req.url, status });
    res.writeHead(status, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
    if (status !== 200) return res.end();
    if (req.method === 'HEAD') return res.end();
    fs.createReadStream(file).pipe(res);
});

(async () => {
    await new Promise(resolve => server.listen(port, '127.0.0.1', resolve));
    const profile = fs.mkdtempSync(path.join(output, 'chromium-profile-'));
    const args = ['--no-sandbox', '--disable-dev-shm-usage', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'];
    let context;
    const evidence = { bundle, runnerSha256: hash(__filename), browserExecutable: '/usr/bin/chromium', port, profile, args, commands: process.argv, claims: 'Local published production CUI host only; no deployed, backend, authenticated or full parity acceptance.', console: [], consoleCounts: {}, pageErrorCount: 0, managedRuntimeErrorCount: 0, pageErrors: [], requestFailures: [], httpErrors: [], observations: [], assertions: [] };
    function check(name, passed, observed) { evidence.assertions.push({ name, passed, observed }); }
    try {
        // Playwright normally disables BFCache; allow the browser's real lifecycle decision.
        context = await chromium.launchPersistentContext(profile, { executablePath: '/usr/bin/chromium', headless: true, args, ignoreDefaultArgs: ['--disable-back-forward-cache'], viewport: { width: 1440, height: 1000 } });
        evidence.browserVersion = context.browser().version();
        await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
        const page = context.pages()[0];
        await page.addInitScript(() => {
            window.__observedNativeLifecycle = [];
            for (const name of ['pagehide', 'pageshow']) window.addEventListener(name, event => window.__observedNativeLifecycle.push({ name, persisted: event.persisted }));
        });
        page.on('console', message => {
            const text = message.text();
            const key = `${message.type()}: ${text.slice(0, 250)}`;
            evidence.consoleCounts[key] = (evidence.consoleCounts[key] || 0) + 1;
            if (/MONO_WASM:|9to1 browser startup failed/.test(text) && message.type() === 'error') evidence.managedRuntimeErrorCount++;
            // The full Playwright trace remains raw; bounded JSON samples prevent error storms from losing the summary.
            if (evidence.console.length < 256) evidence.console.push({ type: message.type(), text: text.slice(0, 16000), originalLength: text.length, sha256: crypto.createHash('sha256').update(text).digest('hex'), location: message.location() });
        });
        page.on('pageerror', error => {
            evidence.pageErrorCount++;
            if (evidence.pageErrors.length < 256) evidence.pageErrors.push({ message: error.message.slice(0, 16000), stack: error.stack?.slice(0, 16000) });
        });
        page.on('requestfailed', request => evidence.requestFailures.push({ url: request.url(), failure: request.failure() }));
        page.on('response', response => { if (response.status() >= 400) evidence.httpErrors.push({ url: response.url(), status: response.status() }); });
        await page.goto(`http://127.0.0.1:${port}/`, { waitUntil: 'domcontentloaded' });
        try { await page.waitForFunction(() => document.querySelector('#browser-status')?.dataset.code !== 'Loading', undefined, { timeout: 90000 }); }
        catch (error) { evidence.startupTimeout = error.message; }
        await page.waitForTimeout(1000);
        async function observe(name) {
            const state = await page.evaluate(() => ({ url: location.href, title: document.title, status: document.querySelector('#browser-status')?.dataset.code, message: document.querySelector('#browser-status')?.textContent, nativeLifecycle: window.__observedNativeLifecycle, canvases: [...document.querySelectorAll('canvas')].map(canvas => ({ width: canvas.width, height: canvas.height, box: canvas.getBoundingClientRect().toJSON() })), html: document.body.innerHTML }));
            evidence.observations.push({ name, state, accessibility: await page.locator('body').ariaSnapshot() });
            await page.screenshot({ path: path.join(output, `${name}.png`), fullPage: true });
            fs.writeFileSync(path.join(output, `${name}.html`), state.html);
            console.log(`${name}: ${state.status}, canvases=${state.canvases.length}`);
            return state;
        }
        const startup = await observe('initial-home');
        check('actual-cui-bootstrap', startup.status === 'HomeServiceUnavailable' && startup.canvases.length > 0 && evidence.managedRuntimeErrorCount === 0 && evidence.pageErrorCount === 0, { status: startup.status, canvasCount: startup.canvases.length, managedRuntimeErrors: evidence.managedRuntimeErrorCount });
        if (startup.status === 'HomeServiceUnavailable' && startup.canvases.length > 0 && evidence.managedRuntimeErrorCount === 0 && evidence.pageErrorCount === 0) {
            evidence.pointerGeometry = await page.evaluate(() => ({ elements: [...document.querySelectorAll('#nine-to-one-root,canvas,.avalonia-native-host')].map(element => ({ id: element.id, box: element.getBoundingClientRect().toJSON(), position: getComputedStyle(element).position })), hit: document.elementFromPoint(70, 192)?.id }));
            await page.evaluate(() => {
                window.__pointerObserved = [];
                for (const type of ['pointerdown', 'pointerup']) window.addEventListener(type, event => window.__pointerObserved.push({ type, target: event.target.id, offsetX: event.offsetX, offsetY: event.offsetY, clientX: event.clientX, clientY: event.clientY, isTrusted: event.isTrusted }), true);
            });
            await page.mouse.click(447, 62);
            await page.waitForTimeout(500);
            const disabled = await observe('actual-disabled-studio-click');
            check('actual-disabled-studio-click', disabled.url === startup.url && disabled.message === startup.message, { url: disabled.url, message: disabled.message });
            await page.mouse.click(70, 192);
            await page.waitForTimeout(1000);
            const pointerLibrary = await observe('actual-enabled-library-click');
            check('actual-enabled-library-click', pointerLibrary.url.endsWith('#/home.library') && pointerLibrary.status === 'HomeServiceUnavailable', { url: pointerLibrary.url, status: pointerLibrary.status });
            evidence.pointerEvents = await page.evaluate(() => window.__pointerObserved);
            for (const [name, fragment, expectedCode] of [['direct-library', '#/home.library?entityType=file&entityId=opaque%20artifact&action=Reveal', 'HomeServiceUnavailable'], ['direct-unregistered-write', '#/app.write?entityId=private', 'HomeServiceUnavailable'], ['direct-invalid', '#/app.write?entityId=%Q0', 'BrowserRouteInvalid'], ['return-home', '#/home.dashboard', 'HomeServiceUnavailable']]) {
                await page.evaluate(fragment => { location.hash = fragment; }, fragment);
                await page.waitForTimeout(700);
                const state = await observe(name);
                check(name, state.status === expectedCode && state.url.endsWith(fragment), { status: state.status, url: state.url });
            }
            await page.goBack();
            await page.waitForTimeout(700);
            const back = await observe('history-back-invalid');
            check('history-back-invalid', back.status === 'BrowserRouteInvalid' && back.url.endsWith('#/app.write?entityId=%Q0'), { status: back.status, url: back.url });
            await page.goForward();
            await page.waitForTimeout(700);
            const forward = await observe('history-forward-home');
            check('history-forward-home', forward.status === 'HomeServiceUnavailable' && forward.url.endsWith('#/home.dashboard'), { status: forward.status, url: forward.url });
            for (const [name, fragment, expectedCode] of [['cold-typed-library', '#/home.library?entityType=file&entityId=opaque%20artifact&action=Reveal', 'HomeServiceUnavailable'], ['cold-invalid-route', '#/app.write?entityId=%Q0', 'BrowserRouteInvalid'], ['cold-return-home', '#/home.dashboard', 'HomeServiceUnavailable']]) {
                await page.evaluate(fragment => { location.hash = fragment; }, fragment);
                await page.reload({ waitUntil: 'domcontentloaded' });
                try { await page.waitForFunction(() => document.querySelector('#browser-status')?.dataset.code !== 'Loading', undefined, { timeout: 90000 }); }
                catch (error) { evidence[`${name}-timeout`] = error.message; }
                await page.waitForTimeout(1000);
                const state = await observe(name);
                check(name, state.status === expectedCode && state.url.endsWith(fragment), { status: state.status, url: state.url });
            }
            await page.goto('about:blank');
            await page.goBack({ waitUntil: 'domcontentloaded' });
            try { await page.waitForFunction(() => document.querySelector('#browser-status')?.dataset.code !== 'Loading', undefined, { timeout: 90000 }); }
            catch (error) { evidence.restorationTimeout = error.message; }
            await page.waitForTimeout(1500);
            const restored = await observe('real-document-history-restoration');
            const cached = restored.nativeLifecycle?.some(event => event.name === 'pageshow' && event.persisted);
            evidence.realBfCacheObserved = Boolean(cached);
            check('actual-history-session-state', restored.status === (cached ? 'PermissionRequired' : 'HomeServiceUnavailable'), { cached, status: restored.status, lifecycle: restored.nativeLifecycle });
            evidence.privateOwnerRevalidation = 'NOT-RUN: no authenticated adapter/session authority is registered.';
        } else evidence.dependentChecks = 'BLOCKED by actual production startup/render state; no mock or alternate page substituted.';
        check('no-runtime-errors', evidence.managedRuntimeErrorCount === 0 && evidence.pageErrorCount === 0 && evidence.requestFailures.length === 0, { managedRuntimeErrors: evidence.managedRuntimeErrorCount, pageErrors: evidence.pageErrorCount, requestFailures: evidence.requestFailures.length });
        await context.tracing.stop({ path: path.join(output, 'production-host-trace.zip') });
    } catch (error) {
        evidence.runnerFailure = { message: error.message, stack: error.stack };
        process.exitCode = 1;
    } finally {
        if (context) await context.close();
        await new Promise(resolve => server.close(resolve));
        evidence.serverRequests = requests;
        fs.writeFileSync(path.join(output, 'production-host-results.json'), JSON.stringify(evidence, null, 2));
        if (evidence.assertions.some(assertion => !assertion.passed)) process.exitCode = 1;
    }
})().catch(error => { console.error(error); process.exitCode = 1; server.close(); });
