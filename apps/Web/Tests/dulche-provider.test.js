// Stream custody controls. These injected owners never establish authenticated product acceptance.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createDulcheProvider } from '../wwwroot/dulche-provider.js';
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
function setup(overrides = {}) {
  const identity = Object.freeze({ observed: true }); let valid = true, participant;
  const owner = { readIdentity: async () => identity, isCurrent: value => valid && value === identity,
    fetch: async () => new Response('aπz', { headers: { 'Content-Type': 'application/x-ndjson' } }),
    attach: value => { participant = value; return () => {}; }, ...overrides };
  const provider = createDulcheProvider(owner);
  return { owner, provider, invalidate: () => { valid = false; participant.invalidate(); }, dispose: () => participant.disposeAsync() };
}
test('whitelist rejects arbitrary path/method/body without reading identity or network', async () => {
  const fixture = setup({ readIdentity: () => assert.fail('No identity read permitted') });
  for (const args of [['x','GET','http://evil',null], ['x','POST','api/pull','{}'], ['x','GET','api/chat',null], ['x','GET','api/tags','body']])
    await assert.rejects(fixture.provider.begin(...args, () => true), { code: 'InvalidInferenceRequest' });
  await fixture.dispose();
});
test('real binary chunks preserve UTF8 bytes and response status/type through EOF', async () => {
  const fixture = setup(); assert.deepEqual(JSON.parse(await fixture.provider.begin('a', 'POST', 'api/chat', '{}', () => true)), { status: 200, contentType: 'application/x-ndjson' });
  const chunks = []; for (;;) { const next = JSON.parse(await fixture.provider.read('a')); if (next.done) break; chunks.push(Buffer.from(next.base64, 'base64')); }
  assert.equal(Buffer.concat(chunks).toString('utf8'), 'aπz'); await fixture.provider.dispose('a'); await fixture.dispose();
});
test('revocation during a noncooperative identity read waits for its exact operation and dispatches no fetch', async () => {
  const gate = deferred(); let fetches = 0;
  const fixture = setup({ readIdentity: () => gate.promise, fetch: () => { fetches++; assert.fail(); } });
  const opening = fixture.provider.begin('a','GET','api/tags',null, () => true); opening.catch(() => {});
  let closed = false; fixture.invalidate(); const closing = fixture.provider.cancel('a').then(() => { closed = true; });
  await Promise.resolve(); assert.equal(closed, false); gate.resolve({ stale: true });
  assert.deepEqual(JSON.parse(await opening), { retired: true }); await closing; assert.equal(fetches, 0); await fixture.dispose();
});
test('revocation during fetch cancels the exact returned body before closing', async () => {
  const gate = deferred(), entered = deferred(), cancelGate = deferred(); let cancelled = 0;
  const fixture = setup({ fetch: () => { entered.resolve(); return gate.promise; } });
  const opening = fixture.provider.begin('a','GET','api/tags',null, () => true); opening.catch(() => {}); await entered.promise;
  fixture.invalidate(); let closed = false; const closing = fixture.provider.cancel('a').then(() => { closed = true; });
  gate.resolve(new Response(new ReadableStream({ cancel() { cancelled++; return cancelGate.promise; } })));
  await new Promise(resolve => setImmediate(resolve)); assert.equal(closed, false); assert.equal(cancelled, 1);
  cancelGate.resolve(); assert.deepEqual(JSON.parse(await opening), { retired: true }); await closing; await fixture.dispose();
});
test('revocation fences a pending read and joins its original stream cancellation', async () => {
  const cancelled = deferred(); let streamController;
  const fixture = setup({ fetch: async () => new Response(new ReadableStream({ start(controller) { streamController = controller; }, cancel: () => cancelled.promise })) });
  await fixture.provider.begin('a','GET','api/tags',null, () => true);
  const reading = fixture.provider.read('a'); reading.catch(() => {});
  let closed = false; fixture.invalidate(); const closing = fixture.provider.cancel('a').then(() => { closed = true; });
  assert.deepEqual(JSON.parse(await reading), { retired: true }); assert.equal(closed, false);
  cancelled.resolve(); await closing; await fixture.dispose(); assert.ok(streamController);
});
test('buffered bytes cannot be returned after original identity is revoked', async () => {
  const fixture = setup({ fetch: async () => new Response(new Uint8Array(32768)) });
  await fixture.provider.begin('a','GET','api/tags',null, () => true); const first = JSON.parse(await fixture.provider.read('a')); assert.equal(Buffer.from(first.base64,'base64').length,16384);
  fixture.invalidate(); assert.deepEqual(JSON.parse(await fixture.provider.read('a')), { retired: true }); await fixture.dispose();
});
test('concurrent reads are rejected and original cancellation failure is retained by identity', async () => {
  const original = new Error('original stream cancellation failed');
  const fixture = setup({ fetch: async () => new Response(new ReadableStream({ cancel() { throw original; } })) });
  await fixture.provider.begin('a','GET','api/tags',null, () => true);
  const read = fixture.provider.read('a'); read.catch(() => {});
  await assert.rejects(fixture.provider.read('a'), { code: 'ProviderReadInProgress' });
  await assert.rejects(fixture.provider.cancel('a'), error => error instanceof AggregateError && error.errors.length === 1 && error.errors[0] === original);
  assert.deepEqual(JSON.parse(await read), { retired: true });
  await assert.rejects(fixture.dispose(), error => error instanceof AggregateError && error.errors[0].errors[0] === original);
});
for (const original of [Object.assign(new Error('foreign lookalike'), { code: 'SessionContextChanged' }), new DOMException('cleanup AbortError', 'AbortError')]) {
  test(`cleanup retains the exact ${original.name}/${original.code} error even after revocation`, async () => {
    const gate = deferred(), entered = deferred();
    const fixture = setup({ fetch: () => { entered.resolve(); return gate.promise; } });
    const opening = fixture.provider.begin('a', 'GET', 'api/tags', null, () => true);
    opening.catch(() => {}); await entered.promise; fixture.invalidate();
    gate.resolve(new Response(new ReadableStream({ cancel() { throw original; } })));
    await assert.rejects(opening, error => error === original);
    await assert.rejects(fixture.provider.dispose('a'), error => error instanceof AggregateError && error.errors.length === 1 && error.errors[0] === original);
    await fixture.dispose();
  });
}
test('foreign request error with a cancellation-looking code remains an original failure', async () => {
  const original = Object.assign(new Error('foreign request failure'), { code: 'Cancelled' });
  const gate = deferred(), entered = deferred();
  const fixture = setup({ fetch: () => { entered.resolve(); return gate.promise; } });
  const opening = fixture.provider.begin('a', 'GET', 'api/tags', null, () => true); opening.catch(() => {});
  await entered.promise; fixture.invalidate(); gate.reject(original);
  await assert.rejects(opening, error => error === original);
  await assert.rejects(fixture.provider.dispose('a'), error => error.errors.length === 1 && error.errors[0] === original);
  await fixture.dispose();
});
test('foreign original AbortError is retained when its rejection races revocation', async () => {
  const original = new DOMException('foreign original abort', 'AbortError');
  const gate = deferred(), entered = deferred();
  const fixture = setup({ fetch: () => { entered.resolve(); return gate.promise; } });
  const opening = fixture.provider.begin('a', 'GET', 'api/tags', null, () => true); opening.catch(() => {});
  await entered.promise; fixture.invalidate(); gate.reject(original);
  await assert.rejects(opening, error => error === original);
  await assert.rejects(fixture.provider.dispose('a'), error => error.errors.length === 1 && error.errors[0] === original);
  await fixture.dispose();
});
test('only the exact original AbortSignal reason produces a retired request terminal', async () => {
  const entered = deferred();
  const fixture = setup({ fetch: (_identity, _method, _path, _body, signal) => new Promise((_resolve, reject) => {
    signal.addEventListener('abort', () => reject(signal.reason), { once: true }); entered.resolve();
  }) });
  const opening = fixture.provider.begin('a', 'GET', 'api/tags', null, () => true); await entered.promise; fixture.invalidate();
  assert.deepEqual(JSON.parse(await opening), { retired: true }); await fixture.provider.dispose('a'); await fixture.dispose();
});
test('native policy withdrawal during identity read refuses the exact original dispatch', async () => {
  const gate = deferred(); let permitted = true, fetches = 0;
  const fixture = setup({ readIdentity: () => gate.promise, isCurrent: () => true, fetch: () => { fetches++; assert.fail('Withdrawn policy cannot dispatch'); } });
  const opening = fixture.provider.begin('policy', 'GET', 'api/tags', null, () => permitted);
  permitted = false; gate.resolve({ observed: true });
  assert.deepEqual(JSON.parse(await opening), { retired: true }); assert.equal(fetches, 0);
  await fixture.provider.dispose('policy'); await fixture.dispose();
});
test('native admission callback failure is retained exactly and async admission never grants dispatch', async () => {
  const original = new Error('native admission failure');
  for (const callback of [() => { throw original; }, async () => true]) {
    const fixture = setup({ fetch: () => assert.fail('Failed admission cannot dispatch') });
    const opening = fixture.provider.begin('policy', 'GET', 'api/tags', null, callback);
    await assert.rejects(opening, error => error === original || error.code === 'InvalidInferenceAdmission');
    await assert.rejects(fixture.provider.dispose('policy'));
    await fixture.dispose();
  }
});
