// Genuine published native CUI/Picture engine + real PNG picker/IndexedDB/download/restart.
// No application module replacement, canonical model write, fake provider or DOM fabrication.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const net = require('node:net');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const decodePng = require('./png-oracle.cjs');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || '/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const sha = data => crypto.createHash('sha256').update(data).digest('hex');
const startingRunner = sha(fs.readFileSync(__filename));
const startingOracle = sha(fs.readFileSync(path.join(__dirname, 'png-oracle.cjs')));
const bundleRoot = path.resolve(process.argv[2] || '');
const output = path.resolve(process.argv[3] || '');
const fixturePath = path.resolve(process.argv[4] || '');
const names = ['create-real-png-local-document', 'canonical-crop', 'canonical-undo-redo', 'clean-save-close',
    'reopen-real-local-row', 'real-png-download-exact-pixels', 'clean-browser-process-restart',
    'real-presentation-and-pending-input-refusal', 'two-tab-cas-dirty-close-and-explicit-reload'];
const outcomes = Object.fromEntries(names.map(name => [name, { state: 'NOT_RUN' }]));
let context, page, secondPage, server, origin, documentId, tracingStarted = false;
let inventoryBefore, manifestBytes, beforeSourceBytes, afterSourceBytes, sourceBytes;
let snapshot3, allowDiscardPage;
const report = { scope: 'Nine genuine local Picture workflow groups; NOT full raster/vector/hybrid/donor/Files/provider/permission/hardware acceptance',
    outcomes, runnerSHA256AtStart: startingRunner, oracleSHA256AtStart: startingOracle,
    diagnostics: { counts: {}, samples: [], maximumSamples: 64 },
    launches: [], closedLaunches: [], readiness: [], snapshots: [], dialogs: [],
    integrity: { state: 'NOT_RUN' },
    interactionLimits: 'Real owner peer focus+Enter and immediate textbox fill; no Tab/pointer/touch/AT certification. Native Image peer/bounds observation is not direct on-screen pixel equality; downloaded actual owner-rendered PNG pixels are independently checked. Explicit browser reload discards a conflicting draft only with an observed native leave-page dialog; it is not in-app conflict merge.' };
let firstRuntimeError, fatalResolve;
const fatal = new Promise(resolve => { fatalResolve = resolve; });
const observed = new WeakSet();

function diagnostic(kind, value) {
    const text = String(value);
    report.diagnostics.counts[kind] = (report.diagnostics.counts[kind] || 0) + 1;
    if (report.diagnostics.samples.length < report.diagnostics.maximumSamples)
        report.diagnostics.samples.push({ kind, text: text.slice(0, 1800) });
    if (!firstRuntimeError) { firstRuntimeError = new Error(kind + ': ' + text.slice(0, 1800)); fatalResolve(firstRuntimeError); }
}

function watch(target) {
    if (observed.has(target)) return; observed.add(target);
    target.on('pageerror', error => {
        const text = [error.name, error.message, error.stack].filter(Boolean).join(': ') || String(error);
        diagnostic('pageError', text);
        if (error.name === 'ManagedError') diagnostic('managedRuntimeError', text);
    });
    target.on('console', event => {
        if (event.type() === 'error') diagnostic('consoleError', event.text());
        if (/Unhandled exception|MONO_WASM.*(?:error|assertion)|RuntimeError:|Aborted\(/i.test(event.text()))
            diagnostic('managedRuntimeError', event.text());
    });
    target.on('requestfailed', request => diagnostic('requestFailed', request.url() + ': ' + request.failure()?.errorText));
    target.on('response', response => { if (response.status() >= 400) diagnostic('httpError', response.status() + ' ' + response.url()); });
    target.on('request', request => {
        const url = new URL(request.url());
        if (['http:', 'https:'].includes(url.protocol) && url.origin !== origin)
            diagnostic('unexpectedExternalRequest', url.origin + url.pathname);
    });
    target.on('dialog', async dialog => {
        try {
            const permitted = dialog.type() === 'beforeunload' && target === allowDiscardPage;
            report.dialogs.push({ type: dialog.type(), controlledExplicitDiscard: permitted, page: target.url() });
            if (permitted) await dialog.accept();
            else { diagnostic('unexpectedDialog', dialog.type()); await dialog.dismiss(); }
        } catch (error) { diagnostic('dialogHandlingError', error.stack || String(error)); }
    });
}

function inventory(root) {
    const rows = [];
    function visit(directory) {
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            const file = path.join(directory, entry.name);
            assert(!entry.isSymbolicLink(), 'Published inventory contains no symlink aliases');
            if (entry.isDirectory()) visit(file);
            else { assert(entry.isFile()); const bytes = fs.readFileSync(file);
                rows.push({ path: path.relative(root, file).split(path.sep).join('/'), bytes: bytes.length, sha256: sha(bytes) }); }
        }
    }
    visit(root); return rows.sort((a, b) => a.path.localeCompare(b.path));
}

