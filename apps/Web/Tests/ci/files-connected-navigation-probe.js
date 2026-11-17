// Test-only browser choreography. Injected into a disposable localhost HTML
// response; not included in the published product or its sealed assets.
(() => {
    'use strict';
    const evidence = document.createElement('pre');
    evidence.id = 'files-navigation-smoke-evidence';
    evidence.hidden = true;
    document.body.append(evidence);
    const observations = [];
    const expected = [
        'select-folder', 'open-folder', 'inside-folder', 'up-to-root',
        'back-to-folder', 'forward-to-root', 'enter-search', 'search-results',
        'clear-search', 'restored-root',
    ];
    let step = 0;
    let finished = false;
    const began = performance.now();

    function peers(selector = '#native-control-semantics button[data-native-peer-id]') {
        return [...document.querySelectorAll(selector)];
    }
    function nameOf(peer) { return peer.getAttribute('aria-label'); }
    function enabled(label) {
        return peers().find(peer => nameOf(peer) === label && !peer.disabled) ?? null;
    }
    function has(label) { return peers().some(peer => nameOf(peer) === label); }
    function controls() {
        // The only admitted labels come from a synthetic fixture, not user files.
        const names = ['Project Atlas', 'draft-notes.txt', 'inside.txt', 'Files root',
            'Open selected folder', 'Back', 'Forward', 'Up', 'Search folder', 'Clear search'];
        return peers().map(nameOf).filter(name => names.includes(name));
    }
    function persist(status = 'RUNNING', failure = null) {
        evidence.textContent = JSON.stringify({
            status, phase: expected[step] ?? 'complete', completed: observations.map(x => x.step),
            observations, failure,
            elapsedMs: Math.round(performance.now() - began),
            actualNativePeers: true,
            source: 'read-only disposable CUI accessibility peer invocation',
            realGoogleDriveAcceptance: 'NOT RUN',
        });
    }
    function advance(name, action) {
        observations.push({ step: name, controls: controls(), action });
        step++;
        persist();
    }
    function act(label, phase) {
        const peer = enabled(label);
        if (!peer) return false;
        // BrowserAccessibilityBridge handles click through the genuine native
        // PerformAccessibility invoke path; no Files command is simulated here.
        peer.click();
        advance(phase, 'invoke:' + label);
        return true;
    }
    function tick() {
        if (finished) return;
        try {
            // Ensure the compiled application actually owns the current CUI.
            const ready = document.querySelector('#browser-status')?.dataset.code === 'Ready';
            if (!ready || location.hash !== '#/app.files') return;
            switch (expected[step]) {
                case 'select-folder':
                    if (has('Project Atlas') && has('draft-notes.txt')) act('Project Atlas', 'select-folder');
                    break;
                case 'open-folder':
                    act('Open selected folder', 'open-folder');
                    break;
                case 'inside-folder':
                    if (has('inside.txt') && !has('draft-notes.txt') && !has('Project Atlas'))
                        advance('inside-folder', 'observed:nested-only');
                    break;
                case 'up-to-root':
                    if (act('Up', 'up-to-root')) break;
                    break;
                case 'back-to-folder':
                    // An Up result must visibly restore both original root rows.
                    if (has('Project Atlas') && has('draft-notes.txt')) {
                        if (act('Back', 'back-to-folder')) break;
                    }
                    break;
                case 'forward-to-root':
                    // Back must have restored the nested file, not merely enabled a button.
                    if (has('inside.txt') && !has('draft-notes.txt'))
                        act('Forward', 'forward-to-root');
                    break;
                case 'enter-search': {
                    if (!has('Project Atlas') || !has('draft-notes.txt')) break;
                    const input = peers('#native-control-semantics input[data-native-peer-id]')
                        .find(peer => nameOf(peer) === 'Search names in this folder' && !peer.disabled && !peer.readOnly);
                    if (!input) break;
                    input.value = 'Atlas';
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    if (act('Search folder', 'enter-search')) break;
                    break;
                }
                case 'search-results':
                    // Search must hide the sibling: checking button names avoids
                    // mistaking breadcrumb/status text for an actual item row.
                    if (has('Project Atlas') && !has('draft-notes.txt'))
                        advance('search-results', 'observed:folder-filtered');
                    break;
                case 'clear-search':
                    act('Clear search', 'clear-search');
                    break;
                case 'restored-root':
                    if (has('Project Atlas') && has('draft-notes.txt')) {
                        advance('restored-root', 'observed:both-root-items');
                        finished = true;
                        persist('PASS');
                        clearInterval(interval);
                    }
                    break;
            }
            if (!finished && performance.now() - began > 38000) {
                finished = true;
                persist('FAIL', 'Timed out waiting for actual native CUI interaction: ' + expected[step]);
                clearInterval(interval);
            }
        } catch (error) {
            finished = true;
            persist('FAIL', String(error?.message ?? error).slice(0, 240));
            clearInterval(interval);
        }
    }
    const interval = setInterval(tick, 180);
    persist();
})();
