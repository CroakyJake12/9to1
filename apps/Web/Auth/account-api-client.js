// Transport binding only. Protocol authority: CAKE ID Worker 7cc52df0411e0e9e617918f6c5158283cc3199fc.
// Authentication, authorisation, account identity and entitlements remain server-owned.
const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value);
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const nullableString = value => value === null || typeof value === 'string';
const date = value => typeof value === 'string' && Number.isFinite(Date.parse(value));
const positiveRevision = value => Number.isSafeInteger(value) && value > 0;
const profile = value => object(value) && uuid(value.accountId) &&
  typeof value.name === 'string' && typeof value.username === 'string' &&
  nullableString(value.icon) && nullableString(value.pronouns) && nullableString(value.job) && positiveRevision(value.revision);
const session = value => object(value) && uuid(value.sessionId) && uuid(value.accountId) &&
  typeof value.deviceName === 'string' && date(value.createdAt) && date(value.expiresAt) &&
  (value.revokedAt === null || date(value.revokedAt)) && nullableString(value.registeredClientId);

function failed(code, action, status = null, body = null) {
  return { ok: false, status, error: { code, action, body } };
}

/** Accepts a trusted configured API origin, not an issuer/client configuration or token authority. */
export class AccountApiClient {
  #origin;
  #token;
  #fetch;
  #clear;
  #beginPrivate;
  #generation = 0;
  #pending = new Set();

  constructor({ apiResource, getAccessToken, onPrivateContextInvalidated, beginPrivateContextInvalidation = undefined, fetch: transport = globalThis.fetch,
    allowLoopbackForIsolatedTests = false }) {
    const resource = new URL(apiResource);
    const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(resource.hostname);
    if (resource.username || resource.password || resource.search || resource.hash || resource.pathname !== '/' ||
        (resource.protocol !== 'https:' && !(allowLoopbackForIsolatedTests && loopback && resource.protocol === 'http:'))) {
      throw new TypeError('A credential-free HTTPS API origin is required.');
    }
    if (typeof getAccessToken !== 'function' || typeof onPrivateContextInvalidated !== 'function' || typeof transport !== 'function') {
      throw new TypeError('A token supplier, private-context invalidator and fetch transport are required.');
    }
    if (beginPrivateContextInvalidation !== undefined && typeof beginPrivateContextInvalidation !== 'function') throw new TypeError('A synchronous private fence is required when supplied.');
    this.#beginPrivate = beginPrivateContextInvalidation;
    this.#origin = resource.origin;
    this.#token = getAccessToken;
    this.#clear = onPrivateContextInvalidated;
    this.#fetch = transport;
  }

  /** Call before switching account/session/organisation; await cleanup before opening new private surfaces. */
  // Explicit sync begin receipt only; caller MUST separately await its owning
  // full cleanup join before admitting new private surfaces. No wire authority.
  beginPrivateContextInvalidation(reason = 'context_changed') {
    this.#fenceAndAbort(reason, null);
  }

  async invalidatePrivateContext(reason = 'context_changed') {
    await this.#invalidate(reason);
  }

