import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { AccountApiClient } from './account-api-client.js';

const accountId = '12345678-1234-4234-8234-123456789abc';
const sessionId = '87654321-4321-4321-8321-cba987654321';
const current = { accountId, displayName: 'Isolated fixture' };
const profile = { accountId, name: 'Fixture', username: 'fixture', icon: null, pronouns: null, job: null, revision: 1 };
const json = (body, status = 200) => Response.json(body, { status });
const pending = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
function client(fetch, extra = {}) {
  return new AccountApiClient({ apiResource: 'https://account.invalid', getAccessToken: async () => 'unit-token',
    onPrivateContextInvalidated: () => {}, fetch, ...extra });
}

test('requires explicit secure origin and cleanup/token boundaries', () => {
  for (const url of ['http://account.invalid', 'https://user:pass@account.invalid', 'https://account.invalid?token=x',
    'https://account.invalid#x', 'https://account.invalid/prefix', 'file:///tmp/account']) {
    assert.throws(() => client(() => {}, { apiResource: url }), TypeError);
  }
  assert.throws(() => client(() => {}, { onPrivateContextInvalidated: undefined }), TypeError);
  assert.throws(() => client(() => {}, { getAccessToken: undefined }), TypeError);
  assert.throws(() => client(() => {}, { apiResource: 'http://not-local.invalid', allowLoopbackForIsolatedTests: true }), TypeError);
  assert.doesNotThrow(() => client(() => {}, { apiResource: 'http://127.0.0.1:1234', allowLoopbackForIsolatedTests: true }));
});

test('current account preserves exact wire body and uses protected fetch policy', async () => {
  const wire = { ...current, forwardCompatible: { source: 'server' } };
  const api = client(async (url, options) => {
    assert.equal(url, 'https://account.invalid/api/account/current');
    assert.equal(options.method, 'GET');
    assert.equal(options.headers.Authorization, 'Bearer unit-token');
    assert.equal(options.credentials, 'omit');
    assert.equal(options.redirect, 'error');
    assert.equal(options.cache, 'no-store');
    assert.equal(options.referrerPolicy, 'no-referrer');
    assert.equal(options.body, undefined);
    return json(wire);
  });
  assert.deepEqual(await api.getCurrent(), { ok: true, status: 200, body: wire });
});

test('profile patch preserves omitted fields and explicit nullable clears; no client entitlement body', async () => {
  const requests = [];
  const api = client(async (url, options) => { requests.push([url, options]); return json({ profile: { ...profile, revision: 2 } }); });
  const fields = { name: 'New name', pronouns: null };
  assert.equal((await api.updateProfile(1, fields)).ok, true);
  assert.equal(requests[0][0], 'https://account.invalid/api/account/profile');
  assert.equal(requests[0][1].method, 'PATCH');
  assert.deepEqual(JSON.parse(requests[0][1].body), { expectedRevision: 1, fields });
  assert.equal(Object.hasOwn(JSON.parse(requests[0][1].body).fields, 'job'), false);
  assert.equal((await api.updateProfile(1, { entitlement: 'Business' })).error.code, 'InvalidArgument');
  assert.equal((await api.updateProfile(0, fields)).error.code, 'InvalidArgument');
  assert.equal((await api.updateProfile(1, { name: null })).error.code, 'InvalidArgument');
  assert.equal(requests.length, 1);
});

test('source conflict and error codes remain unchanged, with revision; no mutation retries', async () => {
  let calls = 0;
  const wire = { error: 'profile_conflict', revision: 7 };
  const api = client(async () => { calls++; return json(wire, 409); });
  const result = await api.updateProfile(1, { name: 'New' });
  assert.deepEqual(result, { ok: false, status: 409, error: { code: 'profile_conflict', action: 'UpdateProfile', body: wire } });
  assert.equal(calls, 1);
});

