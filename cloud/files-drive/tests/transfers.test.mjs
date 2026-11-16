import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import test from 'node:test';
import { DriveQuota } from '../src/quota.mjs';
import { GoogleTransport, CHUNK_BYTES, EXPORT_BYTES } from '../src/google-transport.mjs';
import { DriveProvider } from '../src/provider.mjs';
import { DriveStore } from '../src/store.mjs';
import { DriveTransfers } from '../src/transfers.mjs';

const sessionDigest = 'test-session-digest-a3456789012345678901';
const at = '2026-11-15T12:00:00Z';
const uploadUri = 'https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=one-use-provider-secret';
const newId = 'allocated-google-id';
const root = { id: 'google-root', name: 'My Drive', mimeType: 'application/vnd.google-apps.folder',
  version: '1', createdTime: at, modifiedTime: at, ownedByMe: true, trashed: false, capabilities: { canAddChildren: true } };

function fixture(t, { limits, wait } = {}) {
  const db = new DatabaseSync(':memory:');
  t.after(() => db.close());
  let nesting = 0;
  const storage = {
    sql: { exec(sql, ...args) { const rows = db.prepare(sql).all(...args); return { toArray: () => rows }; } },
    transactionSync(work) {
      if (nesting++ > 0) { try { return work(); } finally { nesting--; } }
      db.exec('BEGIN IMMEDIATE');
      try { const result = work(); db.exec('COMMIT'); return result; }
      catch (error) { db.exec('ROLLBACK'); throw error; }
      finally { nesting--; }
    },
    async sync() {},
  };
  let now = Date.parse(at);
  const clock = () => now;
  const key = new Uint8Array(32).fill(3);
  const store = new DriveStore(storage, '9to1-owner', { now: clock });
  const quota = new DriveQuota(storage, { now: clock, limits });
  const files = new Map();
  const calls = [];
  const behavior = {};
  const oauth = { async getAccessToken() { return 'owner-token-test'; } };
  const google = new GoogleTransport(quota, {
    now: clock, onSafetyFault: async () => { behavior.safetyHeld = true; },
    isSafetyHeld: async () => behavior.safetyHeld === true,
    async fetch(url, request) {
      const { pathname, searchParams } = new URL(url);
      calls.push({ pathname, searchParams, request });
      assert.equal(new Headers(request.headers).get('authorization'), 'Bearer owner-token-test');
      const overridden = await behavior.respond?.(pathname, searchParams, request);
      if (overridden !== undefined) return overridden;
      if (pathname === '/drive/v3/files/root' || pathname === '/drive/v3/files/google-root') return Response.json(root);
      if (pathname === '/drive/v3/files/generateIds') return Response.json({ ids: [newId] });
      if (pathname === '/drive/v3/files') return Response.json({ files: behavior.nameCollision ? [{ id: 'existing' }] : [] });
      const file = files.get(pathname.split('/').at(-1));
      if (file && !searchParams.has('alt')) return Response.json(file);
      if (pathname === '/drive/v3/files/' + newId) return Response.json({}, { status: 404 });
      if (pathname === '/upload/drive/v3/files' && request.method === 'POST') {
        if (searchParams.get('uploadType') === 'multipart') {
          const body = new TextDecoder().decode(request.body);
          const metadata = JSON.parse(body.split('\r\n\r\n')[1].split('\r\n')[0]);
          const file = receipt(metadata, 0);
          files.set(newId, file);
          return Response.json(file);
        }
        behavior.uploadMetadata = JSON.parse(request.body);
        return new Response(null, { headers: { location: behavior.uploadLocation ?? uploadUri } });
      }
      assert.fail('Unexpected controlled provider request: ' + request.method + ' ' + pathname);
    },
  });
  const provider = new DriveProvider(store, oauth, google, { now: clock });
  const reopen = () => new DriveTransfers(new DriveStore(storage, '9to1-owner', { now: clock }), oauth, google, provider,
    { encryptionKey: key, quotaStatus: () => quota.status(), now: clock, ...(wait ? { wait } : {}) });
  return { store, quota, files, calls, behavior, reopen, transfer: reopen(), advance: ms => { now += ms; } };
}

