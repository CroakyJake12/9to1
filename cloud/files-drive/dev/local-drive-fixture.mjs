// Local development fixture only. A stateful in-memory stand-in for the
// Google endpoints the Files Worker calls: oauth2 token/revoke, certificates,
// and the Drive v2/v3 metadata, resumable upload, media and export surfaces.
// It holds no real credentials and cannot reach Google; every byte stays on
// this machine under the caller's disposable fixture directory.
import { createHash, createSign, generateKeyPairSync, randomUUID } from 'node:crypto';
import { SignJWT, exportJWK } from 'jose';

const FOLDER = 'application/vnd.google-apps.folder';
const SCOPES = 'openid email https://www.googleapis.com/auth/drive';

function b64urlJson(value) {
  return Buffer.from(JSON.stringify(value)).toString('base64url');
}

function fileShape(file) {
  const { bytes, ...rest } = file;
  return {
    id: file.id, name: file.name, mimeType: file.mimeType, version: file.version,
    createdTime: file.createdTime, modifiedTime: file.modifiedTime,
    ...(file.bytes === null ? {} : { size: String(file.bytes.byteLength) }),
    ...(file.mimeType === FOLDER ? {} : { size: String(file.size ?? file.bytes?.byteLength ?? 0),
      md5Checksum: file.md5Checksum }),
    parents: [...file.parents], driveId: file.driveId, ownedByMe: file.ownedByMe, shared: file.shared,
    trashed: file.trashed, explicitlyTrashed: file.explicitlyTrashed,
    capabilities: { canAddChildren: true, canRemoveChildren: true, canRename: true, canMoveItemWithinDrive: true,
      canTrash: true, canUntrash: true, canDownload: file.mimeType === FOLDER ? undefined : true },
    owners: [], appProperties: { ...file.appProperties },
  };
}

function strongTag(file) { return JSON.stringify(file.id + ':' + file.version); }

function v2Shape(file) {
  const shape = fileShape(file);
  const properties = [
    ...Object.entries(file.appProperties ?? {}).map(([key, value]) => ({ key, value, visibility: 'PRIVATE' })),
  ];
  return {
    id: shape.id, version: shape.version, etag: strongTag(file),
    title: shape.name, modifiedDate: shape.modifiedTime,
    parents: shape.parents.map(id => ({ id })), labels: { trashed: shape.trashed },
    explicitlyTrashed: shape.explicitlyTrashed, properties,
  };
}

