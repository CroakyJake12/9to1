// Platform persistence for the canonical NotesDocument JSON; no document/identity/ACL model is defined here.
// Full document, NotesVersionInfo and NotesSaveResult JSON remain opaque to preserve exact long integers.
export function createNotesModule({ databaseName = 'nine-to-one-write', indexedDB = globalThis.indexedDB,
  crypto = globalThis.crypto } = {}) {
  let opening, disposed = false;
  const pending = new Map();
  const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
  const version = value => typeof value === 'string' && /^(0|[1-9][0-9]*)$/.test(value) && BigInt(value) <= 9223372036854775807n;
  const failure = (code, extra = {}) => ({ ok: false, error: { code, ...extra } });
  const errorCode = error => error?.name === 'QuotaExceededError' ? 'QuotaExceeded' : 'StorageUnavailable';

  function open() {
    if (disposed || !indexedDB) return Promise.reject(new Error('StorageUnavailable'));
    if (opening) return opening;
    opening = new Promise((resolve, reject) => {
      const request = indexedDB.open(databaseName, 1);
      let abandoned = false;
      request.onupgradeneeded = () => {
        const db = request.result;
        db.createObjectStore('documents', { keyPath: 'id' });
        const history = db.createObjectStore('history', { keyPath: ['documentId', 'versionId'] });
        history.createIndex('documentId', 'documentId');
        db.createObjectStore('trash', { keyPath: 'trashId' });
      };
      request.onblocked = () => { abandoned = true; reject(new Error('StorageBlocked')); };
      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const db = request.result;
        if (abandoned || disposed) { db.close(); reject(new Error('StorageUnavailable')); return; }
        db.onversionchange = () => { db.close(); opening = null; };
        resolve(db);
      };
    }).catch(error => { opening = null; throw error; });
    return opening;
  }

  async function hash(json) {
    if (!crypto?.subtle) throw new Error('CryptoUnavailable');
    const bytes = new TextEncoder().encode(json);
    const digest = await crypto.subtle.digest('SHA-256', bytes);
    return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
  }

  function transaction(db, stores, mode, state, schedule) {
    return new Promise((resolve, reject) => {
      if (state.cancelled) { resolve(failure('Cancelled')); return; }
      const tx = db.transaction(stores, mode, { durability: 'strict' });
      state.transaction = tx;
      let value, denied;
      tx.oncomplete = () => {
        state.committed = mode === 'readwrite';
        state.transaction = null;
        resolve({ ok: true, value, committed: state.committed, cancelledAfterCommit: state.cancelled && state.committed });
      };
      tx.onabort = () => { state.transaction = null; resolve(denied ?? failure(state.cancelled ? 'Cancelled' : errorCode(tx.error))); };
      tx.onerror = () => {}; // Default IndexedDB handling aborts the whole transaction, including history.
      const set = result => { value = result; };
      const deny = result => { denied = result; tx.abort(); };
      try { schedule(tx, set, deny); } catch (error) { try { tx.abort(); } catch {} reject(error); }
    });
  }

  async function execute(action, args, state) {
    if (!['List', 'Load', 'Versions', 'LoadVersion', 'Save', 'Delete'].includes(action)) return failure('InvalidArgument');
    const id = args?.documentId;
    if (!['List', 'Save'].includes(action) && !uuid(id)) return failure('InvalidArgument');
    if (['List', 'Load', 'Versions', 'LoadVersion'].includes(action)) {
      // Durable inspection must not mistake the pre-transaction hash/open phase of an earlier own mutation for an abort.
      const earlier = [...pending.values()].filter(item => item !== state && ['Save', 'Delete'].includes(item.action) &&
        (action === 'List' || item.documentId === id));
      if (earlier.length) await Promise.race([Promise.all(earlier.map(item => item.finished)), state.aborted]);
      if (state.cancelled) return failure('Cancelled');
    }
    let payload, expectedRecordFingerprint, expectedHistoryFingerprint;
    if (action === 'Save') {
      if (!uuid(id) || !version(args.expectedVersion) || !version(args.version) ||
          BigInt(args.version) !== BigInt(args.expectedVersion) + 1n || typeof args.documentJson !== 'string' ||
          typeof args.versionInfoJson !== 'string' || typeof args.receiptJson !== 'string' ||
          typeof args.versionId !== 'string' || !/^v[0-9]+-[0-9]{8}-[0-9]{9}$/.test(args.versionId) ||
          (args.expectedRecord !== null && typeof args.expectedRecord !== 'string')) return failure('InvalidArgument');
      // Normalize only outer transport records. Canonical document/metadata JSON stays opaque and exact.
      expectedRecordFingerprint = JSON.stringify(JSON.parse(args.expectedRecord ?? 'null'));
      if (typeof args.expectedHistoryRecords === 'string') expectedHistoryFingerprint = JSON.stringify(JSON.parse(args.expectedHistoryRecords));
      // Only transport index integrity is checked here. Canonical semantic validation belongs to NotesDocumentValidator in C#.
      const document = JSON.parse(args.documentJson);
      if (document.id !== id) return failure('InvalidArgument');
      payload = { id, version: args.version, documentJson: args.documentJson, sha256: await hash(args.documentJson),
        versionInfoJson: args.versionInfoJson };
      if (payload.sha256 !== args.sha256) return failure('InvalidArgument');
    }
    const db = await open();
    if (state.cancelled) return failure('Cancelled');
    if (action === 'List') return transaction(db, ['documents', 'history'], 'readonly', state, (tx, set) => {
      let documents, history;
      const publish = () => { if (documents && history) set({ documents, history }); };
      tx.objectStore('documents').getAll().onsuccess = event => { documents = event.target.result; publish(); };
      tx.objectStore('history').getAll().onsuccess = event => { history = event.target.result; publish(); };
    });
    if (action === 'Load') return transaction(db, ['documents'], 'readonly', state, (tx, set) => {
      tx.objectStore('documents').get(id).onsuccess = event => set(event.target.result ?? null);
    });
    if (action === 'Versions') return transaction(db, ['history'], 'readonly', state, (tx, set) => {
      tx.objectStore('history').index('documentId').getAll(id).onsuccess = event => set(event.target.result);
    });
    if (action === 'LoadVersion') {
      if (typeof args.versionId !== 'string' || !/^v[0-9]+-[0-9]{8}-[0-9]{9}$/.test(args.versionId)) return failure('InvalidArgument');
      return transaction(db, ['history'], 'readonly', state, (tx, set) => {
        tx.objectStore('history').get([id, args.versionId]).onsuccess = event => set(event.target.result ?? null);
      });
    }
    if (action === 'Save') return transaction(db, ['documents', 'history', 'trash'], 'readwrite', state, (tx, set, deny) => {
      const documents = tx.objectStore('documents');
      documents.get(id).onsuccess = event => {
        const current = event.target.result;
        const actualVersion = current?.version ?? '0';
        const sameRecord = JSON.stringify(current ?? null) === expectedRecordFingerprint;
        // Canonical recovery version is validated by C#; exact original and history fingerprints fence admission.
        const recovery = args.preserveCorruptCurrent === true;
        if (!sameRecord || (!recovery && current && actualVersion !== args.expectedVersion)) {
          deny(version(actualVersion) ? failure('RevisionConflict', { documentId: id, expectedVersion: args.expectedVersion, actualVersion }) : failure('CorruptStorage')); return;
        }
        if (recovery && !current || !recovery && current && !version(actualVersion)) { deny(failure('CorruptStorage')); return; }
        const publish = () => {
          if (recovery) tx.objectStore('trash').add({ trashId: crypto.randomUUID(), documentId: id,
            deletedAt: new Date().toISOString(), current, history: [], reason: 'Preserved corrupt original before validated recovery save' });
          documents.put(payload);
          tx.objectStore('history').add({ documentId: id, versionId: args.versionId, ...payload });
          // Only transaction completion acknowledges this value.
          set(args.receiptJson);
        };
        if (recovery || !current) {
          if (typeof args.expectedHistoryRecords !== 'string') { deny(failure('InvalidArgument')); return; }
          tx.objectStore('history').index('documentId').getAll(id).onsuccess = historyEvent => {
            if (JSON.stringify(historyEvent.target.result) !== expectedHistoryFingerprint) { deny(failure('StorageChanged')); return; }
            publish();
          };
        } else publish();
      };
    });
    if (action === 'Delete') return transaction(db, ['documents', 'history', 'trash'], 'readwrite', state, (tx, set) => {
      const documents = tx.objectStore('documents');
      documents.get(id).onsuccess = currentEvent => {
        const current = currentEvent.target.result;
        tx.objectStore('history').index('documentId').getAll(id).onsuccess = historyEvent => {
          const history = historyEvent.target.result;
          if (current || history.length) {
            tx.objectStore('trash').add({ trashId: crypto.randomUUID(), documentId: id, deletedAt: new Date().toISOString(), current: current ?? null, history });
            documents.delete(id);
            for (const record of history) tx.objectStore('history').delete([id, record.versionId]);
          }
          set(null);
        };
      };
    });
    return failure('InvalidArgument');
  }

  return {
    async invoke(requestId, action, argumentsJson) {
      if (disposed) return JSON.stringify(failure('StorageUnavailable'));
      if (typeof requestId !== 'string' || !requestId || pending.has(requestId)) return JSON.stringify(failure('InvalidArgument'));
      let finish, abort;
      const finished = new Promise(resolve => { finish = resolve; });
      const aborted = new Promise(resolve => { abort = resolve; });
      const state = { cancelled: false, transaction: null, committed: false, finished, finish, aborted, abort };
      pending.set(requestId, state);
      try {
        const args = JSON.parse(argumentsJson);
        state.action = action; state.documentId = args?.documentId;
        const result = await execute(action, args, state);
        if (state.cancelled && !result.committed) return JSON.stringify(failure('Cancelled'));
        return JSON.stringify(result);
      } catch (error) {
        return JSON.stringify(failure(state.cancelled ? 'Cancelled' : error instanceof SyntaxError ? 'InvalidArgument' : error?.message === 'StorageBlocked' ? 'StorageBlocked' : errorCode(error)));
      } finally { pending.delete(requestId); state.finish(); }
    },
    cancel(requestId) {
      const state = pending.get(requestId);
      if (!state) return;
      state.cancelled = true; state.abort();
      if (state.transaction && !state.committed) { try { state.transaction.abort(); } catch {} }
    },
    async close() { const db = await opening?.catch(() => null); db?.close(); opening = null; },
    async dispose() { disposed = true; for (const id of pending.keys()) this.cancel(id); await this.close(); },
  };
}
