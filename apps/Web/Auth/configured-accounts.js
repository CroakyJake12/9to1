// Composition over the maintained clients; config is public host-owned data, not an authentication authority.
import { BrowserPublicClient, handleOAuthPopupCallback } from './browser-public-client.js';
import { AccountApiClient } from './account-api-client.js';
import { createAccountModule } from '../Services/account-service.js';
export { BrowserPublicClient, handleOAuthPopupCallback };
export function createConfiguredAccounts({ configuration = null, window = globalThis.window, fetch = globalThis.fetch,
  crypto = globalThis.crypto, onPrivateContextInvalidated, onVerifiedIdentity, onFailure, beginPrivateContextInvalidation = undefined }) {
  if (typeof onPrivateContextInvalidated !== 'function' || typeof onVerifiedIdentity !== 'function' || typeof onFailure !== 'function') throw new TypeError('Private context callbacks are required.');
  if (beginPrivateContextInvalidation !== undefined && typeof beginPrivateContextInvalidation !== 'function') throw new TypeError('A synchronous root private fence is required when supplied.');
  let broker, api, disposed = false, prepared = false, cleanupTask, module;
  const signIns = new Map(), taskReads = new Map(), taskIdentityReceipts = new Map(), issued = new Set(), cleanupErrors = [];
  const issue = (action, factory) => {
    if(disposed)return Promise.reject(new Error('ServiceUnavailable'));
    let settle;const completion=new Promise(resolve=>{settle=resolve;});const entry={action,completion};issued.add(entry);
    let actual;try{actual=Promise.resolve(factory());}catch(error){actual=Promise.reject(error);}
    actual.then(()=>{issued.delete(entry);settle();},error=>{
      if(action==='prepare'||action==='invalidate')cleanupErrors.push(error);
      issued.delete(entry);settle();
    });return actual;
  };
  const joinIssued = async()=>{while(issued.size)await Promise.all([...issued].map(e=>e.completion));if(cleanupErrors.length)throw new AggregateError([...cleanupErrors],'Configured lifecycle cleanup failed.');};
  const base = configuration === null ? createAccountModule() : (() => {
    const begin = reason => {
      taskIdentityReceipts.clear(); // Synchronous private reset removes observed account/profile metadata.
      // Real parent-memory credentials/generation cleared without abort callbacks.
      broker?.clearToken({ cancelPending: false });
      try {
        const acknowledgement = beginPrivateContextInvalidation(reason);
        if (acknowledgement !== undefined) {
          if (acknowledgement && typeof acknowledgement.then === 'function') Promise.resolve(acknowledgement).catch(() => {});
          throw new TypeError('Root private fencing must synchronously return void.');
        }
      } finally {
        // This is the EXISTING broker's internal generation/cancellation, never
        // server auth_revision. Its original sign-in survives its own start reset.
        if (reason !== 'sign_in_started') broker?.clearToken();
      }
    };
    const clear = async reason => {
      taskIdentityReceipts.clear(); // The legacy awaited reset also retires all one-use identity observations.
      if (beginPrivateContextInvalidation === undefined)
        broker?.clearToken({ cancelPending: reason !== 'sign_in_started' });
      // Explicit begin is invoked by API BEFORE its cancellations; clear is JOIN only.
      // With explicit begin installed, the root callback JOINs its complete
      // native reset/sticky-error ledger; defaults retain original awaited clear.
      await onPrivateContextInvalidated(reason);
    };
    broker = new BrowserPublicClient({ configuration, window, fetch, crypto,
      onBeforeSignIn: () => api.invalidatePrivateContext('sign_in_started'),
      verifyCurrentAccount: async (subject, signal) => {
        const current = await api.getCurrent({ signal });
        if (!current.ok || current.body.accountId !== subject || signal.aborted) throw new Error('CurrentAccountVerificationFailed');
      },
      readCurrentTaskProfile: async (subject, signal) => {
        const current = await api.getCurrent({ signal });
        if (!current.ok) throw Object.assign(new Error('TaskIdentityUnavailable'), { code: 'TaskIdentityUnavailable' });
        if (current.body.accountId !== subject || signal?.aborted) throw Object.assign(new Error('SessionContextChanged'), { code: 'SessionContextChanged' });
        const response = await api.getProfile({ signal });
        if (!response.ok) throw Object.assign(new Error('TaskIdentityUnavailable'), { code: 'TaskIdentityUnavailable' });
        if (response.body.profile.accountId !== subject || signal?.aborted) throw Object.assign(new Error('TaskProfileVerificationFailed'), { code: 'TaskProfileVerificationFailed' });
        // The maintained API profile's real primary key is accountId; no invented profile UUID.
        return { accountId: response.body.profile.accountId, revision: response.body.profile.revision };
      },
      onVerifiedIdentity, onSignInFailed: () => api.invalidatePrivateContext('sign_in_failed'), onTokenExpired: () => beginPrivateContextInvalidation === undefined ? api.invalidatePrivateContext('token_expired') : onPrivateContextInvalidated('token_expired'),
      beginTokenExpiryInvalidation: beginPrivateContextInvalidation === undefined ? undefined : () => api.beginPrivateContextInvalidation('token_expired'), onFailure });
    api = new AccountApiClient({ apiResource: broker.configuration.apiResource, getAccessToken: () => broker.getAccessToken(),
      onPrivateContextInvalidated: clear, beginPrivateContextInvalidation: beginPrivateContextInvalidation === undefined ? undefined : begin, fetch, allowLoopbackForIsolatedTests: broker.configuration.allowLoopbackForIsolatedTests });
    return createAccountModule(api);
  })();
  return module = {
    invalidatePrivateContext(reason) { return issue('invalidate',async()=>{
      // BFCache/private lifetime invalidation must clear the actual in-memory credential supplier,
      // then await the same owner cleanup boundary; configured sign-in remains available afterward.
      if (broker) await api.invalidatePrivateContext(reason);
      else {
        if (beginPrivateContextInvalidation !== undefined) {
          const acknowledgement = beginPrivateContextInvalidation(reason);
          if (acknowledgement !== undefined) {
            if (acknowledgement && typeof acknowledgement.then === 'function') Promise.resolve(acknowledgement).catch(() => {});
            throw new TypeError('Private fencing must synchronously return void.');
          }
        }
        await onPrivateContextInvalidated(reason);
      }
    }); },
    prepare() { return issue('prepare',async()=>{
      if (disposed) throw new Error('ServiceUnavailable');
      if (broker && !prepared) { await api.invalidatePrivateContext('configuration_prepared'); prepared = true; }
    }); },
    invoke(id, action, args) { if(disposed)return Promise.resolve(JSON.stringify({ok:false,status:null,error:{code:'ServiceUnavailable',action,body:null}}));return issue('invoke',async()=>{
      if (broker && !disposed && !broker.getAccessToken()) {
        if (!prepared) return JSON.stringify({ ok: false, status: null, error: { code: 'PrivateContextNotPrepared', action, body: null } });
        return JSON.stringify({ ok: false, status: null, error: { code: 'AuthenticationRequired', action, body: null } });
      }
      return base.invoke(id, action, args);
    }); },
    readTaskIdentity(id) { return issue('task-identity', async () => {
      if (!broker || !prepared || disposed) return JSON.stringify({ ok: false, code: 'AuthenticationRequired' });
      if (typeof id !== 'string' || !id || taskReads.has(id) || taskIdentityReceipts.has(id)) throw new TypeError('Invalid Task identity request.');
      if (taskReads.size + taskIdentityReceipts.size >= 128) throw new Error('Task identity request custody is full.');
      const controller = new AbortController(); taskReads.set(id, controller);
      try {
        const identity = await broker.readCurrentTaskIdentity({ signal: controller.signal });
        if (disposed || controller.signal.aborted) return JSON.stringify({ ok: false, code: 'Cancelled' });
        if (identity !== null && !broker.isOriginalTaskIdentityCurrent(identity)) throw Object.assign(new Error('SessionContextChanged'), { code: 'SessionContextChanged' });
        if (identity !== null) taskIdentityReceipts.set(id, identity);
        return identity === null ? JSON.stringify({ ok: false, code: 'AuthenticationRequired' }) : JSON.stringify({ ok: true, identity });
      } finally { taskReads.delete(id); }
    }); },
    confirmTaskIdentity(id) {
      const original = taskIdentityReceipts.get(id);
      taskIdentityReceipts.delete(id); // Single observation consumption, never a reusable permission.
      return !disposed && broker !== undefined && original !== undefined && broker.isOriginalTaskIdentityCurrent(original);
    },
    releaseTaskIdentity(id) { taskIdentityReceipts.delete(id); },
    cancelTaskIdentity(id) { taskIdentityReceipts.delete(id); taskReads.get(id)?.abort(); },
    signInAvailable() { return Boolean(broker && prepared && !disposed); },
    requestSignIn(id) { if(disposed)return Promise.resolve(JSON.stringify({ok:false,error:{code:'ServiceUnavailable'}}));return issue('sign-in',async()=>{
      if (!broker || !prepared || disposed) return JSON.stringify({ ok: false, error: { code: 'ServiceUnavailable' } });
      if (typeof id !== 'string' || !id || signIns.has(id)) return JSON.stringify({ ok: false, error: { code: 'InvalidArgument' } });
      const controller = new AbortController(); signIns.set(id, controller);
      try { await broker.signIn({ signal: controller.signal }); return JSON.stringify({ ok: true }); }
      catch (error) { return JSON.stringify({ ok: false, error: { code: typeof error?.code === 'string' ? error.code : 'ProviderUnavailable' } }); }
      finally { signIns.delete(id); }
    }); },
    cancel(id) { base.cancel(id); signIns.get(id)?.abort(); },
    revokePrivateContext() { disposed=true; taskIdentityReceipts.clear(); broker?.revokePrivateContext(); base.revokePrivateContext(); },
    disposeAsync() {
      if(cleanupTask) return cleanupTask;
      // Externally-owned terminal release only. Root09 MUST nativeRevoke BEFORE this call.
      // onPrivateContextInvalidated cannot call this full join from an owned command.
      disposed=true; taskIdentityReceipts.clear(); broker?.revokePrivateContext(); base.revokePrivateContext();
      let resolve,reject;cleanupTask=new Promise((a,b)=>{resolve=a;reject=b;});
      const errors=[];for(const controller of [...signIns.values(), ...taskReads.values()])try{controller.abort();}catch(error){errors.push(error);}
      Promise.allSettled([base.disposeAsync(),broker?.disposeAsync() ?? Promise.resolve(),joinIssued()]).then(results=>{
        errors.push(...results.filter(r=>r.status==='rejected').map(r=>r.reason));
        if(errors.length)reject(new AggregateError(errors,'Configured account cleanup failed.'));else resolve();
      },reject);
      return cleanupTask;
    },
    dispose() { module.disposeAsync().catch(()=>{}); },
  };
}
