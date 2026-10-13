// Synthetic signed issuer/API controls. These do not prove a live CAKE/Access login or product permissions.
import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { generateKeyPair, exportJWK, SignJWT } from 'jose';
import { createConfiguredAccounts } from './configured-accounts.js';
import { createDulcheProvider } from '../wwwroot/dulche-provider.js';
import { createPrivateContextLifecycle } from '../wwwroot/browser-private-context.js';

const subject = '11111111-1111-4111-8111-111111111111';
const other = '22222222-2222-4222-8222-222222222222';
const sid = '33333333-3333-4333-8333-333333333333';
const origin = 'https://task-client.example.test';
const config = Object.freeze({ issuer: 'https://issuer.example.test/api/auth', apiResource: 'https://issuer.example.test',
  clientId: 'synthetic-task-client', redirectUri: origin + '/callback', scopes: 'openid cake:account:read cake:profile:read' });
const metadata = { issuer: config.issuer, authorization_endpoint: config.issuer + '/oauth2/authorize',
  token_endpoint: config.issuer + '/oauth2/token', jwks_uri: config.issuer + '/jwks' };
function gate() { let release; const task = new Promise(resolve => { release = resolve; }); return { task, release }; }
function fakeWindow() {
  const listeners = new Set(); const navigated = gate();
  const popup = { closed: false, location: { replace(url) { popup.url = url; navigated.release(url); } }, close() { popup.closed = true; } };
  return { location: { origin }, popup, navigation: navigated.task, open() { return popup; },
    addEventListener(type, callback) { if (type === 'message') listeners.add(callback); },
    removeEventListener(type, callback) { if (type === 'message') listeners.delete(callback); },
    callback(url) { for (const callback of [...listeners]) callback({ origin, source: popup, data: { type: 'nineToOne.oauth.callback', callbackUri: url } }); } };
}
async function rig(options = {}) {
  const keys = await generateKeyPair('EdDSA', { crv: 'Ed25519', extractable: true });
  const jwk = { ...await exportJWK(keys.publicKey), kid: 'synthetic-ed25519', alg: 'EdDSA', use: 'sig' };
  const win = fakeWindow(); let access, accounts, currentCount = 0, profileCount = 0;
  let revoked = false, wrongProfile = false, deniedProfile = false, revokedProfile = false, tokenPosts = 0;
  const fetch = async (url, request = {}) => {
    if (url === config.issuer + '/.well-known/openid-configuration') return Response.json(metadata);
    if (String(url) === metadata.jwks_uri) return Response.json({ keys: [jwk] });
    if (url === metadata.token_endpoint) {
      tokenPosts++;
      const body = new URLSearchParams(request.body);
      assert.equal(body.get('grant_type'), 'authorization_code'); assert.equal(body.get('resource'), config.apiResource);
      const authorize = new URL(win.popup.url); const now = Math.floor(Date.now() / 1000);
      const signingKey = options.wrongSignature ? (await generateKeyPair('EdDSA', { crv: 'Ed25519' })).privateKey : keys.privateKey;
      const sign = payload => new SignJWT(payload).setProtectedHeader({ alg: 'EdDSA', kid: jwk.kid }).setIssuer(config.issuer)
        .setSubject(subject).setIssuedAt(now).setExpirationTime(now + 600).sign(signingKey);
      const id = await sign({ aud: config.clientId, nonce: authorize.searchParams.get('nonce'), azp: config.clientId });
      access = await sign({ aud: config.apiResource, sid });
      return Response.json({ token_type: 'Bearer', access_token: access, id_token: id });
    }
    assert.equal(request.headers.Authorization, `Bearer ${access}`);
    if (url.startsWith(origin + '/api/dulche/')) {
      assert.equal(request.credentials, 'same-origin'); assert.equal(request.redirect, 'error'); assert.equal(request.referrerPolicy, 'no-referrer');
      if (!options.providerFetch) assert.fail('No synthetic inference transport supplied');
      return options.providerFetch(url, request);
    }
    assert.equal(request.credentials, 'omit'); assert.equal(request.redirect, 'error');
    if (url === config.apiResource + '/api/account/current') {
      currentCount++;
      if (options.currentRead) await options.currentRead();
      return revoked ? Response.json({ error: { code: 'Unauthenticated' } }, { status: 401 }) : Response.json({ accountId: subject, displayName: 'Synthetic account' });
    }
    if (url === config.apiResource + '/api/account/profile') {
      profileCount++;
      if (options.profileRead) await options.profileRead();
      if (revokedProfile) return Response.json({ error: 'Unauthenticated' }, { status: 401 });
      if (deniedProfile) return Response.json({ error: { code: 'InsufficientScope' } }, { status: 403 });
      return Response.json({ profile: { accountId: wrongProfile ? other : subject, revision: 7, name: 'Synthetic', username: 'synthetic', icon: null, pronouns: null, job: null } });
    }
    assert.fail('Unexpected synthetic transport target: ' + url);
  };
  accounts = createConfiguredAccounts({ configuration: config, window: win, fetch, crypto: webcrypto,
    beginPrivateContextInvalidation: options.begin,
    onPrivateContextInvalidated: options.clear ?? (async () => {}), onVerifiedIdentity: options.verified ?? (async () => {}), onFailure: () => {} });
  await accounts.prepare();
  const login = async () => {
    const original = accounts.requestSignIn('synthetic-login');
    const authorize = new URL(await Promise.race([win.navigation, original.then(() => assert.fail('Login settled before actual popup navigation.'))]));
    assert.equal(authorize.searchParams.get('code_challenge_method'), 'S256');
    win.callback(config.redirectUri + '?state=' + authorize.searchParams.get('state') + '&code=synthetic-one-use');
    return original;
  };
  return { accounts, login, counts: () => ({ currentCount, profileCount, tokenPosts }),
    revoke: () => { revoked = true; }, revokeProfile: () => { revokedProfile = true; }, wrongProfile: () => { wrongProfile = true; }, denyProfile: () => { deniedProfile = true; } };
}
async function usingRig(options, body) {
  const original = await rig(options);
  let error;
  try { await body(original); } catch (failure) { error = failure; }
  let cleanup;
  try { cleanup = original.accounts.disposeAsync(); await cleanup; }
  catch (failure) { if (error) throw new AggregateError([error, failure], 'Control and actual account owner close failed.'); throw failure; }
  if (error) throw error;
}

