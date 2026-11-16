import { createHash } from 'node:crypto';
import { CHUNK_BYTES, EXPORT_BYTES, JSON_BYTES } from './google-transport.mjs';
import { DRIVE_FILE_FIELDS } from './provider.mjs';
import { API_PREFIX, FilesHttpError, guid } from './protocol.mjs';
import { DRIVE_UNIT_COSTS, QuotaError } from './quota.mjs';

const ORIGIN = 'https://www.googleapis.com';
const API = '/drive/v3/files';
const UPLOAD = '/upload/drive/v3/files';
const MAX_BYTES = 5_000_000_000_000;
const MIN_MEDIA_RESPONSE = 64 * 1024;
const AUTH_RESPONSE_BYTES = 64 * 1024;
const RANGE_UNITS = DRIVE_UNIT_COSTS.download + 2 * DRIVE_UNIT_COSTS.metadata;
const MAX_RANGE_WAIT_MS = 70_000;
const MAX_RANGE_WAITS = 2;
const SESSION_MS = 6 * 24 * 60 * 60 * 1000;
const TICKET_MS = 2 * 60 * 1000;
const PROVIDER_ID = /^[A-Za-z0-9_-]{1,256}$/;
const UPLOAD_ID = /^[A-Za-z0-9_=-]{1,4096}$/;
const UPLOAD_SESSION_CONTROLS = /[\u0000-\u001f\u007f-\u009f]/;
const VERSION = /^\d{1,24}$/;
const MIME = /^[\w!#$&^_.+-]{1,64}\/[\w!#$&^_.+-]{1,128}$/;
const GOOGLE = 'application/vnd.google-apps.';
const PDF = 'application/pdf';
const EXCEL = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
const EXPORTS = new Map([
  [GOOGLE + 'document', [PDF, '.pdf']], [GOOGLE + 'presentation', [PDF, '.pdf']],
  [GOOGLE + 'drawing', [PDF, '.pdf']], [GOOGLE + 'spreadsheet', [EXCEL, '.xlsx']],
]);
const enc = new TextEncoder();
const wrap = value => ({ value });
const LOCATION_ORIGINS = new Set([ORIGIN, 'https://content.googleapis.com', 'https://drive.googleapis.com',
  'https://upload.googleapis.com', 'https://drive.google.com', 'https://www.google.com']);
const LOCATION_QUERY_KEYS = new Set(['uploadType', 'upload_id', 'fields', 'session_crd', 'upload_protocol', 'alt', 'supportsAllDrives']);
const LOCATION_DIAGNOSTIC_KEY = /^[A-Za-z][A-Za-z_]{0,63}$/;
const INIT_REASONS = new Set(['badRequest', 'invalid', 'invalidArgument', 'forbidden', 'notFound', 'conflict',
  'required', 'invalidParameter', 'uploadError', 'storageQuotaExceeded', 'rateLimitExceeded',
  'userRateLimitExceeded', 'dailyLimitExceeded', 'quotaExceeded', 'insufficientFilePermissions',
  'activeItemCreationLimitExceeded', 'backendError']);
const INIT_STATUSES = new Set(['INVALID_ARGUMENT', 'UNAUTHENTICATED', 'PERMISSION_DENIED', 'NOT_FOUND',
  'ALREADY_EXISTS', 'RESOURCE_EXHAUSTED', 'FAILED_PRECONDITION', 'UNAVAILABLE', 'INTERNAL']);
const INIT_LOCAL_ERRORS = new Set(['quotaExceeded', 'quotaTransferTooLarge', 'quotaLedgerFull',
  'quotaAttemptExpired', 'ProviderSafetyHold', 'ProviderRedirectRefused', 'ProviderResponseTooLarge',
  'QuotaAttemptExpired', 'PermissionDenied', 'DriveReconnectRequired', 'DriveAuthTemporarilyUnavailable',
  'FilesBusy', 'TransferInterrupted']);

function failure(status, code, message, notStarted = true, retryAt = null) {
  return new FilesHttpError(status, code, message, { notStarted, retryAt });
}

const missing = () => failure(404, 'ItemNotFound', 'This transfer is not available.');
const unknown = () => failure(503, 'TransferInterrupted', 'Google Drive has not confirmed this upload. Check its status before retrying.', false);
const invalid = () => failure(400, 'InvalidState', 'The file transfer request is invalid.');
const changed = () => failure(409, 'RevisionConflict', 'This file changed. Prepare the download again.');
const mismatch = () => failure(502, 'IntegrityFailed', 'Google Drive did not return the expected bytes.', false);
const nameConflict = () => failure(409, 'NameConflict', 'That folder already contains a file or pending upload with this name.');

function endpoint(path, args = {}) {
  const url = new URL(path, ORIGIN);
  for (const [key, value] of Object.entries(args)) url.searchParams.set(key, value);
  return url.href;
}

function base64url(bytes) {
  return btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}

function unbase64url(value) {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_-]+$/.test(value)) throw invalid();
  return Uint8Array.from(atob(value.replaceAll('-', '+').replaceAll('_', '/')), c => c.charCodeAt(0));
}

