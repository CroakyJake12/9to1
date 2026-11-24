import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { DriveProvider, exactNameQuery, strongDriveETag } from '../src/provider.mjs';
import { DriveStore } from '../src/store.mjs';
import { DriveQuota } from '../src/quota.mjs';
import { GoogleTransport } from '../src/google-transport.mjs';

const T0 = Date.UTC(2026, 10, 15);
const FOLDER = 'application/vnd.google-apps.folder';
const owner = '15d642d8-0e7d-4cbe-ae68-2e289dc9e218';

function file(id, properties = {}) {
  return {
    id, name: id, mimeType: 'text/plain', version: '1', createdTime: new Date(T0).toISOString(),
    modifiedTime: new Date(T0).toISOString(), size: '10', parents: ['my-root'], ownedByMe: true,
    shared: false, trashed: false, explicitlyTrashed: false,
    capabilities: { canAddChildren: true, canRemoveChildren: true, canRename: true, canMoveItemWithinDrive: true,
      canTrash: true, canUntrash: true }, ...properties,
  };
}

function fixture(t, { limits, conditionalWrites = true } = {}) {
  const db = new DatabaseSync(':memory:');
  t.after(() => db.close());
  const model = { now: T0, calls: [], ids: 0, lost: false, nextRace: false, weakETag: false };
  const storage = {
    sql: { exec(query, ...args) {
      if (model.failJournal && query.startsWith('INSERT INTO files_changes')) throw new Error('Controlled journal fault');
      return { toArray: () => db.prepare(query).all(...args) };
    } },
    transactionSync(work) {
      db.exec('BEGIN IMMEDIATE');
      try { const result = work(); db.exec('COMMIT'); return result; } catch (error) { db.exec('ROLLBACK'); throw error; }
    },
    async sync() {},
  };
  const root = file('my-root', { mimeType: FOLDER, parents: [] });
  const remote = new Map([[root.id, root]]);
  const now = () => model.now;
  const tag = value => '"' + value.id + ':' + value.version + '"';
  const v2 = value => ({
    id: value.id, version: value.version, etag: model.weakETag ? 'W/' + tag(value) : tag(value),
    title: value.name, modifiedDate: value.modifiedTime, parents: value.parents.map(id => ({ id })),
    labels: { trashed: value.trashed }, explicitlyTrashed: value.explicitlyTrashed,
    properties: [
      ...Object.entries(value.appProperties ?? {}).map(([key, item]) => ({ key, value: item, visibility: 'PRIVATE' })),
      ...Object.entries(value.publicProperties ?? {}).map(([key, item]) => ({ key, value: item, visibility: 'PUBLIC' })),
    ],
  });
  const response = (status, body, headers) => Response.json(body, { status, headers });
  function googleFetch(raw, options) {
    const url = new URL(raw);
    const [, , version, collection, id] = url.pathname.split('/');
    const method = options.method;
    assert.equal(url.origin, 'https://www.googleapis.com');
    assert.equal(collection, 'files');
    assert.equal(new Headers(options.headers).get('authorization'), 'Bearer local-owner-token');
    model.calls.push({ url, method, options });
    if (method === 'GET' && id === 'generateIds') return response(200, { ids: ['generated-' + ++model.ids] });
    if (method === 'GET' && !id) {
      if (model.list) return response(200, model.list(url));
      const query = url.searchParams.get('q');
      const trash = query === 'trashed = true';
      const parent = /'([^']+)' in parents/.exec(query)?.[1];
      return response(200, { files: [...remote.values()].filter(value =>
        value.trashed === trash && (trash || value.parents.includes(parent)) &&
        (!query.includes(" and name = ") || exactNameQuery(parent, value.name) === query)) });
    }
    if (method === 'POST') {
      const body = JSON.parse(options.body);
      if (remote.has(body.id)) return response(409, { error: {} });
      const created = file(body.id, body);
      remote.set(body.id, created);
      if (model.lost) { model.lost = false; throw new Error('Lost committed create reply'); }
      return response(200, created);
    }
    const current = remote.get(id === 'root' ? root.id : id);
    if (!current) return response(404, { error: {} });
    if (method === 'GET') {
      if (version === 'v3') return response(200, current);
      const body = v2(current);
      return response(200, body, { etag: model.weakRepresentation ? 'W/' + body.etag : body.etag });
    }
    assert.equal(method, 'PATCH');
    assert.equal(version, 'v2');
    if (model.nextRace) {
      model.nextRace = false;
      current.version = String(Number(current.version) + 1);
      current.name = 'Other actor saved';
    }
    if (new Headers(options.headers).get('if-match') !== tag(current)) return response(412, { error: {} });
    const patch = JSON.parse(options.body);
    if (patch.title !== undefined) current.name = patch.title;
    if (patch.labels) current.trashed = current.explicitlyTrashed = patch.labels.trashed;
    if (url.searchParams.has('addParents')) {
      assert.deepEqual(current.parents, [url.searchParams.get('removeParents')]);
      current.parents = [url.searchParams.get('addParents')];
    }
    current.appProperties = Object.fromEntries(patch.properties.filter(p => p.visibility === 'PRIVATE').map(p => [p.key, p.value]));
    current.publicProperties = Object.fromEntries(patch.properties.filter(p => p.visibility === 'PUBLIC').map(p => [p.key, p.value]));
    current.version = String(Number(current.version) + 1);
    if (model.lost) { model.lost = false; throw new Error('Lost committed patch reply'); }
    return response(200, v2(current), { etag: tag(current) });
  }

  function reopen(opts = { conditionalWrites }, quotaLimits = limits) {
    const store = new DriveStore(storage, owner, { now });
    const quota = new DriveQuota(storage, { now, limits: quotaLimits });
    const oauth = { async getAccessToken() { return 'local-owner-token'; } };
    const transport = new GoogleTransport(quota, {
      fetch: googleFetch, now, onSafetyFault: async () => assert.fail('unexpected safety hold'),
      isSafetyHeld: async () => false,
    });
    return { store, quota, provider: new DriveProvider(store, oauth, transport, { now, ...opts }) };
  }
  return { ...reopen(), reopen, model, remote };
}

