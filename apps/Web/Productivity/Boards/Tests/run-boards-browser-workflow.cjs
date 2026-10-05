// Actual published Boards UI, native pointer/keyboard, readonly IndexedDB evidence.
// No editing API, synthetic document, account fixture, or storage setup.
const fs = require('node:fs'), path = require('node:path'), http = require('node:http');
const assert = require('node:assert/strict'), crypto = require('node:crypto');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE);
const bundle = fs.realpathSync(process.argv[2]), output = path.resolve(process.argv[3]);
assert(!fs.existsSync(output), 'Fresh evidence directory required');
fs.mkdirSync(output, { recursive: true });
const sha = text => crypto.createHash('sha256').update(text).digest('hex');
const report = { scope: 'Boards local notebook native rich heading/range-bold/save/normal-close/fresh-process-reopen only; full Boards/Cards/study/cloud/parity not certified',
  sourceCommit: process.env.BOARDS_SOURCE_COMMIT || 'UNSELECTED-source-proposal', bundle, outcomes: [], closes: [], pointerInputs: [], runtimeEvents: [], processIds: [] };
const causes = [];
const add = error => { if (!causes.includes(error)) causes.push(error); };
let server, context, page, origin, firstError, saved, documentId;
const runnerPin = { bytes: fs.statSync(__filename).size, sha256: sha(fs.readFileSync(__filename)) };
function persist(name, value) { fs.writeFileSync(path.join(output, name), JSON.stringify(value, null, 2) + '\n'); }
async function ax() {
  return page.evaluate(async () => {
    const runtime = globalThis.getDotnetRuntime(0);
    const assembly = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return JSON.parse(assembly.NineToOne.Web.Program.ReadAccessibility());
  });
}
async function dirty() {
  return page.evaluate(async () => {
    const runtime = globalThis.getDotnetRuntime(0);
    const assembly = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return assembly.NineToOne.Web.Program.HasUnsavedChanges();
  });
}
async function wait(predicate, name) {
  const end = Date.now() + 30000;
  while (Date.now() < end) {
    if (firstError) throw firstError;
    const snapshot = await ax();
    if (predicate(snapshot)) return snapshot;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw Error('Actual native state did not settle: ' + name);
}
function one(snapshot, role, name) {
  const peers = snapshot.elements.filter(peer => peer.role === role && peer.name === name);
  assert.equal(peers.length, 1, 'One current native peer: ' + name);
  return peers[0];
}
function editor(snapshot) {
  // The real retained root has Edit semantics but no IValueProvider. Its native
  // peer is honestly classified unsupported; use its observed geometry only.
  const peers = snapshot.unsupportedPeers.filter(peer => peer.automationId === 'Write.Document.Surface' && peer.name === 'Document editor');
  assert.equal(peers.length, 1, 'One actual retained document surface');
  return peers[0];
}
async function point(peer, isEditor = false, withdrawOffscreen = false) {
  const snapshot = await ax();
  const current = isEditor ? editor(snapshot) : snapshot.elements.find(candidate => candidate.id === peer.id);
  assert(current && (isEditor || current.enabled), 'SAME current native input target');
  const canvas = await page.evaluate(() => {
    const canvases = [...document.querySelectorAll('#nine-to-one-root canvas')].filter(canvas => canvas.width > 0 && canvas.height > 0);
    if (canvases.length !== 1) throw Error('One actual painted canvas required');
    const bounds = canvases[0].getBoundingClientRect();
    return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height };
  });
  assert(snapshot.viewport && Math.abs(canvas.width - snapshot.viewport.width) <= 1 &&
    Math.abs(canvas.height - snapshot.viewport.height) <= 1, 'Native logical/physical viewport mapping');
  const bounds = current.bounds;
  assert(bounds && [bounds.x, bounds.y, bounds.width, bounds.height].every(Number.isFinite));
  const left = Math.max(0, bounds.x), top = Math.max(0, bounds.y);
  const right = Math.min(canvas.width, bounds.x + bounds.width), bottom = Math.min(canvas.height, bounds.y + bounds.height);
  if (withdrawOffscreen && !(right > left && bottom > top)) {
    report.pointerInputs.push({ name: 'Withdraw moved offscreen ' + peer.name, bounds });
    return false; // A native layout notification moved this SAME target; retry ordinary scrolling.
  }
  assert(right > left && bottom > top, 'Actual input target intersects viewport');
  const pointer = { x: canvas.x + (left + right) / 2, y: canvas.y + (top + bottom) / 2 };
  assert(await page.evaluate(({ x, y }) => !!document.elementFromPoint(x, y)?.closest('#nine-to-one-root'), pointer));
  report.pointerInputs.push({ name: peer.name, automationId: peer.automationId, bounds, pointer });
  await page.mouse.click(pointer.x, pointer.y);
  return true;
}
async function click(name) {
  // Real native focus can scroll the outer view. Use ordinary wheel input,
  // then reread the current peer; never invoke an offscreen automation button.
  for (let attempt = 0; attempt < 4; attempt++) {
    const snapshot = await wait(s => s.elements.some(peer => peer.role === 'button' && peer.name === name && peer.enabled), name);
    const peer = one(snapshot, 'button', name), bounds = peer.bounds;
    assert(bounds && snapshot.viewport, 'Actual native toolbar bounds required');
    if (bounds.y + bounds.height > 0 && bounds.y < snapshot.viewport.height) {
      if (await point(peer, false, true)) return;
      continue;
    }
    const canvas = await page.evaluate(() => {
      const list = [...document.querySelectorAll('#nine-to-one-root canvas')].filter(value => value.width > 0 && value.height > 0);
      if (list.length !== 1) throw Error('One actual painted canvas required');
      const rect = list[0].getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    });
    const pointer = { x: canvas.x + canvas.width / 2, y: canvas.y + canvas.height / 2 };
    const deltaY = bounds.y < 0 ? -600 : 600;
    report.pointerInputs.push({ name: 'Native wheel to ' + name, pointer, deltaY, originalBounds: bounds });
    await page.mouse.move(pointer.x, pointer.y); await page.mouse.wheel(0, deltaY);
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  throw Error('Actual native button remains outside viewport after wheel input: ' + name);
}
async function rows() {
  return page.evaluate(() => new Promise((resolve, reject) => {
    const request = indexedDB.open('nine-to-one-write', 1);
    request.onupgradeneeded = () => request.transaction.abort(); // Never create or seed the store.
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const db = request.result, transaction = db.transaction(['documents', 'history'], 'readonly');
      let documents, history;
      transaction.objectStore('documents').getAll().onsuccess = event => { documents = event.target.result; };
      transaction.objectStore('history').getAll().onsuccess = event => { history = event.target.result; };
      transaction.oncomplete = () => { db.close(); resolve({ documents, history }); };
      transaction.onabort = () => { db.close(); reject(transaction.error || Error('Actual readonly inspection aborted')); };
    };
  }));
}
function firstBlock(doc) { return doc.sections[0].pages[0].blocks[0]; }
function text(doc) { const block = firstBlock(doc); return block.runs.length ? block.runs.map(run => run.text).join('') : block.plainText; }
function checkRecord(record) {
  assert.equal(record.sha256, sha(record.documentJson), 'Exact canonical JSON integrity');
  const doc = JSON.parse(record.documentJson);
  assert.equal(doc.id, record.id); assert.equal(String(doc.version), record.version);
  assert(doc.sections[0].id && doc.sections[0].pages[0].id && firstBlock(doc).id);
  return doc;
}
function checkFormatting(doc) {
  const runs = firstBlock(doc).runs;
  assert.equal(text(doc), 'Alpha beta gamma', 'Original native space/content oracle');
  assert.equal(runs.length, 3, 'Original range formatting structure');
  assert.deepEqual(runs.map(run => run.text), ['Alpha ', 'beta', ' gamma']);
  assert.deepEqual(runs.map(run => run.bold), [false, true, false], 'Only actual selected beta is bold');
}
async function savedRevision(version, requireClean = true) {
  const end = Date.now() + 30000;
  while (Date.now() < end) {
    if (firstError) throw firstError;
    const stored = await rows();
    if (stored.documents.length === 1 && stored.documents[0].version === String(version)) {
      await wait(s => s.elements.some(peer => peer.role === 'button' && peer.name === 'Save' && peer.enabled), 'Original native save action settled');
      if (!requireClean || !await dirty()) return stored;
    }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw Error('Actual original save did not publish revision ' + version);
}
async function launch() {
  context = await chromium.launchPersistentContext(path.join(output, 'profile'), {
    executablePath: process.env.CHROMIUM_EXECUTABLE, headless: true, viewport: { width: 1440, height: 1000 },
    args: ['--no-sandbox', '--disable-dev-shm-usage', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader',
      '--disk-cache-size=1048576', '--media-cache-size=1048576'] });
  report.chromiumVersion = context.browser().version();
  page = await context.newPage();
  page.on('pageerror', error => { firstError ||= error; report.runtimeEvents.push({ kind: 'pageerror', text: String(error) }); });
  page.on('console', message => report.runtimeEvents.push({ kind: message.type(), text: message.text() }));
  page.on('requestfailed', request => report.runtimeEvents.push({ kind: 'requestfailed', url: request.url(), failure: request.failure() }));
  await page.goto(origin + '/#/app.boards', { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: 'New notebook', exact: true }).waitFor({ state: 'attached', timeout: 60000 });
}
async function close(label) {
  const accepted = await page.evaluate(async () => {
    const runtime = globalThis.getDotnetRuntime(0);
    const assembly = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return await assembly.NineToOne.Web.Program.CloseShell();
  });
  report.closes.push({ label, accepted });
  assert.equal(accepted, true, 'SAME original browser close accepted clean saved state');
  await context.close(); context = null;
}
(async () => {
  try {
    server = http.createServer((request, response) => {
      try {
        const pathname = new URL(request.url, 'http://127.0.0.1').pathname;
        const file = path.resolve(bundle, '.' + decodeURIComponent(pathname === '/' ? '/index.html' : pathname));
        assert(file.startsWith(bundle + path.sep) && fs.statSync(file).isFile());
        response.setHeader('Content-Type', ({ '.html': 'text/html', '.js': 'text/javascript', '.wasm': 'application/wasm',
          '.json': 'application/json', '.css': 'text/css' })[path.extname(file)] || 'application/octet-stream');
        fs.createReadStream(file).pipe(response);
      } catch { response.writeHead(404); response.end(); }
    });
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    origin = 'http://127.0.0.1:' + server.address().port; report.origin = origin;
    await launch();
    assert.deepEqual(await rows(), { documents: [], history: [] }, 'Fresh profile has no fabricated documents');
    await click('New notebook');
    const initial = await savedRevision(1, false);
    assert.equal(await dirty(), true, 'Canonical editor initialization remains unsaved after the acknowledged create revision');
    assert.equal(initial.history.length, 1);
    const initialDoc = checkRecord(initial.documents[0]); documentId = initialDoc.id;
    assert.equal(initialDoc.metadata['haven.product'], 'boards', 'Actual canonical notebook product marker');
    assert.equal(text(initialDoc), initialDoc.title);
    assert.equal(initialDoc.sections[0].pages[0].blocks.length, 2);
    const unchangedIntroduction = initialDoc.sections[0].pages[0].blocks[1];
    await point(editor(await ax()), true);
    await page.keyboard.press('Control+Home');
    await page.keyboard.press('Shift+End');
    // Normalize the canonical bold Heading using actual native formatting.
    await click('Bold');
    await point(editor(await ax()), true);
    await page.keyboard.press('Control+Home');
    await page.keyboard.press('Shift+End');
    await page.keyboard.type('Alpha beta gamma');
    await page.keyboard.press('Control+Home');
    for (let n = 0; n < 6; n++) await page.keyboard.press('ArrowRight');
    for (let n = 0; n < 4; n++) await page.keyboard.press('Shift+ArrowRight');
    await click('Bold');
    await click('Save');
    saved = await savedRevision(2);
    const authored = checkRecord(saved.documents[0]); checkFormatting(authored);
    assert.equal(authored.id, documentId); assert.equal(saved.history.length, 2);
    assert.equal(authored.metadata['haven.product'], 'boards');
    assert.deepEqual(authored.sections[0].pages[0].blocks[1], unchangedIntroduction, 'Unedited original introduction/IDs/attributes preserved');
    assert.equal(authored.sections[0].id, initialDoc.sections[0].id);
    assert.equal(authored.sections[0].pages[0].id, initialDoc.sections[0].pages[0].id);
    assert.equal(firstBlock(authored).id, firstBlock(initialDoc).id);
    for (const entry of saved.history) { checkRecord(entry); assert.equal(entry.documentId, documentId); }
    assert(saved.history.some(entry => entry.version === '1' && entry.documentJson === initial.documents[0].documentJson));
    assert(saved.history.some(entry => entry.version === '2' && entry.documentJson === saved.documents[0].documentJson));
    persist('boards-saved-state.json', saved);
    report.outcomes.push({ name: 'actual-create-range-format-save', state: 'PASS', id: documentId, version: 2, sha256: saved.documents[0].sha256 });
    await page.screenshot({ path: path.join(output, 'boards-saved.png') });
    await close('first-original-close');
    await launch();
    assert.deepEqual(await rows(), saved, 'Fresh Chromium process preserved exact entire current/history bytes');
    await click(authored.title);
    await wait(s => s.unsupportedPeers.some(peer => peer.automationId === 'Write.Document.Surface'), 'Fresh process mounted SAME actual editor');
    await wait(s => s.elements.some(peer => peer.role === 'button' && peer.name === 'Save' && peer.enabled), 'Fresh original open action settled');
    assert.equal(await dirty(), false, 'Fresh original open accepted clean state');
    assert.deepEqual(await rows(), saved, 'Native saved-document opening did not mutate stored canonical bytes');
    await page.screenshot({ path: path.join(output, 'boards-reopened.png') });
    // A real subsequent native edit proves the UI acquired the stored content,
    // rather than merely leaving an unrelated saved record in IndexedDB.
    await point(editor(await ax()), true);
    await page.keyboard.press('Control+Home');
    await page.keyboard.press('End');
    await page.keyboard.type(' delta');
    await click('Save');
    const extended = await savedRevision(3), current = checkRecord(extended.documents[0]);
    assert.equal(current.id, documentId); assert.equal(text(current), 'Alpha beta gamma delta');
    assert.equal(firstBlock(current).id, firstBlock(authored).id);
    assert.deepEqual(current.sections[0].pages[0].blocks[1], unchangedIntroduction, 'Fresh native continuation preserves neighbouring owner content');
    assert.equal(extended.history.length, 3);
    assert(extended.history.some(entry => entry.version === '2' && entry.documentJson === saved.documents[0].documentJson));
    persist('boards-reopened-state.json', extended);
    report.outcomes.push({ name: 'fresh-process-reopen-and-native-continuation', state: 'PASS', id: documentId, version: 3, sha256: extended.documents[0].sha256 });
    await close('second-original-close');
  } catch (error) { add(error); report.outcomes.push({ name: 'workflow', state: 'FAIL', error: String(error.stack || error) }); }
  finally {
    if (page && context) {
      try { persist('boards-last-accessibility.json', await ax()); } catch (error) { add(error); }
      try { persist('boards-last-storage.json', await rows()); } catch (error) { add(error); }
      try { await close('independent-original-cleanup'); } catch (error) { add(error); }
      if (context) {
        try { await context.close(); context = null; report.nativeContextClosedAfterRefusal = true; } catch (error) { add(error); }
      }
    }
    if (server) try { await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())); } catch (error) { add(error); }
    assert.deepEqual({ bytes: fs.statSync(__filename).size, sha256: sha(fs.readFileSync(__filename)) }, runnerPin, 'Exact runner custody');
    report.runnerPin = runnerPin; report.errors = causes.map(error => String(error.stack || error));
    persist('boards-results.json', report);
  }
  if (causes.length) throw new AggregateError(causes, 'Actual Boards workflow and independent cleanup causes retained.');
})().catch(error => { console.error(error); process.exitCode = 1; });
