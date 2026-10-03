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
    let disposed = false;
    let generation;
    let restoringFocus = false;

    function clear() {
        root.replaceChildren();
        elements.clear();
        generation = undefined;
    }

    function refresh() {
        if (disposed) return;
        const focused = browserDocument.activeElement;
        const snapshot = JSON.parse(owner.ReadAccessibility());
        if (generation !== snapshot.generation) clear();
        generation = snapshot.generation;
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

    let timer;
    try {
        refresh();
        timer = browserWindow.setInterval(refresh, 250);
    } catch (error) {
        disposed = true;
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
            browserWindow.clearInterval(timer);
            clear();
            root.remove();
        }
    };
}
