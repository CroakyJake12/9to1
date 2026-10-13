// Owned by the SAME authenticated Task storage module. A separate schema preserves
// the deployed schema3 database and immediate old-build rollback. No remote sync.
export function createConversationProductionStorage({ indexedDB, databaseName, hash, transact, refuse, profile, uuid, revision,
  isSealed, retainOwnerError, crypto = globalThis.crypto }) {
  let opening;
  const selected = new Map();
  function open() {
    if (!indexedDB || isSealed()) return Promise.reject(new Error('StorageUnavailable'));
    if (opening) return opening;
    const actual = new Promise((resolve, reject) => {
      const request = indexedDB.open(`${databaseName}-conversation-production`, 1);
      let acquired, blocked = false;
      const errors = [];
      request.onupgradeneeded = event => {
        try {
          acquired = request.result;
          if (event.oldVersion !== 0) throw new Error('UnsupportedConversationProductionMigration');
          const records = acquired.createObjectStore('production', { keyPath: ['profileId', 'conversationId'] });
          records.createIndex('profileId', 'profileId');
          const blobs = acquired.createObjectStore('attachments', { keyPath: ['profileId', 'id'] });
          blobs.createIndex('profileId', 'profileId');
          const settings = acquired.createObjectStore('settings', { keyPath: ['profileId', 'key'] });
          settings.createIndex('profileId', 'profileId');
          acquired.createObjectStore('settingsState', { keyPath: 'profileId' });
        } catch (error) { errors.push(error); try { request.transaction.abort(); } catch (abort) { errors.push(abort); } }
      };
      request.onblocked = () => { blocked = true; };
      request.onerror = () => {
        if (request.error) errors.push(request.error);
        if (acquired) try { acquired.close(); } catch (error) { errors.push(error); }
        errors.forEach(retainOwnerError); reject(new AggregateError(errors, 'Original conversation production open failed.'));
      };
      request.onsuccess = () => {
        const db = acquired = request.result;
        if (blocked || isSealed() || errors.length) {
          try { db.close(); } catch (error) { errors.push(error); }
          errors.forEach(retainOwnerError);
          reject(errors.length ? new AggregateError(errors, 'Original production open failed.') : new Error(blocked ? 'StorageBlocked' : 'StorageUnavailable')); return;
        }
        db.onversionchange = () => { try { db.close(); } catch (error) { retainOwnerError(error); return; } if (opening === actual) opening = null; };
        resolve(db);
      };
    });
    opening = actual; actual.catch(() => { if (opening === actual) opening = null; }); return actual;
  }
  async function validate(row, owner) {
    if (!row || row.profileId !== owner || !uuid(row.conversationId) || typeof row.json !== 'string' || row.json.length > 16 * 1024 * 1024 ||
        !revision(row.revision) || row.revision === '0' || await hash(row.json) !== row.sha256) throw new Error('InvalidCanonicalProductionRecord');
  }
  function choose(owner, original) {
    if (typeof document === 'undefined') return Promise.resolve(refuse('FilePickerUnavailable'));
    return new Promise((resolve, reject) => {
      const input = document.createElement('input'); input.type = 'file'; input.multiple = false;
      input.style.display = 'none'; document.body.append(input);
      let terminal = false;
      const finish = reply => {
        if (terminal) return; terminal = true;
        input.removeEventListener('change', change); input.removeEventListener('cancel', cancel); input.remove();
        original.cancelSource = null; resolve(reply);
      };
      const cancel = () => finish(refuse('Cancelled'));
      const change = () => {
        const file = input.files?.[0];
        if (!file) { cancel(); return; }
        if (selected.size >= 64) { finish(refuse('SelectionCapacity')); return; }
        const token = `browser-selection:${crypto.randomUUID()}`;
        selected.set(token, { file, owner });
        finish({ ok: true, committed: false, value: { token, name: file.name, size: file.size, mediaType: file.type } });
      };
      original.cancelSource = cancel;
      input.addEventListener('change', change); input.addEventListener('cancel', cancel);
      if (original.cancelled) { cancel(); return; }
      try { input.click(); } catch (error) { input.remove(); original.cancelSource = null; reject(error); }
    });
  }
  return {
    async execute(action, args, original) {
      if (!profile(args?.profileId)) return refuse('ActorUnavailable');
      const owner = args.profileId;
      if (action === 'ConversationProduction.SelectAttachment') return choose(owner, original);
      if (original.cancelled) return refuse('Cancelled');
      const db = await open();
      if (original.cancelled) return refuse('Cancelled');
      if (action === 'ConversationProduction.SettingsExport') {
        const result = await transact(db, false, original, (stores, set, _deny, guard) => {
          stores.settingsState.get(owner).onsuccess = guard(e => {
            const version = e.target.result?.version ?? 0;
            if (!Number.isSafeInteger(version) || version < 0 || version > 2147483647) throw new Error('InvalidSettingsVersion');
            stores.settings.index('profileId').getAll(owner).onsuccess = guard(e => set({ profileId: owner, version, rows: e.target.result }));
          });
        }, ['settings', 'settingsState']);
        if (result.ok) for (const row of result.value.rows) {
          if (row.profileId !== owner || !['models.permissions.v1', 'models.personalisation.v1', 'privacy.preferences.v1'].includes(row.key) ||
              typeof row.json !== 'string' || await hash(row.json) !== row.sha256) throw new Error('InvalidCanonicalModelSettings');
        }
        return result;
      }
      if (action === 'ConversationProduction.SettingsCompareExchange') {
        const keys = ['models.permissions.v1', 'models.personalisation.v1', 'privacy.preferences.v1'];
        if (!Number.isSafeInteger(args.expectedVersion) || args.expectedVersion < 0 || !Array.isArray(args.changes) || args.changes.length > 3 ||
            new Set(args.changes.map(change => change.key)).size !== args.changes.length) return refuse('InvalidArgument');
        for (const change of args.changes) {
          if (!keys.includes(change.key) || change.expected != null && typeof change.expected !== 'string' ||
              change.replacement != null && (typeof change.replacement !== 'string' || change.replacement.length > 1024 * 1024 ||
              await hash(change.replacement) !== change.sha256)) return refuse('InvalidArgument');
          if (change.replacement != null) JSON.parse(change.replacement);
        }
        return transact(db, true, original, (stores, set, _deny, guard) => {
          stores.settingsState.get(owner).onsuccess = guard(e => {
            const version = e.target.result?.version ?? 0;
            if (!Number.isSafeInteger(version) || version < 0 || version >= 2147483647) throw new Error('SettingsRevisionExhausted');
            if (version !== args.expectedVersion) { set({ exchanged: false, profileId: owner, version }); return; }
            stores.settings.index('profileId').getAll(owner).onsuccess = guard(e => {
              const before = new Map(e.target.result.map(row => [row.key, row]));
              if (args.changes.some(change => (before.get(change.key)?.json ?? null) !== change.expected)) {
                set({ exchanged: false, profileId: owner, version }); return;
              }
              const next = version + 1;
              for (const change of args.changes) {
                if (change.replacement == null) { stores.settings.delete([owner, change.key]); before.delete(change.key); }
                else {
                  const row = { profileId: owner, key: change.key, json: change.replacement, sha256: change.sha256, revision: String(next) };
                  stores.settings.put(row); before.set(change.key, row);
                }
              }
              stores.settingsState.put({ profileId: owner, version: next });
              set({ exchanged: true, profileId: owner, version: next, rows: [...before.values()] });
            });
          });
        }, ['settings', 'settingsState']);
      }
      if (action === 'ConversationProduction.Export') {
        if (args.conversationId != null && !uuid(args.conversationId)) return refuse('InvalidArgument');
        const reply = await transact(db, false, original, (store, set, _deny, guard) => {
          if (args.conversationId) store.get([owner, args.conversationId]).onsuccess = guard(e => set(e.target.result ? [e.target.result] : []));
          else store.index('profileId').getAll(owner, 1025).onsuccess = guard(e => set(e.target.result));
        }, ['production']);
        if (!reply.ok) return reply;
        if (reply.value.length > 1024) return refuse('ProductionInventoryTooLarge');
        for (const row of reply.value) await validate(row, owner);
        return reply;
      }
      if (action === 'ConversationProduction.CompareExchange') {
        if (!uuid(args.conversationId) || args.expected != null && typeof args.expected !== 'string' ||
            typeof args.replacement !== 'string' || args.replacement.length > 16 * 1024 * 1024 ||
            await hash(args.replacement) !== args.sha256) return refuse('InvalidArgument');
        JSON.parse(args.replacement);
        if (original.cancelled) return refuse('Cancelled');
        return transact(db, true, original, (store, set, _deny, guard) => {
          store.get([owner, args.conversationId]).onsuccess = guard(e => {
            const before = e.target.result ?? null;
            if ((before?.json ?? null) !== args.expected) { set({ exchanged: false, current: before }); return; }
            const next = (BigInt(before?.revision ?? '0') + 1n).toString();
            if (!revision(next)) throw new Error('ProductionRevisionExhausted');
            const row = { profileId: owner, conversationId: args.conversationId, json: args.replacement, sha256: args.sha256, revision: next };
            store.put(row); set({ exchanged: true, current: row });
          });
        }, ['production']);
      }
      if (action === 'ConversationProduction.ImportAttachment') {
        const selection = selected.get(args.token);
        if (!selection || selection.owner !== owner || !uuid(args.id) || !uuid(args.conversationId) ||
            !Number.isSafeInteger(args.maximumBytes) || args.maximumBytes < 1 || args.maximumBytes > 50 * 1024 * 1024)
          return refuse('OriginalFileSelectionUnavailable');
        const file = selection.file;
        if (file.size > args.maximumBytes) return refuse('AttachmentTooLarge');
        const bytes = new Uint8Array(await file.arrayBuffer());
        if (original.cancelled) return refuse('Cancelled');
        const digest = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map(byte => byte.toString(16).padStart(2, '0')).join('');
        const metadata = { profileId: owner, id: args.id, conversationId: args.conversationId, name: file.name, size: bytes.byteLength,
          mediaType: file.type || 'application/octet-stream', sha256: digest };
        const result = await transact(db, true, original, (store, set, deny, guard) => {
          store.get([owner, args.id]).onsuccess = guard(e => {
            if (e.target.result) { deny(refuse('AttachmentIdentityExists')); return; }
            store.put({ ...metadata, bytes }); set(metadata);
          });
        }, ['attachments']);
        if (result.ok && result.committed) selected.delete(args.token);
        return result;
      }
      if (action === 'ConversationProduction.ReadAttachment' || action === 'ConversationProduction.DeleteAttachment') {
        if (!uuid(args.id)) return refuse('InvalidArgument');
        const deleting = action.endsWith('DeleteAttachment');
        const result = await transact(db, deleting, original, (store, set, _deny, guard) => {
          store.get([owner, args.id]).onsuccess = guard(e => {
            const row = e.target.result ?? null;
            if (deleting) { if (row) store.delete([owner, args.id]); set({ removed: !!row }); }
            else set(row);
          });
        }, ['attachments']);
        if (!result.ok || deleting || result.value == null) return result;
        const row = result.value;
        if (row.profileId !== owner || row.id !== args.id || !(row.bytes instanceof Uint8Array) || row.bytes.byteLength !== row.size)
          throw new Error('InvalidOriginalAttachmentBytes');
        const digest = [...new Uint8Array(await crypto.subtle.digest('SHA-256', row.bytes))].map(byte => byte.toString(16).padStart(2, '0')).join('');
        if (digest !== row.sha256) throw new Error('OriginalAttachmentHashChanged');
        let binary = ''; for (let index = 0; index < row.bytes.length; index += 8192) binary += String.fromCharCode(...row.bytes.subarray(index, index + 8192));
        const { bytes, ...metadata } = row;
        return { ...result, value: { ...metadata, base64: btoa(binary) } };
      }
      return refuse('InvalidArgument');
    },
    async close() { selected.clear(); const actual = opening; if (actual) { const db = await actual; db.close(); } opening = null; }
  };
}
