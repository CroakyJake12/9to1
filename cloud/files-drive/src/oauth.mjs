import { base64url, createLocalJWKSet, decodeProtectedHeader, jwtVerify } from 'jose';
import { API_PREFIX, FILES_ORIGIN, FilesHttpError, UUID } from './protocol.mjs';

const AUTHORIZE = 'https://accounts.google.com/o/oauth2/v2/auth';
const TOKEN = 'https://oauth2.googleapis.com/token';
const REVOKE = 'https://oauth2.googleapis.com/revoke';
const CERTS = 'https://www.googleapis.com/oauth2/v3/certs';
export const GOOGLE_DRIVE_REDIRECT_URI = FILES_ORIGIN + API_PREFIX + '/oauth/callback';
const SCOPES = ['openid', 'email', 'https://www.googleapis.com/auth/drive'];
const SCOPE_ALIASES = { 'https://www.googleapis.com/auth/userinfo.email': 'email' };
const GRANT = 'files-google-grant-v1';
const FLOWS = 'files-google-flows-v1';
const CLOCK = 'files-google-clock-v1';
const FLOW_TIME = 10 * 60_000;
const MAX_FLOWS = 8;
const MAX_QUEUE = 16;
const REFRESH_EARLY = 60_000;
const MAX_TOKEN_BYTES = 16_384;
const AUTH_RESPONSE_BYTES = 64 * 1024;
const FORM_HEADERS = { 'content-type': 'application/x-www-form-urlencoded' };
const BASE64URL_32 = /^[A-Za-z0-9_-]{43}$/;
const TOKEN_CHARS = /^[!-~]+$/;
const EMAIL = /^[^\s@]+@[^\s@]+$/;
const encoder = new TextEncoder();
const decoder = new TextDecoder('utf-8', { fatal: true });

function providerError(status, code, message, options) {
  return new FilesHttpError(status, code, message, options);
}

function invalidFlow() {
  return providerError(400, 'DriveOAuthFlowInvalid', 'Start a new Google Drive connection from this 9to1 session.', { notStarted: true });
}

function reconnect(retryAt = null) {
  return providerError(409, 'DriveReconnectRequired', 'Connect Google Drive again in Files.', { notStarted: true, retryAt });
}

function invalidCredentials() {
  return providerError(502, 'DriveOAuthResponseInvalid', 'Google did not confirm the credentials required by Files.');
}

function opaque(value, limit = MAX_TOKEN_BYTES) {
  return typeof value === 'string' && value.length <= limit && TOKEN_CHARS.test(value);
}

function isEmail(value) {
  return typeof value === 'string' && value.length <= 254 && EMAIL.test(value);
}

function config(env) {
  const { OWNER_ACCOUNT_ID: accountId, OWNER_GOOGLE_SUB: sub, OWNER_GOOGLE_EMAIL: email,
    GOOGLE_DRIVE_PROJECT_ID: projectId, GOOGLE_DRIVE_CLIENT_ID: clientId, GOOGLE_DRIVE_CLIENT_SECRET: secret,
    DRIVE_ENCRYPTION_KEY: encodedKey, FILES_DEDICATED_GOOGLE_PROJECT: dedicated } = env ?? {};
  // Google revokes grants project-wide. Production setup must establish that
  // this is the dedicated Files project before asserting this setting.
  if ((dedicated !== true && dedicated !== 'true') || typeof projectId !== 'string' ||
      !/^[a-z][a-z0-9-]{4,28}[a-z0-9]$/.test(projectId) || !UUID.test(accountId ?? '') ||
      !opaque(sub, 255) || !isEmail(email) || !opaque(clientId, 512) || !opaque(secret, 512) ||
      typeof encodedKey !== 'string' || !BASE64URL_32.test(encodedKey)) {
    throw providerError(503, 'DriveOAuthNotConfigured', 'Private Google Drive access is not configured.', { notStarted: true });
  }
  const key = base64url.decode(encodedKey);
  if (key.byteLength !== 32 || base64url.encode(key) !== encodedKey) {
    throw providerError(503, 'DriveOAuthNotConfigured', 'Private Google Drive access is not configured.', { notStarted: true });
  }
  return { accountId, sub, email: email.toLowerCase(), clientId, projectId, secret, key };
}