const wrap = value => ({ value });
function operation(kind, metadata, options = {}) {
  const newName = options.name ?? null;
  return { newName, operation: {
    id: wrap(crypto.randomUUID()), actorId: owner, operation: kind, state: 'Pending',
    itemId: metadata?.id ?? wrap(crypto.randomUUID()), sourceParentId: metadata?.parentId ?? null,
    destinationParentId: options.destination ?? null, baseRevisionId: metadata?.currentRevisionId ?? null,
    payload: newName === null ? null : { newName },
  } };
}

test('search scans at most five Google pages and preserves a private continuation for middle-of-name matches', async t => {
  const f = fixture(t);
  f.model.list = url => {
    assert.equal(url.searchParams.get('q'), "'my-root' in parents and trashed = false");
    assert.equal(url.searchParams.get('orderBy'), 'name');
    assert.equal(url.searchParams.get('pageSize'), '100');
    assert.match(url.searchParams.get('fields'), /owners\(permissionId\).*appProperties/);
    const page = Number(url.searchParams.get('pageToken')?.slice(1) ?? 0);
    return { files: [file('fixture-' + page, { name: page === 5 ? 'Middle [CAfé] specimen' : 'Different' })],
      nextPageToken: page === 5 ? null : 'p' + (page + 1) };
  };
  const first = await f.provider.list({ query: '[café]' });
  assert.equal(first.items.length, 0);
  assert.match(first.nextPageToken, /^[a-f0-9-]{36}$/);
  assert.equal((await f.quota.status()).unitsPerMinute.used, 505);
  await assert.rejects(f.provider.list({ query: 'wrong', pageToken: first.nextPageToken }), { code: 'PageExpired' });
  const final = await f.provider.list({ query: '[café]', pageToken: first.nextPageToken });
  assert.equal(final.items[0].name, 'Middle [CAfé] specimen');
  assert.equal(final.items[0].parentId, null);
  assert.equal(final.nextPageToken, null);
});