function uploadUrl(value) {
  if (typeof value !== 'string' || value.length > 8192) throw unknown();
  const url = new URL(value);
  const fields = [...url.searchParams.keys()];
  const credentials = url.searchParams.getAll('session_crd');
  if (url.origin !== ORIGIN || url.pathname !== UPLOAD || url.username || url.password || url.hash ||
      fields.some(field => !['uploadType', 'upload_id', 'fields', 'session_crd'].includes(field)) ||
      url.searchParams.getAll('uploadType').length !== 1 || url.searchParams.get('uploadType') !== 'resumable' ||
      url.searchParams.getAll('upload_id').length !== 1 ||
      url.searchParams.getAll('fields').length > 1 ||
      (url.searchParams.has('fields') && url.searchParams.get('fields') !== DRIVE_FILE_FIELDS) ||
      credentials.length > 1 || (credentials.length === 1 &&
        (!credentials[0] || credentials[0].length > 4096 || UPLOAD_SESSION_CONTROLS.test(credentials[0]))) ||
      !UPLOAD_ID.test(url.searchParams.get('upload_id') ?? '')) throw unknown();
  return url.href;
}

function describeLocation(value) {
  if (value == null) return { present: false };
  if (typeof value !== 'string' || value.length > 8192) return { present: true, parsed: false };
  let url;
  try { url = new URL(value); } catch { return { present: true, parsed: false }; }
  const keys = [...url.searchParams.keys()];
  const trustedOrigin = LOCATION_ORIGINS.has(url.origin);
  const queryKeys = [...new Set(keys.filter(key => trustedOrigin && LOCATION_DIAGNOSTIC_KEY.test(key)))].sort().slice(0, 16);
  // Only record bounded names on Google origins. Query values and untrusted
  // path segments may contain provider capabilities and never enter storage.
  const safePath = /^\/(?:resumable\/)?upload\/drive\/v[23]\/files/.exec(url.pathname)?.[0];
  return {
    present: true, parsed: true,
    origin: trustedOrigin ? url.origin : 'other',
    path: safePath ? safePath + (url.pathname === safePath ? '' : '/[redacted]') : 'other',
    queryKeys, unknownKeyCount: keys.filter(key => !LOCATION_QUERY_KEYS.has(key)).length,
    credentialsPresent: Boolean(url.username || url.password), fragmentPresent: Boolean(url.hash),
    uploadTypeMatches: url.searchParams.getAll('uploadType').length === 1 && url.searchParams.get('uploadType') === 'resumable',
    uploadIdMatches: url.searchParams.getAll('upload_id').length === 1 && UPLOAD_ID.test(url.searchParams.get('upload_id')),
    fieldsPresent: url.searchParams.has('fields'),
    fieldsMatch: !url.searchParams.has('fields') ||
      (url.searchParams.getAll('fields').length === 1 && url.searchParams.get('fields') === DRIVE_FILE_FIELDS),
  };
}

function describeInitiation(result) {
  let value = null;
  let bodyJson = null;
  if (result.bytes.byteLength) {
    try {
      value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(result.bytes));
      bodyJson = true;
    } catch { bodyJson = false; }
  }
  const reason = value?.error?.errors?.[0]?.reason;
  const providerStatus = value?.error?.status;
  return { value, diagnostic: {
    outcome: 'Received', httpStatus: result.status, bodyJson,
    providerReason: INIT_REASONS.has(reason) ? reason : null,
    providerStatus: INIT_STATUSES.has(providerStatus) ? providerStatus : null,
    location: describeLocation(result.headers.get('location')),
  } };
}

function safeSize(value) {
  return Number.isSafeInteger(value) && value >= 0 && value <= MAX_BYTES;
}

function uploadIntent(body = {}) {
  const { name, sizeBytes, contentType } = body ?? {};
  if (typeof name !== 'string' || name.trim().length === 0 || name.length > 255 ||
      /[\u0000-\u001f\u007f/\\]/.test(name) || name === '.' || name === '..')
    throw failure(400, 'InvalidName', 'Enter a valid file name.');
  if (!safeSize(sizeBytes) || typeof contentType !== 'string' || !MIME.test(contentType) || contentType.startsWith(GOOGLE)) throw invalid();
  return { operationId: guid(body.operationId), parentId: guid(body.parentId, true), name, totalBytes: sizeBytes, contentType };
}

