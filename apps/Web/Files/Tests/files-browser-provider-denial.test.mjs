import assert from 'node:assert/strict';
import test from 'node:test';
import { createServer } from 'node:http';
import { createFilesBrowserBridge } from '../files-browser.js';

const folder = '9a043a89-f518-4c97-9a7c-296fb1a194b7';
const ownerDenied = { code: 'OwnerOnly', message: 'Files is currently private to its owner.', notStarted: true, retryAt: null };
const itemDenied = { code: 'PermissionDenied', message: 'Google Drive denied access to this item.', notStarted: true, retryAt: null };
const argsFor = action => action === 'get' ? JSON.stringify({ id: folder }) : action === 'list' ? JSON.stringify({ parentId: folder }) : '{}';
const requestClosed = /Files request group is closed/;

for (const action of ['list', 'get']) {
    test(`a ${action} item-level provider permission 403 retains owner and exact typed reply`, async t => {
        let lost = 0;
        const requests = [];
        const files = createFilesBrowserBridge({
            fetchImpl: async (url, init) => {
                requests.push({ url, init });
                return requests.length === 2 ? Response.json(itemDenied, { status: 403 }) : Response.json({ allowed: true });
            },
            onSessionLost: () => { lost++; },
        });
        t.after(async () => { files.revokeAll(); await files.drainAll(); });
        files.createGroup('current');
        const context = JSON.parse(await files.invoke('current', 'context', 'context', '{}'));
        assert.equal(context.status, 200);
        files.enroll('current');
        const denied = JSON.parse(await files.invoke('current', 'denied', action, argsFor(action)));
        assert.equal(denied.status, 403);
        assert.deepEqual(JSON.parse(denied.body), itemDenied);
        assert.equal(lost, 0);
        const other = JSON.parse(await files.invoke('current', 'other', 'usage', '{}'));
        assert.deepEqual(JSON.parse(other.body), { allowed: true });
        assert.equal(lost, 0);
        assert.equal(requests[1].init.credentials, 'same-origin');
        assert.equal(requests[1].init.redirect, 'error');
        assert.match(requests[1].url, /^\/api\/files\/v1\/items/);
    });
}

for (const [label, action, body, status, type] of [
    ['non-owner', 'list', ownerDenied, 403],
    ['sign-in loss', 'get', { code: 'SignInRequired', message: 'Sign in.' }, 401],
    ['bad item text', 'list', { ...itemDenied, message: 'Unexpected denial' }, 403],
    ['wrong code', 'get', { ...itemDenied, code: 'OwnerOnly' }, 403],
    ['not-started false', 'list', { ...itemDenied, notStarted: false }, 403],
    ['retry date set', 'get', { ...itemDenied, retryAt: 12345 }, 403],
    ['extraneous body properties', 'list', { ...itemDenied, more: true }, 403],
    ['permission-like mutation', 'mutate', itemDenied, 403],
    ['permission-like context', 'context', itemDenied, 403],
    ['permission-like usage', 'usage', itemDenied, 403],
    ['malformed item response', 'list', '{', 403, 'application/json'],
    ['non-json item response', 'get', '<html>Denied</html>', 403, 'text/html'],
]) {
    test(`${label} remains an authoritative session denial`, async t => {
        let lost = 0;
        const files = createFilesBrowserBridge({
            fetchImpl: async () => type ? new Response(body, { status, headers: { 'content-type': type } }) : Response.json(body, { status }),
            onSessionLost: () => { lost++; },
        });
        t.after(async () => { files.revokeAll(); await files.drainAll(); });
        files.createGroup('current');
        files.enroll('current');
        const denied = JSON.parse(await files.invoke('current', 'denial', action, argsFor(action)));
        assert.deepEqual(denied, { status, body: '' });
        assert.equal(lost, 1);
        await assert.rejects(files.invoke('current', 'later', 'usage', '{}'), requestClosed);
    });
}