test('Trash loads recently modified items first and binds pagination to that ordering', async t => {
  const f = fixture(t);
  const priorCursor = f.store.newPage({ parentId: null, query: '', trash: true }, 'old-name-order');
  await assert.rejects(f.provider.list({ trash: true, pageToken: priorCursor }), { code: 'PageExpired' });
  assert.equal(f.model.calls.length, 0);
  f.model.list = url => {
    assert.equal(url.searchParams.get('q'), 'trashed = true');
    assert.equal(url.searchParams.get('orderBy'), 'modifiedTime desc,name');
    const next = url.searchParams.get('pageToken');
    assert.ok(next === null || next === 'private-google-trash-page');
    const item = next === null ? file('new-trash', { name: 'Z newly modified', trashed: true })
      : file('old-trash', { name: 'A older file', trashed: true, modifiedTime: new Date(T0 - 86400000).toISOString() });
    return { files: [item], nextPageToken: next === null ? 'private-google-trash-page' : null };
  };
  const first = await f.provider.list({ trash: true });
  assert.equal(first.items[0].name, 'Z newly modified');
  assert.match(first.nextPageToken, /^[a-f0-9-]{36}$/);
  await assert.rejects(f.provider.list({ pageToken: first.nextPageToken }), { code: 'PageExpired' });
  const second = await f.provider.list({ trash: true, pageToken: first.nextPageToken });
  assert.equal(second.items[0].name, 'A older file');
  assert.equal(second.nextPageToken, null);
  assert.equal((await f.quota.status()).unitsPerMinute.used, 205);
});

test('create keeps canonical identity across lost reply, refused reconciliation, an intervening list, and replay', async t => {
  const f = fixture(t, { limits: { unitsPerMinute: 165 } });
  const request = operation('CreateFolder', null, { name: 'Private folder' });
  f.model.lost = true;
  const lost = await f.provider.mutate(request);
  assert.equal(lost.error.code, 'ProviderUnavailable');
  assert.equal(lost.error.details, null);
  const denied = await f.reopen().provider.mutate(request);
  assert.equal(denied.error.code, 'QuotaExceeded');
  assert.equal(denied.error.details, null);
  assert.ok(denied.error.retryAfter);
  f.model.now += 120_000;
  // A list costs 100 units and uses the preserved provider ID mapping.
  const reopened = f.reopen({ conditionalWrites: true }, { unitsPerMinute: 105 });
  const listed = await reopened.provider.list();
  assert.deepEqual(listed.items[0].id, request.operation.itemId);
  const committed = await reopened.provider.mutate(request);
  assert.equal(committed.value.state, 'Committed');
  assert.deepEqual(committed.value.id, request.operation.id);
  assert.deepEqual(committed.value.itemId, request.operation.itemId);
  assert.deepEqual(committed.value.payload, { newName: request.newName });
  const calls = f.model.calls.length;
  assert.deepEqual(await reopened.provider.mutate(request), committed);
  assert.equal(f.model.calls.length, calls);
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 1);
  assert.equal(reopened.store.changes(0).items.length, 1);
});

test('quota refused before create leaves a genuinely unissued intent and reuses its fixed provider ID', async t => {
  const f = fixture(t, { limits: { unitsPerMinute: 160 } });
  const request = operation('CreateFolder', null, { name: 'Resumable folder' });
  const refused = await f.provider.mutate(request);
  assert.equal(refused.error.code, 'QuotaExceeded');
  assert.deepEqual(refused.error.details, { notStarted: 'true' });
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 0);
  assert.equal(f.store.operation(request.operation.id).provider_id, 'generated-1');
  f.model.now += 120_000;
  const result = await f.reopen().provider.mutate(request);
  assert.equal(result.value.state, 'Committed');
  assert.equal(f.model.ids, 1);
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 1);
});

