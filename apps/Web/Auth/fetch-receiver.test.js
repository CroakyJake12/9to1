// Supporting UNIT receiver controls. Scripted responses grant no identity and make no network request.
// The real Chromium malformed-URL receiver witness is retained separately in the release evidence.
import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { BrowserPublicClient, createConfiguredAccounts } from './configured-accounts.bundle.js';
import { AccountApiClient } from './account-api-client.js';

const origin = 'https://client.example.test:5096';
const configuration = { issuer: 'https://issuer.example.test/api/auth', apiResource: 'https://issuer.example.test',
  clientId: 'unit-public-config', redirectUri: `${origin}/callback`, scopes: 'openid cake:account:read' };
const current = { accountId: '12345678-1234-4234-8234-123456789abc', displayName: 'Unit receiver control' };

function windowFixture() {
  const popup = { closed: false, location: { replace() { assert.fail('Issuer mismatch must prevent authorization navigation.'); } },
    close() { this.closed = true; } };
  return { location: { origin }, popup, open() { return popup; }, addEventListener() {}, removeEventListener() {} };
}
function broker(win, extra = {}) {
  return new BrowserPublicClient({ configuration, window: win, crypto: webcrypto,
    onBeforeSignIn: async () => {}, verifyCurrentAccount: async () => assert.fail('Unit receiver controls cannot verify accounts.'),
    onVerifiedIdentity: async () => assert.fail('Unit receiver controls cannot register identities.'),
    onSignInFailed: async () => {}, onTokenExpired: async () => {}, onFailure: () => {}, ...extra });
}
function api(extra = {}) {
  return new AccountApiClient({ apiResource: configuration.apiResource, getAccessToken: async () => 'unit-token',
    onPrivateContextInvalidated: async () => {}, ...extra });
}
async function withGlobalFetch(transport, body) {
  const original = Object.getOwnPropertyDescriptor(globalThis, 'fetch');
  Object.defineProperty(globalThis, 'fetch', { ...original, value: transport });
  try { await body(); } finally { Object.defineProperty(globalThis, 'fetch', original); }
}
function policy(options) {
  assert.equal(options.credentials, 'omit'); assert.equal(options.cache, 'no-store');
  assert.equal(options.redirect, 'error'); assert.equal(options.referrerPolicy, 'no-referrer');
  assert.ok(options.signal instanceof AbortSignal); assert.equal(options.signal.aborted, false);
}
function discovery(url, options) {
  assert.equal(url, `${configuration.issuer}/.well-known/openid-configuration`); policy(options);
  assert.equal(options.headers, undefined); assert.equal(options.body, undefined);
  return Response.json({ issuer: 'https://wrong.example.test/api/auth' });
}
function account(url, options) {
  assert.equal(url, `${configuration.apiResource}/api/account/current`); policy(options);
  assert.equal(options.method, 'GET'); assert.equal(options.headers.Authorization, 'Bearer unit-token');
  assert.equal(options.headers.Accept, 'application/json'); assert.equal(options.body, undefined);
  return Response.json(current);
}
function nativeReceiver(response, observed) {
  return async function (url, options) {
    // Model the independently observed native Window brand contract, not an auth authority.
    if (this !== globalThis) throw new TypeError('Illegal invocation');
    observed.calls++; return response(url, options);
  };
}
async function deniedBroker(client, win) {
  try {
    await assert.rejects(client.signIn(), { code: 'IssuerMismatch' });
    assert.equal(client.getAccessToken(), null); assert.equal(win.popup.closed, true);
  } finally { await client.disposeAsync(); }
}

for (const explicit of [false, true]) {
  test(`broker ${explicit ? 'explicit current global' : 'omitted default'} fetch uses Window receiver and preserves denial/policy`, async () => {
    const observed = { calls: 0 };
    await withGlobalFetch(nativeReceiver(discovery, observed), async () => {
      const win = windowFixture(); const client = broker(win, explicit ? { fetch: globalThis.fetch } : {});
      await deniedBroker(client, win); assert.equal(observed.calls, 1);
    });
  });
  test(`account API ${explicit ? 'explicit current global' : 'omitted default'} fetch uses Window receiver and preserves exact wire/policy`, async () => {
    const observed = { calls: 0 };
    await withGlobalFetch(nativeReceiver(account, observed), async () => {
      assert.deepEqual(await api(explicit ? { fetch: globalThis.fetch } : {}).getCurrent(), { ok: true, status: 200, body: current });
      assert.equal(observed.calls, 1);
    });
  });
}

