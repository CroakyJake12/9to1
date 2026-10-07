import { createTaskExecutionHost } from '../../task-execution-host.js';

// Home preferences only. A profile partitions data; no layout record grants
// account, OS, permission, provider, Task or Run authority.
const Schema = 1, Database = '9to1-home-dashboard-v1';
const MaximumJson = 1048576;
const profilePattern = /^cake-account-profile:[0-9a-f]{64}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const decimal = value => {
  if (typeof value !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new TypeError('An exact nonnegative Home Int64 revision is required.');
  return value;
};
const digest = async (crypto, json) => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',
  new TextEncoder().encode(json))), byte => byte.toString(16).padStart(2, '0')).join('');
const envelope = (profile, revision, json, hash) => ({ schema: Schema, profile, revision, json, hash });
const validateEnvelope = async (crypto, row, profile) => {
  if (row === undefined) return null;
  if (!row || row.schema !== Schema || row.profile !== profile || typeof row.json !== 'string' ||
      new TextEncoder().encode(row.json).byteLength > MaximumJson || typeof row.hash !== 'string' || !/^[0-9a-f]{64}$/.test(row.hash))
    throw new TypeError('The stored Home layout envelope is corrupt; preserve it for recovery.');
  decimal(row.revision);
  // Preserve the actual stored SyntaxError, rather than replacing the row with defaults.
  const layout = JSON.parse(row.json);
  const revisions = [...row.json.matchAll(/"Revision"\s*:\s*(0|[1-9][0-9]*)(?=\s*[,}])/g)];
  if (!layout || layout.SchemaVersion !== Schema || !Array.isArray(layout.Tiles) || revisions.length !== 1 ||
      revisions[0][1] !== row.revision || await digest(crypto, row.json) !== row.hash)
    throw new TypeError('The complete stored Home layout/revision/hash is invalid; preserve it.');
  return row;
};

