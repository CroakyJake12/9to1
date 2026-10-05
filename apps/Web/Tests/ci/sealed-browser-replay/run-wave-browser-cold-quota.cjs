// Executes real published .NET/CUI Wave and real Chromium device APIs in an isolated persistent profile.
// No replacement DOM/provider/module or in-memory persistence. Local validation is not deployed staging acceptance.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || '/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const runnerBytesAtStart = fs.readFileSync(__filename);
const runnerSha256AtStart = crypto.createHash('sha256').update(runnerBytesAtStart).digest('hex');
const bundleRoot = path.resolve(process.argv[2]);
const output = path.resolve(process.argv[3]);
fs.mkdirSync(output, { recursive: true });
const cases = [ 'real-cui-route-and-inputs', 'create-canonical-local-project', 'real-filechooser-import', 'nondestructive-trim',
    'persistent-undo-redo', 'exact-pcm-download', 'real-browser-audio-transport', 'wrong-format-preserves-state',
    'browser-process-restart', 'actual-two-tab-revision-conflict', 'failed-save-cannot-close-dirty-project', 'stored-source-integrity-negative-control',
    'actual-browser-quota-two-store-atomicity', 'runtime-and-candidate-integrity' ];
const outcomes = Object.fromEntries(cases.map(id => [id, { outcome: 'NOT-RUN' }]));
const report = { scope: 'Actual published production browser CUI/Wave/IndexedDB/filechooser/HTMLAudioElement in local Chromium; NOT deployed staging, cross-device or full Wave parity',
    runnerSha256AtStart, bundleRoot, candidateManifest: process.env.B3_CANDIDATE_MANIFEST || null, outcomes, browserVersion: null, console: [], downloads: [], fixture: null,
    diagnostics: { counts: { consoleError: 0, managedRuntimeError: 0, pageError: 0, requestFailed: 0 }, boundedSamples: [], sampleLimit: 80 },
    interactionLimits: 'Fatal gate stops awaiting the current case; underlying Playwright work ends when context closes, not a domain cancellation guarantee. Owner peer focus+Enter and text input only. Does not certify Tab ordering, native pointer/touch editing or physical hardware audibility.' };
let context, page, server, origin, id, sourceId, clipId, trackId, sourceBytes, bundleBeforeFault;
let tracingStarted = false;
let manifestBytes, inventoryBefore;
let resolveRuntimeFailure, firstRuntimeError;
const runtimeFailure = new Promise(resolve => { resolveRuntimeFailure = resolve; });
function inventory(root) {
    const rows = [];
    const visit = directory => { for (const entry of fs.readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
        const file = path.join(directory, entry.name);
        if (entry.isDirectory()) visit(file);
        else if (entry.isFile()) rows.push({ path: path.relative(root, file), bytes: fs.statSync(file).size,
            sha256: crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex') });
    } };
    visit(root); return rows.sort((a, b) => a.path.localeCompare(b.path));
}
function diagnostic(kind, text) {
    report.diagnostics.counts[kind]++;
    if (!firstRuntimeError) {
        report.firstRuntimeFailure = { kind, text: String(text).slice(0, 2000) };
        firstRuntimeError = new Error('Unexpected ' + kind + ': ' + String(text).slice(0, 2000));
        resolveRuntimeFailure(firstRuntimeError);
    }
    if (report.diagnostics.boundedSamples.length < report.diagnostics.sampleLimit)
        report.diagnostics.boundedSamples.push({ kind, text: String(text).slice(0, 2000) });
}
const observedPages = new WeakSet();
function observe(target) {
    if (observedPages.has(target)) return;
    observedPages.add(target);
    target.on('dialog', dialog => dialog.accept());
    target.on('console', event => {
        if (report.console.length < 80) report.console.push({ type: event.type(), text: event.text().slice(0, 2000) });
        if (event.type() === 'error') diagnostic('consoleError', event.text());
        if (/Unhandled exception|MONO_WASM.*(?:error|assertion)|RuntimeError:|Aborted\(/i.test(event.text())) diagnostic('managedRuntimeError', event.text());
    });
    target.on('pageerror', error => {
        const detail = [error.name, error.message, error.stack].filter(Boolean).join(': ') || String(error);
        diagnostic('pageError', detail);
        if (error.name === 'ManagedError') diagnostic('managedRuntimeError', detail);
    });
    target.on('requestfailed', request => diagnostic('requestFailed', request.method() + ' ' + request.url() + ': ' + request.failure()?.errorText));
}
const flush = () => fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ ...report,
    counts: { discovered: cases.length, executed: Object.values(outcomes).filter(x => x.outcome !== 'NOT-RUN').length,
        passed: Object.values(outcomes).filter(x => x.outcome === 'PASS').length, failed: Object.values(outcomes).filter(x => x.outcome === 'FAIL').length,
        notRun: Object.values(outcomes).filter(x => x.outcome === 'NOT-RUN').length } }, null, 2));