function receipt(metadata, size) {
  return { ...metadata, size: String(size), version: '1', createdTime: at, modifiedTime: at,
    ownedByMe: true, trashed: false, capabilities: { canDownload: true } };
}

function intent(sizeBytes = 3, name = 'A file.txt') {
  return { operationId: { value: crypto.randomUUID() }, parentId: null, name, sizeBytes, contentType: 'text/plain' };
}

test('durable upload session keeps canonical IDs and its URL out of acknowledgements and persisted plaintext', async t => {
  const f = fixture(t);
  const input = intent();
  const first = await f.transfer.beginUpload(input);
  assert.equal(first.uploadedBytes, 0);
  assert.equal(first.chunkBytes, CHUNK_BYTES);
  assert.equal(first.state, 'Running');
  assert.equal(f.store.googleId(first.itemId), newId);
  const stored = JSON.stringify(f.store.findTransfer(input.operationId));
  assert.doesNotMatch(stored, /one-use-provider-secret|uploadType=resumable|upload_id=/);
  assert.doesNotMatch(JSON.stringify(first), /one-use-provider-secret|www\.googleapis/);
  assert.deepEqual(await f.reopen().beginUpload(input), first);
  await assert.rejects(f.reopen().beginUpload({ ...input, name: 'Other file' }), { code: 'InvalidState' });
  assert.equal(f.calls.filter(c => c.request.method === 'POST').length, 1);
  await assert.rejects(f.transfer.uploadChunk(first.transferId, 1, new Uint8Array(2)), { code: 'InvalidState' });
  assert.ok(f.calls.every(c => c.request.method !== 'PUT'));
});

test('lost final PUT reply reconciles via metered status with no second data submission or new file', async t => {
  const f = fixture(t);
  const first = await f.transfer.beginUpload(intent());
  const file = receipt(f.behavior.uploadMetadata, 3);
  let dataAttempts = 0;
  let probes = 0;
  f.behavior.respond = (path, args, request) => {
    if (request.method !== 'PUT') return;
    assert.equal(path, '/upload/drive/v3/files');
    assert.equal(args.get('upload_id'), 'one-use-provider-secret');
    if (request.headers['Content-Range'] === 'bytes */3') { probes++; return Response.json(file); }
    dataAttempts++;
    assert.equal(request.headers['Content-Range'], 'bytes 0-2/3');
    assert.deepEqual([...request.body], [1, 2, 3]);
    f.files.set(newId, file);
    throw new Error('Lost response after completed upload.');
  };
  await assert.rejects(f.transfer.uploadChunk(first.transferId, 0, Uint8Array.of(1, 2, 3)));
  assert.equal((await f.quota.status()).uploadBytesPerDay.used, 3);
  await assert.rejects(f.reopen().uploadChunk(first.transferId, 0, Uint8Array.of(1, 2, 3)), { code: 'TransferInterrupted' });
  const done = await f.reopen().uploadStatus(first.transferId);
  assert.equal(done.state, 'Completed');
  assert.equal(done.uploadedBytes, 3);
  assert.deepEqual(done.itemId, first.itemId);
  assert.deepEqual(await f.reopen().cancelUpload(first.transferId), done);
  assert.equal(dataAttempts, 1);
  assert.equal(probes, 1);
  assert.equal((await f.quota.status()).uploadBytesPerDay.used, 3);
});

test('only Google range acknowledgement advances partial bytes, and local cancellation never deletes files', async t => {
  const f = fixture(t);
  const first = await f.transfer.beginUpload(intent(2 * 256 * 1024 + 2));
  f.behavior.respond = (_path, _query, request) => {
    if (request.method === 'PUT') return new Response(null, { status: 308, headers: { range: 'bytes=0-262143' } });
  };
  const updated = await f.transfer.uploadChunk(first.transferId, 0, new Uint8Array(2 * 256 * 1024));
  assert.equal(updated.uploadedBytes, 256 * 1024);
  assert.equal((await f.quota.status()).uploadBytesPerDay.used, 2 * 256 * 1024);
  const removed = await f.transfer.cancelUpload(first.transferId);
  assert.equal(removed.state, 'Cancelled');
  assert.ok(f.calls.every(c => c.request.method !== 'DELETE'));
  assert.equal(f.store.transfer(first.transferId).session, undefined);
});

