import { FilesHttpError, readBoundedBytes, cancelBounded } from './protocol.mjs';

export const CHUNK_BYTES = 8 * 1024 * 1024;
export const JSON_BYTES = 1024 * 1024;
export const EXPORT_BYTES = 10 * 1024 * 1024;

function allowedGoogleUrl(value) {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.username || url.password || url.hash || url.port) return false;
  if (url.hostname === 'www.googleapis.com') {
    return /^\/drive\/v[23]\//.test(url.pathname) || /^\/upload\/drive\/v3\/files(?:\/[^/]+)?$/.test(url.pathname)
      || url.pathname === '/oauth2/v3/certs';
  }
  return url.hostname === 'oauth2.googleapis.com' && ['/token', '/revoke'].includes(url.pathname);
}

// Every call is a single, metered attempt. There are no hidden HTTP redirects or
// retries. Large files are consumed through separately bounded range/upload chunks.
export class GoogleTransport {
  #quota; #fetch; #now; #safetyHold; #isSafetyHeld; #faulted = false; #active = 0;
  constructor(quota, { fetch = globalThis.fetch.bind(globalThis), now = Date.now, onSafetyFault, isSafetyHeld } = {}) {
    if (typeof onSafetyFault !== 'function' || typeof isSafetyHeld !== 'function') throw new TypeError('Persistent provider safety callbacks are required.');
    this.#quota = quota; this.#fetch = fetch; this.#now = now; this.#safetyHold = onSafetyFault; this.#isSafetyHeld = isSafetyHeld;
  }

  async request(url, { kind, method = 'GET', headers = {}, body = null,
    uploadBytes = 0, maxResponseBytes = JSON_BYTES, signal } = {}) {
    if (!allowedGoogleUrl(url) || !['GET', 'POST', 'PATCH', 'PUT'].includes(method) ||
        !Number.isSafeInteger(maxResponseBytes) || maxResponseBytes < 0 || maxResponseBytes > EXPORT_BYTES ||
        !Number.isSafeInteger(uploadBytes) || uploadBytes < 0 || uploadBytes > CHUNK_BYTES) {
      throw new FilesHttpError(400, 'InvalidProviderRequest', 'The provider request is outside its allowed bounds.', { notStarted: true });
    }
    // At most two bounded bodies live in an owner object at once; caller retries
    // explicitly, after admission, rather than accumulating an unbounded queue.
    if (this.#active >= 2) throw new FilesHttpError(429, 'FilesBusy', 'Two file requests are already running. Try again shortly.', { notStarted: true, retryAt: this.#now() + 1000 });
    if (body !== null && !(body instanceof Uint8Array) && typeof body !== 'string') {
      throw new FilesHttpError(400, 'UnboundedUpload', 'Uploads must use bounded chunks.', { notStarted: true });
    }
    // Own a fixed snapshot before any quota/storage await; a caller cannot grow
    // a resizable/shared view after its byte count has been admitted.
    if (body instanceof Uint8Array) body = new Uint8Array(body);
    const bodyBytes = typeof body === 'string' ? new TextEncoder().encode(body).byteLength : body?.byteLength ?? 0;
    if (bodyBytes > CHUNK_BYTES || (kind === 'upload' && uploadBytes !== bodyBytes)) {
      throw new FilesHttpError(400, 'InvalidUploadSize', 'The upload size did not match the reserved bytes.', { notStarted: true });
    }
    signal?.throwIfAborted(); this.#active++;
    const id = crypto.randomUUID(); let issued = false; let reserved = false; let dispatchStarted = false; let timeout;
    const controller = new AbortController();
    const cancel = () => controller.abort(signal?.reason);
    try {
      try {
        await this.#quota.reserve({ id, kind, uploadBytes, downloadBytes: maxResponseBytes }); reserved = true;
      } catch (error) {
        if (error?.name === 'QuotaError') error.notStarted = true;
        throw error;
      }
      signal?.throwIfAborted();
      const permit = await this.#quota.issue(id); issued = true;
      if ((this.#faulted && kind !== 'auth') || await this.#isSafetyHeld({ kind, url, method, signal })) throw new FilesHttpError(503, 'ProviderSafetyHold', 'Provider access is paused for a safety check.', { notStarted: true });
      const now = this.#now();
      if (now >= permit.startBy || now >= permit.deadlineAt) {
        throw new FilesHttpError(409, 'QuotaAttemptExpired', 'The reserved request expired before dispatch.', { notStarted: true });
      }
      if (signal?.aborted) controller.abort(signal.reason);
      else signal?.addEventListener('abort', cancel, { once: true });
      timeout = setTimeout(() => controller.abort(new DOMException('Transfer deadline exceeded.', 'TimeoutError')),
        Math.min(120000, permit.deadlineAt - now));
      controller.signal.throwIfAborted();
      dispatchStarted = true;
      const response = await this.#fetch(url, { method, headers, ...(body === null ? {} : { body }),
        redirect: 'manual', signal: controller.signal });
      // Redirects might disclose a bearer credential and bypass the attempt
      // budget. Resumable upload 308 is an acknowledgement, never followed.
      if (response.status >= 300 && response.status < 400 && response.status !== 308) {
        await cancelBounded(response.body);
        throw new FilesHttpError(502, 'ProviderRedirectRefused', 'The provider returned an unexpected redirect.');
      }
      const contentLength = response.headers.get('content-length');
      if (contentLength !== null && (!/^\d+$/.test(contentLength) || Number(contentLength) > maxResponseBytes)) {
        this.#faulted = true;
        try { await this.#safetyHold('provider_response_size'); } finally { await cancelBounded(response.body); }
        throw new FilesHttpError(502, 'ProviderResponseTooLarge', 'Files paused provider access because a response exceeded its allowance.');
      }
      let bytes;
      try { bytes = await readBoundedBytes(response.body, maxResponseBytes, controller.signal); }
      catch (error) { if (error?.code === 'ProviderResponseTooLarge') { this.#faulted = true; await this.#safetyHold('provider_response_size'); } throw error; }
      controller.signal.throwIfAborted();
      // Conservatively keep the entire reserved body allowance, including all
      // failures. A decoded payload is not an authoritative network billing meter.
      await this.#quota.settle(id, 'completed');
      return { status: response.status, headers: response.headers, bytes };
    } catch (error) {
      if (!dispatchStarted && error && typeof error === 'object') error.notStarted = true;
      if (reserved) await this.#quota.settle(id, issued ? 'unknown' : 'not-issued').catch(() => {});
      throw error;
    } finally {
      clearTimeout(timeout); signal?.removeEventListener('abort', cancel); this.#active--;
    }
  }

  async json(url, options) {
    const result = await this.request(url, options);
    let value = null;
    if (result.bytes.byteLength) {
      try { value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(result.bytes)); }
      catch { throw new FilesHttpError(502, 'InvalidProviderResponse', 'The provider response could not be read.'); }
    }
    return { status: result.status, headers: result.headers, value };
  }
}
