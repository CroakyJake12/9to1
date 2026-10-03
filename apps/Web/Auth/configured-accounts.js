// Composition over the maintained clients; config is public host-owned data, not an authentication authority.
import { BrowserPublicClient, handleOAuthPopupCallback } from './browser-public-client.js';
import { AccountApiClient } from './account-api-client.js';
import { createAccountModule } from '../Services/account-service.js';
export { BrowserPublicClient, handleOAuthPopupCallback };
export function createConfiguredAccounts({ configuration = null, window = globalThis.window, fetch = globalThis.fetch,
  crypto = globalThis.crypto, onPrivateContextInvalidated, onVerifiedIdentity, onFailure }) {
  if (typeof onPrivateContextInvalidated !== 'function' || typeof onVerifiedIdentity !== 'function' || typeof onFailure !== 'function') throw new TypeError('Private context callbacks are required.');
  let broker, api, disposed = false, prepared = false;
  const signIns = new Map();
  const base = configuration === null ? createAccountModule() : (() => {
    const clear = async reason => {
      broker?.clearToken({ cancelPending: reason !== 'sign_in_started' });
      await onPrivateContextInvalidated(reason);
    };
    broker = new BrowserPublicClient({ configuration, window, fetch, crypto,
      onBeforeSignIn: () => api.invalidatePrivateContext('sign_in_started'),
      verifyCurrentAccount: async (subject, signal) => {
        const current = await api.getCurrent({ signal });
        if (!current.ok || current.body.accountId !== subject || signal.aborted) throw new Error('CurrentAccountVerificationFailed');
      },
      onVerifiedIdentity, onSignInFailed: () => api.invalidatePrivateContext('sign_in_failed'), onTokenExpired: () => api.invalidatePrivateContext('token_expired'), onFailure });
    api = new AccountApiClient({ apiResource: broker.configuration.apiResource, getAccessToken: () => broker.getAccessToken(),
      onPrivateContextInvalidated: clear, fetch, allowLoopbackForIsolatedTests: broker.configuration.allowLoopbackForIsolatedTests });
    return createAccountModule(api);
  })();
  return {
    async invalidatePrivateContext(reason) {
      // BFCache/private lifetime invalidation must clear the actual in-memory credential supplier,
      // then await the same owner cleanup boundary; configured sign-in remains available afterward.
      if (broker) await api.invalidatePrivateContext(reason);
      else await onPrivateContextInvalidated(reason);
    },
    async prepare() {
      if (disposed) throw new Error('ServiceUnavailable');
      if (broker && !prepared) { await api.invalidatePrivateContext('configuration_prepared'); prepared = true; }
    },
    async invoke(id, action, args) {
      if (broker && !disposed && !broker.getAccessToken()) {
        if (!prepared) return JSON.stringify({ ok: false, status: null, error: { code: 'PrivateContextNotPrepared', action, body: null } });
        return JSON.stringify({ ok: false, status: null, error: { code: 'AuthenticationRequired', action, body: null } });
      }
      return base.invoke(id, action, args);
    },
    signInAvailable() { return Boolean(broker && prepared && !disposed); },
    async requestSignIn(id) {
      if (!broker || !prepared || disposed) return JSON.stringify({ ok: false, error: { code: 'ServiceUnavailable' } });
      if (typeof id !== 'string' || !id || signIns.has(id)) return JSON.stringify({ ok: false, error: { code: 'InvalidArgument' } });
      const controller = new AbortController(); signIns.set(id, controller);
      try { await broker.signIn({ signal: controller.signal }); return JSON.stringify({ ok: true }); }
      catch (error) { return JSON.stringify({ ok: false, error: { code: typeof error?.code === 'string' ? error.code : 'ProviderUnavailable' } }); }
      finally { signIns.delete(id); }
    },
    cancel(id) { base.cancel(id); signIns.get(id)?.abort(); },
    dispose() { disposed = true; for (const controller of signIns.values()) controller.abort(); base.dispose(); broker?.dispose(); },
  };
}