test('new uploads above the app file limit never dispatch and the exact size limit can start', async t => {
  const defaults = fixture(t);
  const large = intent(100_000_000_001);
  await assert.rejects(defaults.transfer.beginUpload(large), {
    status: 413, code: 'UploadTooLarge', message: 'The Files limit per upload is 100 GB. Choose a smaller file.',
    notStarted: true, retryAt: null,
  });
  assert.equal(defaults.calls.length, 0);
  assert.equal(defaults.store.findTransfer(large.operationId), null);

  const f = fixture(t, { limits: { uploadBytesPerDay: 3 } });
  await assert.rejects(f.transfer.beginUpload(intent(4)), {
    status: 413, code: 'UploadTooLarge', message: 'The Files limit per upload is 3 bytes. Choose a smaller file.',
  });
  assert.equal(f.calls.length, 0);
  const allowed = intent(3);
  const progress = await f.transfer.beginUpload(allowed);
  assert.equal(progress.totalBytes, 3);
  const count = f.calls.length;
  assert.deepEqual(await f.reopen().beginUpload(allowed), progress);
  assert.equal(f.calls.length, count);
});

test('quota refusal cannot dispatch a chunk, and name conflict cannot start an upload', async t => {
  const f = fixture(t, { limits: { uploadBytesPerDay: 3 } });
  f.behavior.nameCollision = true;
  await assert.rejects(f.transfer.beginUpload(intent()), { code: 'NameConflict' });
  assert.equal(f.calls.filter(c => c.request.method === 'POST').length, 0);
  f.behavior.nameCollision = false;
  const first = await f.transfer.beginUpload(intent());
  await f.quota.reserve({ id: 'other-upload', kind: 'upload', uploadBytes: 1 });
  await assert.rejects(f.transfer.uploadChunk(first.transferId, 0, Uint8Array.of(1, 2, 3)), { notStarted: true });
  assert.equal(f.calls.filter(c => c.request.method === 'PUT').length, 0);
  assert.equal((await f.transfer.uploadStatus(first.transferId)).uploadedBytes, 0);
});

test('concurrent same-name uploads cannot both pass admission, including a reopened transfer service', async t => {
  const f = fixture(t);
  let start;
  let resume;
  const nameLookup = new Promise(resolve => { start = resolve; });
  const paused = new Promise(resolve => { resume = resolve; });
  let lookups = 0;
  f.behavior.respond = async (path, args) => {
    if (path !== '/drive/v3/files' || !args.has('q')) return;
    lookups++;
    start();
    await paused;
    return Response.json({ files: [] });
  };
  const first = f.transfer.beginUpload(intent());
  await nameLookup;
  try {
    await assert.rejects(f.reopen().beginUpload(intent()), { code: 'NameConflict' });
    assert.equal(lookups, 1);
  } finally { resume(); }
  assert.equal((await first).state, 'Running');
  assert.equal(f.calls.filter(c => c.request.method === 'POST').length, 1);
});

test('zero-length upload requires real creation receipt and refuses an external session URL', async t => {
  const f = fixture(t);
  const done = await f.transfer.beginUpload(intent(0, 'empty.bin'));
  assert.equal(done.state, 'Completed');
  assert.equal(f.files.get(newId).size, '0');
  assert.equal((await f.quota.status()).uploadBytesPerDay.used, 0);
  const unsafe = fixture(t);
  unsafe.behavior.uploadLocation = 'https://www.googleapis.com.attacker.invalid/upload/drive/v3/files?uploadType=resumable&upload_id=token';
  await assert.rejects(unsafe.transfer.beginUpload(intent()), { code: 'TransferInterrupted' });
  assert.ok(unsafe.calls.every(c => c.request.method !== 'PUT'));
});

