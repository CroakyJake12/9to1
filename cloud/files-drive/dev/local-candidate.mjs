#!/usr/bin/env node
// Development-only local Files candidate: serves the sealed WebAssembly app
// through the genuine Files Worker inside local Workerd/Miniflare against a
// disposable in-memory Google Drive fixture. Nothing here reaches Google,
// production credentials or real Drive data; every byte stays on this machine.
//
//   node dev/local-candidate.mjs --wwwroot <sealed wwwroot> [--journey] [--keep]
import { mkdirSync, mkdtempSync, existsSync, readFileSync, writeFileSync } from 'node:fs';
import { randomBytes } from 'node:crypto';
import { createServer as httpsServer } from 'node:https';
import { request as httpRequest } from 'node:http';
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { Miniflare, convertV4MiniflareOptions } = require('miniflare');
const esbuild = require('esbuild');
const HERE = dirname(fileURLToPath(import.meta.url));
const packageRoot = resolve(HERE, '..');

export const OWNER_ACCOUNT_ID = '15d642d8-0e7d-4cbe-ae68-2e289dc9e218';
export const OWNER_SUB = 'local-fixture-owner-subject';
export const OWNER_EMAIL = 'local-fixture-owner@example.test';
export const CLIENT_ID = 'local-files-drive-fixture.apps.googleusercontent.com';
export const PROJECT_ID = 'local-files-drive-fixture';
export const FILES_ORIGIN = 'https://9to1.uk';
export const SESSION_COOKIE = '__Host-cake_session';
export const OWNER_SID = 'O' + '0'.repeat(42);

function arg(name, fallback) {
  const index = process.argv.indexOf('--' + name);
  return index >= 0 && process.argv[index + 1] && !process.argv[index + 1].startsWith('--')
    ? process.argv[index + 1] : fallback;
}

async function bundle(entryPoint) {
  const result = await esbuild.build({
    entryPoints: [entryPoint], bundle: true, format: 'esm', platform: 'browser',
    target: 'es2022', external: ['cloudflare:workers', 'node:*'], write: false, logLevel: 'silent',
  });
  return result.outputFiles[0].text;
}

function ensureCert(state) {
  const key = join(state, 'local.pem'); const cert = join(state, 'local.crt');
  if (!existsSync(key)) {
    const run = spawnSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', key,
      '-out', cert, '-days', '2', '-subj', '/CN=9to1.uk', '-addext', 'subjectAltName=DNS:9to1.uk'],
    { stdio: 'ignore' });
    if (run.status !== 0) throw new Error('Local certificate generation failed');
  }
  return { key: readFileSync(key, 'utf8'), cert: readFileSync(cert, 'utf8') };
}

// The app requires the exact https://9to1.uk origin, so a machine-local TLS
// listener forwards to Miniflare's proxy with Host and forwarded-proto set.
// Chrome is told (host-resolver-rules) to map 9to1.uk:443 to this listener.
function forwardTls(proxyPort, https) {
  const server = httpsServer(https, (incoming, outgoing) => {
    const proxyRequest = httpRequest({ host: '127.0.0.1', port: proxyPort,
      path: incoming.url, method: incoming.method, headers: {
        ...incoming.headers, host: '9to1.uk', 'x-forwarded-proto': 'https' } },
    proxyResponse => {
      outgoing.writeHead(proxyResponse.statusCode ?? 502, proxyResponse.headers);
      proxyResponse.pipe(outgoing);
    });
    proxyRequest.on('error', () => outgoing.end());
    incoming.pipe(proxyRequest);
  });
  return new Promise((resolveListen, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolveListen({ server, port: server.address().port }));
  });
}