  async #invalidate(reason, settledTransport = null) {
    this.#fenceAndAbort(reason, settledTransport);
    // Default callback still means original full awaited cleanup. Explicit
    // two-phase composition installs its full JOIN callback here.
    await this.#clear(reason);
  }

  #fenceAndAbort(reason, settledTransport) {
    this.#generation++;
    try {
      if (this.#beginPrivate !== undefined) {
        const acknowledgement = this.#beginPrivate(reason);
        if (acknowledgement !== undefined) {
          // An async/nonvoid callback NEVER qualifies as a native fence receipt.
          if (acknowledgement && typeof acknowledgement.then === 'function') Promise.resolve(acknowledgement).catch(() => {});
          throw new TypeError('Private fencing must synchronously return void.');
        }
      }
    } finally {
      // With explicit begin installed, credential/native owner fences precede
      // all API cancellation callbacks. Undefined mode preserves old behavior.
      for (const controller of this.#pending) if (controller !== settledTransport) controller.abort();
    }
  }

  // Only INTERNAL request authentication failures can acknowledge the fence
  // without awaiting their own owner drain. They remain owned until they settle.
  async #invalidateFromRequest(reason, settledTransport = null) {
    if (this.#beginPrivate === undefined) return this.#invalidate(reason, settledTransport);
    this.#fenceAndAbort(reason, settledTransport);
  }

  getCurrent({ signal } = {}) {
    return this.#request('GetCurrent', '/api/account/current', 'GET', undefined,
      value => object(value) && uuid(value.accountId) && typeof value.displayName === 'string', signal);
  }

  getProfile({ signal } = {}) {
    return this.#request('GetProfile', '/api/account/profile', 'GET', undefined,
      value => object(value) && profile(value.profile), signal);
  }

  updateProfile(expectedRevision, fields, { signal } = {}) {
    const allowed = new Set(['name', 'username', 'icon', 'pronouns', 'job']);
    if (!positiveRevision(expectedRevision) || expectedRevision >= Number.MAX_SAFE_INTEGER || !object(fields)) {
      return Promise.resolve(failed('InvalidArgument', 'UpdateProfile'));
    }
    // Read caller-owned values once, validate that snapshot, then dispatch only our own plain data.
    // Token acquisition may yield; caller mutation/getters/toJSON must not change the approved edit.
    let entries;
    try { entries = Object.entries(fields); }
    catch { return Promise.resolve(failed('InvalidArgument', 'UpdateProfile')); }
    if (!entries.length || entries.some(([key, value]) => !allowed.has(key) ||
        (typeof value !== 'string' && !(value === null && !['name', 'username'].includes(key))))) {
      return Promise.resolve(failed('InvalidArgument', 'UpdateProfile'));
    }
    const snapshot = Object.fromEntries(entries);
    const nextRevision = expectedRevision + 1;
    return this.#request('UpdateProfile', '/api/account/profile', 'PATCH', { expectedRevision, fields: snapshot },
      value => object(value) && profile(value.profile) && value.profile.revision === nextRevision, signal);
  }

  listSessions({ signal } = {}) {
    return this.#request('ListSessions', '/api/account/sessions', 'GET', undefined,
      value => object(value) && Array.isArray(value.sessions) && value.sessions.every(session) &&
        new Set(value.sessions.map(item => item.sessionId)).size === value.sessions.length &&
        new Set(value.sessions.map(item => item.accountId)).size <= 1, signal);
  }

  signOut(options = {}) {
    return this.#sessionMutation('SignOut', '/api/account/signout', 'POST', options.signal);
  }

  revokeSession(sessionId, options = {}) {
    if (!uuid(sessionId)) return Promise.resolve(failed('InvalidArgument', 'RevokeSession'));
    return this.#sessionMutation('RevokeSession', `/api/account/sessions/${sessionId}`, 'DELETE', options.signal);
  }

  revokeAllOtherSessions(options = {}) {
    return this.#sessionMutation('RevokeAllOtherSessions', '/api/account/revoke-other-sessions', 'POST', options.signal);
  }

  async #sessionMutation(action, path, method, signal) {
    const startingGeneration = this.#generation;
    const controller = new AbortController();
    const abort = () => controller.abort();
    signal?.addEventListener('abort', abort, { once: true });
    if (signal?.aborted) abort();
    this.#pending.add(controller);
    try {
      if (controller.signal.aborted) return failed('Cancelled', action);
      // Bind this mutation to the existing session before clearing UI/token context.
      // Keep the captured bearer only on this call stack, never in client state.
      const token = await this.#token({ signal: controller.signal });
      if (startingGeneration !== this.#generation) return failed('SessionContextChanged', action);
      if (controller.signal.aborted) return failed('Cancelled', action);
      if (typeof token !== 'string' || !token || /\s/.test(token)) {
        try { await this.invalidatePrivateContext('authentication_required'); }
        catch { return failed('PrivateContextCleanupFailed', action); }
        return failed('AuthenticationRequired', action);
      }
      this.#pending.delete(controller);
      const expectedGeneration = this.#generation + 1;
      try { await this.invalidatePrivateContext('session_mutation'); }
      catch { return failed('PrivateContextCleanupFailed', action); }
      if (this.#generation !== expectedGeneration) return failed('SessionContextChanged', action);
      if (controller.signal.aborted) return failed('Cancelled', action);
      return await this.#request(action, path, method, undefined, null, signal, token);
    } catch {
      if (startingGeneration !== this.#generation) return failed('SessionContextChanged', action);
      return failed(controller.signal.aborted ? 'Cancelled' : 'TransportUnavailable', action);
    } finally {
      signal?.removeEventListener('abort', abort);
      this.#pending.delete(controller);
    }
  }

  async #request(action, path, method, body, validate, signal, capturedToken = undefined) {
    const generation = this.#generation;
    let expectedGeneration = generation;
    const controller = new AbortController();
    const abort = () => controller.abort();
    signal?.addEventListener('abort', abort, { once: true });
    if (signal?.aborted) abort();
    this.#pending.add(controller);
    try {
      if (controller.signal.aborted) return failed('Cancelled', action);
      const token = capturedToken === undefined ? await this.#token({ signal: controller.signal }) : capturedToken;
      if (generation !== this.#generation) return failed('SessionContextChanged', action);
      if (controller.signal.aborted) return failed('Cancelled', action);
      if (typeof token !== 'string' || !token || /\s/.test(token)) {
        try { await this.#invalidateFromRequest('authentication_required'); }
        catch { return failed('PrivateContextCleanupFailed', action); }
        return failed('AuthenticationRequired', action);
      }
      const url = new URL(path, this.#origin).href;
      const response = await this.#fetch(url, {
        method, headers: { Authorization: `Bearer ${token}`, Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
        body: body === undefined ? undefined : JSON.stringify(body),
        credentials: 'omit', cache: 'no-store', redirect: 'error', referrerPolicy: 'no-referrer', signal: controller.signal,
      });
      if (generation !== this.#generation) return failed('SessionContextChanged', action);
      if (controller.signal.aborted) return failed('Cancelled', action);
      if (response.redirected || (response.url && response.url !== url) || (response.status >= 300 && response.status < 400)) {
        return failed('UnexpectedRedirect', action, response.status);
      }
      if (response.status === 401) {
        // Headers have arrived; preserve this transport solely to consume its error body.
        // Other private requests are cancelled immediately. Explicit caller abort still applies.
        expectedGeneration = generation + 1;
        try { await this.#invalidateFromRequest('session_invalidated', controller); }
        catch { return failed('PrivateContextCleanupFailed', action, response.status); }
      }
      if (expectedGeneration !== this.#generation) return failed('SessionContextChanged', action);
      if (!response.ok) {
        let wire;
        try { wire = await response.json(); }
        catch {
          if (expectedGeneration !== this.#generation) return failed('SessionContextChanged', action);
          if (controller.signal.aborted) return failed('Cancelled', action);
          return failed('MalformedResponse', action, response.status);
        }
        if (expectedGeneration !== this.#generation) return failed('SessionContextChanged', action);
        if (controller.signal.aborted) return failed('Cancelled', action);
        if (!object(wire) || typeof wire.error !== 'string' || !wire.error ||
            (wire.revision !== undefined && !positiveRevision(wire.revision))) {
          return failed('MalformedResponse', action, response.status);
        }
        return failed(wire.error, action, response.status, wire);
      }
      if (validate === null) {
        return response.status === 204 ? { ok: true, status: 204, body: null } : failed('MalformedResponse', action, response.status);
      }
      if (response.status !== 200 || !response.headers.get('content-type')?.toLowerCase().includes('application/json')) {
        return failed('MalformedResponse', action, response.status);
      }
      let wire;
      try { wire = await response.json(); }
      catch {
        if (expectedGeneration !== this.#generation) return failed('SessionContextChanged', action);
        if (controller.signal.aborted) return failed('Cancelled', action);
        return failed('MalformedResponse', action, response.status);
      }
      if (generation !== this.#generation) return failed('SessionContextChanged', action);
      if (controller.signal.aborted) return failed('Cancelled', action);
      if (!validate(wire)) return failed('MalformedResponse', action, response.status);
      return { ok: true, status: response.status, body: wire };
    } catch {
      if (generation !== this.#generation) return failed('SessionContextChanged', action);
      if (controller.signal.aborted) return failed('Cancelled', action);
      return failed('TransportUnavailable', action);
    } finally {
      signal?.removeEventListener('abort', abort);
      this.#pending.delete(controller);
    }
  }
}
