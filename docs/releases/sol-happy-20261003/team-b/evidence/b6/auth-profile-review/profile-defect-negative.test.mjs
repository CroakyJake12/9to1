import test from 'node:test';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
const modulePath = process.env.B6_ACCOUNT_CLIENT;
const { AccountApiClient } = await import(modulePath ? pathToFileURL(modulePath).href : './original-account-api-client.mjs');
const accountId = '11111111-1111-4111-8111-111111111111';
const row = revision => ({ accountId, name: 'Initial', username: 'fixture', icon: null, pronouns: null, job: null, revision });
const response = revision => new Response(JSON.stringify({ profile: row(revision) }), { status: 200, headers: { 'content-type': 'application/json' } });
const client = overrides => new AccountApiClient({ apiResource: 'https://isolated.example.invalid', getAccessToken: () => 'isolated-fixture-token', onPrivateContextInvalidated: async () => {}, ...overrides });
test('validated caller profile fields are snapshotted before token await', async () => {
  let releaseToken;
  let sent;
  const token = new Promise(resolve => { releaseToken = resolve; });
  const c = client({ getAccessToken: () => token, fetch: async (_url, options) => { sent = JSON.parse(options.body); return response(8); } });
  const fields = { name: 'Initial' };
  const pending = c.updateProfile(7, fields);
  fields.name = 'Changed while token pending';
  fields.accountId = '22222222-2222-4222-8222-222222222222';
  releaseToken('isolated-fixture-token');
  const result = await pending;
  console.log(JSON.stringify({ control: 'caller-mutation', sent, resultCode: result.error?.code ?? 'ACCEPTED' }));
  assert.equal(result.ok, true);
  assert.deepEqual(sent, { expectedRevision: 7, fields: { name: 'Initial' } });
});
test('profile PATCH success must advance expected revision by exactly one', async () => {
  for (const returnedRevision of [7, 6, 9, 8]) {
    const c = client({ fetch: async () => response(returnedRevision) });
    const result = await c.updateProfile(7, { name: 'Initial' });
    console.log(JSON.stringify({ control: 'revision-increment', expectedRevision: 7, returnedRevision, resultCode: result.error?.code ?? 'ACCEPTED' }));
    if (returnedRevision === 8) assert.equal(result.ok, true);
    else { assert.equal(result.ok, false); assert.equal(result.error.code, 'MalformedResponse'); }
  }
});
