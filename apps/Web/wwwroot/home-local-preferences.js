// One Home-owned origin-local presentation envelope. No account/private layout data.
const key = '9to1.home.local-appearance.v1';
const lockName = key + ':writer';
const listeners = new Map();
let nextListener = 1;
const defaults = () => ({ Version: 1, AiTiles: false, AiReorder: false,
    Tiles: ['continue', 'apps', 'activity', 'services'].map(Id => ({ Id, Pinned: false, Locked: false, Hidden: false })) });
function parse(raw) {
    if (raw === null) return defaults();
    if (typeof raw !== 'string' || raw.length > 8192) throw new TypeError('Invalid Home preferences.');
    const value = JSON.parse(raw);
    if (!value || Array.isArray(value) || value.Version !== 1 || !Array.isArray(value.Tiles) || value.Tiles.length !== 4 ||
        value.Tiles.map(tile => tile?.Id).sort().join(',') !== 'activity,apps,continue,services')
        throw new TypeError('Unsupported Home preferences schema. Existing data is preserved.');
    return value;
}
function readEnvelope() { return parse(globalThis.localStorage.getItem(key)); }
function writeEnvelope(value) {
    const json = JSON.stringify(value);
    if (json.length > 8192) throw new TypeError('Home preferences exceed their size limit.');
    globalThis.localStorage.setItem(key, json);
    if (globalThis.localStorage.getItem(key) !== json) throw new Error('Home preferences were not saved.');
}
function locked(callback) {
    if (!globalThis.navigator?.locks?.request) throw new Error('This browser does not support safe shared preference writes.');
    return globalThis.navigator.locks.request(lockName, { mode: 'exclusive' }, callback);
}
function changed() {
    for (const callback of listeners.values()) {
        try { callback(); } catch { console.error('A Home preference observer could not refresh.'); }
    }
}
export function load() { return globalThis.localStorage.getItem(key); }
export async function save(json) {
    const incoming = parse(json);
    await locked(() => {
        const current = readEnvelope();
        // Dashboard saves never replace the freshest theme or device identity,
        // including when the caller began editing before another tab changed it.
        writeEnvelope({ ...current, Version: incoming.Version, AiTiles: incoming.AiTiles,
            AiReorder: incoming.AiReorder, Tiles: incoming.Tiles });
    });
    changed();
}
export async function themeContext() {
    return await locked(() => {
        const current = readEnvelope();
        if (current.DeviceId !== undefined && (typeof current.DeviceId !== 'string' || !/^[a-f0-9-]{36}$/i.test(current.DeviceId)))
            throw new TypeError('Invalid Home preference device identity.');
        if (!current.DeviceId) { current.DeviceId = globalThis.crypto.randomUUID(); writeEnvelope(current); }
        return 'browser:' + current.DeviceId;
    });
}
function exactScope(envelope, scope) { return envelope.DeviceId && scope === 'browser:' + envelope.DeviceId; }
function validAccent(accent) {
    if (accent == null) return true; // Existing v1 preferences inherit app defaults.
    const colour = value => typeof value === 'string' && /^#[0-9A-F]{6}$/.test(value);
    if (!accent || Array.isArray(accent) || ![0, 1, 2].includes(accent.mode) ||
        accent.allAppsColour != null && !colour(accent.allAppsColour) ||
        !accent.individualApps || typeof accent.individualApps !== 'object' || Array.isArray(accent.individualApps)) return false;
    const entries = Object.entries(accent.individualApps);
    return entries.length <= 32 && entries.every(([id, value]) => id.length <= 64 &&
        /^app\.[a-z][a-z0-9.-]*$/.test(id) && !id.endsWith('.') && !id.includes('..') && colour(value));
}
function validTheme(value, scope) {
    const a = value?.accessibility;
    const font = text => text == null || typeof text === 'string' && text.trim() && text.length <= 128 && !/[,\u0000-\u001f]/.test(text);
    return value?.schemaVersion === 1 && Number.isSafeInteger(value.revision) && value.revision >= 0 &&
        value.scope?.scope === 0 && value.scope.ownerId === scope && value.scope.organizationId == null &&
        validAccent(value.accent) &&
        (value.appearance == null || ['SuperBright', 'Bright', 'Dark', 'SuperDark'].includes(value.appearance)) &&
        typeof value.themeName === 'string' && value.themeName.trim() && value.themeName.length <= 128 &&
        !/[\u0000-\u001f]/.test(value.themeName) && a && typeof a.reduceMotion === 'boolean' && typeof a.highContrast === 'boolean' &&
        Number.isFinite(a.displayScale) && a.displayScale >= 0.5 && a.displayScale <= 5 && font(a.interfaceFontFamily) && font(a.codeFontFamily);
}
export async function readTheme(scope) {
    try {
        return await locked(() => {
            const current = readEnvelope();
            if (!exactScope(current, scope)) return JSON.stringify({ state: 3, diagnostic: 'This device preference scope is not authorised.' });
            if (current.Theme != null && !validTheme(current.Theme, scope))
                return JSON.stringify({ state: 4, diagnostic: 'The saved theme is incompatible. Its original data is preserved.' });
            return JSON.stringify({ state: 0, preference: current.Theme ?? null });
        });
    } catch { return JSON.stringify({ state: 4, diagnostic: 'Home preferences could not be read. Existing data is preserved.' }); }
}
export async function compareTheme(scope, expectedRevision, json) {
    if (!Number.isSafeInteger(expectedRevision) || expectedRevision < 0 || typeof json !== 'string' || json.length > 4096)
        return JSON.stringify({ succeeded: false, code: 'InvalidArgument' });
    const candidate = JSON.parse(json);
    const reply = await locked(() => {
        const current = readEnvelope();
        if (!exactScope(current, scope) || candidate.scope?.scope !== 0 || candidate.scope.ownerId !== scope || candidate.scope.organizationId != null)
            return { succeeded: false, code: 'PermissionDenied' };
        const revision = current.Theme?.revision ?? 0;
        if (!Number.isSafeInteger(revision) || revision < 0 || current.Theme && !validTheme(current.Theme, scope))
            return { succeeded: false, code: 'ThemeSchemaUnsupported', diagnostic: 'The existing theme is preserved.' };
        if (revision !== expectedRevision) return { succeeded: false, code: 'RevisionConflict' };
        if (!validTheme(candidate, scope) || candidate.revision !== expectedRevision + 1)
            return { succeeded: false, code: 'InvalidArgument' };
        const stored = { ...current.Theme, ...candidate,
            accent: candidate.accent ?? current.Theme?.accent,
            appearance: candidate.appearance ?? current.Theme?.appearance, accessibility: { ...current.Theme?.accessibility, ...candidate.accessibility } };
        writeEnvelope({ ...current, Theme: stored });
        return { succeeded: true, code: 'Succeeded', preference: stored };
    });
    if (reply.succeeded) changed();
    return JSON.stringify(reply);
}
export function subscribe(callback) {
    if (typeof callback !== 'function') throw new TypeError('A preference observer is required.');
    const id = nextListener++; listeners.set(id, callback); return id;
}
export function unsubscribe(id) { listeners.delete(id); }
if (typeof globalThis.addEventListener === 'function') globalThis.addEventListener('storage', event => {
    if (event.storageArea === globalThis.localStorage && (event.key === key || event.key === null)) changed();
});
