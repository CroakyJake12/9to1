// Independent additive fixture. Never replaces the full integration/OIDC contract.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes, createHmac, createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync, realpathSync, lstatSync, openSync, closeSync, writeSync, ftruncateSync, fsyncSync, readlinkSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const phase = process.argv[6] ?? 'combined';
assert.ok(['combined', 'traffic-only', 'combined-cdp'].includes(phase), 'explicit supported fixture phase required');
// Read-only local corroboration. The complete caller independently proves the source-bound
// Python creator, original process/session custody and BOTH drains before state cleanup.
function runnerIssuedCdp() {
  const endpoint = process.argv[7];
  assert.ok(endpoint && /^ws:\/\/127\.0\.0\.1:([0-9]{1,5})\/devtools\/browser\/[a-zA-Z0-9-]+$/.test(endpoint), 'exact runner-issued loopback browser endpoint');
  const url = new URL(endpoint);
  assert.ok(Number(url.port) >= 1 && Number(url.port) <= 65535);
  const receiptPath = process.argv[8];
  assert.ok(receiptPath && path.isAbsolute(receiptPath), 'explicit absolute producer receipt required');
  assert.equal(realpathSync(receiptPath), receiptPath, 'canonical read-only producer receipt');
  const receiptIdentity = lstatSync(receiptPath);
  assert.ok(receiptIdentity.isFile() && !receiptIdentity.isSymbolicLink() && receiptIdentity.size > 0 && receiptIdentity.size <= 65536);
  const receipt = JSON.parse(readFileSync(receiptPath, 'utf8'));
  assert.equal(receipt.status, 'original browser CDP admitted');
  assert.equal(receipt.drained, false);
  assert.equal(receipt.endpoint, endpoint);
  const pid = receipt.launcherPid;
  assert.ok(Number.isSafeInteger(pid) && pid > 0);
  assert.ok(Array.isArray(receipt.launcherTuple) && receipt.launcherTuple.length === 5);
  assert.ok(Number.isSafeInteger(receipt.launcherTuple[0]) && receipt.launcherTuple[0] > 0);
  assert.equal(receipt.launcherTuple[1], pid); assert.equal(receipt.launcherTuple[2], pid);
  assert.ok(typeof receipt.launcherTuple[3] === 'string' && /^[1-9][0-9]*$/.test(receipt.launcherTuple[3]));
  assert.ok(typeof receipt.launcherTuple[4] === 'string' && receipt.launcherTuple[4] !== 'Z');
  assert.ok(path.isAbsolute(receipt.executable) && /^[a-f0-9]{64}$/.test(receipt.executableSha256));
  assert.equal(realpathSync(receipt.executable), receipt.executable);
  assert.equal(realpathSync(`/proc/${pid}/exe`), receipt.executable);
  const actualExecutable = lstatSync(receipt.executable);
  assert.ok(actualExecutable.isFile() && !actualExecutable.isSymbolicLink());
  assert.deepEqual(receipt.executableIdentity, { device: actualExecutable.dev, inode: actualExecutable.ino,
    mode: actualExecutable.mode & 0o7777, type: actualExecutable.mode & 0o170000 });
  const stat = readFileSync(`/proc/${pid}/stat`, 'utf8');
  const fields = stat.slice(stat.lastIndexOf(')') + 2).trim().split(/\s+/);
  assert.equal(Number(fields[1]), receipt.launcherTuple[0]);
  assert.equal(Number(fields[2]), pid); assert.equal(Number(fields[3]), pid);
  assert.equal(fields[19], receipt.launcherTuple[3]); assert.notEqual(fields[0], 'Z');
  assert.ok(typeof receipt.listenerInode === 'string' && /^[1-9][0-9]*$/.test(receipt.listenerInode));
  const matches = readFileSync(`/proc/${pid}/net/tcp`, 'utf8').trim().split('\n').slice(1)
    .map(line => line.trim().split(/\s+/)).filter(row => row[3] === '0A' &&
      row[1].split(':')[0] === '0100007F' && parseInt(row[1].split(':')[1], 16) === Number(url.port));
  assert.equal(matches.length, 1); assert.equal(matches[0][9], receipt.listenerInode);
  const owned = readdirSync(`/proc/${pid}/fd`).some(fd => {
    try { return readlinkSync(`/proc/${pid}/fd/${fd}`) === `socket:[${receipt.listenerInode}]`; }
    catch (error) { if (error.code === 'ENOENT') return false; throw error; }
  });
  assert.equal(owned, true, 'same original PID owns issued loopback listener');
  assert.ok(path.isAbsolute(receipt.profile)); assert.equal(realpathSync(receipt.profile), receipt.profile);
  const profile = lstatSync(receipt.profile);
  assert.ok(profile.isDirectory() && !profile.isSymbolicLink());
  assert.deepEqual(receipt.profileIdentity, { device: profile.dev, inode: profile.ino,
    mode: 0o700, type: profile.mode & 0o170000 });
  assert.equal(profile.mode & 0o7777, 0o700);
  assert.ok(Number.isSafeInteger(receipt.startupLogBytes) && receipt.startupLogBytes > 0 && receipt.startupLogBytes <= 262144);
  assert.ok(/^[a-f0-9]{64}$/.test(receipt.startupLogSha256));
  return { endpoint, pid, receiptIdentity: { device: receiptIdentity.dev, inode: receiptIdentity.ino } };
}
const issuedCdp = phase === 'combined-cdp' ? runnerIssuedCdp() : undefined;

