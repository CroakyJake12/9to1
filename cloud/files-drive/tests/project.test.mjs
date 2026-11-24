import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, randomBytes } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { exportJWK, generateKeyPair, SignJWT } from 'jose';
import { FilesProject } from '../src/project.mjs';
import { DriveStore } from '../src/store.mjs';
import { exactNameQuery } from '../src/provider.mjs';
import { CHUNK_BYTES, JSON_BYTES } from '../src/google-transport.mjs';

const ROOT_URL = 'https://9to1.uk';
const API = '/api/files/v1';
const OWNER = '15d642d8-0e7d-4cbe-ae68-2e289dc9e218';
const SID = 'a'.repeat(43);
const OTHER_SID = 'b'.repeat(43);
const CLIENT = 'files-fixture.apps.googleusercontent.com';
const FOLDER = 'application/vnd.google-apps.folder';
const fixtureKey = await generateKeyPair('RS256', { extractable: true });
const jwk = { ...await exportJWK(fixtureKey.publicKey), kid: 'fixture-key', use: 'sig' };
const signalLatch = () => Promise.withResolvers();
const nextTurn = () => new Promise(resolve => setImmediate(resolve));

function fixture(t, { bytes = Buffer.from('sample'), validSessions = [SID, OTHER_SID], validationRun } = {}) {
  const db = new DatabaseSync(':memory:');
  const pending = [];
  const sql = (query, ...args) => db.prepare(query).all(...args);
  db.exec('CREATE TABLE fixture_kv (key TEXT PRIMARY KEY, value TEXT NOT NULL)');
  const storage = {
    kv: {
      get(key) { const row = sql('SELECT value FROM fixture_kv WHERE key=?', key)[0]; return row && JSON.parse(row.value); },
      put(key, value) {
        sql('INSERT INTO fixture_kv VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value', key, JSON.stringify(value));
      },
      delete(key) { return db.prepare('DELETE FROM fixture_kv WHERE key=?').run(key).changes > 0; },
    },
    sql: { exec: (query, ...args) => ({ toArray: () => sql(query, ...args) }) },
    transactionSync(callback) {
      db.exec('BEGIN IMMEDIATE');
      try { const value = callback(); db.exec('COMMIT'); return value; }
      catch (error) { db.exec('ROLLBACK'); throw error; }
    },
    async sync() {},
  };
  const now = () => state.now;
  const state = { now: Date.UTC(2026, 10, 15), calls: [], nonce: '', sessions: new Set(validSessions), getAccess: null };
  const env = {
    OWNER_ACCOUNT_ID: OWNER, OWNER_GOOGLE_SUB: '100000000001', OWNER_GOOGLE_EMAIL: 'fixture@example.test',
    GOOGLE_DRIVE_CLIENT_ID: CLIENT, GOOGLE_DRIVE_PROJECT_ID: 'files-fixture', GOOGLE_DRIVE_CLIENT_SECRET: 'fixture-secret',
    FILES_DEDICATED_GOOGLE_PROJECT: true, DRIVE_ENCRYPTION_KEY: randomBytes(32).toString('base64url'),
    FILES_PROVIDER_VALIDATION_RUN: validationRun,
    WEB_SESSIONS: { getByName(sid) { return { async request() {
      const hook = state.sessionCheck?.(sid);
      if (hook !== undefined) return hook;
      return Response.json({ accountId: state.sessions.has(sid) ? OWNER : 'wrong-owner', displayName: 'Fixture owner' });
    } }; } },
  };
  const store = new DriveStore(storage, OWNER, { now });
  const date = new Date(now()).toISOString();
  const root = { id: 'remote-root', name: 'My Drive', mimeType: FOLDER, version: '1', parents: [],
    createdTime: date, modifiedTime: date, ownedByMe: true, trashed: false };
  const file = { ...root, id: 'remote-file', mimeType: 'text/plain', name: 'Fixture private file', size: String(bytes.length),
    sha256Checksum: createHash('sha256').update(bytes).digest('hex'), capabilities: { canDownload: true }, parents: [root.id] };
  store.setRoot(root);
  const item = store.mapFile(file);
  async function fetch(value, options) {
    const url = new URL(value);
    const headers = new Headers(options.headers);
    const pathname = url.pathname;
    const authorization = headers.get('authorization');
    const range = headers.get('range');
    state.calls.push({ pathname, authorization, range, method: options.method });
    if (pathname === '/oauth2/v3/certs') return Response.json({ keys: [jwk] });
    if (pathname === '/revoke') {
      assert.equal(new URLSearchParams(options.body).get('token'), 'fixture-refresh');
      return new Response();
    }
    if (pathname === '/token') {
      const body = new URLSearchParams(options.body);
      if (body.get('grant_type') === 'refresh_token') {
        assert.equal(body.get('refresh_token'), 'fixture-refresh');
        return Response.json({ access_token: 'new-access', expires_in: 3600, token_type: 'Bearer' });
      }
      const instant = now() / 1000;
      const jwt = await new SignJWT({ sub: env.OWNER_GOOGLE_SUB, email: env.OWNER_GOOGLE_EMAIL,
        email_verified: true, nonce: state.nonce, iat: instant, exp: instant + 3600,
        iss: 'https://accounts.google.com', aud: CLIENT }).setProtectedHeader({ alg: 'RS256', kid: jwk.kid }).sign(fixtureKey.privateKey);
      return Response.json({ id_token: jwt, access_token: 'fixture-access', refresh_token: 'fixture-refresh', expires_in: 3600,
        token_type: 'Bearer', scope: 'openid email https://www.googleapis.com/auth/drive' });
    }
    const result = await state.getAccess?.({ url, authorization, range, signal: options.signal, options });
    if (result !== undefined) return result;
    assert.ok(['Bearer fixture-access', 'Bearer new-access'].includes(authorization));
    if (url.searchParams.get('alt') === 'media') {
      const [, start, end] = /^bytes=(\d+)-(\d+)$/.exec(range);
      return new Response(bytes.subarray(Number(start), Number(end) + 1), { status: 206,
        headers: { 'content-range': 'bytes ' + start + '-' + end + '/' + bytes.length } });
    }
    if (pathname.endsWith('/files')) return Response.json({ files: [file] });
    assert.equal(pathname, '/drive/v3/files/' + file.id);
    return Response.json(file);
  }
  const project = new FilesProject({ storage, waitUntil(promise) { pending.push(promise); } }, env, { fetch, now });
  t.after(async () => { await Promise.allSettled(pending); db.close(); });
  function request(path, { method = 'GET', body, useSid = SID, signal, origin = ROOT_URL } = {}) {
    const headers = { origin, 'content-type': 'application/json' };
    if (useSid) headers.cookie = '__Host-cake_session=' + useSid;
    return project.fetch(new Request(ROOT_URL + API + path, { method, body, headers, signal,
      ...(body instanceof ReadableStream ? { duplex: 'half' } : {}) }));
  }
  const post = (path, body = {}) => request(path, { method: 'POST', body: JSON.stringify(body) });
  async function openFlow() {
    const start = await post('/connect');
    assert.equal(start.status, 200);
    const url = new URL((await start.json()).url);
    state.nonce = url.searchParams.get('nonce');
    return '/oauth/callback?code=fixture-code&state=' + url.searchParams.get('state');
  }
  async function link() { assert.equal((await request(await openFlow())).status, 303); }
  async function prepare() {
    const result = await post('/downloads', { itemId: item.id, exportMime: null });
    assert.equal(result.status, 200);
    return (await result.json()).url.slice(API.length);
  }
  return { store, state, item, request, post, openFlow, link, prepare, root,
    configureValidationRun: run => { env.FILES_PROVIDER_VALIDATION_RUN = run; } };
}

