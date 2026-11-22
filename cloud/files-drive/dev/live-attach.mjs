// Resume-mode live verification: attach to the owner's already-open sign-in
// Chrome (profile under /tmp/opencode/live-check), wait for sign-in with
// timeout-safe CDP calls, then run the live Files journey and report.
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { evaluate as rawEvaluate } from './cdp.mjs';

const app = 'https://9to1.uk/files/';
const stamp = '9to1 verify 2026-11-17 live';
const out = process.env.OUT || '/tmp/opencode/live-check';
mkdirSync(out + '/shots', { recursive: true });
const report = { mode: 'attach', target: app, steps: [], status: 'RUNNING' };
const deadline = Date.now() + 25 * 60_000;

function attachClient(wsUrl) {
  let nextId = 1; const pending = new Map(); const listeners = new Set();
  const socket = new WebSocket(wsUrl);
  return new Promise((resolve, reject) => {
    socket.addEventListener('open', () => resolve(client));
    socket.addEventListener('error', () => reject(new Error('attach failed')));
    socket.addEventListener('message', e => {
      const m = JSON.parse(e.data);
      if (m.id !== undefined) { const p = pending.get(m.id); pending.delete(m.id);
        if (p) { m.error ? p.reject(new Error(m.error.message)) : p.resolve(m.result); } return; }
      for (const l of listeners) l(m);
    });
    const send = (method, params = {}, sessionId) => { const id = nextId++;
      socket.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
      return new Promise((res, rej) => pending.set(id, { resolve: res, reject: rej })); };
    const client = { send, on: (p, h) => { const f = m => { if (p(m)) h(m.params ?? {}); }; listeners.add(f); return () => listeners.delete(f); },
      close: () => socket.close() };
  });
}
const timed = (promise, ms) => Promise.race([promise, new Promise((_, rej) => setTimeout(() => rej(new Error('cdp-timeout')), ms))]);