async function sha256(value) {
  return new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(value)));
}

function randomToken() {
  return base64url.encode(crypto.getRandomValues(new Uint8Array(32)));
}

function validScopes(value) {
  if (typeof value !== 'string' || value.length > 2048) return false;
  const scopes = new Set(value.trim().split(/ +/).map(scope => Object.hasOwn(SCOPE_ALIASES, scope) ? SCOPE_ALIASES[scope] : scope));
  return scopes.size === SCOPES.length && SCOPES.every(scope => scopes.has(scope));
}

function tokens(value, issuedAt, prior) {
  if (!value || typeof value !== 'object' || !opaque(value.access_token) ||
      typeof value.token_type !== 'string' || value.token_type.toLowerCase() !== 'bearer' ||
      !Number.isSafeInteger(value.expires_in) || value.expires_in < 1 || value.expires_in > 86_400) {
    throw invalidCredentials();
  }
  if ((value.scope !== undefined || !prior) && !validScopes(value.scope)) {
    throw providerError(403, 'DriveScopeInsufficient', 'Google did not grant the account and Drive access that Files needs.');
  }
  const refreshToken = value.refresh_token === undefined ? prior?.refreshToken : value.refresh_token;
  if (!opaque(refreshToken)) throw invalidCredentials();
  return { accessToken: value.access_token, refreshToken, expiresAt: issuedAt + value.expires_in * 1000 };
}

function retryAfter(headers, now) {
  const value = headers?.get('retry-after');
  let delay = 30_000;
  if (value && /^\d{1,9}$/.test(value)) delay = Number(value) * 1000;
  else if (value && Number.isFinite(Date.parse(value))) delay = Date.parse(value) - now;
  return now + Math.min(300_000, Math.max(5_000, delay));
}

function certTime(headers) {
  const cache = headers?.get('cache-control') ?? '';
  if (/\b(?:no-cache|no-store)\b/i.test(cache)) return 0;
  const maxAge = /(?:^|,)\s*max-age=(\d{1,9})(?=\s*(?:,|$))/i.exec(cache);
  const age = Number(headers?.get('age') ?? 0);
  const seconds = maxAge ? Number(maxAge[1]) : 300;
  return Math.max(0, Math.min(3600, seconds - (Number.isFinite(age) && age >= 0 ? age : 0))) * 1000;
}

export class GoogleDriveOAuth {
  #storage;
  #transport;
  #config;
  #now;
  #key;
  #certs;
  #queue = Promise.resolve();
  #pending = 0;

  constructor(storage, env, transport, { now = Date.now } = {}) {
    if (!storage?.kv || typeof storage.transactionSync !== 'function' || typeof storage.sync !== 'function' ||
        typeof transport?.request !== 'function' || typeof transport?.json !== 'function' || typeof now !== 'function') {
      throw new TypeError('Google Drive OAuth requires metered transport and durable SQLite storage.');
    }
    this.#storage = storage;
    this.#transport = transport;
    this.#config = config(env);
    this.#now = now;
  }

