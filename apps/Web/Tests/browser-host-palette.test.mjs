import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { createBrowserPlatform } from '../wwwroot/browser-platform.js';

const html = await readFile(new URL('../wwwroot/index.html', import.meta.url), 'utf8');
const startupScript = html.match(/<script>([\s\S]*?)<\/script>/)[1];

test('canonical bright and dark host colours survive startup without creating another preference owner', () => {
    const storage = new Map([['9to1.home.local-appearance.v1', 'existing-owner-envelope']]);
    const localStorage = { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) };
    const current = new Map();
    const windowObject = { location: { hash: '' }, localStorage };
    const documentObject = { documentElement: { style: { setProperty: (key, value) => current.set(key, value) } } };
    const platform = createBrowserPlatform(windowObject, documentObject);
    for (const palette of [['#ffffff', '#111111', '#f4f6f8'], ['#06090d', '#eeeeee', '#121526']]) {
        platform.setHostPalette(...palette);
        assert.deepEqual([...current.values()], palette);
        current.clear();
        vm.runInNewContext(startupScript, { document: documentObject, localStorage, performance: { mark() {} } });
        assert.deepEqual([...current.values()], palette);
        assert.equal(storage.get('9to1.home.local-appearance.v1'), 'existing-owner-envelope');
        assert.deepEqual([...storage.keys()].sort(), ['9to1.cui.host-palette.v1', '9to1.home.local-appearance.v1']);
    }
    const before = storage.get('9to1.cui.host-palette.v1');
    platform.setHostPalette('url(untrusted)', '#ffffff', '#ffffff');
    assert.equal(storage.get('9to1.cui.host-palette.v1'), before);
    current.clear(); storage.set('9to1.cui.host-palette.v1', '{broken');
    assert.doesNotThrow(() => vm.runInNewContext(startupScript, { document: documentObject, localStorage, performance: { mark() {} } }));
    assert.equal(current.size, 0);
});