const root = path.resolve(process.argv[2] ?? '');
assert.ok(process.argv[2], 'explicit privately materialized verified auth root required');
const config = JSON.parse(readFileSync(path.join(root, 'wrangler.local.jsonc'), 'utf8'));
assert.equal(config.vars.APP_MODE, 'local-test-only');
assert.equal(config.vars.AUTH_BASE_URL, 'http://127.0.0.1:8798');
assert.equal(config.vars.LOGIN_MAX_ATTEMPTS, '8');
assert.equal(config.vars.LOGIN_WINDOW_SECONDS, '900');
assert.equal(config.vars.LOGIN_LOCK_SECONDS, '1800');
const base = config.vars.AUTH_BASE_URL;
const run = randomBytes(8).toString('hex');
assert.equal(realpathSync(root), root, 'private auth root must be canonical');
const stateRoot = path.join(root, '.local-run');
assert.equal(realpathSync(stateRoot), stateRoot, 'supervisor must create canonical private state parent');
function freshStatePath(argument, label) {
  assert.ok(argument && path.isAbsolute(argument), `${label} requires explicit absolute path`);
  const value = path.resolve(argument);
  assert.equal(path.dirname(value), stateRoot, `${label} must be direct private state child`);
  assert.equal(existsSync(value), false, `${label} must be fresh`);
  return value;
}
const persist = freshStatePath(process.argv[4], 'persist directory');
const receiptPath = freshStatePath(process.argv[5], 'private state receipt');
assert.notEqual(persist, receiptPath);
const varsPath = path.join(root, '.dev.vars');
const key = randomBytes(48).toString('base64url');
const testKey = randomBytes(32).toString('base64url');
const vars = `AUTH_SECRET=${randomBytes(48).toString('base64url')}\nLOGIN_LIMITER_KEY=${key}\nLOCAL_TEST_KEY=${testKey}\n`;
assert.equal(existsSync(varsPath), false, 'never overwrite existing credentials');
mkdirSync(persist, { mode: 0o700 });
const persistedIdentity = lstatSync(persist);
assert.ok(persistedIdentity.isDirectory() && !persistedIdentity.isSymbolicLink());
const receipt = { version: 1, root, persist: { path: persist, device: persistedIdentity.dev, inode: persistedIdentity.ino, mode: persistedIdentity.mode }, vars: null };
const receiptFd = openSync(receiptPath, 'wx', 0o600);
try {
  function saveReceipt() {
    const bytes = Buffer.from(JSON.stringify(receipt) + '\n');
    ftruncateSync(receiptFd, 0);
    let offset = 0;
    while (offset < bytes.length) offset += writeSync(receiptFd, bytes, offset, bytes.length - offset, offset);
    fsyncSync(receiptFd);
  }
  saveReceipt(); // Persist identity recorded before any Worker/D1 process starts.
  writeFileSync(varsPath, vars, { flag: 'wx', mode: 0o600 });
  const varsIdentity = lstatSync(varsPath);
  assert.ok(varsIdentity.isFile() && !varsIdentity.isSymbolicLink());
  receipt.vars = { path: varsPath, device: varsIdentity.dev, inode: varsIdentity.ino, mode: varsIdentity.mode, sha256: createHash('sha256').update(readFileSync(varsPath)).digest('hex') };
  saveReceipt(); // Private receipt contains identities only; never archive it.
} finally { closeSync(receiptFd); }
const wrangler = path.join(root, 'node_modules/wrangler/bin/wrangler.js');
let worker, browser, workerFailure;
let output = '';
async function stop() {
  if (!worker || worker.exitCode !== null || worker.signalCode !== null || (workerFailure && !worker.pid)) return;
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => { cleanup(); reject(new Error('Wrangler did not exit; preserve state for supervisor drain')); }, 10000);
    const exited = () => { cleanup(); resolve(); };
    const failed = error => { cleanup(); reject(error); };
    function cleanup() { clearTimeout(timer); worker.removeListener('exit', exited); worker.removeListener('error', failed); }
    // Attach before signalling; observe signalled exits as well as exit codes.
    worker.once('exit', exited); worker.once('error', failed);
    if (worker.exitCode !== null || worker.signalCode !== null) exited();
    else worker.kill('SIGINT');
  });
}
async function requireOldServiceStopped() {
  for (let i = 0; i < 20; i++) {
    try { await fetch(`${base}/__test/health`, { headers: { 'x-local-test-key': testKey }, signal: AbortSignal.timeout(1000) }); }
    catch (error) { if (error?.cause?.code === 'ECONNREFUSED') return; }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error('Old loopback runtime did not refuse connections; no restart proof');
}
async function start() {
  workerFailure = undefined;
  worker = spawn(process.execPath, [wrangler, 'dev', '--local', '--config', 'wrangler.local.jsonc', '--ip', '127.0.0.1', '--port', '8798', '--persist-to', persist], { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
  worker.once('error', error => { workerFailure = error; });
  for (const stream of [worker.stdout, worker.stderr]) stream.setEncoding('utf8').on('data', s => { output = (output + s).slice(-16000); });
  for (let i = 0; i < 60; i++) {
    if (workerFailure) throw workerFailure;
    assert.equal(worker.exitCode, null, 'actual Wrangler must remain running');
    assert.equal(worker.signalCode, null, 'actual Wrangler must not have exited by signal');
    try { const r = await fetch(`${base}/__test/health`, { headers: { 'x-local-test-key': testKey }, signal: AbortSignal.timeout(2000) }); if (r.ok && (await r.json()).ready === true) return; } catch {}
    await new Promise(r => setTimeout(r, 500));
  }
  throw new Error('Actual local Worker initialization unavailable');
}
async function request(route, body, ip = '192.0.2.91') {
  return fetch(`${base}${route}`, { method: 'POST', headers: { origin: base, 'content-type': 'application/json', 'CF-Connecting-IP': ip }, body: JSON.stringify(body), redirect: 'manual', signal: AbortSignal.timeout(30000) });
}
async function d1(sql) {
  // Actual Wrangler D1 read-only SQL against THIS isolated persisted database.
  const child = spawn(process.execPath, [wrangler, 'd1', 'execute', 'DB', '--local', '--config', 'wrangler.local.jsonc', '--persist-to', persist, '--command', sql, '--json'], { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
  let stdout = '', stderr = '', overflow = false;
  child.stdout.setEncoding('utf8').on('data', s => {
    if (stdout.length + s.length > 262144) overflow = true;
    stdout = (stdout + s).slice(0, 262144);
  });
  child.stderr.setEncoding('utf8').on('data', s => { stderr = (stderr + s).slice(-16000); });
  const code = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Actual D1 read exceeded deadline; supervisor must drain before state cleanup')), 30000);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('exit', code => { clearTimeout(timer); resolve(code); });
  });
  assert.equal(overflow, false, 'bounded actual D1 output');
  assert.equal(code, 0, 'actual local D1 query succeeds');
  const parsed = JSON.parse(stdout);
  assert.ok(parsed.every(x => x.success === true));
  return parsed.flatMap(x => x.results ?? []);
}
async function account(prefix) {
  const email = `${prefix}-${run}@example.test`, password = 'Synthetic-only-password-9!';
  const r = await request('/api/auth/sign-up/email', { name: 'Synthetic traffic fixture', username: `${prefix}_${run}`, email, password });
  assert.equal(r.status, 200);
  const body = await r.json(); assert.ok(body.user?.id);
  const out = await fetch(`${base}/__test/outbox?recipient=${encodeURIComponent(email)}`, { headers: { 'x-local-test-key': testKey } });
  assert.equal(out.status, 200);
  const message = (await out.json()).messages.find(m => m.subject.includes('Verify'));
  assert.ok(message);
  const url = message.body.match(/https?:\/\/[^\s]+/)?.[0]; assert.ok(url);
  assert.equal(new URL(url).origin, base, 'verification stays in isolated local issuer');
  const verified = await fetch(url, { redirect: 'manual' }); assert.ok([200, 302, 303].includes(verified.status));
  return { email, password };
}
let primaryFailure;
try {
  await start();
  const a = await account('traffic');
  const hash = createHmac('sha256', key).update(`login\nemail:${a.email}\n192.0.2.91`).digest('hex');
  const readRow = async () => { const rows = await d1(`SELECT attempts,windowStartedAt,lockedUntil,updatedAt FROM cake_login_attempts WHERE keyHash='${hash}'`); assert.equal(rows.length, 1); return rows[0]; };
  const before = Math.floor(Date.now() / 1000);
  let credentialErrorResponses = 0, builtinThrottleResponses = 0, durableThrottleResponses = 0;
  for (let i = 1; i <= 700; i++) {
    const r = await request('/api/auth/sign-in/email', { email: a.email, password: i === 700 ? a.password : 'incorrect-password' });
    if (i <= 8) {
      // The durable pre-handler bucket admits at most eight attempts, while
      // unchanged maintained Better Auth can apply its stronger 3/10s rule.
      if (r.status === 401) {
        const body = await r.json();
        assert.equal(body.code, 'INVALID_EMAIL_OR_PASSWORD', 'exact maintained wrong-credential response');
        credentialErrorResponses++;
      } else {
        assert.equal(r.status, 429, `early attempt ${i} requires an exact maintained throttle response`);
        const body = await r.json();
        assert.deepEqual(Object.keys(body).sort(), ['message'], 'maintained throttle response has its documented shape');
        assert.equal(body.message, 'Too many requests. Please try again later.');
        assert.ok(Number(r.headers.get('x-retry-after')) > 0, 'maintained throttle retry interval');
        builtinThrottleResponses++;
      }
    } else {
      assert.equal(r.status, 429, `request ${i} blocked by durable pre-handler limit`);
      assert.equal((await r.json()).error, 'too_many_attempts');
      assert.ok(Number(r.headers.get('retry-after')) > 0);
      durableThrottleResponses++;
    }
  }
  assert.equal(credentialErrorResponses + builtinThrottleResponses, 8, 'all eight pre-lock API responses are observed');
  assert.ok(credentialErrorResponses >= 1 && credentialErrorResponses <= 8, 'real credential-error responses never exceed the configured test policy');
  assert.equal(durableThrottleResponses, 692, 'remaining actual requests use the durable lock response');

  const locked = await readRow();
  assert.equal(locked.attempts, 8);
  assert.ok(locked.windowStartedAt >= before);
  assert.equal(locked.lockedUntil - locked.updatedAt, 1800, 'configured thirty-minute lock recorded');
  assert.deepEqual(await d1('SELECT COUNT(*) AS count FROM session'), [{ count: 0 }], 'locked correct credentials create no session');
  await stop(); await requireOldServiceStopped(); await start();
  const denied = await request('/api/auth/sign-in/email', { email: a.email, password: a.password });
  assert.equal(denied.status, 429, 'same durable lock survives actual Worker restart');
  assert.deepEqual(await readRow(), locked, 'locked retry leaves durable row unchanged');
  if (phase === 'combined' || phase === 'combined-cdp') {
    // Browser dependency is an explicit hosted prerequisite; no substituted DOM or response.
    assert.ok(process.argv[3], 'explicit privately pinned Playwright harness required');
    const browserRoot = path.resolve(process.argv[3]);
    assert.equal(JSON.parse(readFileSync(path.join(browserRoot, 'node_modules/playwright/package.json'), 'utf8')).version, '1.63.0');
    assert.equal(JSON.parse(readFileSync(path.join(browserRoot, 'node_modules/playwright-core/package.json'), 'utf8')).version, '1.63.0');
    const { chromium } = await import(pathToFileURL(path.join(browserRoot, 'node_modules/playwright/index.mjs')).href);
    if (phase === 'combined-cdp') {
      assert.deepEqual(runnerIssuedCdp(), issuedCdp, 'same live original browser admission before connection');
      browser = await chromium.connectOverCDP(issuedCdp.endpoint);
    } else {
    browser = await chromium.launch({ headless: true });
    }
    const page = await browser.newPage();
    const b = await account('browser');
    let loginRequests = 0;
    page.on('request', r => { if (new URL(r.url()).pathname === '/api/auth/sign-in/email' && r.method() === 'POST') loginRequests++; });
    await page.goto(`${base}/sign-in`);
    await page.locator('#email').fill(b.email);
    await page.locator('#password').fill('incorrect-password');
    const actualReturn = page.waitForResponse(r => new URL(r.url()).pathname === '/api/auth/sign-in/email' && r.request().method() === 'POST');
    await page.locator('#sign-in-form').evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
    const returned = await actualReturn; assert.ok([400, 401, 422].includes(returned.status()));
    if (phase === 'combined-cdp') {
      assert.equal(returned.status(), 401, 'real wrong-credential browser response');
      assert.equal((await returned.json()).code, 'INVALID_EMAIL_OR_PASSWORD');
    }
    await page.waitForFunction(() => document.querySelector('#sign-in-form').dataset.busy === 'false');
    assert.equal(loginRequests, 1, 'two real form submissions produce one real network request');
    assert.equal(await page.locator('#sign-in-form button[type=submit]').isEnabled(), true);
    if (phase === 'combined-cdp') {
      console.log(JSON.stringify({ result: 'passed', phase: 'combined-cdp', browserExecuted: true, directRequests: 700, durableWindowAttempts: 8, credentialErrorResponses, builtinThrottleResponses, durableThrottleResponses, throttleResponses: builtinThrottleResponses + durableThrottleResponses, durableLockSeconds: 1800, sameStoreRestart: true, browserSubmits: 2, browserNetworkRequests: 1, browserConnection: 'maintained connectOverCDP', browserOriginalPid: issuedCdp.pid, scope: 'isolated local Wrangler/D1 and genuine generated UI via runner-issued original browser CDP; source-backed pre-handler ordering, no credential-call counter/elapsed CPU/ingress billing/universal quota/cloud/OIDC acceptance' }));
    } else {
    console.log(JSON.stringify({ result: 'passed', directRequests: 700, durableWindowAttempts: 8, credentialErrorResponses, builtinThrottleResponses, durableThrottleResponses, throttleResponses: builtinThrottleResponses + durableThrottleResponses, durableLockSeconds: 1800, sameStoreRestart: true, browserSubmits: 2, browserNetworkRequests: 1, scope: 'isolated local Wrangler/D1 and genuine generated UI; source-backed pre-handler ordering, no credential-call counter/elapsed CPU/ingress billing/universal quota/cloud/OIDC acceptance' }));
    }
  } else {
    console.log(JSON.stringify({ result: 'passed', phase: 'traffic-only', browserExecuted: false, directRequests: 700, durableWindowAttempts: 8, credentialErrorResponses, builtinThrottleResponses, durableThrottleResponses, throttleResponses: builtinThrottleResponses + durableThrottleResponses, durableLockSeconds: 1800, sameStoreRestart: true, scope: 'isolated local Wrangler/D1 direct API only; genuine browser still mandatory in combined phase; source-backed pre-handler ordering, no credential-call counter/elapsed CPU/ingress billing/universal quota/cloud/OIDC acceptance' }));
  }
} catch (error) {
  primaryFailure = error;
  throw error;
} finally {
  let cleanupFailure;
  try { await browser?.close(); } catch (error) { cleanupFailure = error; }
  try { await stop(); } catch (error) { cleanupFailure ??= error; }
  // Parent exit is NOT descendant disappearance. Keep this run's random vars/D1
  // private until the OUTER verified original-session drain seal permits cleanup.
  // Never archive this state or raw server logs; no fixture-side deletion here.
  if (!primaryFailure && cleanupFailure) throw cleanupFailure;
}