test('malformed success, invalid identity/schema and invented success are rejected', async () => {
  for (const wire of [{}, { accountId: 'display-name', displayName: 'Fixture' }, { ...current, accountId: accountId.toUpperCase() }]) {
    assert.equal((await client(async () => json(wire)).getCurrent()).error.code, 'MalformedResponse');
  }
  assert.equal((await client(async () => json({ profile: { ...profile, job: undefined } })).getProfile()).error.code, 'MalformedResponse');
  assert.equal((await client(async () => new Response('not json', { status: 200 })).getCurrent()).error.code, 'MalformedResponse');
  assert.equal((await client(async () => new Response(null, { status: 204 })).getCurrent()).error.code, 'MalformedResponse');
  assert.equal((await client(async () => json({ success: true })).signOut()).error.code, 'MalformedResponse');
  assert.equal((await client(async () => json({ error: 'profile_conflict', revision: -1 }, 409)).getProfile()).error.code, 'MalformedResponse');
});

test('session list retains source fields and rejects mixed-account or duplicate identities', async () => {
  const one = { sessionId, accountId, deviceName: 'Fixture browser', createdAt: '2026-10-03T00:00:00Z',
    expiresAt: '2026-10-04T00:00:00Z', revokedAt: null, registeredClientId: null };
  assert.deepEqual((await client(async () => json({ sessions: [one] })).listSessions()).body, { sessions: [one] });
  for (const sessions of [[one, one], [one, { ...one, sessionId: accountId, accountId: sessionId }], [{ ...one, expiresAt: 'unknown' }]]) {
    assert.equal((await client(async () => json({ sessions })).listSessions()).error.code, 'MalformedResponse');
  }
});

test('session mutations clear prior context and exactly use source routes/status', async () => {
  const events = [];
  const api = client(async (url, options) => { events.push([url, options.method, options.body]); return new Response(null, { status: 204 }); },
    { onPrivateContextInvalidated: reason => events.push(reason) });
  for (const result of [await api.signOut(), await api.revokeSession(sessionId), await api.revokeAllOtherSessions()]) {
    assert.deepEqual(result, { ok: true, status: 204, body: null });
  }
  assert.deepEqual(events, [
    'session_mutation', ['https://account.invalid/api/account/signout', 'POST', undefined],
    'session_mutation', [`https://account.invalid/api/account/sessions/${sessionId}`, 'DELETE', undefined],
    'session_mutation', ['https://account.invalid/api/account/revoke-other-sessions', 'POST', undefined],
  ]);
  assert.equal((await api.revokeSession('../admin')).error.code, 'InvalidArgument');
  assert.equal(events.length, 6);
});

test('revoked/expired server session invalidates other pending requests and preserves source failure', async () => {
  const late = pending();
  let requests = 0;
  let cleared = 0;
  const api = client(async () => ++requests === 1 ? late.promise : json({ error: 'session_revoked_or_expired' }, 401),
    { onPrivateContextInvalidated: () => { cleared++; } });
  const stale = api.getProfile();
  await Promise.resolve();
  const expired = await api.getCurrent();
  assert.equal(expired.error.code, 'session_revoked_or_expired');
  assert.equal(cleared, 1);
  late.resolve(json({ profile }));
  assert.equal((await stale).error.code, 'SessionContextChanged');
});

test('explicit account/organisation switch drops late private result', async () => {
  const delayed = pending();
  const calls = [];
  const api = client(async () => delayed.promise, { onPrivateContextInvalidated: reason => calls.push(reason) });
  const old = api.getProfile();
  await Promise.resolve();
  await api.invalidatePrivateContext('account_switched');
  delayed.resolve(json({ profile }));
  assert.equal((await old).error.code, 'SessionContextChanged');
  assert.deepEqual(calls, ['account_switched']);
});

