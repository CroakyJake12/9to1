import { createTaskExecutionModule } from '../../wwwroot/tasks-indexeddb.js';

// Real Chromium IndexedDB/WebCrypto and real file-input selection. Synthetic profile
// observations exercise partitioning only; this suite grants no authenticated actor.
export async function runConversationProductionStorageControls() {
  const results = [], modules = [], databases = [];
  const id = () => crypto.randomUUID(), check = (value, message) => { if (!value) throw new Error(message); };
  const a = `cake-account-profile:${'a'.repeat(64)}:${id()}`, b = `cake-account-profile:${'b'.repeat(64)}:${id()}`;
  const hash = async text => [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text)))].map(byte => byte.toString(16).padStart(2, '0')).join('');
  let count = 0;
  const invoke = async (module, action, args = {}, owner = a) => JSON.parse(await module.invoke(`production-${++count}`, `ConversationProduction.${action}`, JSON.stringify({ ...args, profileId: owner })));
  const create = label => { const databaseName = `production-control-${label}-${id()}`; databases.push(databaseName); const module = createTaskExecutionModule({ databaseName }); modules.push(module); return { databaseName, module }; };
  const cas = async (module, conversationId, expected, replacement, owner = a) => invoke(module, 'CompareExchange', { conversationId, expected, replacement, sha256: await hash(replacement) }, owner);
  const run = async (name, body) => { await body(); results.push({ name, status: 'PASS' }); };
  const closeFault = async module => { let error; try { await module.dispose(); } catch (cause) { error = cause; } check(error instanceof AggregateError, 'actual original fault is retained by close'); modules.splice(modules.indexOf(module), 1); };
  let failure;
  try {
    await run('exact_profile_production_cas_preserves_pending_unknown_fields_and_reopens', async () => {
      const { databaseName, module } = create('restart'), conversationId = id();
      const first = JSON.stringify({ schemaVersion: 1, conversationId, pendingOriginalMessageWrite: { id: id(), hash: 'exact' }, future: { preserved: true } });
      const inserted = await cas(module, conversationId, null, first); check(inserted.ok && inserted.committed && inserted.value.exchanged && inserted.value.current.revision === '1', 'actual committed insert');
      await module.dispose(); const restarted = createTaskExecutionModule({ databaseName }); modules.push(restarted);
      check((await invoke(restarted, 'Export', { conversationId })).value[0].json === first, 'same exact pending payload after restart');
      check((await invoke(restarted, 'Export', { conversationId }, b)).value.length === 0, 'foreign profile sees no payload');
      const next = JSON.stringify({ schemaVersion: 1, conversationId, marker: 'acknowledged by owner' });
      check((await cas(restarted, conversationId, first, next)).value.exchanged, 'exact prior bytes CAS');
      const stale = await cas(restarted, conversationId, first, '{"stale":true}'); check(stale.ok && !stale.value.exchanged && stale.value.current.json === next, 'stale write cannot overwrite newer state');
    });
    await run('two_actual_tab_transactions_have_one_exact_production_cas_winner', async () => {
      const { databaseName, module } = create('two-tabs'); const second = createTaskExecutionModule({ databaseName }); modules.push(second);
      const conversationId = id(); const first = '{"original":true}'; check((await cas(module, conversationId, null, first)).value.exchanged, 'original insert');
      const results = await Promise.all([cas(module, conversationId, first, '{"tab":1}'), cas(second, conversationId, first, '{"tab":2}')]);
      check(results.every(result => result.ok && result.committed) && results.filter(result => result.value.exchanged).length === 1, 'exactly one genuine CAS winner');
      const actual = (await invoke(module, 'Export', { conversationId })).value[0];
      check(actual.revision === '2' && actual.json === results.find(result => result.value.exchanged).value.current.json, 'winning exact payload with one revision advance');
    });
    await run('model_settings_batch_is_atomic_versioned_and_profile_bound', async () => {
      const { databaseName, module } = create('settings');
      check((await invoke(module, 'SettingsExport')).value.version === 0, 'genuine absent settings version');
      const changes = await Promise.all(['models.permissions.v1', 'models.personalisation.v1', 'privacy.preferences.v1'].map(async key => ({ key, expected: null, replacement: '{"original":true}', sha256: await hash('{"original":true}') })));
      const saved = await invoke(module, 'SettingsCompareExchange', { expectedVersion: 0, changes });
      check(saved.ok && saved.committed && saved.value.exchanged && saved.value.rows.length === 3 && saved.value.version === 1, 'one original transaction writes all three keys and version');
      const stale = await invoke(module, 'SettingsCompareExchange', { expectedVersion: 0, changes }); check(stale.ok && !stale.value.exchanged, 'stale version refuses');
      const foreign = await invoke(module, 'SettingsExport', {}, b); check(foreign.value.rows.length === 0 && foreign.value.version === 0, 'no foreign settings/version leak');
      await module.dispose(); const reopened = createTaskExecutionModule({ databaseName }); modules.push(reopened);
      const actual = await invoke(reopened, 'SettingsExport'); check(actual.value.version === 1 && actual.value.rows.length === 3, 'all three original keys survive fresh owner');
    });
    await run('genuine_settings_transaction_abort_preserves_both_keys_and_version', async () => {
      const { databaseName, module } = create('abort'); await invoke(module, 'SettingsExport');
      const original = IDBDatabase.prototype.transaction; let aborted = false;
      try {
        IDBDatabase.prototype.transaction = function (...args) {
          const actual = original.apply(this, args);
          if (this.name === `${databaseName}-conversation-production` && args[1] === 'readwrite') queueMicrotask(() => { aborted = true; actual.abort(); });
          return actual;
        };
        const changes = [{ key: 'models.permissions.v1', expected: null, replacement: '{}', sha256: await hash('{}') }];
        const refused = await invoke(module, 'SettingsCompareExchange', { expectedVersion: 0, changes }); check(aborted && !refused.ok, 'actual abort event owns failure');
      } finally { IDBDatabase.prototype.transaction = original; }
      const read = await invoke(module, 'SettingsExport'); check(read.value.version === 0 && read.value.rows.length === 0, 'no partial settings/version effect');
      await closeFault(module);
    });
    await run('only_genuine_same_module_same_profile_file_selection_can_import_exact_bytes', async () => {
      const { databaseName, module } = create('file'); const attachmentId = id(), conversationId = id();
      const unknown = await invoke(module, 'ImportAttachment', { id: attachmentId, conversationId, token: 'browser-selection:' + id(), maximumBytes: 4096 });
      check(!unknown.ok, 'invented selection rejected');
      const selected = await invoke(module, 'SelectAttachment'); check(selected.ok && selected.value.name === 'original-proof.txt', 'genuine DOM input selection');
      const args = { id: attachmentId, conversationId, token: selected.value.token, maximumBytes: 4096 };
      check(!(await invoke(module, 'ImportAttachment', args, b)).ok, 'foreign profile cannot claim selected File');
      const stored = await invoke(module, 'ImportAttachment', args); check(stored.ok && stored.committed && stored.value.size === 22, 'actual bytes acknowledged');
      check(!(await invoke(module, 'ImportAttachment', { ...args, id: id() })).ok, 'consumed picker token cannot replay');
      const foreign = await invoke(module, 'ReadAttachment', { id: attachmentId }, b); check(foreign.ok && foreign.value === null, 'foreign byte read empty');
      await module.dispose(); const reopened = createTaskExecutionModule({ databaseName }); modules.push(reopened);
      const read = await invoke(reopened, 'ReadAttachment', { id: attachmentId }); check(atob(read.value.base64) === 'Original selected file', 'same original bytes survive new owner');
      check(read.value.sha256 === await hash('Original selected file'), 'genuine WebCrypto integrity');
      check((await invoke(reopened, 'DeleteAttachment', { id: attachmentId })).value.removed, 'actual byte removal');
      check((await invoke(reopened, 'ReadAttachment', { id: attachmentId })).value === null, 'absence after acknowledged delete');
    });
    await run('actual_picker_cancellation_and_close_join_without_fabricating_selection', async () => {
      const { module } = create('picker-cancel');
      const requestId = `cancel-picker-${++count}`;
      globalThis.__productionCancelPicker = true;
      const acquired = new Promise(resolve => {
        const observer = new MutationObserver(() => {
          if (document.querySelector('input[type=file]')) { observer.disconnect(); resolve(); }
        });
        observer.observe(document.body, { childList: true });
      });
      const original = module.invoke(requestId, 'ConversationProduction.SelectAttachment', JSON.stringify({ profileId: a }));
      await acquired; // The genuine input and cancellation owner now exist.
      module.cancel(requestId);
      const result = JSON.parse(await original); check(!result.ok && result.error.code === 'Cancelled', 'same original settles cancelled');
      await module.dispose();
    });
    await run('production_database_does_not_upgrade_task_schema3_or_replace_old_rows', async () => {
      const { databaseName, module } = create('rollback');
      const reply = JSON.parse(await module.invoke(`tasks-${++count}`, 'Conversation.Recent', JSON.stringify({ profileId: a, limit: 10 })));
      check(reply.ok, 'original task/conversation database opened');
      check((await cas(module, id(), null, '{"original":true}')).ok, 'separate production database written');
      await module.dispose();
      const version = await new Promise((resolve, reject) => { const open = indexedDB.open(databaseName); open.onerror = () => reject(open.error); open.onsuccess = () => { const value = open.result.version; open.result.close(); resolve(value); }; });
      check(version === 3, 'canonical task schema remains rollback-readable version3');
    });
    await run('corrupt_actual_production_hash_is_refused_and_preserved_for_recovery', async () => {
      const { databaseName, module } = create('corrupt'); const conversationId = id(); check((await cas(module, conversationId, null, '{}')).ok, 'original record');
      const original = await new Promise((resolve, reject) => { const request = indexedDB.open(`${databaseName}-conversation-production`); request.onerror = () => reject(request.error); request.onsuccess = () => resolve(request.result); });
      await new Promise((resolve, reject) => { const tx = original.transaction(['production'], 'readwrite'); const store = tx.objectStore('production'); const get = store.get([a, conversationId]); get.onsuccess = () => store.put({ ...get.result, json: '{"corrupt":true}' }); tx.oncomplete = resolve; tx.onabort = () => reject(tx.error); }); original.close();
      const refused = await invoke(module, 'Export', { conversationId }); check(!refused.ok, 'hash mismatch refuses no empty success');
      await closeFault(module);
    });
  } catch (error) { failure = error; }
  const errors = [];
  for (const module of modules) try { await module.dispose(); } catch (error) { errors.push(error); }
  for (const database of databases) for (const name of [database, `${database}-conversation-production`, `${database}-spaces`]) {
    try { await new Promise((resolve, reject) => { const request = indexedDB.deleteDatabase(name); request.onsuccess = resolve; request.onerror = () => reject(request.error); }); } catch (error) { errors.push(error); }
  }
  if (failure || errors.length) throw new AggregateError([...(failure ? [failure] : []), ...errors], 'Original production controls failed.');
  return { platform: 'actual Chromium IndexedDB, WebCrypto and selected File', acceptance: 'storage-only; synthetic profile observations are not authentication', results };
}
