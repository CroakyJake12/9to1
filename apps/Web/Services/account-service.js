// CUI/.NET transport bridge over the maintained AccountApiClient.
// External root MUST revoke native private context before terminal cancellation.
export function createAccountModule(client = null) {
  const pending = new Map(); const issued = new Set();
  let disposed = false, cleanupTask;
  const failure = code => JSON.stringify({ ok: false, status: null, error: { code, action: 'AccountSurface', body: null } });
  const revokePrivateContext = () => { disposed = true; client = null; }; // No observer/abort callbacks.
  const drain = async () => { while (issued.size) await Promise.all([...issued].map(entry=>entry.completion)); };
  const disposeAsync = () => {
    if(cleanupTask) return cleanupTask;
    revokePrivateContext();
    let resolve,reject;cleanupTask=new Promise((a,b)=>{resolve=a;reject=b;}); // Before abort reentrancy.
    const errors=[];
    for(const entry of pending.values())try{entry.controller.abort();}catch(error){errors.push(error);}
    drain().then(()=>{if(errors.length)reject(new AggregateError(errors,'Account module cleanup failed.'));else resolve();},reject);
    return cleanupTask;
  };
  return {
    invoke(requestId, action, argumentsJson) {
      if (!client || disposed) return Promise.resolve(failure('ServiceUnavailable'));
      if (typeof requestId !== 'string' || !requestId || pending.has(requestId)) return Promise.resolve(failure('InvalidArgument'));
      let args;try { args = JSON.parse(argumentsJson); } catch { return Promise.resolve(failure('InvalidArgument')); }
      const controller = new AbortController(); const options = { signal: controller.signal };
      const operations = { GetCurrent: () => client.getCurrent(options), GetProfile: () => client.getProfile(options),
        ListSessions: () => client.listSessions(options), UpdateProfile: () => client.updateProfile(args?.expectedRevision,args?.fields,options),
        SignOut: () => client.signOut(options), RevokeSession: () => client.revokeSession(args?.sessionId,options),
        RevokeAllOtherSessions: () => client.revokeAllOtherSessions(options) };
      if (!Object.hasOwn(operations, action)) return Promise.resolve(failure('InvalidArgument'));
      let settle;const completion=new Promise(resolve=>{settle=resolve;});const entry={controller,completion};
      pending.set(requestId,entry);issued.add(entry); // BEFORE the actual client callback/fetch.
      const actual=(async()=>{try{const reply=await operations[action]();return controller.signal.aborted?failure('Cancelled'):JSON.stringify(reply);}
        catch{return failure(controller.signal.aborted?'Cancelled':'TransportUnavailable');}})();
      // Observe the ACTUAL invoke promise settlement, not just its signal or controller lifetime.
      actual.then(()=>{pending.delete(requestId);issued.delete(entry);settle();},()=>{pending.delete(requestId);issued.delete(entry);settle();});
      return actual;
    },
    cancel(requestId) { pending.get(requestId)?.controller.abort(); },
    revokePrivateContext,
    joinIssuedWork: drain,
    hasOutstandingWork() {return issued.size!==0;},
    dispose() {disposeAsync().catch(()=>{});}, // Legacy immediate return is NOT a drained receipt.
    disposeAsync,
  };
}
