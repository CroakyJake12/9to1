export function createBrowserPlatform(windowObject, documentObject) {
    let lastFragment = windowObject.location.hash;
    return {
        readFragment: () => windowObject.location.hash,
        reduceMotion: () => windowObject.matchMedia('(prefers-reduced-motion: reduce)').matches,
        writeFragment(fragment, replace) {
            if (windowObject.location.hash === fragment) return;
            const url = new URL(windowObject.location.href);
            url.hash = fragment;
            windowObject.history[replace ? 'replaceState' : 'pushState'](null, '', url);
            lastFragment = windowObject.location.hash;
        },
        showStatus(code, message) {
            const status = documentObject.getElementById('browser-status');
            status.dataset.code = code;
            status.textContent = message;
            status.hidden = message.length === 0;
            documentObject.title = '9to1';
        },
        subscribe(locationChanged, releasePage, invalidatePrivateContext, hasUnsavedChanges = () => false) {
            const navigate = () => {
                const fragment = windowObject.location.hash;
                if (fragment === lastFragment) return;
                lastFragment = fragment;
                locationChanged(fragment);
            };
            // A cached tab cannot reuse private adapters without the owning account
            // service revalidating identity. This host has no authentication authority.
            const close = event => {
                if (event.persisted) invalidatePrivateContext();
                else releasePage();
            };
            const restore = event => { if (event.persisted) invalidatePrivateContext(); };
            // The browser may terminate execution after pagehide. Warn from actual
            // owner state; persistence must complete through an explicit awaited close.
            const warnUnsaved = event => {
                let dirty;
                try { dirty = hasUnsavedChanges(); }
                catch { dirty = true; } // An unavailable owner cannot confirm that its draft is saved.
                if (!dirty) return;
                event.preventDefault();
                event.returnValue = '';
            };
            windowObject.addEventListener('popstate', navigate);
            windowObject.addEventListener('hashchange', navigate);
            windowObject.addEventListener('pagehide', close);
            windowObject.addEventListener('pageshow', restore);
            windowObject.addEventListener('beforeunload', warnUnsaved);
            return () => {
                windowObject.removeEventListener('popstate', navigate);
                windowObject.removeEventListener('hashchange', navigate);
                windowObject.removeEventListener('pagehide', close);
                windowObject.removeEventListener('pageshow', restore);
                windowObject.removeEventListener('beforeunload', warnUnsaved);
            };
        },
    };
}
