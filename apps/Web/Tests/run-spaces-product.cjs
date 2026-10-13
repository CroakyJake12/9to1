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
const declaredSourceCommit = process.argv[4] || '';
assert(/^[0-9a-f]{40}$/.test(declaredSourceCommit), 'Supply the exact source commit after bundle and fresh evidence directory');
assert(fs.existsSync(path.join(bundle, 'index.html')), 'A real published wwwroot is required');
assert(!fs.existsSync(output), 'Use a fresh evidence directory');
fs.mkdirSync(output, { recursive: true });
fs.copyFileSync(__filename, path.join(output, 'runner.cjs'));
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const report = { startedAt: new Date().toISOString(), declaredSourceCommit, sourceIdentityVerification: 'Supplied by the caller; must be bound independently by the build/publication receipt', runnerSha256: hash(fs.readFileSync(__filename)),
    scope: 'Actual supplied immutable compiled product in a fresh signed-out browser context; explicit empty draft/no-reference-data/model-unavailability checks plus original pointer/keyboard/navigation/resize/reload tests. No account/backend/provider substitution. NOT a fresh authenticated account or functional acceptance.',
    checks: [], errors: [], requestsFailed: [] };
let server, browser, context, page, origin, before, statusTimer, statusTask;
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
        && (document.querySelector('#browser-status')?.dataset.code === code
            || /^(HomeService|Faulted|Failed)/.test(document.querySelector('#browser-status')?.dataset.code || '')), code, { timeout: 90000 });
    const actualStatus = await page.locator('#browser-status').evaluate(node => ({code:node.dataset.code,text:node.textContent}));
    assert.equal(actualStatus.code, code, JSON.stringify(actualStatus));
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
                response.writeHead(200, { 'content-type': mime[path.extname(file)] || 'application/octet-stream', 'cache-control': 'no-store', 'Cross-Origin-Opener-Policy': 'same-origin', 'Cross-Origin-Embedder-Policy': 'require-corp', 'Referrer-Policy': 'no-referrer' });
                fs.createReadStream(file).pipe(response);
            } catch { response.writeHead(500); response.end(); }
        });
        await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
        origin = 'http://127.0.0.1:' + server.address().port;
        browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE || '/usr/bin/chromium', headless: true,
            args: ['--no-sandbox', '--disable-dev-shm-usage', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
        report.browserVersion = browser.version();
        context = await browser.newContext({ viewport: { width: 1530, height: 946 } });
        page = await context.newPage();
        page.on('pageerror', error => report.errors.push(String(error)));
        page.on('console', event => { (report.console ||= []).push({type:event.type(),text:event.text().slice(0,1500)}); if (event.type() === 'error') report.errors.push(event.text()); });
        let priorStatus;
        statusTimer = setInterval(() => {
            if (statusTask) return;
            statusTask = (async () => {
                try {
                    const observed = await page.locator('#browser-status').evaluate(node => ({code:node.dataset.code,text:node.textContent}));
                    const current = JSON.stringify(observed);
                    if (current !== priorStatus) {
                        priorStatus = current; report.currentStatus = observed; save(); console.log(current);
                    }
                } catch { /* A diagnostic read may race normal page navigation. */ }
            })().finally(() => { statusTask = undefined; });
        }, 3000);
        page.on('requestfailed', request => report.requestsFailed.push({ url: request.url(), error: request.failure()?.errorText }));
        await check('Cold Spaces entry opens actual shared Chat shell at approved1530x946 viewport', async () => {
            await page.goto(origin + '/#/app.spaces', { waitUntil: 'domcontentloaded' }); await ready('Ready');
            await stablePeer('button', 'Account');
            const actual = await capture('chat-1530x946');
            assert.equal(new URL(page.url()).hash, '#/app.spaces');
            assert(peer(actual, 'button', 'Account').enabled);
            assert.equal(peer(actual, 'button', 'Send message').enabled, false);
            assert(actual.elements.some(p => (p.value || p.name || '').includes('Sign in')));
            return { nativeElements: actual.elements.length, sharedChatRoot: true, signedOutSendDisabled: true };
        });
        await check('Fresh signed-out product context has empty draft, no visual-fixture records and no invented model availability', async () => {
            const actual = await snapshot();
            const strings = JSON.stringify(actual);
            const forbidden = ['Nearby Restaurants', 'Can you look for nearby restaurants?', 'Looking for restaurants now!', 'Which one should I pick?', 'Help Me Set up a bus', 'Diet Plans for 2026', 'Track Sales Tax', 'Monster Energy Drink', 'Qwen 3.6', 'Searching for restaurants', 'Creating a Shortlist'];
            for (const text of forbidden) assert(!strings.includes(text), 'Visual fixture content leaked: ' + text);
            assert.equal(peer(actual, 'textbox', 'Message').value, '');
            assert.equal(peer(actual, 'button', 'Choose model').enabled, false);
            assert.equal(peer(actual, 'button', 'Send message').enabled, false);
            assert.equal(actual.elements.filter(item => item.automationId === 'sidebar-entry-open').length, 0);
            return { freshBrowserContext: true, authenticatedAccount: false, emptyComposer: true, recentConversationRows: 0, noReferenceContent: true, modelAvailabilityNotInvented: true, mandatoryFreshAccountAcceptance: 'NOT RUN: no authenticated owner session' };
        });
        await check('Real pointer opens account settings from shared header and browser back restores Chat', async () => {
            await click('button', 'Account'); await page.waitForURL(url => url.hash === '#/home.settings'); await ready('Ready');
            assert.equal(new URL(page.url()).hash, '#/home.settings');
            await capture('account-from-chat');
            await page.goBack({waitUntil:'domcontentloaded'}); await ready('Ready');
            await stablePeer('button','Account');
            assert.equal(new URL(page.url()).hash, '#/app.spaces');
            return { actualPointer: true, accountRoute: true, backRestoresChat: true };
        });
        await check('Context sidebar collapses and restores by real pointer', async () => {
            await click('button', 'Hide contextual Space sidebar'); await stablePeer('button','Show contextual Space sidebar');
            await capture('chat-sidebar-collapsed');
            await click('button','Show contextual Space sidebar'); await stablePeer('button','Hide contextual Space sidebar');
            return { actualPointer: true };
        });
        await check('Actual sidebar search accepts keyboard text and clears without account substitution', async () => {
            await click('textbox', 'Search chats, groups and files');
            await page.keyboard.type('navigation input check');
            assert.equal(peer(await snapshot(), 'textbox', 'Search chats, groups and files').value, 'navigation input check');
            await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace');
            assert.equal(peer(await snapshot(), 'textbox', 'Search chats, groups and files').value, '');
            return { actualKeyboard: true, currentOwnerInput: true };
        });
        await check('Keyboard focus reaches Account and Enter opens its real route', async () => {
            let found = false;
            for (let index = 0; index < 18; index++) {
                await page.keyboard.press('Tab');
                const actual = await snapshot();
                if (actual.elements.some(item => item.role === 'button' && item.name === 'Account' && item.focused)) { found = true; break; }
            }
            assert(found, 'Account must be keyboard reachable');
            await page.keyboard.press('Enter'); await page.waitForURL(url => url.hash === '#/home.settings'); await ready('Ready');
            await page.goBack({waitUntil:'domcontentloaded'}); await ready('Ready'); await stablePeer('button','Account');
            return { actualKeyboard: true, enterDispatch: true, backRestoresChat: true };
        });
        await check('Expected signed-out state does not expose failed-turn recovery', async () => {
            const actual = await snapshot();
            assert(!actual.elements.some(item => item.role === 'button' && item.name === 'Resolve Issues'));
            assert(peer(actual, 'button', 'Account').enabled);
            assert.equal(peer(actual, 'button', 'Send message').enabled, false);
            return { expectedSignedOut: true, noFalseIncident: true, realAccountActionAvailable: true };
        });
        await check('Responsive actual Chat shell captures at390x844 and1920x1080 after resize and reload', async () => {
            await page.setViewportSize({width:390,height:844});
            await stablePeer('button','Account');
            await capture('chat-390x844');
            await click('button','Account'); await page.waitForURL(url => url.hash === '#/home.settings'); await ready('Ready');
            await page.goBack({waitUntil:'domcontentloaded'}); await ready('Ready');
            await stablePeer('button','Account');
            await page.reload({waitUntil:'domcontentloaded'}); await ready('Ready');
            await stablePeer('button','Account'); await capture('chat-reloaded-390x844');
            await page.setViewportSize({width:1920,height:1080});
            await stablePeer('button','Account'); await capture('chat-1920x1080');
            return { compact: true, wide: true, reload: true };
        });
        await check('Manage Spaces remains an explicit toolbox utility', async () => {
            await click('button','Manage Spaces'); await page.waitForURL(url => url.hash.includes('action=manage')); await ready('Ready');
            assert(new URL(page.url()).hash.includes('action=manage'));
            await capture('explicit-manager');
            return { toolboxRoute: new URL(page.url()).hash };
        });
        await check('Home exposes Spaces directly without expanding More destinations', async () => {
            await page.goto(origin + '/#/home.dashboard', {waitUntil:'domcontentloaded'}); await ready('Ready');
            await click('button', 'Spaces'); await page.waitForURL(url => url.hash === '#/app.spaces'); await ready('Ready');
            await stablePeer('button', 'Account');
            await capture('home-direct-spaces');
            return { actualPointer: true, directHomeNavigation: true };
        });
        assert.deepEqual(report.errors, []); assert.deepEqual(report.requestsFailed, []);
    } catch (error) {
        report.failure = error.stack || String(error); process.exitCode = 1;
        try {
            report.failureStatus = await page.locator('#browser-status').evaluate(node => ({code:node.dataset.code,text:node.textContent}));
            await page.screenshot({path:path.join(output,'failure.png'),fullPage:true});
            const actual = await snapshot(); fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify(actual,null,2));
        } catch(captureError) { report.failureCaptureError=String(captureError); }
    }
    finally {
        clearInterval(statusTimer);
        await statusTask;
        report.cleanupErrors = [];
        for (const [owner, close] of [
            ['context', () => context?.close()],
            ['browser', () => browser?.close()],
            ['server', () => server && new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()))]
        ]) {
            try { await close(); }
            catch (error) { report.cleanupErrors.push({ owner, error: String(error) }); process.exitCode = 1; }
        }
        report.cleanedUp = report.cleanupErrors.length === 0;
        try { assert.deepEqual(report.errors, []); assert.deepEqual(report.requestsFailed, []); }
        catch (error) { report.retirementError = String(error); process.exitCode = 1; }
        try { report.artifactUnchanged = JSON.stringify(before) === JSON.stringify(inventory()); assert(report.artifactUnchanged); }
        catch (error) { report.integrityError = String(error); process.exitCode = 1; }
        report.finishedAt = new Date().toISOString(); save();
        console.log(JSON.stringify({ checks: report.checks, errors: report.errors, requestsFailed: report.requestsFailed,
            artifactUnchanged: report.artifactUnchanged, cleanedUp: report.cleanedUp }, null, 2));
    }
})();
