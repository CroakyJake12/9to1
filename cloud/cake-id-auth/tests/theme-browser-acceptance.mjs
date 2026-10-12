// Local-only executable integrated app acceptance host; run only when authorized.
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { chmodSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { installFixtureRelease } from './fixture-control.mjs';
import { assertFixturePortFree, fixturePort } from './fixture-network.mjs';
import { createThemeBrowserProxy, fixtureBrowserOrigin, publicAccountConfiguration, validateAppUpstream } from './theme-browser-proxy.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const appUpstream = validateAppUpstream(process.env.CAKE_THEME_APP_UPSTREAM);
const issuerPort = fixturePort(process.env.CAKE_BROWSER_FIXTURE_PORT);
const duration = process.env.CAKE_THEME_FIXTURE_MINUTES ?? '60';
if (!/^[1-9][0-9]?$/.test(duration)) throw new Error('Fixture lifetime must be between 1 and 99 minutes.');
if (issuerPort === Number(appUpstream.port)) throw new Error('Issuer and app listener ports must differ.');
if (existsSync(path.join(root, '.dev.vars'))) throw new Error('Existing .dev.vars is owned elsewhere; no fixture started.');
await assertFixturePortFree(issuerPort);
await assertFixturePortFree(5096);
const appProbe = await fetch(new URL('/index.html', appUpstream), { redirect: 'error', signal: AbortSignal.timeout(5000) });
if (!appProbe.ok || !(await appProbe.text()).includes('main.js')) throw new Error('Start the actual integrated app listener first.');

const runPath = mkdtempSync(path.join(tmpdir(), '9to1-theme-browser-'));
chmodSync(runPath, 0o700);
const keyPath = path.join(runPath, 'client.key');
const certPath = path.join(runPath, 'client.crt');
const privateDiagnosticPath = path.join(runPath, 'fixture-private-diagnostics.log');
const openssl = (args, input) => {
  const result = spawnSync('openssl', args, { input, encoding: 'buffer', timeout: 15000 });
  if (result.status !== 0) throw new Error('Isolated certificate preparation failed.');
  return result.stdout;
};
openssl(['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-subj', '/CN=client.example.test',
  '-addext', 'subjectAltName=DNS:client.example.test', '-keyout', keyPath, '-out', certPath]);
chmodSync(keyPath, 0o600); chmodSync(certPath, 0o600);
const publicKey = openssl(['x509', '-in', certPath, '-pubkey', '-noout']);
const publicDer = openssl(['pkey', '-pubin', '-outform', 'DER'], publicKey);
const spki = createHash('sha256').update(publicDer).digest('base64');
const chromiumArgs = ['--host-resolver-rules=MAP client.example.test 127.0.0.1, EXCLUDE localhost',
  '--no-proxy-server', `--ignore-certificate-errors-spki-list=${spki}`];
let release;
const released = new Promise(resolve => { release = resolve; });
const closeRelease = installFixtureRelease(release);
const deadline = setTimeout(release, Number(duration) * 60000);
let proxy, child, childExit, drained = false, ready = false, privateManifestPath, diagnostics = '';
let failure;
try {
  let resolveReady, rejectReady;
  const readiness = new Promise((resolve, reject) => { resolveReady = resolve; rejectReady = reject; });
  child = spawn(process.execPath, ['tests/browser-fixture.mjs'], { cwd: root,
    env: { ...process.env, CAKE_BROWSER_FIXTURE_PORT: String(issuerPort),
      CAKE_BROWSER_FIXTURE_SMOKE: '0', CAKE_BROWSER_FIXTURE_ISSUER_ORIGIN_TEST: '0', CAKE_BROWSER_FIXTURE_PUBLIC_CORS_TEST: '0',
      XDG_CONFIG_HOME: path.join(runPath, 'xdg') }, stdio: ['pipe', 'pipe', 'pipe'] });
  child.stdin.on('error', () => {}); // Exit/drain receipt decides whether cleanup is authorized.
  let stdout = '';
  const retain = value => { diagnostics = (diagnostics + value).slice(-1024 * 1024); };
  child.stderr.setEncoding('utf8').on('data', retain);
  child.stdout.setEncoding('utf8').on('data', chunk => {
    retain(chunk); stdout += chunk;
    let newline;
    while ((newline = stdout.indexOf('\n')) >= 0) {
      const line = stdout.slice(0, newline); stdout = stdout.slice(newline + 1);
      let value; try { value = JSON.parse(line); } catch { continue; }
      if (value.status === 'ready') resolveReady(value);
      if (value.status === 'drained' && value.custody?.strictReaped === true && value.custody?.originalsDisappeared === true) drained = true;
    }
  });
  childExit = new Promise(resolve => {
    child.once('error', () => { rejectReady(new Error('Maintained fixture launch failed.')); resolve(null); });
    child.once('exit', code => { rejectReady(new Error('Maintained fixture exited before readiness.')); resolve(code); });
  });
  let startupTimer;
  const status = await Promise.race([readiness, released.then(() => { throw new Error('Fixture cancelled before readiness.'); }),
    new Promise((_, reject) => { startupTimer = setTimeout(() => reject(new Error('Fixture startup deadline exceeded.')), 90000); })
  ]).finally(() => clearTimeout(startupTimer));
  privateManifestPath = status.manifestPath;
  const manifest = JSON.parse(readFileSync(privateManifestPath, 'utf8'));
  assert.equal(manifest.browserOrigin, fixtureBrowserOrigin);
  const account = publicAccountConfiguration(manifest);
  assert.equal(account.apiResource, `http://127.0.0.1:${issuerPort}`);
  assert.equal(manifest.accounts.length, 2);
  const publicPath = path.join(runPath, 'browser-public-launch.json');
  writeFileSync(publicPath, JSON.stringify({ fixtureOnly: true, appUrl: `${fixtureBrowserOrigin}/#/app.home`,
    upstream: appUpstream.origin, chromiumArgs, account }, null, 2), { mode: 0o600, flag: 'wx' });
  proxy = createThemeBrowserProxy({ key: readFileSync(keyPath), cert: readFileSync(certPath), manifest, upstream: appUpstream.origin });
  await new Promise((resolve, reject) => { proxy.server.once('error', reject); proxy.server.listen({ host: '127.0.0.1', port: 5096, exclusive: true }, resolve); });
  ready = true;
  console.log(JSON.stringify({ status: 'ready', localOnly: true, appUrl: `${fixtureBrowserOrigin}/#/app.home`,
    privateManifestPath, publicLaunchPath: publicPath, expiresAfterMinutes: Number(duration),
    notes: 'Read credentials in the browser controller only; no secrets in output. Stop with stdin line stop or Ctrl-C.' }));
  await Promise.race([released, childExit.then(() => { throw new Error('Issuer ended during browser acceptance.'); })]);
} catch (error) {
  failure = error;
  diagnostics += `\nAcceptance host: ${error?.stack ?? 'unknown failure'}\n`;
  console.error(JSON.stringify({ status: 'failed', code: 'LocalThemeAcceptanceFixtureUnavailable', privateDiagnosticPath }));
  process.exitCode = 1;
} finally {
  clearTimeout(deadline);
  closeRelease();
  try { await proxy?.close(); } catch { failure ??= new Error('Owned application proxy drain failed.'); process.exitCode = 1; }
  let code;
  if (child) {
    if (child.exitCode === null) child.stdin.end('stop\n');
    let drainTimer;
    code = await Promise.race([childExit, new Promise(resolve => { drainTimer = setTimeout(() => resolve(null), 45000); })])
      .finally(() => clearTimeout(drainTimer));
  }
  if (child && (code !== 0 || !drained)) {
    writeFileSync(privateDiagnosticPath, diagnostics, { mode: 0o600 });
    console.error(JSON.stringify({ status: 'held', reason: 'Strict fixture drain unproven; private state retained.', privateDiagnosticPath }));
    process.exitCode = 1;
  } else if (failure) {
    writeFileSync(privateDiagnosticPath, diagnostics, { mode: 0o600 });
    console.error(JSON.stringify({ status: 'failed-and-drained', privateDiagnosticPath }));
  } else {
    rmSync(runPath, { recursive: true, force: true });
    console.log(JSON.stringify({ status: 'drained', localOnly: true, ready, strictFixtureDrain: true, removedPrivateState: true }));
  }
}
