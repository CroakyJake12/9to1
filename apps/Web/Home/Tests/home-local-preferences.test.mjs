import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { webcrypto } from 'node:crypto';

const source = await readFile(new URL('../../wwwroot/home-local-preferences.js', import.meta.url), 'utf8');
const key = '9to1.home.local-appearance.v1';
let importId = 0;
function fixture() {
    const data = new Map(); let tail = Promise.resolve(); const lockNames = [];
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: {
        getItem: name => data.get(name) ?? null, setItem: (name, value) => data.set(name, value) } });
    Object.defineProperty(globalThis, 'crypto', { configurable: true, value: webcrypto });
    // Unit-level Web Locks contract harness; real multi-tab browser proof is separate.
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { locks: { request(name, options, callback) {
        lockNames.push(name); const operation = tail.then(callback); tail = operation.catch(() => {}); return operation;
    } } } });
    return { data, lockNames };
}
const moduleUnderTest = () => import('data:text/javascript;base64,' + Buffer.from(source + '\n// instance ' + importId++).toString('base64'));
const preference = (scope, revision, themeName = 'Professional') => ({ schemaVersion: 1, scope: { scope: 0, ownerId: scope, organizationId: null },
    themeName, revision, accessibility: { reduceMotion: false, highContrast: false, displayScale: 1, interfaceFontFamily: null, codeFontFamily: null } });

test('optional four-mode appearance shares theme CAS, preserves old data and unknown fields', async () => {
    const { data } = fixture(); const owner = await moduleUnderTest(); const scope = await owner.themeContext();
    const legacy = preference(scope, 1);
    assert.equal(JSON.parse(await owner.compareTheme(scope, 0, JSON.stringify(legacy))).succeeded, true);
    assert.equal(JSON.parse(await owner.readTheme(scope)).preference.appearance, undefined);
    const oldDashboard = JSON.parse(owner.load());
    data.set(key, JSON.stringify({ ...oldDashboard, FutureEnvelope: 'preserve', Theme: { ...oldDashboard.Theme, futureTheme: 7,
        accessibility: { ...oldDashboard.Theme.accessibility, futureAccess: true } } }));
    let revision = 1;
    for (const appearance of ['SuperBright', 'Bright', 'Dark', 'SuperDark']) {
        const candidate = { ...preference(scope, revision + 1, 'Glow'), appearance };
        const result = JSON.parse(await owner.compareTheme(scope, revision, JSON.stringify(candidate)));
        assert.equal(result.succeeded, true); assert.equal(result.preference.appearance, appearance); ++revision;
        assert.equal(JSON.parse(await owner.readTheme(scope)).preference.appearance, appearance);
    }
    await owner.save(JSON.stringify(oldDashboard));
    const saved = JSON.parse(data.get(key));
    assert.equal(saved.Theme.appearance, 'SuperDark'); assert.equal(saved.Theme.revision, revision);
    assert.equal(saved.DeviceId, oldDashboard.DeviceId); assert.equal(saved.FutureEnvelope, 'preserve');
    assert.equal(saved.Theme.futureTheme, 7); assert.equal(saved.Theme.accessibility.futureAccess, true);
    const before = data.get(key);
    assert.equal(JSON.parse(await owner.compareTheme(scope, revision - 1, JSON.stringify({ ...preference(scope, revision), appearance: 'Bright' }))).code, 'RevisionConflict');
    assert.equal(JSON.parse(await owner.compareTheme(scope, revision, JSON.stringify({ ...preference(scope, revision + 1), appearance: 'FutureMode' }))).code, 'InvalidArgument');
    assert.equal(data.get(key), before);
    const incompatible = JSON.stringify({ ...saved, Theme: { ...saved.Theme, appearance: 'FutureMode' } });
    data.set(key, incompatible);
    assert.equal(JSON.parse(await owner.readTheme(scope)).state, 4);
    assert.equal(data.get(key), incompatible);
});

test('theme CAS and stale dashboard save share the same owner and lock', async () => {
    const { data, lockNames } = fixture(); const first = await moduleUnderTest(); const second = await moduleUnderTest();
    const scope = await first.themeContext(); const original = JSON.parse(first.load());
    const [one, two] = await Promise.all([first.compareTheme(scope, 0, JSON.stringify(preference(scope, 1, 'Glow'))),
        second.compareTheme(scope, 0, JSON.stringify(preference(scope, 1, 'Bubble')))]);
    assert.equal(JSON.parse(one).succeeded, true); assert.equal(JSON.parse(two).code, 'RevisionConflict');
    original.AiTiles = true; await second.save(JSON.stringify(original));
    const saved = JSON.parse(data.get(key)); assert.equal(saved.Theme.themeName, 'Glow'); assert.equal(saved.Theme.revision, 1);
    assert.equal(saved.AiTiles, true); assert.equal(saved.DeviceId, original.DeviceId);
    assert.equal(new Set(lockNames).size, 1);
});

test('foreign scopes, corrupt content and unknown envelopes are preserved', async () => {
    const { data } = fixture(); const owner = await moduleUnderTest(); const scope = await owner.themeContext();
    const before = data.get(key);
    assert.equal(JSON.parse(await owner.compareTheme('other', 0, JSON.stringify(preference(scope, 1)))).code, 'PermissionDenied');
    assert.equal(data.get(key), before);
    const dashboard = JSON.parse(before);
    for (const invalid of ['{broken', JSON.stringify({ ...dashboard, Version: 99 })]) {
        data.set(key, invalid); await assert.rejects(owner.save(JSON.stringify(dashboard))); assert.equal(data.get(key), invalid);
        assert.equal(JSON.parse(await owner.readTheme(scope)).state, 4);
    }
});

test('missing locking fails closed and observer failure cannot change a committed result', async () => {
    const { data } = fixture(); const owner = await moduleUnderTest(); const scope = await owner.themeContext();
    const subscription = owner.subscribe(() => { throw new Error('fixture observer'); });
    const receipt = JSON.parse(await owner.compareTheme(scope, 0, JSON.stringify(preference(scope, 1))));
    assert.equal(receipt.succeeded, true); assert.equal(JSON.parse(data.get(key)).Theme.revision, 1); owner.unsubscribe(subscription);
    const before = data.get(key); Object.defineProperty(globalThis, 'navigator', { configurable: true, value: {} });
    await assert.rejects(owner.save(before), /safe shared preference writes/); assert.equal(data.get(key), before);
});
