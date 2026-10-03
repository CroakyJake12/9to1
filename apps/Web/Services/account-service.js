// CUI/.NET transport bridge. The composition root supplies the existing reviewed AccountApiClient.
// No issuer, client ID, API origin, token, identity or product-authority defaults are created here.
export function createAccountModule(client = null) {
  const pending = new Map();
  let disposed = false;
  const failure = code => JSON.stringify({ ok: false, status: null, error: { code, action: 'AccountSurface', body: null } });
  return {
    async invoke(requestId, action, argumentsJson) {
      if (!client || disposed) return failure('ServiceUnavailable');
      if (typeof requestId !== 'string' || !requestId || pending.has(requestId)) return failure('InvalidArgument');
      let args;
      try { args = JSON.parse(argumentsJson); } catch { return failure('InvalidArgument'); }
      const controller = new AbortController();
      const options = { signal: controller.signal };
      const operations = {
        GetCurrent: () => client.getCurrent(options),
        GetProfile: () => client.getProfile(options),
        ListSessions: () => client.listSessions(options),
        UpdateProfile: () => client.updateProfile(args?.expectedRevision, args?.fields, options),
        SignOut: () => client.signOut(options),
        RevokeSession: () => client.revokeSession(args?.sessionId, options),
        RevokeAllOtherSessions: () => client.revokeAllOtherSessions(options),
      };
      if (!Object.hasOwn(operations, action)) return failure('InvalidArgument');
      pending.set(requestId, controller);
      try {
        const reply = await operations[action]();
        return controller.signal.aborted ? failure('Cancelled') : JSON.stringify(reply);
      } catch {
        return failure(controller.signal.aborted ? 'Cancelled' : 'TransportUnavailable');
      } finally { pending.delete(requestId); }
    },
    cancel(requestId) { pending.get(requestId)?.abort(); },
    dispose() { disposed = true; for (const controller of pending.values()) controller.abort(); pending.clear(); },
  };
}