test('strong Drive resource tags tolerate weak representation headers only for the exact same tag', () => {
  const resource = '"MTc5NDc3OTY1NTMwNg"';
  function from(header, etag) {
    return strongDriveETag({ headers: new Headers(header === undefined ? {} : { etag: header }), value: { etag } });
  }
  for (const [header, body] of [[resource, resource], ['W/' + resource, resource], [undefined, resource], [resource, undefined]]) {
    assert.equal(from(header, body), resource);
  }
  for (const [header, body] of [
    ['W/"different"', resource], ['"different"', resource], ['W/' + resource, undefined],
    [resource, 'W/' + resource], [resource, '"malformed'], [resource, ''],
    [resource, null], [resource, [resource]], ['', resource], [undefined, undefined],
  ]) {
    assert.equal(from(header, body), null);
  }
});

test('metadata updates preserve private and public properties and require provider revision protection', async t => {
  const f = fixture(t);
  const current = file('existing', { appProperties: { existingProperty: 'preserve private' }, publicProperties: { publicProperty: 'preserve public' } });
  f.remote.set(current.id, current);
  const item = f.store.mapFile(current);
  const request = operation('Rename', item, { name: 'Renamed' });
  const gated = await f.reopen({}).provider.mutate(request);
  assert.equal(gated.error.code, 'ProviderCapabilityUnsupported');
  f.model.weakETag = true;
  const weak = await f.provider.mutate(request);
  assert.equal(weak.error.code, 'ProviderCapabilityUnsupported');
  f.model.weakETag = false;
  f.model.weakRepresentation = true;
  f.model.nextRace = true;
  const conflict = await f.provider.mutate(request);
  assert.equal(conflict.error.code, 'RevisionConflict');
  assert.equal(new Headers(f.model.calls.find(call => call.method === 'PATCH').options.headers).get('if-match'), '"existing:1"');
  assert.equal(current.name, 'Other actor saved');
  const untouched = f.model.calls.length;
  assert.deepEqual(await f.provider.mutate(request), conflict);
  assert.equal(f.model.calls.length, untouched);
  const fresh = (await f.provider.get(item.id)).value;
  assert.deepEqual(fresh.id, item.id);
  assert.notDeepEqual(fresh.currentRevisionId, item.currentRevisionId);
  const stale = await f.provider.mutate(operation('Rename', item, { name: 'Stale again' }));
  assert.equal(stale.error.code, 'RevisionConflict');
  assert.deepEqual(stale.error.details, { notStarted: 'true' });
  f.model.lost = true;
  const next = operation('Rename', fresh, { name: 'Our rename' });
  assert.equal((await f.provider.mutate(next)).error.code, 'ProviderUnavailable');
  const completed = (await f.reopen().provider.mutate(next)).value;
  assert.equal(completed.state, 'Committed');
  assert.equal(current.name, 'Our rename');
  assert.equal(current.appProperties.existingProperty, 'preserve private');
  assert.equal(current.publicProperties.publicProperty, 'preserve public');
  assert.equal(f.model.calls.filter(call => call.method === 'PATCH').length, 2);
});