test('callback stays private and single use, and the registered child download delivers exact bytes', async t => {
  const f = fixture(t);
  assert.equal((await f.request('/context', { useSid: 'c'.repeat(43) })).status, 403);
  const flow = await f.openFlow();
  assert.equal((await f.request(flow, { useSid: OTHER_SID })).status, 400);
  assert.equal(f.state.calls.length, 0);
  assert.equal((await f.request(flow)).status, 303);
  assert.equal((await f.request(flow)).status, 400);
  assert.equal(f.state.calls.filter(call => call.pathname === '/token').length, 1);
  const ticket = await f.prepare();
  const count = f.state.calls.length;
  const wrongSession = await f.request(ticket, { useSid: OTHER_SID });
  assert.equal(wrongSession.status, 404);
  assert.equal(wrongSession.headers.has('content-disposition'), false);
  assert.equal(f.state.calls.length, count);
  const download = await f.request(ticket);
  assert.equal(download.status, 200);
  assert.equal(download.headers.get('content-length'), '6');
  assert.equal(await download.text(), 'sample');
  assert.equal((await f.request(ticket)).status, 404);
  assert.equal(f.state.calls.filter(call => call.range).length, 1);
});

test('disconnect aborts the tracked JSON body, gates cached data, and preserves paused usage', { timeout: 5000 }, async t => {
  const f = fixture(t);
  await f.link();
  f.store.change({ metadata: { name: 'Previously listed private file' } });
  assert.equal((await (await f.request('/changes')).json()).items.length, 1);
  f.state.getAccess = () => new Response('excess', { headers: { 'content-length': String(JSON_BYTES + 1) } });
  assert.equal((await f.request('/items')).status, 502);
  const usage = await (await f.request('/usage')).json();
  assert.equal(usage.paused, true);
  assert.equal((await (await f.request('/context')).json()).usage.paused, true);
  const reading = signalLatch();
  const caller = new AbortController();
  const waiting = f.request('/operations', { method: 'POST', signal: caller.signal,
    body: new ReadableStream({ pull() { reading.resolve(); return new Promise(() => {}); } }) });
  await reading.promise;
  await nextTurn();
  const disconnected = await f.post('/disconnect');
  assert.equal(disconnected.status, 200);
  assert.equal(caller.signal.aborted, false);
  assert.notEqual((await waiting).status, 200);
  assert.equal((await f.request('/changes')).status, 409);
  assert.equal((await f.request('/uploads?operationId=' + crypto.randomUUID())).status, 409);
  const current = await (await f.request('/context')).json();
  assert.equal(current.connected, false);
  assert.equal(current.accountEmail, null);
  assert.equal(current.usage.paused, true);
  assert.ok(current.usage.unitsPerMinute.used > usage.unitsPerMinute.used);
  assert.equal(f.state.calls.filter(call => call.pathname === '/revoke').length, 1);
});