for (const endpoint of ['inference', 'current', 'profile']) {
  test(`${endpoint} 401 fences immediately and its original provider begin settles before enclosing native reset joins`, { timeout: 1500 }, async () => {
    let provider, active = false, fenced = false, originalSettled = false;
    const extraDrain = gate(), entered = gate();
    const lifecycle = createPrivateContextLifecycle({
      beginOwnerReset() {
        if (!active) return Promise.resolve();
        fenced = true;
        return (async () => {
          await provider.dispose('self-join');
          assert.equal(originalSettled, true);
          entered.release();
          await extraDrain.task;
        })();
      }, clearPresentation() {},
    });
    const r = await rig({ begin: reason => lifecycle.begin(reason), clear: () => lifecycle.join(),
      providerFetch: async () => Response.json({ error: 'Unauthenticated' }, { status: 401 }) });
    try {
      assert.equal(JSON.parse(await r.login()).ok, true);
      provider = createDulcheProvider(r.accounts.createInferenceOwner());
      if (endpoint === 'current') r.revoke();
      if (endpoint === 'profile') r.revokeProfile();
      active = true;
      const original = provider.begin('self-join', 'GET', 'api/tags', null, () => true);
      original.then(() => { originalSettled = true; }, () => { originalSettled = true; });
      assert.deepEqual(JSON.parse(await original), { retired: true });
      assert.equal(fenced, true);
      await entered.task;
      let closed = false;
      const closing = r.accounts.disposeAsync().then(() => { closed = true; });
      const native = lifecycle.join();
      await Promise.resolve();
      if (endpoint === 'inference') assert.equal(closed, false);
      extraDrain.release();
      await Promise.all([closing, native]);
    } finally { extraDrain.release(); }
  });
}

