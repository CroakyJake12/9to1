import assert from 'node:assert/strict';
import { createTaskExecutionHost } from '../../wwwroot/task-execution-host.js';

// Controlled module originals test ownership/fault behavior only. They do not stand in for
// real IndexedDB, a verified signed account, permissions, canonical coordinator or provider.
const deferred = () => {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  promise.catch(() => {});
  return { promise, resolve, reject };
};
const crypto = { randomUUID: (() => { let n = 0; return () => `private-owner-${++n}`; })() };
const causes = error => error instanceof AggregateError ? error.errors.flatMap(causes) : [error];
const failure = async actual => {
  try { await actual; } catch (error) { return error; }
  throw new Error('The actual original unexpectedly succeeded.');
};
let passed = 0;
const run = async (name, control) => { await control(); passed++; console.log(`PASS ${name}`); };

await run('old owner disposal cannot stop its replacement and returns the same original close', async () => {
  const modules = [];
  const host = createTaskExecutionHost({ crypto, createModule: () => {
    const m = { stops: 0, invokes: 0, cancel() {}, async invoke() { m.invokes++; return 'same raw receipt'; },
      async dispose() { m.stops++; } };
    modules.push(m); return m;
  }});
  const first = host.openOwner();
  assert.equal(await host.invoke(first, 'r1', 'Get', '{}'), 'same raw receipt');
  const closed = host.disposeOwner(first);
  assert.equal(host.disposeOwner(first), closed); await closed;
  const next = host.openOwner();
  assert.equal(await host.invoke(next, 'r2', 'Get', '{}'), 'same raw receipt');
  host.cancel(first, 'r2'); await host.disposeOwner(first);
  assert.equal(modules[0].stops, 1); assert.equal(modules[1].stops, 0);
  assert.throws(() => host.invoke(first, 'r3', 'Get', '{}'), /closed/);
  await host.dispose(); assert.equal(modules[1].stops, 1);
});

await run('retirement before lazy source acquisition is an exact no-effect refusal', async () => {
  let acquired = 0;
  const host = createTaskExecutionHost({ crypto, createModule: () => { acquired++; throw new Error('must not acquire'); } });
  const owner = host.openOwner();
  const actual = host.invoke(owner, 'r', 'Upsert', '{}');
  const closed = host.disposeOwner(owner);
  assert.deepEqual(JSON.parse(await actual), { ok: false, error: { code: 'Cancelled' } });
  await closed; assert.equal(acquired, 0); await host.dispose();
});

await run('a cancel before lazy invocation cannot be lost', async () => {
  let actualCancelled, cancels = 0;
  const host = createTaskExecutionHost({ crypto, createModule: () => ({
    async invoke(_id, _action, _json, cancelled) { actualCancelled = cancelled; return 'refusal'; },
    cancel() { cancels++; }, async dispose() {},
  }) });
  const owner = host.openOwner();
  const actual = host.invoke(owner, 'r', 'Upsert', '{}'); host.cancel(owner, 'r');
  assert.equal(await actual, 'refusal'); assert.equal(actualCancelled, true);
  assert.equal(cancels, 0); await host.dispose();
});

await run('external close joins held raw original and preserves late reply without replay', async () => {
  const entered = deferred(), held = deferred(); let calls = 0, stops = 0;
  const host = createTaskExecutionHost({ crypto, createModule: () => ({
    invoke() { calls++; entered.resolve(); return held.promise; }, cancel() {},
    async dispose() { stops++; await held.promise; },
  }) });
  const owner = host.openOwner(); const actual = host.invoke(owner, 'r', 'Upsert', '{}');
  let closed, closeSettled = false;
  try {
    await entered.promise;
    closed = host.disposeOwner(owner); closed.then(() => { closeSettled = true; }, () => { closeSettled = true; });
    await Promise.resolve(); assert.equal(closeSettled, false);
    assert.equal(host.disposeOwner(owner), closed); assert.throws(() => host.invoke(owner, 'late', 'Get', '{}'), /closed/);
  } finally { held.resolve('actual committed receipt'); }
  assert.equal(await actual, 'actual committed receipt'); await closed;
  assert.equal(calls, 1); assert.equal(stops, 1); await host.dispose();
});

