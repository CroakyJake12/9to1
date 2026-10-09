import test from 'node:test';
import assert from 'node:assert/strict';
import { createAccountModule } from './account-service.js';
import { AccountApiClient } from '../Auth/account-api-client.js';

const accountId = '12345678-1234-4234-8234-123456789abc';
const profile = revision => ({ accountId, name: 'Fixture', username: 'fixture', icon: null, pronouns: null, job: null, revision });
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const api = (fetch, extra = {}) => new AccountApiClient({ apiResource: 'https://account.invalid',
  getAccessToken: async () => 'unit-token', onPrivateContextInvalidated: () => {}, fetch, ...extra });

test('unconfigured bridge reports unavailable and unknown actions never dispatch', async () => {
  assert.equal(JSON.parse(await createAccountModule().invoke('1', 'GetCurrent', 'null')).error.code, 'ServiceUnavailable');
  let calls = 0;
  const module = createAccountModule(api(async () => { calls++; throw Error('must not dispatch'); }));
  for (const [action, args] of [['GrantEntitlement', '{}'], ['constructor', '{}'], ['GetCurrent', 'broken']])
    assert.equal(JSON.parse(await module.invoke('1', action, args)).error.code, 'InvalidArgument');
  assert.equal(calls, 0);
});

test('bridge forwards reviewed current/profile CAS replies without a parallel model', async () => {
  const requests = [];
  const module = createAccountModule(api(async (url, options) => {
    requests.push([url, options]);
    return Response.json(options.method === 'PATCH' ? { profile: profile(2), extension: 'retained' } : { accountId, displayName: 'Fixture' });
  }));
  assert.equal(JSON.parse(await module.invoke('1', 'GetCurrent', 'null')).body.accountId, accountId);
  const result = JSON.parse(await module.invoke('2', 'UpdateProfile', JSON.stringify({ expectedRevision: 1, fields: { pronouns: null } })));
  assert.equal(result.ok, true); assert.equal(result.body.extension, 'retained');
  assert.deepEqual(JSON.parse(requests[1][1].body), { expectedRevision: 1, fields: { pronouns: null } });
});

test('bridge cancellation and disposal discard late responses and close future calls', async () => {
  const late = deferred();
  const module = createAccountModule(api(async () => late.promise));
  const pending = module.invoke('1', 'GetCurrent', 'null');
  module.cancel('1'); late.resolve(Response.json({ accountId, displayName: 'Fixture' }));
  assert.equal(JSON.parse(await pending).error.code, 'Cancelled');
  module.dispose();
  assert.equal(JSON.parse(await module.invoke('2', 'GetCurrent', 'null')).error.code, 'ServiceUnavailable');
});

test('bridge preserves original-token mutation through private-view cleanup', async () => {
  let token = 'original-token', clears = 0;
  const module = createAccountModule(api(async (_url, options) => {
    assert.equal(options.headers.Authorization, 'Bearer original-token');
    assert.equal(clears, 1); return new Response(null, { status: 204 });
  }, { getAccessToken: async () => token, onPrivateContextInvalidated: () => { clears++; token = null; } }));
  assert.deepEqual(JSON.parse(await module.invoke('1', 'SignOut', 'null')), { ok: true, status: 204, body: null });
});

test('controller registration precedes synchronous token-supplier cancellation', async () => {
  let module;
  let calls = 0;
  module = createAccountModule(api(async () => { calls++; return Response.json({ accountId, displayName: 'Fixture' }); },
    { getAccessToken: () => { module.cancel('1'); return 'unit-token'; } }));
  assert.equal(JSON.parse(await module.invoke('1', 'GetCurrent', 'null')).error.code, 'Cancelled');
  assert.equal(calls, 0, 'synchronous cancellation during invoke must prevent network dispatch');
});
