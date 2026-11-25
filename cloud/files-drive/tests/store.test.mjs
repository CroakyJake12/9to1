import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { DriveStore } from '../src/store.mjs';

function fixture() {
  const db = new DatabaseSync(':memory:'); let nesting = 0;
  const storage = { sql: { exec(sql, ...args) { const stmt = db.prepare(sql); const rows = stmt.all(...args); return { toArray: () => rows }; } },
    transactionSync(fn) { if (nesting++) { try { return fn(); } finally { nesting--; } } db.exec('BEGIN IMMEDIATE'); try { const result = fn(); db.exec('COMMIT'); return result; } catch (error) { db.exec('ROLLBACK'); throw error; } finally { nesting--; } } };
  let now = 1000000; return { db, storage, store: new DriveStore(storage, 'owner', { now: () => now }), advance: n => { now += n; } };
}
function file(extra = {}) { return { id: 'google-file', name: 'A file', version: '1', mimeType: 'text/plain', size: '5', createdTime: '2026-11-15T10:00:00Z', modifiedTime: '2026-11-15T10:00:00Z', ownedByMe: true, ...extra }; }
test('canonical identities survive rename, parent changes, reopening and old read replies', () => {
  const f = fixture(); const a = f.store.mapFile(file());
  const b = f.store.mapFile(file({ name: 'Renamed', version: '2', parents: ['google-parent'] }));
  assert.deepEqual(a.id, b.id); assert.notDeepEqual(a.currentRevisionId, b.currentRevisionId);
  assert.equal(f.store.googleId(b.parentId), 'google-parent');
  assert.deepEqual(f.store.mapFile(file()), b);
  const reopened = new DriveStore(f.storage, 'owner'); assert.deepEqual(reopened.mapFile(file({ name: 'Renamed', version: '2', parents: ['google-parent'] })), b);
  f.db.close();
});
test('root placement and foreign owner are represented without impersonating the local account', () => {
  const f = fixture(); f.store.setRoot(file({ id: 'root-id', mimeType: 'application/vnd.google-apps.folder' }));
  const external = f.store.mapFile(file({ ownedByMe: false, owners: [{ permissionId: 'external-owner' }], parents: ['root-id'] }));
  assert.equal(external.parentId, null); assert.equal(external.ownerPrincipalId, 'google:external-owner'); f.db.close();
});
test('page tokens are bound to folder/search/trash and expire', () => {
  const f = fixture(); const context = { parentId: null, query: 'cake', trash: false };
  const token = f.store.newPage(context, 'private-google-cursor');
  assert.equal(f.store.readPage(token, context), 'private-google-cursor');
  assert.throws(() => f.store.readPage(token, { ...context, trash: true }), { code: 'PageExpired' });
  f.advance(16 * 60000); assert.throws(() => f.store.readPage(token, context), { code: 'PageExpired' }); f.db.close();
});
test('write journal refuses intent reuse and preserves unknown/committed outcome across reopen', () => {
  const f = fixture(); const id = crypto.randomUUID(); const intent = { operation: 'Rename', itemId: crypto.randomUUID(), name: 'Next' };
  assert.equal(f.store.claimOperation(id, intent).stage, 'prepared');
  f.store.stageOperation(id, 'issuing', 'provider-id');
  assert.equal(new DriveStore(f.storage, 'owner').claimOperation(id, intent).stage, 'issuing');
  assert.throws(() => f.store.claimOperation(id, { ...intent, name: 'Other' }), { code: 'OperationConflict' });
  f.store.stageOperation(id, 'committed', null, { value: { id }, error: null });
  assert.equal(f.store.claimOperation(id, intent).result.value.id, id); f.db.close();
});
test('malformed provider fields and owner reassignment never fabricate usable metadata', () => {
  const f = fixture(); for (const patch of [{ version: 'NaN' }, { size: '-1' }, { createdTime: null }, { id: '../../other' }]) assert.throws(() => f.store.mapFile(file(patch)));
  assert.throws(() => new DriveStore(f.storage, 'another-owner'), /another owner/); f.db.close();
});

test('change pages offer a continuation only when another row exists', t => {
  const { db, store } = fixture();
  t.after(() => db.close());
  for (const id of ['created', 'moved', 'trashed', 'restored']) store.change({ id });

  const first = store.changes(0, 2);
  assert.deepEqual(first.items.map(({ id }) => id), ['created', 'moved']);
  assert.equal(first.nextPageToken, String(first.items.at(-1).cursor.revision));

  const last = store.changes(Number(first.nextPageToken), 2);
  assert.deepEqual(last.items.map(({ id }) => id), ['trashed', 'restored']);
  assert.equal(last.nextPageToken, null);

  const checkpoint = last.items.at(-1).cursor.revision;
  assert.deepEqual(store.changes(checkpoint, 2), { items: [], nextPageToken: null });
  store.change({ id: 'renamed' });
  assert.deepEqual(store.changes(checkpoint, 1), {
    items: [{ id: 'renamed', cursor: { revision: checkpoint + 1 } }],
    nextPageToken: null,
  });
});
