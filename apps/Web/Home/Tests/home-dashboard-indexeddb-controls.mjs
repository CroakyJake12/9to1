// Run only in a real browser with actual IndexedDB/crypto. The profile below is
// explicit test data, never a verified account or a production authentication grant.
export async function runHomeDashboardIndexedDbControls({ moduleUrl = new URL('../Storage/home-dashboard-indexeddb.js', import.meta.url).href } = {}) {
  if (!globalThis.indexedDB || !globalThis.crypto?.subtle) throw new Error('Real browser IndexedDB/SHA-256 required.');
  const { createHomeDashboardModule } = await import(moduleUrl);
  const profile = 'cake-account-profile:' + 'a'.repeat(64) + ':12345678-1234-4234-8234-123456789abc';
  const databaseName = '9to1-home-control-' + crypto.randomUUID();
  const check = (value, message) => { if (!value) throw new Error(message); };
  const contains = (source, same) => source === same || source instanceof AggregateError && source.errors.some(cause => contains(cause, same));
  const faults = async actual => { try { await actual; } catch (cause) { return cause; } throw new Error('Expected SAME actual source fault.'); };
  const hash = async json => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(json))), byte => byte.toString(16).padStart(2, '0')).join('');
  const jsonAt = (revision, flag = false) => `{"SchemaVersion":1,"Revision":${revision},"Tiles":[],"AllowAiGeneratedTiles":${flag},"AllowAiReorder":false,"ParentRevision":null,"ChangeKind":0}`;
  const row = async (revision, json) => ({ schema: 1, profile, revision, json, hash: await hash(json) });
  const args = value => JSON.stringify({ profile, ...value });
  const owners = [], results = [];
  const acquire = options => { const actual = createHomeDashboardModule({ databaseName, ...options }); owners.push(actual); return actual; };
  const invoke = (owner, action, value = {}) => owner.invoke(crypto.randomUUID(), action, args(value), false);
  const close = async owner => { const actual = owner.dispose(); await actual; owners.splice(owners.indexOf(owner), 1); };
  const withStore = async (name, mode, body) => {
    const db = await new Promise((resolve, reject) => {
      const request = indexedDB.open(databaseName); request.onerror = () => reject(request.error); request.onsuccess = () => resolve(request.result);
    });
    try {
      return await new Promise((resolve, reject) => {
        const tx = db.transaction(name, mode); let value, failure;
        try { const request = body(tx.objectStore(name)); request.onsuccess = () => { value = request.result; }; request.onerror = () => { failure = request.error; }; }
        catch (cause) { failure = cause; try { tx.abort(); } catch (cleanup) { failure = new AggregateError([cause, cleanup]); } }
        tx.oncomplete = () => failure ? reject(failure) : resolve(value);
        tx.onabort = () => reject(failure ?? tx.error); tx.onerror = () => { failure ??= tx.error; };
      });
    } finally { db.close(); }
  };
  const cases = [
    ['actual transaction save/fresh-owner reopen/history/conflict proposal', async () => {
      const first = acquire(); const one = jsonAt('1', true);
      const saved = JSON.parse(await invoke(first, 'Save', { expected: '0', revision: '1', json: one }));
      check(saved.committed === true && saved.record.json === one, 'Actual first commit receipt lost proposal.');
      await close(first);
      const fresh = acquire(); const reopened = JSON.parse(await invoke(fresh, 'Load'));
      check(reopened.record.json === one, 'Fresh actual owner did not reopen saved JSON.');
      const stale = jsonAt('1', false);
      const conflict = JSON.parse(await invoke(fresh, 'Save', { expected: '0', revision: '1', json: stale }));
      check(conflict.outcome === 'Conflict' && conflict.committed === false && conflict.record.json === one, 'Actual CAS conflict replaced winner.');
      const conflicts = await withStore('conflicts', 'readonly', store => store.getAll());
      check(conflicts.some(value => value.json === stale && value.profile === profile), 'Actual conflict proposal not durably retained.');
      const two = jsonAt('2'); await invoke(fresh, 'Save', { expected: '1', revision: '2', json: two });
      const old = JSON.parse(await invoke(fresh, 'GetRevision', { revision: '1' }));
      check(old.record.json === one, 'Original complete immutable history lost.');
      await close(fresh);
    }],
    ['parallel actual owners retain every reported CAS conflict proposal', async () => {
      const first = acquire(), second = acquire();
      const a = jsonAt('3', false), b = jsonAt('3', true);
      const actuals = [invoke(first, 'Save', { expected: '2', revision: '3', json: a }), invoke(second, 'Save', { expected: '2', revision: '3', json: b })];
      const replies = (await Promise.all(actuals)).map(JSON.parse);
      check(replies.filter(reply => reply.committed).length === 1 && replies.filter(reply => reply.outcome === 'Conflict').length === 1, 'Actual concurrent CAS produced incompatible outcomes.');
      const proposals = [a, b], conflicts = await withStore('conflicts', 'readonly', store => store.getAll());
      replies.forEach((reply, index) => { if (reply.outcome === 'Conflict') check(conflicts.some(value => value.json === proposals[index]), 'Race conflict lost original proposal.'); });
      await close(first); await close(second);
    }],
    ['exact full Int64 revisions beyond JavaScript safe integer', async () => {
      const seed = await row('9007199254740993', jsonAt('9007199254740993'));
      await withStore('layouts', 'readwrite', store => store.put(seed));
      const owner = acquire(), proposal = jsonAt('9007199254740994', true);
      const saved = JSON.parse(await invoke(owner, 'Save', { expected: '9007199254740993', revision: '9007199254740994', json: proposal }));
      check(saved.record.revision === '9007199254740994' && saved.record.json === proposal, 'Int64 revision rounded.');
      const historical = JSON.parse(await invoke(owner, 'GetRevision', { revision: '9007199254740993' }));
      check(historical.record.json === seed.json, 'Int64 historical row changed.');
      await close(owner);
    }],
    ['actual corrupt stored SyntaxError and SAME original close cause are preserved', async () => {
      const corrupt = await row('1', '{'); await withStore('layouts', 'readwrite', store => store.put(corrupt));
      const owner = acquire(), original = invoke(owner, 'Load'), cause = await faults(original);
      check(cause instanceof SyntaxError, 'Actual stored parse cause was replaced.');
      const closeFailure = await faults(owner.dispose());
      check(contains(closeFailure, cause), 'SAME actual SyntaxError missing from owner close.');
      owners.splice(owners.indexOf(owner), 1);
      const retained = await withStore('layouts', 'readonly', store => store.get(profile));
      check(JSON.stringify(retained) === JSON.stringify(corrupt), 'Corrupt original row was rewritten.');
    }],
    ['held actual SHA source keeps original/close pending; negative assertion cleanup releases SAME source', async () => {
      // Repair only this explicitly owned fixture, then acquire a real producer whose
      // first actual hash is held. No production provider/readiness source is replaced.
      await withStore('layouts', 'readwrite', store => store.delete(profile));
      let release, entered; const held = new Promise(resolve => { release = resolve; }); const admission = new Promise(resolve => { entered = resolve; });
      const actualCrypto = { subtle: { digest: async (...arguments_) => { entered(); await held; return await crypto.subtle.digest(...arguments_); } } };
      const owner = acquire({ crypto: actualCrypto });
      let settled = false, closed = false, originalFailure, closeFailure, assertion;
      const original = invoke(owner, 'Save', { expected: '0', revision: '1', json: jsonAt('1') });
      const observedOriginal = original.then(() => { settled = true; }, cause => { settled = true; originalFailure = cause; });
      let closeTask, observedClose;
      try {
        await admission;
        closeTask = owner.dispose(); observedClose = closeTask.then(() => { closed = true; }, cause => { closed = true; closeFailure = cause; });
        await Promise.resolve();
        check(!settled && !closed, 'Cancellation/close signal replaced held actual source settlement.');
      } catch (cause) { assertion = cause; }
      finally {
        release(); // SAME original source is released even on the original assertion failure.
        await observedOriginal;
        closeTask ??= owner.dispose(); observedClose ??= closeTask.then(() => { closed = true; }, cause => { closed = true; closeFailure = cause; });
        await observedClose; owners.splice(owners.indexOf(owner), 1);
      }
      if (assertion) throw new AggregateError([assertion, ...[originalFailure, closeFailure].filter(Boolean)], 'Original negative assertion and independent actual/close joins.');
      check(originalFailure && closeFailure && settled && closed, 'Closed held write fabricated successful save/close.');
      const absent = await withStore('layouts', 'readonly', store => store.get(profile)); check(absent === undefined, 'Held retired owner wrote a late layout.');
    }],
  ];
  let originalFailure;
  try {
    for (const [name, run] of cases) { await run(); results.push({ name, passed: true }); }
  } catch (cause) { originalFailure = cause; }
  const cleanup = [];
  for (const owner of owners) try { await owner.dispose(); } catch (cause) { cleanup.push(cause); }
  try {
    await new Promise((resolve, reject) => { const request = indexedDB.deleteDatabase(databaseName); request.onsuccess = resolve; request.onerror = () => reject(request.error); request.onblocked = () => reject(new Error('Actual fixture database cleanup blocked.')); });
  } catch (cause) { cleanup.push(cause); }
  if (originalFailure || cleanup.length) throw new AggregateError([...[originalFailure].filter(Boolean), ...cleanup], 'Actual Home IndexedDB controls and independent cleanup.');
  return { scope: 'REAL_INDEXEDDB_HOME_STORAGE_ONLY_SCRIPTED_PROFILE_NO_AUTH_BROWSER_UI_ACCEPTANCE', discovered: cases.length, passed: results.length, failed: 0, skipped: 0, results };
}
