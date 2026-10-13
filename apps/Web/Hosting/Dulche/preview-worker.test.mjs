// Isolated transport controls; synthetic account responses are never product acceptance.
import test from 'node:test';
import assert from 'node:assert/strict';
import worker from './preview-worker.mjs';
const preview = 'https://preview.example.test';
const subject = '11111111-1111-4111-8111-111111111111';
const env = { PREVIEW_ORIGIN: preview, ACCOUNT_ORIGIN: 'https://account.example.test', DULCHE_ORIGIN: 'https://runtime.example.test',
  MODEL_ID: 'qwen3:1.7b', RUNTIME_ACCESS_CLIENT_ID: 'synthetic-id', RUNTIME_ACCESS_CLIENT_SECRET: 'synthetic-not-secret',
  ASSETS: { fetch: () => new Response('actual asset owner fixture') }, ACCOUNT_AUTH: { fetch: () => Response.json({ accountId: subject }) } };
const request = (path, options = {}) => new Request(preview + path, { ...options,
  headers: { Origin: preview, Authorization: 'Bearer synthetic-unit', 'Content-Type': 'application/json', ...options.headers } });
test('unrelated assets remain with original owner; foreign and unsupported routes never contact authority/runtime', async () => {
  const blocked = { ...env, ACCOUNT_AUTH: { fetch: () => assert.fail('No authority request') } };
  assert.equal(await (await worker.fetch(request('/'), blocked)).text(), 'actual asset owner fixture');
  assert.equal((await worker.fetch(request('/api/dulche/api/tags', { headers: { Origin: 'https://foreign.example' } }), blocked)).status, 403);
  assert.equal((await worker.fetch(request('/api/dulche/api/pull', { method: 'POST', body: '{}' }), blocked)).status, 404);
});
test('anonymous and revoked bearer are denied before any runtime dispatch', async () => {
  const actualFetch = globalThis.fetch; let runtimeCalls = 0;
  globalThis.fetch = async () => { runtimeCalls++; assert.fail('No runtime request'); };
  try {
    assert.equal((await worker.fetch(new Request(preview + '/api/dulche/api/tags'), env)).status, 401);
    assert.equal((await worker.fetch(request('/api/dulche/api/tags'), { ...env, ACCOUNT_AUTH: { fetch: () => Response.json({ error: 'session_revoked_or_expired' }, { status: 401 }) } })).status, 401);
    assert.equal(runtimeCalls, 0);
  } finally { globalThis.fetch = actualFetch; }
});
test('wrong models and oversized bodies are refused without runtime requests', async () => {
  const actualFetch = globalThis.fetch;
  globalThis.fetch = async () => assert.fail('No runtime request');
  try {
    assert.equal((await worker.fetch(request('/api/dulche/api/chat', { method: 'POST', body: JSON.stringify({ model: 'other', messages: [{}], stream: true }) }), env)).status, 400);
    assert.equal((await worker.fetch(request('/api/dulche/api/chat', { method: 'POST', body: 'x'.repeat(1_048_577) }), env)).status, 413);
  } finally { globalThis.fetch = actualFetch; }
});
test('original body streams with fixed destination and service credentials; no client-supplied route or cookie is forwarded', async () => {
  const actualFetch = globalThis.fetch;
  globalThis.fetch = async (url, options) => {
    assert.equal(url.href, 'https://runtime.example.test/api/chat'); assert.equal(options.headers['CF-Access-Client-Secret'], 'synthetic-not-secret');
    assert.equal(options.headers.Cookie, undefined); assert.equal(options.redirect, 'manual');
    assert.equal(new TextDecoder().decode(options.body), JSON.stringify({ model: 'qwen3:1.7b', messages: [{ role: 'user', content: 'unit' }], stream: true }));
    return new Response('original bytes\n', { headers: { 'Content-Type': 'application/x-ndjson' } });
  };
  try {
    const response = await worker.fetch(request('/api/dulche/api/chat', { method: 'POST', body: JSON.stringify({ model: 'qwen3:1.7b', messages: [{ role: 'user', content: 'unit' }], stream: true }) }), env);
    assert.equal(response.status, 200); assert.equal(await response.text(), 'original bytes\n'); assert.equal(response.headers.get('cache-control'), 'no-store');
  } finally { globalThis.fetch = actualFetch; }
});
test('a session revoked while awaiting bytes cannot publish the late chunk and cancels the original upstream', async () => {
  const actualFetch = globalThis.fetch; let supply, cancelled = 0, reads = 0;
  globalThis.fetch = async () => new Response(new ReadableStream({ start(controller) { supply = controller; }, cancel() { cancelled++; } }));
  try {
    const response = await worker.fetch(request('/api/dulche/api/tags'), { ...env, ACCOUNT_AUTH: { fetch: () => ++reads === 1 ? Response.json({ accountId: subject }) : Response.json({ error: 'unauthorized' }, { status: 401 }) } });
    const reading = response.text(); reading.catch(() => {}); supply.enqueue(new TextEncoder().encode('must not escape'));
    await assert.rejects(reading); assert.equal(cancelled, 1);
  } finally { globalThis.fetch = actualFetch; }
});
