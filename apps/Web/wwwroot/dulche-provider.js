// Binary HTTP bridge for the existing OllamaClient. Authority remains the real account
// owner and canonical governed turn; this module creates no identity or model record.
const problem = code => Object.assign(new Error(code), { code });
export function createDulcheProvider(accountOwner) {
    const entries = new Map();
    let disposed = false, drain;
    const current = entry => !disposed && !entry.revoked && !entry.controller.signal.aborted && accountOwner.isCurrent(entry.identity);
    const retired = JSON.stringify({ retired: true });
    const retiredOperation = Symbol('original request retired');
    const operation = async (entry, action) => {
        try { return await action(); }
        catch (error) {
            if (entry.controller.signal.aborted && (accountOwner.isRetirement?.(error) === true ||
                error === entry.abortReason)) return retiredOperation;
            throw error;
        }
    };
    function issue(entry, action) {
        let settle;
        const completion = new Promise(resolve => { settle = resolve; });
        entry.issued.add(completion);
        let actual;
        try { actual = Promise.resolve(action()); } catch (error) { actual = Promise.reject(error); }
        actual.then(() => { entry.issued.delete(completion); settle(); }, error => {
            // Cleanup failures are always retained, including errors whose text
            // or code resembles cancellation. Only original request operations
            // can produce the explicitly owned retired terminal above.
            entry.errors.push(error);
            entry.issued.delete(completion); settle();
        });
        return actual;
    }
    function invalidate(entry) {
        if (entry.revoked) return;
        entry.revoked = true;
        entry.buffer = null;
        // Own the original body cancellation before Fetch aborts its stream.
        // Chromium may otherwise replace the signal reason with a new platform
        // AbortError when cancel is attempted on an already errored body.
        if (entry.reader) issue(entry, () => entry.reader.cancel()).catch(() => {});
        try { entry.controller.abort(entry.abortReason); } catch (error) { entry.errors.push(error); }
    }
    async function join(entry) {
        while (entry.issued.size) await Promise.all([...entry.issued]);
        if (entry.errors.length) throw new AggregateError([...entry.errors], 'Provider stream did not close cleanly.');
    }
    const participant = {
        invalidate() { for (const entry of entries.values()) invalidate(entry); },
        disposeAsync() {
            if (drain) return drain;
            disposed = true;
            participant.invalidate();
            const originals = [...entries.values()];
            drain = Promise.allSettled(originals.map(join)).then(results => {
                entries.clear(); detach();
                const failures = results.filter(result => result.status === 'rejected').map(result => result.reason);
                if (failures.length) throw new AggregateError(failures, 'Private provider streams did not drain.');
            });
            drain.catch(() => {});
            return drain;
        },
    };
    const detach = accountOwner.attach(participant);
    const demand = id => { const entry = entries.get(id); if (!entry) throw problem('ProviderStreamUnavailable'); return entry; };
    return Object.freeze({
        begin(id, method, relativePath, bodyJson, canDispatch) {
            if (disposed) return Promise.reject(problem('ProviderStreamUnavailable'));
            if (typeof canDispatch !== 'function' || typeof id !== 'string' || !/^[a-zA-Z0-9_-]{1,128}$/.test(id) || entries.has(id) || entries.size >= 128 ||
                !((method === 'GET' && relativePath === 'api/tags' && bodyJson === null) ||
                  (method === 'POST' && relativePath === 'api/chat' && typeof bodyJson === 'string' && bodyJson.length <= 1_048_576)))
                return Promise.reject(problem('InvalidInferenceRequest'));
            const entry = { controller: new AbortController(), abortReason: new DOMException('Original provider request retired.', 'AbortError'), issued: new Set(), errors: [], identity: null,
                reader: null, buffer: null, reading: false, revoked: false, finished: false };
            entries.set(id, entry); // Custody is recorded before account/network callbacks.
            return issue(entry, async () => {
                const identity = await operation(entry, () => accountOwner.readIdentity(entry.controller.signal));
                if (identity === retiredOperation) return retired;
                entry.identity = identity;
                if (!current(entry)) return retired;
                // Recheck the original native exchange at the final dispatch boundary.
                const admission = canDispatch();
                if (admission !== true) {
                    if (admission !== false) throw problem('InvalidInferenceAdmission');
                    return retired;
                }
                const adopt = response => {
                    if (entry.reader) throw problem('ProviderResponseAlreadyOwned');
                    entry.reader = response.body?.getReader() ?? null;
                    entry.finished = !entry.reader;
                };
                const response = await operation(entry, () => accountOwner.fetch(entry.identity, method, relativePath, bodyJson, entry.controller.signal, adopt));
                if (response === retiredOperation) return retired;
                if (!current(entry)) { if (!entry.reader) await response.body?.cancel(); return retired; }
                if (!entry.reader) adopt(response);
                return JSON.stringify({ status: response.status, contentType: response.headers.get('content-type') ?? '' });
            });
        },
        read(id) {
            const entry = demand(id);
            if (entry.reading) return Promise.reject(problem('ProviderReadInProgress'));
            if (!current(entry)) return Promise.resolve(retired);
            entry.reading = true;
            return issue(entry, async () => {
                try {
                    if (!entry.buffer && !entry.finished) {
                        const result = await operation(entry, () => entry.reader.read());
                        if (result === retiredOperation) return retired;
                        if (!current(entry)) return retired;
                        if (result.done) entry.finished = true;
                        else {
                            if (!(result.value instanceof Uint8Array) || result.value.byteLength > 1_048_576) throw problem('ProviderChunkTooLarge');
                            entry.buffer = result.value;
                        }
                    }
                    if (!current(entry)) return retired;
                    if (!entry.buffer) return JSON.stringify({ done: entry.finished, base64: '' });
                    const chunk = entry.buffer.subarray(0, 16384);
                    entry.buffer = chunk.byteLength === entry.buffer.byteLength ? null : entry.buffer.subarray(chunk.byteLength);
                    return JSON.stringify({ done: false, base64: btoa(String.fromCharCode(...chunk)) });
                } finally { entry.reading = false; }
            });
        },
        async cancel(id) { const entry = demand(id); invalidate(entry); await join(entry); },
        async dispose(id) {
            const entry = entries.get(id);
            if (!entry) return;
            invalidate(entry);
            try { await join(entry); } finally { entries.delete(id); }
        },
        disposeAsync: participant.disposeAsync,
    });
}
