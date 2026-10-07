import { createTaskExecutionModule } from '../../wwwroot/tasks-indexeddb.js';

// Authenticated metadata test fixtures only. These execute real browser IndexedDB/WebCrypto;
// they do not issue a signed actor, Task admission, provider, Home, permission or GUI witness.
export async function runConversationStorageControls() {
  if (!globalThis.indexedDB || !globalThis.crypto?.subtle || !globalThis.IDBDatabase)
    throw new Error('Genuine browser IndexedDB/WebCrypto is required; no memory adapter is supported.');
  const results = [], modules = [], databases = [];
  const check = (truth, message) => { if (!truth) throw new Error(message); };
  const id = () => crypto.randomUUID();
  const profileA = `cake-account-profile:${'a'.repeat(64)}:${id()}`;
  const profileB = `cake-account-profile:${'a'.repeat(64)}:${id()}`;
  let requests = 0;
  const digest = async text => [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text)))].map(b => b.toString(16).padStart(2, '0')).join('');
  const row = async (payload, entity, owner = profileA) => {
    const json = JSON.stringify(payload);
    return { profileId: owner, entity, id: payload.id, json, sha256: await digest(json), revision: '1',
      createdAt: payload.createdAt, updatedAt: payload.updatedAt ?? null, mode: payload.mode ?? null, kind: payload.kind ?? null,
      conversationId: payload.conversationId ?? null, spaceId: payload.spaceId ?? null,
      isTemporary: payload.isTemporary ?? false, isArchived: payload.isArchived ?? false, isCompacted: payload.isCompacted ?? false };
  };
  const conversation = (contextId = id(), spaceId = null) => ({ id: contextId, mode: 2, kind: 3, title: 'Original Tasks context',
    containerId: null, lessonId: null, isPinned: false, isTemporary: false,
    createdAt: '2026-10-06T00:00:00+00:00', updatedAt: '2026-10-06T00:00:00+00:00', isArchived: false,
    parentConversationId: null, compactedAt: null, spaceId });
  const create = name => {
    const databaseName = `a2-conversation-control-${name}-${id()}`;
    databases.push(databaseName); const actual = createTaskExecutionModule({ databaseName }); modules.push(actual);
    return { databaseName, actual };
  };
  const invoke = async (actual, action, args, owner = profileA) => JSON.parse(await actual.invoke(`request-${++requests}`, action,
    JSON.stringify({ ...args, profileId: owner })));
  const put = async (actual, value, owner = profileA, expectedRevision) => invoke(actual, 'Conversation.Upsert',
    { row: await row(value, 'conversation', owner), ...(expectedRevision === undefined ? {} : { expectedRevision }) }, owner);
  const one = async (actual, context, owner = profileA) => invoke(actual, 'Conversation.Get', { id: context.id }, owner);
  const run = async (name, body) => { await body(); results.push({ name, status: 'PASS' }); };
  let originalFailure;
  try {
    await run('same_canonical_context_reopens_from_a_fresh_module_and_foreign_profile_reads_no_rows', async () => {
      const { databaseName, actual } = create('reopen'); const original = conversation();
      const written = await put(actual, original, profileA, '0');
      check(written.ok && written.committed && written.value.rows[0].revision === '1', 'actual insert receipt');
      await actual.dispose();
      const reopened = createTaskExecutionModule({ databaseName }); modules.push(reopened);
      const read = await one(reopened, original); check(read.ok && JSON.stringify(JSON.parse(read.value.json)) === JSON.stringify(original), 'same complete canonical record');
      const foreign = await one(reopened, original, profileB); check(foreign.ok && foreign.value === null, 'foreign profile cannot read context');
      const recent = await invoke(reopened, 'Conversation.Recent', { mode: 2, limit: 10 }, profileB); check(recent.ok && recent.value.length === 0, 'foreign recent history empty');
    });
    await run('two_tabs_stale_cas_refuses_without_overwriting_original_context_or_revision', async () => {
      const { databaseName, actual } = create('cas'); const second = createTaskExecutionModule({ databaseName }); modules.push(second);
      const context = conversation(); check((await put(actual, context, profileA, '0')).ok, 'first insertion');
      const observed = await one(second, context); check(observed.value.revision === '1', 'second tab actual revision');
      check((await put(actual, { ...context, title: 'First exact update' }, profileA, '1')).ok, 'first tab update');
      const refused = await put(second, { ...context, title: 'Stale update' }, profileA, '1');
      check(!refused.ok && refused.error.code === 'ConversationRevisionConflict', 'stale conflict');
      const read = await one(second, context); check(read.value.revision === '2' && JSON.parse(read.value.json).title === 'First exact update', 'no overwrite');
    });
    await run('native_original_createdAt_is_preserved_and_scoped_filter_precedes_limit', async () => {
      const { actual } = create('created'); const context = conversation(); check((await put(actual, context)).ok, 'initial');
      check((await put(actual, { ...context, createdAt: '2026-10-07T00:00:00+00:00', title: 'Updated' })).ok, 'updated');
      check(JSON.parse((await one(actual, context)).value.json).createdAt === context.createdAt, 'original creation identity');
      const group = id(); const ordinary = { ...conversation(), mode: 0, kind: 0, containerId: group };
      const newer = { ...conversation(), mode: 0, kind: 0, updatedAt: '2026-10-08T00:00:00+00:00' };
      check((await put(actual, ordinary)).ok && (await put(actual, newer)).ok, 'both scopes');
      const scoped = await invoke(actual, 'Conversation.Recent', { mode: 0, limit: 1, scope: { mode: 0, kind: 0, containerId: group, lessonId: null } });
      check(scoped.ok && scoped.value.length === 1 && scoped.value[0].id === ordinary.id, 'scope before limit');
    });
    await run('native_unbounded_recent_and_archived_limits_keep_original_rows_without_browser_only_cap', async () => {
      const { actual } = create('limits');
      const first = conversation(), second = conversation(), archived = { ...conversation(), isArchived: true };
      check((await put(actual, first)).ok && (await put(actual, second)).ok && (await put(actual, archived)).ok, 'original rows');
      const negative = await invoke(actual, 'Conversation.Recent', { mode: 2, limit: -1 });
      check(negative.ok && negative.value.length === 2, 'native negative recent LIMIT is unbounded');
      const large = await invoke(actual, 'Conversation.Recent', { mode: 2, limit: 2147483647 });
      check(large.ok && large.value.length === 2, 'no invented 10000-row limit');
      const zero = await invoke(actual, 'Conversation.Recent', { mode: 2, limit: 0 });
      check(zero.ok && zero.value.length === 0, 'zero returns no rows');
      const history = await invoke(actual, 'Conversation.Archived', { mode: 2, limit: -1 });
      check(history.ok && history.value.length === 1 && history.value[0].id === archived.id, 'same native archived limit');
    });
    await run('messages_preserve_invalid_metadata_text_and_compaction_excludes_only_requested_originals', async () => {
      const { actual } = create('messages'); const context = conversation(); check((await put(actual, context)).ok, 'context');
      const first = { id: id(), conversationId: context.id, role: 0, content: 'system original', agentName: null, modelName: null,
        metadataJson: '{historical invalid json', createdAt: '2026-10-06T01:00:00+00:00', isCompacted: false };
      const second = { ...first, id: id(), role: 1, content: 'user original', createdAt: '2026-10-06T02:00:00+00:00' };
      check((await invoke(actual, 'Conversation.PutMessage', { row: await row(first, 'message') })).ok, 'first message');
      check((await invoke(actual, 'Conversation.PutMessage', { row: await row(second, 'message') })).ok, 'second message');
      check((await invoke(actual, 'Conversation.CompactMessages', { conversationId: context.id, messageIds: [first.id] })).ok, 'actual atomic compact');
      const all = await invoke(actual, 'Conversation.Messages', { conversationId: context.id, contextOnly: false });
      const current = await invoke(actual, 'Conversation.Messages', { conversationId: context.id, contextOnly: true });
      check(all.value.length === 2 && JSON.parse(all.value[0].json).metadataJson === first.metadataJson, 'all original text retained');
      check(current.value.length === 1 && current.value[0].id === second.id, 'selected compaction only');
    });
    await run('compact_summary_cannot_be_deleted_and_foreign_message_cannot_mutate_current_context', async () => {
      const { actual } = create('protected'); const context = conversation(); check((await put(actual, context)).ok, 'context');
      const summary = { id: id(), conversationId: context.id, kind: 1, title: 'Real compact summary', content: 'continuity', evidence: '', createdAt: context.createdAt };
      check((await invoke(actual, 'Conversation.PutContext', { row: await row(summary, 'context') })).ok, 'summary');
      const removed = await invoke(actual, 'Conversation.DeleteContext', { conversationId: context.id, id: summary.id });
      check(removed.ok && removed.committed && removed.value.removed === 0 && removed.value.protectedEntry, 'summary protected');
      check((await invoke(actual, 'Conversation.Context', { conversationId: context.id })).value.length === 1, 'summary still stored');
      const foreign = { id: id(), conversationId: context.id, role: 1, content: 'foreign', agentName: null, modelName: null, metadataJson: null, createdAt: context.createdAt, isCompacted: false };
      const denied = await invoke(actual, 'Conversation.PutMessage', { row: await row(foreign, 'message', profileB) }, profileB);
      check(!denied.ok && denied.error.code === 'ContextUnavailable', 'foreign source no context');
      check((await invoke(actual, 'Conversation.Messages', { conversationId: context.id })).value.length === 0, 'no cross-profile mutation');
    });
    await run('detach_space_is_atomic_for_all_canonical_contexts_and_delete_keeps_other_profile', async () => {
      const { actual } = create('space'); const space = id(), first = conversation(id(), space), second = { ...conversation(id(), space), isArchived: true };
      check((await put(actual, first)).ok && (await put(actual, second)).ok && (await put(actual, first, profileB)).ok, 'same original IDs in isolated profiles');
      check((await invoke(actual, 'Conversation.DetachSpace', { spaceId: space })).ok, 'actual detach');
      check(JSON.parse((await one(actual, first)).value.json).spaceId === null && JSON.parse((await one(actual, second)).value.json).spaceId === null, 'active/archive both detached');
      check(JSON.parse((await one(actual, first, profileB)).value.json).spaceId === space, 'foreign membership untouched');
      check((await invoke(actual, 'Conversation.Delete', { id: first.id })).ok, 'delete original');
      check((await one(actual, first)).value === null && (await one(actual, first, profileB)).value !== null, 'same profile delete only');
    });
    await run('genuine_transaction_abort_keeps_entire_multirow_detach_before_state', async () => {
      const { databaseName, actual } = create('abort'); const space = id(), first = conversation(id(), space), second = conversation(id(), space);
      check((await put(actual, first)).ok && (await put(actual, second)).ok, 'two originals');
      const before1 = (await one(actual, first)).value, before2 = (await one(actual, second)).value;
      const originalTransaction = IDBDatabase.prototype.transaction;
      let realAbort = false;
      try {
        IDBDatabase.prototype.transaction = function (...args) {
          const tx = originalTransaction.apply(this, args);
          if (this.name === databaseName && args[1] === 'readwrite') queueMicrotask(() => { realAbort = true; tx.abort(); });
          return tx; // SAME actual browser transaction and events, never a memory substitute.
        };
        const denied = await invoke(actual, 'Conversation.DetachSpace', { spaceId: space }); check(!denied.ok && realAbort, 'actual abort observed');
      } finally { IDBDatabase.prototype.transaction = originalTransaction; }
      check(JSON.stringify((await one(actual, first)).value) === JSON.stringify(before1) && JSON.stringify((await one(actual, second)).value) === JSON.stringify(before2), 'no partial batch overwrite');
      let closeFault; try { await actual.dispose(); } catch (error) { closeFault = error; }
      check(closeFault instanceof AggregateError, 'actual abort cause retained by encompassing storage owner');
      modules.splice(modules.indexOf(actual), 1); // Already joined exact faulted close; preserved in this result control.
    });
    await run('task_partition_requires_the_real_tasks_context_and_preserves_same_canonical_ids', async () => {
      const { actual } = create('task'); const context = conversation(); check((await put(actual, context)).ok, 'canonical context');
      const taskId = id(), executionId = id(), json = JSON.stringify({ taskId, contextId: context.id, executionId, persistenceRevision: 1 });
      const task = { taskId, contextId: context.id, executionId, revision: '1', state: 3, json, sha256: await digest(json), createdAt: context.createdAt, updatedAt: context.updatedAt };
      const receipt = await invoke(actual, 'Upsert', { row: task, expectedRevision: '0' }); check(receipt.ok && receipt.committed, 'actual task CAS');
      const seen = await invoke(actual, 'GetByContext', { contextId: context.id }); check(seen.value.taskId === taskId && seen.value.executionId === executionId, 'same task/run');
      check((await invoke(actual, 'Get', { taskId }, profileB)).value === null, 'foreign task hidden');
      const missing = { ...task, taskId: id(), contextId: id() }; const denied = await invoke(actual, 'Upsert', { row: missing, expectedRevision: '0' });
      check(!denied.ok && denied.error.code === 'ContextUnavailable', 'account or copied UUID cannot create context ownership');
    });
    for (const action of ['Get', 'GetByContext', 'GetResumable']) {
      await run(`same_profile_${action}_joins_the_earlier_actual_hash_and_write_before_inspection`, async () => {
        const databaseName = `a2-conversation-held-task-${action}-${id()}`;
        databases.push(databaseName);
        const context = conversation(), taskId = id(), executionId = id();
        const json = JSON.stringify({ taskId, contextId: context.id, executionId, persistenceRevision: 1 });
        let enterHash, releaseHash;
        const entered = new Promise(resolve => { enterHash = resolve; });
        const held = new Promise(resolve => { releaseHash = resolve; });
        const actualCrypto = { subtle: { digest: (algorithm, bytes) => {
          if (new TextDecoder().decode(bytes) !== json) return crypto.subtle.digest(algorithm, bytes);
          enterHash(); return held.then(() => crypto.subtle.digest(algorithm, bytes));
        } } }; // Only a phase barrier before SAME genuine browser WebCrypto.
        const actual = createTaskExecutionModule({ databaseName, crypto: actualCrypto }); modules.push(actual);
        check((await put(actual, context)).ok, 'actual canonical Tasks context');
        const task = { taskId, contextId: context.id, executionId, revision: '1', state: 3, json,
          sha256: await digest(json), createdAt: context.createdAt, updatedAt: context.updatedAt };
        const originalTransaction = IDBDatabase.prototype.transaction;
        let taskReadAcquisitions = 0, readSettled = false, originalFailure;
        let write, read;
        try {
          IDBDatabase.prototype.transaction = function (...args) {
            if (this.name === databaseName && args[1] === 'readonly' &&
                Array.from(args[0]).includes('taskProfiles')) taskReadAcquisitions++;
            return originalTransaction.apply(this, args); // SAME actual browser transaction.
          };
          write = invoke(actual, 'Upsert', { row: task, expectedRevision: '0' });
          await entered;
          const query = action === 'Get' ? { taskId } : action === 'GetByContext' ? { contextId: context.id } : {};
          read = invoke(actual, action, query).then(value => { readSettled = true; return value; });
          const foreign = await invoke(actual, 'Get', { taskId }, profileB);
          // The actual foreign transaction's terminal reply is the barrier. It
          // must complete independently while the SAME-profile read has no transaction.
          check(foreign.ok && foreign.value === null, 'foreign profile observes no task and does not wait another profile');
          check(taskReadAcquisitions === 1, 'only the foreign original acquired a readonly transaction while hash is held');
          check(!readSettled, 'same-profile original cannot certify absence before the admitted write settles');
          releaseHash();
          const committed = await write, inspected = await read;
          check(committed.ok && committed.committed, 'same actual prior write committed');
          check(inspected.ok, 'same actual inspection completed');
          const observed = action === 'GetResumable' ? inspected.value[0] : inspected.value;
          check(observed?.taskId === taskId && observed.contextId === context.id && observed.executionId === executionId,
            'actual inspection returns SAME Task/Context/Run');
          check(observed.json === json && observed.revision === '1' && observed.sha256 === task.sha256,
            'complete exact task bytes/revision/hash retained');
          check(taskReadAcquisitions === 2, 'matching original acquires only after its prior write terminal');
        } catch (error) { originalFailure = error; throw error; }
        finally {
          releaseHash(); IDBDatabase.prototype.transaction = originalTransaction;
          const joined = await Promise.allSettled([write, read].filter(Boolean));
          const cleanup = joined.filter(value => value.status === 'rejected').map(value => value.reason);
          if (cleanup.length) throw new AggregateError(originalFailure ? [originalFailure, ...cleanup] : cleanup,
            'Actual held write/inspection controls independently joined with original causes.');
        }
      });
    }
    await run('canceled_same_profile_inspection_still_joins_the_prior_write_and_never_acquires_its_transaction', async () => {
      const databaseName = `a2-conversation-canceled-task-inspection-${id()}`;
      databases.push(databaseName);
      const context = conversation(), taskId = id(), executionId = id();
      const json = JSON.stringify({ taskId, contextId: context.id, executionId, persistenceRevision: 1 });
      let enterHash, releaseHash;
      const entered = new Promise(resolve => { enterHash = resolve; }), held = new Promise(resolve => { releaseHash = resolve; });
      const actualCrypto = { subtle: { digest: (algorithm, bytes) => {
        if (new TextDecoder().decode(bytes) !== json) return crypto.subtle.digest(algorithm, bytes);
        enterHash(); return held.then(() => crypto.subtle.digest(algorithm, bytes));
      } } };
      const actual = createTaskExecutionModule({ databaseName, crypto: actualCrypto }); modules.push(actual);
      check((await put(actual, context)).ok, 'actual canonical Tasks context');
      const task = { taskId, contextId: context.id, executionId, revision: '1', state: 3, json,
        sha256: await digest(json), createdAt: context.createdAt, updatedAt: context.updatedAt };
      const originalTransaction = IDBDatabase.prototype.transaction;
      const requestId = `cancel-inspection-${++requests}`;
      let taskReadAcquisitions = 0, readSettled = false, originalFailure;
      let write, read;
      try {
        IDBDatabase.prototype.transaction = function (...args) {
          if (this.name === databaseName && args[1] === 'readonly' && Array.from(args[0]).includes('taskProfiles')) taskReadAcquisitions++;
          return originalTransaction.apply(this, args);
        };
        write = invoke(actual, 'Upsert', { row: task, expectedRevision: '0' }); await entered;
        read = actual.invoke(requestId, 'Get', JSON.stringify({ profileId: profileA, taskId }))
          .then(reply => { readSettled = true; return JSON.parse(reply); });
        actual.cancel(requestId);
        const foreign = await invoke(actual, 'Get', { taskId }, profileB);
        check(foreign.ok && foreign.value === null, 'independent actual foreign transaction completes');
        check(!readSettled && taskReadAcquisitions === 1, 'cancellation is not prior-write settlement or a new acquisition');
        releaseHash();
        const committed = await write, canceled = await read;
        check(committed.ok && committed.committed, 'view/read cancellation never cancels the SAME admitted writer');
        check(!canceled.ok && canceled.error.code === 'Cancelled', 'actual canceled read refuses after prior original terminal');
        check(taskReadAcquisitions === 1, 'canceled inspection never acquires a readonly transaction');
        const inspected = await invoke(actual, 'Get', { taskId });
        check(inspected.ok && inspected.value.json === json && inspected.value.executionId === executionId,
          'actual write is durable under SAME Task/Run after canceled inspection');
      } catch (error) { originalFailure = error; throw error; }
      finally {
        releaseHash(); IDBDatabase.prototype.transaction = originalTransaction;
        const joined = await Promise.allSettled([write, read].filter(Boolean));
        const cleanup = joined.filter(value => value.status === 'rejected').map(value => value.reason);
        if (cleanup.length) throw new AggregateError(originalFailure ? [originalFailure, ...cleanup] : cleanup,
          'Actual canceled inspection/write originals independently joined.');
      }
    });
    await run('forward_v1_migration_preserves_complete_task_bytes_and_backs_up_unbound_rows', async () => {
      const databaseName = `a2-conversation-v1-${id()}`; databases.push(databaseName);
      const taskId = id(), contextId = id(), executionId = id(), json = '{"persistenceRevision":9007199254740993,"legacy":"exact"}';
      const legacy = { taskId, contextId, executionId, revision: '9007199254740993', state: 3, json, sha256: await digest(json), createdAt: '2026-10-06T00:00:00+00:00', updatedAt: '2026-10-06T00:00:00+00:00' };
      await new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => { const tasks = request.result.createObjectStore('tasks', { keyPath: 'taskId' }); tasks.createIndex('contextId', 'contextId'); tasks.put(legacy); };
        request.onerror = () => reject(request.error); request.onsuccess = () => { request.result.close(); resolve(); };
      });
      const actual = createTaskExecutionModule({ databaseName }); modules.push(actual);
      const unbound = JSON.parse(await actual.invoke(`legacy-${++requests}`, 'Get', JSON.stringify({ taskId })));
      check(JSON.stringify(unbound.value) === JSON.stringify(legacy), 'complete old row untouched');
      check((await invoke(actual, 'Get', { taskId })).value === null, 'legacy unbound source cannot acquire profile by metadata');
      await actual.dispose();
      const backup = await new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName); request.onerror = () => reject(request.error);
        request.onsuccess = () => {
          const db = request.result, tx = db.transaction(['legacyTaskBackupV1']), get = tx.objectStore('legacyTaskBackupV1').get(taskId);
          let value; get.onsuccess = () => { value = get.result; }; tx.oncomplete = () => { db.close(); resolve(value); }; tx.onabort = () => { db.close(); reject(tx.error); };
        };
      });
      check(JSON.stringify(backup) === JSON.stringify(legacy), 'actual forward migration backup exact');
    });
  } catch (error) { originalFailure = error; throw error; }
  finally {
    const cleanupErrors = [];
    for (const actual of modules) try { await actual.dispose(); } catch (error) { cleanupErrors.push(error); }
    for (const name of databases) try {
      await new Promise((resolve, reject) => { const request = indexedDB.deleteDatabase(name); request.onsuccess = resolve; request.onerror = () => reject(request.error); request.onblocked = () => {}; });
    } catch (error) { cleanupErrors.push(error); }
    if (cleanupErrors.length) throw new AggregateError(originalFailure ? [originalFailure, ...cleanupErrors] : cleanupErrors, 'Original browser storage controls and real cleanup failed.');
  }
  return { platform: 'actual browser IndexedDB + WebCrypto', acceptance: 'storage-only synthetic profile observations; no issuer/provider/UI grant', results };
}
