import { createTaskExecutionModule } from '../../wwwroot/tasks-indexeddb.js';

// Real IndexedDB/WebCrypto with explicit profile metadata fixtures. These do not
// authenticate a user or prove deployed account/provider/workspace acceptance.
export async function runSpaceSettingsStorageControls(createPriorModule = null) {
  if (!globalThis.indexedDB || !globalThis.crypto?.subtle) throw new Error('Real browser storage is required.');
  const results = [], modules = [], names = [];
  const check = (ok, message) => { if (!ok) throw new Error(message); };
  const uuid = () => crypto.randomUUID();
  const profileA = `cake-account-profile:${'a'.repeat(64)}:${uuid()}`;
  const profileB = `cake-account-profile:${'a'.repeat(64)}:${uuid()}`;
  const digest = async json => [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(json)))].map(b => b.toString(16).padStart(2, '0')).join('');
  const name = () => { const n = `astra-spaces-storage-${uuid()}`; names.push(n, `${n}-spaces`); return n; };
  const module = databaseName => { const m = createTaskExecutionModule({ databaseName }); modules.push(m); return m; };
  const call = async (m, action, args = {}, owner = profileA) => JSON.parse(await m.invoke(uuid(), action, JSON.stringify({ ...args, profileId: owner })));
  const read = (m, owner = profileA) => call(m, 'SpacesSettings.Export', {}, owner);
  const put = async (m, key, expected, replacement, guards = {}, owner = profileA) => call(m, 'SpacesSettings.CompareExchange',
    { key, expected, replacement, sha256: replacement === null ? null : await digest(replacement), guards }, owner);
  const run = async (id, body) => { await body(); results.push({ id, status: 'PASS' }); };
  const open = (databaseName, version, upgrade) => new Promise((resolve, reject) => {
    const r = version === undefined ? indexedDB.open(databaseName) : indexedDB.open(databaseName, version);
    r.onupgradeneeded = () => upgrade(r.result); r.onerror = () => reject(r.error); r.onsuccess = () => resolve(r.result);
  });
  const dump = (db, store) => new Promise((resolve, reject) => {
    const tx = db.transaction(store); let rows;
    tx.objectStore(store).getAll().onsuccess = e => { rows = e.target.result; };
    tx.oncomplete = () => resolve(rows); tx.onabort = () => reject(tx.error);
  });
  try {
    await run('signed_profile_partition_and_same_payload_survive_owner_reopen', async () => {
      const n = name(), m = module(n), json = JSON.stringify({ Version: 3, Spaces: [{ Id: uuid(), Name: 'My preserved configuration' }] });
      const saved = await put(m, 'spaces.registry', null, json);
      check(saved.ok && saved.committed && saved.value.exchanged && saved.value.version === 1, 'Exact durable settings ACK');
      await m.dispose();
      const fresh = module(n), own = await read(fresh), foreign = await read(fresh, profileB);
      check(own.ok && own.value.rows.length === 1 && own.value.rows[0].json === json, 'Fresh owner same bytes');
      check(foreign.ok && foreign.value.version === 0 && foreign.value.rows.length === 0, 'Foreign partition empty');
    });
    await run('stale_tab_cas_and_registry_guard_cannot_overwrite_or_select_removed_configuration', async () => {
      const n = name(), first = module(n), second = module(n);
      const a = '{"Version":3,"Spaces":[]}', b = '{"Version":3,"Spaces":["new"]}';
      check((await put(first, 'spaces.registry', null, a)).value.exchanged, 'Initial seed');
      check((await put(first, 'spaces.registry', a, b)).value.exchanged, 'Exact update');
      const stale = await put(second, 'spaces.registry', a, '{"lost":true}');
      check(stale.ok && !stale.value.exchanged && stale.value.current.json === b, 'Stale edit refused');
      const guard = await put(second, 'spaces.current', null, JSON.stringify(uuid()), { 'spaces.registry': a });
      check(guard.ok && !guard.value.exchanged && guard.value.conflictingGuardKey === 'spaces.registry', 'Selection guard refused');
      check((await read(first)).value.rows.length === 1, 'No partial selection write');
    });
    await run('simultaneous_same_owner_writes_terminate_with_one_exact_cas_winner', async () => {
      const m = module(name());
      const replies = await Promise.all([put(m, 'spaces.registry', null, '{"a":1}'), put(m, 'spaces.registry', null, '{"b":1}')]);
      check(replies.filter(reply => reply.ok && reply.value.exchanged).length === 1, 'One atomic winner');
      check((await read(m)).value.version === 1, 'Exactly one committed logical revision');
    });
    await run('revoked_pending_owner_cannot_commit_after_reopen', async () => {
      const n = name(), m = module(n), request = uuid(), json = '{"cancelled":true}';
      const args = { profileId: profileA, key: 'spaces.registry', expected: null, replacement: json, sha256: await digest(json), guards: {} };
      const pending = m.invoke(request, 'SpacesSettings.CompareExchange', JSON.stringify(args));
      const close = m.dispose();
      const reply = JSON.parse(await pending); await close;
      check(!reply.ok && reply.error.code === 'Cancelled', 'Pending original cancelled');
      check((await read(module(n))).value.rows.length === 0, 'Reopen observes no late write');
    });
    await run('spaces_writes_leave_task_schema3_and_every_prior_store_compatible_with_old_deployment', async () => {
      const n = name();
      const prior = await open(n, 3, db => {
        for (const store of ['tasks', 'conversations', 'conversationMessages', 'conversationContexts', 'taskProfiles',
          'executionEvents', 'executionEventSequences', 'unknownPreservedStore']) db.createObjectStore(store, { keyPath: 'id' });
      });
      const expected = {};
      await new Promise((resolve, reject) => {
        const tx = prior.transaction([...prior.objectStoreNames], 'readwrite');
        for (const store of prior.objectStoreNames) { const row = { id: uuid(), opaque: `${store}:9007199254740993` }; expected[store] = [row]; tx.objectStore(store).put(row); }
        tx.oncomplete = resolve; tx.onabort = () => reject(tx.error);
      });
      prior.close(); const m = module(n);
      check((await put(m, 'spaces.registry', null, '{"Version":3,"Spaces":[]}')).value.exchanged, 'Separate Space settings saved'); await m.dispose();
      const upgraded = await open(n, 3);
      try {
        check(upgraded.version === 3 && !upgraded.objectStoreNames.contains('spaceSettings'), 'Old deployment opens unchanged schema3');
        for (const [store, rows] of Object.entries(expected)) check(JSON.stringify(await dump(upgraded, store)) === JSON.stringify(rows), `Preserved ${store}`);
      } finally { upgraded.close(); }
    });
    await run('unsupported_keys_and_foreign_guard_shapes_have_no_effect', async () => {
      const m = module(name());
      const bad = await put(m, 'account.token', null, '"unowned"');
      check(!bad.ok && bad.error.code === 'InvalidArgument', 'Unknown key refused');
      const guard = await put(m, 'spaces.registry', null, '{}', { 'account.token': null });
      check(!guard.ok && guard.error.code === 'InvalidArgument', 'Unknown guard refused');
      check((await read(m)).value.version === 0, 'No mutation');
    });
    if (createPriorModule) await run('exact_prior_production_module_reopens_same_profile_task_after_spaces_save', async () => {
      const n = name(), current = module(n), contextId = uuid(), taskId = uuid(), executionId = uuid();
      const createdAt = '2026-10-13T00:00:00+00:00';
      const context = { id: contextId, mode: 2, kind: 3, title: 'Rollback fixture', containerId: null, lessonId: null,
        isPinned: false, isTemporary: false, createdAt, updatedAt: createdAt, isArchived: false,
        parentConversationId: null, compactedAt: null, spaceId: null };
      const contextJson = JSON.stringify(context);
      const inserted = await call(current, 'Conversation.Upsert', { row: { profileId: profileA, entity: 'conversation', id: contextId,
        json: contextJson, sha256: await digest(contextJson), revision: '1', createdAt, updatedAt: createdAt, mode: 2, kind: 3,
        conversationId: null, spaceId: null, isTemporary: false, isArchived: false, isCompacted: false }, expectedRevision: '0' });
      check(inserted.ok && inserted.committed, 'Original canonical context fixture saved');
      const json = JSON.stringify({ taskId, contextId, executionId, persistenceRevision: 1 });
      const task = { taskId, contextId, executionId, revision: '1', state: 3, json, sha256: await digest(json), createdAt, updatedAt: createdAt };
      check((await call(current, 'Upsert', { row: task, expectedRevision: '0' })).committed, 'Original Task fixture saved');
      check((await put(current, 'spaces.registry', null, '{"Version":3,"Spaces":[]}')).value.exchanged, 'Current manager settings saved');
      await current.dispose();
      const prior = createPriorModule({ databaseName: n }); modules.push(prior);
      const reopened = await call(prior, 'Get', { taskId });
      check(reopened.ok && reopened.value.taskId === taskId && reopened.value.executionId === executionId && reopened.value.json === json,
        'Exact prior source reopens preserved task/profile without VersionError');
      await prior.dispose();
      check((await read(module(n))).value.rows[0].json === '{"Version":3,"Spaces":[]}', 'Returning to candidate preserves Space settings');
    });
    return results;
  } finally {
    const settled = await Promise.allSettled(modules.map(m => m.dispose()));
    const failures = settled.filter(result => result.status === 'rejected');
    for (const databaseName of names) await new Promise((resolve, reject) => {
      const r = indexedDB.deleteDatabase(databaseName); r.onsuccess = resolve; r.onerror = () => reject(r.error); r.onblocked = () => reject(new Error('Fixture cleanup blocked'));
    });
    if (failures.length) throw new AggregateError(failures.map(result => result.reason), 'Storage fixture cleanup failed');
  }
}
