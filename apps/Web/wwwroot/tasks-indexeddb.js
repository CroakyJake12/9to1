// Platform storage for the canonical TaskExecutionSnapshot, not another task model or grant.
export function createTaskExecutionModule({ databaseName = 'nine-to-one-task-execution',
  indexedDB = globalThis.indexedDB, crypto = globalThis.crypto } = {}) {
  let opening, sealed = false, close;
  const originals = new Map();
  const failedOriginals = [], ownerErrors = [];
  const maximumRetainedOriginals = 256; // Finite actual frame custody, never prune a failed frame.
  const add = (errors, error) => { if (!errors.some(original => original === error)) errors.push(error); };
  const retain = (original, error) => add(original.errors, error);
  const refuse = (code, values = {}) => ({ ok: false, error: { code, ...values } });
  const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(value)
    && value !== '00000000-0000-0000-0000-000000000000';
  // Decimal strings preserve the SAME signed Int64 CAS contract beyond JS safe-integer range.
  const revision = value => typeof value === 'string' && /^(0|[1-9][0-9]*)$/.test(value) && BigInt(value) <= 9223372036854775807n;
  const storageCode = error => error?.name === 'QuotaExceededError' ? 'QuotaExceeded' : 'StorageUnavailable';
  function open() {
    if (!indexedDB || sealed) return Promise.reject(new Error('StorageUnavailable'));
    if (opening) return opening;
    // onblocked is an observation, not a terminal IDBOpenDBRequest event. Keep the SAME
    // request/Promise owned until its actual success/error and any acquired handle cleanup.
    const actual = new Promise((resolve, reject) => {
      const request = indexedDB.open(databaseName, 3);
      let blocked = false, acquired;
      const errors = [];
      request.onupgradeneeded = event => {
        try {
          acquired = request.result; // Retain actual partial upgrade handle before any callback.
          const oldVersion = event.oldVersion;
          const priorNames = [...acquired.objectStoreNames];
          const copy = (name, backup, wrap) => {
            const source = request.transaction.objectStore(name);
            let expected, copied = 0;
            const fail = error => { add(errors, error); try { request.transaction.abort(); } catch (abort) { add(errors, abort); } };
            const observeError = actual => { actual.onerror = () => { if (actual.error) add(errors, actual.error); }; return actual; };
            observeError(source.count()).onsuccess = e => { try { expected = e.target.result; } catch (error) { fail(error); } };
            observeError(source.openCursor()).onsuccess = e => {
              try {
                const cursor = e.target.result;
                if (cursor) {
                  const value = cursor.value, key = cursor.primaryKey;
                  observeError(backup.put(wrap ? { store: name, key, value } : value));
                  copied++; cursor.continue(); return;
                }
                const count = observeError(wrap ? backup.index('store').count(name) : backup.count());
                count.onsuccess = result => {
                  try { if (expected !== copied || result.target.result !== copied) throw new Error('AtomicMigrationBackupIntegrityFailed'); }
                  catch (error) { fail(error); }
                };
              } catch (error) { fail(error); }
            };
          };
          if (oldVersion > 0 && !priorNames.includes('tasks')) throw new Error('MissingPriorCanonicalTaskStore');
          if (oldVersion === 2) {
            for (const name of ['tasks', 'conversations', 'conversationMessages', 'conversationContexts', 'taskProfiles'])
              if (!priorNames.includes(name)) throw new Error('MissingPriorCanonicalProfileStore');
            const backup = acquired.createObjectStore('schema2BackupV2', { keyPath: ['store', 'key'] });
            backup.createIndex('store', 'store');
            // Every actual prior store, including the complete schema1 backup and any
            // unknown prior store, is copied in this SAME atomic versionchange transaction.
            for (const name of priorNames) copy(name, backup, true);
          }
          if (oldVersion === 0) {
            const tasks = acquired.createObjectStore('tasks', { keyPath: 'taskId' });
            tasks.createIndex('contextId', 'contextId');
          } else if (oldVersion === 1) {
            const backup = acquired.createObjectStore('legacyTaskBackupV1', { keyPath: 'taskId' });
            copy('tasks', backup, false); // Original rows/IDs/revisions/opaque JSON stay unchanged.
          }
          if (oldVersion < 2) {
            for (const name of ['conversations', 'conversationMessages', 'conversationContexts']) {
              const store = acquired.createObjectStore(name, { keyPath: ['profileId', 'id'] });
              store.createIndex('profileId', 'profileId');
              store.createIndex(name === 'conversations' ? 'profileSpace' : 'profileConversation',
                name === 'conversations' ? ['profileId', 'spaceId'] : ['profileId', 'conversationId']);
            }
            acquired.createObjectStore('taskProfiles', { keyPath: 'taskId' });
          }
          const events = acquired.createObjectStore('executionEvents', { keyPath: ['profileId', 'eventId'] });
          events.createIndex('profileId', 'profileId');
          events.createIndex('profileExecution', ['profileId', 'executionId']);
          events.createIndex('profileSequence', ['profileId', 'sequenceSort'], { unique: true });
          acquired.createObjectStore('executionEventSequences', { keyPath: 'profileId' });
        } catch (error) {
          add(errors, error);
          try { request.transaction.abort(); } catch (abort) { add(errors, abort); }
          // Backup/schema failures never reset the database or claim a terminal request.
        }
      };
      request.onblocked = () => { blocked = true; };
      request.onerror = () => {
        if (request.error) add(errors, request.error);
        if (acquired) try { acquired.close(); } catch (error) { add(errors, error); }
        for (const error of errors) add(ownerErrors, error);
        reject(errors.length === 1 ? errors[0] : new AggregateError(errors, 'Original task database open failed.'));
      };
      request.onsuccess = () => {
        const db = acquired = request.result;
        if (blocked || sealed || errors.length) {
          try { db.close(); } catch (error) { add(errors, error); }
          for (const error of errors) add(ownerErrors, error);
          reject(errors.length ? new AggregateError(errors, 'Original task database open/cleanup failed.')
            : new Error(blocked ? 'StorageBlocked' : 'StorageUnavailable'));
          return;
        }
        db.onversionchange = () => {
          try { db.close(); } catch (error) { add(ownerErrors, error); return; }
          if (opening === actual) opening = null;
        };
        resolve(db);
      };
    });
    opening = actual;
    // Do not replace/drop a pending request on a nonterminal notification.
    actual.catch(() => { if (opening === actual) opening = null; });
    return actual;
  }
  async function hash(text) {
    if (!crypto?.subtle) throw new Error('CryptoUnavailable');
    const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
    return [...new Uint8Array(bytes)].map(value => value.toString(16).padStart(2, '0')).join('');
  }
  function transact(db, write, original, schedule, storeNames = ['tasks']) {
    return new Promise(resolve => {
      if (original.cancelled) { resolve(refuse('Cancelled')); return; }
      const tx = db.transaction(storeNames, write ? 'readwrite' : 'readonly', { durability: 'strict' });
      original.transaction = tx;
      let value, rejection;
      const failed = () => ({ ...refuse(storageCode(original.errors[0])), committed: original.committed,
        value: original.completionObservation }); // Unvalidated observation, never a successful CAS receipt.
      tx.oncomplete = () => {
        original.transaction = null;
        original.committed = write;
        original.completionObservation = value;
        resolve(original.errors.length ? failed() : { ok: true, value, committed: write });
      };
      tx.onabort = () => {
        original.transaction = null;
        if (tx.error && !original.cancelled && !rejection) retain(original, tx.error);
        resolve(original.errors.length ? failed() : rejection ?? refuse(original.cancelled ? 'Cancelled' : storageCode(tx.error)));
      };
      tx.onerror = () => {}; // Actual terminal onabort/oncomplete is the only settlement witness.
      const stop = () => { try { tx.abort(); } catch (abort) { retain(original, abort); } };
      const fail = error => { retain(original, error); stop(); };
      const guard = callback => event => {
        try { callback(event); } catch (error) { fail(error); }
        // A scheduling/callback/abort failure never settles before the SAME transaction event.
      };
      const deny = result => { rejection = result; stop(); };
      try { schedule(storeNames.length === 1 ? tx.objectStore(storeNames[0])
        : Object.fromEntries(storeNames.map(name => [name, tx.objectStore(name)])), result => { value = result; }, deny, guard); }
      catch (error) { fail(error); }
    });
  }
  // Fixed platform operations over the SAME canonical Conversation/ChatMessage/context models.
  // profileId is supplied by the managed verified-actor boundary, never a ContextId or grant.
  const profile = value => typeof value === 'string' && /^cake-account-profile:[0-9a-f]{64}:[0-9a-f-]{36}$/.test(value)
    && uuid(value.slice(value.lastIndexOf(':') + 1));
  const conversationStores = ['conversations', 'conversationMessages', 'conversationContexts'];
  const conversationActions = new Set(['Conversation.Get', 'Conversation.Recent', 'Conversation.Archived',
    'Conversation.BySpace', 'Conversation.Upsert', 'Conversation.Delete', 'Conversation.DetachSpace',
    'Conversation.Messages', 'Conversation.PutMessage', 'Conversation.DeleteMessage', 'Conversation.CompactMessages',
    'Conversation.Context', 'Conversation.PutContext', 'Conversation.DeleteContext']);
  const conversationWrites = new Set(['Conversation.Upsert', 'Conversation.Delete', 'Conversation.DetachSpace',
    'Conversation.PutMessage', 'Conversation.DeleteMessage', 'Conversation.CompactMessages',
    'Conversation.PutContext', 'Conversation.DeleteContext']);
  const entityStore = kind => kind === 'conversation' ? 'conversations' : kind === 'message' ? 'conversationMessages' : 'conversationContexts';
  const byUpdated = (a, b) => b.updatedAt.localeCompare(a.updatedAt) || a.id.localeCompare(b.id);
  const byCreated = (a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id);
  async function validateConversationRow(row, expectedProfile, expectedKind) {
    if (!['conversation', 'message', 'context'].includes(expectedKind) || !row || row.profileId !== expectedProfile || row.entity !== expectedKind || !uuid(row.id) ||
        !revision(row.revision) || row.revision === '0' || typeof row.json !== 'string' ||
        !/^[0-9a-f]{64}$/.test(row.sha256 ?? '') || await hash(row.json) !== row.sha256)
      throw new Error('InvalidCanonicalConversationRecord');
    const payload = JSON.parse(row.json);
    if (payload.id !== row.id || payload.createdAt !== row.createdAt ||
        (expectedKind === 'conversation' ? payload.updatedAt !== row.updatedAt || payload.mode !== row.mode ||
          payload.kind !== row.kind || (payload.spaceId ?? null) !== row.spaceId ||
          payload.isTemporary !== row.isTemporary || payload.isArchived !== row.isArchived :
          payload.conversationId !== row.conversationId || !uuid(row.conversationId) ||
          expectedKind === 'message' && payload.isCompacted !== row.isCompacted ||
          expectedKind === 'context' && payload.kind !== row.kind))
      throw new Error('InvalidCanonicalConversationMetadata');
    return payload;
  }
  async function executeConversation(action, args, original) {
    if (!profile(args?.profileId)) return refuse('ActorUnavailable');
    const owner = args.profileId;
    // Inspect actual already-admitted mutations before claiming absence. Capture only this
    // request's earlier insertion cohort, avoiding cycles with later source admissions.
    const earlier = [];
    for (const other of originals.values()) {
      if (other === original) break;
      if (other.profileId === owner && conversationWrites.has(other.action)) earlier.push(other.settled);
    }
    await Promise.all(earlier);
    if (original.cancelled) return refuse('Cancelled');
    const isWrite = conversationWrites.has(action);
    if (action === 'Conversation.Upsert' || action === 'Conversation.PutMessage' || action === 'Conversation.PutContext') {
      const kind = action === 'Conversation.Upsert' ? 'conversation' : action === 'Conversation.PutMessage' ? 'message' : 'context';
      if (!args.row || args.row.profileId !== owner || args.row.entity !== kind || !uuid(args.row.id) ||
          typeof args.row.json !== 'string' || !/^[0-9a-f]{64}$/.test(args.row.sha256 ?? '')) return refuse('InvalidArgument');
      // Candidate revision is metadata only. The actual next revision is selected from a real read below.
      await validateConversationRow({ ...args.row, revision: '1' }, owner, kind);
    } else if (['Conversation.Get', 'Conversation.Delete'].includes(action) && !uuid(args.id) ||
        ['Conversation.BySpace', 'Conversation.DetachSpace'].includes(action) && !uuid(args.spaceId) ||
        ['Conversation.Messages', 'Conversation.Context', 'Conversation.DeleteMessage', 'Conversation.CompactMessages',
          'Conversation.DeleteContext'].includes(action) && !uuid(args.conversationId)) return refuse('InvalidArgument');
    if (['Conversation.DeleteMessage', 'Conversation.DeleteContext'].includes(action) && !uuid(args.id)) return refuse('InvalidArgument');
    if (args.expectedRevision != null && !revision(args.expectedRevision)) return refuse('InvalidArgument');
    if (original.cancelled) return refuse('Cancelled');
    const db = await open();
    if (original.cancelled) return refuse('Cancelled');
    const prior = await transact(db, false, original, (stores, set, _deny, guard) => {
      const conversations = stores.conversations;
      const get = (store, key, done) => store.get(key).onsuccess = guard(e => done(e.target.result ?? null));
      const all = (store, index, key, done) => store.index(index).getAll(key).onsuccess = guard(e => done(e.target.result));
      if (action === 'Conversation.Get' || action === 'Conversation.Delete')
        get(conversations, [owner, args.id], value => set({ rows: value ? [value] : [] }));
      else if (action === 'Conversation.Upsert')
        get(conversations, [owner, args.row.id], value => set({ rows: value ? [value] : [] }));
      else if (['Conversation.Recent', 'Conversation.Archived'].includes(action))
        all(conversations, 'profileId', owner, rows => set({ rows: rows.filter(row => !row.isTemporary &&
          row.isArchived === (action === 'Conversation.Archived') && (args.mode == null || row.mode === args.mode)).sort(byUpdated) }));
      else if (['Conversation.BySpace', 'Conversation.DetachSpace'].includes(action))
        all(conversations, 'profileSpace', [owner, args.spaceId], rows => set({ rows: rows.filter(row =>
          action === 'Conversation.DetachSpace' || !row.isTemporary && !row.isArchived).sort(byUpdated) }));
      else {
        const conversationId = args.row?.conversationId ?? args.conversationId;
        get(conversations, [owner, conversationId], parent => {
          if (!parent) { set({ rows: [], parent: null }); return; }
          const isMessage = action.includes('Message');
          const store = stores[isMessage ? 'conversationMessages' : 'conversationContexts'];
          if (args.row) get(store, [owner, args.row.id], value => set({ rows: value ? [value] : [], parent }));
          else if (args.id) get(store, [owner, args.id], value => set({ rows: value ? [value] : [], parent }));
          else all(store, 'profileConversation', [owner, conversationId], rows => set({ rows: rows.sort(byCreated), parent }));
        });
      }
    }, conversationStores);
    if (!prior.ok) return prior;
    const captured = prior.value;
    if (captured.parent === null && isWrite) return refuse('ContextUnavailable');
    if (captured.parent) await validateConversationRow(captured.parent, owner, 'conversation');
    for (const row of captured.rows) await validateConversationRow(row, owner, row.entity);
    if (!isWrite) {
      let rows = captured.rows;
      if (action === 'Conversation.Messages' && args.contextOnly === true) rows = rows.filter(row => !row.isCompacted);
      if (['Conversation.Recent', 'Conversation.Archived', 'Conversation.BySpace'].includes(action)) {
        if (!Number.isInteger(args.limit) || args.limit < -2147483648 || args.limit > 2147483647) return refuse('InvalidArgument');
        if (args.scope) rows = rows.filter(row => {
          const data = JSON.parse(row.json); const scope = args.scope;
          return data.mode === scope.mode && data.kind === scope.kind &&
            (data.containerId ?? null) === (scope.containerId ?? null) && (data.lessonId ?? null) === (scope.lessonId ?? null);
        });
        // SQLite Recent/Archived negative LIMIT means all matching rows. The managed
        // BySpace/scoped callers retain their actual Math.Max(0, limit) contract.
        if (args.limit >= 0) rows = rows.slice(0, args.limit);
      }
      return { ok: true, committed: false, value: action === 'Conversation.Get' ? rows[0] ?? null : rows };
    }
    let changes = [], removed = [], protectedEntry = false;
    if (args.row) {
      const before = captured.rows[0] ?? null;
      if (before && (before.entity !== args.row.entity || before.conversationId && before.conversationId !== args.row.conversationId))
        return refuse('IdentityConflict');
      const expected = args.expectedRevision ?? before?.revision ?? '0';
      if (expected !== (before?.revision ?? '0')) return refuse('ConversationRevisionConflict', { id: args.row.id, expectedRevision: expected, actualRevision: before?.revision ?? '0' });
      const row = { ...args.row, revision: (BigInt(expected) + 1n).toString() };
      if (!revision(row.revision)) return refuse('RevisionExhausted');
      if (row.entity === 'conversation' && before && row.createdAt !== before.createdAt) {
        // Preserve native UpsertConversation's original CreatedAt; all other canonical fields stay complete.
        const payload = JSON.parse(row.json); payload.createdAt = before.createdAt;
        row.createdAt = before.createdAt; row.json = JSON.stringify(payload); row.sha256 = await hash(row.json);
      }
      changes.push({ row, before });
    } else if (action === 'Conversation.DetachSpace' || action === 'Conversation.CompactMessages') {
      if (action === 'Conversation.CompactMessages' && (!Array.isArray(args.messageIds) || args.messageIds.some(id => !uuid(id)))) return refuse('InvalidArgument');
      for (const before of captured.rows) {
        if (action === 'Conversation.CompactMessages' && !args.messageIds.includes(before.id)) continue;
        const payload = JSON.parse(before.json);
        if (action === 'Conversation.DetachSpace') payload.spaceId = null; else payload.isCompacted = true;
        const json = JSON.stringify(payload), row = { ...before, json, sha256: await hash(json), revision: (BigInt(before.revision) + 1n).toString() };
        if (!revision(row.revision)) return refuse('RevisionExhausted');
        if (action === 'Conversation.DetachSpace') row.spaceId = null; else row.isCompacted = true;
        changes.push({ before, row });
      }
    } else {
      removed = captured.rows.filter(row => row.conversationId == null || row.conversationId === args.conversationId);
      if (action === 'Conversation.DeleteContext') {
        protectedEntry = removed.some(row => row.kind === 1); // Actual canonical CompactSummary ordinal, never deletable.
        removed = removed.filter(row => row.kind !== 1);
      }
    }
    if (original.cancelled) return refuse('Cancelled');
    return transact(db, true, original, (stores, set, deny, guard) => {
      const checks = [...changes.map(change => ({ ...change, remove: false })), ...removed.map(before => ({ before, remove: true }))];
      const proceed = () => {
        let pending = checks.length;
        const commit = () => {
          for (const change of changes) stores[entityStore(change.row.entity)].put(change.row);
          for (const before of removed) stores[entityStore(before.entity)].delete([owner, before.id]);
          if (action === 'Conversation.Delete' && removed.length) {
            for (const name of ['conversationMessages', 'conversationContexts']) {
              const request = stores[name].index('profileConversation').openCursor([owner, args.id]);
              request.onsuccess = guard(e => { const cursor = e.target.result; if (cursor) { cursor.delete(); cursor.continue(); } });
            }
          }
          set({ rows: changes.map(change => change.row), removed: removed.length, protectedEntry });
        };
        if (!pending) { commit(); return; }
        for (const check of checks) {
          const store = stores[entityStore(check.row?.entity ?? check.before.entity)], id = check.row?.id ?? check.before.id;
          store.get([owner, id]).onsuccess = guard(e => {
            const current = e.target.result ?? null, expected = check.before?.revision ?? '0';
            if ((current?.revision ?? '0') !== expected) {
              deny(refuse('ConversationRevisionConflict', { id, expectedRevision: expected, actualRevision: current?.revision ?? '0' })); return;
            }
            if (--pending === 0) commit();
          });
        }
      };
      if (captured.parent) stores.conversations.get([owner, captured.parent.id]).onsuccess = guard(e => {
        if (e.target.result?.revision !== captured.parent.revision) { deny(refuse('ContextChanged')); return; }
        proceed();
      }); else proceed();
    }, conversationStores);
  }
  async function executeScopedTask(action, args, original) {
    const owner = args.profileId;
    if (!profile(owner)) return refuse('ActorUnavailable');
    if (action === 'Upsert') {
      const row = args.row;
      if (!row || !uuid(row.taskId) || !uuid(row.contextId) || !uuid(row.executionId) || !revision(args.expectedRevision) ||
          !revision(row.revision) || BigInt(row.revision) !== BigInt(args.expectedRevision) + 1n ||
          !Number.isInteger(row.state) || row.state < 0 || row.state > 6 || typeof row.json !== 'string' ||
          typeof row.createdAt !== 'string' || typeof row.updatedAt !== 'string' || !/^[0-9a-f]{64}$/.test(row.sha256 ?? '') ||
          await hash(row.json) !== row.sha256) return refuse('InvalidArgument');
    } else if (action === 'Get' && !uuid(args.taskId) || action === 'GetByContext' && !uuid(args.contextId)) return refuse('InvalidArgument');
    if (action !== 'Upsert') {
      const earlier = [];
      for (const other of originals.values()) {
        if (other === original) break; // Only the actual insertion prefix, never self/later work.
        if (other.profileId === owner && other.action === 'Upsert' &&
            (action === 'GetResumable' || action === 'Get' && other.taskId === args.taskId ||
              action === 'GetByContext' && other.contextId === args.contextId)) earlier.push(other);
      }
      // Preserve read inspection of SAME-profile already-admitted hashing/open/write originals.
      // A terminal latch is settlement only; exact failed causes remain in owner close custody.
      await Promise.all(earlier.map(other => other.settled));
    }
    if (original.cancelled) return refuse('Cancelled');
    const db = await open();
    return transact(db, action === 'Upsert', original, (stores, set, deny, guard) => {
      const owns = (task, done) => stores.taskProfiles.get(task.taskId).onsuccess = guard(e => {
        const binding = e.target.result;
        if (binding?.profileId !== owner || binding.contextId !== task.contextId) { done(false); return; }
        stores.conversations.get([owner, task.contextId]).onsuccess = guard(c => done(c.target.result?.mode === 2));
      });
      if (action === 'Upsert') {
        const row = args.row;
        stores.conversations.get([owner, row.contextId]).onsuccess = guard(c => {
          if (c.target.result?.mode !== 2) { deny(refuse('ContextUnavailable')); return; }
          stores.taskProfiles.get(row.taskId).onsuccess = guard(b => {
            const binding = b.target.result;
            if (binding && (binding.profileId !== owner || binding.contextId !== row.contextId)) { deny(refuse('IdentityConflict')); return; }
            stores.tasks.get(row.taskId).onsuccess = guard(e => {
              const current = e.target.result ?? null;
              // Legacy unbound tasks are preserved, but cannot be claimed by a copied context or first browser actor.
              if (current && !binding || (current === null ? args.expectedRevision !== '0' : current.revision !== args.expectedRevision ||
                  current.contextId !== row.contextId || current.executionId !== row.executionId || current.createdAt !== row.createdAt)) {
                deny(refuse('RevisionConflict', { taskId: row.taskId, expectedRevision: args.expectedRevision, proposedRevision: row.revision })); return;
              }
              stores.tasks.put({ ...row }); stores.taskProfiles.put({ taskId: row.taskId, contextId: row.contextId, profileId: owner });
              set({ taskId: row.taskId, contextId: row.contextId, executionId: row.executionId, revision: row.revision, sha256: row.sha256 });
            });
          });
        });
      } else if (action === 'Get') stores.tasks.get(args.taskId).onsuccess = guard(e => {
        const row = e.target.result; if (!row) { set(null); return; }
        owns(row, yes => set(yes ? row : null));
      });
      else {
        const request = action === 'GetByContext' ? stores.tasks.index('contextId').getAll(args.contextId) : stores.tasks.getAll();
        request.onsuccess = guard(e => {
          const rows = e.target.result.filter(row => action !== 'GetResumable' || [0, 1, 2, 3].includes(row.state)), accepted = [];
          let remaining = rows.length;
          if (!remaining) { set(action === 'GetByContext' ? null : []); return; }
          for (const row of rows) owns(row, yes => {
            if (yes) accepted.push(row);
            if (--remaining === 0) { accepted.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt) || a.taskId.localeCompare(b.taskId)); set(action === 'GetByContext' ? accepted[0] ?? null : accepted); }
          });
        });
      }
    }, ['tasks', 'taskProfiles', 'conversations']);
  }

  const eventActions = new Set(['ExecutionEvents.Append', 'ExecutionEvents.GetExecution', 'ExecutionEvents.Search']);
  const eventFields = ['eventId', 'executionId', 'actionId', 'parentActionId', 'origin', 'actionType', 'status',
    'name', 'componentId', 'timestamp', 'startedAt', 'endedAt', 'retryOfActionId', 'recoveryOfActionId',
    'remediationId', 'taskId', 'tabId', 'projectId', 'json', 'sha256'];
  const roundtripTimestamp = text => {
    if (typeof text !== 'string') throw new Error('InvalidCanonicalEventTimestamp');
    const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/.exec(text);
    if (!match) throw new Error('InvalidCanonicalEventTimestamp');
    return `${match[1]}.${(match[2] ?? '').padEnd(7, '0')}${match[3] === 'Z' ? '+00:00' : match[3]}`;
  };
  function inspectEventRow(row, owner, stored, original) {
    if (!row || row.profileId !== owner || !uuid(row.eventId) || !uuid(row.executionId) || !uuid(row.actionId) ||
        typeof row.json !== 'string' || !/^[0-9a-f]{64}$/.test(row.sha256 ?? '') ||
        !Number.isInteger(row.origin) || row.origin < 0 || row.origin > 4 ||
        !Number.isInteger(row.actionType) || row.actionType < 0 || row.actionType > 33 ||
        !Number.isInteger(row.status) || row.status < 0 || row.status > 11 || typeof row.name !== 'string')
      throw new Error('InvalidCanonicalExecutionEvent');
    let payload;
    try { payload = JSON.parse(row.json); }
    catch (error) {
      if (stored) {
        original.eventPayloadParseError = error;
        retain(original, error); // SAME persisted-source parse fault, never request argument syntax.
      }
      throw error;
    }
    for (const field of eventFields) {
      if (field === 'json' || field === 'sha256') continue;
      if (field === 'timestamp' ? row.timestamp !== roundtripTimestamp(payload.timestamp) :
          (row[field] ?? null) !== (payload[field] ?? null)) throw new Error('InvalidCanonicalEventMetadata');
    }
    for (const field of ['parentActionId', 'retryOfActionId', 'recoveryOfActionId', 'remediationId', 'taskId', 'tabId', 'projectId'])
      if (row[field] != null && !uuid(row[field])) throw new Error('InvalidCanonicalEventLink');
    if (stored && (!revision(row.sequence) || row.sequence === '0' || row.sequenceSort !== row.sequence.padStart(19, '0')))
      throw new Error('InvalidCanonicalEventSequence');
    return payload;
  }
  async function validateEventRow(row, owner, stored, original) {
    inspectEventRow(row, owner, stored, original);
    if (await hash(row.json) !== row.sha256) throw new Error('InvalidCanonicalEventHash');
  }
  async function executeEvents(action, args, original) {
    if (!profile(args?.profileId)) return refuse('ActorUnavailable');
    const owner = args.profileId;
    const earlier = [];
    for (const other of originals.values()) {
      if (other === original) break;
      if (other.profileId === owner && other.action === 'ExecutionEvents.Append') earlier.push(other.settled);
    }
    await Promise.all(earlier); // Only actual earlier originals, never a later/self cycle.
    if (original.cancelled) return refuse('Cancelled');
    let candidates;
    if (action === 'ExecutionEvents.Append') {
      if (!Array.isArray(args.rows) || !args.rows.length) return refuse('InvalidArgument');
      const unique = new Map();
      for (const supplied of args.rows) {
        const row = { ...supplied }; await validateEventRow(row, owner, false, original);
        const before = unique.get(row.eventId);
        if (before && eventFields.some(field => before[field] !== row[field]))
          return refuse('ExecutionEventIdentityConflict', { eventId: row.eventId });
        if (!before) unique.set(row.eventId, row);
      }
      candidates = [...unique.values()];
    } else if (action === 'ExecutionEvents.GetExecution' && !uuid(args.executionId) ||
        action === 'ExecutionEvents.Search' && (typeof args.query !== 'string' || !Number.isInteger(args.limit) ||
          args.limit < -2147483648 || args.limit > 2147483647)) return refuse('InvalidArgument');
    if (original.cancelled) return refuse('Cancelled'); // Held hashing cancel precedes database acquisition.
    const db = await open();
    if (original.cancelled) return refuse('Cancelled');
    if (action !== 'ExecutionEvents.Append') {
      const reply = await transact(db, false, original, (store, set, _deny, guard) => {
        const request = action === 'ExecutionEvents.GetExecution' ? store.index('profileExecution').getAll([owner, args.executionId])
          : store.index('profileId').getAll(owner);
        request.onsuccess = guard(e => set(e.target.result.sort((a, b) => a.sequenceSort.localeCompare(b.sequenceSort))));
      }, ['executionEvents']);
      if (reply.ok) for (const row of reply.value) await validateEventRow(row, owner, true, original);
      return reply; // Managed canonical adapter derives exact owning SQLite summary semantics.
    }
    return transact(db, true, original, (stores, set, deny, guard) => {
      stores.executionEventSequences.get(owner).onsuccess = guard(e => {
        const counter = e.target.result;
        if (counter && (counter.profileId !== owner || !revision(counter.sequence))) throw new Error('InvalidCanonicalEventAllocator');
        let sequence = BigInt(counter?.sequence ?? '0'), remaining = candidates.length;
        const existing = new Map();
        const commit = () => {
          const rows = [];
          for (const candidate of candidates) {
            const before = existing.get(candidate.eventId);
            if (before) {
              inspectEventRow(before, owner, true, original);
              if (BigInt(before.sequence) > sequence) throw new Error('InvalidCanonicalEventAllocator');
              // Candidate full hash was checked before this SAME append transaction.
              // Matching complete bytes+hash validates the immutable original without async I/O inside IDB.
              if (eventFields.some(field => before[field] !== candidate[field])) {
                deny(refuse('ExecutionEventIdentityConflict', { eventId: candidate.eventId })); return;
              }
              rows.push(before); continue;
            }
            sequence++;
            if (sequence > 9223372036854775807n) { deny(refuse('EventSequenceExhausted')); return; }
            const row = { ...candidate, sequence: sequence.toString(), sequenceSort: sequence.toString().padStart(19, '0') };
            stores.executionEvents.put(row); rows.push(row);
          }
          stores.executionEventSequences.put({ profileId: owner, sequence: sequence.toString() });
          set(rows); // Released only by the original tx.oncomplete, including identical duplicates.
        };
        for (const candidate of candidates) stores.executionEvents.get([owner, candidate.eventId]).onsuccess = guard(result => {
          existing.set(candidate.eventId, result.target.result ?? null); if (--remaining === 0) commit();
        });
      });
    }, ['executionEvents', 'executionEventSequences']);
  }

  async function execute(action, args, original) {
    if (eventActions.has(action)) return executeEvents(action, args, original);
    if (conversationActions.has(action)) return executeConversation(action, args, original);
    if (args?.profileId !== undefined && ['Upsert', 'Get', 'GetByContext', 'GetResumable'].includes(action))
      return executeScopedTask(action, args, original);
    if (!['Upsert', 'Get', 'GetByContext', 'GetResumable'].includes(action)) return refuse('InvalidArgument');
    if (action === 'Get' && !uuid(args?.taskId) || action === 'GetByContext' && !uuid(args?.contextId)) return refuse('InvalidArgument');
    if (action !== 'Upsert') {
      const earlier = [...originals.values()].filter(other => other !== original && other.action === 'Upsert' &&
        (action === 'GetResumable' || action === 'Get' && other.taskId === args.taskId ||
          action === 'GetByContext' && other.contextId === args.contextId));
      // Read inspection joins real already-admitted hashing/open/transaction work;
      // it cannot claim absence while that same task may still commit later.
      await Promise.all(earlier.map(other => other.settled));
      if (original.cancelled) return refuse('Cancelled');
    }
    let row;
    if (action === 'Upsert') {
      row = args?.row;
      if (!row || !uuid(row.taskId) || !uuid(row.contextId) || !uuid(row.executionId) ||
          !revision(args.expectedRevision) || !revision(row.revision) ||
          BigInt(row.revision) !== BigInt(args.expectedRevision) + 1n ||
          !Number.isInteger(row.state) || row.state < 0 || row.state > 6 ||
          typeof row.createdAt !== 'string' || typeof row.updatedAt !== 'string' ||
          typeof row.json !== 'string' || typeof row.sha256 !== 'string' || !/^[0-9a-f]{64}$/.test(row.sha256))
        return refuse('InvalidArgument');
      // C# owns semantic snapshot validation and reads back every exact identity/hash.
      // This layer never reparses Int64 payload fields into approximate JS numbers.
      if (await hash(row.json) !== row.sha256) return refuse('InvalidArgument');
      row = { ...row }; // Detached complete C# capture, before transaction admission.
    }
    // A scoped stop during the held actual hash seals this original before any database acquisition.
    if (original.cancelled) return refuse('Cancelled');
    const db = await open();
    if (original.cancelled) return refuse('Cancelled');
    if (action === 'Get') return transact(db, false, original, (tasks, set, _deny, guard) => {
      tasks.get(args.taskId).onsuccess = guard(event => set(event.target.result ?? null));
    });
    if (action === 'GetByContext') return transact(db, false, original, (tasks, set, _deny, guard) => {
      tasks.index('contextId').getAll(args.contextId).onsuccess = guard(event => {
        const rows = event.target.result.sort((left, right) => right.updatedAt.localeCompare(left.updatedAt) || left.taskId.localeCompare(right.taskId));
        set(rows[0] ?? null);
      });
    });
    if (action === 'GetResumable') return transact(db, false, original, (tasks, set, _deny, guard) => {
      tasks.getAll().onsuccess = guard(event => set(event.target.result.filter(row => [0, 1, 2, 3].includes(row.state))
        .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt) || left.taskId.localeCompare(right.taskId))));
    });
    return transact(db, true, original, (tasks, set, deny, guard) => {
      tasks.get(row.taskId).onsuccess = guard(event => {
        const actual = event.target.result ?? null;
        if (actual === null ? args.expectedRevision !== '0' :
            actual.revision !== args.expectedRevision || actual.contextId !== row.contextId ||
            actual.executionId !== row.executionId || actual.createdAt !== row.createdAt) {
          deny(refuse('RevisionConflict', { taskId: row.taskId, expectedRevision: args.expectedRevision,
            proposedRevision: row.revision })); return;
        }
        tasks.put(row);
        set({ taskId: row.taskId, contextId: row.contextId, executionId: row.executionId,
          revision: row.revision, sha256: row.sha256 }); // Receipt released ONLY at tx.oncomplete.
      });
    });
  }
  const module = {
    async invoke(requestId, action, json, cancelledAtAdmission = false) {
      if (typeof cancelledAtAdmission !== 'boolean' || typeof requestId !== 'string' || !requestId || originals.has(requestId))
        return JSON.stringify(refuse('InvalidArgument'));
      if (cancelledAtAdmission) return JSON.stringify(refuse('Cancelled'));
      if (sealed) return JSON.stringify(refuse('StorageUnavailable'));
      if (originals.size + failedOriginals.length >= maximumRetainedOriginals)
        return JSON.stringify(refuse('OriginalCapacity')); // No business acquisition after finite frame capacity.
      let finish;
      const original = { transaction: null, committed: false, cancelled: false, errors: [], completionObservation: undefined,
        settled: new Promise(resolve => { finish = resolve; }) };
      originals.set(requestId, original); // Actual admission is visible before hashing/open/transaction.
      try {
        const args = JSON.parse(json);
        original.action = action;
        original.profileId = args?.profileId;
        original.taskId = args?.row?.taskId ?? args?.taskId;
        original.contextId = args?.row?.contextId ?? args?.contextId;
        const reply = await execute(action, args, original);
        return JSON.stringify(original.cancelled && !reply.committed ? refuse('Cancelled') : reply);
      } catch (error) {
        const requestSyntax = error instanceof SyntaxError && error !== original.eventPayloadParseError;
        if (!requestSyntax) retain(original, error);
        return JSON.stringify({ ...refuse(original.cancelled ? 'Cancelled' : requestSyntax ? 'InvalidArgument' : storageCode(error)),
          committed: original.committed, value: original.completionObservation });
      } finally {
        if (original.errors.length) failedOriginals.push(original); // Exact original error refs stay in owner custody.
        originals.delete(requestId); finish();
      }
    },
    cancel(requestId) {
      const original = originals.get(requestId);
      if (!original) return;
      original.cancelled = true;
      if (original.transaction && !original.committed)
        try { original.transaction.abort(); } catch (error) { retain(original, error); throw error; }
    },
    dispose() {
      if (close) return close;
      let resolve, reject;
      close = new Promise((yes, no) => { resolve = yes; reject = no; });
      sealed = true; // Publish/coalesce before stops can reenter.
      const cohort = [...originals.entries()];
      const errors = [];
      for (const [id] of cohort) { try { module.cancel(id); } catch (error) { add(errors, error); } }
      Promise.allSettled(cohort.map(([, original]) => original.settled)).then(async settled => {
        for (const outcome of settled) if (outcome.status === 'rejected') add(errors, outcome.reason);
        for (const original of failedOriginals)
          for (const error of original.errors) add(errors, error);
        try { const db = await opening; db?.close(); } catch (error) { add(errors, error); }
        for (const error of ownerErrors) add(errors, error);
        opening = null;
        if (errors.length) reject(new AggregateError(errors, 'Original Task storage close failed.')); else resolve();
      }).catch(reject);
      return close;
    },
  };
  return module;
}