test('upload initialization persists only bounded Google status and redacted location shape', async t => {
  const f = fixture(t);
  const input = intent();
  f.behavior.respond = (path, _params, request) => {
    if (path !== '/upload/drive/v3/files' || request.method !== 'POST') return;
    return Response.json({
      error: { status: 'INVALID_ARGUMENT', errors: [{ reason: 'badRequest', message: 'sensitive-provider-echo' }] },
    }, { status: 400, headers: { Location: 'https://www.googleapis.com/upload/drive/v3/files/sensitive-path' +
      '?uploadType=resumable&upload_id=sensitive.value&fields=sensitive-value&extraParameter=sensitive-value' +
      '&sensitive-parameter=sensitive-value#sensitive-fragment' } });
  };
  await assert.rejects(f.transfer.beginUpload(input), { code: 'TransferInterrupted' });
  const entry = f.store.findTransfer(input.operationId);
  assert.equal(entry.phase, 'creating');
  assert.equal(entry.session, undefined);
  assert.deepEqual(entry.initDiagnostic, {
    outcome: 'Received', httpStatus: 400, bodyJson: true, providerReason: 'badRequest', providerStatus: 'INVALID_ARGUMENT',
    location: { present: true, parsed: true, origin: 'https://www.googleapis.com', path: '/upload/drive/v3/files/[redacted]',
      queryKeys: ['extraParameter', 'fields', 'uploadType', 'upload_id'], unknownKeyCount: 2, credentialsPresent: false, fragmentPresent: true,
      uploadTypeMatches: true, uploadIdMatches: false, fieldsPresent: true, fieldsMatch: false },
  });
  assert.ok(!JSON.stringify(entry).includes('sensitive-'));
  assert.ok(!JSON.stringify(entry).includes('sensitive.value'));
});

test('an unresolved initialization can resume with its original ID only through the explicit POST flow', async t => {
  const f = fixture(t);
  const input = intent();
  f.behavior.uploadLocation = 'https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=private.dot';
  await assert.rejects(f.transfer.beginUpload(input), { code: 'TransferInterrupted' });
  const entry = f.store.findTransfer(input.operationId);
  assert.equal(entry.initDiagnostic.outcome, 'LocationRefused');
  assert.equal(entry.initDiagnostic.httpStatus, 200);
  assert.equal(entry.initDiagnostic.location.uploadIdMatches, false);
  assert.ok(!JSON.stringify(entry).includes('private.dot'));

  const before = f.calls.length;
  await assert.rejects(f.reopen().uploadStatus(entry.transferId), { code: 'TransferInterrupted' });
  assert.deepEqual(f.calls.slice(before).map(call => [call.request.method, call.pathname]),
    [['GET', '/drive/v3/files/' + newId]]);
  const credential = 'fixture-private-credential._+/%=';
  const location = new URL(uploadUri);
  location.searchParams.set('session_crd', credential);
  f.behavior.uploadLocation = location.href;
  const resumed = await f.reopen().beginUpload(input);
  assert.equal(resumed.transferId.value, entry.transferId);
  assert.equal(resumed.itemId.value, entry.itemId);
  assert.equal(resumed.uploadedBytes, 0);
  assert.deepEqual(f.calls.slice(before + 1).map(call => [call.request.method, call.pathname]),
    [['GET', '/drive/v3/files/' + newId], ['POST', '/upload/drive/v3/files']]);
  const posts = f.calls.filter(call => call.request.method === 'POST');
  assert.deepEqual(posts.map(call => JSON.parse(call.request.body).id), [newId, newId]);
  const stored = f.store.transfer(entry.transferId);
  assert.equal(stored.initDiagnostic.outcome, 'SessionReady');
  assert.ok(stored.initDiagnostic.location.queryKeys.includes('session_crd'));
  assert.ok(!JSON.stringify(stored).includes('fixture-private-credential'));
  assert.ok(!JSON.stringify(resumed).includes('fixture-private-credential'));
  assert.equal((await f.quota.status()).uploadBytesPerDay.used, 0);
  f.behavior.respond = (path, args, request) => {
    if (request.method !== 'PUT') return;
    assert.equal(path, '/upload/drive/v3/files');
    assert.equal(args.get('session_crd'), credential);
    assert.equal(args.getAll('session_crd').length, 1);
    return Response.json(receipt(f.behavior.uploadMetadata, 3));
  };
  const finished = await f.reopen().uploadChunk(resumed.transferId, 0, Uint8Array.of(1, 2, 3));
  assert.equal(finished.state, 'Completed');
});

