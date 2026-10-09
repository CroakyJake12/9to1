import assert from 'node:assert/strict';
import { createBrowserPlatform } from '../wwwroot/browser-platform.js';

let executed = 0;
const listeners = new Map();
const calls = [];
const status = { dataset: {}, textContent: '', hidden: false };
const windowObject = {
    location: { href: 'https://staging.example/apps/#/home.dashboard', hash: '#/home.dashboard' },
    history: {
        pushState(state, title, url) { calls.push(['push', state, title, String(url)]); windowObject.location.hash = url.hash; },
        replaceState(state, title, url) { calls.push(['replace', state, title, String(url)]); windowObject.location.hash = url.hash; },
    },
    addEventListener(name, fn) { listeners.set(name, fn); },
    removeEventListener(name, fn) { assert.equal(listeners.get(name), fn); listeners.delete(name); },
};
const documentObject = { getElementById: id => { assert.equal(id, 'browser-status'); return status; }, title: '' };
const platform = createBrowserPlatform(windowObject, documentObject);
function run(id, fn) { fn(); executed++; console.log(`PASS ${id}`); }

run('B1-HISTORY-01-duplicate-does-not-push', () => {
    platform.writeFragment('#/home.dashboard', false);
    assert.equal(calls.length, 0);
});
run('B1-HISTORY-02-context-address', () => {
    platform.writeFragment('#/home.library?entityId=opaque%2Fid', false);
    assert.equal(calls[0][0], 'push');
    assert.equal(calls[0][3], 'https://staging.example/apps/#/home.library?entityId=opaque%2Fid');
    assert.equal(platform.readFragment(), '#/home.library?entityId=opaque%2Fid');
});
run('B1-HISTORY-03-replace-is-explicit', () => {
    platform.writeFragment('#/home.events', true);
    assert.equal(calls[1][0], 'replace');
});
let observed, closed = 0, invalidated = 0;
let dirty = false, ownerFailed = false;
const unsubscribe = platform.subscribe(fragment => { observed = fragment; }, () => { closed++; }, () => { invalidated++; },
    () => { if (ownerFailed) throw new Error('UNIT owner unavailable'); return dirty; });
run('B1-HISTORY-04-back-forward-location', () => {
    windowObject.location.hash = '#/home.library?entityId=original';
    listeners.get('popstate')();
    assert.equal(observed, '#/home.library?entityId=original');
});
run('B1-HISTORY-05-status-is-text', () => {
    platform.showStatus('HomeServiceUnavailable', '<script>private-content</script>');
    assert.equal(status.textContent, '<script>private-content</script>');
    assert.equal(status.dataset.code, 'HomeServiceUnavailable');
    assert.equal(status.hidden, false);
    platform.showStatus('Ready', '');
    assert.equal(status.hidden, true);
});
run('B1-HISTORY-06-bfcache-preserves-live-runtime', () => {
    listeners.get('pagehide')({ persisted: true });
    assert.equal(closed, 0);
    assert.equal(invalidated, 1);
    listeners.get('pageshow')({ persisted: true });
    assert.equal(invalidated, 2);
    listeners.get('pagehide')({ persisted: false });
    assert.equal(closed, 1);
});
run('B1-HISTORY-08-clean-owner-does-not-warn', () => {
    let prevented = false;
    const event = { preventDefault() { prevented = true; } };
    listeners.get('beforeunload')(event);
    assert.equal(prevented, false);
    assert.equal(Object.hasOwn(event, 'returnValue'), false);
});
run('B1-HISTORY-09-actual-dirty-owner-warns-without-clearing', () => {
    dirty = true;
    let prevented = false;
    const event = { preventDefault() { prevented = true; } };
    listeners.get('beforeunload')(event);
    assert.equal(prevented, true);
    assert.equal(event.returnValue, '');
    assert.equal(dirty, true);
    assert.equal(closed, 1);
});
run('B1-HISTORY-10-owner-failure-cannot-confirm-saved', () => {
    ownerFailed = true;
    let prevented = false;
    const event = { preventDefault() { prevented = true; } };
    listeners.get('beforeunload')(event);
    assert.equal(prevented, true);
    assert.equal(event.returnValue, '');
});
run('B1-HISTORY-07-release-subscriptions', () => {
    unsubscribe(); assert.equal(listeners.size, 0);
});
console.log(`Discovered: 10; executed: ${executed}; passed: ${executed}; failed: 0. Real browser acceptance: NOT-RUN.`);
