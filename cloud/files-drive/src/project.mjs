import { DriveQuota } from './quota.mjs';
import { GoogleTransport, CHUNK_BYTES } from './google-transport.mjs';
import { GoogleDriveOAuth } from './oauth.mjs';
import { DriveStore } from './store.mjs';
import { DriveProvider } from './provider.mjs';
import { DRIVE_VALIDATION_TIMEOUT_MS, DriveValidationProbe, validValidationRun, validationReceipt } from './provider-validation.mjs';
import { DriveTransfers } from './transfers.mjs';
import { API_PREFIX, SESSION_COOKIE, FilesHttpError, demandOwner, demandMutationOrigin,
  readCookie, readJsonBody, readBoundedBytes, json, errorResponse, cancelBounded } from './protocol.mjs';

async function sessionDigest(request) {
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(readCookie(request, SESSION_COOKIE)));
  return Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join('');
}
const redirect = location => new Response(null, { status: 303, headers: { location, 'cache-control': 'no-store', 'referrer-policy': 'no-referrer' } });
const GOOGLE_REVOKE = 'https://oauth2.googleapis.com/revoke';
const disconnected = () => new FilesHttpError(409, 'DriveReconnectRequired', 'Connect Google Drive again in Files.', { notStarted: true });
const validationPath = API_PREFIX + '/validation';
const validationPage = '<!doctype html><html lang="en"><meta charset="utf-8"><title>Files provider check</title>' +
  '<main><h1>Check Google Drive editing</h1><p>This will create two empty test folders in your Google Drive to check rename, move, Trash and restore. No files will be permanently deleted.</p>' +
  '<form method="post" action="' + validationPath + '"><button>Run disposable file checks</button></form></main></html>';