test('disconnect abandons a blocked session response before Google dispatch', { timeout: 5000 }, async t => {
  const f = fixture(t);
  await f.link();
  const entered = signalLatch(), rpc = signalLatch();
  let calls = 0;
  f.state.sessionCheck = () => {
    if (++calls !== 2) return undefined;
    entered.resolve();
    return rpc.promise;
  };
  const list = f.request('/items');
  await entered.promise;
  assert.equal((await f.post('/disconnect')).status, 200);
  rpc.resolve(Response.json({ accountId: OWNER }));
  assert.notEqual((await list).status, 200);
  assert.deepEqual(f.state.calls.map(call => call.pathname), ['/token', '/oauth2/v3/certs', '/revoke']);
});

test('a session that expires during disconnect cannot send the Google revocation request', async t => {
  const f = fixture(t);
  await f.link();
  let checks = 0;
  f.state.sessionCheck = () => ++checks === 3 ? new Response(null, { status: 401 }) : undefined;
  const result = await f.post('/disconnect');
  assert.equal(result.status, 503);
  assert.equal((await result.json()).code, 'DriveRevocationUnconfirmed');
  assert.equal(f.state.calls.filter(call => call.pathname === '/revoke').length, 0);
  const context = await (await f.request('/context')).json();
  assert.equal(context.connected, false);
  assert.equal(context.accountEmail, null);
});

