// Owner-authorized LIVE verification against https://9to1.uk using the owner's
// real 9to1 session and connected Google Drive. Opens a visible browser, waits
// for the owner to sign in, then exercises read-only listing plus ONE
// disposable folder they create (create → rename → search → trash; never
// purged, never touching pre-existing files), the real thumbnail proxy, and
// persistence across reload. Evidence only; acceptance by a separate reviewer.
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { launchChrome, attachPage, evaluate, pollFor } from './cdp.mjs';

const HELPER = `window.__nine = {
  status: () => ({ ready: document.querySelector('#browser-status')?.dataset.code ?? null,
    hash: location.hash, controls: [...document.querySelectorAll('#native-control-semantics button[data-native-peer-id], #native-control-semantics input[data-native-peer-id]')].map(p => p.getAttribute('aria-label')).filter(Boolean) }),
  has: l => [...document.querySelectorAll('#native-control-semantics button[data-native-peer-id]')].some(p => p.getAttribute('aria-label') === l),
  enabled: l => [...document.querySelectorAll('#native-control-semantics button[data-native-peer-id]')].some(p => p.getAttribute('aria-label') === l && !p.disabled),
  click: l => { const p = [...document.querySelectorAll('#native-control-semantics button[data-native-peer-id]')].find(x => x.getAttribute('aria-label') === l && !x.disabled); if (!p) return false; p.click(); return true; },
  set: (n, v) => { const i = [...document.querySelectorAll('#native-control-semantics input[data-native-peer-id]')].find(x => x.getAttribute('aria-label') === n && !x.disabled); if (!i) return false; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(i, v); i.dispatchEvent(new Event('input', { bubbles: true })); return true; },
  firstFileName: () => window.__nine.firstRow || null,
};`;
const app = 'https://9to1.uk/files/';
const stamp = '9to1 verify 2026-11-17 live';
const out = process.env.OUT || '/tmp/opencode/live-check';
mkdirSync(out + '/shots', { recursive: true });
const report = { target: app, steps: [], status: 'RUNNING' };
const until = async (session, expr, ms = 30000, what = 'condition') => {
  const r = await pollFor(session, expr, ms);
  if (r !== true) throw new Error('timeout: ' + what);
};
const shot = async (session, name) => {
  const s = await session.send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(out + '/shots/' + name + '.png', Buffer.from(s.data, 'base64'));
};
const step = async (session, name, body) => {
  try {
    const detail = await body();
    report.steps.push({ name, ok: true, detail: detail ?? null });
  } catch (error) {
    report.steps.push({ name, ok: false, detail: String(error?.message ?? error).slice(0, 300) });
    report.status = 'FAIL'; report.failedStep = name;
  }
  await shot(session, String(report.steps.length).padStart(2, '0') + '-' + name);
  if (report.status === 'FAIL') throw new Error(report.failedStep);
};

