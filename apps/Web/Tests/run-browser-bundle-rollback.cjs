// Runs two immutable published CUI bundles on the SAME origin/browser profile.
// The real Write UI creates and saves its own device-local document. No identity,
// account profile, route owner, stored document or application module is substituted.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || '/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const bundles = { candidate: path.resolve(process.argv[2] || ''), previous: path.resolve(process.argv[3] || '') };
let bundle = bundles.candidate, phase = 'candidate';
const output = path.resolve(process.argv[4] || '');
for (const root of Object.values(bundles)) assert(fs.existsSync(path.join(root, 'index.html')), 'Both real published wwwroots are required');
assert(!fs.existsSync(output), 'Use a fresh evidence directory');
fs.mkdirSync(output, { recursive: true });
fs.copyFileSync(__filename, path.join(output, 'runner.cjs'));
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const report = { startedAt: new Date().toISOString(), runnerSha256: hash(fs.readFileSync(__filename)),
    scope: 'Same-profile candidate / previous / candidate compiled bundle rollback and real device-local Write persistence. Authenticated Spaces writes NOT-RUN: legitimate profile required; no account or full Spaces acceptance.',
    assemblyResponses: [], unexpectedDialogs: [],
    checks: [], errors: [], requestsFailed: [] };
let server, browser, context, page, origin, before;
function save() { fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2)); }
function inventory(root = bundle) {
    const rows = [];
    function walk(directory) {
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            const file = path.join(directory, entry.name);
            assert(!entry.isSymbolicLink(), 'Published assets cannot alias mutable files');
            if (entry.isDirectory()) walk(file);
            else { const bytes = fs.readFileSync(file); rows.push({ path: path.relative(root, file), bytes: bytes.length, sha256: hash(bytes) }); }
        }
    }
    walk(root); return rows.sort((a, b) => a.path.localeCompare(b.path));
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
async function documentStorage() {
    const storage = await page.evaluate(async () => {
        const databases = (await indexedDB.databases()).map(db => ({ name: db.name, version: db.version })).sort((a, b) => a.name.localeCompare(b.name));
        if (!databases.some(db => db.name === 'nine-to-one-write')) throw Error('Actual saved Write database is absent');
        const db = await new Promise((resolve, reject) => {
            const request = indexedDB.open('nine-to-one-write');
            request.onupgradeneeded = () => { request.transaction.abort(); reject(Error('Readback must never create storage')); };
            request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
            request.onblocked = () => reject(Error('Readback was blocked'));
        });
        try {
            const transaction = db.transaction(['documents', 'history'], 'readonly');
            const done = new Promise((resolve, reject) => { transaction.oncomplete = resolve; transaction.onabort = () => reject(transaction.error); });
            const read = store => new Promise((resolve, reject) => {
                const request = transaction.objectStore(store).getAll(); request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
            });
            const [documents, history] = await Promise.all([read('documents'), read('history')]); await done;
            return { databases, documents, history };
        } finally { db.close(); }
    });
    assert.equal(storage.documents.length, 1, 'Exactly the UI-created local document is saved');
    assert(storage.history.length > 0, 'Canonical saved history exists');
    const tasks = storage.databases.find(db => db.name === 'nine-to-one-task-execution');
    if (tasks) assert.equal(tasks.version, 3, 'Shared task database remains rollback-compatible schema 3');
    const spaces = storage.databases.find(db => db.name === 'nine-to-one-task-execution-spaces');
    if (spaces) assert.equal(spaces.version, 1, 'New Spaces settings use their separate schema 1');
    return { databases: storage.databases, documentIds: storage.documents.map(d => d.id), historyCount: storage.history.length,
        documentsAndHistorySha256: hash(JSON.stringify({ documents: storage.documents, history: storage.history })) };
}
async function saved() {
    await page.waitForFunction(async () => {
        const runtime = getDotnetRuntime(0), exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
        return exports.NineToOne.Web.Program.HasUnsavedChanges() === false;
    }, null, { timeout: 30000 });
}
(async () => {
    try {
        before = Object.fromEntries(Object.entries(bundles).map(([name, root]) => [name, inventory(root)]));
        fs.writeFileSync(path.join(output, 'artifact-manifest.json'), JSON.stringify(before, null, 2));
        const application = name => before[name].find(file => /^_framework\/NineToOne\.Web\.[^.]+\.wasm$/.test(file.path));
        assert(application('candidate') && application('previous'), 'Actual managed application modules must exist');
        assert.notEqual(application('candidate').sha256, application('previous').sha256, 'Candidate must contain genuinely different compiled application code');
        report.applicationModules = { candidate: application('candidate'), previous: application('previous') };
        report.artifactManifestSha256 = hash(fs.readFileSync(path.join(output, 'artifact-manifest.json')));
        const mime = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css', '.ttf': 'font/ttf' };
        server = http.createServer((request, response) => {
            try {
                const url = new URL(request.url, 'http://localhost');
                const file = path.resolve(bundle, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
                if (!file.startsWith(bundle + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
                    response.writeHead(404); response.end(); return;
                }
                const requestedPhase = phase;
                if (/^NineToOne\.Web\.[^.]+\.wasm$/.test(path.basename(file))) {
                    const receipt = { phase: requestedPhase, path: path.relative(bundle, file), sha256: hash(fs.readFileSync(file)) };
                    response.once('finish', () => report.assemblyResponses.push(receipt));
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
        page.on('dialog', dialog => { report.unexpectedDialogs.push(dialog.type()); dialog.dismiss(); });
        page.on('pageerror', error => report.errors.push(String(error)));
        page.on('console', event => { if (event.type() === 'error') report.errors.push(event.text()); });
        page.on('requestfailed', request => report.requestsFailed.push({ url: request.url(), error: request.failure()?.errorText }));
        const title = 'Saved document for actual bundle rollback';
        let original;
        await check('Candidate creates and saves a real document through physical CUI controls', async () => {
            await page.goto(origin + '/#/home.dashboard', { waitUntil: 'domcontentloaded' }); await ready('Ready');
            await click('button', 'Write'); await ready('Ready'); await click('button', 'New document');
            await click('textbox', 'Document title'); await page.keyboard.press('Control+A'); await page.keyboard.type(title);
            assert.equal(peer(await snapshot(), 'textbox', 'Document title').value, title);
            assert.equal(await owner('HasUnsavedChanges'), true);
            await click('button', 'Save'); await saved();
            original = await documentStorage(); await capture('candidate-saved-document');
            await click('button', 'Home'); await ready('Ready'); await click('button', 'Write'); await ready('Ready');
            await stablePeer('button', title);
            return original;
        });
        await check('Actual previous bundle reloads and opens the unchanged saved document in the same browser profile', async () => {
            bundle = bundles.previous; phase = 'previous';
            await page.reload({ waitUntil: 'domcontentloaded' }); await ready('Ready');
            await click('button', title); await stablePeer('textbox', 'Document title');
            assert.equal(peer(await snapshot(), 'textbox', 'Document title').value, title); assert.equal(await owner('HasUnsavedChanges'), false);
            const storage = await documentStorage(); assert.equal(storage.documentsAndHistorySha256, original.documentsAndHistorySha256);
            assert.deepEqual(storage.documentIds, original.documentIds); await capture('previous-bundle-reopened-document');
            return storage;
        });
        await check('Candidate reopens the same unchanged document after returning from the previous bundle', async () => {
            bundle = bundles.candidate; phase = 'candidate-return';
            await page.reload({ waitUntil: 'domcontentloaded' }); await ready('Ready');
            await click('button', title); await stablePeer('textbox', 'Document title');
            assert.equal(peer(await snapshot(), 'textbox', 'Document title').value, title); assert.equal(await owner('HasUnsavedChanges'), false);
            const storage = await documentStorage(); assert.equal(storage.documentsAndHistorySha256, original.documentsAndHistorySha256);
            assert.deepEqual(storage.documentIds, original.documentIds); await capture('candidate-return-reopened-document');
            return storage;
        });
        for (const expected of ['candidate', 'previous', 'candidate-return']) {
            const expectedModule = report.applicationModules[expected === 'previous' ? 'previous' : 'candidate'];
            assert(report.assemblyResponses.some(actual => actual.phase === expected && actual.path === expectedModule.path && actual.sha256 === expectedModule.sha256),
                'The actual expected compiled application was delivered for ' + expected);
        }
        assert.deepEqual(report.unexpectedDialogs, []);
        assert.deepEqual(report.errors, []); assert.deepEqual(report.requestsFailed, []);
    } catch (error) { report.failure = error.stack || String(error); process.exitCode = 1; }
    finally {
        try { await context?.close(); await browser?.close(); if (server) await new Promise(resolve => server.close(resolve)); report.cleanedUp = true; }
        catch (error) { report.cleanupError = String(error); process.exitCode = 1; }
        try { report.artifactUnchanged = Object.entries(bundles).every(([name, root]) => JSON.stringify(before[name]) === JSON.stringify(inventory(root))); assert(report.artifactUnchanged); }
        catch (error) { report.integrityError = String(error); process.exitCode = 1; }
        report.finishedAt = new Date().toISOString(); save();
        console.log(JSON.stringify({ checks: report.checks, errors: report.errors, requestsFailed: report.requestsFailed,
            artifactUnchanged: report.artifactUnchanged, cleanedUp: report.cleanedUp }, null, 2));
    }
})();
