// One-use same-tab PKCE handoff. This stores no account, authorization code or token.
const key = '9to1.account.redirect.v1';
const lifetimeMs = 10 * 60 * 1000;
const receipts = new WeakMap();
const problem = code => Object.assign(new Error(code), { code });
const random = value => typeof value === 'string' && /^[A-Za-z0-9_-]{43,128}$/.test(value);
const binding = config => JSON.stringify([config.issuer, config.apiResource, config.clientId, config.redirectUri, config.scopes]);
const fragment = value => typeof value === 'string' && value.length <= 4096 &&
  (value === '' || (value.startsWith('#/') && !/[\r\n\u0000]/.test(value))) ? value : '';

export function saveRedirectTransaction(config, win, pending, now = Date.now()) {
  if (![pending.state, pending.nonce, pending.verifier].every(random)) throw problem('InvalidRedirectTransaction');
  const value = JSON.stringify({ version: 1, binding: binding(config), createdAt: now,
    state: pending.state, nonce: pending.nonce, verifier: pending.verifier, returnFragment: fragment(win.location.hash) });
  try {
    win.sessionStorage.setItem(key, value);
    if (win.sessionStorage.getItem(key) !== value) throw problem('RedirectStorageUnavailable');
  } catch {
    try { win.sessionStorage.removeItem(key); } catch { }
    throw problem('RedirectStorageUnavailable');
  }
}

export function clearRedirectTransaction(win) {
  try { win.sessionStorage.removeItem(key); } catch { throw problem('RedirectStorageUnavailable'); }
}

// Capture before application startup: remove storage and callback query before exchange.
// The opaque receipt can be consumed only once by this module's current instance.
export function captureRedirectCallback(config, win, now = Date.now()) {
  const expected = new URL(config.redirectUri);
  if (win.location.origin !== expected.origin || win.location.pathname !== expected.pathname) {
    try { clearRedirectTransaction(win); } catch { } // Sign-in itself fails closed if storage is unavailable.
    return null;
  }
  const receipt = Object.freeze({ callback: true });
  let raw, transaction, callbackUri = win.location.href, failure;
  try { raw = win.sessionStorage.getItem(key); }
  catch { failure = 'RedirectStorageUnavailable'; }
  try { clearRedirectTransaction(win); }
  catch { failure = 'RedirectStorageUnavailable'; }
  try {
    if (failure) throw problem(failure);
    if (typeof raw !== 'string' || raw.length > 16384) throw problem('RedirectTransactionUnavailable');
    transaction = JSON.parse(raw);
    if (!transaction || transaction.version !== 1 || transaction.binding !== binding(config) ||
        !Number.isSafeInteger(transaction.createdAt) || transaction.createdAt > now || now - transaction.createdAt > lifetimeMs ||
        ![transaction.state, transaction.nonce, transaction.verifier].every(random) ||
        transaction.returnFragment !== fragment(transaction.returnFragment) ||
        Object.keys(transaction).sort().join(',') !== 'binding,createdAt,nonce,returnFragment,state,verifier,version')
      throw problem('InvalidRedirectTransaction');
    const callback = new URL(callbackUri);
    if (callback.username || callback.password || callback.hash || callback.searchParams.getAll('state').length !== 1 ||
        callback.searchParams.get('state') !== transaction.state) throw problem('StateMismatch');
  } catch (error) { failure = error?.code ?? 'InvalidRedirectTransaction'; transaction = null; }
  try { win.history.replaceState(null, '', '/' + (transaction?.returnFragment ?? '')); }
  catch { failure = 'CallbackHistoryUnavailable'; transaction = null; }
  receipts.set(receipt, failure ? { failure } : { transaction, callbackUri });
  return receipt;
}

export function consumeRedirectCallback(receipt, config, now = Date.now()) {
  const captured = receipts.get(receipt);
  receipts.delete(receipt);
  if (!captured) throw problem('RedirectTransactionUnavailable');
  if (captured.failure) throw problem(captured.failure);
  if (captured.transaction.binding !== binding(config) || captured.transaction.createdAt > now ||
      now - captured.transaction.createdAt > lifetimeMs) throw problem('InvalidRedirectTransaction');
  return captured;
}