function counts() {
    const rows = Object.values(outcomes);
    return { discovered: names.length, executed: rows.filter(row => row.state !== 'NOT_RUN').length,
        passed: rows.filter(row => row.state === 'PASS').length, failed: rows.filter(row => row.state === 'FAIL').length,
        notRun: rows.filter(row => row.state === 'NOT_RUN').length };
}
function flush() { fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ ...report, counts: counts() }, null, 2)); }
async function test(name, execute) {
    try {
        if (firstRuntimeError) throw firstRuntimeError;
        const result = await Promise.race([execute(), fatal.then(error => { throw error; })]);
        outcomes[name] = { state: 'PASS', observed: result }; flush();
    } catch (error) { outcomes[name] = { state: 'FAIL', error: error.stack || String(error) }; flush(); throw error; }
}

async function readStore(target = page) {
    return target.evaluate(() => new Promise((resolve, reject) => {
        const request = indexedDB.open('9to1-picture-local-v1', 1);
        request.onupgradeneeded = () => request.transaction.abort(); // Never create substitute test storage.
        request.onerror = () => reject(request.error);
        request.onsuccess = () => {
            const db = request.result, values = {};
            const tx = db.transaction(['documents', 'summaries'], 'readonly');
            for (const name of ['documents', 'summaries']) {
                const read = tx.objectStore(name).getAll();
                read.onsuccess = () => { values[name] = read.result.sort((a, b) => a.documentId.localeCompare(b.documentId)); };
                read.onerror = () => reject(read.error);
            }
            tx.oncomplete = () => { db.close(); resolve(values); };
            tx.onerror = () => { db.close(); reject(tx.error); };
            tx.onabort = () => { db.close(); reject(tx.error); };
        };
    }));
}
function canonical(bundle) { return JSON.parse(bundle.documentJson); }
function checkPackage(store, revision, width, height) {
    assert.equal(store.documents.length, 1); assert.equal(store.summaries.length, 1);
    const bundle = store.documents[0], doc = canonical(bundle), summary = store.summaries[0];
    assert.equal(bundle.documentId, documentId); assert.equal(doc.documentId, documentId);
    assert.equal(bundle.revision, revision); assert.equal(doc.revision, revision); assert.equal(summary.revision, revision);
    assert.equal(summary.documentId, documentId); assert.equal(summary.name, bundle.name);
    assert.equal(bundle.name, path.basename(fixturePath)); assert.equal(doc.schemaVersion, 1);
    assert.equal(doc.canvasWidth, width); assert.equal(doc.canvasHeight, height);
    assert.equal(doc.fileId, null); assert.match(doc.sourcePath, /\/9to1-picture-local\/sources\/[0-9a-f]{32}\.png$/);
    assert.equal(doc.displayName, path.basename(doc.sourcePath, '.png'));
    assert.deepEqual(Buffer.from(bundle.sourceBase64, 'base64'), sourceBytes);
    assert.match(doc.sourceRevision, /^[0-9a-f]{64}$/i);
    assert.deepEqual(Buffer.from(doc.sourceRevision, 'hex'), Buffer.from(sha(sourceBytes), 'hex'));
    for (const encoded of [...bundle.undo, ...bundle.redo]) {
        const old = JSON.parse(encoded);
        assert.equal(old.documentId, documentId); assert.equal(old.sourcePath, doc.sourcePath);
        assert.equal(old.sourceRevision, doc.sourceRevision); assert.equal(old.fileId, null);
    }
    return bundle;
}
async function saved(revision, target = page) {
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        if (firstRuntimeError) throw firstRuntimeError;
        const store = await readStore(target);
        if (store.documents.find(item => item.documentId === documentId)?.revision === revision) return store;
        await target.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)));
    }
    throw new Error('Actual durable canonical revision not observed: ' + revision);
}
async function ownerCompleted(revision, width, height, target = page) {
    const handle = await target.waitForFunction(expected => {
        const root = document.querySelector('#native-control-semantics');
        const importButton = root?.querySelector('button[aria-label="Import static PNG"]');
        const identity = [...(root?.querySelectorAll('span') || [])].find(node =>
            node.textContent.startsWith('Document ' + expected.id + ' · revision ' + expected.revision + ' · ' + expected.width + ' × ' + expected.height + ' · '));
        if (!importButton || importButton.disabled || !identity) return false;
        return { identity: identity.textContent, ownerNotBusy: true, page: location.href };
    }, { id: documentId, revision, width, height }, { timeout: 15000 });
    report.readiness.push(await handle.jsonValue()); await handle.dispose();
}
async function acknowledged(revision, width, height, target = page) {
    const store = await saved(revision, target); await ownerCompleted(revision, width, height, target);
    checkPackage(store, revision, width, height); report.snapshots.push({ revision, store }); return store;
}
async function owner(target = page) {
    return target.evaluate(async () => {
        const api = (await globalThis.getDotnetRuntime(0).getAssemblyExports('NineToOne.Web')).NineToOne.Web.Program;
        return { unsaved: api.HasUnsavedChanges(), accessibility: JSON.parse(api.ReadAccessibility()) };
    });
}
async function button(name, target = page) {
    const control = target.getByRole('button', { name, exact: true });
    await control.waitFor({ state: 'attached' }); assert.equal(await control.isEnabled(), true, name + ' is actually available');
    await control.focus(); await control.press('Enter');
}
async function field(name, value, target = page) {
    const control = target.getByRole('textbox', { name, exact: true });
    await control.fill(value); assert.equal(await control.inputValue(), value, 'Immediate genuine owner input is retained');
}
async function code(value, target = page) {
    await target.waitForFunction(value => document.querySelector('#browser-status')?.dataset.code === value, value, { timeout: 15000 });
}
async function nativeText(value, target = page) {
    await target.waitForFunction(value => document.querySelector('#native-control-semantics')?.textContent.includes(value), value, { timeout: 15000 });
}
async function emptyOwner(target = page) {
    await target.waitForFunction(() => {
        const root = document.querySelector('#native-control-semantics');
        const importButton = root?.querySelector('button[aria-label="Import static PNG"]');
        return !!importButton && !importButton.disabled && [...root.querySelectorAll('span')].some(node => node.textContent === 'No document open');
    }, null, { timeout: 15000 });
    assert.equal((await owner(target)).unsaved, false);
}
async function imagePeer(target = page) {
    const snapshot = (await owner(target)).accessibility;
    const image = snapshot.unsupportedPeers.find(peer => peer.name === 'Canonical Picture raster preview' && peer.controlType === 'Image');
    assert(image && image.bounds && image.bounds.width > 0 && image.bounds.height > 0, 'Actual native Image peer and nonzero layout bounds');
    return image;
}