test('resumable credentials do not weaken host, path or query validation', async t => {
  for (const variant of [
    uploadUri + '&session_crd=one&session_crd=two',
    uploadUri + '&session_crd=',
    uploadUri + '&session_crd=invalid%0Acontrol',
    uploadUri + '&session_crd=' + 'x'.repeat(4097),
    uploadUri.replace('www.googleapis.com', 'www.googleapis.com.attacker.invalid') + '&session_crd=credential',
    uploadUri.replace('/files?', '/files/another?') + '&session_crd=credential',
    uploadUri + '&session_crd=credential&another_key=extra',
  ]) {
    const f = fixture(t);
    f.behavior.uploadLocation = variant;
    const input = intent();
    await assert.rejects(f.transfer.beginUpload(input), { code: 'TransferInterrupted' });
    const stored = f.store.findTransfer(input.operationId);
    assert.equal(stored.initDiagnostic.outcome, 'LocationRefused');
    assert.equal(stored.session, undefined);
    assert.ok(f.calls.every(call => call.request.method !== 'PUT'));
  }
});

function rangedFile(f, bytes, change = {}) {
  const file = receipt({ id: 'google-data', parents: [root.id], name: 'A file.txt', mimeType: 'text/plain',
    md5Checksum: createHash('md5').update(bytes).digest('hex') }, bytes.byteLength);
  const item = f.store.mapFile({ ...file, ...change });
  f.files.set(file.id, { ...file, ...change });
  return item.id;
}

function rangeResponder(bytes, callback) {
  return (_path, searchParams, request) => {
    if (searchParams.get('alt') !== 'media') return;
    const [start, end] = request.headers.Range.slice('bytes='.length).split('-').map(Number);
    callback?.(start, end);
    return new Response(bytes.subarray(start, end + 1), { status: 206,
      headers: { 'Content-Range': 'bytes ' + start + '-' + end + '/' + bytes.byteLength } });
  };
}

test('download ticket checks owner session, version and checksum for each range and cannot replay', async t => {
  const f = fixture(t);
  const bytes = new Uint8Array(CHUNK_BYTES + 3).fill(172);
  const itemId = rangedFile(f, bytes);
  let checks = 0;
  let count = 0;
  f.behavior.respond = rangeResponder(bytes, () => { assert.ok(checks > count * 2); count++; });
  const ticket = await f.transfer.prepareDownload({ itemId }, sessionDigest);
  assert.equal(ticket.url, '/api/files/v1/downloads/' + ticket.transferId.value + '/content');
  await assert.rejects(f.transfer.downloadContent(ticket.transferId, 'another-session', { confirmSession() {} }), { code: 'ItemNotFound' });
  const response = await f.transfer.downloadContent(ticket.transferId, sessionDigest, { async confirmSession() { checks++; } });
  assert.match(response.headers.get('content-disposition'), /^attachment; filename="A file.txt"/);
  assert.deepEqual(new Uint8Array(await response.arrayBuffer()), bytes);
  assert.equal(count, 2);
  assert.equal(checks, 4);
  assert.equal(f.store.transfer(ticket.transferId).state, 'Completed');
  await assert.rejects(f.reopen().downloadContent(ticket.transferId, sessionDigest, { confirmSession() {} }), { code: 'ItemNotFound' });
});