test('disconnect waits for the actual active range before Google revocation', { timeout: 5000 }, async t => {
  const f = fixture(t, { bytes: new Uint8Array(CHUNK_BYTES + 1) });
  await f.link();
  const entered = signalLatch(), aborted = signalLatch(), end = signalLatch();
  f.state.getAccess = async ({ range, signal }) => {
    if (range !== 'bytes=' + CHUNK_BYTES + '-' + CHUNK_BYTES) return undefined;
    signal.addEventListener('abort', aborted.resolve, { once: true });
    entered.resolve();
    await end.promise;
    return new Response(Uint8Array.of(0), { status: 206,
      headers: { 'content-range': 'bytes ' + CHUNK_BYTES + '-' + CHUNK_BYTES + '/' + (CHUNK_BYTES + 1) } });
  };
  const content = await f.request(await f.prepare());
  const reader = content.body.getReader();
  assert.equal((await reader.read()).value.byteLength, CHUNK_BYTES);
  const second = reader.read().catch(error => error.name);
  await entered.promise;
  const disconnect = f.post('/disconnect');
  await aborted.promise;
  // The old implementation released the whole operation after 100ms even
  // though the pending Google fetch in this fixture had not settled.
  try {
    const marker = await Promise.race([disconnect.then(() => 'released'), new Promise(resolve => setTimeout(() => resolve('draining'), 150))]);
    assert.equal(marker, 'draining');
    assert.equal(f.state.calls.filter(call => call.pathname === '/revoke').length, 0);
  } finally { end.resolve(); }
  assert.equal(await second, 'AbortError');
  assert.equal((await disconnect).status, 200);
  assert.equal(f.state.calls.filter(call => call.pathname === '/revoke').length, 1);
});

test('Google revocation invalidates only the access token that actually failed', async t => {
  const f = fixture(t);
  await f.link();
  const sent = signalLatch(), reply = signalLatch();
  f.state.getAccess = ({ authorization }) => {
    if (authorization !== 'Bearer fixture-access') return undefined;
    sent.resolve();
    return reply.promise;
  };
  const path = '/items/' + f.item.id.value;
  const old = f.request(path);
  await sent.promise;
  f.state.now += 3_600_000;
  const fresh = await f.request(path);
  assert.equal(fresh.status, 200);
  assert.equal((await fresh.json()).value.name, 'Fixture private file');
  reply.resolve(Response.json({ error: {} }, { status: 401 }));
  assert.equal((await (await old).json()).error.code, 'ProviderUnavailable');
  assert.equal((await (await f.request('/context')).json()).connected, true);
  f.state.getAccess = () => Response.json({ error: {} }, { status: 401 });
  const revoked = await (await f.request(path)).json();
  assert.match(revoked.error.message, /Connect Google Drive again/);
  const context = await (await f.request('/context')).json();
  assert.equal(context.connected, false);
  assert.equal(context.reauthorizationRequired, true);
  assert.equal(f.state.calls.filter(call => call.pathname === '/revoke').length, 0);
});

