// Ordering/failure unit controls over the actual root module. Scripted
// owner tasks do not establish native/interop/issuer/browser acceptance.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { setImmediate as eventTurn } from 'node:timers/promises';
import { createPrivateContextLifecycle } from '../wwwroot/browser-private-context.js';

const deferred = () => {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
};
const tick = async () => { await Promise.resolve(); await Promise.resolve(); };
const contains = (error, original) => error === original
    || (error instanceof AggregateError && error.errors.some(item => contains(item, original)));
const failedWith = original => error => contains(error, original);

test('reentrant join sees placeholder before actual owner export returns', async () => {
    const native = deferred(); let lifecycle, joined, settled = false;
    lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => {
        joined = lifecycle.join(); joined.then(() => { settled = true; }); return native.promise;
    }, clearPresentation: () => {} });
    assert.equal(lifecycle.begin(), undefined);
    await tick(); assert.equal(settled, false);
    native.resolve(); await joined; assert.equal(settled, true);
});

test('owner fence precedes presentation cleanup and begin is a void acknowledgment', async () => {
    const native = deferred(); const order = [];
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => { order.push('actual-owner-fence'); return native.promise; },
        clearPresentation: () => { order.push('presentation-after-fence'); } });
    assert.equal(lifecycle.begin(), undefined);
    assert.deepEqual(order, ['actual-owner-fence', 'presentation-after-fence']);
    assert.equal(lifecycle.retainedDrains, 1); native.resolve(); await lifecycle.join();
    assert.equal(lifecycle.retainedDrains, 0);
});

test('join waits all earlier and newly issued drains', async () => {
    const first = deferred(), second = deferred(); const tasks = [first, second]; let index = 0, settled = false;
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => tasks[index++].promise, clearPresentation: () => {} });
    lifecycle.begin(); const joined = lifecycle.join().then(() => { settled = true; });
    lifecycle.begin(); first.resolve();
    // Let the first receipt and every already queued continuation finish while
    // the second true owner task remains held. Two microtasks were insufficient
    // to distinguish a join that captured only its initial set.
    await eventTurn(); assert.equal(lifecycle.retainedDrains, 1); assert.equal(settled, false);
    second.resolve(); await joined; assert.equal(lifecycle.version, 2);
});

test('completed owner failure remains observable to every late join', async () => {
    const native = deferred(), original = new Error('controlled owner disposal fault');
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => native.promise, clearPresentation: () => {} });
    lifecycle.begin(); native.reject(original);
    await assert.rejects(lifecycle.join(), failedWith(original));
    assert.equal(lifecycle.failed, true); assert.equal(lifecycle.retainedDrains, 1);
    await assert.rejects(lifecycle.join(), failedWith(original));
});

test('a later successful reset cannot erase a completed earlier failure', async () => {
    const first = deferred(), original = new Error('controlled retained failure'); let calls = 0;
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => ++calls === 1 ? first.promise : Promise.resolve(), clearPresentation: () => {} });
    lifecycle.begin(); first.reject(original); await assert.rejects(lifecycle.join(), failedWith(original));
    lifecycle.begin(); await assert.rejects(lifecycle.join(), failedWith(original)); assert.equal(calls, 2);
});

test('presentation fault is immediate and its join still drains actual owner work', async () => {
    const native = deferred(), original = new Error('controlled DOM cleanup fault'); let settled = false;
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => native.promise, clearPresentation: () => { throw original; } });
    assert.throws(() => lifecycle.begin(), error => error === original); assert.equal(lifecycle.failed, true);
    const joined = lifecycle.join().then(() => assert.fail('cleanup must fail'), error => { assert(contains(error, original)); settled = true; });
    await tick(); assert.equal(settled, false); native.resolve(); await joined;
});

test('throw undefined in presentation is still failure and cannot grant a join', async () => {
    const native = deferred(); const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => native.promise, clearPresentation: () => { throw undefined; } });
    let threw = false; try { lifecycle.begin(); } catch { threw = true; }
    assert.equal(threw, true); assert.equal(lifecycle.failed, true);
    native.resolve(); await assert.rejects(lifecycle.join(), failedWith(undefined));
});