test('inference uses only original verified account observations and fixed same-origin paths', async () => {
  let requests = 0;
  await usingRig({ providerFetch: async url => { requests++; assert.equal(url, origin + '/api/dulche/api/tags'); return Response.json({ models: [] }); } }, async r => {
    const owner = r.accounts.createInferenceOwner();
    await assert.rejects(owner.readIdentity(), { code: 'AuthenticationRequired' });
    assert.equal(JSON.parse(await r.login()).ok, true);
    const observed = await owner.readIdentity(); assert.equal(owner.isCurrent(observed), true);
    await assert.rejects(owner.fetch({ ...observed }, 'GET', 'api/tags', null), { code: 'AuthenticationRequired' });
    await assert.rejects(owner.fetch(observed, 'POST', 'api/pull', '{}'), { code: 'InvalidInferenceRequest' });
    assert.equal(requests, 0);
    assert.equal((await owner.fetch(observed, 'GET', 'api/tags', null)).status, 200); assert.equal(requests, 1);
    await r.accounts.invalidatePrivateContext('retire-inference'); assert.equal(owner.isCurrent(observed), false);
    await assert.rejects(owner.fetch(observed, 'GET', 'api/tags', null), { code: 'AuthenticationRequired' }); assert.equal(requests, 1);
  });
});

test('private inference participant fences with account retirement and terminal account close joins its exact drain', async () => {
  const release = gate(); let invalidations = 0, drained = false;
  await usingRig({}, async r => {
    const owner = r.accounts.createInferenceOwner();
    owner.attach({ invalidate() { invalidations++; }, async disposeAsync() { await release.task; drained = true; } });
    assert.equal(JSON.parse(await r.login()).ok, true); assert.ok(invalidations >= 1);
    await r.accounts.invalidatePrivateContext('private-owner-changed'); assert.ok(invalidations >= 2);
    let settled = false; const closing = r.accounts.disposeAsync().then(() => { settled = true; });
    await Promise.resolve(); assert.equal(settled, false); release.release(); await closing; assert.equal(drained, true);
  });
});

test('signed Task identity appears only after real current account and private owner replacement ACK', async () => {
  const entered = gate(), release = gate();
  await usingRig({ verified: async () => { entered.release(); await release.task; } }, async r => {
    assert.equal(JSON.parse(await r.accounts.readTaskIdentity('before')).ok, false);
    const login = r.login();
    try {
      await Promise.race([entered.task, login.then(() => assert.fail('Sign-in settled before owner replacement entered.'))]);
      assert.equal(JSON.parse(await r.accounts.readTaskIdentity('during-owner-replace')).ok, false);
      release.release(); assert.equal(JSON.parse(await login).ok, true);
      const reply = JSON.parse(await r.accounts.readTaskIdentity('after'));
      assert.equal(reply.ok, true); assert.equal(reply.identity.accountId, subject); assert.equal(reply.identity.profileAccountId, subject);
      assert.equal(reply.identity.sessionId, sid); assert.equal(reply.identity.profileRevision, 7);
      assert.equal(reply.identity.issuer, config.issuer); assert.equal(reply.identity.ownerEpoch.length, 43);
      assert.equal(Object.hasOwn(reply.identity, 'access_token'), false); assert.equal(Object.hasOwn(reply.identity, 'id_token'), false);
      assert.equal(r.accounts.confirmTaskIdentity('after'), true); assert.equal(r.accounts.confirmTaskIdentity('after'), false);
      assert.equal(r.counts().tokenPosts, 1); assert.equal(r.counts().profileCount, 1);
    } finally { release.release(); await login; }
  });
});