test('move refuses shared folders, then moves within private Drive and supports recoverable Trash and restore', async t => {
  const f = fixture(t);
  const source = file('source-file');
  const target = file('folder', { mimeType: FOLDER, shared: true });
  f.remote.set(source.id, source);
  f.remote.set(target.id, target);
  const item = f.store.mapFile(source);
  const folder = f.store.mapFile(target);
  const move = operation('Move', item, { destination: folder.id });
  const review = (await f.provider.mutate(move)).error;
  assert.equal(review.code, 'ConflictRequiresDecision');
  assert.deepEqual(review.details, { notStarted: 'true' });
  assert.equal(f.model.calls.filter(call => call.method === 'PATCH').length, 0);
  target.shared = false;
  assert.equal((await f.provider.mutate(move)).value.state, 'Committed');
  assert.deepEqual(source.parents, [target.id]);
  const moved = (await f.provider.get(item.id)).value;
  source.capabilities.canTrash = false;
  const denyTrash = (await f.provider.mutate(operation('Delete', moved))).error;
  assert.equal(denyTrash.code, 'ProviderCapabilityUnsupported');
  source.capabilities.canTrash = true;
  assert.equal((await f.provider.mutate(operation('Delete', moved))).value.operation, 'Delete');
  const trashed = (await f.provider.list({ trash: true })).items[0];
  assert.deepEqual(trashed.id, item.id);
  assert.equal(source.trashed, true);
  assert.equal((await f.provider.mutate(operation('Restore', trashed))).value.state, 'Committed');
  assert.equal(source.trashed, false);
  assert.deepEqual(source.parents, [target.id]);
  assert.deepEqual((await f.provider.list({ trash: true })).items, []);
  assert.equal(f.model.calls.filter(call => call.method === 'DELETE').length, 0);
});

test('refuses a different actor before Google and maps real upstream read failures without raw provider details', async t => {
  const f = fixture(t);
  const request = operation('CreateFolder', null, { name: 'Wrong owner' });
  request.operation.actorId = 'someone-else';
  assert.equal((await f.provider.mutate(request)).error.code, 'PermissionDenied');
  assert.equal(f.model.calls.length, 0);
  const id = f.store.ensureId('unavailable-file');
  const missing = await f.provider.get(id);
  assert.equal(missing.error.code, 'ItemNotFound');
  assert.equal(missing.error.targetId, id);
});

test('transfer helpers return current provider identity and reject a folder without upload capability', async t => {
  const f = fixture(t);
  const actual = file('transfer-file', { version: '21', size: '79', sha256Checksum: 'fixture-hash' });
  const folder = file('read-only-folder', { mimeType: FOLDER, capabilities: { canAddChildren: false } });
  f.remote.set(actual.id, actual);
  f.remote.set(folder.id, folder);
  const item = f.store.mapFile({ ...actual, version: '20', size: '50' });
  assert.deepEqual(await f.provider.readRaw(item.id), actual);
  assert.equal((await f.provider.readParent(null)).id, 'my-root');
  await assert.rejects(f.provider.readParent(f.store.mapFile(folder).id), { code: 'ProviderCapabilityUnsupported' });
  actual.size = 'incorrect';
  await assert.rejects(f.provider.readRaw(item.id), { code: 'ProviderUnavailable' });
  actual.size = '79';
  actual.version = Number.MAX_SAFE_INTEGER + 1;
  await assert.rejects(f.provider.readRaw(item.id), { code: 'ProviderUnavailable' });
});

test('rename and move refuse observed exact-name collisions without mutating or discarding the intent', async t => {
  const f = fixture(t);
  const source = file('collision-source', { name: "Jacob's notes" });
  const destination = file('collision-folder', { mimeType: FOLDER });
  const occupying = file('collision-occupant', { name: "Jacob's notes", parents: [destination.id] });
  const sibling = file('collision-sibling', { name: 'Existing folder' });
  for (const existing of [source, destination, occupying, sibling]) f.remote.set(existing.id, existing);
  const mapped = f.store.mapFile(source);
  const folder = f.store.mapFile(destination);
  const move = operation('Move', mapped, { destination: folder.id });
  const rename = operation('Rename', mapped, { name: sibling.name });

  for (const request of [rename, move]) {
    const refused = await f.provider.mutate(request);
    assert.equal(refused.error.code, 'NameConflict');
    assert.deepEqual(refused.error.details, { notStarted: 'true' });
    assert.equal(f.store.operation(request.operation.id).stage, 'prepared');
  }
  assert.equal(f.model.calls.filter(call => call.method === 'PATCH').length, 0);
  const exactQueries = f.model.calls.map(call => call.url.searchParams.get('q')).filter(Boolean);
  assert.ok(exactQueries.includes("'collision-folder' in parents and name = 'Jacob" + String.fromCharCode(92) + "'s notes' and trashed = false"));
  assert.equal(f.store.changes(0).items.length, 0);
  assert.deepEqual(source.parents, ['my-root']);

  f.remote.delete(occupying.id);
  const moved = await f.reopen().provider.mutate(move);
  assert.equal(moved.value.state, 'Committed');
  assert.deepEqual(moved.value.itemId, mapped.id);
  assert.deepEqual(source.parents, [destination.id]);
  assert.equal(f.store.changes(0).items.length, 1);
});