function disposableDrive(f, fault) {
  const folderCapabilities = { canAddChildren: true, canRemoveChildren: true, canRename: true, canTrash: true,
    canUntrash: true, canMoveItemWithinDrive: true };
  const remote = new Map([[f.root.id, { ...f.root, shared: false, capabilities: folderCapabilities }]]);
  const patchIds = [];
  let allocated = 0;
  const etag = file => '"' + file.id + ':' + file.version + '"';
  const v2 = file => ({
    id: file.id, title: file.name, version: file.version, modifiedDate: file.modifiedTime,
    parents: file.parents.map(id => ({ id })), labels: { trashed: file.trashed },
    explicitlyTrashed: file.explicitlyTrashed ?? false, etag: etag(file),
    properties: Object.entries(file.appProperties ?? {}).map(([key, value]) => ({ key, value, visibility: 'PRIVATE' })),
  });
  f.state.getAccess = ({ url, options, authorization }) => {
    assert.equal(authorization, 'Bearer fixture-access');
    const [, , version, collection, id] = url.pathname.split('/');
    assert.equal(collection, 'files');
    if (options.method === 'GET' && id === 'generateIds') {
      assert.equal(url.searchParams.get('count'), '2');
      allocated++;
      const prefix = allocated === 1 ? 'disposable' : 'disposable-' + allocated;
      return Response.json({ ids: [prefix + '-parent', prefix + '-child'] });
    }
    if (options.method === 'POST') {
      const body = JSON.parse(options.body);
      const receipt = f.store.get('providerValidationV1');
      assert.ok(Object.values(receipt.folders).some(folder => folder.googleId === body.id));
      assert.equal(receipt.state, 'Running');
      assert.equal(remote.has(body.id), false);
      assert.ok(remote.has(body.parents[0]));
      const file = { ...f.root, ...body, shared: false, capabilities: folderCapabilities };
      remote.set(file.id, file);
      return Response.json(file);
    }
    if (options.method === 'GET' && !id) {
      const query = url.searchParams.get('q');
      if (query.includes(' and name = ')) {
        return Response.json({ files: [...remote.values()].filter(file => !file.trashed &&
          file.parents.some(parent => exactNameQuery(parent, file.name) === query)).map(file => ({ id: file.id })) });
      }
      assert.equal(query, "'" + f.store.get('providerValidationV1').folders.child.googleId + "' in parents");
      return Response.json({ files: fault === 'unrelated-child' ? [{ id: 'existing-file' }] : [] });
    }
    const file = remote.get(id);
    assert.ok(file, id);
    if (options.method === 'GET') {
      if (version === 'v2' && fault === 'invalid-v2-fields' && id === 'disposable-child' &&
          url.searchParams.get('fields') !== 'id,etag,version') {
        return Response.json({ error: { message: 'private raw provider response', errors: [{ reason: 'invalid' }] } }, { status: 400 });
      }
      return version === 'v2' ? Response.json(v2(file), { headers: { etag: etag(file) } }) : Response.json(file);
    }
    assert.equal(options.method, 'PATCH');
    assert.equal(version, 'v2');
    assert.equal(id, f.store.get('providerValidationV1').folders.child.googleId);
    const patch = JSON.parse(options.body);
    const intent = patch.properties.find(property => property.key === 'nineOperation');
    assert.equal(f.store.get('providerValidationV1').steps.at(-1).operationId, intent.value);
    patchIds.push(intent.value);
    const stale = new Headers(options.headers).get('if-match') !== etag(file);
    if (stale && fault === 'lost-stale-reply') throw new Error('Fixture write response lost');
    if (stale && fault !== 'ignore-if-match') return Response.json({ error: {} }, { status: 412 });
    file.name = patch.title ?? file.name;
    if (patch.labels) file.trashed = file.explicitlyTrashed = patch.labels.trashed;
    if (url.searchParams.has('addParents')) {
      assert.equal(url.searchParams.get('removeParents'), file.parents[0]);
      file.parents = [url.searchParams.get('addParents')];
    }
    if (fault !== 'omit-properties') file.appProperties = Object.fromEntries(patch.properties.map(({ key, value }) => [key, value]));
    file.version = String(Number(file.version) + 1);
    return Response.json(v2(file), { headers: { etag: etag(file) } });
  };
  return { remote, patchIds };
}

test('owner starts one explicitly approved metered validation and all proof writes target new folders', async t => {
  const f = fixture(t, { validationRun: crypto.randomUUID() });
  await f.link();
  const drive = disposableDrive(f);
  assert.equal(f.state.calls.length, 2, 'OAuth consent has no synthetic Drive side effects');
  assert.equal((await f.request('/validation', { useSid: 'w'.repeat(43) })).status, 403);
  const startPage = await f.request('/validation');
  assert.equal(startPage.status, 200);
  assert.match(startPage.headers.get('content-type'), /text\/html/);
  assert.match(await startPage.text(), /Run disposable file checks/);
  assert.equal((await f.request('/validation', { method: 'POST', origin: 'https://elsewhere.test' })).status, 403);
  assert.equal((await f.post('/validation', { arbitrary: 'selector' })).status, 400);
  assert.equal((await f.request('/validation?target=unrelated', { method: 'POST' })).status, 400);
  assert.equal(f.state.calls.length, 2);
  const started = await f.request('/validation', { method: 'POST' });
  assert.equal(started.status, 303);
  assert.equal(started.headers.get('location'), API + '/validation');
  const response = await f.request('/validation');
  const receipt = await response.json();
  assert.equal(receipt.state, 'Passed', JSON.stringify(receipt));
  assert.equal(receipt.steps.find(step => step.name === 'RejectStalePatch').httpStatus, 412);
  assert.ok(['Rename', 'MoveToRoot', 'MoveBack', 'Trash', 'Restore'].every(name =>
    receipt.steps.some(step => step.name === name && step.state === 'Passed')));
  assert.ok(!JSON.stringify(receipt).includes('fixture-access'));
  assert.deepEqual([...drive.remote.keys()], [f.root.id, 'disposable-parent', 'disposable-child']);
  assert.deepEqual(drive.remote.get('disposable-child').parents, ['disposable-parent']);
  assert.equal(drive.remote.get('disposable-child').trashed, false);
  assert.equal(drive.patchIds.length, 6);
  const calls = f.state.calls.length;
  await f.request('/validation', { method: 'POST' });
  assert.equal(f.state.calls.length, calls);
  const context = await (await f.request('/context')).json();
  assert.ok(!context.location.capabilities.includes('Rename'));
  assert.ok(context.usage.unitsPerDay.used > 200);
});