await run('physical factory, invocation and module member callbacks cannot acquire self-joins', async () => {
  let host, owner, factoryGuard, invokeGuard, memberGuard;
  host = createTaskExecutionHost({ crypto, createModule: () => {
    assert.throws(() => host.disposeOwner(owner), error => { factoryGuard = error; return /source/.test(error.message); });
    return {
      get invoke() {
        assert.throws(() => host.disposeOwner(owner), error => { memberGuard = error; return /source/.test(error.message); });
        return () => {
          assert.throws(() => host.dispose(), error => { invokeGuard = error; return /source/.test(error.message); });
          return Promise.resolve('raw');
        };
      }, cancel() {}, async dispose() {},
    };
  }});
  owner = host.openOwner(); assert.equal(await host.invoke(owner, 'r', 'Get', '{}'), 'raw');
  assert.ok(factoryGuard && invokeGuard && memberGuard); await host.dispose();
});

await run('raw invocation, stop and cancel causes all remain in the same failed owner', async () => {
  const entered = deferred(), raw = deferred();
  const rawCause = new Error('actual raw fault'), cancelCause = new Error('actual cancel fault'), stopCause = new Error('actual stop fault');
  const host = createTaskExecutionHost({ crypto, createModule: () => ({
    invoke() { entered.resolve(); return raw.promise; }, cancel() { throw cancelCause; },
    dispose() { return Promise.reject(stopCause); },
  }) });
  const owner = host.openOwner(); const actual = host.invoke(owner, 'r', 'Upsert', '{}');
  let closed;
  try { await entered.promise; assert.throws(() => host.cancel(owner, 'r'), error => error === cancelCause); closed = host.disposeOwner(owner); }
  finally { raw.reject(rawCause); }
  assert.equal(await failure(actual), rawCause);
  const fault = await failure(closed); const all = causes(fault);
  for (const cause of [rawCause, cancelCause, stopCause]) assert.ok(all.includes(cause));
  assert.equal(host.disposeOwner(owner), closed);
  const terminal = await failure(host.dispose()); for (const cause of [rawCause, cancelCause, stopCause]) assert.ok(causes(terminal).includes(cause));
});

await run('terminal host fences every owner before the first stop callback and coalesces', async () => {
  let host, second, stops = 0;
  host = createTaskExecutionHost({ crypto, createModule: () => ({
    async invoke() { return 'raw'; }, cancel() {}, dispose() {
      stops++; assert.throws(() => host.openOwner(), /closed/);
      assert.throws(() => host.invoke(second, 'late', 'Get', '{}'), /closed/);
      return Promise.resolve();
    },
  }) });
  const first = host.openOwner(); second = host.openOwner();
  await host.invoke(first, 'r1', 'Get', '{}'); await host.invoke(second, 'r2', 'Get', '{}');
  const closed = host.dispose(); assert.equal(host.dispose(), closed); await closed;
  assert.equal(stops, 2);
});

await run('partial acquired module and original factory failure remain cleanup-owned', async () => {
  const originalCause = new Error('actual invalid invoke member'); let stops = 0;
  const host = createTaskExecutionHost({ crypto, createModule: () => ({
    get invoke() { throw originalCause; }, cancel() {}, async dispose() { stops++; },
  }) });
  const owner = host.openOwner();
  assert.equal(await failure(host.invoke(owner, 'r', 'Get', '{}')), originalCause);
  assert.ok(causes(await failure(host.disposeOwner(owner))).includes(originalCause));
  assert.equal(stops, 1); assert.ok(causes(await failure(host.dispose())).includes(originalCause));
});