function progress(entry) {
  return { transferId: wrap(entry.transferId), itemId: wrap(entry.itemId), chunkBytes: CHUNK_BYTES,
    uploadedBytes: entry.uploadedBytes, totalBytes: entry.totalBytes, state: entry.state };
}

function exactNameQuery(parent, name) {
  const escaped = name.replaceAll('\\', '\\\\').replaceAll("'", "\\'");
  return "'" + parent + "' in parents and name = '" + escaped + "' and trashed = false";
}

function acknowledgedRange(headers, floor, ceiling, total) {
  const range = headers.get('range');
  const match = range === null ? null : /^bytes=0-(\d{1,13})$/.exec(range);
  const offset = match ? Number(match[1]) + 1 : range === null ? 0 : -1;
  if (offset < floor || offset > ceiling || offset >= total) throw unknown();
  return offset;
}

function emptyMultipart(metadata, mime) {
  const boundary = 'nine-' + crypto.randomUUID();
  const body = enc.encode('--' + boundary + '\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n' +
    JSON.stringify(metadata) + '\r\n--' + boundary + '\r\nContent-Type: ' + mime + '\r\n\r\n\r\n--' + boundary + '--\r\n');
  return { body, type: 'multipart/related; boundary=' + boundary };
}

function receiptMatches(file, entry) {
  return file?.id === entry.googleId && file.appProperties?.nineUpload === entry.operationId &&
    String(file.size) === String(entry.totalBytes) && VERSION.test(String(file.version)) &&
    file.name === entry.name && file.mimeType === entry.contentType && file.trashed === false &&
    file.parents?.length === 1 && file.parents[0] === entry.googleParent;
}

function contentHash(file) {
  if (/^[\da-f]{64}$/i.test(file.sha256Checksum ?? '')) return { name: 'sha256', hex: file.sha256Checksum.toLowerCase() };
  if (/^[\da-f]{32}$/i.test(file.md5Checksum ?? '')) return { name: 'md5', hex: file.md5Checksum.toLowerCase() };
  return null;
}