test('cancelled token acquisition and fetch never become successful data', async () => {
  const token = pending();
  const abort = new AbortController();
  let calls = 0;
  const api = client(async () => { calls++; return json(current); }, { getAccessToken: () => token.promise });
  const result = api.getCurrent({ signal: abort.signal });
  abort.abort(); token.resolve('unit-token');
  assert.equal((await result).error.code, 'Cancelled');
  assert.equal(calls, 0);
  const cancelled = new AbortController(); cancelled.abort();
  assert.equal((await api.getCurrent({ signal: cancelled.signal })).error.code, 'Cancelled');
});

test('transport errors and missing authentication never log/expose tokens or retry writes', async () => {
  let calls = 0;
  const api = client(async () => { calls++; throw new Error('unit-token: secret transport detail'); });
  const result = await api.updateProfile(1, { name: 'New' });
  assert.equal(result.error.code, 'TransportUnavailable');
  assert.equal(JSON.stringify(result).includes('unit-token'), false);
  assert.equal(calls, 1);
  const missing = client(async () => { throw Error('must not fetch'); }, { getAccessToken: async () => null });
  assert.equal((await missing.getCurrent()).error.code, 'AuthenticationRequired');
});

test('failed cleanup blocks mutation before any dispatch', async () => {
  let calls = 0;
  const api = client(async () => { calls++; return new Response(null, { status: 204 }); },
    { onPrivateContextInvalidated: () => { throw new Error('cleanup'); } });
  assert.equal((await api.signOut()).error.code, 'PrivateContextCleanupFailed');
  assert.equal(calls, 0);
});

test('session mutations bind original token before cleanup can replace/remove token context', async () => {
  for (const replacement of ['different-account-token', null]) {
    let token = 'original-session-token';
    let cleared = false;
    let tokenReads = 0;
    const api = client(async (_url, options) => {
      assert.equal(cleared, true);
      assert.equal(options.headers.Authorization, 'Bearer original-session-token');
      return new Response(null, { status: 204 });
    }, { getAccessToken: async () => { tokenReads++; return token; },
      onPrivateContextInvalidated: () => { cleared = true; token = replacement; } });
    assert.equal((await api.signOut()).ok, true);
    assert.equal(tokenReads, 1);
  }
});

test('concurrent context switch during asynchronous session cleanup prevents dispatch', async () => {
  const cleanup = pending();
  const entered = pending();
  let calls = 0;
  const api = client(async () => { calls++; return new Response(null, { status: 204 }); },
    { onPrivateContextInvalidated: async reason => { if (reason === 'session_mutation') { entered.resolve(); await cleanup.promise; } } });
  const mutation = api.revokeAllOtherSessions();
  await entered.promise;
  await api.invalidatePrivateContext('different_account');
  cleanup.resolve();
  assert.equal((await mutation).error.code, 'SessionContextChanged');
  assert.equal(calls, 0);
});

test('invalid original token cannot be replaced by a new account token during cleanup', async () => {
  for (const initial of [undefined, null, '', 'invalid token']) {
    let token = initial;
    let calls = 0;
    let tokenReads = 0;
    const api = client(async () => { calls++; return new Response(null, { status: 204 }); },
      { getAccessToken: () => { tokenReads++; return token; }, onPrivateContextInvalidated: () => { token = 'new-account-token'; } });
    assert.equal((await api.signOut()).error.code, 'AuthenticationRequired');
    assert.equal(tokenReads, 1);
    assert.equal(calls, 0);
  }
});

test('401 from previous context cannot surface after an additional switch while reading its body', async () => {
  const reading = pending();
  const release = pending();
  const api = client(async () => ({ ok: false, status: 401, redirected: false, url: '',
    json: async () => { reading.resolve(); await release.promise; return { error: 'unauthorized' }; } }));
  const result = api.getCurrent();
  await reading.promise;
  await api.invalidatePrivateContext('account_switched');
  release.resolve();
  assert.equal((await result).error.code, 'SessionContextChanged');
});