test('server-revoked signed session refuses Task identity and clears the same broker session', async () => {
  await usingRig({}, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true); r.revoke();
    await assert.rejects(r.accounts.readTaskIdentity('revoked'), { code: 'TaskIdentityUnavailable' });
    assert.equal(JSON.parse(await r.accounts.readTaskIdentity('after-revoke')).ok, false);
    assert.equal(r.counts().profileCount, 0); assert.equal(r.counts().tokenPosts, 1);
  });
});

test('actual profile owned by another subject never becomes the verified Task profile', async () => {
  await usingRig({}, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true); r.wrongProfile();
    await assert.rejects(r.accounts.readTaskIdentity('wrong-profile'), { code: 'TaskProfileVerificationFailed' });
    assert.equal(r.counts().profileCount, 1); assert.equal(r.counts().tokenPosts, 1);
  });
});

test('generation retirement while actual profile read is held refuses stale signed facts', async () => {
  const entered = gate(), release = gate();
  await usingRig({ profileRead: async () => { entered.release(); await release.task; } }, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true);
    const actual = r.accounts.readTaskIdentity('held-profile');
    try {
      await Promise.race([entered.task, actual.then(() => assert.fail('Read settled before real profile hold.'))]);
      await r.accounts.invalidatePrivateContext('synthetic-retirement'); release.release();
      await assert.rejects(actual); assert.equal(JSON.parse(await r.accounts.readTaskIdentity('after-retirement')).ok, false);
    } finally { release.release(); await Promise.allSettled([actual]); }
  });
});

test('actual profile Task is joined by the same terminal account owner before close completes', async () => {
  const entered = gate(), release = gate();
  await usingRig({ profileRead: async () => { entered.release(); await release.task; } }, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true);
    const actual = r.accounts.readTaskIdentity('held-close'); let close, settled = false;
    try {
      await Promise.race([entered.task, actual.then(() => assert.fail('Read settled before real profile hold.'))]);
      close = r.accounts.disposeAsync(); assert.equal(close, r.accounts.disposeAsync());
      close.then(() => { settled = true; }, () => { settled = true; }); await Promise.resolve(); assert.equal(settled, false);
      release.release(); await Promise.allSettled([actual]); await close;
      assert.equal(settled, true);
    } finally { release.release(); await Promise.allSettled([actual, close]); }
  });
});

test('expired memory session never starts a fresh API request or repeats the code exchange', async () => {
  await usingRig({}, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true); const now = Date.now; const fixed = now() + 601_000;
    try {
      Date.now = () => fixed;
      assert.equal(JSON.parse(await r.accounts.readTaskIdentity('expired')).ok, false);
      assert.deepEqual(r.counts(), { currentCount: 1, profileCount: 0, tokenPosts: 1 });
    } finally { Date.now = now; }
  });
});

test('wrong EdDSA signature grants neither Task identity nor canonical account verification', async () => {
  await usingRig({ wrongSignature: true }, async r => {
    assert.equal(JSON.parse(await r.login()).ok, false);
    assert.equal(JSON.parse(await r.accounts.readTaskIdentity('invalid-signature')).ok, false);
    assert.deepEqual(r.counts(), { currentCount: 0, profileCount: 0, tokenPosts: 1 });
  });
});

test('profile scope denial is unavailable Task identity and preserves ordinary current account access', async () => {
  await usingRig({}, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true); r.denyProfile();
    await assert.rejects(r.accounts.readTaskIdentity('denied-profile'), { code: 'TaskIdentityUnavailable' });
    const current = JSON.parse(await r.accounts.invoke('ordinary-read', 'GetCurrent', 'null'));
    assert.equal(current.ok, true); assert.equal(current.body.accountId, subject); assert.equal(r.counts().tokenPosts, 1);
  });
});

