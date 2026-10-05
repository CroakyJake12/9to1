// Genuine published Present UI and real browser IndexedDB; no mocked repository or direct editing API.
const fs = require('node:fs'), path = require('node:path'), http = require('node:http');
const assert = require('node:assert/strict'), crypto = require('node:crypto');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE);
const bundle = fs.realpathSync(process.argv[2]), output = path.resolve(process.argv[3]);
assert(!fs.existsSync(output), 'Fresh evidence directory required');
fs.mkdirSync(output, { recursive: true });
const report = { scope: 'Present text/shape/notes create-save-normal-close-fresh-process-reopen only; media/format/auth/full parity not certified',
  outcomes: [], diagnostics: [], closes: [], pointerInputs: [] };
let server, context, page, saved, firstError;
const causes = [];
const add = error => { if (!causes.includes(error)) causes.push(error); };
const hash = text => crypto.createHash('sha256').update(text).digest('hex');
const flush = () => fs.writeFileSync(path.join(output, 'present-results.json'), JSON.stringify(report, null, 2));
async function ax() {
  return page.evaluate(async () => {
    const runtime = globalThis.getDotnetRuntime(0);
    const assembly = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return JSON.parse(assembly.NineToOne.Web.Program.ReadAccessibility());
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
async function point(peer) {
  const snapshot = await ax();
  const current = snapshot.elements.find(candidate => candidate.id === peer.id);
  assert(current && current.enabled, 'SAME current enabled native peer');
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
  assert(right > left && bottom > top, 'Actual input target intersects viewport');
  const pointer = { x: canvas.x + (left + right) / 2, y: canvas.y + (top + bottom) / 2 };
  assert(await page.evaluate(({ x, y }) => !!document.elementFromPoint(x, y)?.closest('#nine-to-one-root'), pointer));
  report.pointerInputs.push({ name: peer.name, bounds, pointer });
  await page.mouse.click(pointer.x, pointer.y);
}
async function click(name) {
  const snapshot = await wait(s => s.elements.some(peer => peer.role === 'button' && peer.name === name && peer.enabled), name);
  await point(one(snapshot, 'button', name));
}
async function rows() {
  return page.evaluate(() => new Promise((resolve, reject) => {
    const request = indexedDB.open('nine-to-one-present', 1);
    request.onupgradeneeded = () => request.transaction.abort(); // Read-only inspection never creates the store.
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const db = request.result, transaction = db.transaction(['documents'], 'readonly');
      let result;
      transaction.objectStore('documents').getAll().onsuccess = event => { result = event.target.result; };
      transaction.oncomplete = () => { db.close(); resolve(result); };
      transaction.onabort = () => { db.close(); reject(transaction.error || Error('Actual inspection aborted')); };
    };
  }));
}
async function launch(fragment) {
  context = await chromium.launchPersistentContext(path.join(output, 'profile'), {
    executablePath: process.env.CHROMIUM_EXECUTABLE, headless: true, viewport: { width: 1440, height: 1000 },
    args: ['--no-sandbox', '--disable-dev-shm-usage', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  page = await context.newPage();
  page.on('pageerror', error => { firstError ||= error; report.diagnostics.push(String(error)); });
  await page.goto(origin + '/' + fragment, { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: 'New presentation', exact: true }).waitFor({ state: 'attached', timeout: 60000 });
}
async function close(label) {
  // Join SAME real app/surface/storage close while the native dispatcher is still alive.
  const accepted = await page.evaluate(async () => {
    const runtime = globalThis.getDotnetRuntime(0);
    const assembly = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return await assembly.NineToOne.Web.Program.CloseShell();
  });
  assert.equal(accepted, true, 'Original browser close accepted clean saved state');
  await context.close(); context = null; report.closes.push(label);
}
let origin;
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
    origin = 'http://127.0.0.1:' + server.address().port;
    await launch('#/app.present');
    assert.equal((await rows()).length, 0, 'Fresh real profile contains no fabricated document');
    await click('New presentation');
    await wait(s => s.elements.some(peer => peer.role === 'button' && peer.name === 'Save' && peer.enabled), 'Owning initial save settled');
    const initial = await rows(); assert.equal(initial.length, 1); assert.equal(initial[0].current.version, 1); assert.equal(initial[0].previous, null);
    await click('Add text'); await click('Add shape');
    const notes = one(await ax(), 'textbox', 'Speaker notes');
    await point(notes); await page.keyboard.press('Control+A'); await page.keyboard.type('Alpha beta gamma\nSpeaker notes preserved.');
    await click('Add slide');
    await click('Save');
    const savedDeadline = Date.now() + 30000;
    while ((await rows())[0]?.current?.version !== 2) {
      if (firstError) throw firstError;
      if (Date.now() >= savedDeadline) throw Error('Actual durable save receipt did not arrive.');
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    await wait(s => s.elements.some(peer => peer.role === 'button' && peer.name === 'Save' && peer.enabled), 'Owning edited save settled');
    const records = await rows(); assert.equal(records.length, 1); saved = records[0];
    assert.equal(saved.current.sha256, hash(saved.current.json));
    assert.equal(saved.previous.sha256, hash(saved.previous.json));
    assert.equal(saved.current.version, 2); assert.equal(saved.previous.version, 1);
    const document = JSON.parse(saved.current.json), previous = JSON.parse(saved.previous.json);
    assert.equal(document.id, saved.id); assert.equal(document.version, 2); assert.equal(document.schemaVersion, 3);
    assert.equal(document.slides.length, previous.slides.length + 1);
    assert(document.slides.some(slide => slide.elements.some(element => element.kind === 2)), 'Actual owning shape persisted');
    assert(document.slides.some(slide => slide.speakerNotes === 'Alpha beta gamma\nSpeaker notes preserved.'));
    assert.notDeepEqual(document.slides, previous.slides);
    report.outcomes.push({ name: 'actual-create-edit-save', state: 'PASS', id: saved.id, revision: document.version, sha256: saved.current.sha256 });
    await page.screenshot({ path: path.join(output, 'present-saved.png') });
    await close('first-original-close');
    await launch('#/app.present?entityType=PresentDocument&entityId=' + encodeURIComponent(saved.id));
    await wait(s => s.elements.some(peer => peer.role === 'button' && peer.name === 'Save' && peer.enabled), 'Fresh process opened saved canonical document');
    const reopened = await rows(); assert.deepEqual(reopened, [saved], 'Fresh process preserved exact current and previous bytes');
    assert((await ax()).elements.some(peer => peer.role === 'textbox' && peer.name === 'Speaker notes' &&
      peer.value === 'Alpha beta gamma\nSpeaker notes preserved.'), 'Actual reopened native speaker notes match');
    report.outcomes.push({ name: 'fresh-process-reopen', state: 'PASS', id: saved.id, revision: 2 });
    await close('second-original-close');
  } catch (error) { add(error); report.outcomes.push({ name: 'workflow', state: 'FAIL', error: error.stack }); }
  finally {
    if (context) {
      try { await close('independent-original-cleanup'); } catch (error) { add(error); }
      if (context) { try { await context.close(); context = null; } catch (error) { add(error); } }
    }
    if (server) { try { await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())); } catch (error) { add(error); } }
    report.errors = causes.map(error => String(error.stack || error)); flush();
  }
  if (causes.length) throw new AggregateError(causes, 'Original Present workflow and cleanup causes retained.');
})().catch(error => { console.error(error); process.exitCode = 1; });