test('probe stops at stale-write, atomicity, unknown-reply or nonempty-folder evidence and keeps the connection', async t => {
  for (const [fault, verdict, reason, patchCount] of [
    ['ignore-if-match', 'Failed', 'StalePatchDidNotReturn412', 2],
    ['omit-properties', 'Indeterminate', 'AttemptUnconfirmed', 1],
    ['lost-stale-reply', 'Indeterminate', 'AttemptUnconfirmed', 2],
    ['unrelated-child', 'Failed', 'FolderNotVerifiedEmpty', 2],
  ]) {
    const f = fixture(t, { validationRun: crypto.randomUUID() });
    await f.link();
    const drive = disposableDrive(f, fault);
    assert.equal((await f.request('/validation', { method: 'POST' })).status, 303);
    const receipt = await (await f.request('/validation')).json();
    assert.equal(receipt.state, verdict, JSON.stringify(receipt));
    assert.equal(receipt.reason, reason);
    assert.equal(drive.patchIds.length, patchCount);
    const calls = f.state.calls.length;
    await f.request('/validation', { method: 'POST' });
    assert.equal(f.state.calls.length, calls);
    assert.equal((await (await f.request('/context')).json()).connected, true);
  }
});

test('owner diagnostics read only the failed probe child and redact the full provider response', async t => {
  const f = fixture(t, { validationRun: crypto.randomUUID() });
  await f.link();
  const drive = disposableDrive(f, 'invalid-v2-fields');
  assert.deepEqual(await (await f.request('/validation/details')).json(), { state: 'Unavailable', reason: 'NoEligibleProbeFolder' });
  assert.equal((await f.request('/validation', { method: 'POST' })).status, 303);
  const receipt = f.store.get('providerValidationV1');
  assert.equal(receipt.reason, 'StrongETagUnavailable');
  assert.equal(drive.patchIds.length, 0);
  const count = f.state.calls.length;
  assert.equal((await f.request('/validation/details', { useSid: 'w'.repeat(43) })).status, 403);
  const response = await f.request('/validation/details');
  assert.equal(response.status, 200);
  const body = await response.text();
  assert.doesNotMatch(body, /fixture-access|private raw provider response/);
  const diagnostics = JSON.parse(body);
  assert.equal(diagnostics.original.httpStatus, 400);
  assert.equal(diagnostics.original.errorReason, 'invalid');
  assert.deepEqual(diagnostics.original.bodyETag, { kind: 'Missing', value: null });
  assert.equal(diagnostics.minimal.httpStatus, 200);
  assert.equal(diagnostics.minimal.identityMatches, true);
  assert.equal(diagnostics.minimal.versionMatches, true);
  assert.equal(diagnostics.minimal.receivedVersionType, 'string');
  assert.deepEqual(diagnostics.minimal.headerETag, { kind: 'Strong', value: '"disposable-child:1"' });
  assert.deepEqual(diagnostics.minimal.bodyETag, diagnostics.minimal.headerETag);
  const reads = f.state.calls.slice(count);
  assert.ok(reads.every(read => read.method === 'GET'));
  assert.deepEqual(reads.map(read => read.pathname), ['/drive/v3/files/disposable-child',
    '/drive/v3/files', '/drive/v2/files/disposable-child', '/drive/v2/files/disposable-child']);
  assert.deepEqual(f.store.get('providerValidationV1'), receipt);
  drive.remote.get('disposable-child').version = '2';
  const changed = await (await f.request('/validation/details')).json();
  assert.deepEqual(changed, { state: 'Unavailable', reason: 'ProbeFolderChanged' });
});

