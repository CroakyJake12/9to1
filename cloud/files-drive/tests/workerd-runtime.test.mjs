import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes } from 'node:crypto';
import test from 'node:test';
import { exportJWK, generateKeyPair, SignJWT } from 'jose';

const require = createRequire(import.meta.url);
const { Miniflare, convertV4MiniflareOptions } = require('miniflare');
const esbuild = require('esbuild');
const miniflareVersion = require('miniflare/package.json').version;
const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(packageRoot, '..', '..');

const FILES_ORIGIN = 'https://9to1.uk';
const API_PREFIX = '/api/files/v1';
const SESSION_COOKIE = '__Host-cake_session';
const OWNER_ACCOUNT_ID = '15d642d8-0e7d-4cbe-ae68-2e289dc9e218';
const OWNER_SUB = 'files-drive-owner-subject-fixture';
const OWNER_EMAIL = 'owner@example.test';
const GOOGLE_CLIENT_ID = 'files-drive-test.apps.googleusercontent.com';
const GOOGLE_SCOPE = 'openid email https://www.googleapis.com/auth/drive';
const TOKEN_ENDPOINT = 'https://oauth2.googleapis.com/token';
const CERTS_ENDPOINT = 'https://www.googleapis.com/oauth2/v3/certs';
const DRIVE_FILE_ID = 'fixture-document-01';
const FILE_BYTES = new TextEncoder().encode('fixture Drive content\n');

assert.equal(miniflareVersion, '5.20261113.0-beta');
assert.equal(esbuild.version, '0.28.1');

function isInside(parent, child) {
  const path = relative(parent, child);
  return path === '' || (!path.startsWith('..' + sep) && path !== '..' && !isAbsolute(path));
}

async function bundle(entryPoint) {
  const result = await esbuild.build({
    entryPoints: [entryPoint], bundle: true, format: 'esm', platform: 'browser',
    target: 'es2022', external: ['cloudflare:workers', 'node:*'], write: false, logLevel: 'silent',
  });
  assert.equal(result.outputFiles.length, 1);
  return result.outputFiles[0].text;
}

function sessionId(mode) { return mode + '0'.repeat(42); }

function request(mf, path, { sid = sessionId('O'), method = 'GET', body, origin } = {}) {
  const headers = new Headers();
  if (sid !== null) headers.set('cookie', `${SESSION_COOKIE}=${sid}`);
  if (origin !== undefined) headers.set('origin', origin);
  if (method !== 'GET' && method !== 'HEAD') {
    headers.set('sec-fetch-site', origin === FILES_ORIGIN ? 'same-origin' : 'cross-site');
  }
  if (body !== undefined) headers.set('content-type', 'application/json');
  return mf.dispatchFetch(FILES_ORIGIN + API_PREFIX + path, {
    method, headers, redirect: 'manual', ...(body === undefined ? {} : { body }),
  });
}

async function jsonResponse(response) {
  return { status: response.status, body: await response.json() };
}