test('a two-range download waits through the app minute cap before sending its next range', async t => {
  const waits = [];
  const f = fixture(t, { limits: { unitsPerMinute: 225 }, wait: async (ms, signal) => {
    signal.throwIfAborted();
    waits.push({ ms, count: f.calls.length });
    assert.equal(f.calls.filter(call => call.searchParams.get('alt') === 'media').length, 1);
    assert.equal((await f.quota.status()).unitsPerMinute.remaining, 0);
    f.advance(ms);
  } });
  const bytes = new Uint8Array(CHUNK_BYTES + 1).fill(23);
  const itemId = rangedFile(f, bytes);
  let sessions = 0;
  f.behavior.respond = rangeResponder(bytes);
  const ticket = await f.transfer.prepareDownload({ itemId }, sessionDigest);
  const response = await f.transfer.downloadContent(ticket.transferId, sessionDigest, { confirmSession() { sessions++; } });
  assert.deepEqual(new Uint8Array(await response.arrayBuffer()), bytes);
  assert.deepEqual(waits.map(wait => wait.ms), [65_000]);
  assert.equal(f.calls.filter(call => call.searchParams.get('alt') === 'media').length, 2);
  assert.equal(f.calls[waits[0].count].searchParams.has('alt'), false, 'file version is checked again after waiting');
  assert.equal(sessions, 5);
});

test('a post-range metadata pause never sends the already consumed range again', async t => {
  let waited = 0;
  const f = fixture(t, { limits: { unitsPerMinute: 215 }, wait: async ms => { waited++; f.advance(ms); } });
  const bytes = new Uint8Array(CHUNK_BYTES + 1).fill(3);
  const id = rangedFile(f, bytes);
  const offsets = [];
  f.behavior.respond = rangeResponder(bytes, start => offsets.push(start));
  const ticket = await f.transfer.prepareDownload({ itemId: id }, sessionDigest);
  const response = await f.transfer.downloadContent(ticket.transferId, sessionDigest, { confirmSession() {} });
  assert.deepEqual(new Uint8Array(await response.arrayBuffer()), bytes);
  assert.equal(waited, 1);
  assert.deepEqual(offsets, [0, CHUNK_BYTES]);
});

test('canceling a queued minute wait settles the download without more provider work', async t => {
  const waiting = Promise.withResolvers();
  const f = fixture(t, { limits: { unitsPerMinute: 225 },
    wait: (_ms, signal) => new Promise((resolve, reject) => {
      signal.addEventListener('abort', () => reject(signal.reason), { once: true });
      waiting.resolve();
    }),
  });
  const bytes = new Uint8Array(CHUNK_BYTES + 1);
  const id = rangedFile(f, bytes);
  f.behavior.respond = rangeResponder(bytes);
  const ticket = await f.transfer.prepareDownload({ itemId: id }, sessionDigest);
  const owner = new AbortController();
  const response = await f.transfer.downloadContent(ticket.transferId, sessionDigest, { confirmSession() {}, signal: owner.signal });
  const reader = response.body.getReader();
  assert.equal((await reader.read()).value.byteLength, CHUNK_BYTES);
  const blocked = reader.read();
  await waiting.promise;
  owner.abort();
  await assert.rejects(blocked, { name: 'AbortError' });
  assert.equal(f.store.transfer(ticket.transferId).state, 'Cancelled');
  assert.equal(f.calls.filter(call => call.searchParams.get('alt') === 'media').length, 1);
});

test('daily estimates reject excess before media and are checked again when a ticket starts', async t => {
  const bytes = new Uint8Array(CHUNK_BYTES + 1);
  const tooLarge = fixture(t, { limits: { unitsPerDay: 430 } });
  await assert.rejects(tooLarge.transfer.prepareDownload({ itemId: rangedFile(tooLarge, bytes) }, sessionDigest),
    { code: 'QuotaExceeded', retryAt: null, notStarted: true });
  assert.ok(tooLarge.calls.every(call => !call.searchParams.has('alt')));
  const nowFull = fixture(t, { limits: { unitsPerDay: 500 } });
  const ticket = await nowFull.transfer.prepareDownload({ itemId: rangedFile(nowFull, bytes) }, sessionDigest);
  await nowFull.quota.reserve({ id: 'other-work', kind: 'list' });
  await assert.rejects(nowFull.transfer.downloadContent(ticket.transferId, sessionDigest, { confirmSession() {} }),
    { code: 'QuotaExceeded', notStarted: true });
  assert.ok(nowFull.calls.every(call => !call.searchParams.has('alt')));
});