async function test(id, execute) {
    try { if (firstRuntimeError) throw firstRuntimeError; const observed = await Promise.race([execute(), runtimeFailure.then(error => { throw error; })]); outcomes[id] = { outcome: 'PASS', observed }; flush(); }
    catch (error) { outcomes[id] = { outcome: 'FAIL', error: error.stack }; flush(); throw error; }
}
function fixture() {
    const bytes = Buffer.alloc(44 + 48000 * 4);
    bytes.write('RIFF'); bytes.writeUInt32LE(bytes.length - 8, 4); bytes.write('WAVEfmt ', 8); bytes.writeUInt32LE(16, 16);
    bytes.writeUInt16LE(1, 20); bytes.writeUInt16LE(2, 22); bytes.writeUInt32LE(48000, 24); bytes.writeUInt32LE(192000, 28);
    bytes.writeUInt16LE(4, 32); bytes.writeUInt16LE(16, 34); bytes.write('data', 36); bytes.writeUInt32LE(bytes.length - 44, 40);
    for (let frame = 0; frame < 48000; frame++) { bytes.writeInt16LE(frame % 2000 - 1000, 44 + frame * 4); bytes.writeInt16LE(1000 - frame % 2000, 46 + frame * 4); }
    return bytes;
}
async function readBundles(target = page) {
    return target.evaluate(() => new Promise((resolve, reject) => {
        const request = indexedDB.open('9to1-wave-local-v1');
        request.onupgradeneeded = () => request.transaction.abort();
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
            const db = request.result; const transaction = db.transaction('projects', 'readonly');
            const read = transaction.objectStore('projects').getAll();
            read.onsuccess = () => { resolve(read.result); db.close(); }; read.onerror = () => { reject(read.error); db.close(); };
        };
    }));
}
async function saved(revision, target = page) {
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        const value = (await readBundles(target)).find(p => p.projectId === id);
        if (value?.revision === revision) return value;
        await target.waitForTimeout(100);
    }
    throw new Error('Expected durable revision ' + revision);
}
// Call only for successful operations acknowledged by this actual owner, never external winner/conflict reads.
async function ownerCompleted(revision, target = page) {
    const ready = await target.waitForFunction(expected => {
        const root = document.querySelector('#native-control-semantics');
        const create = root?.querySelector('[data-automation-id="wave-create"]');
        const identity = root?.querySelector('[data-automation-id="wave-project-identity"]')?.textContent;
        if (!create || create.disabled || !identity?.includes('Project ' + expected.projectId + ' · ')
            || !identity.endsWith(' · revision ' + expected.revision)) return false;
        return { projectId: expected.projectId, revision: expected.revision, ownerNotBusy: true,
            identity, status: root.querySelector('[data-automation-id="wave-status"]')?.textContent };
    }, { projectId: id, revision }, { timeout: 15000 });
    (report.operationReadiness ??= []).push({ ...(await ready.jsonValue()), page: target.url() });
    await ready.dispose();
}
async function acknowledgedByOwner(revision, target = page) {
    const value = await saved(revision, target);
    await ownerCompleted(revision, target);
    return value;
}
async function button(name, target = page) {
    const control = target.getByRole('button', { name, exact: true });
    await control.waitFor({ state: 'attached' });
    assert.equal(await control.isEnabled(), true, name + ' must be available');
    await control.focus(); await control.press('Enter');
}
async function field(name, value, target = page) {
    const control = target.getByRole('textbox', { name, exact: true }); await control.waitFor({ state: 'attached' });
    await control.fill(value);
}
async function choose(filePath, target = page) {
    const chooser = target.waitForEvent('filechooser');
    await button('Import WAV', target); await (await chooser).setFiles(filePath);
}
async function nativeText(target = page) { return target.locator('#native-control-semantics').innerText(); }
async function waitText(text, target = page) {
    await target.waitForFunction(text => document.querySelector('#native-control-semantics')?.textContent.includes(text), text, { timeout: 15000 });
}
function parse(value) { return JSON.parse(value.projectJson); }
function exactSource(value) {
    const source = value.sources.find(s => s.path === parse(value).Tracks[0].Clips[0].SourcePath);
    assert(source); assert.deepEqual(Buffer.from(source.base64, 'base64'), sourceBytes);
    assert.equal(source.sha256.toLowerCase(), report.fixture.sha256);
}
// Deliberately blank controlled pages after genuine owner readiness; does not certify automatic tab/URL restoration.
async function prepareBlankRestart(phase, revision) {
    await ownerCompleted(revision);
    const receipts = [];
    for (const target of context.pages()) {
        const beforeUrl = target.url();
        let actualOwnerUnsaved = null;
        if (beforeUrl !== 'about:blank') {
            assert(beforeUrl.startsWith(origin + '/'), 'Only controlled application pages may be blanked for restart.');
            actualOwnerUnsaved = await target.evaluate(async () => {
                const runtime = globalThis.getDotnetRuntime(0);
                const owner = (await runtime.getAssemblyExports('NineToOne.Web')).NineToOne.Web.Program;
                return owner.HasUnsavedChanges();
            });
            assert.equal(actualOwnerUnsaved, false, 'Actual shell must have no dirty/busy/unknown session before deliberate blanking.');
            await target.goto('about:blank');
        }
        assert.equal(target.url(), 'about:blank');
        receipts.push({ beforeUrl, afterUrl: target.url(), actualOwnerUnsaved });
    }
    (report.deliberateBlankRestarts ??= []).push({ phase, canonicalProjectId: id, revision, pages: receipts,
        scope: 'Actual blank navigation before process close prevents app URL restoration; no automatic tab/URL restoration acceptance.' });
}
async function stopTrace(file) {
    assert.equal(tracingStarted, true, 'An actual tracing session must have started.');
    await context.tracing.stop({ path: path.join(output, file) });
    tracingStarted = false;
}
async function launch() {
    context = await chromium.launchPersistentContext(path.join(output, 'profile'), { executablePath: process.env.CHROMIUM_EXECUTABLE || '/usr/bin/chromium', headless: true,
        viewport: { width: 1100, height: 800 }, acceptDownloads: true, args: ['--no-sandbox', '--disable-dev-shm-usage'] });
    context.on('page', observe);
    const initialPages = context.pages();
    initialPages.forEach(observe);
    const initialPageUrls = initialPages.map(target => target.url());
    (report.browserLaunchPages ??= []).push({ launch: report.browserLaunchPages?.length ?? 0, initialPageUrls });
    await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
    tracingStarted = true;
    assert(initialPageUrls.every(url => url === 'about:blank'), 'A restored nonblank initial page could perform production storage writes before the quota override.');
    report.browserVersion = context.browser()?.version() ?? await initialPages[0].evaluate(() => navigator.userAgent);
    page = await context.newPage();
    observe(page);
    return page;
}
(async () => {
    flush();
    try {
        assert(report.candidateManifest && process.env.B3_CANDIDATE_MANIFEST_SHA256, 'Root-reviewed candidate manifest and its exact SHA256 are required.');
        manifestBytes = fs.readFileSync(report.candidateManifest);
        report.candidateManifestSha256 = crypto.createHash('sha256').update(manifestBytes).digest('hex');
        assert.equal(report.candidateManifestSha256, process.env.B3_CANDIDATE_MANIFEST_SHA256);
        const manifest = JSON.parse(manifestBytes);
        report.candidateCommit = manifest.sourceCommit ?? manifest.commit;
        assert(/^[a-f0-9]{40}$/.test(report.candidateCommit || ''), 'Manifest must identify exact sourceCommit or commit.');
        inventoryBefore = inventory(bundleRoot); assert(inventoryBefore.length > 0); report.bundleInventoryBefore = inventoryBefore;
        const expected = manifest.publishFiles ?? manifest.files;
        assert(Array.isArray(expected) && expected.length > 0, 'Root-reviewed manifest must contain exact published file inventory.');
        const sealed = expected.map(row => {
            assert(typeof row.path === 'string' && !path.isAbsolute(row.path) && !row.path.split('/').includes('..'), 'Manifest paths must be relative published paths.');
            assert(Number.isSafeInteger(row.bytes) && /^[a-f0-9]{64}$/.test(row.sha256), 'Each published file needs exact bytes and SHA256.');
            return { path: row.path, bytes: row.bytes, sha256: row.sha256 };
        }).sort((a, b) => a.path.localeCompare(b.path));
        assert.deepEqual(inventoryBefore, sealed, 'Actual bundle must match sealed manifest exactly, including missing/extra/stale files.');
        sourceBytes = fixture(); const fixturePath = path.join(output, 'stereo-pcm16.wav'); fs.writeFileSync(fixturePath, sourceBytes);
        report.fixture = { path: fixturePath, sha256: crypto.createHash('sha256').update(sourceBytes).digest('hex'), frames: 48000, channels: 2, sampleRate: 48000 };
        server = http.createServer((request, response) => {
            const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname);
            const file = path.resolve(bundleRoot, '.' + (pathname === '/' ? '/index.html' : pathname));
            if (!file.startsWith(bundleRoot + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { response.writeHead(404); response.end(); return; }
            const type = file.endsWith('.html') ? 'text/html' : file.endsWith('.js') || file.endsWith('.mjs') ? 'text/javascript' : file.endsWith('.wasm') ? 'application/wasm' : file.endsWith('.json') ? 'application/json' : 'application/octet-stream';
            response.writeHead(200, { 'Content-Type': type, 'Cache-Control': 'no-store' }); fs.createReadStream(file).pipe(response);
        });
        await new Promise((resolve, reject) => { server.once('error', reject); server.listen(Number(process.env.B3_PORT || 0), '127.0.0.1', resolve); });
        origin = 'http://127.0.0.1:' + server.address().port; report.origin = origin;
        await launch();
        await test(cases[0], async () => {
            await page.goto(origin + '/#/app.wave'); await page.getByRole('button', { name: 'Create project', exact: true }).waitFor({ state: 'attached', timeout: 60000 });
            assert.equal(await page.getByRole('textbox', { name: 'Project or new track name', exact: true }).count(), 1);
            assert.equal(await page.getByRole('textbox', { name: 'Project sample rate in Hertz', exact: true }).count(), 1);
            assert((await nativeText()).includes('Local audio editor'));
            await page.screenshot({ path: path.join(output, 'wave-empty.png'), fullPage: true });
            const cdp = await context.newCDPSession(page); const ax = await cdp.send('Accessibility.getFullAXTree');
            fs.writeFileSync(path.join(output, 'native-ax-tree.json'), JSON.stringify(ax.nodes, null, 2)); await cdp.detach();
            return { route: page.url(), realNativeSemantics: true };
        });
        await test(cases[1], async () => {
            await field('Project or new track name', 'Browser Wave fixture'); await button('Create project');
            await page.waitForFunction(() => document.querySelector('#native-control-semantics')?.textContent.includes('Saved in this browser'));
            const all = await readBundles(); assert.equal(all.length, 1); const value = all[0]; const project = parse(value); id = value.projectId;
            assert.equal(project.ProjectId, id); assert.equal(project.Revision, 0); assert.equal(project.SchemaVersion, 6); assert.equal(project.Tracks.length, 1);
            assert.equal(value.name, 'Browser Wave fixture'); assert.equal(project.Tracks[0].Name, 'Browser Wave fixture');
            trackId = project.Tracks[0].TrackId; assert(trackId && trackId !== '00000000-0000-0000-0000-000000000000');
            const priorPackage = JSON.stringify(all);
            const rejectedDeepLinks = [];
            for (const deepLink of ['', 'unsupported-local-route']) {
                const invalid = origin + '/#/app.wave?entityType=WaveProject&entityId=' + crypto.randomUUID() + '&deepLink=' + encodeURIComponent(deepLink);
                await page.goto(invalid);
                await page.waitForFunction(() => document.querySelector('#browser-status')?.dataset.code === 'InvalidArgument');
                assert.equal(page.url(), invalid, 'Rejected route must preserve its exact request URL.');
                assert.equal(JSON.stringify(await readBundles()), priorPackage, 'Unsupported DeepLink cannot change durable local state.');
                rejectedDeepLinks.push(deepLink);
            }
            await page.goto(origin + '/#/app.wave?entityType=WaveProject&entityId=' + id);
            await waitText('Opened local project');
            assert.equal(JSON.stringify(await acknowledgedByOwner(0)), JSON.stringify(value));
            return { projectId: id, trackId, revision: 0, rejectedDeepLinksBeforeMissingEntityLookup: rejectedDeepLinks };
        });
        await test(cases[2], async () => {
            await field('Timeline position in seconds for import split move duplicate marker', '0.5'); await choose(fixturePath);
            const value = await acknowledgedByOwner(1); const clip = parse(value).Tracks[0].Clips[0]; clipId = clip.ClipId; sourceId = clip.SourceReferenceId;
            assert.equal(clip.SourceStartFrame, 0); assert.equal(clip.FrameCount, 48000); assert.equal(clip.TimelineStartFrame, 24000);
            assert.equal(clip.SourceSha256.toLowerCase(), report.fixture.sha256); exactSource(value);
            return { clipId, sourceId, timelineStart: clip.TimelineStartFrame, frames: clip.FrameCount };
        });
        await test(cases[3], async () => {
            await field('Seconds removed from clip start', '0.125'); await field('Seconds removed from clip end', '0.125'); await button('Trim selected clip');
            const value = await acknowledgedByOwner(2); const clip = parse(value).Tracks[0].Clips[0];
            assert.equal(clip.ClipId, clipId); assert.equal(clip.SourceStartFrame, 6000); assert.equal(clip.FrameCount, 36000); assert.equal(clip.TimelineStartFrame, 30000); exactSource(value);
            return { revision: 2, sourceStart: clip.SourceStartFrame, frames: clip.FrameCount };
        });
        await test(cases[4], async () => {
            await button('Undo'); assert.equal(parse(await acknowledgedByOwner(3)).Tracks[0].Clips[0].FrameCount, 48000);
            await button('Redo'); assert.equal(parse(await acknowledgedByOwner(4)).Tracks[0].Clips[0].FrameCount, 36000);
            await button('Save and close'); await waitText('Closed.'); await button('Reopen last project'); await waitText('Opened local project');
            await button('Undo'); const value = await acknowledgedByOwner(5); const project = parse(value);
            assert.equal(project.ProjectId, id); assert.equal(project.Tracks[0].TrackId, trackId); assert.equal(project.Tracks[0].Clips[0].ClipId, clipId);
            assert.equal(project.Tracks[0].Clips[0].FrameCount, 48000); assert(value.redo.length > 0); exactSource(value);
            return { reopenedHistory: true, revision: 5, persistentIds: [id, trackId, clipId, sourceId] };
        });
        await test(cases[5], async () => {
            const before = JSON.stringify(await saved(5)); const download = page.waitForEvent('download'); await button('Export PCM16 WAV');
            const actual = await download; const file = path.join(output, 'browser-export.wav'); await actual.saveAs(file);
            assert.equal(await actual.failure(), null); const bytes = fs.readFileSync(file);
            assert.equal(bytes.length, 44 + 72000 * 4); assert(bytes.subarray(44, 44 + 24000 * 4).every(byte => byte === 0));
            assert.deepEqual(bytes.subarray(44 + 24000 * 4), sourceBytes.subarray(44)); assert.equal(JSON.stringify(await saved(5)), before);
            report.downloads.push({ file, sha256: crypto.createHash('sha256').update(bytes).digest('hex') });
            return { actualDownloadBytes: bytes.length, exactPcm: true, projectUnchanged: true };
        });
        await test(cases[6], async () => {
            await button('Play mix'); await waitText('Playing local project');
            const read = () => page.evaluate(async () => JSON.parse(await (await import('./wave-browser.js')).invoke('state', '{}')).value);
            let state = await read(); await page.waitForTimeout(250); const later = await read();
            assert.equal(later.paused, false); assert(later.position > state.position); assert.equal(later.duration, 1.5);
            await button('Pause'); state = await read(); assert.equal(state.paused, true);
            await field('Seek position in seconds', '0.75'); await button('Seek to seconds'); state = await read(); assert(Math.abs(state.position - .75) < .05);
            await button('Stop'); state = await read(); assert.equal(state.paused, true); assert.equal(state.position, 0);
            return { decodedDuration: later.duration, actualAdvancingPosition: later.position, hardwareAudibilityVerified: false };
        });
        await test(cases[7], async () => {
            const before = JSON.stringify(await saved(5)); const wrong = Buffer.from(sourceBytes); wrong.writeUInt32LE(44100, 24); wrong.writeUInt32LE(44100 * 4, 28);
            const file = path.join(output, 'wrong-rate.wav'); fs.writeFileSync(file, wrong); await choose(file); await waitText('CodecUnsupported');
            assert.equal(JSON.stringify(await saved(5)), before); return { refusedActual44100Source: true, durableBytesUnchanged: true };
        });
        await test(cases[8], async () => {
            await page.screenshot({ path: path.join(output, 'wave-populated.png'), fullPage: true });
            await prepareBlankRestart('durable-project-restart', 5);
            await stopTrace('browser-phase1.zip'); await context.close(); context = null;
            await launch(); await page.goto(origin + '/#/app.wave?entityType=WaveProject&entityId=' + id);
            await waitText('Opened local project'); const value = await acknowledgedByOwner(5); const project = parse(value);
            assert.equal(project.ProjectId, id); assert.equal(project.Tracks[0].TrackId, trackId); assert.equal(project.Tracks[0].Clips[0].ClipId, clipId);
            assert.equal(project.Tracks[0].Clips[0].FrameCount, 48000); exactSource(value);
            return { actualBrowserProcessRestart: true, sameProfileSameOrigin: true, revision: 5 };
        });
        await test(cases[9], async () => {
            const second = await context.newPage(); observe(second); report.secondPage = second.url();
            await second.goto(origin + '/#/app.wave?entityType=WaveProject&entityId=' + id); await waitText('Opened local project', second);
            await field('Timeline position in seconds for import split move duplicate marker', '0.25'); await button('Move to timeline time'); const committed = await acknowledgedByOwner(6);
            await field('Timeline position in seconds for import split move duplicate marker', '0.75', second); await button('Move to timeline time', second); await waitText('RevisionConflict', second);
            assert.equal(JSON.stringify(await saved(6, second)), JSON.stringify(committed));
            assert((await nativeText(second)).includes('timeline frame 36000'));
            report.secondPage = second.url(); global.secondWavePage = second;
            return { winnerRevision: 6, winnerTimeline: parse(committed).Tracks[0].Clips[0].TimelineStartFrame, rejectedTabPreservedUnsavedTimeline: 36000 };
        });
        await test(cases[10], async () => {
            const second = global.secondWavePage; const before = JSON.stringify(await saved(6));
            await button('Save and close', second); await waitText('RevisionConflict', second);
            assert((await nativeText(second)).includes('timeline frame 36000')); assert.equal(JSON.stringify(await saved(6)), before);
            await second.close(); return { dirtyConflictCannotClose: true, durableWinnerUnchanged: true };
        });
        await test(cases[11], async () => {
            bundleBeforeFault = await saved(6);
            await page.evaluate(projectId => new Promise((resolve, reject) => {
                const request = indexedDB.open('9to1-wave-local-v1'); request.onsuccess = () => {
                    const db = request.result; const transaction = db.transaction('projects', 'readwrite'); const store = transaction.objectStore('projects');
                    const read = store.get(projectId); read.onsuccess = () => { const value = read.result; value.sources[0].base64 = btoa('controlled-corrupt-local-fixture'); store.put(value); };
                    transaction.oncomplete = () => { db.close(); resolve(); }; transaction.onabort = () => { db.close(); reject(transaction.error); };
                }; request.onerror = () => reject(request.error);
            }), id);
            await button('Save and close'); await waitText('Closed.'); await button('Reopen last project'); await waitText('SourceChanged');
            assert(!(await nativeText()).includes('Opened local project'));
            await page.evaluate(bundle => new Promise((resolve, reject) => {
                const request = indexedDB.open('9to1-wave-local-v1'); request.onsuccess = () => {
                    const db = request.result; const transaction = db.transaction('projects', 'readwrite'); transaction.objectStore('projects').put(bundle);
                    transaction.oncomplete = () => { db.close(); resolve(); }; transaction.onabort = () => { db.close(); reject(transaction.error); };
                }; request.onerror = () => reject(request.error);
            }), bundleBeforeFault);
            await button('Reopen last project'); await waitText('Opened local project'); exactSource(await acknowledgedByOwner(6));
            return { isolatedDurableSourceCorruptionDetected: true, originalPackageRestored: true };
        });
        await test(cases[12], async () => {
            const before = await saved(6);
            const summary = async () => page.evaluate(id => new Promise((resolve, reject) => {
                const request = indexedDB.open('9to1-wave-local-v1'); request.onsuccess = () => {
                    const db = request.result; const read = db.transaction('summaries', 'readonly').objectStore('summaries').get(id);
                    read.onsuccess = () => { db.close(); resolve(read.result); }; read.onerror = () => { db.close(); reject(read.error); };
                }; request.onerror = () => reject(request.error);
            }), id);
            const beforeSummary = await summary();
            async function browserIdentity() {
                const metadata = await context.browser().newBrowserCDPSession();
                try {
                    const info = await metadata.send('SystemInfo.getProcessInfo');
                    const browserProcess = info.processInfo.find(item => item.type === 'browser');
                    assert(browserProcess && Number.isSafeInteger(browserProcess.id) && browserProcess.id > 0);
                    return { browserPid: browserProcess.id, version: await metadata.send('Browser.getVersion') };
                } finally { await metadata.detach(); }
            }
            const previousIdentity = await browserIdentity();
            const previousBrowser = context.browser();
            await prepareBlankRestart('cold-quota-restart', 6);
            await stopTrace('browser-before-quota-restart.zip');
            await context.close(); context = null;
            assert.equal(previousBrowser.isConnected(), false, 'Real previous browser must disconnect before the cold quota phase.');
            await launch();
            const restartedIdentity = await browserIdentity();
            assert.notEqual(restartedIdentity.browserPid, previousIdentity.browserPid, 'Cold quota needs an actual new browser process.');
            assert.equal(restartedIdentity.version.product, previousIdentity.version.product, 'Same actual browser version required.');
            assert.equal(restartedIdentity.version.revision, previousIdentity.version.revision, 'Same actual browser revision required.');
            const cdp = await context.newCDPSession(page);
            report.actualQuotaRestart = { previousIdentity, restartedIdentity,
                previousBrowserDisconnected: !previousBrowser.isConnected(),
                order: 'Original baseline in prior process; close/disconnect; new same-profile process; quota override BEFORE page navigation/production storage access; original owner project open; first owner mutation is Move.' };
            try {
                const beforeOverridePageUrls = context.pages().map(target => target.url());
                report.actualQuotaRestart.beforeOverridePageUrls = beforeOverridePageUrls;
                assert(beforeOverridePageUrls.every(url => url === 'about:blank'), 'All restarted pages must remain blank until the quota override is active.');
                await cdp.send('Storage.overrideQuotaForOrigin', { origin, quotaSize: 1 });
                const quota = await cdp.send('Storage.getUsageAndQuota', { origin }); report.actualQuotaControl = quota;
                assert.equal(quota.quota, 1, 'Actual quota must equal requested one-byte override.');
                assert.equal(quota.overrideActive, true, 'Actual quota override must be active.');
                await page.goto(origin + '/#/app.wave?entityType=WaveProject&entityId=' + id);
                await waitText('Opened local project');
                exactSource(await acknowledgedByOwner(6));
                await field('Timeline position in seconds for import split move duplicate marker', '0.35'); await button('Move to timeline time');
                await waitText('StorageFull');
                await ownerCompleted(7); // Actual failed owner operation finished with its unsaved revision7; durable revision stays6.
                assert.deepEqual((await readBundles()).find(value => value.projectId === id), before);
                assert.deepEqual(await summary(), beforeSummary);
                assert((await nativeText()).includes('timeline frame 16800'));
            } finally { await cdp.send('Storage.overrideQuotaForOrigin', { origin }); await cdp.detach(); }
            await button('Save locally'); const after = await acknowledgedByOwner(7);
            assert.equal(parse(after).Tracks[0].Clips[0].TimelineStartFrame, 16800); assert.equal((await summary()).revision, 7);
            return { genuineChromiumQuotaRefusal: true, bothDurableStoresUnchangedOnAbort: true, samePendingEditCommittedOnceAfterRecovery: true };
        });
        await test(cases[13], async () => {
            assert.deepEqual(inventory(bundleRoot), inventoryBefore, 'Actual published bundle cannot change during acceptance.');
            assert.deepEqual(fs.readFileSync(report.candidateManifest), manifestBytes, 'Candidate manifest cannot change during acceptance.');
            assert.equal(Object.values(report.diagnostics.counts).reduce((a, b) => a + b, 0), 0, 'Unexpected browser/managed/resource errors must fail acceptance.');
            return { exactManifestSha256: report.candidateManifestSha256, candidateCommit: report.candidateCommit, assetFiles: inventoryBefore.length, unexpectedErrors: report.diagnostics.counts };
        });
        await page.setViewportSize({ width: 390, height: 844 }); await page.screenshot({ path: path.join(output, 'wave-narrow.png'), fullPage: true });
        report.narrowScreenshot = 'Visual evidence only; narrow layout/touch acceptance needs independent review.';
    } catch (error) { report.failure = error.stack; process.exitCode = 1; }
    finally {
        const finalFailure = (stage, error) => {
            (report.finalizationFailures ??= []).push({ stage, error: error.stack ?? String(error) });
            outcomes[cases[13]] = { outcome: 'FAIL', error: 'Acceptance finalization failed.', observed: report.finalizationFailures };
            process.exitCode = 1;
        };
        if (context) {
            if (tracingStarted) {
                try { await stopTrace('browser-final.zip'); }
                catch (error) { finalFailure('trace-stop', error); }
            } else report.finalTraceNotStarted = true;
            try { await context.close(); } catch (error) { finalFailure('browser-close', error); }
        }
        if (server) {
            try { await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())); }
            catch (error) { finalFailure('server-close', error); }
        }
        if (manifestBytes) {
            try {
                report.bundleInventoryAfter = inventory(bundleRoot);
                assert.deepEqual(report.bundleInventoryAfter, inventoryBefore, 'Final published bundle differs from initial identity.');
                assert.deepEqual(fs.readFileSync(report.candidateManifest), manifestBytes, 'Final manifest differs from initial identity.');
            } catch (error) { finalFailure('candidate-identity', error); }
        }
        if (Object.values(report.diagnostics.counts).reduce((a, b) => a + b, 0) > 0) {
            outcomes[cases[13]] = { outcome: 'FAIL', error: 'Unexpected runtime/resource failures recorded.', observed: report.diagnostics }; process.exitCode = 1;
        }
        try {
            report.runnerSha256AtEnd = crypto.createHash('sha256').update(fs.readFileSync(__filename)).digest('hex');
            assert.equal(report.runnerSha256AtEnd, runnerSha256AtStart, 'Runner bytes changed during acceptance.');
        } catch (error) { finalFailure('runner-identity', error); }
        report.exit = process.exitCode || 0;
        flush();
        console.log(JSON.stringify({ counts: JSON.parse(fs.readFileSync(path.join(output, 'results.json'))).counts, exit: report.exit }));
    }
})();