test('caller cancellation cannot publish held profile facts and actual request still drains', async () => {
  const entered = gate(), release = gate();
  await usingRig({ profileRead: async () => { entered.release(); await release.task; } }, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true);
    const actual = r.accounts.readTaskIdentity('cancelled');
    try {
      await Promise.race([entered.task, actual.then(() => assert.fail('Read settled before real profile hold.'))]);
      r.accounts.cancelTaskIdentity('cancelled'); release.release(); await assert.rejects(actual);
      assert.equal(r.counts().profileCount, 1); assert.equal(r.counts().tokenPosts, 1);
    } finally { release.release(); await Promise.allSettled([actual]); }
  });
});

test('session retirement after JSON response still invalidates the privately issued final observation', async () => {
  await usingRig({}, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true);
    const reply = JSON.parse(await r.accounts.readTaskIdentity('returned-before-retirement'));
    assert.equal(reply.ok, true);
    assert.equal(r.accounts.confirmTaskIdentity('copied-or-unissued-id'), false);
    await r.accounts.invalidatePrivateContext('synthetic-final-bridge-race');
    assert.equal(r.accounts.confirmTaskIdentity('returned-before-retirement'), false);
    assert.equal(r.accounts.confirmTaskIdentity('returned-before-retirement'), false);
  });
});

test('released Task identity read receipt cannot be consumed or reconstructed from its JSON', async () => {
  await usingRig({}, async r => {
    assert.equal(JSON.parse(await r.login()).ok, true);
    const reply = JSON.parse(await r.accounts.readTaskIdentity('released'));
    assert.equal(reply.ok, true); r.accounts.releaseTaskIdentity('released');
    assert.equal(r.accounts.confirmTaskIdentity('released'), false);
    assert.equal(r.accounts.confirmTaskIdentity(reply.identity), false);
    assert.equal(r.counts().tokenPosts, 1);
  });
});

test('unconfigured broker cannot derive actor from caller identity-looking arguments', async () => {
  const accounts = createConfiguredAccounts({ configuration: null, window: fakeWindow(),
    onPrivateContextInvalidated: async () => {}, onVerifiedIdentity: async () => assert.fail('No identity producer'), onFailure: () => {} });
  try {
    await accounts.prepare();
    const result = JSON.parse(await accounts.readTaskIdentity(subject, { accountId: subject, sessionId: sid, generation: 1 }));
    assert.equal(result.ok, false); assert.equal(Object.hasOwn(result, 'identity'), false);
  } finally { await accounts.disposeAsync(); }
});

test('inference-origin retirement keeps its exact later native cleanup failure through terminal account disposal', { timeout: 1500 }, async () => {
  let provider, active = false;
  const originalFailure = new Error('native retirement drain failed after original HTTP completion');
  const release = gate(), entered = gate();
  const lifecycle = createPrivateContextLifecycle({ beginOwnerReset() {
    if (!active) return Promise.resolve();
    return (async () => { await provider.dispose('retirement-failure'); entered.release(); await release.task; throw originalFailure; })();
  }, clearPresentation() {} });
  const r = await rig({ begin: reason => lifecycle.begin(reason), clear: () => lifecycle.join(),
    providerFetch: async () => Response.json({ error: 'Unauthenticated' }, { status: 401 }) });
  assert.equal(JSON.parse(await r.login()).ok, true);
  provider = createDulcheProvider(r.accounts.createInferenceOwner()); active = true;
  assert.deepEqual(JSON.parse(await provider.begin('retirement-failure', 'GET', 'api/tags', null, () => true)), { retired: true });
  await entered.task;
  const closing = r.accounts.disposeAsync(); assert.equal(closing, r.accounts.disposeAsync());
  release.release();
  const leaves = error => error instanceof AggregateError ? error.errors.flatMap(leaves) : [error];
  await assert.rejects(closing, error => leaves(error).length === 1 && leaves(error)[0] === originalFailure);
  await assert.rejects(lifecycle.join(), error => leaves(error).includes(originalFailure));
});