function disposition(name) {
  const clean = name.toWellFormed().replace(/[\u0000-\u001f\u007f/\\]/g, '_').slice(0, 247).toWellFormed() || 'download';
  const ascii = clean.replace(/[^A-Za-z0-9._ -]/g, '_').replace(/^\.+/, '') || 'download';
  const encoded = encodeURIComponent(clean).replace(/['()*]/g, c => '%' + c.charCodeAt(0).toString(16).toUpperCase());
  return "attachment; filename=\"" + ascii + "\"; filename*=UTF-8''" + encoded;
}

function downloadEstimate(size) {
  const ranges = size === null ? 1 : Math.max(1, Math.ceil(size / CHUNK_BYTES));
  const lastRange = size === null ? null : size === 0 ? 0 : size % CHUNK_BYTES || CHUNK_BYTES;
  const media = size === null ? EXPORT_BYTES : size + Math.max(0, MIN_MEDIA_RESPONSE - lastRange);
  // Two verification reads per range plus one extra check after a pause.
  // Reserve headroom for token refresh as well as the transport's JSON bounds.
  const verificationReads = 2 * ranges + 1;
  const authAttempts = Math.max(2, Math.ceil(ranges / 128));
  return {
    bytes: media + verificationReads * JSON_BYTES + authAttempts * AUTH_RESPONSE_BYTES,
    units: ranges * DRIVE_UNIT_COSTS.download + verificationReads * DRIVE_UNIT_COSTS.metadata + authAttempts * DRIVE_UNIT_COSTS.auth,
  };
}

async function waitForQuota(ms, signal) {
  signal.throwIfAborted();
  let timer;
  let listener;
  try {
    await new Promise((resolve, reject) => {
      listener = () => reject(signal.reason);
      signal.addEventListener('abort', listener, { once: true });
      timer = setTimeout(resolve, ms);
    });
  } finally {
    clearTimeout(timer);
    signal.removeEventListener('abort', listener);
  }
}

export class DriveTransfers {
  #store; #oauth; #transport; #provider; #quotaStatus; #now; #key; #keyBytes; #wait;
  #writes = new Set();
  #downloads = new Set();

  constructor(store, oauth, transport, provider, { encryptionKey, quotaStatus, now = Date.now, wait = waitForQuota } = {}) {
    this.#store = store;
    this.#oauth = oauth;
    this.#transport = transport;
    this.#provider = provider;
    this.#now = now;
    if (typeof quotaStatus !== 'function') throw new TypeError('A quota status source is required.');
    this.#quotaStatus = quotaStatus;
    if (typeof wait !== 'function') throw new TypeError('An abortable quota wait is required.');
    this.#wait = wait;
    this.#keyBytes = typeof encryptionKey === 'string' ? unbase64url(encryptionKey) :
      encryptionKey instanceof Uint8Array ? new Uint8Array(encryptionKey) : null;
    if (this.#keyBytes?.byteLength !== 32) throw new TypeError('A dedicated 256-bit upload session key is required.');
  }

  async #encrypt(entry, url) {
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const buffer = await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: this.#associatedData(entry) },
      await this.#aesKey(), enc.encode(uploadUrl(url)));
    return { iv: base64url(iv), ciphertext: base64url(new Uint8Array(buffer)) };
  }

  async #decrypt(entry) {
    try {
      const decoded = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: unbase64url(entry.session.iv),
        additionalData: this.#associatedData(entry) }, await this.#aesKey(), unbase64url(entry.session.ciphertext));
      return uploadUrl(new TextDecoder('utf-8', { fatal: true }).decode(decoded));
    } catch { throw unknown(); }
  }

  #associatedData(entry) {
    return enc.encode(JSON.stringify(['9to1-files-upload-v1', this.#store.ownerId, entry.transferId, entry.operationId, entry.googleId]));
  }
  #aesKey() {
    this.#key ??= crypto.subtle.importKey('raw', this.#keyBytes, 'AES-GCM', false, ['encrypt', 'decrypt']);
    return this.#key;
  }

  async #withUpload(operationId, work) {
    if (this.#writes.has(operationId)) throw failure(409, 'TransferBusy', 'This upload is already in progress.', false);
    if (this.#writes.size >= 2) throw failure(429, 'FilesBusy', 'Two uploads are already running. Try again shortly.');
    this.#writes.add(operationId);
    try { return await work(); }
    finally { this.#writes.delete(operationId); }
  }

  #upload(id) {
    const entry = this.#store.transfer(guid(id));
    if (entry?.direction !== 'upload') throw missing();
    return entry;
  }

  async #persist(entry) {
    this.#store.putTransfer(entry, entry.direction);
    await this.#store.storage.sync();
    return entry;
  }

  async #claimName(entry, googleParent) {
    this.#store.storage.transactionSync(() => {
      const reserved = this.#store.query("SELECT id FROM files_transfers WHERE direction='upload' AND state='Running' " +
        "AND id<>? AND json_extract(record, '$.googleParent')=? AND json_extract(record, '$.name')=? LIMIT 1",
      entry.transferId, googleParent, entry.name);
      if (reserved.length) throw nameConflict();
      entry.googleParent = googleParent;
      this.#store.putTransfer(entry, 'upload');
    });
    await this.#store.storage.sync();
  }

  async #request(url, options, { json = true } = {}) {
    const token = await this.#oauth.getAccessToken({ signal: options.signal });
    const headers = { Authorization: 'Bearer ' + token, ...options.headers };
    const result = await (json ? this.#transport.json(url, { ...options, headers })
      : this.#transport.request(url, { ...options, headers }));
    if (result.status === 401) await this.#oauth.rejectAccessToken?.(token);
    return result;
  }

  async beginUpload(body, signal) {
    const intent = uploadIntent(body);
    return this.#withUpload(intent.operationId, async () => {
      let entry = this.#store.findTransfer(intent.operationId);
      if (entry) {
        if (entry.direction !== 'upload' || Object.keys(intent).some(key => intent[key] !== entry[key]))
          throw failure(409, 'InvalidState', 'This upload identifier already represents another file.');
        if (entry.state === 'Completed' || entry.state === 'Cancelled' || entry.state === 'Failed') return progress(entry);
        if (entry.session || entry.phase === 'sending' || entry.phase === 'creating')
          return progress(await this.#checkUpload(entry, signal, { restartCreation: true }));
      } else {
        const limit = (await this.#quotaStatus()).uploadBytesPerDay?.limit;
        if (!Number.isSafeInteger(limit) || limit < 0) {
          throw failure(503, 'ProviderUnavailable', 'The Files upload allowance could not be checked.');
        }
        if (intent.totalBytes > limit) {
          const maximum = limit > 0 && limit % 1_000_000_000 === 0
            ? (limit / 1_000_000_000).toLocaleString('en-GB') + ' GB' : limit.toLocaleString('en-GB') + ' bytes';
          throw failure(413, 'UploadTooLarge', 'The Files limit per upload is ' + maximum + '. Choose a smaller file.');
        }
        signal?.throwIfAborted();
        entry = { ...intent, transferId: crypto.randomUUID(), itemId: crypto.randomUUID(), direction: 'upload',
          phase: 'allocating', state: 'Running', uploadedBytes: 0 };
        await this.#persist(entry);
      }
      if (!entry.googleId) {
        const parent = await this.#provider.readParent(intent.parentId, signal);
        if (!PROVIDER_ID.test(parent.id) || parent.driveId) throw invalid();
        await this.#claimName(entry, parent.id);
        const names = await this.#request(endpoint(API, { q: exactNameQuery(parent.id, intent.name), spaces: 'drive',
          fields: 'files(id),nextPageToken,incompleteSearch', pageSize: '1' }), { kind: 'list', signal });
        if (names.status !== 200 || !Array.isArray(names.value?.files) || names.value.incompleteSearch) throw unknown();
        if (names.value.files.length || names.value.nextPageToken) {
          entry.phase = 'rejected'; entry.state = 'Failed';
          await this.#persist(entry);
          throw nameConflict();
        }
        const allocation = await this.#request(endpoint(API + '/generateIds', { count: '1', space: 'drive', type: 'files' }),
          { kind: 'metadata', signal });
        const googleId = allocation.value?.ids?.[0];
        if (allocation.status !== 200 || typeof googleId !== 'string' || !PROVIDER_ID.test(googleId)) throw unknown();
        entry.googleId = googleId;
        entry.googleParent = parent.id;
        entry.phase = 'prepared';
        this.#store.ensureId(googleId, entry.itemId);
        await this.#persist(entry);
      }
      return progress(await this.#initiate(entry, signal));
    });
  }

  async #initiate(entry, signal) {
    const metadata = { id: entry.googleId, name: entry.name, parents: [entry.googleParent],
      mimeType: entry.contentType, appProperties: { nineUpload: entry.operationId } };
    const previouslyUncertain = entry.phase === 'creating';
    entry.phase = 'creating';
    await this.#persist(entry);
    const empty = entry.totalBytes === 0 ? emptyMultipart(metadata, entry.contentType) : null;
    let result;
    try {
      result = await this.#request(endpoint(UPLOAD, {
        uploadType: empty ? 'multipart' : 'resumable', fields: DRIVE_FILE_FIELDS,
      }), {
        kind: 'mutate', method: 'POST', signal, body: empty?.body ?? JSON.stringify(metadata),
        headers: empty ? { 'Content-Type': empty.type } : { 'Content-Type': 'application/json; charset=UTF-8',
          'X-Upload-Content-Type': entry.contentType, 'X-Upload-Content-Length': String(entry.totalBytes) },
      }, { json: false });
    } catch (error) {
      entry.initDiagnostic = {
        outcome: 'RequestUnconfirmed', notStarted: error?.notStarted === true,
        localCode: INIT_LOCAL_ERRORS.has(error?.code) ? error.code : null,
      };
      if (!previouslyUncertain && error?.notStarted === true) {
        entry.phase = 'prepared';
      }
      await this.#persist(entry);
      if (previouslyUncertain && error && typeof error === 'object') error.notStarted = false;
      throw error;
    }
    const { value, diagnostic } = describeInitiation(result);
    entry.initDiagnostic = diagnostic;
    await this.#persist(entry);
    if (result.status === 409) {
      const committed = await this.#lookForCompleted(entry, signal);
      if (committed) return committed;
      throw unknown();
    }
    if (![200, 201].includes(result.status)) {
      const error = this.#providerFailure({ ...result, value }, !previouslyUncertain);
      if (previouslyUncertain) error.notStarted = false;
      throw error;
    }
    if (empty) return this.#complete(entry, value);
    let address;
    try { address = uploadUrl(result.headers.get('location')); }
    catch {
      diagnostic.outcome = 'LocationRefused';
      await this.#persist(entry);
      throw unknown();
    }
    try { entry.session = await this.#encrypt(entry, address); }
    catch {
      diagnostic.outcome = 'SessionSealFailed';
      await this.#persist(entry);
      throw unknown();
    }
    diagnostic.outcome = 'SessionReady';
    entry.sessionExpires = this.#now() + SESSION_MS;
    entry.phase = 'ready';
    return this.#persist(entry);
  }

  async #lookForCompleted(entry, signal) {
    let file;
    try { file = await this.#provider.readRaw(entry.itemId, signal); }
    catch (error) { if (error?.code === 'ItemNotFound') return null; throw error; }
    return this.#complete(entry, file);
  }

  async #complete(entry, file) {
    if (!receiptMatches(file, entry)) throw unknown();
    this.#store.mapFile(file, entry.itemId);
    entry.uploadedBytes = entry.totalBytes;
    entry.phase = 'complete';
    entry.state = 'Completed';
    delete entry.session;
    delete entry.pendingEnd;
    return this.#persist(entry);
  }

  async findUpload(operationId) {
    const entry = this.#store.findTransfer(guid(operationId));
    if (entry?.direction !== 'upload') throw missing();
    return progress(entry);
  }

  async uploadStatus(id, signal) {
    const entry = this.#upload(id);
    return this.#withUpload(entry.operationId, async () => {
      const current = await this.#checkUpload(this.#upload(id), signal);
      if (current.state === 'Running' && !current.session) throw unknown();
      return progress(current);
    });
  }

  async #checkUpload(entry, signal, { restartCreation = false } = {}) {
    if (entry.state !== 'Running') return entry;
    if (entry.phase === 'creating') {
      let committed;
      try { committed = await this.#lookForCompleted(entry, signal); }
      catch (error) {
        if (error && typeof error === 'object') error.notStarted = false;
        throw error;
      }
      // An explicit Resume may retry only the original preallocated ID after
      // a read finds no completed file. It never allocates another file ID.
      if (committed || !restartCreation) return committed ?? entry;
      return this.#initiate(entry, signal);
    }
    if (entry.phase === 'sending') {
      const result = await this.#request(await this.#decrypt(entry), {
        kind: 'upload', method: 'PUT', headers: { 'Content-Range': 'bytes */' + entry.totalBytes, 'Content-Length': '0' },
        uploadBytes: 0, signal,
      });
      return this.#recordChunk(entry, result, signal);
    }
    if (entry.session && entry.sessionExpires <= this.#now()) {
      entry.state = 'Failed';
      entry.phase = 'expired';
      delete entry.session;
      return this.#persist(entry);
    }
    return entry;
  }

  async uploadChunk(id, offset, bytes, signal) {
    const original = this.#upload(id);
    if (!(bytes instanceof Uint8Array) || bytes.byteLength < 1 || bytes.byteLength > CHUNK_BYTES || !safeSize(offset)) throw invalid();
    const data = new Uint8Array(bytes);
    return this.#withUpload(original.operationId, async () => {
      const entry = this.#upload(id);
      if (entry.state === 'Completed' || entry.state === 'Cancelled') return progress(entry);
      if (entry.phase !== 'ready' || entry.sessionExpires <= this.#now())
        throw failure(409, 'TransferInterrupted', 'Check the current upload before sending more bytes.', false);
      const end = offset + data.byteLength;
      if (offset !== entry.uploadedBytes || end > entry.totalBytes ||
          (end !== entry.totalBytes && data.byteLength % (256 * 1024) !== 0)) throw invalid();
      const url = await this.#decrypt(entry);
      entry.phase = 'sending';
      entry.pendingEnd = end;
      await this.#persist(entry);
      let result;
      try {
        result = await this.#request(url, { kind: 'upload', uploadBytes: data.byteLength,
          method: 'PUT', signal, body: data, headers: { 'Content-Type': entry.contentType,
            'Content-Length': String(data.byteLength), 'Content-Range': 'bytes ' + offset + '-' + (end - 1) + '/' + entry.totalBytes } });
      } catch (error) {
        if (error?.notStarted === true) {
          entry.phase = 'ready'; delete entry.pendingEnd;
          await this.#persist(entry);
        }
        throw error;
      }
      return progress(await this.#recordChunk(entry, result, signal));
    });
  }

  async #recordChunk(entry, result, signal) {
    if (result.status === 308) {
      entry.uploadedBytes = acknowledgedRange(result.headers, entry.uploadedBytes, entry.pendingEnd, entry.totalBytes);
      entry.phase = 'ready'; delete entry.pendingEnd;
      return this.#persist(entry);
    }
    if ([200, 201].includes(result.status)) {
      if (entry.pendingEnd !== entry.totalBytes) throw unknown();
      return this.#complete(entry, result.value);
    }
    if ([404, 410].includes(result.status)) {
      const committed = await this.#lookForCompleted(entry, signal);
      if (committed) return committed;
      if (entry.pendingEnd === entry.totalBytes) throw unknown();
      entry.phase = 'expired'; entry.state = 'Failed'; delete entry.session; delete entry.pendingEnd;
      return this.#persist(entry);
    }
    throw this.#providerFailure(result, false);
  }

  async cancelUpload(id, signal) {
    const original = this.#upload(id);
    return this.#withUpload(original.operationId, async () => {
      let entry = this.#upload(id);
      if (entry.phase === 'sending' || entry.phase === 'creating' && entry.totalBytes === 0) {
        entry = await this.#checkUpload(entry, signal, { restartCreation: false });
        if (entry.phase === 'sending' || entry.phase === 'creating') throw unknown();
      }
      if (entry.state === 'Completed' || entry.state === 'Cancelled') return progress(entry);
      entry.phase = 'cancelled'; entry.state = 'Cancelled';
      delete entry.session; delete entry.pendingEnd;
      return progress(await this.#persist(entry));
    });
  }

  async #checkBudget(size) {
    const quota = await this.#quotaStatus();
    const estimate = downloadEstimate(size);
    const checks = [[quota.downloadBytesPerDay, estimate.bytes], [quota.unitsPerDay, estimate.units], [quota.unitsPerMinute, RANGE_UNITS]];
    for (const [budget, cost] of checks) {
      if (!Number.isSafeInteger(budget?.limit) || !Number.isSafeInteger(budget?.remaining) ||
          budget.remaining < 0 || budget.remaining > budget.limit) {
        throw failure(503, 'ProviderUnavailable', 'The Files allowance could not be checked.');
      }
      if (budget.limit < cost) {
        throw failure(429, 'QuotaExceeded', 'This file exceeds the Files download allowance.');
      }
      if (budget !== quota.unitsPerMinute && budget.remaining < cost) {
        throw failure(429, 'QuotaExceeded', 'The current Files allowance cannot cover this download.', true, budget.resetAt);
      }
    }
  }

  async prepareDownload({ itemId, exportMime = null } = {}, sessionDigest, signal) {
    itemId = guid(itemId);
    if (typeof sessionDigest !== 'string' || sessionDigest.length < 32 || sessionDigest.length > 256) throw invalid();
    const file = await this.#provider.readRaw(itemId, signal);
    if (!PROVIDER_ID.test(file.id) || !VERSION.test(String(file.version)) || typeof file.name !== 'string' ||
        typeof file.mimeType !== 'string' || file.trashed !== false || file.capabilities?.canDownload !== true)
      throw failure(409, 'ProviderCapabilityUnsupported', 'Google Drive does not allow downloading this file.');
    const native = file.mimeType.startsWith(GOOGLE);
    const format = EXPORTS.get(file.mimeType);
    if (native ? !format || exportMime !== format[0] : exportMime !== null)
      throw failure(409, 'ProviderCapabilityUnsupported', 'Files cannot export this item in that format.');
    const size = native ? null : Number(file.size);
    if (size !== null && !safeSize(size)) throw mismatch();
    const hash = native ? null : contentHash(file);
    if (size > CHUNK_BYTES && !hash)
      throw failure(409, 'ProviderCapabilityUnsupported', 'A checksum is needed to download this file in separate ranges.');
    await this.#checkBudget(size);
    const transferId = crypto.randomUUID();
    await this.#persist({ transferId, direction: 'download', state: 'Queued', itemId, googleId: file.id,
      version: String(file.version), name: native ? file.name + format[1] : file.name, mimeType: file.mimeType, exportMime,
      size, hash, sessionDigest, expires: this.#now() + TICKET_MS });
    return { transferId: wrap(transferId), url: API_PREFIX + '/downloads/' + transferId + '/content' };
  }

  async downloadContent(id, sessionDigest, { confirmSession, signal, registerChildSignal } = {}) {
    id = guid(id);
    const entry = this.#store.transfer(id);
    if (entry?.direction !== 'download' || entry.sessionDigest !== sessionDigest ||
        entry.expires <= this.#now() || entry.state !== 'Queued' || this.#downloads.has(id)) throw missing();
    if (typeof confirmSession !== 'function') throw invalid();
    if (this.#downloads.size >= 2) throw failure(429, 'FilesBusy', 'Two downloads are already running. Try again shortly.');
    const owner = new AbortController();
    const current = owner.signal;
    registerChildSignal?.(current);
    this.#downloads.add(id);
    const hash = entry.hash ? createHash(entry.hash.name) : null;
    let finished = false;
    const close = (state) => {
      if (finished) return;
      finished = true;
      entry.state = state;
      try { this.#store.putTransfer(entry, 'download'); }
      finally { this.#downloads.delete(id); signal?.removeEventListener('abort', cancel); }
    };
    const cancel = () => { owner.abort(signal?.reason); close('Cancelled'); };
    signal?.addEventListener('abort', cancel, { once: true });
    const session = async () => {
      current.throwIfAborted();
      if (await confirmSession(current) === false) throw failure(401, 'SignInRequired', 'Sign in again to download this file.');
      current.throwIfAborted();
    };
    const verify = async () => {
      const file = await this.#provider.readRaw(entry.itemId, current);
      if (file.id !== entry.googleId || String(file.version) !== entry.version || file.trashed !== false ||
          file.mimeType !== entry.mimeType || (entry.size !== null && Number(file.size) !== entry.size) ||
          file.capabilities?.canDownload !== true) throw changed();
    };
    const fetchChunk = async offset => {
      let waits = 0, waitedMs = 0;
      // Allow only a known refusal before Google saw the attempt. On a
      // successful media fetch, a subsequent metadata wait stays in that stage.
      const admit = async (attempt, resume) => {
        for (;;) {
          current.throwIfAborted();
          try { return await attempt(); } catch (error) {
            if (!(error instanceof QuotaError) || error.code !== 'quotaExceeded' || error.notStarted !== true ||
                error.budgets.length !== 1 || error.budgets[0] !== 'unitsPerMinute' ||
                !Number.isSafeInteger(error.retryAt) || waits >= MAX_RANGE_WAITS) throw error;
            const delay = Math.max(0, error.retryAt - this.#now());
            if (waitedMs + delay > MAX_RANGE_WAIT_MS) throw error;
            waits++;
            waitedMs += delay;
            if (delay) await this.#wait(delay, current);
            current.throwIfAborted();
            await resume();
          }
        }
      };
      const verifyWithWait = () => admit(verify, session);
      await session();
      await verifyWithWait();
      const end = entry.size === null ? null : Math.min(offset + CHUNK_BYTES, entry.size);
      const range = end !== null && end > offset ? 'bytes=' + offset + '-' + (end - 1) : null;
      const fetchMedia = () => this.#request(entry.exportMime
        ? endpoint(API + '/' + entry.googleId + '/export', { mimeType: entry.exportMime })
        : endpoint(API + '/' + entry.googleId, { alt: 'media' }), {
        kind: entry.exportMime ? 'export' : 'download', signal: current,
        maxResponseBytes: entry.exportMime ? EXPORT_BYTES : Math.max(MIN_MEDIA_RESPONSE, end - offset),
        headers: range ? { Range: range, 'Accept-Encoding': 'identity' } : { 'Accept-Encoding': 'identity' },
      }, { json: false });
      const result = await admit(fetchMedia, async () => {
        await session();
        await verifyWithWait();
      });
      if (entry.exportMime ? result.status !== 200 : range
        ? result.status !== 206 || result.headers.get('content-range') !== 'bytes ' + offset + '-' + (end - 1) + '/' + entry.size
        : result.status !== 200) throw result.status >= 400 ? this.#providerFailure(result, false) : mismatch();
      if (!entry.exportMime && result.bytes.byteLength !== end - offset) throw mismatch();
      await verifyWithWait();
      await session();
      hash?.update(result.bytes);
      const last = entry.exportMime || end === entry.size;
      if (last && hash?.digest('hex') !== entry.hash?.hex) throw mismatch();
      return { bytes: result.bytes, last, next: end };
    };
    let first;
    try {
      if (signal?.aborted) cancel();
      current.throwIfAborted();
      await this.#checkBudget(entry.size);
      current.throwIfAborted();
      entry.state = 'Running';
      await this.#persist(entry);
      first = await fetchChunk(0);
    } catch (error) { close('Failed'); throw error; }
    const headers = new Headers({ 'Content-Disposition': disposition(entry.name), 'Cache-Control': 'private, no-store',
      'Content-Type': 'application/octet-stream', 'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer',
      'Content-Length': String(entry.size ?? first.bytes.byteLength) });
    if (first.last) { close('Completed'); return new Response(first.bytes, { headers }); }
    let offset = 0, inFlight;
    const stream = new ReadableStream({
      pull(controller) {
        inFlight = (async () => {
          try {
            const chunk = first ?? await fetchChunk(offset);
            first = null;
            current.throwIfAborted();
            controller.enqueue(chunk.bytes);
            offset = chunk.next;
            if (chunk.last) { close('Completed'); controller.close(); }
          } catch (error) { close('Failed'); controller.error(error); }
        })();
        return inFlight;
      },
      async cancel(reason) {
        owner.abort(reason);
        close('Cancelled');
        // Canceling the reader resolves pending reads before the underlying
        // asynchronous pull unwinds. Keep its scope until that work settles.
        await inFlight?.catch(() => {});
      },
    }, { highWaterMark: 0 });
    return new Response(stream, { headers });
  }

  #providerFailure(result, notStarted = true) {
    let value = result.value;
    if (!value && result.status >= 400 && result.bytes?.byteLength < MIN_MEDIA_RESPONSE) {
      try { value = JSON.parse(new TextDecoder().decode(result.bytes)); } catch { /* Generic provider error below. */ }
    }
    const reason = value?.error?.errors?.[0]?.reason;
    if (result.status === 429 || result.status === 403 &&
        ['rateLimitExceeded', 'userRateLimitExceeded', 'quotaExceeded', 'dailyLimitExceeded'].includes(reason))
      return failure(429, 'QuotaExceeded', 'Google Drive paused this transfer.', notStarted, this.#now() + 30_000);
    if (result.status === 403) return failure(409, 'ProviderCapabilityUnsupported', 'Google Drive refused this file transfer.', notStarted);
    if (result.status === 401) return failure(409, 'DriveReconnectRequired', 'Connect Google Drive again in Files.', notStarted);
    if (result.status === 404) return missing();
    return unknown();
  }
}
