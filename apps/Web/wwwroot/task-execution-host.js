import { createTaskExecutionModule } from './tasks-indexeddb.js';

const IntrinsicPromise = Promise;
const intrinsicDescriptor = Object.getOwnPropertyDescriptor;
const intrinsicPrototypeOf = Object.getPrototypeOf;
const intrinsicDefine = Object.defineProperties;
const intrinsicFreeze = Object.freeze;
const intrinsicApply = Reflect.apply;
const intrinsicPrototype = Promise.prototype;
const nativeThen = Promise.prototype.then;
const constructorShape = intrinsicDescriptor(intrinsicPrototype, 'constructor');
const speciesShape = intrinsicDescriptor(IntrinsicPromise, Symbol.species);
const intrinsicResolve = IntrinsicPromise.resolve;
const intrinsicAllSettled = IntrinsicPromise.allSettled;
// Private observation Tasks have frozen instances/prototype/constructor and own
// captured Promise methods. Source callbacks cannot rewrite their constructor,
// then/catch or species through the mutable realm prototype. Raw module Promises
// remain completely untouched and separately retained.
class OwnedPromise extends IntrinsicPromise {
  constructor(executor) { super(executor); intrinsicFreeze(this); }
  static get [Symbol.species]() { return OwnedPromise; }
}
intrinsicDefine(OwnedPromise.prototype, {
  then: { value: nativeThen },
  catch: { value: function (failed) { return intrinsicApply(nativeThen, this, [undefined, failed]); } },
});
intrinsicDefine(OwnedPromise, {
  resolve: { value: intrinsicResolve },
  allSettled: { value: intrinsicAllSettled },
});
intrinsicFreeze(OwnedPromise.prototype);
intrinsicFreeze(OwnedPromise);
const sameDescriptor = (a, b) => a && b && a.value === b.value && a.get === b.get && a.set === b.set &&
  a.writable === b.writable && a.enumerable === b.enumerable && a.configurable === b.configurable;