test('stale item denial after cancellation cannot keep a retired group alive or call session-loss', async t => {
    const waiting = Promise.withResolvers();
    const entered = Promise.withResolvers();
    let lost = 0, cancelled = 0;
    const files = createFilesBrowserBridge({
        fetchImpl: async () => { entered.resolve(); return waiting.promise; },
        onSessionLost: () => { lost++; },
    });
    t.after(async () => { files.revokeAll(); await files.drainAll(); });
    files.createGroup('current');
    files.enroll('current');
    const work = files.invoke('current', 'denial', 'list', argsFor('list')).catch(e => e);
    await entered.promise;
    files.cancel('current', 'denial');
    waiting.resolve(new Response(new ReadableStream({ cancel() { cancelled++; } }), { status: 403, headers: { 'content-type': 'application/json' } }));
    assert.equal((await work).name, 'AbortError');
    assert.equal(cancelled, 1);
    assert.equal(lost, 0);
});


test('real loopback HTTP item denial retains the owner but owner-only denial fences it', async t => {
    const seen = [];
    const server = createServer((req, res) => {
        seen.push({ path: req.url, method: req.method, accept: req.headers.accept });
        const denied = req.url?.startsWith('/api/files/v1/items');
        const error = denied ? itemDenied : ownerDenied;
        res.writeHead(403, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'private, no-store' });
        res.end(JSON.stringify(error));
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    const url = `http://127.0.0.1:${address.port}`;
    let lost = 0;
    const files = createFilesBrowserBridge({
        fetchImpl: (path, init) => fetch(url + path, init),
        onSessionLost: () => { lost++; },
    });
    t.after(async () => { files.revokeAll(); await files.drainAll(); await new Promise(resolve => server.close(resolve)); });
    files.createGroup('connected');
    files.enroll('connected');
    const permission = JSON.parse(await files.invoke('connected', 'folder', 'list', argsFor('list')));
    assert.equal(permission.status, 403);
    assert.equal(JSON.parse(permission.body).code, 'PermissionDenied');
    assert.equal(lost, 0);
    const denial = JSON.parse(await files.invoke('connected', 'usage', 'usage', '{}'));
    assert.deepEqual(denial, { status: 403, body: '' });
    assert.equal(lost, 1);
    assert.equal(seen.length, 2);
    assert.deepEqual(seen.map(item => item.path.split('?')[0]), ['/api/files/v1/items', '/api/files/v1/usage']);
    assert.deepEqual(seen.map(item => item.method), ['GET', 'GET']);
    assert.deepEqual(seen.map(item => item.accept), ['application/json', 'application/json']);
});

test('oversized and stalled item-denial bodies fail closed without preserving a session', { timeout: 7000 }, async t => {
    // AbortSignal.timeout is unrefed by Node; keep the isolated test runner alive.
    const keepAlive = setInterval(() => {}, 100);
    t.after(() => clearInterval(keepAlive));
    for (const kind of ['oversized', 'stalled']) {
        let losses = 0, cancelled = 0;
        const content = kind === 'oversized' ? JSON.stringify({ ...itemDenied, padding: 'x'.repeat(5000) }) : null;
        const files = createFilesBrowserBridge({
            fetchImpl: async () => kind === 'oversized'
                ? new Response(content, { status: 403, headers: { 'Content-Type': 'application/json' } })
                : new Response(new ReadableStream({ cancel() { cancelled++; } }, { highWaterMark: 0 }),
                    { status: 403, headers: { 'Content-Type': 'application/json' } }),
            onSessionLost: () => { losses++; },
        });
        files.createGroup(kind);
        files.enroll(kind);
        const denied = JSON.parse(await files.invoke(kind, 'denial', 'get', argsFor('get')));
        assert.deepEqual(denied, { status: 403, body: '' });
        assert.equal(losses, 1);
        await assert.rejects(files.invoke(kind, 'after', 'usage', '{}'), requestClosed);
        if (kind === 'stalled') assert.equal(cancelled, 1);
        files.revokeAll();
        await files.drainAll();
    }
});


test('an un-enrolled request cannot use a provider-like 403 to bypass admission denial', async t => {
    let losses = 0;
    const files = createFilesBrowserBridge({
        fetchImpl: async () => Response.json(itemDenied, { status: 403 }),
        onSessionLost: () => { losses++; },
    });
    t.after(async () => { files.revokeAll(); await files.drainAll(); });
    files.createGroup('notEnrolled');
    const reply = JSON.parse(await files.invoke('notEnrolled', 'denied', 'list', argsFor('list')));
    assert.deepEqual(reply, { status: 403, body: '' });
    assert.equal(losses, 1);
});