test('real-fetch successful body cancellation remains Cancelled instead of malformed JSON', async t => {
  const clearing = pending();
  const server = createServer((req, res) => {
    res.writeHead(200, { 'content-type': 'application/json' });
    res.flushHeaders();
  });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  t.after(() => { server.closeAllConnections(); server.close(); });
  const abort = new AbortController();
  const api = client(async (...args) => {
    const response = await globalThis.fetch(...args);
    const original = response.json.bind(response);
    response.json = () => { clearing.resolve(); return original(); };
    return response;
  }, { apiResource: `http://127.0.0.1:${server.address().port}`, allowLoopbackForIsolatedTests: true });
  const result = api.getCurrent({ signal: abort.signal });
  await clearing.promise;
  abort.abort();
  assert.equal((await result).error.code, 'Cancelled');
});

test('401 and missing token preserve cleanup failure distinctly', async () => {
  const extra = { onPrivateContextInvalidated: () => { throw Error('cannot clear private view'); } };
  assert.equal((await client(async () => json({ error: 'unauthorized' }, 401), extra).getCurrent()).error.code, 'PrivateContextCleanupFailed');
  assert.equal((await client(async () => { throw Error('no dispatch'); }, { ...extra, getAccessToken: () => null }).getCurrent()).error.code, 'PrivateContextCleanupFailed');
});

test('redirect responses and foreign response URL are rejected', async () => {
  assert.equal((await client(async () => new Response(null, { status: 302, headers: { location: 'https://other.invalid' } })).getCurrent()).error.code, 'UnexpectedRedirect');
  const response = json(current);
  Object.defineProperty(response, 'url', { value: 'https://other.invalid/api/account/current' });
  assert.equal((await client(async () => response).getCurrent()).error.code, 'UnexpectedRedirect');
});

test('streamed real-fetch 401 body survives invalidation; explicit body-phase abort is cancelled', async t => {
  let releaseBody;
  const bodyReady = pending();
  const server = createServer((req, res) => {
    res.writeHead(401, { 'content-type': 'application/json' });
    res.flushHeaders();
    releaseBody = () => res.end(JSON.stringify({ error: 'session_revoked_or_expired' }));
    bodyReady.resolve();
  });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  t.after(() => { server.closeAllConnections(); server.close(); });
  const cleared = pending();
  const api = client(globalThis.fetch, { apiResource: `http://127.0.0.1:${server.address().port}`,
    allowLoopbackForIsolatedTests: true, onPrivateContextInvalidated: () => cleared.resolve() });
  const result = api.getCurrent();
  await bodyReady.promise;
  await cleared.promise;
  releaseBody();
  assert.equal((await result).error.code, 'session_revoked_or_expired');

  const reading = pending();
  const abort = new AbortController();
  const cancelled = client(async () => ({ ok: false, status: 409, redirected: false, url: '',
    json: async () => { reading.resolve(); await new Promise(done => abort.signal.addEventListener('abort', done, { once: true })); return { error: 'profile_conflict', revision: 2 }; } }));
  const aborted = cancelled.getProfile({ signal: abort.signal });
  await reading.promise;
  abort.abort();
  assert.equal((await aborted).error.code, 'Cancelled');
});

test('real fetch loopback redirect does not transmit bearer to second origin (transport fixture, not issuer acceptance)', async t => {
  let targetRequests = 0;
  const target = createServer((req, res) => { targetRequests++; res.end('unexpected'); });
  target.listen(0, '127.0.0.1'); await once(target, 'listening');
  const source = createServer((req, res) => {
    assert.equal(req.headers.authorization, 'Bearer unit-token');
    res.writeHead(302, { location: `http://127.0.0.1:${target.address().port}/leak` }); res.end();
  });
  source.listen(0, '127.0.0.1'); await once(source, 'listening');
  t.after(async () => { await Promise.all([new Promise(done => source.close(done)), new Promise(done => target.close(done))]); });
  const api = client(globalThis.fetch, { apiResource: `http://127.0.0.1:${source.address().port}`, allowLoopbackForIsolatedTests: true });
  assert.equal((await api.getCurrent()).ok, false);
  assert.equal(targetRequests, 0);
});