test('an incomplete exact-name search never grants a Drive mutation', async t => {
  const f = fixture(t);
  const source = file('unchecked-source');
  f.remote.set(source.id, source);
  f.model.list = () => ({ files: [], incompleteSearch: true });
  const result = await f.provider.mutate(operation('Rename', f.store.mapFile(source), { name: 'Unverified' }));
  assert.equal(result.error.code, 'ProviderUnavailable');
  assert.deepEqual(result.error.details, { notStarted: 'true' });
  assert.equal(f.model.calls.filter(call => call.method === 'PATCH').length, 0);
  assert.equal(source.name, 'unchecked-source');
});

test('rename ignores the selected item but checks later collision pages', async t => {
  const f = fixture(t);
  const source = file('first-match');
  f.remote.set(source.id, source);
  const request = operation('Rename', f.store.mapFile(source), { name: 'Rename me' });
  f.model.list = url => url.searchParams.get('pageToken') === 'next'
    ? { files: [{ id: 'different-match' }] }
    : { files: [{ id: source.id }], nextPageToken: 'next' };

  assert.equal((await f.provider.mutate(request)).error.code, 'NameConflict');
  assert.equal(f.model.calls.filter(call => call.method === 'PATCH').length, 0);
  f.model.list = () => ({ files: [{ id: source.id }] });
  assert.equal((await f.provider.mutate(request)).value.state, 'Committed');
  assert.equal(f.model.calls.filter(call => call.method === 'PATCH').length, 1);
});

test('confirmed Drive edits publish metadata and the original result with exactly one event', async t => {
  const f = fixture(t);
  const source = file('journal-source');
  f.remote.set(source.id, source);
  const before = f.store.mapFile(source);
  const edit = operation('Rename', before, { name: 'New name' });
  f.model.failJournal = true;

  const unresolved = await f.provider.mutate(edit);
  assert.equal(unresolved.error.code, 'ProviderUnavailable');
  assert.equal(unresolved.error.details, null);
  assert.equal(source.name, 'New name', 'provider already applied the change');
  assert.deepEqual(JSON.parse(f.store.byId(before.id).metadata), before, 'do not expose a local revision without its event');
  assert.equal(f.store.operation(edit.operation.id).stage, 'issuing');
  assert.deepEqual(f.store.changes(0), { items: [], nextPageToken: null });

  f.model.failJournal = false;
  const { store, provider } = f.reopen();
  const result = await provider.mutate(edit);
  assert.equal(result.value.state, 'Committed');
  assert.deepEqual(result.value.itemId, before.id);
  const [event] = store.changes(0).items;
  assert.deepEqual(event.operationId, edit.operation.id);
  assert.deepEqual(event.baseRevisionId, before.currentRevisionId);
  assert.deepEqual(event.resultRevisionId, result.value.resultRevisionId);
  assert.equal(event.metadata.name, 'New name');
  assert.deepEqual(await provider.mutate(edit), result);
  assert.equal(store.changes(event.cursor.revision).items.length, 0);
  assert.equal(f.model.calls.filter(call => call.method === 'PATCH').length, 1);
});