// This core is hosted in one SQLite Durable Object for the dedicated Google
// project. Session changes never choose a different quota object or clear it.
export class FilesProject {
  #ctx; #env; #store; #quota; #transport; #oauth; #provider; #transfers; #now; #validation;
  #closing = false; #work = new Set(); #validators = new Map();
  constructor(ctx, env, { fetch, now = Date.now } = {}) {
    this.#ctx = ctx; this.#env = env; this.#now = now;
    if (typeof env.OWNER_ACCOUNT_ID !== 'string' || !env.OWNER_ACCOUNT_ID) throw new Error('Files owner configuration is required.');
    this.#store = new DriveStore(ctx.storage, env.OWNER_ACCOUNT_ID, { now });
    let limits = {};
    if (env.DRIVE_APP_LIMITS) {
      try { limits = JSON.parse(env.DRIVE_APP_LIMITS); } catch { throw new Error('Invalid Files quota configuration.'); }
    }
    this.#quota = new DriveQuota(ctx.storage, { limits, now });
    this.#transport = new GoogleTransport(this.#quota, { ...(fetch ? { fetch } : {}), now,
      isSafetyHeld: async ({ kind, url, method, signal }) => {
        const scope = this.#validators.get(signal);
        if (!scope || (scope.mode === 'revoke' && (kind !== 'auth' || url !== GOOGLE_REVOKE || method !== 'POST'))) {
          throw new FilesHttpError(403, 'PermissionDenied', 'The private request context has closed.', { notStarted: true });
        }
        await scope.validate(signal);
        if (scope.mode === 'revoke') return false;
        if (this.#store.get('connectionClosed') && !(scope.mode === 'connect' && kind === 'auth')) return true;
        return kind !== 'auth' && Boolean(this.#store.get('providerSafetyHold'));
      },
      onSafetyFault: async reason => { this.#store.set('providerSafetyHold', { reason, at: now() }); await ctx.storage.sync(); },
    });
  }
  #services() {
    this.#oauth ??= new GoogleDriveOAuth(this.#ctx.storage, this.#env, this.#transport, { now: this.#now });
    this.#provider ??= new DriveProvider(this.#store, this.#oauth, this.#transport,
      { conditionalWrites: this.#env.DRIVE_CONDITIONAL_WRITES_VERIFIED === 'true', now: this.#now });
    this.#transfers ??= new DriveTransfers(this.#store, this.#oauth, this.#transport, this.#provider,
      { encryptionKey: this.#env.DRIVE_ENCRYPTION_KEY, quotaStatus: () => this.#quota.status(), now: this.#now });
    return { oauth: this.#oauth, provider: this.#provider, transfers: this.#transfers };
  }
  async #usage() {
    return { ...await this.#quota.status(), paused: Boolean(this.#store.get('providerSafetyHold')) };
  }
  async #context(owner) {
    let connection;
    try { connection = await this.#services().oauth.status(); }
    catch (error) { if (error.code !== 'DriveOAuthNotConfigured') throw error; connection = { connected: false, configurationRequired: true }; }
    if (this.#store.get('connectionClosed')) connection = { ...connection, connected: false, accountEmail: null };
    const location = this.#store.location();
    if (!connection.connected) location.capabilities = 'None';
    else if (this.#env.DRIVE_CONDITIONAL_WRITES_VERIFIED !== 'true') location.capabilities = 'Read, Write, Search, Streaming, ResumableTransfers';
    const usage = await this.#usage();
    return json({ ...owner, ...connection, location, usage, safetyPaused: usage.paused });
  }
  async fetch(request) {
    const path = new URL(request.url).pathname;
    const isDownload = /^\/api\/files\/v1\/downloads\/[0-9a-f-]+\/content$/.test(path);
    try {
      if (!path.startsWith(API_PREFIX + '/')) throw new FilesHttpError(404, 'NotFound', 'This Files route does not exist.', { notStarted: true });
      const owner = await demandOwner(request, this.#env);
      if (!['GET', 'POST', 'PUT'].includes(request.method)) throw new FilesHttpError(405, 'MethodNotAllowed', 'This request method is not supported.', { notStarted: true });
      if (request.method !== 'GET') demandMutationOrigin(request);
      const route = path.slice(API_PREFIX.length);
      if (route === '/context' && request.method === 'GET') return await this.#context(owner);
      if (route === '/usage' && request.method === 'GET') return json(await this.#usage());
      if (route === '/disconnect' && request.method === 'POST') {
        await readJsonBody(request, 1024);
        return await this.#disconnect(request);
      }
      const connecting = route === '/connect' || route === '/oauth/callback';
      return await this.#withWork(request, isDownload, connecting ? 'connect' : 'drive', async (signal, scope) => {
        if (route === '/validation/details' && request.method === 'GET') {
          const run = this.#env.FILES_PROVIDER_VALIDATION_RUN;
          if (!validValidationRun(run)) throw new FilesHttpError(404, 'NotFound', 'The provider check is not enabled.', { notStarted: true });
          const { oauth } = this.#services();
          if (this.#store.get('connectionClosed') || !(await oauth.status()).connected) throw disconnected();
          const bounded = AbortSignal.any([signal, AbortSignal.timeout(15_000)]);
          scope.registerChildSignal(bounded);
          this.#validation ??= new DriveValidationProbe(this.#store, oauth, this.#transport, { now: this.#now });
          return json(await this.#validation.inspect(run, bounded));
        }
        if (route === '/validation' && request.method === 'GET') {
          const run = this.#env.FILES_PROVIDER_VALIDATION_RUN;
          const receipt = this.#validation?.status() ?? validationReceipt(this.#store);
          if (!validValidationRun(run)) return json(receipt);
          const { oauth } = this.#services();
          this.#validation ??= new DriveValidationProbe(this.#store, oauth, this.#transport, { now: this.#now });
          if (!this.#validation.canStart(run)) return json(receipt);
          if (this.#store.get('connectionClosed') || !(await oauth.status()).connected) throw disconnected();
          return new Response(validationPage, { headers: { 'content-type': 'text/html; charset=utf-8' } });
        }
        if (route === '/validation' && request.method === 'POST') {
          const run = this.#env.FILES_PROVIDER_VALIDATION_RUN;
          if (!validValidationRun(run)) throw new FilesHttpError(404, 'NotFound', 'The provider check is not enabled.', { notStarted: true });
          try {
            if (new URL(request.url).search) throw new Error('Unexpected query');
            await readBoundedBytes(request.body, 0, signal);
          } catch {
            signal.throwIfAborted();
            throw new FilesHttpError(400, 'InvalidRequest', 'This provider check takes no options.', { notStarted: true });
          }
          const { oauth } = this.#services();
          if (this.#store.get('connectionClosed') || !(await oauth.status()).connected) throw disconnected();
          const bounded = AbortSignal.any([signal, AbortSignal.timeout(DRIVE_VALIDATION_TIMEOUT_MS)]);
          scope.registerChildSignal(bounded);
          this.#validation ??= new DriveValidationProbe(this.#store, oauth, this.#transport, { now: this.#now });
          await this.#validation.run(run, bounded);
          return redirect(validationPath);
        }
        const services = this.#services();
        const digest = await sessionDigest(request);
        if (route === '/connect' && request.method === 'POST') {
          await readJsonBody(request, 1024, signal);
          return json(await services.oauth.begin({ accountId: owner.accountId, sessionDigest: digest, signal }));
        }
        if (route === '/oauth/callback' && request.method === 'GET') {
          const url = new URL(request.url);
          if (url.searchParams.has('error')) return redirect('/files/#/app.files');
          await services.oauth.complete({ accountId: owner.accountId, sessionDigest: digest,
            code: url.searchParams.get('code'), state: url.searchParams.get('state'), signal });
          this.#store.set('connectionClosed', false); await this.#ctx.storage.sync();
          return redirect('/files/#/app.files');
        }
        if (this.#store.get('connectionClosed') || !(await services.oauth.status()).connected) throw disconnected();
        signal.throwIfAborted();
        const url = new URL(request.url);
        if (route === '/items' && request.method === 'GET') return json(await services.provider.list({
          parentId: url.searchParams.get('parentId'), query: url.searchParams.get('query'),
          pageToken: url.searchParams.get('pageToken'), trash: url.searchParams.get('trash') === 'true',
        }, signal));
        const thumbnail = /^\/items\/([0-9a-f-]+)\/thumbnail$/.exec(route);
        if (thumbnail && request.method === 'GET') {
          const image = await services.provider.thumbnail(thumbnail[1], signal);
          return new Response(image.bytes, { headers: { 'content-type': image.contentType,
            'cache-control': 'private, no-store', 'x-content-type-options': 'nosniff',
            'content-security-policy': "default-src 'none'", 'referrer-policy': 'no-referrer' } });
        }
        const item = /^\/items\/([0-9a-f-]+)$/.exec(route);
        if (item && request.method === 'GET') return json(await services.provider.get(item[1], signal));
        if (route === '/operations' && request.method === 'POST') return json(await services.provider.mutate(await readJsonBody(request, 65536, signal), signal));
        if (route === '/changes' && request.method === 'GET') return json(this.#store.changes(
          Number(url.searchParams.get('after') ?? 0), Number(url.searchParams.get('limit') ?? 100)));
        if (route === '/uploads' && request.method === 'POST') return json(await services.transfers.beginUpload(await readJsonBody(request, 65536, signal), signal));
        if (route === '/uploads' && request.method === 'GET') return json(await services.transfers.findUpload(url.searchParams.get('operationId'), signal));
        const upload = /^\/uploads\/([0-9a-f-]+)(\/content|\/cancel)?$/.exec(route);
        if (upload) {
          if (!upload[2] && request.method === 'GET') return json(await services.transfers.uploadStatus(upload[1], signal));
          if (upload[2] === '/cancel' && request.method === 'POST') {
            await readJsonBody(request, 1024, signal); return json(await services.transfers.cancelUpload(upload[1], signal));
          }
          if (upload[2] === '/content' && request.method === 'PUT') {
            const count = request.headers.get('x-file-chunk-size'), offset = url.searchParams.get('offset');
            if (!/^\d{1,8}$/.test(count ?? '') || !/^\d{1,16}$/.test(offset ?? '') ||
                Number(count) > CHUNK_BYTES || !Number.isSafeInteger(Number(offset)) ||
                request.headers.get('content-type') !== 'application/octet-stream') {
              throw new FilesHttpError(400, 'InvalidUploadSize', 'The upload chunk is invalid.', { notStarted: true });
            }
            const bytes = await readBoundedBytes(request.body, Number(count), signal);
            if (bytes.byteLength !== Number(count)) throw new FilesHttpError(400, 'InvalidUploadSize', 'The upload chunk was incomplete.', { notStarted: true });
            return json(await services.transfers.uploadChunk(upload[1], Number(offset), bytes, signal));
          }
        }
        if (route === '/downloads' && request.method === 'POST') return json(await services.transfers.prepareDownload(await readJsonBody(request, 65536, signal), digest, signal));
        const download = /^\/downloads\/([0-9a-f-]+)\/content$/.exec(route);
        if (download && request.method === 'GET') return services.transfers.downloadContent(download[1], digest, {
          signal, registerChildSignal: scope.registerChildSignal, confirmSession: async current => {
            if (this.#closing || this.#store.get('connectionClosed')) throw disconnected();
            await scope.validate(current);
            return true;
          },
        });
        throw new FilesHttpError(404, 'NotFound', 'This Files route does not exist.', { notStarted: true });
      });
    } catch (error) {
      if (isDownload) {
        const response = errorResponse(error);
        const body = await response.json();
        return new Response('Download did not complete. ' + body.message + '\nReturn to Files: https://9to1.uk/files/',
          { status: response.status, headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff',
            ...(response.headers.has('retry-after') ? { 'retry-after': response.headers.get('retry-after') } : {}) } });
      }
      return errorResponse(error);
    }
  }
  async #validate(request, signal) {
    signal.throwIfAborted();
    try { await demandOwner(request, this.#env, signal); }
    catch {
      signal.throwIfAborted();
      throw new FilesHttpError(403, 'PermissionDenied', 'This 9to1 session no longer permits access to Files.', { notStarted: true });
    }
    signal.throwIfAborted();
  }
  async #withWork(request, streamed, mode, action) {
    if (this.#closing || this.#work.size >= 4) throw new FilesHttpError(429, 'FilesBusy', 'Files is finishing another request. Try again shortly.', { notStarted: true, retryAt: this.#now() + 1000 });
    const controller = new AbortController(); let finish;
    const done = new Promise(resolve => { finish = resolve; });
    const entry = { controller, done }; this.#work.add(entry);
    const signals = new Set();
    const scope = {
      mode,
      validate: signal => this.#validate(request, signal),
      registerChildSignal: signal => {
        controller.signal.throwIfAborted();
        if (this.#validators.has(signal)) throw new Error('Duplicate Files request signal.');
        signals.add(signal);
        this.#validators.set(signal, scope);
      },
    };
    scope.registerChildSignal(controller.signal);
    const abort = () => controller.abort(request.signal.reason);
    if (request.signal.aborted) abort(); else request.signal.addEventListener('abort', abort, { once: true });
    const release = () => {
      request.signal.removeEventListener('abort', abort);
      for (const signal of signals) this.#validators.delete(signal);
      this.#work.delete(entry);
      finish();
    };
    try {
      controller.signal.throwIfAborted();
      const response = await action(controller.signal, scope);
      try { await scope.validate(controller.signal); }
      catch (error) {
        error.notStarted = false;
        if (streamed) await response.body?.cancel(error).catch(() => {});
        else await cancelBounded(response.body);
        throw error;
      }
      if (!streamed || !response.body) { release(); return response; }
      const reader = response.body.getReader();
      let settled = false, cancelling;
      const settle = () => {
        if (settled) return;
        settled = true;
        controller.signal.removeEventListener('abort', stop);
        try { reader.releaseLock(); } finally { release(); }
      };
      const cancelReader = reason => {
        cancelling ??= reader.cancel(reason).catch(() => {}).finally(settle);
        return cancelling;
      };
      let streamController;
      const stop = () => {
        try { streamController?.error(controller.signal.reason); } catch { }
        this.#ctx.waitUntil(cancelReader(controller.signal.reason));
      };
      const body = new ReadableStream({
        start(target) { streamController = target; },
        async pull(target) {
          try {
            controller.signal.throwIfAborted();
            const next = await reader.read(); controller.signal.throwIfAborted();
            if (next.done) { target.close(); settle(); } else target.enqueue(next.value);
          } catch (error) { try { target.error(error); } finally { await cancelReader(error); } }
        },
        cancel(reason) { controller.abort(reason); return cancelReader(reason); },
      }, { highWaterMark: 0 });
      controller.signal.addEventListener('abort', stop, { once: true });
      if (controller.signal.aborted) stop();
      const declaredLength = response.headers.get('content-length');
      const length = declaredLength === null ? null : Number(declaredLength);
      // The native Worker stream carries the exact admitted size. Node fixtures
      // exercise the plain stream; Workerd checks cover FixedLengthStream itself.
      if (typeof FixedLengthStream !== 'undefined' && Number.isSafeInteger(length) && length >= 0) {
        const fixed = new FixedLengthStream(length);
        this.#ctx.waitUntil(body.pipeTo(fixed.writable).catch(() => {}));
        return new Response(fixed.readable, response);
      }
      return new Response(body, response);
    } catch (error) { release(); throw error; }
  }
  async #disconnect(request) {
    if (this.#closing) throw new FilesHttpError(409, 'FilesBusy', 'Files is already disconnecting.', { notStarted: true });
    this.#closing = true;
    const signal = request.signal;
    try {
      await this.#validate(request, signal);
      this.#store.set('connectionClosed', true); await this.#ctx.storage.sync();
      const pending = [...this.#work];
      for (const entry of pending) entry.controller.abort(new DOMException('Drive disconnected.', 'AbortError'));
      await Promise.allSettled(pending.map(entry => entry.done));
      this.#validators.set(signal, { mode: 'revoke', validate: current => this.#validate(request, current) });
      await this.#services().oauth.disconnect({ signal });
      return json({ disconnected: true });
    } finally { this.#validators.delete(signal); this.#closing = false; }
  }
}