async function startBrowser() {
    context = await chromium.launchPersistentContext(path.join(output, 'profile'), { executablePath: process.env.CHROMIUM_EXECUTABLE || '/usr/bin/chromium',
        headless: true, viewport: { width: 1100, height: 850 }, acceptDownloads: true,
        args: ['--no-sandbox', '--disable-dev-shm-usage'] });
    context.on('page', watch); context.pages().forEach(watch);
    const initialUrls = context.pages().map(target => target.url());
    assert(initialUrls.every(url => url === 'about:blank'), 'Only deliberate clean blank-page restart is covered');
    page = await context.newPage(); watch(page);
    const cdp = await context.browser().newBrowserCDPSession();
    const processes = await cdp.send('SystemInfo.getProcessInfo'); await cdp.detach();
    const browsers = processes.processInfo.filter(item => item.type === 'browser'); assert.equal(browsers.length, 1);
    report.launches.push({ number: report.launches.length + 1, initialUrls, browserPID: browsers[0].id, browserVersion: context.browser().version() });
    await context.tracing.start({ screenshots: true, snapshots: true, sources: true }); tracingStarted = true;
}
async function finishTrace(name) {
    assert(tracingStarted); await context.tracing.stop({ path: path.join(output, name) }); tracingStarted = false;
}
async function bindProof(port) {
    const probe = net.createServer();
    await new Promise((resolve, reject) => { probe.once('error', reject); probe.listen(port, '127.0.0.1', resolve); });
    await new Promise((resolve, reject) => probe.close(error => error ? reject(error) : resolve()));
    return true;
}

