const CHANNEL = "9to1-account-session-v1";
const SIGNED_OUT = "signed-out";
const SESSIONS_CHANGED = "sessions-changed";
const CHECK_INTERVAL_MS = 30_000;
const CHECK_TIMEOUT_MS = 10_000;
const EVENT_DEBOUNCE_MS = 300;

// The website notice can only revoke an existing local view. The cookie-backed
// Files endpoint is the authority for the owner session on every request.
export function createFilesSessionMonitor(browserWindow, browserDocument, onSessionLoss) {
    let pending;
    let channel;
    let lastStarted = -Infinity;
    let recheckAfterAbort = false;
    let recheckWhenVisible = false;
    let stopped = false;
    let invalidated = false;
    const visible = () => browserDocument.visibilityState === "visible";
    const now = () => browserWindow.performance.now();
    const onFocus = () => check();

    function onNotice(event) {
        if (event.data === SIGNED_OUT) invalidate();
        else if (event.data === SESSIONS_CHANGED) check(true);
    }

    function cancelCheck() {
        recheckAfterAbort = false;
        recheckWhenVisible = false;
        lastStarted = -Infinity;
        pending?.controller.abort();
    }

    function stop() {
        if (stopped) return;
        stopped = true;
        browserWindow.clearInterval(timer);
        browserWindow.removeEventListener("focus", onFocus);
        browserDocument.removeEventListener("visibilitychange", onVisibility);
        channel?.removeEventListener("message", onNotice);
        channel?.close();
        cancelCheck();
    }

    function invalidate() {
        if (stopped) return;
        invalidated = true;
        stop();
        onSessionLoss();
    }

    function onVisibility() {
        if (visible()) check(recheckWhenVisible);
        else {
            if (recheckAfterAbort) {
                recheckAfterAbort = false;
                recheckWhenVisible = true;
            }
            lastStarted = -Infinity;
            pending?.controller.abort();
        }
    }

    function check(force = false) {
        if (stopped) return;
        if (!visible()) {
            if (force) recheckWhenVisible = true;
            return;
        }
        if (pending) {
            if (force || pending.controller.signal.aborted) {
                recheckAfterAbort = true;
                if (force && !pending.controller.signal.aborted) pending.controller.abort();
            }
            return;
        }
        const started = now();
        if (!force && !recheckAfterAbort && !recheckWhenVisible && started - lastStarted < EVENT_DEBOUNCE_MS) return;
        recheckAfterAbort = false;
        recheckWhenVisible = false;
        lastStarted = started;
        const controller = new AbortController();
        const timeout = browserWindow.setTimeout(() => controller.abort(), CHECK_TIMEOUT_MS);
        const work = Promise.resolve().then(async () => {
            if (controller.signal.aborted) return;
            const response = await browserWindow.fetch("/api/files/v1/context", {
                credentials: "same-origin", mode: "same-origin", cache: "no-store", redirect: "error",
                headers: { Accept: "application/json" }, signal: controller.signal,
            });
            try {
                if (!controller.signal.aborted && (response.status === 401 || response.status === 403)) invalidate();
            } finally {
                await response.body?.cancel().catch(() => {});
            }
        }).catch(() => {
            // A failed network check is not an account denial; the next visible check can retry.
        }).finally(() => {
            browserWindow.clearTimeout(timeout);
            pending = undefined;
            if (recheckAfterAbort) {
                recheckAfterAbort = false;
                if (visible()) check(true);
                else recheckWhenVisible = true;
            }
        });
        pending = { controller, work };
    }

    browserWindow.addEventListener("focus", onFocus);
    browserDocument.addEventListener("visibilitychange", onVisibility);
    try {
        channel = new browserWindow.BroadcastChannel(CHANNEL);
        channel.addEventListener("message", onNotice);
    } catch {
        channel = undefined; // Visibility and periodic checks still verify the server.
    }
    const timer = browserWindow.setInterval(check, CHECK_INTERVAL_MS);
    check();
    return {
        get invalidated() { return invalidated; },
        invalidate,
        cancelCheck,
        async dispose() {
            stop();
            await pending?.work;
        },
    };
}