export function createDriveFixture({ projectId, clientId, ownerSub, ownerEmail }) {
  const { privateKey, publicKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
  const root = {
    id: 'localDriveRoot' + randomUUID().replaceAll('-', '').slice(0, 12),
    name: 'My Drive', mimeType: FOLDER, version: '1',
    createdTime: new Date().toISOString(), modifiedTime: new Date().toISOString(),
    parents: [], driveId: null, ownedByMe: true, shared: false,
    trashed: false, explicitlyTrashed: false, appProperties: {}, bytes: null,
  };
  const files = new Map([[root.id, root]]);
  const uploads = new Map();
  const authQueue = [];
  const tokensIssued = new Map();
  const calls = [];
  let counter = 0;
  let publicJwkPromise = exportJWK(publicKey).then(value => ({ ...value, kid: 'local-drive-fixture-key', alg: 'RS256', use: 'sig' }));

  const store = (file, bytes) => {
    file.bytes = bytes;
    file.size = bytes?.byteLength ?? 0;
    file.md5Checksum = bytes ? createHash('md5').update(bytes).digest('hex') : undefined;
  };
  const touch = file => {
    file.version = String(Number(file.version) + 1);
    file.modifiedTime = new Date().toISOString();
  };
  const json = (status, body, headers) => Response.json(body, { status, headers });

  const seed = (name, parent, text, mimeType = 'text/plain') => {
    const file = { id: 'localSeed' + randomUUID().replaceAll('-', '').slice(0, 16),
      name, mimeType, version: '1', createdTime: new Date().toISOString(),
      modifiedTime: new Date().toISOString(), parents: [parent ?? root.id], driveId: null,
      ownedByMe: true, shared: false, trashed: false, explicitlyTrashed: false, appProperties: {}, bytes: null };
    store(file, new TextEncoder().encode(text));
    files.set(file.id, file);
    return file;
  };

  function authorized(request) {
    const bearer = new Headers(request.headers).get('authorization') ?? '';
    const token = bearer.slice(7);
    const entry = tokensIssued.get(token);
    return entry !== undefined && entry.expiresAt > Date.now();
  }

  function respondError(status, reason) {
    return json(status, { error: { code: status, message: reason, errors: [{ reason }] } });
  }

  function listFiles(url) {
    const q = url.searchParams.get('q') ?? '';
    const trashed = /trashed = true/.test(q);
    const parent = /'([^']*)' in parents/.exec(q)?.[1] ?? null;
    const exact = /name = '((?:[^'\\]|\\.)+)'/.exec(q)?.[1]?.replaceAll("\\'", "'").replaceAll('\\\\', '\\') ?? null;
    let rows = [...files.values()].filter(file =>
      (trashed ? file.trashed : file.trashed === false) &&
      (trashed || parent === null || file.parents.includes(parent)) &&
      (exact === null || file.name === exact));
    if ((url.searchParams.get('orderBy') ?? '').startsWith('modifiedTime')) {
      rows = rows.sort((a, b) => b.modifiedTime.localeCompare(a.modifiedTime));
    } else rows = rows.sort((a, b) => a.name.localeCompare(b.name));
    const size = Number(url.searchParams.get('pageSize') ?? '100');
    const start = Number(url.searchParams.get('pageToken') ?? '0');
    const page = rows.slice(start, start + size);
    const next = start + size < rows.length ? String(start + size) : null;
    return json(200, { files: page.map(fileShape), nextPageToken: next, incompleteSearch: false });
  }

  async function idToken(nonce) {
    const key = await publicJwkPromise;
    return new SignJWT({
      email: ownerEmail, email_verified: true, nonce,
      azp: clientId, sub: ownerSub,
    }).setProtectedHeader({ alg: 'RS256', kid: key.kid })
      .setIssuer('https://accounts.google.com')
      .setAudience(clientId)
      .setIssuedAt()
      .setExpirationTime('1h')
      .sign(privateKey);
  }

  function issueToken(extra = {}) {
    const token = 'localAt' + (++counter) + 'x'.repeat(20);
    tokensIssued.set(token, { expiresAt: Date.now() + 3600_000 });
    return json(200, {
      token_type: 'Bearer', expires_in: 3600, scope: SCOPES,
      access_token: token, refresh_token: 'localRt' + counter, ...extra,
    });
  }

  async function driveRequest(url, request) {
    const [, , version, collection, id, action] = url.pathname.split('/');
    if (collection !== 'files' && id !== 'files') return respondError(404, 'notFound');
    const method = request.method;
    if (method === 'GET' && id === 'generateIds') {
      return json(200, { ids: ['localGen' + (++counter) + 'x'.repeat(8)] });
    }
    if (!authorized(request)) return respondError(401, 'unauthenticated');
    if (method === 'GET' && !id) return listFiles(url);
    if (method === 'POST' && !id) {
      const body = JSON.parse(await request.text());
      if (files.has(body.id)) return respondError(409, 'duplicate');
      const created = { id: body.id, name: body.name, mimeType: body.mimeType ?? 'text/plain',
        version: '1', createdTime: new Date().toISOString(), modifiedTime: new Date().toISOString(),
        parents: [...(body.parents ?? [root.id])], driveId: null, ownedByMe: true, shared: false,
        trashed: false, explicitlyTrashed: false, appProperties: body.appProperties ?? {}, bytes: null };
      store(created, new Uint8Array(0));
      if (created.mimeType === FOLDER) created.bytes = null;
      files.set(created.id, created);
      return json(200, fileShape(created));
    }
    const target = id === 'root' ? root : files.get(id);
    if (!target) return respondError(404, 'notFound');
    if (method === 'GET' && action === 'export') {
      const exported = new TextEncoder().encode('%PDF-1.4 local export fixture for ' + target.name + '\n%%EOF\n');
      return new Response(exported, { headers: { 'content-type': url.searchParams.get('mimeType') ?? 'application/pdf',
        'content-length': String(exported.byteLength) } });
    }
    if (method === 'GET') {
      if (url.searchParams.get('alt') === 'media') {
        if (target.bytes === null || target.trashed) return respondError(404, 'notFound');
        const total = target.bytes.byteLength;
        const range = request.headers.get('range');
        if (range) {
          const match = /^bytes=(\d+)-(\d+)$/.exec(range);
          if (!match || Number(match[1]) >= total || Number(match[2]) >= Math.max(total, 1)) {
            return respondError(416, 'rangeNotSatisfiable');
          }
          const slice = target.bytes.slice(Number(match[1]), Number(match[2]) + 1);
          return new Response(slice, { status: 206, headers: {
            'content-range': `bytes ${match[1]}-${match[2]}/${total}`,
            'content-type': 'application/octet-stream', 'content-length': String(slice.byteLength) } });
        }
        return new Response(target.bytes, { headers: { 'content-type': 'application/octet-stream',
          'content-length': String(total) } });
      }
      if (version === 'v3') return json(200, fileShape(target), { etag: strongTag(target) });
      return json(200, v2Shape(target), { etag: strongTag(target) });
    }
    if (method === 'PATCH') {
      if (version !== 'v2') return respondError(400, 'unsupportedOperation');
      const tag = '"' + target.id + ':' + target.version + '"';
      if ((request.headers.get('if-match') ?? '') !== tag) return respondError(412, 'conditionNotMet');
      const patch = JSON.parse(await request.text());
      if (patch.title !== undefined) target.name = patch.title;
      if (patch.labels) target.trashed = target.explicitlyTrashed = Boolean(patch.labels.trashed);
      if (url.searchParams.has('addParents')) {
        target.parents = [url.searchParams.get('addParents')];
      }
      if (Array.isArray(patch.properties)) {
        target.appProperties = Object.fromEntries(patch.properties
          .filter(property => property.visibility === 'PRIVATE').map(property => [property.key, property.value]));
      }
      touch(target);
      return json(200, v2Shape(target), { etag: strongTag(target) });
    }
    return respondError(405, 'methodNotAllowed');
  }

  async function uploadRequest(url, request) {
    if (!authorized(request)) return respondError(401, 'unauthenticated');
    const uploadType = url.searchParams.get('uploadType');
    if (request.method === 'POST' && uploadType === 'resumable') {
      const metadata = JSON.parse(await request.text());
      if (files.has(metadata.id)) return respondError(409, 'duplicate');
      const uploadId = 'localUpload' + (++counter) + 'x'.repeat(10);
      const parent = files.get((metadata.parents ?? [])[0]) ?? root;
      uploads.set(uploadId, { metadata, parent: parent.id, contentType: metadata.mimeType ?? 'application/octet-stream',
        received: null, expected: Number(request.headers.get('x-upload-content-length') ?? '0') });
      const location = new URL('https://www.googleapis.com/upload/drive/v3/files');
      location.searchParams.set('uploadType', 'resumable');
      location.searchParams.set('upload_id', uploadId);
      location.searchParams.set('fields', url.searchParams.get('fields'));
      return new Response(null, { status: 200, headers: { location: location.href } });
    }
    if (request.method === 'POST' && uploadType === 'multipart') {
      // The Files Worker only sends an empty-file multipart with a known length.
      const metadataText = /application\/json[^\n]*\r\n\r\n(\{.*?\})\r\n--/s.exec(await request.text())?.[1];
      const metadata = JSON.parse(metadataText ?? 'null');
      if (!metadata || files.has(metadata.id)) return respondError(409, 'duplicate');
      const created = { id: metadata.id, name: metadata.name, mimeType: metadata.mimeType, version: '1',
        createdTime: new Date().toISOString(), modifiedTime: new Date().toISOString(),
        parents: [...(metadata.parents ?? [root.id])], driveId: null, ownedByMe: true, shared: false,
        trashed: false, explicitlyTrashed: false, appProperties: metadata.appProperties ?? {}, bytes: null };
      store(created, new Uint8Array(0));
      files.set(created.id, created);
      return json(200, fileShape(created));
    }
    const upload = uploads.get(url.searchParams.get('upload_id'));
    if (!upload) return respondError(404, 'notFound');
    if (request.method === 'PUT') {
      const range = request.headers.get('content-range') ?? '';
      const progress = /^bytes \*\/(\d+)$/.exec(range);
      if (progress) {
        const ack = upload.received === null ? null : 'bytes=0-' + (upload.received.byteLength - 1);
        return new Response(null, { status: 308, headers: ack ? { range: ack } : {} });
      }
      const match = /^bytes (\d+)-(\d+)\/(\d+)$/.exec(range);
      if (!match) return respondError(400, 'invalid');
      const chunk = new Uint8Array(await request.arrayBuffer());
      const offset = Number(match[1]); const total = Number(match[3]);
      if (offset !== (upload.received?.byteLength ?? 0) || offset + chunk.byteLength > total) {
        return respondError(400, 'invalid');
      }
      upload.received = new Uint8Array(total);
      if (chunk.byteLength) upload.received.set(chunk, offset);
      const done = offset + chunk.byteLength === total;
      if (!done) {
        return new Response(null, { status: 308, headers: { range: 'bytes=0-' + (offset + chunk.byteLength - 1) } });
      }
      const created = { id: upload.metadata.id, name: upload.metadata.name, mimeType: upload.metadata.mimeType ?? 'text/plain',
        version: '1', createdTime: new Date().toISOString(), modifiedTime: new Date().toISOString(),
        parents: [...(upload.metadata.parents ?? [root.id])], driveId: null, ownedByMe: true, shared: false,
        trashed: false, explicitlyTrashed: false, appProperties: upload.metadata.appProperties ?? {}, bytes: null };
      store(created, upload.received);
      files.set(created.id, created);
      uploads.delete(url.searchParams.get('upload_id'));
      return json(200, fileShape(created));
    }
    return respondError(405, 'methodNotAllowed');
  }

  async function fetchImpl(raw, options = {}) {
    const url = new URL(raw);
    const request = new Request(raw, options);
    const method = request.method;
    calls.push({ method, host: url.host, path: url.pathname + (url.search ?? '') });
    if (url.origin === 'https://oauth2.googleapis.com') {
      if (url.pathname === '/token' && method === 'POST') {
        const form = new URLSearchParams(await request.text());
        if (form.get('grant_type') === 'authorization_code') {
          const pending = authQueue.shift();
          if (!pending) return respondError(400, 'invalid_grant');
          return issueToken({ id_token: await idToken(pending.nonce) });
        }
        if (form.get('grant_type') === 'refresh_token') return issueToken();
        return respondError(400, 'unsupported_grant_type');
      }
      if (url.pathname === '/revoke' && method === 'POST') return json(200, {});
      return respondError(404, 'notFound');
    }
    if (url.hostname === 'www.googleapis.com') {
      if (url.pathname === '/oauth2/v3/certs') {
        const key = await publicJwkPromise;
        return json(200, { keys: [key] }, { 'cache-control': 'public, max-age=600' });
      }
      if (url.pathname.startsWith('/upload/drive/v3/')) return await uploadRequest(url, request);
      if (url.pathname.startsWith('/drive/v')) return await driveRequest(url, request);
    }
    return respondError(599, 'noLocalFixtureRoute');
  }

  return {
    fetch: fetchImpl,
    calls,
    // The driver registers the state/nonce pair it observed in the returned
    // authorization URL so the next token exchange can mint a matching id_token.
    expectAuth(nonce) { authQueue.push({ nonce }); },
    seed,
    root,
    files,
    ids: { projectId, clientId },
    keys: async () => [await publicJwkPromise],
  };
}