(async () => {
    let exit = 1;
    if (!process.argv[2] || !process.argv[3] || !process.argv[4] || fs.existsSync(output))
        throw new Error('Explicit bundle/fresh output/actual PNG fixture arguments required');
    fs.mkdirSync(output, { recursive: true }); flush();
    try {
        assert.equal(process.env.B3_PICTURE_GUI_GRANTED, 'granted', 'Separate root exclusive GUI/owned-family/resource grant required');
        const manifestPath = process.env.B3_PICTURE_CANDIDATE_MANIFEST;
        assert(manifestPath && process.env.B3_PICTURE_CANDIDATE_MANIFEST_SHA256);
        manifestBytes = fs.readFileSync(manifestPath); assert.equal(sha(manifestBytes), process.env.B3_PICTURE_CANDIDATE_MANIFEST_SHA256);
        const manifest = JSON.parse(manifestBytes); assert.match(manifest.sourceCommit, /^[a-f0-9]{40}$/);
        assert.equal(manifest.sourceCommitAfter, manifest.sourceCommit); assert.equal(manifest.exitCode, 0);
        assert.deepEqual(manifest.addedSourceInputs, []); assert.deepEqual(manifest.removedSourceInputs, []); assert.deepEqual(manifest.changedSourceInputs, []);
        assert(manifest.sourceCustodyRoots.includes('9to1 Workspace/Picture'), 'Publisher must capture unchanged original Picture authority');
        const beforePath = process.env.B3_PICTURE_SOURCE_BEFORE, afterPath = process.env.B3_PICTURE_SOURCE_AFTER;
        assert(beforePath && afterPath && process.env.B3_PICTURE_SOURCE_BEFORE_SHA256 && process.env.B3_PICTURE_SOURCE_AFTER_SHA256);
        beforeSourceBytes = fs.readFileSync(beforePath); afterSourceBytes = fs.readFileSync(afterPath);
        assert.equal(sha(beforeSourceBytes), process.env.B3_PICTURE_SOURCE_BEFORE_SHA256);
        assert.equal(sha(afterSourceBytes), process.env.B3_PICTURE_SOURCE_AFTER_SHA256);
        const sourceBefore = JSON.parse(beforeSourceBytes), sourceAfter = JSON.parse(afterSourceBytes);
        assert.deepEqual(sourceBefore, sourceAfter); assert.equal(sourceBefore.length, manifest.sourceInputs); assert.equal(sourceAfter.length, manifest.sourceInputsAfter);
        const requiredSources = [ 'apps/Web/Picture/PictureBrowserFeature.cs', 'apps/Web/Picture/PictureBrowserSession.cs',
            'apps/Web/Picture/Picture.cui', 'apps/Web/Picture/Engine/NineToOne.Picture.BrowserEngine.csproj',
            'apps/Web/Media/picture-browser.js', 'apps/Web/Media/BrowserPictureMedia.cs', 'apps/Web/Media/IPictureBrowserMedia.cs',
            'apps/Web/NineToOne.Web.csproj', 'apps/Web/BrowserFeatureComposition.cs', 'apps/Web/wwwroot/main.js',
            '9to1 Workspace/Picture/PictureDocument.cs', '9to1 Workspace/Picture/PictureCropService.cs', '9to1 Workspace/Picture/ImageViewportState.cs' ];
        for (const required of requiredSources) assert.equal(sourceBefore.filter(row => row.path === required).length, 1, 'Exact required linked/current input ' + required);
        for (const required of requiredSources.filter(name => name.startsWith('9to1 Workspace/Picture/')))
            assert(manifest.requiredLinkedOwnerCuts.includes(required), 'Required owner cut cannot be silently omitted');
        const expectedAssets = manifest.publishFiles.map(row => ({ path: row.path, bytes: row.bytes, sha256: row.sha256 })).sort((a, b) => a.path.localeCompare(b.path));
        inventoryBefore = inventory(bundleRoot); assert.deepEqual(inventoryBefore, expectedAssets);
        assert.equal(inventoryBefore.length, manifest.fileCount); assert.equal(inventoryBefore.reduce((sum, row) => sum + row.bytes, 0), manifest.totalBytes);
        const pictureJS = fs.readFileSync(path.join(bundleRoot, 'picture-browser.js'));
        assert.equal(sha(pictureJS), '2e3f39261df532c5007380fef4027d6e1de3a99f0ff1ade1205d692705b24fe5', 'Reviewed genuine fault-brand module required; no overlay');
        assert.equal(sourceBefore.find(row => row.path === 'apps/Web/Media/picture-browser.js').sha256, sha(pictureJS));
        report.candidate = { sourceCommit: manifest.sourceCommit, manifestSHA256: sha(manifestBytes), sourceBeforeSHA256: sha(beforeSourceBytes),
            sourceAfterSHA256: sha(afterSourceBytes), assets: inventoryBefore.length, assetBytes: manifest.totalBytes };
        report.bundleInventoryBefore = inventoryBefore;
        sourceBytes = fs.readFileSync(fixturePath); assert.equal(sha(sourceBytes), 'a17e304d0a333065202cf77c656d7bc86abdd37237b407a0a5cb870d33870297');
        const sourcePng = decodePng(sourceBytes); assert.equal(sourcePng.width, 8); assert.equal(sourcePng.height, 6);
        report.fixture = { bytes: sourceBytes.length, sha256: sha(sourceBytes), width: 8, height: 6, name: path.basename(fixturePath) };
        server = http.createServer((request, response) => {
            try {
                if (!['GET', 'HEAD'].includes(request.method)) { response.writeHead(405); response.end(); return; }
                const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname);
                const file = path.resolve(bundleRoot, '.' + (pathname === '/' ? '/index.html' : pathname));
                if (!file.startsWith(bundleRoot + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { response.writeHead(404); response.end(); return; }
                const contentType = /\.m?js$/.test(file) ? 'text/javascript' : file.endsWith('.html') ? 'text/html'
                    : file.endsWith('.wasm') ? 'application/wasm' : file.endsWith('.json') ? 'application/json' : 'application/octet-stream';
                response.writeHead(200, { 'Content-Type': contentType, 'Cache-Control': 'no-store' });
                if (request.method === 'HEAD') response.end();
                else { const stream = fs.createReadStream(file); stream.on('error', error => { diagnostic('serverReadError', error.message); response.destroy(error); }); stream.pipe(response); }
            } catch (error) { diagnostic('serverRequestError', error.message); response.writeHead(500); response.end(); }
        });
        await new Promise((resolve, reject) => { server.once('error', reject); server.listen(Number(process.env.B3_PORT || 0), '127.0.0.1', resolve); });
        origin = 'http://127.0.0.1:' + server.address().port; report.origin = origin;
        await startBrowser(); await page.goto(origin + '/#/app.picture');
        await page.getByRole('button', { name: 'Import static PNG', exact: true }).waitFor({ state: 'attached', timeout: 60000 });
        await code('Ready'); await emptyOwner();
        await test(names[0], async () => {
            const chooser = page.waitForEvent('filechooser'); await button('Import static PNG'); await (await chooser).setFiles(fixturePath);
            await nativeText('Saved in this browser'); const store = await readStore(); assert.equal(store.documents.length, 1);
            documentId = store.documents[0].documentId; assert.match(documentId, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
            const saved0 = await acknowledged(0, 8, 6); const doc = canonical(saved0.documents[0]);
            assert.deepEqual(doc.operations, []); assert.deepEqual(saved0.documents[0].undo, []); assert.deepEqual(saved0.documents[0].redo, []);
            assert.equal((await owner()).unsaved, false); const image = await imagePeer();
            await page.screenshot({ path: path.join(output, 'picture-original.png') });
            return { documentId, revision: 0, realNativeImage: image, canonical: doc };
        });
        await test(names[1], async () => {
            await field('Crop left pixel', '1'); await field('Crop top pixel', '2');
            await field('Crop width pixels', '5'); await field('Crop height pixels', '3'); await button('Apply crop');
            const store = await acknowledged(1, 5, 3); const bundle = store.documents[0];
            assert.deepEqual(canonical(bundle).operations, [{ type: 'crop', x: 1, y: 2, width: 5, height: 3 }]);
            assert.equal(bundle.undo.length, 1); assert.equal(JSON.parse(bundle.undo[0]).revision, 0); assert.deepEqual(bundle.redo, []);
            assert.equal((await owner()).unsaved, false); return { documentId, revision: 1, crop: canonical(bundle).operations[0] };
        });
        await test(names[2], async () => {
            assert.equal(await page.getByRole('button', { name: 'Undo', exact: true }).isEnabled(), true); await button('Undo');
            const undone = await acknowledged(2, 8, 6); assert.deepEqual(canonical(undone.documents[0]).operations, []);
            assert.equal(undone.documents[0].undo.length, 0); assert.equal(undone.documents[0].redo.length, 1);
            assert.equal(JSON.parse(undone.documents[0].redo[0]).revision, 1);
            assert.equal(await page.getByRole('button', { name: 'Redo', exact: true }).isEnabled(), true); await button('Redo');
            snapshot3 = await acknowledged(3, 5, 3); assert.deepEqual(canonical(snapshot3.documents[0]).operations, [{ type: 'crop', x: 1, y: 2, width: 5, height: 3 }]);
            assert.equal(snapshot3.documents[0].undo.length, 1); assert.equal(JSON.parse(snapshot3.documents[0].undo[0]).revision, 2);
            assert.deepEqual(snapshot3.documents[0].redo, []); return { documentId, undoRevision: 2, redoRevision: 3 };
        });
        await test(names[3], async () => {
            await button('Save locally'); await ownerCompleted(3, 5, 3); assert.deepEqual(await readStore(), snapshot3);
            assert.equal((await owner()).unsaved, false); await button('Save and close'); await emptyOwner();
            assert.deepEqual(await readStore(), snapshot3); return { savedRevision: 3, actualClosed: true, packageUnchanged: true };
        });
        await test(names[4], async () => {
            await button(path.basename(fixturePath) + ' · revision 3'); await ownerCompleted(3, 5, 3);
            assert.deepEqual(await readStore(), snapshot3); assert.equal((await owner()).unsaved, false);
            return { documentId, revision: 3, sameOriginalSource: true, realNativeImage: await imagePeer() };
        });
        await test(names[5], async () => {
            const downloading = page.waitForEvent('download'); await button('Export PNG removing all metadata'); const download = await downloading;
            assert.equal(await download.failure(), null); assert.equal(download.suggestedFilename(), 'Picture-' + documentId.replaceAll('-', '') + '.png');
            const exportedPath = path.join(output, 'actual-crop-remove-all.png'); await download.saveAs(exportedPath);
            const bytes = fs.readFileSync(exportedPath), decoded = decodePng(bytes);
            assert.equal(decoded.width, 5); assert.equal(decoded.height, 3);
            const expectedPixels = Buffer.alloc(5 * 3 * 4);
            for (let row = 0; row < 3; row++) sourcePng.rgba.copy(expectedPixels, row * 5 * 4, ((row + 2) * 8 + 1) * 4, ((row + 2) * 8 + 6) * 4);
            assert.deepEqual(decoded.rgba, expectedPixels, 'Actual downloaded owner PNG equals exact original integer-crop RGBA including alpha');
            assert(!decoded.chunks.some(type => ['tEXt', 'zTXt', 'iTXt', 'eXIf'].includes(type)), 'RemoveAll contains no removable PNG text/XMP/EXIF');
            await ownerCompleted(3, 5, 3); assert.deepEqual(await readStore(), snapshot3); assert.equal((await owner()).unsaved, false);
            return { pixelsChecked: 15, rgbaBytes: expectedPixels.length, rgbaSHA256: sha(expectedPixels), actualPngSHA256: sha(bytes), canonicalPackageUnchanged: true };
        });
        await test(names[6], async () => {
            await ownerCompleted(3, 5, 3); assert.equal((await owner()).unsaved, false);
            for (const controlledPage of context.pages()) if (controlledPage.url() !== 'about:blank') {
                assert(controlledPage.url().startsWith(origin + '/')); assert.equal((await owner(controlledPage)).unsaved, false);
                await controlledPage.goto('about:blank');
            }
            await finishTrace('picture-first-process.zip'); const firstPID = report.launches[0].browserPID;
            await context.close(); report.closedLaunches.push({ number: 1, browserPID: firstPID, contextCloseResolved: true });
            context = null; await startBrowser(); assert.notEqual(report.launches[1].browserPID, firstPID);
            await page.goto(origin + '/#/app.picture?entityType=PictureDocument&entityId=' + documentId); await code('Ready'); await ownerCompleted(3, 5, 3);
            assert.deepEqual(await readStore(), snapshot3); assert.equal((await owner()).unsaved, false);
            return { firstPID, secondPID: report.launches[1].browserPID, sameCanonicalID: documentId, sameRevision: 3, packageByteObjectEquality: true,
                qualification: 'Deliberately clean blank navigation covers controlled process restart; no automatic tab restoration/BFCache claim' };
        });
        await test(names[7], async () => {
            const canonicalUrl = origin + '/#/app.picture?entityType=PictureDocument&entityId=' + documentId;
            const before = await readStore();
            for (const deepLink of ['', 'unsupported']) {
                await page.goto(canonicalUrl); await code('Ready'); await ownerCompleted(3, 5, 3);
                const invalid = canonicalUrl + '&deepLink=' + deepLink; await page.goto(invalid); await code('InvalidArgument'); assert.equal(page.url(), invalid);
                await ownerCompleted(3, 5, 3); assert.deepEqual(await readStore(), before);
            }
            await page.goto(canonicalUrl); await code('Ready'); await ownerCompleted(3, 5, 3);
            const cropX = page.getByRole('textbox', { name: 'Crop left pixel', exact: true }); const originalX = await cropX.inputValue();
            const pendingX = originalX === '2' ? '1' : '2'; await field('Crop left pixel', pendingX); assert.equal((await owner()).unsaved, true);
            const rootUrl = origin + '/#/app.picture'; await page.goto(rootUrl); await code('UnsavedInput'); assert.equal(page.url(), rootUrl);
            assert.equal(await cropX.inputValue(), pendingX); assert.deepEqual(await readStore(), before);
            await ownerCompleted(3, 5, 3); await field('Crop left pixel', originalX); assert.equal((await owner()).unsaved, false);
            await page.goto(canonicalUrl); await code('Ready'); await ownerCompleted(3, 5, 3); assert.deepEqual(await readStore(), before);
            return { refusedDeepLinks: ['', 'unsupported'], pendingInputRetainedBeforeExplicitRestoration: true, sameCanonicalRevision: 3 };
        });
        await test(names[8], async () => {
            secondPage = await context.newPage(); watch(secondPage);
            const route = origin + '/#/app.picture?entityType=PictureDocument&entityId=' + documentId;
            await secondPage.goto(route); await code('Ready', secondPage); await ownerCompleted(3, 5, 3, secondPage);
            await field('Resize width pixels', '6'); await field('Resize height pixels', '3'); await button('Apply resize');
            const winner = await acknowledged(4, 6, 3); assert.deepEqual(canonical(winner.documents[0]).operations,
                [{ type: 'crop', x: 1, y: 2, width: 5, height: 3 }, { type: 'resize', width: 6, height: 3 }]);
            await button('Rotate clockwise', secondPage); await nativeText('RevisionConflict', secondPage); await ownerCompleted(4, 3, 5, secondPage);
            assert.equal((await owner(secondPage)).unsaved, true); assert.deepEqual(await readStore(secondPage), winner);
            await button('Save and close', secondPage); await nativeText('RevisionConflict', secondPage); await ownerCompleted(4, 3, 5, secondPage);
            assert.equal((await owner(secondPage)).unsaved, true); assert.deepEqual(await readStore(secondPage), winner);
            const priorDialogs = report.dialogs.length; allowDiscardPage = secondPage;
            try { await secondPage.reload(); } finally { allowDiscardPage = undefined; }
            assert(report.dialogs.slice(priorDialogs).some(item => item.type === 'beforeunload' && item.controlledExplicitDiscard), 'Actual native leave-page confirmation observed for deliberate loser reload');
            await code('Ready', secondPage); await ownerCompleted(4, 6, 3, secondPage);
            assert.equal((await owner(secondPage)).unsaved, false); assert.deepEqual(await readStore(secondPage), winner);
            assert.equal((await owner()).unsaved, false);
            return { winnerRevision: 4, winnerCanvas: [6, 3], loserRevision: 4, retainedLoserCanvasBeforeConsent: [3, 5],
                actualBothStoresUnchangedOnConflict: true, failedCloseRetainedDirty: true, explicitBrowserDiscardReloadObserved: true,
                inAppConflictMerge: 'NOT_RUN/not implemented by this slice', quotaAndTransactionAbort: 'NOT_RUN/separate genuine calibration and two-store invariance gates required' };
        });
        assert.equal(Object.values(report.diagnostics.counts).reduce((a, b) => a + b, 0), 0);
        report.integrity.state = 'PASS'; exit = 0;
    } catch (error) { report.failure = error.stack || String(error); exit = 1; }
    finally {
        function failure(stage, error) { (report.finalizationFailures ||= []).push({ stage, error: error.stack || String(error) }); report.integrity.state = 'FAIL'; exit = 1; }
        if (context) {
            if (tracingStarted) try { await finishTrace('picture-final-process.zip'); } catch (error) { failure('trace-finalization', error); }
            try { await context.close(); report.contextClosed = true;
                report.closedLaunches.push({ number: report.launches.length, browserPID: report.launches.at(-1)?.browserPID, contextCloseResolved: true });
            } catch (error) { failure('browser-context-close', error); }
        }
        if (server) {
            const port = server.address()?.port;
            try { await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())); report.serverClosed = true; }
            catch (error) { failure('server-close', error); }
            if (port) try { report.portRebind = await bindProof(port); } catch (error) { failure('port-rebind', error); }
        }
        try {
            report.runnerSHA256AtEnd = sha(fs.readFileSync(__filename)); assert.equal(report.runnerSHA256AtEnd, startingRunner);
            report.oracleSHA256AtEnd = sha(fs.readFileSync(path.join(__dirname, 'png-oracle.cjs'))); assert.equal(report.oracleSHA256AtEnd, startingOracle);
            if (sourceBytes) assert.equal(sha(fs.readFileSync(fixturePath)), sha(sourceBytes));
            else report.fixtureCustody = 'NOT_LOADED_BECAUSE_PREREQUISITE_FAILED';
            if (inventoryBefore) { report.bundleInventoryAfter = inventory(bundleRoot); assert.deepEqual(report.bundleInventoryAfter, inventoryBefore); }
            if (manifestBytes) assert.deepEqual(fs.readFileSync(process.env.B3_PICTURE_CANDIDATE_MANIFEST), manifestBytes);
            if (beforeSourceBytes) assert.deepEqual(fs.readFileSync(process.env.B3_PICTURE_SOURCE_BEFORE), beforeSourceBytes);
            if (afterSourceBytes) assert.deepEqual(fs.readFileSync(process.env.B3_PICTURE_SOURCE_AFTER), afterSourceBytes);
        } catch (error) { failure('source-and-candidate-final-custody', error); }
        if (firstRuntimeError) { report.integrity.state = 'FAIL'; exit = 1; }
        if (exit || counts().passed !== names.length) report.integrity.state = 'FAIL';
        report.exit = exit; report.acceptance = exit === 0 && report.integrity.state === 'PASS' ? 'PASS_SCOPED_NINE' : 'FAIL_OR_INCOMPLETE';
        report.familyCustody = 'This runner has no signalling authority. Root-owned reviewed launcher must prove both sequential browser families gone/held pidFDs/ECHILD before accepting release.';
        flush(); console.log(JSON.stringify({ counts: counts(), acceptance: report.acceptance, exit })); process.exitCode = exit;
    }
})().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