test('a new configured run archives the failed receipt and creates separate probe folders', async t => {
  const firstRun = crypto.randomUUID();
  const f = fixture(t, { validationRun: firstRun });
  await f.link();
  const drive = disposableDrive(f, 'invalid-v2-fields');
  await f.request('/validation', { method: 'POST' });
  const old = f.store.get('providerValidationV1');
  assert.equal(old.state, 'Failed');
  assert.equal(old.reason, 'StrongETagUnavailable');
  const before = f.state.calls.length;
  f.configureValidationRun(firstRun.toUpperCase());
  await f.request('/validation', { method: 'POST' });
  assert.equal(f.state.calls.length, before);
  assert.equal(f.store.get('providerValidationV1:' + firstRun), null);

  const secondRun = crypto.randomUUID();
  f.configureValidationRun(secondRun);
  await f.request('/validation', { method: 'POST' });
  const current = f.store.get('providerValidationV1');
  assert.equal(current.runId, secondRun);
  assert.equal(current.state, 'Passed');
  assert.deepEqual(f.store.get('providerValidationV1:' + firstRun), old);
  assert.deepEqual(f.store.get('providerValidationV1:archivedRunIds'), [firstRun]);
  assert.deepEqual([...drive.remote.keys()], [f.root.id, 'disposable-parent', 'disposable-child', 'disposable-2-parent', 'disposable-2-child']);
  assert.equal(drive.remote.get('disposable-child').version, old.folders.child.lastVerified.version);
  assert.notEqual(current.folders.child.id, old.folders.child.id);
});

test('a new run refuses unknown or interrupted outcomes and never exceeds the archive bound', async t => {
  const f = fixture(t, { validationRun: crypto.randomUUID() });
  await f.link();
  const before = f.state.calls.length;
  for (const state of ['Running', 'Indeterminate']) {
    const old = { state, runId: crypto.randomUUID(), folders: { child: { id: crypto.randomUUID() } } };
    f.store.set('providerValidationV1', old);
    await f.request('/validation', { method: 'POST' });
    assert.deepEqual(f.store.get('providerValidationV1'), old);
    assert.equal(f.store.get('providerValidationV1:' + old.runId), null);
  }
  const old = { state: 'Failed', runId: crypto.randomUUID() };
  const archived = Array.from({ length: 7 }, () => crypto.randomUUID());
  f.store.set('providerValidationV1', old);
  f.store.set('providerValidationV1:archivedRunIds', archived);
  await f.request('/validation', { method: 'POST' });
  assert.deepEqual(f.store.get('providerValidationV1'), old);
  assert.deepEqual(f.store.get('providerValidationV1:archivedRunIds'), archived);
  assert.equal(f.state.calls.length, before);
});

test('GET /changes rejects non-decimal cursors and resumes without a phantom page', async t => {
  const f = fixture(t);
  await f.link();
  for (const id of ['created', 'moved', 'trashed', 'restored']) f.store.change({ id });
  for (const query of ['after=', 'after=%20', 'after=0x1', 'after=1e0', 'after=-0',
    'after=1%0A', 'after=9007199254740992', 'after=1&after=2', 'limit=1e0', 'limit=501', 'limit=1&limit=2']) {
    const response = await f.request('/changes?' + query);
    assert.equal(response.status, 400, query);
    assert.equal((await response.json()).code, 'InvalidCursor', query);
  }

  const first = await f.request('/changes?limit=2');
  assert.equal(first.status, 200);
  const { items, nextPageToken } = await first.json();
  assert.deepEqual(items.map(item => item.id), ['created', 'moved']);
  assert.equal(nextPageToken, String(items.at(-1).cursor.revision));

  const last = await f.request('/changes?after=' + nextPageToken + '&limit=2');
  assert.equal(last.status, 200);
  assert.deepEqual(await last.json(), {
    items: [{ id: 'trashed', cursor: { revision: 3 } }, { id: 'restored', cursor: { revision: 4 } }],
    nextPageToken: null,
  });
});