export function createHomeDashboardModule({ indexedDB = globalThis.indexedDB, crypto = globalThis.crypto,
  databaseName = Database } = {}) {
  if (!indexedDB || !crypto?.subtle) throw new Error('Actual IndexedDB and SHA-256 are required for Home preferences.');
  let db, opening, closed = false, close;
  const originals = new Map(), transactions = new Map(), cancelled = new Set();
  const errors = [];
  const add = error => { if (!errors.includes(error)) errors.push(error); };
  const open = () => opening ??= new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, Schema);
    request.onupgradeneeded = () => {
      try {
        const actual = request.result;
        for (const name of ['layouts', 'revisions', 'conflicts'])
          if (!actual.objectStoreNames.contains(name)) actual.createObjectStore(name, { keyPath: name === 'layouts' ? 'profile' : ['profile', 'revision'] });
      } catch (cause) {
        add(cause);
        try { request.transaction.abort(); } catch (cleanup) { add(cleanup); }
        // Keep this actual opening pending until its own success/error terminal callback.
      }
    };
    request.onerror = () => reject(request.error);
    request.onblocked = () => add(new Error('Home schema opening is blocked; the SAME pending request is retained until terminal acquisition/error.'));
    request.onsuccess = () => {
      db = request.result;
      db.onversionchange = () => { closed = true; try { db.close(); } catch (cause) { add(cause); } };
      if (closed) { db.close(); reject(new Error('The original Home storage owner retired during opening.')); }
      else resolve(db);
    };
  });
  const readRow = async (profile, revision) => {
    const actual = await open();
    return await new Promise((resolve, reject) => {
      const name = revision === undefined ? 'layouts' : 'revisions';
      const transaction = actual.transaction(name, 'readonly');
      const request = transaction.objectStore(name).get(revision === undefined ? profile : [profile, revision]);
      let value, failure;
      request.onsuccess = () => { value = request.result; };
      request.onerror = () => { failure = request.error; };
      transaction.oncomplete = () => resolve(value);
      transaction.onabort = () => reject(failure ?? transaction.error ?? new Error('The actual Home read aborted.'));
      transaction.onerror = () => { failure ??= transaction.error; };
    });
  };
  const save = async (requestId, profile, expected, proposed, attempt = 0) => {
    // Hash validation happens outside the write transaction. The transaction then
    // compares the SAME complete snapshot, so crypto awaits cannot make it inactive.
    const before = await validateEnvelope(crypto, await readRow(profile), profile);
    const actual = await open();
    let changed = false, result;
    await new Promise((resolve, reject) => {
      const transaction = actual.transaction(['layouts', 'revisions', 'conflicts'], 'readwrite');
      transactions.set(requestId, transaction);
      const layouts = transaction.objectStore('layouts');
      const current = layouts.get(profile);
      let failure, committed = false;
      const abort = cause => {
        failure = cause;
        try { transaction.abort(); } catch (cleanup) { add(cleanup); failure = new AggregateError([cause, cleanup], 'Original Home abort failed; retain the SAME transaction until terminal observation.'); }
      };
      current.onsuccess = () => {
        try {
          if (cancelled.has(requestId) || closed) { abort(new Error('The Home write was cancelled before its commit.')); return; }
          const observed = current.result ?? null;
          if (JSON.stringify(observed) !== JSON.stringify(before)) { changed = true; transaction.abort(); return; }
          const revision = before?.revision ?? '0';
          if (revision !== expected) {
            const conflicts = transaction.objectStore('conflicts');
            // Correlation identity preserves the conflicting proposal; it is not authority.
            const conflict = { ...proposed, revision: requestId, proposedRevision: proposed.revision };
            const old = conflicts.get([profile, requestId]);
            old.onsuccess = () => {
              try {
                if (old.result && JSON.stringify(old.result) !== JSON.stringify(conflict)) abort(new Error('The Home conflict correlation collided.'));
                else if (!old.result) conflicts.add(conflict);
              } catch (cause) { abort(cause); }
            };
            old.onerror = () => abort(old.error);
            result = { ok: true, committed: false, outcome: 'Conflict', profile, record: before };
            return;
          }
          if (BigInt(proposed.revision) !== BigInt(expected) + 1n) { abort(new TypeError('The proposed Home revision must follow the actual expected revision.')); return; }
          if (before) {
            const history = transaction.objectStore('revisions');
            const archived = history.get([profile, before.revision]);
            archived.onsuccess = () => {
              try {
                if (archived.result && JSON.stringify(archived.result) !== JSON.stringify(before))
                  throw new Error('An immutable Home history revision collided or was corrupted.');
                if (!archived.result) history.add(before);
                layouts.put(proposed);
                committed = true;
              } catch (cause) { abort(cause); }
            };
            archived.onerror = () => abort(archived.error);
          } else { layouts.add(proposed); committed = true; }
          result = { ok: true, committed: true, outcome: 'Saved', profile, record: proposed };
        } catch (cause) { abort(cause); }
      };
      current.onerror = () => { failure = current.error; };
      transaction.oncomplete = () => {
        transactions.delete(requestId);
        if (failure) reject(failure);
        else if (committed && result?.committed !== true) reject(new Error('The Home commit lost its original receipt.'));
        else resolve();
      };
      transaction.onabort = () => {
        transactions.delete(requestId);
        if (changed && !failure) resolve();
        else reject(failure ?? transaction.error ?? new Error('The actual Home transaction aborted.'));
      };
      transaction.onerror = () => { failure ??= transaction.error; };
    });
    if (changed) {
      // The SAME race transaction is proven terminally aborted. Re-read/revalidate
      // before a bounded retry, so EVERY reported conflict durably retains its proposal.
      if (attempt >= 7) throw new Error('Home CAS contention exceeded its bounded attempts; the original proposal remains owned, no outcome is fabricated.');
      return await save(requestId, profile, expected, proposed, attempt + 1);
    }
    return result;
  };
  const run = async (requestId, action, json, cancelledAtAdmission) => {
    const arguments_ = JSON.parse(json);
    const profile = arguments_?.profile;
    if (!profilePattern.test(profile)) throw new TypeError('A genuine verified account-profile partition is required.');
    if (closed || cancelledAtAdmission || cancelled.has(requestId))
      return JSON.stringify({ ok: false, committed: false, dispatched: false, error: { code: 'CancelledBeforeStorage' } });
    if (action === 'Load' || action === 'GetRevision') {
      const revision = action === 'GetRevision' ? decimal(arguments_.revision) : undefined;
      let row = await readRow(profile);
      if (revision !== undefined && row?.revision !== revision) row = await readRow(profile, revision);
      const record = await validateEnvelope(crypto, row, profile);
      return JSON.stringify({ ok: true, committed: false, outcome: 'Read', profile, record });
    }
    if (action !== 'Save') throw new TypeError('Home storage exposes only layout read/revision/CAS save.');
    const expected = decimal(arguments_.expected), revision = decimal(arguments_.revision);
    if (typeof arguments_.json !== 'string' || new TextEncoder().encode(arguments_.json).byteLength > MaximumJson)
      throw new TypeError('A bounded complete Home layout JSON is required.');
    const proposal = envelope(profile, revision, arguments_.json, await digest(crypto, arguments_.json));
    await validateEnvelope(crypto, proposal, profile);
    return JSON.stringify(await save(requestId, profile, expected, proposal));
  };
  return {
    invoke(requestId, action, json, cancelledAtAdmission = false) {
      if (closed || originals.has(requestId)) throw new Error('The same live Home storage original is required.');
      let release;
      const start = new Promise(resolve => { release = resolve; });
      const actual = (async () => {
        await start;
        try {
          const result = await run(requestId, action, json, cancelledAtAdmission);
          originals.delete(requestId); // Only proven successful original completion is retired.
          return result;
        } catch (cause) { add(cause); throw cause; }
        finally { cancelled.delete(requestId); }
      })();
      originals.set(requestId, actual); // BEFORE the actual DB/open/parse/hash callbacks.
      release();
      return actual;
    },
    cancel(requestId) {
      cancelled.add(requestId);
      const transaction = transactions.get(requestId);
      if (transaction) transaction.abort(); // A failed stop remains a source fault in the qualified host.
    },
    dispose() {
      if (close) return close;
      closed = true;
      let release; const start = new Promise(resolve => { release = resolve; });
      close = (async () => {
        await start;
        const failures = [...errors];
        for (const transaction of transactions.values())
          try { transaction.abort(); } catch (cause) { if (!failures.includes(cause)) failures.push(cause); }
        for (const actual of originals.values())
          try { await actual; } catch (cause) { if (!failures.includes(cause)) failures.push(cause); }
        if (opening) try { await opening; } catch (cause) { if (!failures.includes(cause)) failures.push(cause); }
        try { db?.close(); } catch (cause) { if (!failures.includes(cause)) failures.push(cause); }
        if (failures.length) throw new AggregateError(failures, 'Original Home storage and close failures.');
      })();
      release();
      return close;
    }
  };
}

// Reuse the immutable qualified Promise/physical source/own-join host. Each
// managed private lifetime has its own issued correlation; an old close cannot close a new owner.
const host = createTaskExecutionHost({ createModule: createHomeDashboardModule });
const preparedCloses = new Map();
export const openOwner = () => host.openOwner();
export const invoke = (owner, id, action, json, cancelled) => host.invoke(owner, id, action, json, cancelled);
export const cancel = (owner, id) => host.cancel(owner, id);
// Synchronous physical own-join refusal occurs before managed close admission.
// The SAME host-owned actual close Promise is retained, then consumed by joinClose.
export function prepareClose(owner) { preparedCloses.set(owner, host.disposeOwner(owner)); }
export const joinClose = owner => preparedCloses.get(owner) ?? host.disposeOwner(owner);