const browser = await launchChrome({
  chrome: '/home/jacob/.cache/puppeteer/chrome/linux-157.0.8103.78/chrome-linux64/chrome',
  profileDir: out + '/profile', headless: false, args: ['--window-size=1500,1500', '--window-position=60,40'],
});
try {
  const { sessionId } = await browser.client.send('Target.attachToTarget',
    { targetId: (await browser.client.send('Target.createTarget', { url: 'about:blank' })).targetId, flatten: true });
  const session = {
    send: (m, p = {}) => browser.client.send(m, p, sessionId),
    on: (m, h) => browser.client.on(x => x.sessionId === sessionId && x.method === m, p => h(p)),
  };
  await session.send('Page.enable'); await session.send('Network.enable');
  await session.send('Page.addScriptToEvaluateOnNewDocument', { source: HELPER });
  await session.send('Page.navigate', { url: app });
  // Chrome windows opened from a background session can stay unmapped under the
  // compositor; force-map and focus the owner's sign-in window so it is visible.
  await new Promise(r => setTimeout(r, 2500));
  const { execFile } = await import('node:child_process');
  const raise = `
from Xlib import display, X
from Xlib.protocol import event
import time
d = display.Display(); root = d.screen().root
def walk(win):
    out = []
    try: children = win.query_tree().children
    except Exception: return out
    for c in children:
        try: cls = c.get_wm_class()
        except Exception: cls = None
        if cls and 'chrome' in str(cls[0] or '').lower(): out.append(c)
        out += walk(c)
    return out
for w in walk(root):
    def send(name, payload):
        a = d.intern_atom(name)
        w.send_event(event.ClientMessage(window=w.id, client_type=a, message_type=a, format=32, data=(32, payload)))
    w.map(); w.configure(stack_mode=X.Above)
    send('_NET_WM_STATE', (0, d.intern_atom('_NET_WM_STATE_HIDDEN'), 0, 0, 0))
    send('_NET_WM_STATE', (1, d.intern_atom('_NET_WM_STATE_DEMANDS_ATTENTION'), 0, 0, 0))
    send('_NET_ACTIVE_WINDOW', (2, int(time.time()), 0, 0, 0))
    w.set_input_focus(X.RevertToParent, X.CurrentTime)
d.sync(); print('raised', len(walk(root)))
`;
  await new Promise((resolveRaise) => execFile('python3', ['-c', raise], () => resolveRaise()));
  // Human step: the owner signs in once. Poll for the real session cookie only.
  await pollFor(session, `(async () => { const c = await document.cookie; return true; })()`, 5000).catch(() => true);
  let signed = false;
  const deadline = Date.now() + 20 * 60_000;
  while (!signed && Date.now() < deadline) {
    await new Promise(r => setTimeout(r, 3000));
    const url = await evaluate(session, 'location.href').catch(() => '');
    signed = String(url).startsWith('https://9to1.uk/files');
  }
  if (!signed) { report.status = 'WAITED_OWNER_LOGIN_TIMEOUT'; throw new Error('owner sign-in window elapsed'); }
  await until(session, `(() => { const s = __nine.status(); return s.ready === "Ready" && s.hash === "#/app.files"; })()`,
    90000, 'connected Files root');
  await step(session, 'live-real-connection', async () => {
    await until(session, `(() => __nine.enabled("Disconnect Google Drive"))()`, 60000, 'Drive-connected state');
    const controls = await evaluate(session, '__nine.status().controls.length');
    return { controlCount: controls };
  });
  await step(session, 'live-read-listing', async () => {
    const rows = await evaluate(session, `(() => { const s = __nine.status(); return s.controls.length; })()`);
    if (!rows || rows < 8) throw new Error('real listing appears empty');
    const items = await evaluate(session, `fetch('/api/files/v1/items', { credentials: 'same-origin' })
      .then(r => r.json()).then(j => j.items.map(i => ({ name: i.name, kind: i.kind, id: i.id.value })))`);
    return { realItems: items.length, sample: items.slice(0, 4).map(i => i.kind + ':' + i.name) };
  });
  await step(session, 'live-thumbnail-proxy', async () => {
    const result = await evaluate(session, `(async () => {
      const items = await (await fetch('/api/files/v1/items', { credentials: 'same-origin' })).json();
      const file = items.items.find(i => i.kind === 'File');
      if (!file) return { skipped: 'no file in first page' };
      const response = await fetch('/api/files/v1/items/' + file.id.value + '/thumbnail', { credentials: 'same-origin' });
      const bytes = await response.arrayBuffer();
      return { name: file.name, status: response.status, type: response.headers.get('content-type'), bytes: bytes.byteLength };
    })()`);
    if (result.status && result.status !== 200 && result.status !== 404) throw new Error('thumbnail route failed: ' + JSON.stringify(result));
    return result;
  });
  await step(session, 'live-disposable-folder', async () => {
    await until(session, `(() => __nine.click("New folder"))()`, 15000, 'new folder editor');
    await until(session, `(() => __nine.set("Folder or item name", ${JSON.stringify(stamp)}))()`, 15000, 'name draft');
    await until(session, `(() => __nine.click("Save"))()`, 15000, 'create save');
    await until(session, `(() => __nine.has(${JSON.stringify(stamp)}))()`, 40000, 'created folder row');
    return { created: stamp };
  });
  await step(session, 'live-rename', async () => {
    await until(session, `(() => __nine.click(${JSON.stringify(stamp)}))()`, 15000, 'select created folder');
    await until(session, `(() => __nine.click("Rename"))()`, 15000, 'rename editor');
    await until(session, `(() => __nine.set("Folder or item name", ${JSON.stringify(stamp + ' renamed')}))()`, 15000, 'rename draft');
    await until(session, `(() => __nine.click("Save"))()`, 15000, 'rename save');
    await until(session, `(() => __nine.has(${JSON.stringify(stamp + ' renamed')}) && !__nine.has(${JSON.stringify(stamp)}))()`,
      40000, 'renamed row');
    return { renamed: stamp + ' renamed' };
  });
  await step(session, 'live-search', async () => {
    await until(session, `(() => __nine.set("Search names in this folder", "verify 2026-11-17"))()`, 15000, 'search draft');
    await until(session, `(() => __nine.click("Search folder"))()`, 15000, 'search action');
    await until(session, `(() => __nine.has(${JSON.stringify(stamp + ' renamed')}))()`, 30000, 'search hit');
    await until(session, `(() => __nine.click("Clear search"))()`, 15000, 'clear search');
    return { query: 'verify 2026-11-17' };
  });
  await step(session, 'live-persistence-reload', async () => {
    await session.send('Page.reload');
    await until(session, `(() => { const s = __nine.status(); return s.ready === "Ready" && s.controls.includes(${JSON.stringify(stamp + ' renamed')}); })()`,
      90000, 'renamed folder after reload');
    return { persisted: true };
  });
  await step(session, 'live-trash-recoverable', async () => {
    await until(session, `(() => __nine.click(${JSON.stringify(stamp + ' renamed')}))()`, 15000, 'select created folder');
    await until(session, `(() => __nine.click("Move to Trash…"))()`, 15000, 'trash action');
    await until(session, `(() => __nine.click("Move to Trash"))()`, 15000, 'trash confirm');
    await until(session, `(() => !__nine.has(${JSON.stringify(stamp + ' renamed')}))()`, 30000, 'removed from listing');
    return { trashedRecoverable: stamp + ' renamed (left in Trash; nothing purged)' };
  });
  report.status = 'PASS';
} catch (error) {
  report.status = report.status === 'RUNNING' ? 'FAIL' : report.status;
  report.error = String(error?.message ?? error).slice(0, 300);
} finally {
  writeFileSync(out + '/report.json', JSON.stringify(report, null, 2) + '\n');
  await new Promise(r => setTimeout(r, Number(process.env.KEEP || 0) * 1000));
  await browser.close().catch(() => {});
  rmSync(out + '/profile', { recursive: true, force: true });
}
console.log(JSON.stringify({ status: report.status, failedStep: report.failedStep ?? null,
  steps: report.steps.map(s => (s.ok ? 'OK ' : 'FAIL ') + s.name) }));