const browser = await (async () => {
  const port = Number(readFileSync('/tmp/opencode/live-check/profile/DevToolsActivePort', 'utf8').split('\n')[0]);
  const version = await timed(fetch(`http://127.0.0.1:${port}/json/version`).then(r => r.json()), 8000);
  return { client: await attachClient(version.webSocketDebuggerUrl) };
})();
let session = null;
const HELPER_SRC = readFileSync(new URL('./files-management-driver.js', import.meta.url), 'utf8');
try {
  const { targetInfos } = await timed(browser.client.send('Target.getTargets'), 8000);
  const page = targetInfos.find(t => t.type === 'page' && /9to1\.uk|cloudflareaccess|cake-id/.test(t.url))
    ?? targetInfos.find(t => t.type === 'page' && t.url !== 'chrome://newtab/');
  if (!page) throw new Error('no sign-in page target found');
  const { sessionId } = await browser.client.send('Target.attachToTarget', { targetId: page.targetId, flatten: true });
  session = { id: sessionId,
    send: (m, p = {}) => timed(browser.client.send(m, p, sessionId), 10000),
    eval: expr => browser.client.send('Runtime.evaluate', { expression: expr, returnByValue: true,
      awaitPromise: true, userGesture: true }, sessionId).then(r => { if (r.exceptionDetails) throw new Error('page-error'); return r.result.value; }) };
  await session.send('Page.enable', {}); await session.send('Runtime.enable', {});
  await session.send('Page.addScriptToEvaluateOnNewDocument', { source: HELPER_SRC });
  const until = async (expr, ms, what) => { const stop = Date.now() + ms;
    for (;;) { let v = false; try { v = await session.eval(expr); } catch { /* mid-navigation */ }
      if (v === true) return;
      if (Date.now() > stop) throw new Error('timeout: ' + what);
      await new Promise(r => setTimeout(r, 700)); } };
  const shot = async name => { const s = await session.send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(out + '/shots/' + String(report.steps.length).padStart(2, '0') + '-' + name + '.png', Buffer.from(s.data, 'base64')); };
  const step = async (name, body) => {
    try { const detail = await body(); report.steps.push({ name, ok: true, detail: detail ?? null }); }
    catch (error) { report.steps.push({ name, ok: false, detail: String(error?.message ?? error).slice(0, 300) });
      report.status = 'FAIL'; report.failedStep = name; }
    await shot(name); if (report.status === 'FAIL') return false; return true;
  };
  // Wait for the owner: signed when the attached page is Ready on #/app.files.
  const readyExpr = `(() => { const s = (window.__nine && __nine.status()) || {}; return s.ready === "Ready" && s.hash === "#/app.files"; })()`;
  for (;;) {
    await new Promise(r => setTimeout(r, 3000));
    try { if (await session.eval(readyExpr) === true) break; } catch { /* not there yet */ }
    if (Date.now() > deadline) throw new Error('WAITED_OWNER_LOGIN_TIMEOUT');
  }
  const B = l => `(() => __nine.click(${JSON.stringify(l)}))()`;
  const H = l => `(() => __nine.has(${JSON.stringify(l)}))()`;
  const E = l => `(() => __nine.enabled(${JSON.stringify(l)}))()`;
  const S = (n, v) => `(() => __nine.set(${JSON.stringify(n)}, ${JSON.stringify(v)}))()`;
  if (await step('live-real-connection', async () => {
    await until(E('Disconnect Google Drive'), 60000, 'Drive-connected');
    return { controls: await session.eval('__nine.status().controls.length') };
  }))
  if (await step('live-read-listing', async () => {
    const items = await session.eval(`fetch('/api/files/v1/items',{credentials:'same-origin'}).then(r=>r.json()).then(j=>j.items.map(i=>({name:i.name,kind:i.kind,id:i.id.value})))`);
    if (!Array.isArray(items) || !items.length) throw new Error('real listing empty');
    return { realItems: items.length, sample: items.slice(0, 3).map(i => i.kind + ':' + i.name) };
  }))
  if (await step('live-thumbnail-proxy', async () => {
    const items = await session.eval(`fetch('/api/files/v1/items',{credentials:'same-origin'}).then(r=>r.json()).then(j=>j.items)`);
    const file = items.find(i => i.kind === 'File');
    if (!file) return { skipped: 'no file on first page' };
    const probe = `(async () => { const resp = await fetch('/api/files/v1/items/' + ${JSON.stringify(file.id.value)} + '/thumbnail', { credentials: 'same-origin' }); const b = await resp.arrayBuffer(); return { name: ${JSON.stringify(file.name)}, status: resp.status, type: resp.headers.get('content-type'), bytes: b.byteLength }; })()`;
    const r = await session.eval(probe);
    if (r.status !== 200 && r.status !== 404) throw new Error('thumbnail route: ' + JSON.stringify(r));
    return r;
  }))
  if (await step('live-disposable-folder', async () => {
    await until(B('New folder'), 15000, 'new folder editor');
    await until(S('Folder or item name', stamp), 15000, 'draft name');
    await until(B('Save'), 15000, 'save create');
    await until(H(stamp), 40000, 'created folder row');
    return { created: stamp };
  }))
  if (await step('live-rename', async () => {
    await until(B(stamp), 15000, 'select created');
    await until(B('Rename'), 15000, 'rename editor');
    await until(S('Folder or item name', stamp + ' renamed'), 15000, 'rename draft');
    await until(B('Save'), 15000, 'rename save');
    await until(`(() => __nine.has(${JSON.stringify(stamp + ' renamed')}) && !__nine.has(${JSON.stringify(stamp)}))()`, 40000, 'renamed row');
    return { renamed: stamp + ' renamed' };
  }))
  if (await step('live-search', async () => {
    await until(S('Search names in this folder', 'verify 2026-11-17'), 15000, 'search draft');
    await until(B('Search folder'), 15000, 'search');
    await until(H(stamp + ' renamed'), 30000, 'search hit');
    await until(B('Clear search'), 15000, 'clear');
    return { query: 'verify 2026-11-17' };
  }))
  if (await step('live-persistence-reload', async () => {
    await session.send('Page.reload', {});
    await until(`(() => { const s = __nine.status(); return s.ready === "Ready" && s.controls.includes(${JSON.stringify(stamp + ' renamed')}); })()`,
      90000, 'after reload');
    return { persisted: true };
  }))
  if (await step('live-trash-recoverable', async () => {
    await until(B(stamp + ' renamed'), 15000, 'select created');
    await until(B('Move to Trash…'), 15000, 'trash action');
    await until(B('Move to Trash'), 15000, 'trash confirm');
    await until(`(() => !__nine.has(${JSON.stringify(stamp + ' renamed')}))()`, 30000, 'left listing');
    return { trashedRecoverable: 'created folder left in Trash; nothing purged' };
  }))
  report.status = 'PASS';
} catch (error) {
  if (report.status === 'RUNNING') { report.status = 'FAIL'; report.error = String(error?.message ?? error).slice(0, 300); }
} finally {
  writeFileSync(out + '/report.json', JSON.stringify(report, null, 2) + '\n');
  browser.client.close();
}
console.log(JSON.stringify({ status: report.status, failedStep: report.failedStep ?? null,
  steps: report.steps.map(s => (s.ok ? 'OK ' : 'FAIL ') + s.name) }));