test('new folder refuses an observed name collision before allocating or mutating', async t => {
  const f = fixture(t);
  const occupied = file('existing-folder', { name: "Jacob's work", mimeType: FOLDER });
  f.remote.set(occupied.id, occupied);
  const request = operation('CreateFolder', null, { name: occupied.name });
  const result = await f.provider.mutate(request);
  assert.equal(result.error.code, 'NameConflict');
  assert.deepEqual(result.error.details, { notStarted: 'true' });
  assert.equal(f.model.ids, 0);
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 0);
  assert.equal(f.store.changes(0).items.length, 0);
  f.remote.delete(occupied.id);
  const committed = await f.reopen().provider.mutate(request);
  assert.equal(committed.value.state, 'Committed');
  assert.deepEqual(committed.value.itemId, request.operation.itemId);
  assert.deepEqual(await f.provider.mutate(request), committed);
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 1);
});

test('a committed or uncertain new folder blocks another identity when the name index has not caught up', async t => {
  const f = fixture(t);
  f.model.list = () => ({ files: [], incompleteSearch: false });
  const first = operation('CreateFolder', null, { name: 'Lagging search' });
  const alternative = operation('CreateFolder', null, { name: 'Lagging search' });

  f.model.lost = true;
  const uncertain = await f.provider.mutate(first);
  assert.equal(uncertain.error.code, 'ProviderUnavailable');
  assert.equal(f.store.operation(first.operation.id).stage, 'issuing');
  assert.equal((await f.reopen().provider.mutate(alternative)).error.code, 'NameConflict');
  const settled = await f.reopen().provider.mutate(first);
  assert.equal(settled.value.state, 'Committed');
  const sameName = await f.reopen().provider.mutate(alternative);
  assert.equal(sameName.error.code, 'NameConflict');
  assert.deepEqual(sameName.error.details, { notStarted: 'true' });
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 1);
  assert.equal(f.store.changes(0).items.length, 1);
  assert.equal(f.remote.size, 2, 'only root and original folder exist');

  // The scoped evidence is a check of the current file, not a permanent
  // reservation: once the actual previous folder is trashed the name is free.
  const committedRow = f.store.operation(first.operation.id);
  f.remote.get(committedRow.provider_id).trashed = true;
  const reopened = await f.reopen().provider.mutate(alternative);
  assert.equal(reopened.value.state, 'Committed');
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 2);
  assert.equal(f.store.changes(0).items.length, 2);

});

test('independent concurrent folder intents cannot both dispatch against identical preflight answers', async t => {
  const f = fixture(t);
  // Both handles use the same underlying Durable Object SQLite fixture and see an empty advisory provider listing.
  f.model.list = () => ({ files: [] });
  const work = [
    f.provider.mutate(operation('CreateFolder', null, { name: 'Parallel' })),
    f.reopen().provider.mutate(operation('CreateFolder', null, { name: 'Parallel' })),
  ];
  const results = await Promise.all(work);
  assert.equal(results.filter(result => result.value?.state === 'Committed').length, 1);
  assert.equal(results.filter(result => result.error?.code === 'NameConflict').length, 1);
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 1);
  assert.equal(f.store.changes(0).items.length, 1);
});

test('two callers recovering the same preallocated folder cannot reset or resend that admission', async t => {
  const f = fixture(t, { limits: { unitsPerMinute: 160 } });
  const original = operation('CreateFolder', null, { name: 'Prepared retry target' });
  const lowered = await f.provider.mutate(original);
  assert.equal(lowered.error.code, 'QuotaExceeded');
  assert.equal(f.store.operation(original.operation.id).provider_id, 'generated-1');
  f.model.now += 120_000;

  const [one, two] = await Promise.all([f.reopen().provider.mutate(original), f.reopen().provider.mutate(original)]);
  assert.ok([one, two].some(result => result.value?.state === 'Committed'));
  const receipt = await f.reopen().provider.mutate(original);
  assert.deepEqual(receipt.value.itemId, original.operation.itemId);
  assert.equal(f.store.operation(original.operation.id).provider_id, 'generated-1');
  assert.equal(f.model.calls.filter(call => call.method === 'POST').length, 1);
  assert.equal(f.store.changes(0).items.length, 1);
});
