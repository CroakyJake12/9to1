import { validThemePreference } from './theme-preference-contract.js';

// This cache is available only after the maintained account API has verified the
// current live token's account. It never migrates or uploads device-local themes.
export function createAccountThemePreferences({ client, hasCredential, apiResource, storage = globalThis.localStorage, window = globalThis.window }) {
    let accountId = null, epoch = 0, disposed = false, nextListener = 1;
    const listeners = new Map(), pending = new Map();
    const cacheKey = id => `9to1.home.account-theme-cache.v1:${JSON.stringify([apiResource, id])}`;
    const current = id => !disposed && typeof id === 'string' && id === accountId && hasCredential();
    const notify = revoked => { for (const callback of listeners.values()) { try { callback(revoked); } catch { /* Observer cannot change account custody. */ } } };
    const deniedRead = () => ({ state: 3, diagnostic: 'Sign in to access account-wide theme preferences.' });
    const unavailableWrite = code => ({ succeeded: false, code, diagnostic: 'Account appearance was not confirmed saved. Refresh before retrying.' });
    const run = (id, work) => {
        if (disposed || typeof id !== 'string' || !id || pending.has(id)) return Promise.reject(new Error('ThemeRequestUnavailable'));
        const controller = new AbortController();
        const entry = { controller, completion: null };
        pending.set(id, entry);
        const original = Promise.resolve().then(() => work(controller.signal));
        entry.completion = original.then(() => { pending.delete(id); }, () => { pending.delete(id); });
        return original;
    };
    const readCache = scope => {
        if (!current(scope)) return deniedRead();
        try {
            const raw = storage?.getItem(cacheKey(scope));
            if (raw == null) return { state: 1, diagnostic: 'No offline theme cache exists for this account.' };
            if (raw.length > 2_200_000) return { state: 4 };
            const envelope = JSON.parse(raw);
            return envelope.version === 1 && envelope.apiResource === apiResource && validThemePreference(envelope.preference, scope)
                ? { state: 0, preference: envelope.preference } : { state: 4 };
        } catch { return { state: 4, diagnostic: 'This account’s offline theme cache could not be read.' }; }
    };
    const store = (scope, preference) => {
        if (!current(scope) || !validThemePreference(preference, scope)) throw new Error('ThemeScopeMismatch');
        const cached = readCache(scope);
        if (cached.preference?.revision > preference.revision) return; // Never regress a newer same-account cache.
        const serialized = JSON.stringify({ version: 1, apiResource, preference });
        if (serialized.length > 2_200_000) throw new Error('ThemeCacheTooLarge');
        storage.setItem(cacheKey(scope), serialized);
        if (storage.getItem(cacheKey(scope)) !== serialized) throw new Error('ThemeCacheWriteFailed');
    };
    const onStorage = event => { if (accountId && event.key === cacheKey(accountId) && current(accountId)) notify(false); };
    window?.addEventListener?.('storage', onStorage);
    return {
        async themeContext(id) {
            return run(id, async signal => {
                if (disposed || !client || !hasCredential()) return '';
                if (current(accountId)) return accountId;
                const generation = epoch;
                const reply = await client.getCurrent({ signal });
                if (!reply.ok || signal.aborted || generation !== epoch || disposed || !hasCredential()) return '';
                accountId = reply.body.accountId;
                return accountId;
            });
        },
        canAccess: current,
        async readTheme(id, scope) {
            return run(id, async signal => {
                if (!current(scope)) return JSON.stringify(deniedRead());
                const generation = epoch;
                const reply = await client.getTheme(scope, { signal });
                if (generation !== epoch || !current(scope) || signal.aborted) return JSON.stringify(deniedRead());
                if (reply.ok) return JSON.stringify({ state: 0, preference: reply.body.preference });
                if (reply.status === 401 || reply.status === 403) return JSON.stringify(deniedRead());
                if (reply.error?.code === 'TransportUnavailable' || reply.status >= 500)
                    return JSON.stringify({ state: 1, diagnostic: 'Account preferences are temporarily offline.' });
                return JSON.stringify({ state: reply.status === 404 ? 2 : 4, diagnostic: 'Account theme synchronisation is unavailable or incompatible. Existing preferences are preserved.' });
            });
        },
        readCache: scope => JSON.stringify(readCache(scope)),
        async compareTheme(id, scope, expectedRevision, json) {
            return run(id, async signal => {
                if (!current(scope)) return JSON.stringify(unavailableWrite('PermissionDenied'));
                let candidate;
                try { candidate = JSON.parse(json); } catch { return JSON.stringify(unavailableWrite('InvalidArgument')); }
                if (!validThemePreference(candidate, scope)) return JSON.stringify(unavailableWrite('InvalidArgument'));
                const generation = epoch;
                const reply = await client.updateTheme(scope, expectedRevision, candidate, { signal });
                // The API itself rejects responses from a changed credential generation.
                if (generation !== epoch || !current(scope) || signal.aborted) return JSON.stringify(unavailableWrite('ThemeOutcomeUnknown'));
                if (!reply.ok) return JSON.stringify(unavailableWrite(reply.status === 409 ? 'RevisionConflict' :
                    reply.status === 401 || reply.status === 403 ? 'PermissionDenied' : 'ThemeOutcomeUnknown'));
                return JSON.stringify({ succeeded: true, code: 'Succeeded', preference: reply.body.preference });
            });
        },
        writeCache(scope, json) { store(scope, JSON.parse(json)); },
        subscribe(callback) { const id = nextListener++; listeners.set(id, callback); return id; },
        unsubscribe(id) { listeners.delete(id); },
        cancel(id) { pending.get(id)?.controller.abort(); },
        invalidate() { epoch++; accountId = null; notify(true); }, // Synchronous observation fence, before cancellation.
        signedIn() { if (!disposed) notify(false); },
        revoke() { disposed = true; epoch++; accountId = null; notify(true); },
        async disposeAsync() {
            this.revoke();
            window?.removeEventListener?.('storage', onStorage);
            for (const entry of pending.values()) entry.controller.abort();
            while (pending.size) await Promise.all([...pending.values()].map(entry => entry.completion));
            listeners.clear();
        }
    };
}