// Platform ownership only. These private correlation keys confer no signed actor, profile,
// Task admission, Home grant or provider capability. The managed signed boundary supplies
// each original operation's profile, and the canonical repository validates every receipt.
export function createTaskExecutionHost({ createModule = createTaskExecutionModule,
  crypto = globalThis.crypto, maximumOwners = 128 } = {}) {
  if (!Number.isInteger(maximumOwners) || maximumOwners < 1 || maximumOwners > 128)
    throw new TypeError('A finite private Task module owner capacity is required.');
  const owners = new Map();
  let sealed = false, close;
  const add = (errors, error) => { if (!errors.includes(error)) errors.push(error); };
  const demand = key => {
    const owner = owners.get(key);
    if (!owner) throw new Error('The same issued private Task storage owner is required.');
    return owner;
  };
  const physical = (owner, callback) => {
    owner.sources++;
    try { return callback(); }
    finally { owner.sources--; }
  };
  const observeActualPromise = (owner, actual, original, member, label) => {
    // Keep the SAME returned Promise before any source-defined getter/registration.
    // The private observation is only its terminal join; it never substitutes a
    // reply, races cancellation or discards a held actual source.
    original[member] = actual;
    const errors = [];
    try {
      const then = physical(owner, () => actual?.then);
      if (typeof then !== 'function') throw new TypeError(`The actual module must return its original ${label} Promise.`);
    } catch (error) { add(errors, error); add(owner.errors, error); }
    let resolve, reject;
    const observed = new OwnedPromise((yes, no) => { resolve = yes; reject = no; });
    let standard;
    try {
      // The maintained module returns ordinary same-realm native Promises. A
      // custom constructor/species can execute capability settlement outside
      // the handler and can make a finite generic join impossible. Refuse that
      // shape while retaining SAME raw original; never mutate it or fake close.
      standard = physical(owner, () => {
        const prototype = intrinsicPrototypeOf(actual);
        const ownConstructor = intrinsicDescriptor(actual, 'constructor');
        const currentConstructor = intrinsicDescriptor(intrinsicPrototype, 'constructor');
        const currentSpecies = intrinsicDescriptor(IntrinsicPromise, Symbol.species);
        return prototype === intrinsicPrototype && !ownConstructor &&
          sameDescriptor(currentConstructor, constructorShape) && sameDescriptor(currentSpecies, speciesShape);
      });
      if (!standard) throw new TypeError('A standard same-realm original Promise is required for a finite terminal join.');
    } catch (error) {
      add(errors, error); add(owner.errors, error);
      original[`${member}RegistrationFailure`] = error;
      return observed; // Same unresolved original is retained; no terminal/no-effect receipt exists.
    }
    let outcome;
    physical(owner, () => intrinsicApply(nativeThen, observed, [undefined, () => {}]));
    const terminal = (failed, value) => physical(owner, () => {
      if (failed) { add(errors, value); add(owner.errors, value); }
      else if (label === 'invocation' ? typeof value !== 'string' : value !== undefined) {
        const error = new TypeError(`The actual ${label} Promise returned an invalid terminal value.`);
        add(errors, error); add(owner.errors, error);
      }
      outcome = value; // No source-defined thenable escapes this guarded consumption.
    });
    try {
      // Intrinsic registration joins the genuine Promise even when its own then
      // getter throws. Brand/species/member callbacks remain physically guarded.
      original[`${member}Registration`] = physical(owner, () => intrinsicApply(nativeThen, actual,
        [value => terminal(false, value), error => terminal(true, error)]));
      // This registration has the known standard intrinsic species, not a
      // source-selected constructor. Join its SAME terminal Task before the
      // encompassing wrapper may settle; its final observer has only private
      // no-throw primitive settlement handlers.
      original[`${member}RegistrationJoin`] = physical(owner, () => intrinsicApply(nativeThen,
        original[`${member}Registration`], [() => errors.length
          ? reject(errors.length === 1 ? errors[0] : new AggregateError([...errors], `Original ${label} Promise inspection or settlement failed.`))
          : resolve(outcome), error => { add(errors, error); add(owner.errors, error); reject(error); }]));
    } catch (error) {
      add(errors, error); add(owner.errors, error);
      // No terminal callback was acquired. Do not claim the returned original
      // settled or complete the observation: retain this unresolved source.
      original[`${member}RegistrationFailure`] = error;
    }
    return observed;
  };
  const acquire = owner => {
    if (owner.module) return owner.module;
    if (owner.acquisitionFailure) throw owner.acquisitionFailure;
    try {
      const actual = physical(owner, () => createModule());
      // Keep an acquired partial module before inspecting any source-defined member.
      owner.module = actual;
      physical(owner, () => {
        if (!actual || typeof actual.invoke !== 'function' || typeof actual.cancel !== 'function' ||
          typeof actual.dispose !== 'function') throw new TypeError('An actual complete Task module is required.');
      });
      return actual;
    } catch (error) { owner.acquisitionFailure = error; add(owner.errors, error); throw error; }
  };
  const originalClose = owner => {
    if (owner.sources) throw new Error('An actual Task storage source cannot join its encompassing close.');
    if (owner.close) return owner.close;
    let resolve, reject;
    owner.close = new OwnedPromise((yes, no) => { resolve = yes; reject = no; });
    owner.close.catch(() => {});
    owner.sealed = true; // SAME owner closes before any cancellation/factory/disposal callback.
    const cohort = [...owner.originals.values()];
    let actualStop;
    try {
      if (owner.module) {
        const raw = physical(owner, () => owner.module.dispose());
        actualStop = observeActualPromise(owner, raw, owner, 'rawStop', 'disposal');
      }
    } catch (error) { add(owner.errors, error); }
    OwnedPromise.allSettled([...cohort.map(original => original.actual), actualStop]).then(results => {
      for (const result of results) if (result.status === 'rejected') add(owner.errors, result.reason);
      // Failed wrappers stay in owner custody even after their consumer observed them.
      if (owner.errors.length) reject(new AggregateError([...owner.errors], 'Private Task module originals or cleanup failed.'));
      else resolve();
    }).catch(error => { add(owner.errors, error); reject(error); });
    return owner.close;
  };
  const host = {
    openOwner() {
      if (sealed) throw new Error('The terminal Task storage host is closed.');
      if (owners.size >= maximumOwners) throw new Error('Private Task storage owner capacity requires terminal reload.');
      const key = crypto?.randomUUID?.();
      if (typeof key !== 'string' || !key || owners.has(key)) throw new Error('A unique private module correlation is required.');
      // No IDB/factory callback is acquired by enrollment. Lifetime exists first.
      owners.set(key, { key, module: null, acquisitionFailure: null, sealed: false,
        sources: 0, originals: new Map(), errors: [], close: null });
      return key;
    },
    invoke(key, requestId, action, json, cancelledAtAdmission = false) {
      const owner = demand(key);
      if (sealed || owner.sealed) throw new Error('The private Task storage owner is closed.');
      if (typeof requestId !== 'string' || !requestId || owner.originals.has(requestId))
        throw new TypeError('A unique request correlation is required.');
      // Failed actual wrappers are retained; finite refusal precedes another source acquisition.
      if (owner.originals.size >= 256) throw new Error('Private Task storage original capacity requires external retirement.');
      let resolve, reject;
      const actual = new OwnedPromise((yes, no) => { resolve = yes; reject = no; });
      const original = { actual, requestId, cancelled: cancelledAtAdmission };
      owner.originals.set(requestId, original);
      actual.catch(() => {});
      // Publish the entire driver before factory/invoke callbacks can attempt retirement.
      OwnedPromise.resolve().then(() => {
        // An externally closed original admitted before this microtask has no new effect.
        if (sealed || owner.sealed) return JSON.stringify({ ok: false, error: { code: 'Cancelled' } }); // Exact owner refused before acquiring a business original.
        const module = acquire(owner);
        const raw = physical(owner, () => module.invoke(requestId, action, json, original.cancelled));
        const observed = observeActualPromise(owner, raw, original, 'raw', 'invocation');
        return observed; // Joins SAME raw original and the whole guarded terminal callback.
      }).then(value => {
        owner.originals.delete(requestId); // Only successful terminal originals can be pruned.
        resolve(value);
      }, error => { add(owner.errors, error); reject(error); });
      return actual;
    },
    cancel(key, requestId) {
      const owner = demand(key);
      const original = owner.originals.get(requestId);
      if (!original) return;
      original.cancelled = true; // Preserve a stop that precedes lazy factory/invocation acquisition.
      if (!owner.module) return;
      try { physical(owner, () => owner.module.cancel(requestId)); }
      catch (error) { add(owner.errors, error); throw error; }
    },
    disposeOwner(key) { return originalClose(demand(key)); },
    dispose() {
      if (close) return close;
      // Refuse a genuine synchronous module/factory source before acquiring a self-join.
      if ([...owners.values()].some(owner => owner.sources))
        throw new Error('An actual Task module source cannot join its terminal host.');
      let resolve, reject;
      close = new OwnedPromise((yes, no) => { resolve = yes; reject = no; });
      close.catch(() => {});
      sealed = true;
      const cohort = [...owners.values()];
      // Fence every old owner before the first actual stop can observe another owner.
      for (const owner of cohort) owner.sealed = true;
      const causes = [], originals = [];
      for (const owner of cohort) {
        try { originals.push(originalClose(owner)); } catch (error) { add(causes, error); }
      }
      OwnedPromise.allSettled(originals).then(results => {
        for (const result of results) if (result.status === 'rejected') add(causes, result.reason);
        if (causes.length) reject(new AggregateError(causes, 'Terminal Task storage originals failed.'));
        else resolve();
      }).catch(reject);
      return close;
    },
  };
  return host;
}