await run('finite retired owner custody refuses capacity before a new source acquisition', async () => {
  let sources = 0;
  const host = createTaskExecutionHost({ crypto, maximumOwners: 1, createModule: () => { sources++; throw new Error('unexpected'); } });
  const owner = host.openOwner(); await host.disposeOwner(owner);
  assert.throws(() => host.openOwner(), /capacity/); assert.equal(sources, 0);
  assert.throws(() => host.disposeOwner('fabricated'), /same issued/); await host.dispose();
});
await run('returned invocation then getter cannot acquire its encompassing own join', async () => {
  const entered = deferred(), raw = deferred(); let host, owner, guarded = false, calls = 0;
  Object.defineProperty(raw.promise, 'then', { get() {
    assert.throws(() => host.disposeOwner(owner), /source/);
    assert.throws(() => host.dispose(), /source/);
    guarded = true; entered.resolve(); return Promise.prototype.then;
  } });
  host = createTaskExecutionHost({ crypto, createModule: () => ({
    invoke() { calls++; return raw.promise; }, cancel() {}, async dispose() {},
  }) });
  owner = host.openOwner(); const actual = host.invoke(owner, 'r', 'Get', '{}');
  let closed, completed = false;
  try {
    await entered.promise; closed = host.disposeOwner(owner);
    closed.then(() => { completed = true; }, () => { completed = true; });
    await Promise.resolve(); assert.equal(completed, false); assert.equal(guarded, true);
  } finally { raw.resolve('same actual reply'); }
  assert.equal(await actual, 'same actual reply'); await closed; await host.dispose(); assert.equal(calls, 1);
});

await run('throwing returned then getter joins held raw fault and every distinct stop sibling', async () => {
  const entered = deferred(), raw = deferred(), stopping = deferred();
  const getterCause = new Error('actual invocation then getter fault');
  const rawCause = new Error('actual invocation raw fault');
  const stopGetterCause = new Error('actual disposal then getter fault');
  const stopCause = new Error('actual disposal raw fault');
  Object.defineProperty(raw.promise, 'then', { get() { entered.resolve(); throw getterCause; } });
  Object.defineProperty(stopping.promise, 'then', { get() { throw stopGetterCause; } });
  const host = createTaskExecutionHost({ crypto, createModule: () => ({
    invoke() { return raw.promise; }, cancel() {}, dispose() { return stopping.promise; },
  }) });
  const owner = host.openOwner(), actual = host.invoke(owner, 'r', 'Upsert', '{}');
  let closed, completed = false;
  try {
    await entered.promise; closed = host.disposeOwner(owner);
    closed.then(() => { completed = true; }, () => { completed = true; });
    await Promise.resolve(); assert.equal(completed, false);
  } finally { raw.reject(rawCause); stopping.reject(stopCause); }
  const requestFault = causes(await failure(actual));
  assert.ok(requestFault.includes(getterCause)); assert.ok(requestFault.includes(rawCause));
  const all = causes(await failure(closed));
  for (const cause of [getterCause, rawCause, stopGetterCause, stopCause]) assert.ok(all.includes(cause));
  assert.equal(host.disposeOwner(owner), closed);
  const terminal = causes(await failure(host.dispose()));
  for (const cause of [getterCause, rawCause, stopGetterCause, stopCause]) assert.ok(terminal.includes(cause));
});

await run('returned disposal then getter cannot acquire own joins and whole native registration settles', async () => {
  let host, owner, getterGuarded = false;
  const stop = deferred();
  Object.defineProperty(stop.promise, 'then', { get() {
    assert.throws(() => host.disposeOwner(owner), /source/);
    getterGuarded = true; return Promise.prototype.then;
  } });
  host = createTaskExecutionHost({ crypto, createModule: () => ({
    async invoke() { return 'raw'; }, cancel() {}, dispose() { return stop.promise; },
  }) });
  owner = host.openOwner(); await host.invoke(owner, 'r', 'Get', '{}');
  const closed = host.disposeOwner(owner);
  try { assert.equal(getterGuarded, true); }
  finally { stop.resolve(); }
  await closed; await host.dispose();
});

