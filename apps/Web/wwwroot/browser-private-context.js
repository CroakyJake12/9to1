// SOURCE PROPOSAL: root-owned two-phase lifecycle bookkeeping, not authority.
// The native owner performs the synchronous fence and owns its actual drain.
export function createPrivateContextLifecycle({ beginOwnerReset, clearPresentation, onFailure }) {
    if (typeof beginOwnerReset !== 'function' || typeof clearPresentation !== 'function'
        || (onFailure !== undefined && typeof onFailure !== 'function')) {
        throw new TypeError('Actual owner reset and presentation callbacks are required.');
    }
    const issued = new Set();
    const failures = new Set();
    let version = 0;

    function requireVoid(value, message) {
        if (value === undefined) return;
        // Invalid async callbacks cannot acknowledge the synchronous fence.
        // Still observe their rejection and preserve its original cause.
        if (typeof value?.then === 'function') Promise.resolve(value).catch(error => failures.add(error));
        throw new TypeError(message);
    }

    function notifyFailure() {
        if (!onFailure) return;
        try {
            requireVoid(onFailure(), 'Failure notification must be synchronous and void.');
        } catch (error) { failures.add(error); }
    }

    function begin(ownerOperation) {
        ++version;
        let resolve, reject;
        const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
        const entry = { promise };
        // Native cancellation can reenter JS before the exported call returns.
        // Publish the completion placeholder before that call, not afterward.
        issued.add(entry);
        promise.catch(() => {}); // Observe rejection; keep its original task and failure for every join.
        const fail = error => {
            failures.add(error);
            reject(error);
            notifyFailure();
        };
        let ownerTask, ownerFailure, ownerFailed = false;
        try {
            ownerTask = beginOwnerReset(ownerOperation);
            if (typeof ownerTask?.then !== 'function') throw new TypeError('The native owner drain must return its actual task.');
        } catch (error) {
            ownerFailed = true;
            ownerFailure = error;
        }
        let presentationFailure, presentationFailed = false;
        try {
            // Even a failed native invocation must attempt to remove the old
            // semantic presentation. That does not establish an owner fence.
            requireVoid(clearPresentation(), 'Presentation cleanup must be synchronous and void.');
        } catch (error) {
            presentationFailed = true;
            presentationFailure = error;
            failures.add(error);
            notifyFailure();
        }
        if (ownerFailed) {
            const error = !presentationFailed ? ownerFailure
                : new AggregateError([ownerFailure, presentationFailure], 'Private owner invocation and presentation cleanup failed.');
            fail(error);
            throw error; // Never acknowledge a failed or missing native fence.
        }
        Promise.resolve(ownerTask).then(() => {
            if (presentationFailed) { fail(presentationFailure); return; }
            resolve();
            issued.delete(entry); // Remove success only after its receipt settles.
        }, error => fail(!presentationFailed ? error
            : new AggregateError([error, presentationFailure], 'Private owner and presentation cleanup failed.')));
        if (presentationFailed) throw presentationFailure;
        // Acknowledge only the immediate fence. Request-originated invalidation
        // must not wait for the same request's native owner drain here.
    }

    async function join() {
        const observed = new Set();
        const errors = new Set(failures);
        while (true) {
            const entries = [...issued].filter(entry => !observed.has(entry));
            if (entries.length === 0) break;
            for (const entry of entries) observed.add(entry);
            const settled = await Promise.allSettled(entries.map(entry => entry.promise));
            for (const result of settled) if (result.status === 'rejected') errors.add(result.reason);
        }
        for (const error of failures) errors.add(error);
        if (errors.size !== 0) throw new AggregateError([...errors], 'Private cleanup failed. Reload before opening private content.');
    }

    function holdFailure(error) {
        failures.add(error);
        notifyFailure();
    }

    return {
        begin, join, holdFailure,
        get version() { return version; },
        get failed() { return failures.size !== 0; },
        get retainedDrains() { return issued.size; },
    };
}
