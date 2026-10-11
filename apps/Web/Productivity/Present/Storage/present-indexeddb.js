// This-origin durable bytes for canonical Present documents. Semantic validation stays in C#.
export function createPresentModule({ databaseName = 'nine-to-one-present', indexedDB = globalThis.indexedDB,
  crypto = globalThis.crypto } = {}) {
  let opening, disposed = false, disposal;
  const pending = new Map();
  const failure = (code, extra = {}) => ({ ok: false, error: { code, ...extra } });
  const uuid = id => typeof id === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) && id !== '00000000-0000-0000-0000-000000000000';
  const revision = n => Number.isInteger(n) && n >= 0 && n <= 2147483647;
  const code = error => error?.name === 'QuotaExceededError' ? 'QuotaExceeded' : 'StorageUnavailable';
  let picking = false;
  const encodeBytes = bytes => {
    let value = '';
    for (let offset = 0; offset < bytes.length; offset += 8192) value += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
    return btoa(value);
  };
  function open() {
    if (disposed || !indexedDB) return Promise.reject(new Error('StorageUnavailable'));
    if (opening) return opening;
    opening = new Promise((resolve, reject) => {
      const request = indexedDB.open(databaseName, 1);
      let abandoned = false;
      request.onupgradeneeded = () => {
        request.result.createObjectStore('documents', { keyPath: 'id' });
        request.result.createObjectStore('quarantine', { keyPath: 'id' });
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
  async function hash(text) {
    if (!crypto?.subtle) throw new Error('CryptoUnavailable');
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
    return [...new Uint8Array(digest)].map(n => n.toString(16).padStart(2, '0')).join('');
  }
  function transact(db, mode, state, schedule) {
    return new Promise((resolve, reject) => {
      if (state.cancelled) { resolve(failure('Cancelled')); return; }
      const tx = db.transaction(mode === 'readwrite' ? ['documents', 'quarantine'] : ['documents'], mode, { durability: 'strict' });
      state.transaction = tx;
      let value, refused;
      tx.oncomplete = () => {
        state.transaction = null;
        state.committed = mode === 'readwrite';
        resolve({ ok: true, value, committed: state.committed, cancelledAfterCommit: state.cancelled && state.committed });
      };
      tx.onabort = () => { state.transaction = null; resolve(refused ?? failure(state.cancelled ? 'Cancelled' : code(tx.error))); };
      tx.onerror = () => {}; // IndexedDB aborts the complete transaction.
      try { schedule(tx, result => { value = result; }, result => { refused = result; tx.abort(); }); }
      catch (error) { try { tx.abort(); } catch {} reject(error); }
    });
  }
  async function execute(action, args, state) {
    if (!['List', 'Load', 'Save', 'Delete'].includes(action)) return failure('InvalidArgument');
    if (action !== 'List' && !uuid(args?.documentId)) return failure('InvalidArgument');
    if (action === 'List' || action === 'Load') {
      const earlier = [...pending.values()].filter(other => other !== state && ['Save', 'Delete'].includes(other.action) &&
        (action === 'List' || other.documentId === args.documentId));
      // Join actual already-admitted mutation work, including its hash/open interval.
      await Promise.all(earlier.map(other => other.finished));
      if (state.cancelled) return failure('Cancelled');
    }
    let candidate, expectedRecord;
    if (action === 'Save' || action === 'Delete') {
      if (args.expectedRecord !== null && typeof args.expectedRecord !== 'string') return failure('InvalidArgument');
      expectedRecord = JSON.parse(args.expectedRecord ?? 'null');
    }
    if (action === 'Save') {
      if (!revision(args.expectedVersion) || !revision(args.version) || args.version !== args.expectedVersion + 1 ||
          typeof args.documentJson !== 'string' || typeof args.receiptJson !== 'string' ||
          (args.expectedRecord !== null && typeof args.expectedRecord !== 'string') ||
          typeof args.recoveredFromBackup !== 'boolean') return failure('InvalidArgument');
      const document = JSON.parse(args.documentJson);
      if (document.id !== args.documentId || document.version !== args.version) return failure('InvalidArgument');
      const sha256 = await hash(args.documentJson);
      if (sha256 !== args.sha256) return failure('InvalidArgument');
      candidate = { json: args.documentJson, sha256, version: args.version };
    }
    const db = await open();
    if (state.cancelled) return failure('Cancelled');
    if (action === 'List') return transact(db, 'readonly', state, (tx, set) => {
      tx.objectStore('documents').getAll().onsuccess = event => set(event.target.result);
    });
    if (action === 'Load') return transact(db, 'readonly', state, (tx, set) => {
      tx.objectStore('documents').get(args.documentId).onsuccess = event => set(event.target.result ?? null);
    });
    return transact(db, 'readwrite', state, (tx, set, deny) => {
      const documents = tx.objectStore('documents');
      documents.get(args.documentId).onsuccess = event => {
        const actual = event.target.result ?? null;
        if (JSON.stringify(actual) !== JSON.stringify(expectedRecord)) {
          const expectedVersion = action === 'Delete'
            ? expectedRecord === null ? 0 : expectedRecord?.current?.version : args.expectedVersion;
          const actualVersion = actual === null ? 0 : actual?.current?.version;
          deny(revision(expectedVersion) && revision(actualVersion) ? failure('RevisionConflict', {
            documentId: args.documentId, expectedVersion, actualVersion }) : failure('StorageChanged'));
          return;
        }
        if (action === 'Delete') {
          if (actual) {
            tx.objectStore('quarantine').add({ id: crypto.randomUUID(), documentId: args.documentId,
              record: actual, reason: 'Deleted presentation', at: new Date().toISOString() });
            documents.delete(args.documentId);
          }
          set(null); return;
        }
        const base = args.recoveredFromBackup ? actual?.previous : actual?.current;
        if ((base?.version ?? 0) !== args.expectedVersion || args.recoveredFromBackup && !actual?.previous) {
          deny(failure('RevisionConflict', { documentId: args.documentId, expectedVersion: args.expectedVersion,
            actualVersion: revision(base?.version) ? base.version : 0 })); return;
        }
        if (args.recoveredFromBackup) tx.objectStore('quarantine').add({ id: crypto.randomUUID(), documentId: args.documentId,
          record: actual, reason: 'Preserved unreadable current before recovered save', at: new Date().toISOString() });
        documents.put({ id: args.documentId, current: candidate, previous: base ?? null });
        set(args.receiptJson); // Acknowledged only by this SAME transaction's oncomplete.
      };
    });
  }
  const module = {
    async invoke(requestId, action, json, cancelledAtAdmission = false) {
      if (typeof cancelledAtAdmission !== 'boolean') return JSON.stringify(failure('InvalidArgument'));
      if (cancelledAtAdmission) return JSON.stringify(failure('Cancelled'));
      if (disposed) return JSON.stringify(failure('StorageUnavailable'));
      if (typeof requestId !== 'string' || !requestId || pending.has(requestId)) return JSON.stringify(failure('InvalidArgument'));
      let finish;
      const state = { action, cancelled: false, transaction: null, committed: false,
        finished: new Promise(resolve => { finish = resolve; }) };
      pending.set(requestId, state);
      try {
        const args = JSON.parse(json); state.documentId = args?.documentId;
        const result = await execute(action, args, state);
        return JSON.stringify(state.cancelled && !result.committed ? failure('Cancelled') : result);
      } catch (error) {
        return JSON.stringify(failure(state.cancelled ? 'Cancelled' : error instanceof SyntaxError ? 'InvalidArgument' :
          error?.message === 'StorageBlocked' ? 'StorageBlocked' : code(error)));
      } finally { pending.delete(requestId); finish(); }
    },
    cancel(requestId) {
      const state = pending.get(requestId);
      if (!state) return;
      state.cancelled = true;
      if (state.transaction && !state.committed) { try { state.transaction.abort(); } catch {} }
    },
    // Device-local transfer for the Present surface itself: bytes leave as a
    // download and arrive through a picker. Canonical content stays in C#.
    async download(name, base64) {
      try {
        if (typeof name !== 'string' || !name || typeof base64 !== 'string' || !globalThis.document) return JSON.stringify(null);
        const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
        const url = URL.createObjectURL(new Blob([bytes],
          { type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation' }));
        const link = document.createElement('a'); link.href = url; link.download = name;
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        return JSON.stringify({ initiated: true });
      } catch { return JSON.stringify(null); }
    },
    async pick(accept) {
      if (picking || !globalThis.document) return JSON.stringify(null);
      picking = true;
      try {
        return await new Promise(resolve => {
          const input = document.createElement('input');
          input.type = 'file'; input.hidden = true;
          const kinds = String(accept || '').split(',').map(value => value.trim()).filter(Boolean);
          if (kinds.length) input.accept = kinds.join(',');
          document.body.append(input);
          let settled = false;
          const finish = value => {
            if (settled) return;
            settled = true; picking = false; input.remove(); resolve(JSON.stringify(value));
          };
          input.addEventListener('cancel', () => finish(null), { once: true });
          input.addEventListener('change', async () => {
            try {
              const file = input.files?.[0];
              if (!file || file.size > 64 * 1024 * 1024) return finish(null);
              finish({ name: file.name, base64: encodeBytes(new Uint8Array(await file.arrayBuffer())) });
            } catch { finish(null); }
          }, { once: true });
          input.click();
        });
      } catch { picking = false; return JSON.stringify(null); }
    },
    dispose() {
      if (disposal) return disposal;
      let resolve, reject;
      disposal = new Promise((yes, no) => { resolve = yes; reject = no; });
      disposed = true; // SAME drain exists before cancellation can call back.
      const originals = [...pending.values()];
      const errors = [];
      for (const [id] of pending) { try { module.cancel(id); } catch (error) { errors.push(error); } }
      Promise.allSettled(originals.map(state => state.finished)).then(async results => {
        for (const result of results) if (result.status === 'rejected') errors.push(result.reason);
        try { const db = await opening; db?.close(); } catch (error) { errors.push(error); }
        opening = null;
        if (errors.length) reject(new AggregateError(errors, 'Present storage close failed.')); else resolve();
      });
      return disposal;
    },
  };
  return module;
}
