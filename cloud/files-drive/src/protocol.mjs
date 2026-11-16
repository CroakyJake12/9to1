export const FILES_ORIGIN = 'https://9to1.uk';
export const API_PREFIX = '/api/files/v1';
export const SESSION_COOKIE = '__Host-cake_session';
export const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export class FilesHttpError extends Error {
  constructor(status, code, message, { notStarted = false, retryAt = null } = {}) {
    super(message); this.status = status; this.code = code;
    this.notStarted = notStarted; this.retryAt = retryAt;
  }
}

export function json(value, status = 200, extra = {}) {
  return new Response(JSON.stringify(value), { status, headers: {
    'content-type': 'application/json; charset=utf-8', 'cache-control': 'private, no-store',
    'x-content-type-options': 'nosniff', 'referrer-policy': 'no-referrer', ...extra,
  } });
}

export function errorResponse(error) {
  const known = error instanceof FilesHttpError || error?.name === 'QuotaError';
  const status = known && Number.isInteger(error.status) ? error.status : 503;
  const retryAt = Number.isFinite(error?.retryAt) ? error.retryAt : null;
  const code = error?.name === 'QuotaError' ? 'QuotaExceeded' : known ? error.code : 'ServiceUnavailable';
  return json({ code,
    message: known ? error.message : 'Files could not confirm the result. Refresh its status before retrying a change.',
    notStarted: error?.notStarted === true, retryAt }, status,
  retryAt ? { 'retry-after': String(Math.max(1, Math.ceil((retryAt - Date.now()) / 1000))) } : {});
}

export function readCookie(request, name) {
  const values = (request.headers.get('cookie') ?? '').split(';').map(x => x.trim())
    .filter(x => x.startsWith(name + '='));
  return values.length === 1 ? values[0].slice(name.length + 1) : null;
}

export function demandMutationOrigin(request) {
  if (request.headers.get('origin') !== FILES_ORIGIN ||
      !['same-origin', null].includes(request.headers.get('sec-fetch-site'))) {
    throw new FilesHttpError(403, 'OriginNotAllowed', 'Open Files from your 9to1 website.', { notStarted: true });
  }
}

export async function readBoundedBytes(body, maxBytes, signal) {
  if (!Number.isSafeInteger(maxBytes) || maxBytes < 0) throw new TypeError('A response limit is required.');
  const reader = body?.getReader(); if (!reader) return new Uint8Array();
  const chunks = []; let size = 0;
  let rejectAbort;
  const aborted = new Promise((_, reject) => { rejectAbort = reject; });
  const onAbort = () => rejectAbort(signal.reason ?? new DOMException('Request cancelled.', 'AbortError'));
  signal?.addEventListener('abort', onAbort, { once: true });
  try {
    for (;;) {
      signal?.throwIfAborted();
      const { value, done } = await (signal ? Promise.race([reader.read(), aborted]) : reader.read());
      if (done) break;
      if (!(value instanceof Uint8Array) || value.byteLength > maxBytes - size) {
        throw new FilesHttpError(502, 'ProviderResponseTooLarge', 'The provider response exceeded its reserved allowance.');
      }
      size += value.byteLength; chunks.push(value);
    }
  } catch (error) { await cancelBounded(reader); throw error; }
  finally { signal?.removeEventListener('abort', onAbort); reader.releaseLock(); }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  return bytes;
}

export async function cancelBounded(streamOrReader) {
  let timer;
  try { await Promise.race([Promise.resolve().then(() => streamOrReader?.cancel()).catch(() => {}),
    new Promise(resolve => { timer = setTimeout(resolve, 100); })]); }
  finally { clearTimeout(timer); }
}

export async function readJsonBody(request, maxBytes = 65536, signal = request.signal) {
  if (request.headers.get('content-type')?.split(';')[0].trim() !== 'application/json') {
    throw new FilesHttpError(415, 'JsonRequired', 'Send a JSON request.', { notStarted: true });
  }
  const bytes = await readBoundedBytes(request.body, maxBytes, signal);
  try {
    const value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
    if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error();
    return value;
  } catch { throw new FilesHttpError(400, 'InvalidRequest', 'The request could not be read.', { notStarted: true }); }
}

export function guid(value, nullable = false) {
  if (nullable && (value === null || value === undefined || value === '')) return null;
  const text = typeof value === 'object' ? value?.value : value;
  if (typeof text !== 'string' || !UUID.test(text)) {
    throw new FilesHttpError(400, 'InvalidIdentifier', 'The file identifier is invalid.', { notStarted: true });
  }
  return text.toLowerCase();
}

async function sessionResponse(request, checkSignal) {
  checkSignal.throwIfAborted();
  let reject;
  const interrupted = new Promise((_, stop) => { reject = stop; });
  const abort = () => reject(checkSignal.reason);
  checkSignal.addEventListener('abort', abort, { once: true });
  const pending = Promise.resolve().then(() => {
    checkSignal.throwIfAborted();
    return request();
  });
  try {
    return await Promise.race([pending, interrupted]);
  } catch (error) {
    // The existing session RPC has no cancellation argument. Ignore any
    // approval after cancellation and close a late response body.
    if (checkSignal.aborted) void pending.then(result => cancelBounded(result?.body), () => {}).catch(() => {});
    throw error;
  } finally {
    checkSignal.removeEventListener('abort', abort);
  }
}

export async function demandOwner(request, env, signal = request.signal) {
  if (new URL(request.url).origin !== FILES_ORIGIN || !env.OWNER_ACCOUNT_ID || !env.WEB_SESSIONS) {
    throw new FilesHttpError(503, 'FilesNotConfigured', 'Private Files access is not configured.', { notStarted: true });
  }
  const sid = readCookie(request, SESSION_COOKIE);
  if (!sid || !/^[\w-]{43}$/.test(sid)) throw new FilesHttpError(401, 'SignInRequired', 'Sign in to your 9to1 Account.', { notStarted: true });
  const deadline = AbortSignal.timeout(4000);
  const checkSignal = signal ? AbortSignal.any([signal, deadline]) : deadline;
  let response;
  try {
    response = await sessionResponse(() => env.WEB_SESSIONS.getByName(sid).request('/api/account/current', 'GET', null, FILES_ORIGIN),
      checkSignal);
  }
  catch { throw new FilesHttpError(503, 'AccountUnavailable', 'Your account could not be verified.', { notStarted: true }); }
  if (!(response instanceof Response)) throw new FilesHttpError(503, 'AccountUnavailable', 'Your account could not be verified.', { notStarted: true });
  if (!response.ok) {
    await cancelBounded(response.body);
    throw new FilesHttpError(response.status === 401 ? 401 : 503, response.status === 401 ? 'SignInRequired' : 'AccountUnavailable', 'Your account needs to be checked again.', { notStarted: true });
  }
  const bytes = await readBoundedBytes(response.body, 8192, checkSignal);
  checkSignal.throwIfAborted();
  let current; try { current = JSON.parse(new TextDecoder().decode(bytes)); } catch { throw new FilesHttpError(503, 'AccountUnavailable', 'Your account could not be verified.', { notStarted: true }); }
  if (!current || typeof current !== 'object' || Array.isArray(current) || typeof current.accountId !== 'string') throw new FilesHttpError(503, 'AccountUnavailable', 'Your account could not be verified.', { notStarted: true });
  if (current.accountId !== env.OWNER_ACCOUNT_ID) throw new FilesHttpError(403, 'OwnerOnly', 'Files is currently private to its owner.', { notStarted: true });
  return { accountId: current.accountId, displayName: typeof current.displayName === 'string' ? current.displayName : 'Your account' };
}
