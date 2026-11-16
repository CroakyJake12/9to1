import test from 'node:test';
import assert from 'node:assert/strict';
import { demandOwner, demandMutationOrigin, readJsonBody, readBoundedBytes, guid, readCookie, errorResponse } from '../src/protocol.mjs';

const owner = '15d642d8-0e7d-4cbe-ae68-2e289dc9e218';
const session = 'x'.repeat(43);
const request = (extra = {}) => new Request('https://9to1.uk/api/files/v1/context', { headers: { cookie: `__Host-cake_session=${session}` }, ...extra });
function environment(current = { accountId: owner, displayName: 'Test owner' }, status = 200) {
  const calls = [];
  return { OWNER_ACCOUNT_ID: owner, calls, WEB_SESSIONS: { getByName(sid) { assert.equal(sid, session); return { async request(...args) { calls.push(args); return new Response(JSON.stringify(current), { status }); } }; } } };
}

test('owner is obtained from the maintained active session, never a caller header', async () => {
  const env = environment(); const result = await demandOwner(request(), env);
  assert.equal(result.accountId, owner);
  assert.deepEqual(env.calls, [['/api/account/current', 'GET', null, 'https://9to1.uk']]);
  await assert.rejects(demandOwner(request({ headers: { 'x-owner-id': owner } }), env), { status: 401 });
});
test('wrong account, stale session and unconfigured or foreign origin fail closed', async () => {
  await assert.rejects(demandOwner(request(), environment({ accountId: 'another' })), { status: 403 });
  await assert.rejects(demandOwner(request(), environment({}, 401)), { status: 401 });
  await assert.rejects(demandOwner(request(), {}), { status: 503 });
  await assert.rejects(demandOwner(new Request('https://attacker.invalid/api/files/v1/context'), environment()), { status: 503 });
});
test('duplicate session cookies are ambiguous and refused', async () => {
  const req = request({ headers: { cookie: `__Host-cake_session=${session}; __Host-cake_session=${session}` } });
  assert.equal(readCookie(req, '__Host-cake_session'), null);
  await assert.rejects(demandOwner(req, environment()), { status: 401 });
});
test('mutations require exact same-origin request', () => {
  assert.doesNotThrow(() => demandMutationOrigin(request({ method: 'POST', headers: { origin: 'https://9to1.uk', 'sec-fetch-site': 'same-origin' } })));
  for (const origin of [undefined, 'https://cakemods.com', 'https://9to1.uk.attacker.invalid']) {
    assert.throws(() => demandMutationOrigin(request({ method: 'POST', headers: origin ? { origin } : {} })), { status: 403 });
  }
});
test('bounded JSON rejects oversized, malformed and non-object inputs', async () => {
  const make = body => request({ method: 'POST', headers: { 'content-type': 'application/json' }, body });
  assert.deepEqual(await readJsonBody(make('{"valid":true}')), { valid: true });
  await assert.rejects(readJsonBody(make('{"name":"' + 'x'.repeat(100) + '"}'), 40), { code: 'ProviderResponseTooLarge' });
  for (const body of ['[]', 'null', '{']) await assert.rejects(readJsonBody(make(body)), { status: 400 });
});
test('canonical identity parsing and generic errors never expose implementation details', async () => {
  assert.equal(guid({ value: owner.toUpperCase() }), owner); assert.equal(guid(null, true), null);
  assert.throws(() => guid('../secrets'), { status: 400 });
  const response = errorResponse(new Error('access_token=private'));
  assert.equal(response.status, 503); assert.ok(!(await response.text()).includes('private'));
});
test('a pending body read can be cancelled even without fetch-owned stream cancellation', async () => {
  const controller = new AbortController(); let cancelled = false;
  const stream = new ReadableStream({ pull() { return new Promise(() => {}); }, cancel() { cancelled = true; } });
  const reading = readBoundedBytes(stream, 1024, controller.signal);
  controller.abort(); await assert.rejects(reading, { name: 'AbortError' });
  assert.equal(cancelled, true);
});
test('malformed account reply is a typed pre-provider refusal', async () => {
  for (const value of [null, [], 'not-account', {}]) await assert.rejects(demandOwner(request(), environment(value)), { status: 503, notStarted: true });
});