test('owner and presentation failures preserve both original causes', async () => {
    const native = deferred(), owner = new Error('controlled owner fault'), presentation = new Error('controlled presentation fault');
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => native.promise, clearPresentation: () => { throw presentation; } });
    assert.throws(() => lifecycle.begin(), error => error === presentation); native.reject(owner);
    await assert.rejects(lifecycle.join(), error => contains(error, owner) && contains(error, presentation));
});

test('synchronous export failure stays held and still clears semantic presentation', async () => {
    const original = new Error('controlled export failure'); let presentations = 0;
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => { throw original; }, clearPresentation: () => { ++presentations; } });
    assert.throws(() => lifecycle.begin(), error => error === original); assert.equal(presentations, 1);
    await assert.rejects(lifecycle.join(), failedWith(original));
});

test('missing owner drain receipt cannot be acknowledged', async () => {
    let presentations = 0;
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => undefined, clearPresentation: () => { ++presentations; } });
    assert.throws(() => lifecycle.begin(), /actual task/); assert.equal(lifecycle.failed, true);
    assert.equal(presentations, 1);
    await assert.rejects(lifecycle.join(), /Private cleanup failed/);
});

test('async presentation acknowledgment is rejected without dropping owner drain', async () => {
    const native = deferred(); const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => native.promise, clearPresentation: () => Promise.resolve() });
    assert.throws(() => lifecycle.begin(), /synchronous and void/); native.resolve();
    await assert.rejects(lifecycle.join(), /Private cleanup failed/);
});

test('failure observer exception cannot replace or hide the original failure', async () => {
    const native = deferred(), owner = new Error('controlled owner fault'), observer = new Error('controlled status callback fault');
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => native.promise, clearPresentation: () => {}, onFailure: () => { throw observer; } });
    lifecycle.begin(); native.reject(owner);
    await assert.rejects(lifecycle.join(), error => contains(error, owner) && contains(error, observer));
});

test('external admission failure remains held even when no owner work is active', async () => {
    const original = new Error('controlled admission failure');
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => Promise.resolve(), clearPresentation: () => {} });
    await lifecycle.join(); lifecycle.holdFailure(original); assert.equal(lifecycle.retainedDrains, 0);
    await assert.rejects(lifecycle.join(), failedWith(original)); assert.equal(lifecycle.failed, true);
});

test('synchronous owner and presentation faults retain both causes without acknowledgment', async () => {
    const owner = new Error('controlled synchronous export fault'), presentation = new Error('controlled synchronous cleanup fault');
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => { throw owner; }, clearPresentation: () => { throw presentation; } });
    assert.throws(() => lifecycle.begin(), error => contains(error, owner) && contains(error, presentation));
    await assert.rejects(lifecycle.join(), error => contains(error, owner) && contains(error, presentation));
});

test('a rejected invalid async presentation acknowledgment is observed and retained', async () => {
    const original = new Error('controlled invalid async presentation fault');
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => Promise.resolve(), clearPresentation: () => Promise.reject(original) });
    assert.throws(() => lifecycle.begin(), /synchronous and void/);
    await eventTurn(); await assert.rejects(lifecycle.join(), failedWith(original));
});

test('a rejected invalid async failure notification is observed and retained', async () => {
    const owner = new Error('controlled owner fault'), observer = new Error('controlled invalid async observer fault');
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: () => Promise.resolve(), clearPresentation: () => {}, onFailure: () => Promise.reject(observer) });
    lifecycle.holdFailure(owner); await eventTurn();
    await assert.rejects(lifecycle.join(), error => contains(error, owner) && contains(error, observer)
        && error.errors.some(item => item instanceof TypeError));
});

test('root opaque operation reaches its actual owner callback and uses the same drain', async () => {
    const operation = Symbol('root-owned operation'), native = deferred(); let observed;
    const lifecycle = createPrivateContextLifecycle({ beginOwnerReset: kind => { observed = kind; return native.promise; }, clearPresentation: () => {} });
    lifecycle.begin(operation); assert.equal(observed, operation); assert.equal(lifecycle.retainedDrains, 1);
    native.resolve(); await lifecycle.join(); assert.equal(lifecycle.retainedDrains, 0);
});