test('configured composition forwards its native default without changing cleanup or granting identity', async () => {
  const observed = { calls: 0 }, reasons = [], failures = [];
  await withGlobalFetch(nativeReceiver(discovery, observed), async () => {
    const win = windowFixture();
    const accounts = createConfiguredAccounts({ configuration, window: win, crypto: webcrypto,
      onPrivateContextInvalidated: async reason => { reasons.push(reason); },
      onVerifiedIdentity: async () => assert.fail('No identity from scripted metadata.'), onFailure: code => failures.push(code) });
    try {
      await accounts.prepare(); assert.equal(accounts.signInAvailable(), true);
      assert.deepEqual(JSON.parse(await accounts.requestSignIn('unit-receiver')), { ok: false, error: { code: 'IssuerMismatch' } });
      assert.deepEqual(reasons, ['configuration_prepared', 'sign_in_started', 'sign_in_failed']);
      assert.deepEqual(failures, ['IssuerMismatch']); assert.equal(observed.calls, 1); assert.equal(win.popup.closed, true);
      assert.equal(JSON.parse(await accounts.invoke('unit-read', 'GetCurrent', 'null')).error.code, 'AuthenticationRequired');
    } finally { await accounts.disposeAsync(); }
  });
});

for (const kind of ['broker', 'account API']) {
  test(`${kind} captures native transport and receiver before later global replacement`, async () => {
    const observed = { calls: 0 };
    await withGlobalFetch(nativeReceiver(kind === 'broker' ? discovery : account, observed), async () => {
      const win = windowFixture(); const client = kind === 'broker' ? broker(win) : api();
      globalThis.fetch = () => assert.fail('Later global replacement must not change the captured transport.');
      if (kind === 'broker') await deniedBroker(client, win);
      else assert.deepEqual(await client.getCurrent(), { ok: true, status: 200, body: current });
      assert.equal(observed.calls, 1);
    });
  });
  test(`${kind} preserves injected non-global transport receiver and policy`, async () => {
    let client, calls = 0; const win = windowFixture();
    const transport = async function (url, options) {
      assert.equal(this, client); calls++; return (kind === 'broker' ? discovery : account)(url, options);
    };
    client = kind === 'broker' ? broker(win, { fetch: transport }) : api({ fetch: transport });
    if (kind === 'broker') await deniedBroker(client, win);
    else assert.deepEqual(await client.getCurrent(), { ok: true, status: 200, body: current });
    assert.equal(calls, 1);
  });
  test(`${kind} native-default receiver binding preserves caller cancellation`, async () => {
    let started, actualSignal; const entered = new Promise(resolve => { started = resolve; });
    await withGlobalFetch(async function (_url, options) {
      if (this !== globalThis) throw new TypeError('Illegal invocation');
      policy(options); actualSignal = options.signal; started();
      return new Promise((_resolve, reject) => options.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true }));
    }, async () => {
      const win = windowFixture(), controller = new AbortController(); const client = kind === 'broker' ? broker(win) : api();
      const work = kind === 'broker' ? client.signIn({ signal: controller.signal }) : client.getCurrent({ signal: controller.signal });
      // A premature receiver failure must fail promptly rather than hanging this control.
      try {
        await Promise.race([entered, work.then(() => assert.fail('Transport must remain pending until caller abort.'))]);
        controller.abort(); assert.equal(actualSignal.aborted, true);
        if (kind === 'broker') { await assert.rejects(work, { code: 'Cancelled' }); assert.equal(win.popup.closed, true); assert.equal(client.getAccessToken(), null); }
        else assert.equal((await work).error.code, 'Cancelled');
      } finally { controller.abort(); await work.catch(() => {}); if (kind === 'broker') await client.disposeAsync(); }
    });
  });
}