  async begin({ accountId, sessionDigest, signal }) {
    const session = this.#session(accountId, sessionDigest);
    return this.#exclusive(signal, async () => {
      const { grant } = await this.#readGrant();
      if (grant?.phase === 'revoking') throw reconnect();
      const now = this.#time();
      if (grant?.phase === 'uncertain' && grant.retryAt > now) throw reconnect(grant.retryAt);
      const state = randomToken();
      const nonce = randomToken();
      const verifier = randomToken();
      const [stateHash, verifierHash] = await Promise.all([sha256(state), sha256(verifier)]);
      const id = base64url.encode(stateHash);
      const challenge = base64url.encode(verifierHash);
      const box = await this.#seal('flow:' + id + ':' + session, { nonce, verifier });
      signal?.throwIfAborted();
      this.#storage.transactionSync(() => {
        const flows = this.#flows(now);
        if (flows.length >= MAX_FLOWS) {
          const retryAt = Math.min(...flows.map(item => item.expiresAt));
          throw providerError(429, 'DriveOAuthFlowsFull', 'Finish an existing Google connection or wait to start another.', { notStarted: true, retryAt });
        }
        flows.push({ id, session, expiresAt: now + FLOW_TIME, box });
        this.#storage.kv.put(FLOWS, flows);
      });
      await this.#storage.sync();
      const url = new URL(AUTHORIZE);
      url.search = new URLSearchParams({
        client_id: this.#config.clientId, redirect_uri: GOOGLE_DRIVE_REDIRECT_URI, response_type: 'code',
        scope: SCOPES.join(' '), state, nonce, code_challenge: challenge, code_challenge_method: 'S256',
        access_type: 'offline', prompt: 'consent select_account', include_granted_scopes: 'false',
      });
      return { url: url.href };
    });
  }

  async complete({ code, state, accountId, sessionDigest, signal }) {
    const session = this.#session(accountId, sessionDigest);
    if (!opaque(code, 2048) || typeof state !== 'string' || !BASE64URL_32.test(state)) throw invalidFlow();
    return this.#exclusive(signal, async () => {
      const { box: prior, grant: oldGrant } = await this.#readGrant();
      if (oldGrant?.phase === 'revoking') throw reconnect();
      const id = base64url.encode(await sha256(state));
      // Keep the high-water clock even if the flow transaction refuses an
      // expired or unknown state, so a later clock rollback cannot revive it.
      const now = this.#time();
      const flow = this.#storage.transactionSync(() => {
        const flows = this.#flows(now);
        const index = flows.findIndex(item => item.id === id && item.session === session);
        if (index < 0) throw invalidFlow();
        const [item] = flows.splice(index, 1);
        this.#storage.kv.put(FLOWS, flows);
        return item;
      });
      await this.#storage.sync();
      const { nonce, verifier } = await this.#open('flow:' + id + ':' + session, flow.box);
      const startedAt = this.#time();
      if (startedAt >= flow.expiresAt) throw invalidFlow();
      const result = await this.#token({
        code, code_verifier: verifier, redirect_uri: GOOGLE_DRIVE_REDIRECT_URI, grant_type: 'authorization_code',
      }, signal);
      if (result.status !== 200) throw this.#exchangeError(result);
      const credential = tokens(result.value, startedAt);
      const identity = await this.#verifyIdentity(result.value?.id_token, nonce, credential.accessToken, signal);
      signal?.throwIfAborted();
      await this.#storeGrant(prior, {
        phase: 'active', ...credential, ...identity, scope: SCOPES.join(' '), retryAt: 0,
      }, true);
      return { connected: true, accountEmail: identity.email };
    });
  }

  async getAccessToken({ signal } = {}) {
    return this.#exclusive(signal, async () => {
      const { box, grant } = await this.#readGrant();
      const now = this.#time();
      if (!grant || grant.phase !== 'active') throw reconnect(grant?.retryAt || null);
      signal?.throwIfAborted();
      if (grant.expiresAt - now > REFRESH_EARLY) return grant.accessToken;
      if (grant.retryAt > now) throw this.#backoff(grant.retryAt);
      return this.#refresh(box, grant, signal);
    });
  }

  async rejectAccessToken(token) {
    return this.#exclusive(undefined, async () => {
      const { box, grant } = await this.#readGrant();
      // A delayed 401 for an old request cannot invalidate a newly rotated token.
      if (grant?.phase !== 'active' || grant.accessToken !== token) return false;
      await this.#storeGrant(box, { ...grant, phase: 'uncertain', retryAt: 0 });
      return true;
    });
  }

  async status() {
    return this.#exclusive(undefined, async () => {
      const { grant, incompatible } = await this.#readGrant();
      if (incompatible) return { connected: false, accountEmail: null, reauthorizationRequired: true };
      if (!grant) return { connected: false, accountEmail: null };
      if (grant.phase === 'revoking') return { connected: false, accountEmail: null, revocationPending: true };
      if (grant.phase !== 'active') {
        return { connected: false, accountEmail: null, reauthorizationRequired: true, retryAt: grant.retryAt || null };
      }
      const result = { connected: true, accountEmail: grant.email };
      if (grant.retryAt > this.#time()) result.retryAt = grant.retryAt;
      return result;
    });
  }

  async disconnect({ signal } = {}) {
    return this.#exclusive(signal, async () => {
      const { box, grant, incompatible } = await this.#readGrant();
      if (!grant) {
        await this.#storeGrant(box, null, true);
        if (incompatible) throw this.#unconfirmedRevocation();
        return { connected: false, accountEmail: null };
      }
      const marker = await this.#storeGrant(box, { ...grant, phase: 'revoking' }, true);
      let confirmed = false;
      try {
        const result = await this.#transport.request(REVOKE, {
          kind: 'auth', method: 'POST', headers: FORM_HEADERS,
          body: new URLSearchParams({ token: grant.refreshToken }).toString(), maxResponseBytes: AUTH_RESPONSE_BYTES, signal,
        });
        confirmed = result.status === 200;
      } catch {
        // A lost reply cannot tell whether Google removed its permission.
      }
      await this.#storeGrant(marker, null);
      if (!confirmed) throw this.#unconfirmedRevocation();
      return { connected: false, accountEmail: null };
    });
  }

  #exclusive(signal, operation) {
    if (this.#pending >= MAX_QUEUE) {
      throw providerError(429, 'FilesBusy', 'Google Drive connection work is already running.', { notStarted: true, retryAt: this.#time() + 1000 });
    }
    this.#pending++;
    const job = this.#queue.then(() => {
      signal?.throwIfAborted();
      return operation();
    }).finally(() => { this.#pending--; });
    this.#queue = job.catch(() => {});
    return job;
  }

  #session(accountId, digest) {
    if (accountId !== this.#config.accountId || typeof digest !== 'string') throw invalidFlow();
    if (/^[a-f0-9]{64}$/i.test(digest)) return digest.toLowerCase();
    if (!BASE64URL_32.test(digest)) throw invalidFlow();
    return digest;
  }

  #time() {
    const physical = this.#now();
    if (!Number.isSafeInteger(physical) || physical < 0 || physical > 8_640_000_000_000_000 - 86_400_000) {
      throw new Error('Google Drive OAuth clock is unavailable.');
    }
    const previous = this.#storage.kv.get(CLOCK) ?? 0;
    if (physical > previous) this.#storage.kv.put(CLOCK, physical);
    return Math.max(physical, previous);
  }

  #flows(now) {
    return (this.#storage.kv.get(FLOWS) ?? []).filter(item => item.expiresAt > now);
  }

  async #token(fields, signal) {
    const result = await this.#transport.request(TOKEN, {
      kind: 'auth', method: 'POST', headers: FORM_HEADERS, maxResponseBytes: AUTH_RESPONSE_BYTES, signal,
      body: new URLSearchParams({ client_id: this.#config.clientId, client_secret: this.#config.secret, ...fields }).toString(),
    });
    let value = null;
    try {
      if (result.bytes.byteLength) value = JSON.parse(decoder.decode(result.bytes));
    } catch {
      if (result.status === 200) throw invalidCredentials();
    }
    return { status: result.status, headers: result.headers, value };
  }

  #exchangeError(result) {
    if (result.status === 429 || result.status >= 500) return this.#backoff(retryAfter(result.headers, this.#time()));
    return providerError(400, 'DriveOAuthCodeRejected', 'Google did not accept the connection. Start a new connection in Files.');
  }

  #backoff(retryAt) {
    return providerError(503, 'DriveAuthTemporarilyUnavailable', 'Google Drive authentication is temporarily unavailable.', { retryAt });
  }

  #unconfirmedRevocation() {
    return providerError(503, 'DriveRevocationUnconfirmed',
      'Files is disconnected. Google’s permission removal could not be confirmed.');
  }

  async #refresh(box, grant, signal) {
    signal?.throwIfAborted();
    const startedAt = this.#time();
    const marker = await this.#storeGrant(box, { ...grant, phase: 'refreshing', retryAt: 0 });
    if (signal?.aborted) {
      await this.#storeGrant(marker, grant);
      signal.throwIfAborted();
    }
    let result;
    try {
      result = await this.#token({ grant_type: 'refresh_token', refresh_token: grant.refreshToken }, signal);
    } catch (error) {
      if (error?.notStarted === true) {
        await this.#storeGrant(marker, grant);
        throw error;
      }
      await this.#storeGrant(marker, { ...grant, phase: 'uncertain', retryAt: 0 });
      throw reconnect();
    }
    if (result.status === 429) {
      const retryAt = retryAfter(result.headers, this.#time());
      await this.#storeGrant(marker, { ...grant, retryAt });
      throw this.#backoff(retryAt);
    }
    if (result.status >= 500) {
      // A server error may have lost the response to a completed rotation.
      const retryAt = retryAfter(result.headers, this.#time());
      await this.#storeGrant(marker, { ...grant, phase: 'uncertain', retryAt });
      throw reconnect(retryAt);
    }
    if (result.status === 400 && result.value?.error === 'invalid_grant') {
      await this.#storeGrant(marker, null);
      throw reconnect();
    }
    if (result.status !== 200) {
      await this.#storeGrant(marker, { ...grant, phase: 'uncertain', retryAt: 0 });
      throw reconnect();
    }
    let refreshed;
    try {
      const credential = tokens(result.value, startedAt, grant);
      refreshed = { ...grant, ...credential, phase: 'active', retryAt: 0 };
    } catch (error) {
      await this.#storeGrant(marker, { ...grant, phase: 'uncertain', retryAt: 0 });
      throw error;
    }
    await this.#storeGrant(marker, refreshed);
    signal?.throwIfAborted();
    if (refreshed.expiresAt <= this.#time()) throw invalidCredentials();
    return refreshed.accessToken;
  }

  async #verifyIdentity(idToken, nonce, accessToken, signal) {
    if (!opaque(idToken)) throw invalidCredentials();
    let header;
    try { header = decodeProtectedHeader(idToken); } catch { throw invalidCredentials(); }
    if (header.alg !== 'RS256' || !opaque(header.kid, 128)) throw invalidCredentials();
    const keys = await this.#googleKeys(signal);
    let payload;
    try {
      ({ payload } = await jwtVerify(idToken, keys, {
        algorithms: ['RS256'], issuer: ['https://accounts.google.com', 'accounts.google.com'],
        audience: this.#config.clientId, subject: this.#config.sub,
        currentDate: new Date(this.#time()), maxTokenAge: '10 minutes', clockTolerance: 5,
        requiredClaims: ['exp', 'iat', 'nonce', 'email', 'email_verified'],
      }));
    } catch {
      throw providerError(403, 'DriveOwnerUnverified', 'Google did not verify the configured Files owner.');
    }
    const exactAudience = payload.aud === this.#config.clientId ||
      (Array.isArray(payload.aud) && payload.aud.length === 1 && payload.aud[0] === this.#config.clientId);
    if (!exactAudience || (payload.azp !== undefined && payload.azp !== this.#config.clientId) ||
        payload.nonce !== nonce || payload.email_verified !== true || !isEmail(payload.email) ||
        payload.email.toLowerCase() !== this.#config.email || payload.exp * 1000 <= this.#time()) {
      throw providerError(403, 'DriveOwnerUnverified', 'Google did not verify the configured Files owner.');
    }
    if (payload.at_hash !== undefined && payload.at_hash !== base64url.encode((await sha256(accessToken)).slice(0, 16))) {
      throw invalidCredentials();
    }
    return { sub: payload.sub, email: payload.email };
  }

  async #googleKeys(signal) {
    const now = this.#time();
    if (this.#certs?.expiresAt > now) return this.#certs.keys;
    const result = await this.#transport.json(CERTS, { kind: 'auth', maxResponseBytes: AUTH_RESPONSE_BYTES, signal });
    if (result.status !== 200) throw this.#exchangeError(result);
    const keys = result.value?.keys;
    if (!Array.isArray(keys) || keys.length < 1 || keys.length > 16) throw invalidCredentials();
    let resolver;
    try { resolver = createLocalJWKSet({ keys }); } catch { throw invalidCredentials(); }
    this.#certs = { keys: resolver, expiresAt: now + certTime(result.headers) };
    return resolver;
  }

  async #readGrant() {
    const box = this.#storage.kv.get(GRANT);
    if (box === undefined) return { box };
    let grant;
    try { grant = await this.#open('grant', box); } catch (error) {
      if (error?.code !== 'DriveReconnectRequired') throw error;
      return { box, incompatible: true };
    }
    if (!grant || !['active', 'refreshing', 'uncertain', 'revoking'].includes(grant.phase) ||
        grant.sub !== this.#config.sub || !isEmail(grant.email) || grant.email.toLowerCase() !== this.#config.email ||
        !validScopes(grant.scope) || !opaque(grant.accessToken) || !opaque(grant.refreshToken) ||
        !Number.isSafeInteger(grant.expiresAt) || !Number.isSafeInteger(grant.retryAt)) {
      return { box, incompatible: true };
    }
    return { box, grant };
  }

  async #storeGrant(previous, grant, clearFlows = false) {
    const next = grant ? await this.#seal('grant', grant) : undefined;
    this.#storage.transactionSync(() => {
      if (this.#storage.kv.get(GRANT) !== previous) throw reconnect();
      if (clearFlows) this.#storage.kv.delete(FLOWS);
      if (next === undefined) this.#storage.kv.delete(GRANT);
      else this.#storage.kv.put(GRANT, next);
    });
    await this.#storage.sync();
    return next;
  }

  #associatedData(purpose) {
    const { accountId, projectId, clientId } = this.#config;
    return encoder.encode(JSON.stringify(['9to1 Files Google Drive v1', accountId, projectId, clientId, purpose]));
  }

  #cryptoKey() {
    this.#key ??= crypto.subtle.importKey('raw', this.#config.key, 'AES-GCM', false, ['encrypt', 'decrypt']);
    return this.#key;
  }

  async #seal(purpose, value) {
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const encrypted = await crypto.subtle.encrypt({
      name: 'AES-GCM', iv, additionalData: this.#associatedData(purpose),
    }, await this.#cryptoKey(), encoder.encode(JSON.stringify(value)));
    return base64url.encode(iv) + '.' + base64url.encode(new Uint8Array(encrypted));
  }

  async #open(purpose, box) {
    try {
      const [iv, ciphertext] = box.split('.');
      const plaintext = await crypto.subtle.decrypt({
        name: 'AES-GCM', iv: base64url.decode(iv), additionalData: this.#associatedData(purpose),
      }, await this.#cryptoKey(), base64url.decode(ciphertext));
      return JSON.parse(decoder.decode(plaintext));
    } catch {
      throw reconnect();
    }
  }
}