test('production Worker uses Workerd SQLite and cross-worker session RPC without Google egress', async () => {
  // This MSIX-hosted environment redirects OS temp to a path unavailable to
  // Workerd. Keep only its SQLite persistence in the writable checkout parent.
  const fixtureParent = process.env.FILES_DRIVE_TEST_TMP_ROOT
    ? resolve(process.env.FILES_DRIVE_TEST_TMP_ROOT) : resolve(repositoryRoot, '..');
  const fixtureRoot = mkdtempSync(resolve(fixtureParent, 'files-drive-workerd-'));
  let mf;
  try {
    assert.ok(!isInside(repositoryRoot, fixtureRoot), 'Workerd persistence must be outside the Git checkout');
    const assetDirectory = resolve(fixtureRoot, 'assets');
    mkdirSync(resolve(assetDirectory, '_framework'), { recursive: true });
    mkdirSync(resolve(assetDirectory, 'files'));
    const index = '<!doctype html><title>Files fixture</title><script type="module" src="./main.js"></script>';
    writeFileSync(resolve(assetDirectory, 'index.html'), index);
    writeFileSync(resolve(assetDirectory, 'files/index.html'), 'This asset must not bypass the Worker.');
    writeFileSync(resolve(assetDirectory, 'main.js'), 'export const witness = 1;');
    const wasm = Uint8Array.of(0, 97, 115, 109, 1, 0, 0, 0);
    writeFileSync(resolve(assetDirectory, '_framework/test.wasm'), wasm);
    const googleCalls = [];
    let nonce;

    const { privateKey, publicKey } = await generateKeyPair('RS256', { extractable: true });
    const publicJwk = { ...await exportJWK(publicKey), kid: 'files-drive-fixture-key', alg: 'RS256', use: 'sig' };
    const environment = {
    OWNER_ACCOUNT_ID,
    OWNER_GOOGLE_SUB: OWNER_SUB,
    OWNER_GOOGLE_EMAIL: OWNER_EMAIL,
    GOOGLE_DRIVE_PROJECT_ID: 'files-drive-test-01',
    GOOGLE_DRIVE_CLIENT_ID: GOOGLE_CLIENT_ID,
    GOOGLE_DRIVE_CLIENT_SECRET: 'fixture-only-client-secret',
    DRIVE_ENCRYPTION_KEY: randomBytes(32).toString('base64url'),
    FILES_DEDICATED_GOOGLE_PROJECT: 'true',
    DRIVE_CONDITIONAL_WRITES_VERIFIED: 'false',
    FILES_PROVIDER_VALIDATION_RUN: '2f7c006a-5b57-4bcb-8fef-207c9bfe50b8',
    };

    const outboundService = async request => {
    const url = new URL(request.url);
    googleCalls.push({ host: url.host, path: url.pathname, method: request.method });

    if (url.href === TOKEN_ENDPOINT && request.method === 'POST') {
      const form = new URLSearchParams(await request.text());
      assert.equal(form.get('grant_type'), 'authorization_code');
      assert.equal(form.get('code'), 'fixture-auth-code');
      assert.equal(form.get('redirect_uri'), FILES_ORIGIN + API_PREFIX + '/oauth/callback');
      const idToken = await new SignJWT({
        email: OWNER_EMAIL, email_verified: true, nonce,
        azp: GOOGLE_CLIENT_ID,
      }).setProtectedHeader({ alg: 'RS256', kid: publicJwk.kid })
        .setIssuer('https://accounts.google.com')
        .setAudience(GOOGLE_CLIENT_ID)
        .setSubject(OWNER_SUB)
        .setIssuedAt()
        .setExpirationTime('1h')
        .sign(privateKey);
      return Response.json({
        token_type: 'Bearer', expires_in: 3600, scope: GOOGLE_SCOPE,
        access_token: 'fixture-access-token', refresh_token: 'fixture-refresh-token', id_token: idToken,
      });
    }
    if (url.href === CERTS_ENDPOINT && request.method === 'GET') {
      return Response.json({ keys: [publicJwk] }, { headers: { 'cache-control': 'public, max-age=600' } });
    }
    if (url.origin === 'https://www.googleapis.com' && url.pathname === '/drive/v3/files/root' && request.method === 'GET') {
      const now = new Date().toISOString();
      return Response.json({
        id: 'google-root-drive-fixture-id', name: 'My Drive', mimeType: 'application/vnd.google-apps.folder', version: '1',
        createdTime: now, modifiedTime: now, parents: [], driveId: null, trashed: false,
        ownedByMe: true, capabilities: { canAddChildren: true }, owners: [], appProperties: {},
      });
    }
    if (url.origin === 'https://www.googleapis.com' && url.pathname === '/drive/v3/files' && request.method === 'GET') {
      const now = new Date().toISOString();
      return Response.json({ files: [{
        id: DRIVE_FILE_ID, name: 'Fixture document.txt', mimeType: 'text/plain', version: '1',
        createdTime: now, modifiedTime: now, parents: ['google-root-drive-fixture-id'],
        size: String(FILE_BYTES.byteLength), ownedByMe: true, shared: false, trashed: false,
        explicitlyTrashed: false, capabilities: { canDownload: true }, owners: [], appProperties: {},
      }], nextPageToken: null, incompleteSearch: false });
    }
    if (url.origin === 'https://www.googleapis.com' && url.pathname === `/drive/v3/files/${DRIVE_FILE_ID}` && request.method === 'GET') {
      const now = new Date().toISOString();
      const metadata = {
        id: DRIVE_FILE_ID, name: 'Fixture document.txt', mimeType: 'text/plain', version: '1',
        createdTime: now, modifiedTime: now, parents: ['google-root-drive-fixture-id'],
        size: String(FILE_BYTES.byteLength), ownedByMe: true, shared: false, trashed: false,
        explicitlyTrashed: false, capabilities: { canDownload: true }, owners: [], appProperties: {},
      };
      if (url.searchParams.get('alt') === 'media') {
        assert.equal(request.headers.get('range'), `bytes=0-${FILE_BYTES.byteLength - 1}`);
        return new Response(FILE_BYTES, { status: 206, headers: {
          'content-range': `bytes 0-${FILE_BYTES.byteLength - 1}/${FILE_BYTES.byteLength}`,
          'content-type': 'application/octet-stream',
        } });
      }
      return Response.json(metadata);
    }
    return Response.json({ error: { message: 'No fixture response for this outbound request.' } }, { status: 599 });
    };

    const start = async () => {
    const workerScript = await bundle(resolve(packageRoot, 'src/worker.mjs'));
    const sessionScript = await bundle(resolve(packageRoot, 'tests/workerd/web-session-worker.mjs'));
    return new Miniflare(convertV4MiniflareOptions({
      telemetry: { enabled: false },
      resourcePersistencePath: fixtureRoot,
      workers: [
        {
          name: 'files-drive', modules: true, script: workerScript,
          compatibilityDate: '2026-11-15',
          compatibilityFlags: ['nodejs_compat', 'enable_request_signal'],
          routes: ['https://9to1.uk/*'],
          bindings: environment,
          assets: { directory: assetDirectory, binding: 'ASSETS', run_worker_first: true,
            assetConfig: { html_handling: 'none', not_found_handling: 'none' } },
          durableObjects: {
            DRIVE_PROJECT: { className: 'DriveProject', useSQLite: true },
            WEB_SESSIONS: { className: 'WebSessionFixture', scriptName: 'web-sessions', useSQLite: true },
          },
          outboundService,
        },
        {
          name: 'web-sessions', modules: true, script: sessionScript,
          compatibilityDate: '2026-11-15',
          compatibilityFlags: ['nodejs_compat', 'enable_request_signal'],
          durableObjects: {
            WEB_SESSIONS: { className: 'WebSessionFixture', useSQLite: true },
          },
        },
      ],
    }));
    };

    mf = await start();

    const asset = (name, sid, method = 'GET') => mf.dispatchFetch(FILES_ORIGIN + name, {
      method, redirect: 'manual', headers: sid ? { cookie: SESSION_COOKIE + '=' + sid } : {},
    });
    const guestShell = await asset('/files/');
    assert.equal(guestShell.status, 303);
    assert.equal(guestShell.headers.get('location'), '/auth/sign-in?return=%2Ffiles%2F');
    for (const alias of ['/files/%69ndex.html', '/files/index%2ehtml', '/files//index.html', '/files/%2findex.html']) {
      const refused = await asset(alias);
      assert.equal(refused.status, 404);
      assert.equal(refused.headers.has('location'), false);
    }
    assert.equal((await asset('/files/index.html', sessionId('W'))).status, 403);
    const directIndex = await asset('/files/index.html', sessionId('O'));
    assert.equal(directIndex.status, 308);
    assert.equal(directIndex.headers.get('location'), '/files/');
    const ownerShell = await asset('/files/', sessionId('O'));
    assert.equal(ownerShell.status, 200);
    assert.match(ownerShell.headers.get('content-type'), /^text\/html/);
    assert.match(ownerShell.headers.get('content-security-policy'), /wasm-unsafe-eval/);
    assert.equal(await ownerShell.text(), index);
    const head = await asset('/files/', sessionId('O'), 'HEAD');
    assert.equal(head.status, 200);
    assert.equal(await head.text(), '');
    const module = await asset('/files/main.js');
    assert.match(module.headers.get('content-type'), /javascript/);
    assert.equal(await module.text(), 'export const witness = 1;');
    const runtime = await asset('/files/_framework/test.wasm');
    assert.equal(runtime.headers.get('content-type'), 'application/wasm');
    assert.deepEqual(new Uint8Array(await runtime.arrayBuffer()), wasm);
    assert.equal((await asset('/files/_framework/missing.wasm')).status, 404);
    assert.equal((await asset('/index.html', sessionId('O'))).status, 404);

    const guest = await jsonResponse(await request(mf, '/context', { sid: null }));
    assert.equal(guest.status, 401);
    assert.equal(guest.body.code, 'SignInRequired');

    const wrongAccount = await jsonResponse(await request(mf, '/context', { sid: sessionId('W') }));
    assert.equal(wrongAccount.status, 403);
    assert.equal(wrongAccount.body.code, 'OwnerOnly');

    const revoked = await jsonResponse(await request(mf, '/context', { sid: sessionId('R') }));
    assert.equal(revoked.status, 401);
    assert.equal(revoked.body.code, 'SignInRequired');
    assert.deepEqual(googleCalls, [], 'denied sessions must not reach any Google endpoint');

    const before = await jsonResponse(await request(mf, '/context'));
    assert.equal(before.status, 200);
    assert.equal(before.body.accountId, OWNER_ACCOUNT_ID);
    assert.equal(before.body.displayName, 'Fixture Files Owner');
    assert.equal(before.body.connected, false);
    assert.equal(before.body.usage.unitsPerDay.used, 0);

    const wrongOrigin = await jsonResponse(await request(mf, '/connect', {
      method: 'POST', body: '{}', origin: 'https://attacker.invalid',
    }));
    assert.equal(wrongOrigin.status, 403);
    assert.equal(wrongOrigin.body.code, 'OriginNotAllowed');

    const malformed = await jsonResponse(await request(mf, '/connect', {
      method: 'POST', body: '{', origin: FILES_ORIGIN,
    }));
    assert.equal(malformed.status, 400);
    assert.equal(malformed.body.code, 'InvalidRequest');

    const connect = await request(mf, '/connect', { method: 'POST', body: '{}', origin: FILES_ORIGIN });
    assert.equal(connect.status, 200);
    const connectBody = await connect.json();
    const authorization = new URL(connectBody.url);
    assert.equal(authorization.origin + authorization.pathname, 'https://accounts.google.com/o/oauth2/v2/auth');
    assert.equal(authorization.searchParams.get('redirect_uri'), FILES_ORIGIN + API_PREFIX + '/oauth/callback');
    assert.equal(authorization.searchParams.get('response_type'), 'code');
    assert.equal(authorization.searchParams.get('scope'), GOOGLE_SCOPE);
    assert.equal(authorization.searchParams.get('access_type'), 'offline');
    assert.equal(authorization.searchParams.get('code_challenge_method'), 'S256');
    assert.ok(/^[A-Za-z0-9_-]{43}$/.test(authorization.searchParams.get('state') ?? ''), 'state is a 32-byte base64url token');
    assert.ok(/^[A-Za-z0-9_-]{43}$/.test(authorization.searchParams.get('nonce') ?? ''), 'nonce is a 32-byte base64url token');
    assert.ok(/^[A-Za-z0-9_-]{43}$/.test(authorization.searchParams.get('code_challenge') ?? ''), 'PKCE challenge is a 32-byte base64url token');
    nonce = authorization.searchParams.get('nonce');
    assert.deepEqual(googleCalls, [], 'starting consent must not grant or call Google');

    const stillDisconnected = await jsonResponse(await request(mf, '/context'));
    assert.equal(stillDisconnected.body.connected, false);
    assert.equal(stillDisconnected.body.usage.unitsPerDay.used, 0);

    const callback = await request(mf, `/oauth/callback?code=fixture-auth-code&state=${encodeURIComponent(authorization.searchParams.get('state'))}`);
    assert.equal(callback.status, 303,
      `callback failed: ${await callback.clone().text()}; outbound=${JSON.stringify(googleCalls)}`);
    assert.equal(callback.headers.get('location'), '/files/#/app.files');

    const connected = await jsonResponse(await request(mf, '/context'));
    assert.equal(connected.status, 200);
    assert.equal(connected.body.accountId, OWNER_ACCOUNT_ID);
    assert.equal(connected.body.displayName, 'Fixture Files Owner');
    assert.equal(connected.body.connected, true);
    assert.equal(connected.body.accountEmail, OWNER_EMAIL);

    const privateForm = await request(mf, '/validation');
    assert.equal(privateForm.status, 200);
    assert.equal(privateForm.headers.get('referrer-policy'), 'same-origin');
    assert.match(privateForm.headers.get('content-security-policy'), /form-action 'self'/);
    assert.match(await privateForm.text(), /Run disposable file checks/);
    const opaqueForm = await request(mf, '/validation', { method: 'POST', origin: 'null' });
    assert.equal(opaqueForm.status, 403, 'opaque origin still cannot start provider writes');
    await opaqueForm.body.cancel();

    const listing = await jsonResponse(await request(mf, '/items'));
    assert.equal(listing.status, 200, `list failed: ${JSON.stringify(listing.body)}; outbound=${JSON.stringify(googleCalls)}`);
    assert.equal(listing.body.items.length, 1);
    assert.equal(listing.body.items[0].name, 'Fixture document.txt');

    const downloadTicket = await jsonResponse(await request(mf, '/downloads', {
      method: 'POST', body: JSON.stringify({ itemId: listing.body.items[0].id }), origin: FILES_ORIGIN,
    }));
    assert.equal(downloadTicket.status, 200);
    const downloadId = downloadTicket.body.transferId.value;
    const download = await request(mf, `/downloads/${downloadId}/content`);
    assert.equal(download.status, 200);
    assert.equal(download.headers.get('content-length'), String(FILE_BYTES.byteLength));
    assert.deepEqual(new Uint8Array(await download.arrayBuffer()), FILE_BYTES);

    const callsBeforeRestart = [...googleCalls];
    assert.deepEqual(callsBeforeRestart.slice(0, 4).map(call => [call.host, call.path, call.method]), [
      ['oauth2.googleapis.com', '/token', 'POST'],
      ['www.googleapis.com', '/oauth2/v3/certs', 'GET'],
      ['www.googleapis.com', '/drive/v3/files/root', 'GET'],
      ['www.googleapis.com', '/drive/v3/files', 'GET'],
    ]);
    assert.equal(callsBeforeRestart.filter(call => call.path === `/drive/v3/files/${DRIVE_FILE_ID}`).length, 4,
      'download setup, version checks and content must all use the Workerd outbound gate');
    const contextAfterList = await jsonResponse(await request(mf, '/context'));
    assert.equal(contextAfterList.status, 200);
    const usageBeforeRestart = contextAfterList.body.usage;
    assert.ok(usageBeforeRestart.unitsPerDay.used > 100,
      `OAuth and list attempts must be charged to SQLite quota: ${JSON.stringify(usageBeforeRestart)}`);
    assert.ok(usageBeforeRestart.downloadBytesPerDay.used > connected.body.usage.downloadBytesPerDay.used,
      'the download attempt must be charged to SQLite transfer quota');

    const unknown = await jsonResponse(await request(mf, '/not-a-files-route', {
      method: 'POST', body: '{}', origin: FILES_ORIGIN,
    }));
    assert.equal(unknown.status, 404);
    assert.equal(unknown.body.code, 'NotFound');
    assert.deepEqual(googleCalls, callsBeforeRestart, 'invalid routes must not trigger outbound Google calls');

    const previous = mf;
    mf = null;
    await previous.dispose();
    mf = await start();
    const reopened = await jsonResponse(await request(mf, '/context'));
    assert.equal(reopened.status, 200);
    assert.equal(reopened.body.accountId, OWNER_ACCOUNT_ID);
    assert.equal(reopened.body.displayName, 'Fixture Files Owner');
    assert.equal(reopened.body.connected, true);
    assert.equal(reopened.body.accountEmail, OWNER_EMAIL);
    assert.equal(reopened.body.location.capabilities, connected.body.location.capabilities);
    assert.equal(reopened.body.usage.unitsPerDay.used, usageBeforeRestart.unitsPerDay.used);
    assert.equal(reopened.body.usage.downloadBytesPerDay.used, usageBeforeRestart.downloadBytesPerDay.used);
    assert.deepEqual(googleCalls, callsBeforeRestart, 'SQLite-backed context and quota read must not make new provider calls');
  } finally {
    try { if (mf) await mf.dispose(); }
    finally {
      esbuild.stop();
      rmSync(fixtureRoot, { recursive: true, force: true });
    }
  }
});