await run('custom species source is refused and its same raw original remains unresolved', async () => {
  const entered = deferred(), raw = deferred(); let speciesAcquired = 0, settled = false, closeSettled = false;
  const speciesCause = new Error('species settlement must never be acquired');
  const Constructor = { get [Symbol.species]() {
    speciesAcquired++;
    return function (_executor) { throw speciesCause; };
  } };
  Object.defineProperty(raw.promise, 'constructor', { value: Constructor });
  Object.defineProperty(raw.promise, 'then', { get() { entered.resolve(); return Promise.prototype.then; } });
  const host = createTaskExecutionHost({ crypto, createModule: () => ({
    invoke() { return raw.promise; }, cancel() {}, async dispose() {},
  }) });
  const owner = host.openOwner(), actual = host.invoke(owner, 'r', 'Get', '{}');
  actual.then(() => { settled = true; }, () => { settled = true; });
  await entered.promise;
  const closed = host.disposeOwner(owner);
  closed.then(() => { closeSettled = true; }, () => { closeSettled = true; });
  raw.resolve('same raw value');
  // Bounded observations do not claim that an arbitrary source eventually settles.
  // This exact unsupported shape has no admitted finite subscription and remains
  // retained, rather than becoming a fabricated terminal/no-effect/clean receipt.
  for (let turn = 0; turn < 8; turn++) await Promise.resolve();
  assert.equal(speciesAcquired, 0); assert.equal(settled, false); assert.equal(closeSettled, false);
  assert.equal(host.disposeOwner(owner), closed); assert.throws(() => host.invoke(owner, 'late', 'Get', '{}'), /closed/);
});
await run('returned getter cannot redirect frozen host observations through live realm then mutation', async () => {
  const originalThen = Object.getOwnPropertyDescriptor(Promise.prototype, 'then');
  const raw = deferred(); let redirected = 0;
  Object.defineProperty(raw.promise, 'then', { get() {
    Object.defineProperty(Promise.prototype, 'then', { ...originalThen, value() {
      redirected++; throw new Error('live realm then must not redirect owned observations');
    } });
    return originalThen.value;
  } });
  const host = createTaskExecutionHost({ crypto, createModule: () => ({
    invoke() { return raw.promise; }, cancel() {}, async dispose() {},
  }) });
  const owner = host.openOwner(), actual = host.invoke(owner, 'r', 'Get', '{}');
  try {
    assert.equal(Object.isFrozen(actual), true);
    assert.throws(() => Object.defineProperty(actual, 'constructor', { value: {} }), TypeError);
    raw.resolve('original raw receipt');
    assert.equal(await actual, 'original raw receipt');
    await host.disposeOwner(owner); await host.dispose(); assert.equal(redirected, 0);
  } finally { Object.defineProperty(Promise.prototype, 'then', originalThen); }
});
await run('returned getter cannot forge reflection or replace exact raw registration with a resolved substitute', async () => {
  const originals = { prototypeOf: Object.getPrototypeOf, descriptor: Object.getOwnPropertyDescriptor,
    apply: Reflect.apply, freeze: Object.freeze };
  const raw = deferred(), entered = deferred(); let redirected = 0, closed, closeSettled = false;
  Object.defineProperty(raw.promise, 'then', { get() {
    Object.getPrototypeOf = () => { redirected++; return Promise.prototype; };
    Object.getOwnPropertyDescriptor = () => { redirected++; return undefined; };
    Reflect.apply = () => { redirected++; return Promise.resolve('fabricated terminal'); };
    Object.freeze = value => { redirected++; return value; };
    entered.resolve(); return Promise.prototype.then;
  } });
  const host = createTaskExecutionHost({ crypto, createModule: () => ({
    invoke() { return raw.promise; }, cancel() {}, async dispose() {},
  }) });
  const owner = host.openOwner(), actual = host.invoke(owner, 'r', 'Get', '{}');
  try {
    await entered.promise;
    closed = host.disposeOwner(owner); closed.then(() => { closeSettled = true; }, () => { closeSettled = true; });
    for (let turn = 0; turn < 8; turn++) await Promise.resolve();
    assert.equal(redirected, 0); assert.equal(closeSettled, false);
  } finally {
    Object.getPrototypeOf = originals.prototypeOf; Object.getOwnPropertyDescriptor = originals.descriptor;
    Reflect.apply = originals.apply; Object.freeze = originals.freeze; raw.resolve('same actual raw reply');
  }
  assert.equal(await actual, 'same actual raw reply'); await closed; await host.dispose();
});
console.log(`${passed} private Task module ownership controls passed.`);
