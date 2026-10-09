import { createTaskExecutionModule } from '../../wwwroot/tasks-indexeddb.js';

// Real browser IndexedDB/WebCrypto and exact actual transaction controls. Synthetic profile
// observations below grant no signed identity, admission, provider, Home or permission.
export async function runExecutionEventStorageControls() {
  if (!globalThis.indexedDB || !globalThis.crypto?.subtle || !globalThis.IDBDatabase)
    throw new Error('Genuine browser IndexedDB/WebCrypto is required; no memory substitute is supported.');
  const results = [], modules = [], names = [];
  const check = (value, message) => { if (!value) throw new Error(message); };
  const uuid = () => crypto.randomUUID();
  const ownerA = `cake-account-profile:${'a'.repeat(64)}:${uuid()}`;
  const ownerB = `cake-account-profile:${'a'.repeat(64)}:${uuid()}`;
  const deferred = () => { let resolve; const promise = new Promise(yes => { resolve = yes; }); return { promise, resolve }; };
  const digest = async text => [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text)))].map(n => n.toString(16).padStart(2, '0')).join('');
  const event = (executionId = uuid()) => ({ eventId: uuid(), executionId, actionId: uuid(), parentActionId: uuid(), origin: 2,
    actionType: 0, status: 6, name: 'Original canonical prompt', safeReasoningSummary: 'safe reasoning summary',
    safeDetail: 'safe observed detail', componentId: 'observed-component', timestamp: '2026-10-07T08:00:00.1234567+00:00',
    startedAt: '2026-10-07T07:59:59+00:00', endedAt: '2026-10-07T08:00:00+00:00', retryOfActionId: uuid(),
    recoveryOfActionId: uuid(), remediationId: uuid(), taskId: uuid(), tabId: uuid(), projectId: uuid(),
    failure: { code: 'observed-code', title: 'observed-title', message: 'safe failure', providerMessage: 'safe provider', httpStatus: 503,
      attempt: 3, retryAfter: '2026-10-07T08:01:00+00:00', affectedComponent: 'observed-component', recovered: true },
    safeMetadata: { observation: 'original safe metadata' }, duration: '00:00:01' });
  const row = async (payload, profileId = ownerA) => {
    const json = JSON.stringify(payload);
    return { profileId, ...Object.fromEntries(['eventId', 'executionId', 'actionId', 'parentActionId', 'origin', 'actionType', 'status',
      'name', 'componentId', 'timestamp', 'startedAt', 'endedAt', 'retryOfActionId', 'recoveryOfActionId', 'remediationId', 'taskId', 'tabId', 'projectId']
      .map(key => [key, payload[key] ?? null])), json, sha256: await digest(json) };
  };
  let requests = 0;
  const call = async (actual, action, args, profileId = ownerA, requestId = `event-control-${++requests}`) =>
    JSON.parse(await actual.invoke(requestId, action, JSON.stringify({ ...args, profileId })));
  const append = (actual, rows, profileId = ownerA, requestId) => call(actual, 'ExecutionEvents.Append', { rows }, profileId, requestId);
  const read = (actual, executionId, profileId = ownerA) => call(actual, 'ExecutionEvents.GetExecution', { executionId }, profileId);
  const create = (name, extra = {}) => {
    const databaseName = `a3-event-${name}-${uuid()}`; names.push(databaseName);
    const actual = createTaskExecutionModule({ databaseName, ...extra }); modules.push(actual); return { actual, databaseName };
  };
  const open = (name, version) => new Promise((resolve, reject) => {
    const request = version === undefined ? indexedDB.open(name) : indexedDB.open(name, version);
    request.onerror = () => reject(request.error); request.onsuccess = () => resolve(request.result);
  });
  const transaction = (db, stores, body) => new Promise((resolve, reject) => {
    const tx = db.transaction(stores, 'readwrite'); tx.oncomplete = resolve; tx.onabort = () => reject(tx.error ?? new Error('Actual fixture transaction aborted.'));
    body(tx);
  });
  const dump = (db, name) => new Promise((resolve, reject) => {
    const tx = db.transaction([name], 'readonly'), values = []; const request = tx.objectStore(name).openCursor();
    request.onsuccess = e => { const cursor = e.target.result; if (cursor) { values.push({ key: cursor.primaryKey, value: cursor.value }); cursor.continue(); } };
    tx.oncomplete = () => resolve(values); tx.onabort = () => reject(tx.error);
  });
  const fixture = async (name, version) => {
    const actual = await new Promise((resolve, reject) => {
      const request = indexedDB.open(name, version);
      request.onupgradeneeded = () => {
        const db = request.result; const tasks = db.createObjectStore('tasks', { keyPath: 'taskId' }); tasks.createIndex('contextId', 'contextId');
        if (version === 2) {
          db.createObjectStore('legacyTaskBackupV1', { keyPath: 'taskId' });
          for (const storeName of ['conversations', 'conversationMessages', 'conversationContexts']) {
            const store = db.createObjectStore(storeName, { keyPath: ['profileId', 'id'] }); store.createIndex('profileId', 'profileId');
            store.createIndex(storeName === 'conversations' ? 'profileSpace' : 'profileConversation',
              storeName === 'conversations' ? ['profileId', 'spaceId'] : ['profileId', 'conversationId']);
          }
          db.createObjectStore('taskProfiles', { keyPath: 'taskId' }); db.createObjectStore('unknownPriorStore', { keyPath: 'id' });
        }
      };
      request.onerror = () => reject(request.error); request.onsuccess = () => resolve(request.result);
    });
    const priorNames = [...actual.objectStoreNames]; const taskId = uuid(), contextId = uuid();
    await transaction(actual, priorNames, tx => {
      tx.objectStore('tasks').put({ taskId, contextId, executionId: uuid(), revision: '9007199254740993',
        json: '{complete historical malformed payload', sha256: 'old-opaque-hash', preservedUnknown: { old: [1, 2, 3] } });
      if (version === 2) {
        tx.objectStore('legacyTaskBackupV1').put({ taskId, json: 'exact original backup bytes', originalRevision: '9223372036854775807' });
        tx.objectStore('conversations').put({ profileId: ownerA, id: contextId, mode: 2, json: 'complete prior context bytes', unknown: 'preserved' });
        tx.objectStore('conversationMessages').put({ profileId: ownerA, id: uuid(), conversationId: contextId, json: '{historical message bytes' });
        tx.objectStore('conversationContexts').put({ profileId: ownerA, id: uuid(), conversationId: contextId, json: 'prior private context bytes' });
        tx.objectStore('taskProfiles').put({ taskId, contextId, profileId: ownerA, originalUnknown: true });
        tx.objectStore('unknownPriorStore').put({ id: 'opaque-original', bytes: new Uint8Array([0, 255, 3]), untouched: true });
      }
    });
    const saved = new Map(); for (const store of priorNames) saved.set(store, await dump(actual, store)); actual.close();
    return saved;
  };
  const causes = error => error instanceof AggregateError ? error.errors.flatMap(causes) : [error];
  const failClose = async actual => {
    let error; try { await actual.dispose(); } catch (failure) { error = failure; }
    check(error instanceof AggregateError, 'Actual failed owner close must retain its original fault.');
    modules.splice(modules.indexOf(actual), 1); return error;
  };
  const run = async (name, control) => { await control(); results.push({ name, status: 'PASS' }); };
  let originalFailure;
  try {
    for (const version of [1, 2]) await run(`forward_schema${version}_to3_preserves_complete_actual_prior_stores_and_backup`, async () => {
      const { actual, databaseName } = create(`migration${version}`); const before = await fixture(databaseName, version);
      check((await call(actual, 'ExecutionEvents.Search', { query: '', limit: -1 })).ok, 'Actual schema3 open/read.');
      const db = await open(databaseName);
      try {
        check(db.version === 3, 'Forward-only version3.');
        for (const [store, values] of before) check(JSON.stringify(await dump(db, store)) === JSON.stringify(values), 'Complete original store changed: ' + store);
        const backup = await dump(db, version === 1 ? 'legacyTaskBackupV1' : 'schema2BackupV2');
        if (version === 1) check(JSON.stringify(backup) === JSON.stringify(before.get('tasks')), 'Complete actual schema1 backup.');
        else {
          for (const [store, values] of before) for (const original of values) {
            const copied = backup.find(row => row.value.store === store && JSON.stringify(row.value.key) === JSON.stringify(original.key));
            check(copied && JSON.stringify(copied.value.value) === JSON.stringify(original.value), 'Atomic schema2 whole-key/value backup: ' + store);
            if (store === 'unknownPriorStore') check(copied.value.value.bytes instanceof Uint8Array, 'Opaque structured-clone bytes type preserved.');
          }
        }
      } finally { db.close(); }
    });
    await run('full_canonical_event_duplicate_is_immutable_and_fresh_owner_reopens_same_task_run_links', async () => {
      const { actual, databaseName } = create('reopen'); const original = event(), candidate = await row(original);
      const first = await append(actual, [candidate]); check(first.ok && first.committed && first.value[0].sequence === '1', 'Actual append oncomplete receipt.');
      const duplicate = await append(actual, [candidate, candidate]);
      check(duplicate.ok && duplicate.value.length === 1 && JSON.stringify(duplicate.value[0]) === JSON.stringify(first.value[0]), 'Duplicate original bytes/order.');
      await actual.dispose(); const reopened = createTaskExecutionModule({ databaseName }); modules.push(reopened);
      const seen = await read(reopened, original.executionId);
      check(seen.ok && seen.value.length === 1 && seen.value[0].json === candidate.json &&
        JSON.parse(seen.value[0].json).taskId === original.taskId && JSON.parse(seen.value[0].json).executionId === original.executionId &&
        JSON.parse(seen.value[0].json).failure.attempt === 3 && JSON.parse(seen.value[0].json).timestamp === original.timestamp,
        'Complete canonical event/TaskId/ExecutionId/failure/ticks must reopen intact.');
    });
    await run('conflicting_event_payload_aborts_whole_actual_append_and_conserves_original_order', async () => {
      const { actual } = create('collision'); const original = event(), candidate = await row(original);
      check((await append(actual, [candidate])).ok, 'Original append.'); const next = event(original.executionId);
      const conflict = await append(actual, [await row(next), await row({ ...original, name: 'conflicting original' })]);
      check(!conflict.ok && conflict.error.code === 'ExecutionEventIdentityConflict', 'Actual atomic collision refusal.');
      const conserved = await read(actual, original.executionId);
      check(conserved.value.length === 1 && conserved.value[0].json === candidate.json && conserved.value[0].sequence === '1', 'No partial new event/overwrite.');
      check((await append(actual, [await row(next)])).value[0].sequence === '2', 'Aborted allocator mutation was rolled back.');
    });
    await run('decimal_int64_allocator_and_padded_order_are_exact_above_js_safe_integer', async () => {
      const { actual, databaseName } = create('int64'); await call(actual, 'ExecutionEvents.Search', { query: '', limit: -1 });
      const db = await open(databaseName); try { await transaction(db, ['executionEventSequences'], tx =>
        tx.objectStore('executionEventSequences').put({ profileId: ownerA, sequence: '9007199254740992' })); } finally { db.close(); }
      const first = event(), second = event(first.executionId);
      const written = await append(actual, [await row(first), await row(second)]);
      check(written.value[0].sequence === '9007199254740993' && written.value[1].sequence === '9007199254740994' &&
        written.value[0].sequenceSort === '0009007199254740993'.padStart(19, '0'), 'Exact signed Int64 sequence.');
      const readback = await read(actual, first.executionId); check(readback.value[0].eventId === first.eventId && readback.value[1].eventId === second.eventId, 'Exact order.');
    });
    await run('same_event_id_and_history_are_partitioned_only_by_actual_supplied_profile', async () => {
      const { actual } = create('profiles'); const original = event(); const first = await row(original);
      check((await append(actual, [first])).ok, 'Original profile append.');
      check((await read(actual, original.executionId, ownerB)).value.length === 0 &&
        (await call(actual, 'ExecutionEvents.Search', { query: '', limit: -1 }, ownerB)).value.length === 0, 'Foreign profile history empty.');
      const other = { ...event(), eventId: original.eventId, name: 'foreign profile event' };
      check((await append(actual, [await row(other, ownerB)], ownerB)).ok, 'Same EventId remains an independent profile record.');
      check((await read(actual, original.executionId)).value[0].json === first.json, 'Foreign profile never overwrites original bytes.');
    });
    await run('cancel_during_actual_hash_precedes_any_database_acquisition_and_joins_same_original', async () => {
      const entered = deferred(), held = deferred(); let opens = 0; const original = event(), candidate = await row(original);
      const { actual } = create('hash-cancel', { indexedDB: { open(...args) { opens++; return indexedDB.open(...args); } },
        crypto: { subtle: { digest(algorithm, bytes) { entered.resolve(); return held.promise.then(() => crypto.subtle.digest(algorithm, bytes)); } } } });
      const requestId = `event-control-${++requests}`; const pending = append(actual, [candidate], ownerA, requestId); let closed;
      try { await entered.promise; actual.cancel(requestId); closed = actual.dispose(); }
      finally { held.resolve(); }
      const reply = await pending; check(!reply.ok && reply.error.code === 'Cancelled' && opens === 0, 'No database/source acquisition after held hash cancellation.');
      await closed; check(actual.dispose() === closed, 'Same encompassing actual close.');
    });
    await run('same_profile_read_joins_earlier_held_append_before_claiming_absence', async () => {
      const entered = deferred(), held = deferred(); let hold = true; const original = event(), candidate = await row(original);
      const { actual } = create('hash-read', { crypto: { subtle: { digest(algorithm, bytes) {
        if (!hold) return crypto.subtle.digest(algorithm, bytes); entered.resolve(); return held.promise.then(() => crypto.subtle.digest(algorithm, bytes));
      } } } });
      const write = append(actual, [candidate]); let pending, finished = false;
      try { await entered.promise; pending = read(actual, original.executionId); pending.then(() => { finished = true; });
        for (let turn = 0; turn < 8; turn++) await Promise.resolve(); check(!finished, 'Read claimed absence before same admitted hash/append.'); }
      finally { hold = false; held.resolve(); }
      check((await write).committed && (await pending).value[0].json === candidate.json, 'Same acknowledged original read, no replay.');
    });
    await run('late_cancel_after_actual_transaction_complete_keeps_exact_commit_without_replay', async () => {
      const { actual, databaseName } = create('late'); const original = event(), candidate = await row(original);
      const requestId = `event-control-${++requests}`, nativeTransaction = IDBDatabase.prototype.transaction; let cancelled = false;
      try {
        IDBDatabase.prototype.transaction = function (...args) {
          const tx = nativeTransaction.apply(this, args);
          if (this.name === databaseName && args[1] === 'readwrite' && Array.from(args[0]).includes('executionEvents'))
            tx.addEventListener('complete', () => queueMicrotask(() => { cancelled = true; actual.cancel(requestId); }));
          return tx;
        };
        const ack = await append(actual, [candidate], ownerA, requestId);
        check(cancelled && ack.ok && ack.committed && ack.value[0].json === candidate.json, 'Actual complete receipt survives late stop.');
      } finally { IDBDatabase.prototype.transaction = nativeTransaction; }
      check((await read(actual, original.executionId)).value.length === 1, 'Late stop did not duplicate original append.');
    });
    await run('actual_abort_failure_and_close_failure_preserve_original_late_commit_and_all_causes', async () => {
      const { actual, databaseName } = create('abort-fault'); const original = event(), candidate = await row(original);
      const nativeTransaction = IDBDatabase.prototype.transaction, nativeAbort = IDBTransaction.prototype.abort, nativeClose = IDBDatabase.prototype.close;
      const abortCause = new Error('actual abort callback fault'), closeCause = new Error('actual acquired handle close fault');
      const requestId = `event-control-${++requests}`; let originalHandle, cancellationCause;
      try {
        IDBTransaction.prototype.abort = function () { if (this.db.name === databaseName) throw abortCause; return nativeAbort.call(this); };
        IDBDatabase.prototype.transaction = function (...args) {
          const tx = nativeTransaction.apply(this, args);
          if (this.name === databaseName && args[1] === 'readwrite' && Array.from(args[0]).includes('executionEvents')) {
            originalHandle = this; queueMicrotask(() => { try { actual.cancel(requestId); } catch (error) { cancellationCause = error; } });
          }
          return tx;
        };
        const observed = await append(actual, [candidate], ownerA, requestId);
        check(cancellationCause === abortCause && !observed.ok && observed.committed && observed.value[0].json === candidate.json,
          'Failed abort must keep actual later complete insertion separately.');
        IDBDatabase.prototype.close = function () { if (this.name === databaseName) throw closeCause; return nativeClose.call(this); };
        const fault = await failClose(actual); check(causes(fault).includes(abortCause) && causes(fault).includes(closeCause), 'Every distinct raw cause retained.');
      } finally {
        IDBDatabase.prototype.transaction = nativeTransaction; IDBTransaction.prototype.abort = nativeAbort; IDBDatabase.prototype.close = nativeClose;
        originalHandle?.close(); // Test-only external cleanup after the exact failed original close was observed; never relabel clean.
      }
    });
    await run('failed_actual_schema2_backup_rolls_back_version_and_every_original_byte', async () => {
      const { actual, databaseName } = create('backup-fault'); const before = await fixture(databaseName, 2);
      const nativePut = IDBObjectStore.prototype.put; const cause = new Error('actual migration backup scheduling fault');
      try {
        IDBObjectStore.prototype.put = function (...args) { if (this.name === 'schema2BackupV2') throw cause; return nativePut.apply(this, args); };
        const refused = await call(actual, 'ExecutionEvents.Search', { query: '', limit: -1 }); check(!refused.ok, 'Failed upgrade refused.');
      } finally { IDBObjectStore.prototype.put = nativePut; }
      const db = await open(databaseName); try {
        check(db.version === 2 && !db.objectStoreNames.contains('schema2BackupV2') && !db.objectStoreNames.contains('executionEvents'), 'Atomic versionchange rollback.');
        for (const [store, values] of before) check(JSON.stringify(await dump(db, store)) === JSON.stringify(values), 'Original bytes conserved after failure: ' + store);
      } finally { db.close(); }
      check(causes(await failClose(actual)).includes(cause), 'Exact failed backup cause retained by original owner.');
    });
    await run('stored_event_syntax_fault_remains_exact_in_same_owner_close_while_request_syntax_stays_argument_refusal', async () => {
      const requestOwner = create('request-syntax').actual;
      const invalidRequest = JSON.parse(await requestOwner.invoke(`invalid-request-${++requests}`, 'ExecutionEvents.Search', '{malformed request'));
      check(!invalidRequest.ok && invalidRequest.error.code === 'InvalidArgument', 'Malformed request remains an argument refusal.');
      await requestOwner.dispose(); // Same actual clean request owner; no stored source was acquired.
      const { actual, databaseName } = create('stored-syntax'); const original = event(), candidate = await row(original);
      const ack = await append(actual, [candidate]); check(ack.ok && ack.committed, 'Actual original event insertion.');
      const malformed = '{malformed persisted canonical event';
      const db = await open(databaseName);
      try { await transaction(db, ['executionEvents'], tx => tx.objectStore('executionEvents').put({ ...ack.value[0], json: malformed })); }
      finally { db.close(); }
      const nativeParse = JSON.parse, originalCauses = [];
      try {
        JSON.parse = function (...args) {
          try { return nativeParse.apply(this, args); }
          catch (error) { if (args[0] === malformed) originalCauses.push(error); throw error; }
        };
        const refusedRead = await read(actual, original.executionId);
        const refusedSearch = await call(actual, 'ExecutionEvents.Search', { query: '', limit: -1 });
        check(!refusedRead.ok && refusedRead.error.code === 'StorageUnavailable' && !refusedSearch.ok &&
          refusedSearch.error.code === 'StorageUnavailable' && originalCauses.length === 2 &&
          originalCauses.every(error => error instanceof SyntaxError), 'Stored JSON faults became request syntax or escaped actual reads.');
      } finally { JSON.parse = nativeParse; }
      const close = actual.dispose(), failure = await failClose(actual);
      check(actual.dispose() === close && originalCauses.every(error => causes(failure).includes(error)),
        'Exact persisted-source SyntaxErrors were replaced, pruned or waived by same encompassing owner close.');
    });
    await run('genuine_factory_open_argument_fault_remains_in_original_close_without_replay', async () => {
      let originalCause, opens = 0;
      const { actual } = create('open-fault', { indexedDB: { open(name) {
        opens++; try { return indexedDB.open(name, 0); } catch (error) { originalCause = error; throw error; }
      } } });
      const refused = await append(actual, [await row(event())]); check(!refused.ok && opens === 1 && originalCause, 'Actual browser open failed once.');
      check(causes(await failClose(actual)).includes(originalCause), 'Exact native open cause retained, not replayed or waived.');
    });
  } catch (error) { originalFailure = error; }
  finally {
    const failures = originalFailure ? [originalFailure] : [];
    for (const actual of modules) try { await actual.dispose(); } catch (error) { failures.push(error); }
    for (const name of names) try { await new Promise((resolve, reject) => {
      const request = indexedDB.deleteDatabase(name); request.onsuccess = resolve; request.onerror = () => reject(request.error);
      request.onblocked = () => reject(new Error('Actual control database deletion remains blocked; no clean cleanup claim.'));
    }); } catch (error) { failures.push(error); }
    if (failures.length) throw new AggregateError(failures, 'Actual event storage control or cleanup failed.');
  }
  return { controls: results.length, results };
}