test('Google rate-limit and unknown media outcomes never auto-retry', async t => {
  for (const providerReply of [
    () => new Response(JSON.stringify({ error: { errors: [{ reason: 'dailyLimitExceeded' }] } }), { status: 429 }),
    () => { throw new Error('Lost provider response'); },
  ]) {
    const f = fixture(t, { wait: async () => assert.fail('Only pre-dispatch app minute limits may wait') });
    const id = rangedFile(f, Uint8Array.of(1, 2, 3));
    const ticket = await f.transfer.prepareDownload({ itemId: id }, sessionDigest);
    f.behavior.respond = (_path, args) => args.get('alt') === 'media' ? providerReply() : undefined;
    await assert.rejects(f.transfer.downloadContent(ticket.transferId, sessionDigest, { confirmSession() {} }));
    assert.equal(f.calls.filter(call => call.searchParams.get('alt') === 'media').length, 1);
  }
});

test('range mismatch, revision change, session loss and digest mismatch refuse or abort downloads', async t => {
  const bytes = new Uint8Array(CHUNK_BYTES + 3).fill(4);
  for (const scenario of ['range', 'revision', 'session', 'checksum']) {
    const f = fixture(t);
    const itemId = rangedFile(f, bytes);
    const responseBytes = scenario === 'checksum' ? new Uint8Array(bytes.length).fill(5) : bytes;
    let checks = 0;
    let ranges = 0;
    const good = rangeResponder(responseBytes, start => {
      ranges++;
      if (scenario === 'revision' && start > 0) f.files.get('google-data').version = '2';
    });
    f.behavior.respond = (path, args, request) => {
      if (scenario === 'range' && args.get('alt') === 'media') return new Response('wrong', { status: 206 });
      return good(path, args, request);
    };
    const ticket = await f.transfer.prepareDownload({ itemId }, sessionDigest);
    const download = f.transfer.downloadContent(ticket.transferId, sessionDigest, {
      confirmSession() { checks++; return scenario !== 'session' || checks < 3; },
    });
    if (scenario === 'range') await assert.rejects(download, { code: 'IntegrityFailed' });
    else {
      const response = await download;
      await assert.rejects(response.arrayBuffer());
      assert.equal(ranges, scenario === 'session' ? 1 : 2);
    }
    assert.equal(f.store.transfer(ticket.transferId).state, 'Failed');
  }
});

test('export allowlist, reservation and ticket expiry are enforced before bytes reach the owner', async t => {
  const f = fixture(t);
  const document = rangedFile(f, Uint8Array.of(1, 2, 3), { mimeType: 'application/vnd.google-apps.document' });
  const pdf = 'application/pdf';
  await assert.rejects(f.transfer.prepareDownload({ itemId: document, exportMime: 'text/html' }, sessionDigest),
    { code: 'ProviderCapabilityUnsupported' });
  const expires = await f.transfer.prepareDownload({ itemId: document, exportMime: pdf }, sessionDigest);
  f.advance(120001);
  await assert.rejects(f.transfer.downloadContent(expires.transferId, sessionDigest, { confirmSession() {} }), { code: 'ItemNotFound' });
  f.behavior.respond = path => path.endsWith('/export') ? new Response('pdf-bytes') : undefined;
  const before = (await f.quota.status()).downloadBytesPerDay.used;
  const ticket = await f.transfer.prepareDownload({ itemId: document, exportMime: pdf }, sessionDigest);
  const response = await f.transfer.downloadContent(ticket.transferId, sessionDigest, { confirmSession() {} });
  assert.equal(await response.text(), 'pdf-bytes');
  assert.match(response.headers.get('content-disposition'), /A file\.txt\.pdf/);
  assert.ok((await f.quota.status()).downloadBytesPerDay.used - before >= EXPORT_BYTES);
});
