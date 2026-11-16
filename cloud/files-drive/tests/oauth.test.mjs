import assert from 'node:assert/strict';
import { createHash, randomBytes } from 'node:crypto';
import { mkdtempSync, rmdirSync, unlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import test from 'node:test';
import { SignJWT, exportJWK, generateKeyPair } from 'jose';
import { GOOGLE_DRIVE_REDIRECT_URI, GoogleDriveOAuth } from '../src/oauth.mjs';

const clientId = 'files-test.apps.googleusercontent.com';
const accountId = '15d642d8-0e7d-4cbe-ae68-2e289dc9e218';
const email = 'owner@example.test';
const sub = '100000000000000000001';
const auth = { accountId, sessionDigest: randomBytes(32).toString('base64url') };
const scope = 'openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/drive';
const tokenEndpoint = 'https://oauth2.googleapis.com/token';
const certEndpoint = 'https://www.googleapis.com/oauth2/v3/certs';
const revokeEndpoint = 'https://oauth2.googleapis.com/revoke';
const key = await generateKeyPair('RS256', { extractable: true });
const jwk = { ...await exportJWK(key.publicKey), kid: 'fixture-key', alg: 'RS256', use: 'sig' };
const T0 = Date.UTC(2026, 10, 15, 18);
const toBytes = value => Uint8Array.from(Buffer.from(value === undefined ? '' : JSON.stringify(value)));

function fixture(t) {
  const dir = mkdtempSync(join(tmpdir(), 'files-oauth-'));
  const path = join(dir, 'oauth.sqlite');
  const env = { OWNER_ACCOUNT_ID: accountId, OWNER_GOOGLE_SUB: sub, OWNER_GOOGLE_EMAIL: email,
    GOOGLE_DRIVE_CLIENT_ID: clientId, GOOGLE_DRIVE_CLIENT_SECRET: 'fixture-client-secret',
    GOOGLE_DRIVE_PROJECT_ID: 'files-test-project', FILES_DEDICATED_GOOGLE_PROJECT: 'true',
    DRIVE_ENCRYPTION_KEY: randomBytes(32).toString('base64url') };
  const model = { now: T0, calls: [], nonce: '', jwt: {}, reply: {}, refresh: undefined, revoke: undefined };
  const opens = new Set();
  function open(overrides) {
    const db = new DatabaseSync(path);
    opens.add(db);
    db.exec('CREATE TABLE IF NOT EXISTS kv (key TEXT PRIMARY KEY, value TEXT NOT NULL)');
    const storage = {
      kv: {
        get(name) {
          const raw = db.prepare('SELECT value FROM kv WHERE key = ?').get(name)?.value;
          return raw === undefined ? undefined : JSON.parse(raw);
        },
        put(name, value) {
          db.prepare('INSERT INTO kv VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value')
            .run(name, JSON.stringify(value));
        },
        delete(name) { return db.prepare('DELETE FROM kv WHERE key = ?').run(name).changes > 0; },
      },
      transactionSync(fn) {
        db.exec('BEGIN IMMEDIATE');
        try { const out = fn(); db.exec('COMMIT'); return out; } catch (error) { db.exec('ROLLBACK'); throw error; }
      },
      async sync() { await storage.onSync?.(); },
    };
    const result = (status, value, headers = {}) => ({ status, bytes: toBytes(value), headers: new Headers(headers) });
    const transport = {
      async json(url, options) {
        model.calls.push({ url, options });
        assert.equal(url, certEndpoint);
        return { status: 200, value: { keys: [jwk] }, headers: new Headers({ 'cache-control': 'public, max-age=600' }) };
      },
      async request(url, options) {
        const form = new URLSearchParams(options.body);
        model.calls.push({ url, options, form });
        if (url === revokeEndpoint) return model.revoke ? model.revoke(form, result) : result(200);
        assert.equal(url, tokenEndpoint);
        if (form.get('grant_type') === 'refresh_token') {
          assert.ok(model.refresh, 'test must arrange provider refresh response');
          return model.refresh(form, result);
        }
        const instant = Math.floor(model.now / 1000);
        const jwt = new SignJWT({
          sub, email, email_verified: true, nonce: model.nonce, iss: 'https://accounts.google.com', aud: clientId,
          azp: clientId, iat: instant, exp: instant + 3600, ...model.jwt,
        }).setProtectedHeader({ alg: 'RS256', kid: 'fixture-key' });
        return result(200, {
          token_type: 'Bearer', expires_in: 3600, scope, access_token: 'test-access-a',
          refresh_token: 'test-refresh-a', id_token: await jwt.sign(key.privateKey), ...model.reply,
        });
      },
    };
    return {
      storage, oauth: new GoogleDriveOAuth(storage, { ...env, ...overrides }, transport, { now: () => model.now }),
      data: () => db.prepare('SELECT key, value FROM kv ORDER BY key').all(),
      close() { db.close(); opens.delete(db); },
    };
  }
  t.after(() => { for (const db of opens) db.close(); unlinkSync(path); rmdirSync(dir); });
  return { open, env, model, ...open() };
}

async function begin(f, oauth = f.oauth) {
  const url = new URL((await oauth.begin(auth)).url);
  f.model.nonce = url.searchParams.get('nonce');
  return url;
}

function callback(url, fields = {}) {
  return { ...auth, code: 'fixture-auth-code', state: url.searchParams.get('state'), ...fields };
}

async function linked(f) {
  const url = await begin(f);
  await f.oauth.complete(callback(url));
  return url;
}

test('consent uses the pinned callback and PKCE, and the signed owner grant survives SQLite restart as ciphertext', async t => {
  const f = fixture(t);
  const url = await begin(f);
  assert.equal(url.origin + url.pathname, 'https://accounts.google.com/o/oauth2/v2/auth');
  assert.equal(url.searchParams.get('redirect_uri'), GOOGLE_DRIVE_REDIRECT_URI);
  assert.equal(url.searchParams.get('response_type'), 'code');
  assert.equal(url.searchParams.get('access_type'), 'offline');
  assert.equal(url.searchParams.get('code_challenge_method'), 'S256');
  assert.equal(url.searchParams.get('scope'), 'openid email https://www.googleapis.com/auth/drive');
  assert.equal(f.model.calls.length, 0);
  const flowRows = JSON.stringify(f.data());
  assert.ok(!flowRows.includes(url.searchParams.get('state')));
  assert.ok(!flowRows.includes(url.searchParams.get('nonce')));
  assert.deepEqual(await f.oauth.complete(callback(url)), { connected: true, accountEmail: email });
  const [exchange, certs] = f.model.calls;
  assert.equal(exchange.url, tokenEndpoint);
  assert.equal(certs.url, certEndpoint);
  assert.ok(f.model.calls.every(call => call.options.kind === 'auth' && call.options.maxResponseBytes === 64 * 1024));
  assert.equal(exchange.form.get('grant_type'), 'authorization_code');
  assert.equal(exchange.form.get('redirect_uri'), GOOGLE_DRIVE_REDIRECT_URI);
  assert.equal(createHash('sha256').update(exchange.form.get('code_verifier')).digest('base64url'), url.searchParams.get('code_challenge'));
  const disk = JSON.stringify(f.data());
  for (const secret of ['test-access-a', 'test-refresh-a', 'fixture-client-secret', 'fixture-auth-code', email, sub]) {
    assert.ok(!disk.includes(secret));
  }
  f.close();
  const reopened = f.open();
  assert.deepEqual(await reopened.oauth.status(), { connected: true, accountEmail: email });
  assert.equal(await reopened.oauth.getAccessToken(), 'test-access-a');
});

test('wrong 9to1 session/state, expiry and replay stop before any Google exchange', async t => {
  const f = fixture(t);
  const url = await begin(f);
  for (const invalid of [
    { accountId: 'wrong-owner' }, { state: randomBytes(32).toString('base64url') },
    { sessionDigest: randomBytes(32).toString('base64url') },
  ]) {
    await assert.rejects(f.oauth.complete(callback(url, invalid)), { code: 'DriveOAuthFlowInvalid' });
  }
  assert.equal(f.model.calls.length, 0);
  const second = f.open();
  const outcomes = await Promise.allSettled([f.oauth.complete(callback(url)), second.oauth.complete(callback(url))]);
  assert.equal(outcomes.filter(x => x.status === 'fulfilled').length, 1);
  assert.equal(f.model.calls.filter(x => x.url === tokenEndpoint).length, 1);
  await assert.rejects(f.oauth.complete(callback(url)), { code: 'DriveOAuthFlowInvalid' });
  const expired = await begin(f);
  f.model.now += 10 * 60_000;
  await assert.rejects(f.oauth.complete(callback(expired)), { code: 'DriveOAuthFlowInvalid' });
  f.model.now = T0;
  await assert.rejects(second.oauth.complete(callback(expired)), { code: 'DriveOAuthFlowInvalid' });
  assert.equal(f.model.calls.filter(x => x.url === tokenEndpoint).length, 1);
});

test('signature verification rejects wrong Google issuer, audience, presenter, owner, nonce and verification', async t => {
  const f = fixture(t);
  for (const claims of [
    { iss: 'https://attacker.invalid' }, { aud: 'another-client' }, { aud: [clientId, 'other-client'] },
    { azp: 'another-client' }, { sub: 'another-owner' }, { nonce: 'wrong' },
    { email: 'someone-else@example.test' }, { email_verified: false }, { email_verified: 'true' },
    { exp: Math.floor(T0 / 1000) - 1 },
  ]) {
    const url = await begin(f);
    f.model.jwt = claims;
    await assert.rejects(f.oauth.complete(callback(url)), { code: 'DriveOwnerUnverified' });
    assert.deepEqual(await f.oauth.status(), { connected: false, accountEmail: null });
  }
  const url = await begin(f);
  f.model.jwt = {};
  const stolenSignature = await new SignJWT({ sub, email, email_verified: true, nonce: f.model.nonce,
    iss: 'https://accounts.google.com', aud: clientId, iat: T0 / 1000, exp: T0 / 1000 + 300 })
    .setProtectedHeader({ alg: 'RS256', kid: jwk.kid }).sign((await generateKeyPair('RS256')).privateKey);
  f.model.reply = { id_token: stolenSignature };
  await assert.rejects(f.oauth.complete(callback(url)), { code: 'DriveOwnerUnverified' });
  assert.equal(f.model.calls.filter(x => x.url === certEndpoint).length, 1);
});

test('rejects incomplete scopes, missing offline grant and a mismatched access-token hash', async t => {
  const f = fixture(t);
  for (const reply of [{ scope: 'openid email' }, { scope: scope + ' https://mail.google.com/' }, { refresh_token: undefined }]) {
    const url = await begin(f);
    f.model.reply = reply;
    await assert.rejects(f.oauth.complete(callback(url)),
      { code: reply.refresh_token === undefined && !reply.scope ? 'DriveOAuthResponseInvalid' : 'DriveScopeInsufficient' });
  }
  f.model.reply = {};
  f.model.jwt = { at_hash: 'some-other-token' };
  await assert.rejects(f.oauth.complete(callback(await begin(f))), { code: 'DriveOAuthResponseInvalid' });
  f.model.jwt = { at_hash: createHash('sha256').update('test-access-a').digest().subarray(0, 16).toString('base64url') };
  await f.oauth.complete(callback(await begin(f)));
});

test('a bounded number of pending authorizations can expire and be replaced', async t => {
  const f = fixture(t);
  for (let i = 0; i < 8; i++) await begin(f);
  await assert.rejects(begin(f), { code: 'DriveOAuthFlowsFull', retryAt: T0 + 600_000 });
  f.model.now += 600_000;
  await begin(f);
  assert.equal(f.model.calls.length, 0);
});

test('overlapping tabs share a single refresh; a rotated refresh token is used after restart', async t => {
  const f = fixture(t);
  await linked(f);
  f.model.now += 3_600_000;
  f.model.refresh = async (form, result) => {
    assert.equal(form.get('refresh_token'), 'test-refresh-a');
    await new Promise(resolve => setImmediate(resolve));
    return result(200, { token_type: 'Bearer', expires_in: 3600, access_token: 'test-access-b', refresh_token: 'test-refresh-b' });
  };
  assert.deepEqual(await Promise.all(Array.from({ length: 4 }, () => f.oauth.getAccessToken())), Array(4).fill('test-access-b'));
  assert.equal(f.model.calls.filter(x => x.form?.get('grant_type') === 'refresh_token').length, 1);
  f.close();
  f.model.now += 3_600_000;
  f.model.refresh = (form, result) => {
    assert.equal(form.get('refresh_token'), 'test-refresh-b');
    return result(200, { token_type: 'Bearer', expires_in: 3600, access_token: 'test-access-c' });
  };
  assert.equal(await f.open().oauth.getAccessToken(), 'test-access-c');
});

test('a cold restart during durable refresh cannot reuse the possibly rotated token', async t => {
  const f = fixture(t);
  await linked(f);
  f.model.now += 3_600_000;
  let entered, release;
  const inFlight = new Promise(resolve => { entered = resolve; });
  f.model.refresh = async () => {
    entered();
    await new Promise(resolve => { release = resolve; });
    throw new Error('Refresh reply lost');
  };
  const lost = f.oauth.getAccessToken();
  await inFlight;
  const reopened = f.open();
  assert.equal((await reopened.oauth.status()).reauthorizationRequired, true);
  await assert.rejects(reopened.oauth.getAccessToken(), { code: 'DriveReconnectRequired' });
  assert.equal(f.model.calls.filter(x => x.form?.get('grant_type') === 'refresh_token').length, 1);
  release();
  await assert.rejects(lost, { code: 'DriveReconnectRequired' });
  await assert.rejects(reopened.oauth.getAccessToken(), { code: 'DriveReconnectRequired' });
});

test('explicit 429 backs off, invalid_grant clears custody, and 500 never resends a possibly rotated credential', async t => {
  const f = fixture(t);
  await linked(f);
  f.model.now += 3_600_000;
  f.model.refresh = (_form, result) => result(429, { error: 'rate_limit' }, { 'retry-after': '999999' });
  const retryAt = f.model.now + 300_000;
  await assert.rejects(f.oauth.getAccessToken(), { code: 'DriveAuthTemporarilyUnavailable', retryAt });
  await assert.rejects(f.open().oauth.getAccessToken(), { code: 'DriveAuthTemporarilyUnavailable', retryAt });
  assert.equal(f.model.calls.filter(x => x.form?.get('grant_type') === 'refresh_token').length, 1);
  f.model.now = retryAt;
  f.model.refresh = (_form, result) => result(400, { error: 'invalid_grant' });
  await assert.rejects(f.oauth.getAccessToken(), { code: 'DriveReconnectRequired' });
  assert.deepEqual(await f.oauth.status(), { connected: false, accountEmail: null });
  await linked(f);
  f.model.now += 3_600_000;
  f.model.refresh = () => ({ status: 500, headers: new Headers({ 'retry-after': '1' }), bytes: Buffer.from('service offline') });
  await assert.rejects(f.oauth.getAccessToken(), { code: 'DriveReconnectRequired', retryAt: f.model.now + 5000 });
  f.model.now += 5000;
  await assert.rejects(f.oauth.getAccessToken(), { code: 'DriveReconnectRequired' });
  assert.equal(f.model.calls.filter(x => x.form?.get('grant_type') === 'refresh_token').length, 3);
});

test('a definitely unissued refresh can be tried again with the existing grant', async t => {
  const f = fixture(t);
  await linked(f);
  f.model.now += 3_600_000;
  f.model.refresh = () => { throw Object.assign(new Error('App budget refused'), { notStarted: true, code: 'quotaExceeded' }); };
  await assert.rejects(f.oauth.getAccessToken(), { code: 'quotaExceeded' });
  assert.equal((await f.oauth.status()).connected, true);
  f.model.refresh = (_form, result) => result(200, { token_type: 'Bearer', expires_in: 3800, access_token: 'test-access-b' });
  assert.equal(await f.oauth.getAccessToken(), 'test-access-b');
});

test('disconnect only revokes the Files grant and clears local custody even when Google reply is lost', async t => {
  const f = fixture(t);
  f.storage.kv.put('some-quota-key', { units: 15 });
  await linked(f);
  const pending = await begin(f);
  f.model.revoke = form => {
    assert.deepEqual([...form], [['token', 'test-refresh-a']]);
    throw new Error('revocation reply lost');
  };
  await assert.rejects(f.oauth.disconnect(), { code: 'DriveRevocationUnconfirmed' });
  assert.equal(f.model.calls.at(-1).url, revokeEndpoint);
  assert.deepEqual(await f.oauth.status(), { connected: false, accountEmail: null });
  assert.deepEqual(f.storage.kv.get('some-quota-key'), { units: 15 });
  await assert.rejects(f.oauth.complete(callback(pending)), { code: 'DriveOAuthFlowInvalid' });
  await linked(f);
  f.model.revoke = undefined;
  assert.deepEqual(await f.oauth.disconnect(), { connected: false, accountEmail: null });
  assert.equal(f.model.calls.filter(x => x.url === revokeEndpoint).length, 2);
});

test('requires dedicated project declaration, canonical encryption key and original grant credentials', async t => {
  const f = fixture(t);
  for (const config of [{ FILES_DEDICATED_GOOGLE_PROJECT: false }, { GOOGLE_DRIVE_PROJECT_ID: '' },
    { DRIVE_ENCRYPTION_KEY: 'not-a-key' }, { OWNER_GOOGLE_SUB: '' }, { OWNER_GOOGLE_EMAIL: '' }]) {
    assert.throws(() => f.open(config), { code: 'DriveOAuthNotConfigured' });
  }
  await linked(f);
  for (const config of [{ GOOGLE_DRIVE_CLIENT_ID: 'different-client.apps.googleusercontent.com' },
    { OWNER_ACCOUNT_ID: 'f57dc990-3983-4268-8f19-2188366db17a' }, { OWNER_GOOGLE_SUB: 'different-subject' },
    { GOOGLE_DRIVE_PROJECT_ID: 'some-other-project' }]) {
    const { oauth } = f.open(config);
    await assert.rejects(oauth.getAccessToken(), { code: 'DriveReconnectRequired' });
    assert.equal((await oauth.status()).reauthorizationRequired, true);
  }
});