export async function startCandidate({ wwwroot, state }) {
  const { createDriveFixture } = await import(join(HERE, 'local-drive-fixture.mjs'));
  const fixture = createDriveFixture({ projectId: PROJECT_ID, clientId: CLIENT_ID,
    ownerSub: OWNER_SUB, ownerEmail: OWNER_EMAIL });
  fixture.files.set('localSeedFolder00000001', { id: 'localSeedFolder00000001',
    name: 'Local Fixture Folder', mimeType: 'application/vnd.google-apps.folder', version: '1',
    createdTime: new Date().toISOString(), modifiedTime: new Date().toISOString(),
    parents: [fixture.root.id], driveId: null, ownedByMe: true, shared: false, trashed: false,
    explicitlyTrashed: false, appProperties: {}, bytes: null });
  fixture.seed('welcome.txt', fixture.root.id, 'Welcome to the local Files candidate fixture.\n');
  const nested = fixture.seed('readme.txt', 'localSeedFolder00000001', 'Nested fixture file.\n');
  nested.parents = ['localSeedFolder00000001'];

  const environment = {
    OWNER_ACCOUNT_ID, OWNER_GOOGLE_SUB: OWNER_SUB, OWNER_GOOGLE_EMAIL: OWNER_EMAIL,
    GOOGLE_DRIVE_PROJECT_ID: PROJECT_ID, GOOGLE_DRIVE_CLIENT_ID: CLIENT_ID,
    GOOGLE_DRIVE_CLIENT_SECRET: 'local-fixture-client-secret',
    DRIVE_ENCRYPTION_KEY: randomBytes(32).toString('base64url'),
    FILES_DEDICATED_GOOGLE_PROJECT: 'true', DRIVE_CONDITIONAL_WRITES_VERIFIED: 'true',
  };
  const workerScript = await bundle(join(packageRoot, 'src/worker.mjs'));
  const sessionScript = await bundle(join(packageRoot, 'tests/workerd/web-session-worker.mjs'));
  const outboundService = async request => {
    const headers = new Headers(request.headers);
    if (['GET', 'HEAD'].includes(request.method)) return fixture.fetch(request.url, { method: request.method, headers });
    const body = await request.arrayBuffer();
    headers.delete('content-length');
    return fixture.fetch(request.url, { method: request.method, headers, body });
  };
  const mf = new Miniflare(convertV4MiniflareOptions({
    telemetry: { enabled: false },
    resourcePersistencePath: state,
    workers: [{
      name: 'files-drive', modules: true, script: workerScript,
      compatibilityDate: '2026-11-15', compatibilityFlags: ['nodejs_compat', 'enable_request_signal'],
      routes: ['https://9to1.uk/*'], bindings: environment,
      assets: { directory: wwwroot, binding: 'ASSETS', run_worker_first: true,
        assetConfig: { html_handling: 'none', not_found_handling: 'none' } },
      durableObjects: {
        DRIVE_PROJECT: { className: 'DriveProject', useSQLite: true },
        WEB_SESSIONS: { className: 'WebSessionFixture', scriptName: 'web-sessions', useSQLite: true },
      },
      outboundService,
    }, {
      name: 'web-sessions', modules: true, script: sessionScript,
      compatibilityDate: '2026-11-15', compatibilityFlags: ['nodejs_compat'],
      durableObjects: { WEB_SESSIONS: { className: 'WebSessionFixture', useSQLite: true } },
    }],
  }));
  const ready = await mf.ready;
  const proxyPort = Number(new URL(ready).port);
  const https = ensureCert(state);
  const tls = await forwardTls(proxyPort, https);

  const api = (path, { method = 'GET', body, headers = {} } = {}) => mf.dispatchFetch(FILES_ORIGIN + path, {
    method, redirect: 'manual',
    headers: { cookie: SESSION_COOKIE + '=' + OWNER_SID, origin: FILES_ORIGIN,
      'sec-fetch-site': 'same-origin', 'content-type': 'application/json', ...headers },
    ...(body === undefined ? {} : { body: typeof body === 'string' ? body : JSON.stringify(body) }),
  });

  async function connect() {
    const begun = await (await api('/api/files/v1/connect', { method: 'POST', body: {} })).json();
    const authorization = new URL(begun.url ?? begun.value?.url);
    if (authorization.origin !== 'https://accounts.google.com') throw new Error('Unexpected authorization origin');
    fixture.expectAuth(authorization.searchParams.get('nonce'));
    const callback = await api('/api/files/v1/oauth/callback?code=local-fixture-code&state=' +
      encodeURIComponent(authorization.searchParams.get('state')));
    if (callback.status !== 303) throw new Error('OAuth callback refused with ' + callback.status);
    const context = await (await api('/api/files/v1/context')).json();
    if (context.connected !== true) throw new Error('Fixture connection did not become active');
    return context;
  }

  return { mf, fixture, api, connect, tlsPort: tls.port, close: async () => { tls.server.close(); await mf.dispose(); } };
}

async function main() {
  const state = mkdtempSync('/tmp/opencode/files-local-candidate-');
  const wwwroot = resolve(arg('wwwroot', '/tmp/opencode/files-web-candidate-run9/wwwroot'));
  const journey = process.argv.includes('--journey');
  const keep = process.argv.includes('--keep');
  const candidate = await startCandidate({ wwwroot, state });
  console.log(JSON.stringify({ status: 'SERVING', state, wwwroot, tlsPort: candidate.tlsPort,
    chromeMapping: `--host-resolver-rules="MAP 9to1.uk:443 127.0.0.1:${candidate.tlsPort}"`,
    app: FILES_ORIGIN + '/files/', sessionCookieHint: 'Owner session cookie is injected by the journey driver.',
    realGoogleDriveAcceptance: 'NOT RUN' }));
  let exitCode = 0;
  try {
    if (journey) {
      const { runJourney } = await import(join(HERE, 'local-journey.mjs'));
      const report = await runJourney({ ...candidate, state,
        chrome: arg('chrome', '/home/jacob/.cache/puppeteer/chrome/linux-157.0.8103.78/chrome-linux64/chrome') });
      const reportPath = arg('report', join(state, 'report.json'));
      writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n');
      console.log(JSON.stringify({ status: report.status, failedStep: report.failedStep ?? null, report: reportPath }));
      exitCode = report.status === 'PASS' ? 0 : 1;
    } else {
      if (process.argv.includes('--connect')) await candidate.connect();
      console.log('Serving. Ctrl-C stops and discards all fixture state.');
      await new Promise(resolveWait => process.once('SIGINT', resolveWait));
    }
  } finally {
    if (!keep) await candidate.close();
  }
  return exitCode;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  main().then(code => process.exit(code), error => { console.error(error); process.exit(2); });
}
