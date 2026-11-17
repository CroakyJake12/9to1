// Development-only in-page helpers for the local Files candidate journey.
// They only read and invoke the genuine rendered CUI accessibility peers that
// BrowserAccessibilityBridge already exposes; no Files command is simulated.
(() => {
    'use strict';
    const peerSelector = '#native-control-semantics button[data-native-peer-id]';
    const inputSelector = '#native-control-semantics input[data-native-peer-id]';
    const peers = () => [...document.querySelectorAll(peerSelector)];
    const inputs = () => [...document.querySelectorAll(inputSelector)];
    const nameOf = peer => peer.getAttribute('aria-label');
    window.__nine = {
        status: () => ({
            ready: document.querySelector('#browser-status')?.dataset.code ?? null,
            hash: location.hash,
            controls: [...peers(), ...inputs()].map(nameOf).filter(Boolean),
        }),
        has: label => peers().some(peer => nameOf(peer) === label),
        enabled: label => peers().some(peer => nameOf(peer) === label && !peer.disabled),
        click: label => {
            const peer = peers().find(peer => nameOf(peer) === label && !peer.disabled);
            if (!peer) return false;
            peer.click();
            return true;
        },
        set: (accessibleName, value) => {
            const input = inputs().find(peer => nameOf(peer) === accessibleName && !peer.disabled && !peer.readOnly);
            if (!input) return false;
            const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
            setter.call(input, value);
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
            return true;
        },
        text: () => document.body.innerText.slice(0, 8000),
    };
})();
