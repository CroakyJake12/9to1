'use strict';
// Production-path-independent tests of the test-only accessibility interaction
// driver. Full .NET/Avalonia execution remains a separate CI Chrome gate.
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, 'files-connected-navigation-probe.js'), 'utf8');

function harness({ missing = '', ready = true, hash = '#/app.files', disableOpen = false, ignoreSearch = false } = {}) {
    const actions = [];
    let observed = null;
    let interval;
    let ticks = 0;
    let folderSelected = false;
    let view = 'root';
    let searched = false;
    let text = '';
    let back = false;
    let forward = false;
    const page = { append(element) { observed = element; } };
    function labels() {
        const result = [];
        if (view === 'root') {
            result.push('Project Atlas');
            if (!searched) result.push('draft-notes.txt');
        } else result.push('inside.txt');
        result.push('Files root', 'Open selected folder', 'Back', 'Forward', 'Up', 'Search folder', 'Clear search');
        return result.filter(label => label !== missing);
    }
    function click(label) {
        actions.push(label);
        if (label === 'Project Atlas') folderSelected = true;
        if (label === 'Open selected folder' && folderSelected && !disableOpen) {
            view = 'nested'; folderSelected = false; back = true; forward = false;
        }
        if (label === 'Up' && view === 'nested') { view = 'root'; back = true; forward = false; }
        if (label === 'Back' && back) { view = 'nested'; back = false; forward = true; }
        if (label === 'Forward' && forward) { view = 'root'; back = true; forward = false; }
        if (label === 'Search folder' && !ignoreSearch) searched = text.toLowerCase() === 'atlas';
        if (label === 'Clear search') { searched = false; text = ''; }
    }
    function buttons() {
        return labels().map(label => ({
            disabled: (label === 'Open selected folder' && (!folderSelected || disableOpen))
                || (label === 'Up' && view !== 'nested')
                || (label === 'Back' && !back)
                || (label === 'Forward' && !forward)
                || (label === 'Clear search' && !searched),
            getAttribute(attribute) { return attribute === 'aria-label' ? label : null; },
            click() { click(label); },
        }));
    }
    const input = {
        disabled: false, readOnly: false, value: '',
        getAttribute(attribute) { return attribute === 'aria-label' ? 'Search names in this folder' : null; },
        dispatchEvent(event) { if (event.type === 'input') text = this.value; },
    };
    const context = {
        document: {
            body: page,
            createElement() { return { id: '', hidden: false, textContent: '' }; },
            querySelector(selector) { return selector === '#browser-status' ? { dataset: { code: ready ? 'Ready' : 'Loading' } } : null; },
            querySelectorAll(selector) {
                if (selector.endsWith('button[data-native-peer-id]')) return buttons();
                if (selector.endsWith('input[data-native-peer-id]')) return missing === 'search-input' ? [] : [input];
                return [];
            },
        },
        location: { hash },
        performance: { now: () => ticks * 180 },
        Event: class Event { constructor(type, init) { this.type = type; this.bubbles = init.bubbles; } },
        setInterval(callback) { interval = callback; return 1; },
        clearInterval() { interval = null; },
        JSON, Math, String,
    };
    vm.runInNewContext(script, context);
    function step(count = 1) { for (let i = 0; i < count; i++) { ticks++; if (interval) interval(); } }
    function result() { return JSON.parse(observed.textContent); }
    return { step, result, actions };
}

test('read-only native peer sequence selects, opens, navigates, searches and restores', () => {
    const h = harness();
    h.step(30);
    const result = h.result();
    assert.equal(result.status, 'PASS');
    assert.deepEqual(result.completed, [
        'select-folder', 'open-folder', 'inside-folder', 'up-to-root',
        'back-to-folder', 'forward-to-root', 'enter-search', 'search-results',
        'clear-search', 'restored-root',
    ]);
    assert.deepEqual(h.actions, ['Project Atlas', 'Open selected folder', 'Up', 'Back', 'Forward',
        'Search folder', 'Clear search']);
    assert.equal(result.realGoogleDriveAcceptance, 'NOT RUN');
    assert.equal(result.observations[2].action, 'observed:nested-only');
    assert.equal(result.observations[7].action, 'observed:folder-filtered');
});

test('a static root without a functioning Open does not produce a false pass', () => {
    const h = harness({ disableOpen: true });
    h.step(250);
    assert.equal(h.result().status, 'FAIL');
    assert.equal(h.result().phase, 'open-folder');
});

test('no searched provider change means result remains unverified', () => {
    const h = harness({ ignoreSearch: true });
    h.step(250);
    assert.equal(h.result().status, 'FAIL');
    assert.equal(h.result().phase, 'search-results');
});

test('missing required CUI peer refuses to progress', () => {
    for (const missing of ['Project Atlas', 'Open selected folder', 'Up', 'Back', 'Forward',
        'Search folder', 'Clear search', 'search-input']) {
        const h = harness({ missing });
        h.step(250);
        assert.notEqual(h.result().status, 'PASS', missing);
    }
});

test('render-ready and exact Files route are prerequisites to every action', () => {
    for (const config of [{ ready: false }, { hash: '#/other' }]) {
        const h = harness(config);
        h.step(250);
        assert.equal(h.result().status, 'RUNNING');
        assert.equal(h.result().completed.length, 0);
        assert.deepEqual(h.actions, []);
    }
});

test('actual peer names must be observed; no synthesized file identity or success', () => {
    const h = harness({ missing: 'draft-notes.txt' });
    h.step(250);
    const result = h.result();
    assert.equal(result.status, 'FAIL');
    assert.deepEqual(result.completed, []);
    assert.equal(result.source, 'read-only disposable CUI accessibility peer invocation');
});
