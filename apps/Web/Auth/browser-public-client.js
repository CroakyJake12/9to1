// Public-client PKCE/OIDC consumer. Authority: maintained CAKE ID aedc29ec + BROWSER_FIXTURE.md.
// There are no issuer/client/redirect defaults, local account grants, refresh-token persistence or auth_revision claims.
import { createRemoteJWKSet, jwtVerify, customFetch } from 'jose';

const problem = code => Object.assign(new Error(code), { code });
const base64url = bytes => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(value);
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
function endpoint(value, allowLoopback) {
  let url; try { url = new URL(value); } catch { throw problem('InvalidConfiguration'); }
  const isolated = allowLoopback === true && ['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) && url.protocol === 'http:';
  if (url.username || url.password || url.search || url.hash || (url.protocol !== 'https:' && !isolated)) throw problem('InvalidConfiguration');
  return url;
}
export function validatePublicClientConfiguration(input, origin) {
  if (!object(input)) throw problem('InvalidConfiguration');
  const allowed = new Set(['issuer', 'apiResource', 'clientId', 'redirectUri', 'scopes', 'allowLoopbackForIsolatedTests']);
  if (Object.keys(input).some(key => !allowed.has(key))) throw problem('InvalidConfiguration');
  const snapshot = Object.fromEntries(Object.entries(input));
  const issuer = endpoint(snapshot.issuer, snapshot.allowLoopbackForIsolatedTests);
  const api = endpoint(snapshot.apiResource, snapshot.allowLoopbackForIsolatedTests);
  const redirect = endpoint(snapshot.redirectUri, false);
  if (api.pathname !== '/' || redirect.origin !== origin || typeof snapshot.clientId !== 'string' || !snapshot.clientId ||
      typeof snapshot.scopes !== 'string' || !snapshot.scopes.split(/\s+/).includes('openid') ||
      (snapshot.allowLoopbackForIsolatedTests !== undefined && typeof snapshot.allowLoopbackForIsolatedTests !== 'boolean')) throw problem('InvalidConfiguration');
  return Object.freeze({ issuer: issuer.href, apiResource: api.origin, clientId: snapshot.clientId,
    redirectUri: redirect.href, scopes: snapshot.scopes, allowLoopbackForIsolatedTests: snapshot.allowLoopbackForIsolatedTests === true });
}

/** Callback transport only. It grants nothing and never starts .NET. Parent verifies its exact source/state and real tokens. */
export function handleOAuthPopupCallback(configuration, win = globalThis.window) {
  if (!configuration || !win) return false;
  const config = validatePublicClientConfiguration(configuration, win.location.origin);
  const expected = new URL(config.redirectUri);
  if (win.location.origin !== expected.origin || win.location.pathname !== expected.pathname) return false;
  let parent;
  try { parent = win.opener; if (!parent || parent.closed || parent.location.origin !== expected.origin) throw problem('CallbackParentUnavailable'); }
  catch { throw problem('CallbackParentUnavailable'); }
  const callbackUri = win.location.href;
  // Remove the one-use code from this page's current history before parent processing; no browser storage is used.
  win.history.replaceState(null, '', expected.pathname);
  parent.postMessage({ type: 'nineToOne.oauth.callback', callbackUri }, expected.origin);
  return true;
}

export class BrowserPublicClient {
  #config; #window; #fetch; #crypto; #before; #verifyAccount; #verified; #signInFailed; #expired; #failure;
  #issued = new Set(); #cleanupErrors = []; #cleanupTask; #beginExpiry;
  #token = null; #expires = 0; #timer; #pending = null; #generation = 0; #disposed = false; #discovery; #jwks;
  constructor({ configuration, window: win = globalThis.window, fetch: transport = globalThis.fetch,
    crypto = globalThis.crypto, onBeforeSignIn, verifyCurrentAccount, onVerifiedIdentity, onSignInFailed, onTokenExpired, onFailure, beginTokenExpiryInvalidation = undefined }) {
    if (!win || typeof transport !== 'function' || !crypto?.subtle || typeof onBeforeSignIn !== 'function' ||
        typeof verifyCurrentAccount !== 'function' || typeof onVerifiedIdentity !== 'function' || typeof onSignInFailed !== 'function' || typeof onTokenExpired !== 'function' || typeof onFailure !== 'function') throw problem('InvalidConfiguration');
    if(beginTokenExpiryInvalidation !== undefined && typeof beginTokenExpiryInvalidation !== 'function') throw problem('InvalidConfiguration');
    this.#beginExpiry = beginTokenExpiryInvalidation;
    this.#config = validatePublicClientConfiguration(configuration, win.location.origin);
    // Native fetch requires its Window receiver; injected transports retain their existing receiver.
    this.#window = win; this.#fetch = transport === globalThis.fetch ? transport.bind(globalThis) : transport; this.#crypto = crypto; this.#before = onBeforeSignIn;
    this.#verifyAccount = verifyCurrentAccount; this.#verified = onVerifiedIdentity; this.#signInFailed = onSignInFailed; this.#expired = onTokenExpired; this.#failure = onFailure;
  }
  get configuration() { return this.#config; } // Public configuration only.
  getAccessToken() { return !this.#disposed && this.#token && Date.now() < this.#expires ? this.#token : null; }
  clearToken({ cancelPending = true } = {}) {
    this.#generation++; this.#token = null; this.#expires = 0; clearTimeout(this.#timer);
    if (cancelPending) this.#pending?.controller.abort();
  }
  async #json(url, options, signal) {
    const response = await this.#fetch(url, { ...options, credentials: 'omit', cache: 'no-store', redirect: 'error', referrerPolicy: 'no-referrer', signal });
    if (signal.aborted) throw problem('Cancelled');
    if (!response.ok || response.redirected || (response.url && response.url !== url)) throw problem('ProviderRequestFailed');
    let body; try { body = await response.json(); } catch { throw problem(signal.aborted ? 'Cancelled' : 'MalformedProviderResponse'); }
    if (signal.aborted) throw problem('Cancelled');
    if (!object(body)) throw problem('MalformedProviderResponse');
    return body;
  }
  async #metadata(signal) {
    if (this.#discovery) return this.#discovery;
    const issuer = new URL(this.#config.issuer);
    const discovery = await this.#json(`${issuer.href.replace(/\/$/, '')}/.well-known/openid-configuration`, {}, signal);
    if (discovery.issuer !== this.#config.issuer) throw problem('IssuerMismatch');
    for (const field of ['authorization_endpoint', 'token_endpoint', 'jwks_uri']) {
      const url = endpoint(discovery[field], this.#config.allowLoopbackForIsolatedTests);
      if (url.origin !== issuer.origin) throw problem('UnexpectedProviderOrigin');
    }
    this.#discovery = Object.freeze({ issuer: discovery.issuer, authorization_endpoint: discovery.authorization_endpoint,
      token_endpoint: discovery.token_endpoint, jwks_uri: discovery.jwks_uri });
    this.#jwks = createRemoteJWKSet(new URL(discovery.jwks_uri), { [customFetch]: (url, options) => this.#fetch(url, {
      ...options, credentials: 'omit', cache: 'no-store', redirect: 'error', referrerPolicy: 'no-referrer' }) });
    return this.#discovery;
  }
  async #exchange(pending, callbackUri, discovery) {
    const callback = new URL(callbackUri); const expected = new URL(this.#config.redirectUri);
    if (callback.origin !== expected.origin || callback.pathname !== expected.pathname || callback.username || callback.password || callback.hash) throw problem('CallbackTargetMismatch');
    if (callback.searchParams.getAll('state').length !== 1 || callback.searchParams.get('state') !== pending.state) throw problem('StateMismatch');
    if (callback.searchParams.has('iss') && (callback.searchParams.getAll('iss').length !== 1 || callback.searchParams.get('iss') !== this.#config.issuer)) throw problem('IssuerMismatch');
    if (callback.searchParams.has('error')) throw problem('AuthorizationDenied');
    if (callback.searchParams.getAll('code').length !== 1 || !callback.searchParams.get('code')) throw problem('AuthorizationCodeMissing');
    const tokens = await this.#json(discovery.token_endpoint, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({ grant_type: 'authorization_code', client_id: this.#config.clientId, redirect_uri: this.#config.redirectUri,
        code: callback.searchParams.get('code'), code_verifier: pending.verifier, resource: this.#config.apiResource }) }, pending.controller.signal);
    if (tokens.token_type?.toLowerCase() !== 'bearer' || typeof tokens.access_token !== 'string' || !tokens.access_token || typeof tokens.id_token !== 'string') throw problem('MalformedProviderResponse');
    let id, access;
    try {
      ({ payload: id } = await jwtVerify(tokens.id_token, this.#jwks, { issuer: this.#config.issuer, audience: this.#config.clientId, requiredClaims: ['exp', 'iat', 'sub', 'nonce'] }));
      ({ payload: access } = await jwtVerify(tokens.access_token, this.#jwks, { issuer: this.#config.issuer, audience: this.#config.apiResource, requiredClaims: ['exp', 'iat', 'sub', 'sid'] }));
    } catch { throw problem('TokenVerificationFailed'); }
    if (id.nonce !== pending.nonce || (Array.isArray(id.aud) && id.aud.length > 1 && id.azp !== this.#config.clientId) ||
        (id.azp !== undefined && id.azp !== this.#config.clientId) || !uuid(id.sub) || access.sub !== id.sub || !uuid(access.sid) ||
        !Number.isSafeInteger(access.exp) || access.exp * 1000 <= Date.now()) throw problem('TokenBindingMismatch');
    if (pending.controller.signal.aborted || pending.generation !== this.#generation || this.#disposed) throw problem('SessionContextChanged');
    this.#token = tokens.access_token; this.#expires = access.exp * 1000;
    try { await pending.callbacks.verifyAccount(id.sub, pending.controller.signal); }
    catch { if (pending.generation === this.#generation) this.clearToken({ cancelPending: false }); throw problem('CurrentAccountVerificationFailed'); }
    if (pending.controller.signal.aborted || pending.generation !== this.#generation || this.#disposed) {
      if (pending.generation === this.#generation) this.clearToken({ cancelPending: false }); throw problem('SessionContextChanged');
    }
    try { await pending.callbacks.verified(); }
    catch { if (pending.generation === this.#generation) this.clearToken({ cancelPending: false }); throw problem('PrivateContextCleanupFailed'); }
    if (pending.controller.signal.aborted || pending.generation !== this.#generation || this.#disposed) throw problem('SessionContextChanged');
    const tokenGeneration = this.#generation;
    this.#timer = setTimeout(() => {
      if(this.#disposed || tokenGeneration !== this.#generation) return;
      const expired=this.#expired, failure=this.#failure, begin=this.#beginExpiry;
      this.#issue('expiry',async()=>{
        try {
          this.clearToken({cancelPending:false}); // Credential/generation fence without callbacks.
          if(begin !== undefined) {
            try {
              const acknowledgement=begin('token_expired');
              if(acknowledgement !== undefined) {
                if(acknowledgement && typeof acknowledgement.then === 'function') Promise.resolve(acknowledgement).catch(()=>{});
                throw problem('PrivateContextCleanupFailed');
              }
            } finally { this.#pending?.controller.abort(); } // Actual native fence first.
          } else this.clearToken(); // Legacy default remains qualified; no synchronous owner fence claimed.
          await expired(); // Explicit composition supplies native JOIN only; never joins own broker task.
        } catch(error) {
          this.#cleanupErrors.push(error);
          try{failure('PrivateContextCleanupFailed');}catch(observerError){this.#cleanupErrors.push(observerError);}
          throw error;
        }
      }).catch(()=>{}); // Retained cleanup failure remains observable through async disposal/join.
    }, Math.min(this.#expires - Date.now(), 2_147_483_647));
    // Refresh tokens are deliberately not retained. Current identity/API access remains server-authoritative.
  }
  #issue(action, factory) {
    if(this.#disposed) return Promise.reject(problem('ServiceUnavailable'));
    let settle; const completion = new Promise(resolve => { settle = resolve; });
    const entry = { action, completion }; this.#issued.add(entry); // Before callback/fetch/window.open.
    let actual; try { actual = Promise.resolve(factory()); } catch (error) { actual = Promise.reject(error); }
    actual.then(() => { this.#issued.delete(entry); settle(); }, error => {
      if(error?.code === 'PrivateContextCleanupFailed') this.#cleanupErrors.push(error);
      this.#issued.delete(entry); settle();
    });
    return actual;
  }
  async joinIssuedWork() {
    while(this.#issued.size) await Promise.all([...this.#issued].map(entry=>entry.completion));
    if(this.#cleanupErrors.length) throw new AggregateError([...this.#cleanupErrors], 'Broker lifecycle cleanup failed.');
  }
  hasOutstandingWork() { return this.#issued.size !== 0; }
  signIn(options = {}) {
    if(this.#disposed) return Promise.reject(problem('ServiceUnavailable'));
    // Actual prior exchange may outlive cancelled outer sign-in; preserve original single-sign-in intent.
    if(this.#issued.size) return Promise.reject(problem('SignInInProgress'));
    return this.#issue('sign-in',()=>this.#signInCore(options));
  }
  async #signInCore({ signal } = {}) {
    if (this.#disposed) throw problem('ServiceUnavailable');
    if (this.#pending) throw problem('SignInInProgress');
    if (signal?.aborted) throw problem('Cancelled');
    const callbacks = { before:this.#before, verifyAccount:this.#verifyAccount, verified:this.#verified, signInFailed:this.#signInFailed, failure:this.#failure };
    const popup = this.#window.open('about:blank', `nine-to-one-${base64url(this.#crypto.getRandomValues(new Uint8Array(16)))}`, 'popup,width=520,height=720');
    if (!popup) throw problem('PopupUnavailable');
    if(this.#disposed || signal?.aborted) { try{popup.close();}catch{} throw problem('Cancelled'); }
    const controller = new AbortController(); const abort = () => controller.abort();
    signal?.addEventListener('abort', abort, { once: true });
    const pending = { popup, controller, generation: null, state: null, nonce: null, verifier: null, claimed: false, callbacks };
    this.#pending = pending;
    let listener, poll;
    try {
      await callbacks.before(controller.signal);
      if (controller.signal.aborted || this.#disposed) throw problem('Cancelled');
      pending.generation = this.#generation;
      pending.state = base64url(this.#crypto.getRandomValues(new Uint8Array(32)));
      pending.nonce = base64url(this.#crypto.getRandomValues(new Uint8Array(32)));
      pending.verifier = base64url(this.#crypto.getRandomValues(new Uint8Array(48)));
      const challenge = base64url(new Uint8Array(await this.#crypto.subtle.digest('SHA-256', new TextEncoder().encode(pending.verifier))));
      const discovery = await this.#metadata(controller.signal);
      if (controller.signal.aborted || pending.generation !== this.#generation) throw problem('SessionContextChanged');
      const completion = new Promise((resolve, reject) => {
        listener = event => {
          if (event.origin !== new URL(this.#config.redirectUri).origin || event.source !== popup || event.data?.type !== 'nineToOne.oauth.callback' || pending.claimed) return;
          pending.claimed = true;
          this.#issue('exchange',()=>this.#exchange(pending, event.data.callbackUri, discovery)).then(resolve, reject);
        };
        this.#window.addEventListener('message', listener);
        controller.signal.addEventListener('abort', () => reject(problem('Cancelled')), { once: true });
        poll = setInterval(() => { if (popup.closed && !pending.claimed) reject(problem('PopupClosed')); }, 100);
      });
      const authorization = new URL(discovery.authorization_endpoint);
      authorization.search = new URLSearchParams({ response_type: 'code', client_id: this.#config.clientId, redirect_uri: this.#config.redirectUri,
        scope: this.#config.scopes, state: pending.state, nonce: pending.nonce, code_challenge: challenge, code_challenge_method: 'S256', resource: this.#config.apiResource });
      popup.location.replace(authorization.href);
      await completion;
    } catch (error) {
      this.clearToken({ cancelPending: false });
      let code = controller.signal.aborted ? 'Cancelled' : typeof error?.code === 'string' ? error.code : 'ProviderUnavailable';
      try { await callbacks.signInFailed(); } catch { code = 'PrivateContextCleanupFailed'; }
      try{callbacks.failure(code);}catch(error){this.#cleanupErrors.push(error);throw error;} throw problem(code);
    } finally {
      signal?.removeEventListener('abort', abort); this.#window.removeEventListener('message', listener); clearInterval(poll);
      try { popup.close(); } catch {}
      if (this.#pending === pending) this.#pending = null;
      pending.verifier = null; pending.nonce = null; pending.state = null; pending.callbacks = null;
    }
  }
  revokePrivateContext() {
    this.#disposed=true; this.clearToken({cancelPending:false});
    if(this.#pending) { this.#pending.verifier=null; this.#pending.nonce=null; this.#pending.state=null; }
    this.#before=null;this.#verifyAccount=null;this.#verified=null;this.#signInFailed=null;
    this.#expired=null;this.#failure=null;this.#beginExpiry=undefined;
    this.#discovery=undefined;this.#jwks=undefined;
  }
  disposeAsync() {
    if(this.#cleanupTask) return this.#cleanupTask;
    this.revokePrivateContext(); // Root MUST have synchronously revoked native owners before cancellation.
    let resolve,reject;this.#cleanupTask=new Promise((a,b)=>{resolve=a;reject=b;}); // Before abort reentrancy.
    try{this.#pending?.controller.abort();}catch(error){this.#cleanupErrors.push(error);}
    this.joinIssuedWork().then(resolve,reject);return this.#cleanupTask;
  }
  dispose() { this.disposeAsync().catch(()=>{}); } // Legacy caller gets no full-drain receipt.
}
