import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { DriveProvider } from '../src/provider.mjs';
import { DriveStore } from '../src/store.mjs';
import { DriveQuota } from '../src/quota.mjs';
import { GoogleTransport } from '../src/google-transport.mjs';

const T0 = Date.UTC(2026, 10, 17);
const FOLDER = 'application/vnd.google-apps.folder';
const owner = '15d642d8-0e7d-4cbe-ae68-2e289dc9e218';
// Smallest valid PNG (1×1 transparent) for content-type and byte identity checks.
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64');

function file(id, properties = {}) {
  return {
    id, name: id, mimeType: 'text/plain', version: '1', createdTime: new Date(T0).toISOString(),
    modifiedTime: new Date(T0).toISOString(), size: '10', parents: ['my-root'], ownedByMe: true,
    shared: false, trashed: false, explicitlyTrashed: false,
    capabilities: { canAddChildren: true, canRemoveChildren: true, canRename: true, canMoveItemWithinDrive: true,
      canTrash: true, canUntrash: true, canDownload: true }, ...properties,
  };
}

function fileShape(value) {
  return { ...value, driveId: null, owners: [], appProperties: {} };
}

function fixture(t, behaviour = {}) {
  const db = new DatabaseSync(':memory:');
  t.after(() => db.close());
  const storage = {
    sql: { exec(query, ...args) { return { toArray: () => db.prepare(query).all(...args) }; } },
    transactionSync(work) {
      db.exec('BEGIN IMMEDIATE');
      try { const result = work(); db.exec('COMMIT'); return result; } catch (error) { db.exec('ROLLBACK'); throw error; }
    },
    async sync() {},
  };
  const calls = [];
  const root = file('my-root', { mimeType: FOLDER, parents: [] });
  const document = file('thumb-doc');
  const remote = new Map([[root.id, root], [document.id, document]]);
  const googleFetch = async raw => {
    const url = new URL(raw);
    calls.push(url.pathname + (url.search ?? ''));
    const id = /^\/drive\/v3\/files\/([^/]+)\/thumbnail$/.exec(url.pathname)?.[1];
    if (id) assert.equal(url.searchParams.get('pageSize'), 'M');
    if (url.pathname === '/drive/v3/files/root') return Response.json(fileShape(root));
    if (behaviour.noThumb) return new Response(JSON.stringify({ error: { message: 'noThumbnail' } }),
      { status: 404, headers: { 'content-type': 'application/json' } });
    if (id === 'thumb-doc') return new Response(behaviour.bytes ?? PNG,
      { status: behaviour.status ?? 200, headers: { 'content-type': behaviour.type ?? 'image/png' } });
    if (id === 'weak-etag-source') return new Response('x', { status: 404, headers: { 'content-type': 'application/json' } });
    if (remote.has(id)) return new Response(JSON.stringify({ error: { message: 'noThumbnail' } }),
      { status: 404, headers: { 'content-type': 'application/json' } });
    return new Response('{}', { status: 404, headers: { 'content-type': 'application/json' } });
  };
  const store = new DriveStore(storage, owner, { now: () => T0 });
  const quota = new DriveQuota(storage, { now: () => T0 });
  const oauth = { async getAccessToken() { return 'local-owner-token'; } };
  const transport = new GoogleTransport(quota, {
    fetch: googleFetch, now: () => T0, onSafetyFault: async () => { throw new Error('unexpected safety hold'); },
    isSafetyHeld: async () => false,
  });
  const provider = new DriveProvider(store, oauth, transport, { conditionalWrites: true, now: () => T0 });
  const mapped = store.mapFile(document);
  return { provider, store, remote, calls, root, document, canonical: mapped.id.value };
}

test('the same-origin thumbnail proxy serves bounded metered image bytes to the owner', async t => {
  const f = fixture(t);
  const image = await f.provider.thumbnail(f.canonical, undefined);
  assert.equal(image.contentType, 'image/png');
  assert.deepEqual(Buffer.from(image.bytes), PNG);
  assert.ok(f.calls.some(call => call.startsWith('/drive/v3/files/thumb-doc/thumbnail?pageSize=M')));
});

test('thumbnail refusals and non-image provider answers fail closed without inventing art', async t => {
  const missing = fixture(t, { noThumb: true });
  await assert.rejects(missing.provider.thumbnail(missing.canonical, undefined),
    error => error.code === 'ThumbnailUnavailable');
  const wrongType = fixture(t, { type: 'image/svg+xml' });
  await assert.rejects(wrongType.provider.thumbnail(wrongType.canonical, undefined),
    error => error.code === 'ProviderUnavailable');
  const oversized = fixture(t, { bytes: Buffer.alloc(64 * 1024 + 1) });
  await assert.rejects(oversized.provider.thumbnail(oversized.canonical, undefined));
});

test('thumbnail reads demand canonical identities and mapped provider ids only', async t => {
  const f = fixture(t);
  await assert.rejects(f.provider.thumbnail('not-a-guid', undefined));
  await assert.rejects(f.provider.thumbnail('00000000-0000-4000-8000-000000000000', undefined),
    error => error.code === 'ProviderUnavailable' || error.code === 'InvalidState' || error.code === 'ItemNotFound');
});
