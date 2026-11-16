import test from 'node:test';
import assert from 'node:assert/strict';
import { GoogleTransport, CHUNK_BYTES } from '../src/google-transport.mjs';

const url = 'https://www.googleapis.com/drive/v3/files?pageSize=100';
const transportFor = (quota, options = {}) => new GoogleTransport(quota, {
  onSafetyFault: async () => {}, isSafetyHeld: async () => false, ...options,
});
function ledger({ refuse = false, stale = false } = {}) {
  const calls = [];
  return { calls,
    async reserve(value) { calls.push(['reserve', value]); if (refuse) throw Object.assign(new Error('Allowance exhausted.'), { name: 'QuotaError', status: 429 }); },
    async issue(id) { calls.push(['issue', id]); return { startBy: Date.now() + (stale ? -100 : 5000), deadlineAt: Date.now() + 120000 }; },
    async settle(...args) { calls.push(['settle', ...args]); },
  };
}
test('a real fetch attempt is preceded by reservation and durable issuance', async () => {
  const quota = ledger(); let request;
  const transport = transportFor(quota, { fetch: async (_, options) => {
    assert.deepEqual(quota.calls.map(x => x[0]), ['reserve', 'issue']); request = options;
    return Response.json({ files: [] });
  } });
  assert.deepEqual((await transport.json(url, { kind: 'list' })).value, { files: [] });
  assert.equal(request.redirect, 'manual'); assert.ok(request.signal instanceof AbortSignal);
  assert.equal(quota.calls.at(-1)[2], 'completed');
});
test('quota refusal and expired dispatch cannot send any upstream request', async () => {
  for (const option of [{ refuse: true }, { stale: true }]) {
    const quota = ledger(option); let fetched = false;
    const transport = transportFor(quota, { fetch: async () => { fetched = true; return Response.json({}); } });
    await assert.rejects(transport.json(url, { kind: 'list' })); assert.equal(fetched, false);
  }
});
test('network failures and caller retries each retain distinct charged attempts', async () => {
  const quota = ledger(); let count = 0;
  const transport = transportFor(quota, { fetch: async () => { count++; throw new Error('Lost reply'); } });
  await assert.rejects(transport.json(url, { kind: 'mutate', method: 'POST', body: '{}' }));
  assert.equal(count, 1); // No hidden automatic retry.
  await assert.rejects(transport.json(url, { kind: 'mutate', method: 'POST', body: '{}' }));
  const reservations = quota.calls.filter(x => x[0] === 'reserve');
  assert.notEqual(reservations[0][1].id, reservations[1][1].id);
  assert.ok(quota.calls.filter(x => x[0] === 'settle').every(x => x[2] === 'unknown'));
});
test('unexpected redirect is not followed and oversized body trips persistent circuit callback', async () => {
  let faults = 0;
  const redirected = transportFor(ledger(), { fetch: async () => new Response(null, { status: 302, headers: { location: 'https://attacker.invalid/' } }) });
  await assert.rejects(redirected.json(url, { kind: 'metadata' }), { code: 'ProviderRedirectRefused' });
  for (const withLength of [true, false]) {
    const transport = transportFor(ledger(), { onSafetyFault: async () => { faults++; }, fetch: async () => new Response('too-long', { headers: withLength ? { 'content-length': '8' } : {} }) });
    await assert.rejects(transport.request(url, { kind: 'metadata', maxResponseBytes: 4 }), { code: 'ProviderResponseTooLarge' });
  }
  assert.equal(faults, 2);
});
test('Google URL allowlist, bounded binary and exact upload length hold before reserve', async () => {
  const quota = ledger(); const transport = transportFor(quota);
  for (const bad of ['http://www.googleapis.com/drive/v3/files', 'https://www.googleapis.com.attacker.invalid/drive/v3/files', 'https://www.googleapis.com/other', 'https://secret@www.googleapis.com/drive/v3/files']) {
    await assert.rejects(transport.request(bad, { kind: 'metadata' }), { notStarted: true });
  }
  await assert.rejects(transport.request(url, { kind: 'upload', body: new Uint8Array(2), uploadBytes: 1 }), { code: 'InvalidUploadSize' });
  await assert.rejects(transport.request(url, { kind: 'upload', body: new Uint8Array(CHUNK_BYTES + 1), uploadBytes: CHUNK_BYTES + 1 }), { notStarted: true });
  assert.equal(quota.calls.length, 0);
});
test('only two provider bodies can be live; canceled reads retain allowance', async () => {
  const quota = ledger(); const controllers = []; const releases = [];
  const transport = transportFor(quota, { fetch: async (_, options) => {
    controllers.push(options.signal);
    await new Promise((resolve, reject) => { releases.push(resolve); options.signal.addEventListener('abort', () => reject(options.signal.reason), { once: true }); });
    return Response.json({});
  } });
  const cancel = new AbortController();
  const first = transport.json(url, { kind: 'metadata', signal: cancel.signal });
  const second = transport.json(url, { kind: 'metadata' });
  await new Promise(resolve => setImmediate(resolve));
  await assert.rejects(transport.json(url, { kind: 'metadata' }), { code: 'FilesBusy' });
  cancel.abort(); await assert.rejects(first); releases[1](); await second;
  assert.equal(controllers[0].aborted, true);
  assert.equal(quota.calls.filter(x => x[0] === 'reserve').length, 2);
});

test('failed durable issue is definitely not dispatched but keeps uncertain quota custody', async () => {
  const quota = ledger(); quota.issue = async () => { throw new Error('sync failed'); };
  let sent = false; const transport = transportFor(quota, { fetch: async () => { sent = true; } });
  await assert.rejects(transport.json(url, { kind: 'mutate', method: 'POST' }), { notStarted: true });
  assert.equal(sent, false);
});
test('oversized response latches the hold even when cancellation rejects', async () => {
  let calls = 0, held = false;
  const transport = transportFor(ledger(), { isSafetyHeld: async () => held, onSafetyFault: async () => { held = true; }, fetch: async () => {
    calls++; return { status: 200, headers: new Headers({ 'content-length': '100' }), body: { cancel() { throw new Error('cancel failed'); } } };
  } });
  await assert.rejects(transport.request(url, { kind: 'metadata', maxResponseBytes: 10 }), { code: 'ProviderResponseTooLarge' });
  assert.equal(held, true);
  await assert.rejects(transport.request(url, { kind: 'metadata' }), { code: 'ProviderSafetyHold', notStarted: true });
  assert.equal(calls, 1);
});
test('upload snapshot cannot grow or change during reservation', async () => {
  const source = new ArrayBuffer(2, { maxByteLength: CHUNK_BYTES + 1 });
  const view = new Uint8Array(source); view.set([11, 22]);
  const quota = ledger(); const originalReserve = quota.reserve;
  quota.reserve = async value => { await originalReserve(value); source.resize(CHUNK_BYTES + 1); view.fill(99); };
  let sent;
  const transport = transportFor(quota, { fetch: async (_, options) => { sent = options.body; return Response.json({}); } });
  await transport.request('https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable', { kind: 'upload', method: 'PUT', body: view, uploadBytes: 2 });
  assert.deepEqual([...sent], [11, 22]);
});
