// Supporting controls: no scripted input establishes a real authenticated account.
import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { saveRedirectTransaction, captureRedirectCallback, consumeRedirectCallback } from './browser-redirect-transaction.js';
import { BrowserPublicClient, captureOAuthRedirectCallback } from './configured-accounts.bundle.js';
const origin = 'https://client.example.test:5096';
const config = { issuer: 'http://127.0.0.1:8798/api/auth', apiResource: 'http://127.0.0.1:8798', clientId: 'unit-public-config',
  redirectUri: origin + '/oauth/callback', scopes: 'openid cake:account:read', allowLoopbackForIsolatedTests: true };
const metadata = { issuer: config.issuer, authorization_endpoint: config.issuer + '/oauth2/authorize', token_endpoint: config.issuer + '/oauth2/token', jwks_uri: config.issuer + '/jwks' };
const pending = { state: 's'.repeat(43), nonce: 'n'.repeat(43), verifier: 'v'.repeat(64) };
function windowFor(url = origin + '/#/app.spaces', storage = new Map()) {
  let current = new URL(url), navigate;
  const navigation = new Promise(resolve => { navigate = resolve; });
  return { storage, navigation, sessionStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
    location: { get origin() { return current.origin; }, get pathname() { return current.pathname; }, get href() { return current.href; }, get hash() { return current.hash; }, assign: navigate },
    history: { replaceState(_state, _title, path) { current = new URL(path, current); } } };
}
function client(win, fetch, extra = {}) {
  return new BrowserPublicClient({ configuration: config, window: win, crypto: webcrypto, fetch,
    signInMode: 'redirect', canRedirect: () => true, onBeforeSignIn: async () => {},
    verifyCurrentAccount: () => assert.fail('Negative controls cannot grant an account'), onVerifiedIdentity: () => assert.fail('Negative controls cannot register identity'),
    onSignInFailed: async () => {}, onTokenExpired: async () => {}, onFailure: () => {}, ...extra });
}
test('callback consumes storage and history, restores only original fragment, and its receipt is one-use', () => {
  const start = windowFor(); saveRedirectTransaction(config, start, pending, 1000);
  const callback = windowFor(config.redirectUri + '?state=' + pending.state + '&code=negative', start.storage);
  const receipt = captureRedirectCallback(config, callback, 1001);
  assert.equal(start.storage.size, 0); assert.equal(callback.location.href, origin + '/#/app.spaces');
  const captured = consumeRedirectCallback(receipt, config, 1002); assert.equal(captured.transaction.verifier, pending.verifier);
  assert.throws(() => consumeRedirectCallback(receipt, config, 1003), { code: 'RedirectTransactionUnavailable' });
});
test('expired, future, malformed, foreign configuration and extra-field transactions fail closed and are removed', () => {
  for (const mutate of [record => { record.createdAt = -600001; }, record => { record.createdAt = 2000; },
    record => { record.binding = 'foreign'; }, record => { record.verifier = 'short'; }, record => { record.accessToken = 'never-accepted'; },
    record => { record.returnFragment = 'https://foreign.example/'; }]) {
    const start = windowFor(); saveRedirectTransaction(config, start, pending, 1000);
    const [key, raw] = [...start.storage][0], record = JSON.parse(raw); mutate(record); start.storage.set(key, JSON.stringify(record));
    const callback = windowFor(config.redirectUri + '?state=' + pending.state + '&code=negative', start.storage);
    const receipt = captureRedirectCallback(config, callback, 1001); assert.equal(start.storage.size, 0); assert.equal(callback.location.href, origin + '/');
    assert.throws(() => consumeRedirectCallback(receipt, config, 1002), { code: 'InvalidRedirectTransaction' });
  }
});
test('duplicate, incorrect or fragment-bearing callback state is rejected before any exchange', () => {
  for (const query of ['?state=wrong', '?state=' + pending.state + '&state=' + pending.state, '?state=' + pending.state + '#unexpected']) {
    const start = windowFor(); saveRedirectTransaction(config, start, pending, 1000);
    const callback = windowFor(config.redirectUri + query, start.storage);
    const receipt = captureRedirectCallback(config, callback, 1001);
    assert.throws(() => consumeRedirectCallback(receipt, config, 1002), { code: 'StateMismatch' }); assert.equal(start.storage.size, 0);
  }
});
test('receipt expires during startup, changed configuration cannot consume it and abandoned normal load clears handoff', () => {
  const start = windowFor(); saveRedirectTransaction(config, start, pending, 1000);
  let callback = windowFor(config.redirectUri + '?state=' + pending.state, start.storage);
  let receipt = captureRedirectCallback(config, callback, 1001);
  assert.throws(() => consumeRedirectCallback(receipt, config, 601001), { code: 'InvalidRedirectTransaction' });
  saveRedirectTransaction(config, start, pending, 1000); callback = windowFor(config.redirectUri + '?state=' + pending.state, start.storage);
  receipt = captureRedirectCallback(config, callback, 1001);
  assert.throws(() => consumeRedirectCallback(receipt, { ...config, clientId: 'other' }, 1002), { code: 'InvalidRedirectTransaction' });
  saveRedirectTransaction(config, start, pending, 1000); assert.equal(captureRedirectCallback(config, start, 1001), null); assert.equal(start.storage.size, 0);
});
test('storage denial and unsaved drafts prevent outgoing navigation', async () => {
  const win = windowFor(); let requests = 0;
  let broker = client(win, async () => { requests++; return new Response(JSON.stringify(metadata)); }, { canRedirect: () => false });
  await assert.rejects(broker.signIn(), { code: 'UnsavedChanges' }); assert.equal(requests, 0); await broker.disposeAsync();
  win.sessionStorage.setItem = () => { throw new Error('disabled'); };
  broker = client(win, async () => new Response(JSON.stringify(metadata)));
  await assert.rejects(broker.signIn(), { code: 'RedirectStorageUnavailable' }); assert.equal(win.storage.size, 0); await broker.disposeAsync();
});
test('real S256 handoff survives departure cancellation, is consumed before requests, and unsigned tokens/replay never grant identity', async () => {
  const start = windowFor(), controller = new AbortController();
  const first = client(start, async () => new Response(JSON.stringify(metadata)));
  const leaving = first.signIn({ signal: controller.signal }); leaving.catch(() => {});
  const authorization = new URL(await start.navigation), stored = JSON.parse([...start.storage.values()][0]);
  assert.equal(authorization.searchParams.get('code_challenge_method'), 'S256');
  assert.equal(authorization.searchParams.get('code_challenge'), Buffer.from(await webcrypto.subtle.digest('SHA-256', new TextEncoder().encode(stored.verifier))).toString('base64url'));
  assert.equal(authorization.searchParams.get('state'), stored.state); assert.notEqual(stored.state, stored.nonce);
  assert.deepEqual(Object.keys(stored).sort(), ['binding', 'createdAt', 'nonce', 'returnFragment', 'state', 'verifier', 'version']);
  controller.abort(); await assert.rejects(leaving, { code: 'Cancelled' }); await first.disposeAsync(); assert.equal(start.storage.size, 1);
  const returned = windowFor(config.redirectUri + '?state=' + stored.state + '&code=negative', start.storage);
  const receipt = captureOAuthRedirectCallback(config, returned); assert.equal(start.storage.size, 0);
  let exchanges = 0;
  const next = client(returned, async url => { assert.equal(start.storage.size, 0); assert.equal(returned.location.search, undefined);
    if (url === metadata.token_endpoint) { exchanges++; return new Response(JSON.stringify({ token_type: 'Bearer', access_token: 'invalid', id_token: 'invalid' })); }
    return new Response(JSON.stringify(metadata)); });
  await assert.rejects(next.resumeRedirect(receipt), { code: 'TokenVerificationFailed' }); assert.equal(exchanges, 1); assert.equal(next.getAccessToken(), null);
  await assert.rejects(next.resumeRedirect(receipt), { code: 'RedirectTransactionUnavailable' }); assert.equal(exchanges, 1); await next.disposeAsync();
});
