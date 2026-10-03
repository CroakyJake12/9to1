// Generic nonvisual semantics for actual Avalonia automation peers. No application
// commands, labels, service data or availability policy are created here.
export function createBrowserAccessibility(browserWindow, browserDocument, owner) {
    const root = browserDocument.createElement('div');
    root.id = 'native-control-semantics';
    Object.assign(root.style, { position: 'absolute', width: '1px', height: '1px',
        padding: '0', margin: '-1px', overflow: 'hidden', clipPath: 'inset(50%)', whiteSpace: 'nowrap' });
    // Keep this outside the canvas host's raw keyboard handlers: one native
    // provider invocation must not also become a second raw keyboard click.
    browserDocument.body.append(root);
    const elements = new Map();
    const nativeHost = browserDocument.getElementById?.('nine-to-one-root');
    let disposed = false;
    let generation;
    let restoringFocus = false;
    let nativeFocusedId;
    let nativeHostTabIndex;
    let ownsHostTabIndex = false;

    function clear() {
        root.replaceChildren();
        elements.clear();
        generation = undefined;
        nativeFocusedId = undefined;
    }

    function refresh() {
        if (disposed) return;
        const focused = browserDocument.activeElement;
        const snapshot = JSON.parse(owner.ReadAccessibility());
        if (generation !== snapshot.generation) clear();
        generation = snapshot.generation;
        nativeFocusedId = snapshot.elements.find(peer => peer.focused)?.id;
        // Native pointer/keyboard focus remains available; sequential browser
        // focus uses the actual peer projection rather than the raw input host.
        if (nativeHost && nativeHost.tabIndex !== -1) {
            if (!ownsHostTabIndex) nativeHostTabIndex = nativeHost.getAttribute('tabindex');
            nativeHost.tabIndex = -1;
            ownsHostTabIndex = true;
        }
        root.dataset.unsupportedPeers = snapshot.unsupported.join(',');
        const current = new Set();
        for (const peer of snapshot.elements) {
            current.add(peer.id);
            let node = elements.get(peer.id);
            if (!node) {
                node = browserDocument.createElement(peer.role === 'button' ? 'button' : peer.role === 'textbox' ? 'input' : 'span');
                node.dataset.nativePeerId = peer.id;
                node.addEventListener('focus', () => {
                    if (restoringFocus || disposed) return;
                    restoringFocus = true;
                    try {
                        if (owner.PerformAccessibility(peer.id, 'focus', null)) node.focus({ preventScroll: true });
                    } finally { restoringFocus = false; }
                });
                if (peer.role === 'button') {
                    node.type = 'button';
                    node.addEventListener('click', () => { if (disposed) return; owner.PerformAccessibility(peer.id, 'invoke', null); refresh(); });
                } else if (peer.role === 'textbox') {
                    node.type = 'text';
                    node.addEventListener('input', () => { if (disposed) return; owner.PerformAccessibility(peer.id, 'value', node.value); refresh(); });
                }
                elements.set(peer.id, node);
                root.append(node);
            }
            node.dataset.automationId = peer.automationId ?? '';
            if (peer.role === 'text') node.textContent = peer.name;
            else {
                node.setAttribute('aria-label', peer.name);
                node.disabled = !peer.enabled;
                node.tabIndex = peer.enabled && peer.focusable ? 0 : -1;
                if (peer.role === 'button') node.textContent = peer.name;
                else {
                    node.readOnly = peer.readOnly;
                    if (node.value !== (peer.value ?? '')) node.value = peer.value ?? '';
                }
            }
            if (peer.help) node.setAttribute('aria-description', peer.help);
            else node.removeAttribute('aria-description');
        }
        for (const [id, node] of elements) if (!current.has(id)) { node.remove(); elements.delete(id); }
        // Stable peer identity must not freeze old reading/tab order after the
        // native tree is reordered. Leave already ordered nodes untouched.
        snapshot.elements.forEach((peer, index) => {
            const node = elements.get(peer.id);
            if (root.children[index] !== node) root.insertBefore(node, root.children[index] ?? null);
        });
        if (focused && elements.get(focused.dataset.nativePeerId) === focused
            && !focused.disabled && focused.tabIndex >= 0 && browserDocument.activeElement !== focused) {
            restoringFocus = true;
            try { focused.focus({ preventScroll: true }); }
            finally { restoringFocus = false; }
        }
    }

    function nativeTab(event) {
        if (disposed || event.key !== 'Tab' || event.ctrlKey || event.altKey || event.metaKey
            || !nativeHost?.contains(event.target)) return;
        refresh();
        // Capture before the native host's Tab handler can prevent the browser
        // traversal. Other keys still follow the owning native input adapter.
        event.stopImmediatePropagation();
        const nodes = [...root.children].filter(node => node.dataset.nativePeerId && !node.disabled && node.tabIndex >= 0);
        if (!nodes.length) return;
        const current = nodes.findIndex(node => node.dataset.nativePeerId === nativeFocusedId);
        const next = current < 0 ? (event.shiftKey ? nodes.length - 1 : 0) : current + (event.shiftKey ? -1 : 1);
        if (next < 0 || next >= nodes.length) {
            // Let normal browser traversal leave the app at either boundary.
            nodes[current].focus({ preventScroll: true });
            return;
        }
        event.preventDefault();
        nodes[next].focus({ preventScroll: true });
    }

    function releaseNativeHost() {
        if (!nativeHost) return;
        browserDocument.removeEventListener('keydown', nativeTab, true);
        if (ownsHostTabIndex && nativeHost.tabIndex === -1) {
            if (nativeHostTabIndex === null) nativeHost.removeAttribute('tabindex');
            else nativeHost.setAttribute('tabindex', nativeHostTabIndex);
        }
        ownsHostTabIndex = false;
    }

    let timer;
    try {
        refresh();
        if (nativeHost) browserDocument.addEventListener('keydown', nativeTab, true);
        timer = browserWindow.setInterval(refresh, 250);
    } catch (error) {
        disposed = true;
        releaseNativeHost();
        clear();
        root.remove();
        throw error;
    }
    return {
        clear,
        refresh,
        dispose() {
            if (disposed) return;
            disposed = true;
            releaseNativeHost();
            browserWindow.clearInterval(timer);
            clear();
            root.remove();
        }
    };
}
