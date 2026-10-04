import test from 'node:test';
import assert from 'node:assert/strict';
import { AccountApiClient } from './account-api-client.js';

const accountId = '12345678-1234-4234-8234-123456789abc';
const profile = revision => ({ accountId, name: 'Fixture', username: 'fixture',
  icon: null, pronouns: null, job: null, revision });
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const client = (fetch, getAccessToken = async () => 'unit-token') => new AccountApiClient({
  apiResource: 'https://account.invalid', getAccessToken,
  onPrivateContextInvalidated: () => {}, fetch,
});

test('profile update snapshots scalar fields before deferred token acquisition', async () => {
  const token = deferred();
  const fields = { name: 'Approved edit', pronouns: null };
  let dispatched;
  const api = client(async (_url, options) => {
    dispatched = JSON.parse(options.body);
    return Response.json({ profile: profile(2) });
  }, () => token.promise);
  const result = api.updateProfile(1, fields);
  fields.name = 'Unapproved edit';
  delete fields.pronouns;
  fields.job = 'Unapproved addition';
  token.resolve('unit-token');
  assert.equal((await result).ok, true);
  assert.deepEqual(dispatched, { expectedRevision: 1, fields: { name: 'Approved edit', pronouns: null } });
});

test('post-call invalid fields and values cannot enter a validated profile patch', async () => {
  for (const mutate of [fields => { fields.entitlement = 'Business'; }, fields => { fields.name = { invalid: true }; }]) {
    const token = deferred();
    const fields = { name: 'Approved edit' };
    let dispatched;
    const api = client(async (_url, options) => {
      dispatched = JSON.parse(options.body);
      return Response.json({ profile: profile(2) });
    }, () => token.promise);
    const result = api.updateProfile(1, fields);
    mutate(fields);
    token.resolve('unit-token');
    assert.equal((await result).ok, true);
    assert.deepEqual(dispatched, { expectedRevision: 1, fields: { name: 'Approved edit' } });
  }
});

test('profile success must return exactly expected revision plus one', async () => {
  for (const revision of [1, 3, 0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1]) {
    let calls = 0;
    const api = client(async () => { calls++; return Response.json({ profile: profile(revision) }); });
    const result = await api.updateProfile(1, { name: 'Approved edit' });
    assert.equal(result.ok, false, `revision ${revision} must not acknowledge CAS success`);
    assert.equal(result.error.code, 'MalformedResponse');
    assert.equal(calls, 1, 'malformed success must not retry mutation');
  }
});

test('profile revision rejects unrepresentable next integer before token or network work', async () => {
  for (const revision of [Number.MAX_SAFE_INTEGER, Number.MAX_SAFE_INTEGER + 1, Infinity, NaN]) {
    let tokenReads = 0;
    let calls = 0;
    const api = client(async () => { calls++; return Response.json({ profile: profile(1) }); },
      async () => { tokenReads++; return 'unit-token'; });
    const result = await api.updateProfile(revision, { name: 'Approved edit' });
    assert.equal(result.ok, false);
    assert.equal(result.error.code, 'InvalidArgument');
    assert.equal(tokenReads, 0);
    assert.equal(calls, 0);
  }
});

test('sequential server CAS success preserves wire body at normal and safe integer upper bounds', async () => {
  for (const expectedRevision of [1, 7, Number.MAX_SAFE_INTEGER - 1]) {
    const wire = { profile: profile(expectedRevision + 1), serverExtension: 'preserved' };
    const api = client(async () => Response.json(wire));
    assert.deepEqual(await api.updateProfile(expectedRevision, { name: 'Approved edit' }), { ok: true, status: 200, body: wire });
  }
});

test('profile patch validates the same single scalar snapshot used for dispatch', async () => {
  let reads = 0;
  const fields = { get name() { reads++; return reads === 1 ? 'Approved edit' : { unapproved: true }; } };
  let dispatched;
  const api = client(async (_url, options) => {
    dispatched = JSON.parse(options.body);
    return Response.json({ profile: profile(2) });
  });
  const result = await api.updateProfile(1, fields);
  assert.equal(result.ok, true);
  assert.equal(reads, 1);
  assert.deepEqual(dispatched.fields, { name: 'Approved edit' });
});

test('caller serialization hooks cannot replace the validated profile fields', async () => {
  let hookCalls = 0;
  let dispatched;
  const fields = { name: 'Approved edit' };
  Object.defineProperty(fields, 'toJSON', { value: () => {
    hookCalls++;
    return { name: 'Unapproved edit', accountId: 'Unapproved identity' };
  } });
  const api = client(async (_url, options) => {
    dispatched = JSON.parse(options.body);
    return Response.json({ profile: profile(2) });
  });
  assert.equal((await api.updateProfile(1, fields)).ok, true);
  assert.equal(hookCalls, 0);
  assert.deepEqual(dispatched.fields, { name: 'Approved edit' });
});
