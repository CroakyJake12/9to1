// Browser platform storage only. Canonical project JSON is produced/validated by WaveProjectStore.
const databaseName = '9to1-wave-local-v1';
let database;
let audio;
let audioUrl;
// Local device request validity only; never a project, asset or hosted Files revision.
let audioGeneration = 0;
let dirty = false;
let pendingPicker;
const maximumBytes = 32 * 1024 * 1024;

// One-use Wave input focus authority only; never a project/audio/source revision.
// Empty/unknown ownership denies. Browser/chrome handoff cannot be undone by re-enable.
let focusVersion = 0;
let focusObservers;
let focusClaim;
let focusExhausted = false;

function revokeFocusClaim() {
    focusClaim = undefined;
    if (focusVersion >= Number.MAX_SAFE_INTEGER) focusExhausted = true;
    else focusVersion++;
}
function releaseFocusClaims() {
    revokeFocusClaim();
    const state = focusObservers;
    focusObservers = undefined;
    if (!state) return;
    for (const [target, name, listener] of state.listeners) {
        try { target.removeEventListener(name, listener, true); } catch { focusExhausted = true; }
    }
}
function ownsWaveHost(state) {
    const doc = state.document;
    const active = doc.activeElement;
    return doc === globalThis.document && doc.hasFocus() === true && doc.visibilityState === 'visible'
        && state.native.isConnected === true && state.semantic.isConnected === true
        && state.native.ownerDocument === doc && state.semantic.ownerDocument === doc
        && doc.getElementById('nine-to-one-root') === state.native
        && doc.getElementById('native-control-semantics') === state.semantic
        && !!active && (state.native.contains(active) || state.semantic.contains(active));
}
function observeFocusClaims() {
    if (focusObservers) return focusObservers;
    const doc = globalThis.document;
    if (!doc || typeof doc.hasFocus !== 'function' || typeof doc.getElementById !== 'function'
        || typeof doc.addEventListener !== 'function' || typeof doc.removeEventListener !== 'function'
        || typeof globalThis.addEventListener !== 'function' || typeof globalThis.removeEventListener !== 'function')
        return undefined;
    const native = doc.getElementById('nine-to-one-root');
    const semantic = doc.getElementById('native-control-semantics');
    if (!native || !semantic || typeof native.contains !== 'function' || typeof semantic.contains !== 'function')
        return undefined;
    const state = { document: doc, native, semantic, listeners: [] };
    focusObservers = state;
    const install = (target, name, listener) => {
        // Record before installation so any partial setup can be retired fail-closed.
        state.listeners.push([target, name, listener]);
        target.addEventListener(name, listener, true);
    };
    try {
        install(doc, 'pointerdown', revokeFocusClaim);
        install(doc, 'keydown', revokeFocusClaim);
        install(doc, 'focusin', event => {
            try { if (!native.contains(event.target) && !semantic.contains(event.target)) revokeFocusClaim(); }
            catch { revokeFocusClaim(); }
        });
        install(globalThis, 'blur', revokeFocusClaim);
        install(doc, 'visibilitychange', () => {
            try { if (doc.visibilityState !== 'visible') revokeFocusClaim(); } catch { revokeFocusClaim(); }
        });
        install(globalThis, 'pagehide', releaseFocusClaims);
        return state;
    } catch {
        releaseFocusClaims();
        return undefined;
    }
}
export function captureFocusClaim() {
    try {
        revokeFocusClaim();
        if (focusExhausted) return '';
        const state = observeFocusClaims();
        if (!state || !ownsWaveHost(state)) return '';
        const token = 'wave-focus:' + focusVersion;
        focusClaim = { token, version: focusVersion, state };
        return token;
    } catch { releaseFocusClaims(); return ''; }
}
export function validateFocusClaim(token) {
    // Consume before native Focus can cause a reentrant DOM/native focus event.
    const claim = focusClaim;
    focusClaim = undefined;
    try {
        return typeof token === 'string' && token.length !== 0 && !focusExhausted
            && !!claim && token === claim.token && claim.version === focusVersion
            && claim.state === focusObservers && ownsWaveHost(claim.state);
    } catch { releaseFocusClaims(); return false; }
}


const beforeUnload = event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } };
globalThis.addEventListener?.('beforeunload', beforeUnload);

function fault(code, message) { const error = new Error(message); error.code = code; return error; }
// Own module faults retain their documented string codes; native DOMException.code is numeric.
const moduleFaultCodes = new Set(['InvalidArgument', 'CapacityExceeded', 'RevisionConflict', 'StorageFailed',
    'StorageBlocked', 'OperationBusy', 'OperationCancelled', 'CapabilityUnavailable', 'ProjectNotFound', 'InvalidTimeRange']);
function browserFailure(error) {
    const fallback = { ok: false, code: 'StorageFailed', message: 'The browser operation failed. Your prior saved project is intact.' };
    try {
        const ownCode = error?.code;
        const custom = typeof ownCode === 'string' && moduleFaultCodes.has(ownCode);
        const name = custom ? undefined : error?.name;
        const ownMessage = custom ? error?.message : undefined;
        const code = custom ? ownCode : name === 'QuotaExceededError' ? 'StorageFull'
            : name === 'NotAllowedError' || name === 'SecurityError' ? 'PermissionDenied'
            : name === 'NotSupportedError' ? 'CodecUnsupported' : 'StorageFailed';
        return { ok: false, code, message: custom && typeof ownMessage === 'string' ? ownMessage
            : code === 'PermissionDenied' ? 'The browser refused this operation. Your saved project is intact.'
            : code === 'StorageFull' ? 'Local browser storage is full. Your unsaved work has been preserved.'
            : fallback.message };
    } catch { return fallback; }
}
function openDatabase() {
    if (!globalThis.indexedDB) throw fault('CapabilityUnavailable', 'This browser cannot store local Wave projects.');
    if (database) return Promise.resolve(database);
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => {
            request.result.createObjectStore('projects', { keyPath: 'projectId' });
            request.result.createObjectStore('summaries', { keyPath: 'projectId' });
        };
        request.onerror = () => reject(request.error);
        request.onblocked = () => reject(fault('StorageBlocked', 'Close another Wave tab to upgrade local storage.'));
        request.onsuccess = () => {
            database = request.result;
            database.onversionchange = () => { database.close(); database = null; };
            resolve(database);
        };
    });
}

async function readProject(id) {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const transaction = db.transaction('projects', 'readonly');
        const request = transaction.objectStore('projects').get(id);
        request.onsuccess = () => resolve(request.result ?? null);
        request.onerror = () => reject(request.error);
    });
}

async function listProjects() {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const transaction = db.transaction('summaries', 'readonly');
        const request = transaction.objectStore('summaries').getAll();
        // Project lists read metadata only, never every retained audio package.
        request.onsuccess = () => resolve(request.result.sort((a, b) => a.name.localeCompare(b.name)));
        request.onerror = () => reject(request.error);
    });
}

async function commitProject({ bundle, expectedRevision }) {
    if (!bundle || typeof bundle.projectId !== 'string' || !Number.isSafeInteger(bundle.revision)
        || bundle.revision < 0 || !Number.isSafeInteger(expectedRevision) || expectedRevision < -1
        || typeof bundle.projectJson !== 'string' || !Array.isArray(bundle.sources)
        || !Array.isArray(bundle.undo) || !Array.isArray(bundle.redo))
        throw fault('InvalidArgument', 'The local project package is invalid.');
    if (new TextEncoder().encode(JSON.stringify(bundle)).length > maximumBytes)
        throw fault('CapacityExceeded', 'This local browser adapter supports packages up to 32 MiB. Your prior saved project is intact.');
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        let failure;
        // Revision comparison and replacement share one transaction: two tabs cannot overwrite each other.
        const transaction = db.transaction(['projects', 'summaries'], 'readwrite');
        const store = transaction.objectStore('projects');
        const read = store.get(bundle.projectId);
        read.onsuccess = () => {
            const previous = read.result;
            if ((previous ? previous.revision : -1) !== expectedRevision
                || previous && bundle.revision <= previous.revision) {
                failure = fault('RevisionConflict', 'This project changed in another tab. Your unsaved work has been preserved.');
                transaction.abort();
                return;
            }
            store.put(bundle);
            transaction.objectStore('summaries').put({ projectId: bundle.projectId, name: bundle.name, revision: bundle.revision });
        };
        transaction.oncomplete = () => resolve({ revision: bundle.revision });
        transaction.onerror = () => reject(failure ?? transaction.error);
        transaction.onabort = () => reject(failure ?? transaction.error ?? fault('StorageFailed', 'The local save was interrupted.'));
    });
}

function bytesFromBase64(value) {
    const decoded = atob(value);
    return Uint8Array.from(decoded, character => character.charCodeAt(0));
}
function base64FromBytes(bytes) {
    let text = '';
    for (let offset = 0; offset < bytes.length; offset += 8192)
        text += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
    return btoa(text);
}

function pickWav() {
    if (pendingPicker) throw fault('OperationBusy', 'A file chooser is already open.');
    return new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file'; input.accept = '.wav,audio/wav,audio/x-wav';
        input.hidden = true; document.body.append(input);
        let completed = false;
        const finish = (error, value) => {
            if (completed) return;
            completed = true; pendingPicker = null; input.remove();
            error ? reject(error) : resolve(value);
        };
        pendingPicker = () => finish(fault('OperationCancelled', 'The file chooser was cancelled.'));
        input.addEventListener('cancel', pendingPicker, { once: true });
        input.addEventListener('change', async () => {
            try {
                const file = input.files?.[0];
                if (!file) throw fault('OperationCancelled', 'No audio file was selected.');
                if (file.size > maximumBytes / 2) throw fault('CapacityExceeded', 'Choose a WAV file smaller than 16 MiB for this local adapter.');
                finish(null, { name: file.name, base64: base64FromBytes(new Uint8Array(await file.arrayBuffer())) });
            } catch (error) { finish(error); }
        }, { once: true });
        input.click();
    });
}

function stopAudio() {
    ++audioGeneration;
    if (audio) { audio.pause(); audio.removeAttribute('src'); audio.load(); audio = null; }
    if (audioUrl) { URL.revokeObjectURL(audioUrl); audioUrl = null; }
}

function download({ name, base64, mime }) {
    const url = URL.createObjectURL(new Blob([bytesFromBase64(base64)], { type: mime ?? 'application/octet-stream' }));
    const link = document.createElement('a'); link.href = url; link.download = name;
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    return { initiated: true };
}

export function setDirty(value) { dirty = value === true; }
export function release() { releaseFocusClaims(); pendingPicker?.(); stopAudio(); dirty = false; }

export async function invoke(action, argumentsJson) {
    try {
        const args = JSON.parse(argumentsJson);
        let value;
        switch (action) {
            case 'list': value = await listProjects(); break;
            case 'open':
                value = await readProject(args.projectId);
                if (!value) throw fault('ProjectNotFound', 'This local project was not found.');
                break;
            case 'commit': value = await commitProject(args); break;
            case 'pick': value = await pickWav(); break;
            case 'download': value = download(args); break;
            case 'play': {
                stopAudio();
                audioUrl = URL.createObjectURL(new Blob([bytesFromBase64(args.base64)], { type: 'audio/wav' }));
                const target = new Audio(audioUrl); audio = target; target.loop = args.loop === true;
                const generation = audioGeneration;
                try { await target.play(); }
                catch (error) {
                    if (audio !== target || audioGeneration !== generation)
                        throw fault('OperationCancelled', 'This playback was superseded or released. The current device media is unchanged.');
                    throw error;
                }
                if (audio !== target || audioGeneration !== generation)
                    throw fault('OperationCancelled', 'This playback was superseded or released. The current device media is unchanged.');
                value = { playing: true }; break;
            }
            case 'resume': {
                if (!audio) throw fault('CapabilityUnavailable', 'No playback is loaded.');
                const target = audio; const generation = ++audioGeneration;
                try { await target.play(); }
                catch (error) {
                    if (audio !== target || audioGeneration !== generation)
                        throw fault('OperationCancelled', 'This resume was superseded or released. The current device media is unchanged.');
                    throw error;
                }
                if (audio !== target || audioGeneration !== generation)
                    throw fault('OperationCancelled', 'This resume was superseded or released. The current device media is unchanged.');
                value = { playing: true }; break;
            }
            case 'pause': ++audioGeneration; audio?.pause(); value = {}; break;
            case 'stop': ++audioGeneration; if (audio) { audio.pause(); audio.currentTime = 0; } value = {}; break;
            case 'seek':
                if (!audio) throw fault('CapabilityUnavailable', 'Play this project before seeking.');
                if (!Number.isFinite(args.seconds) || args.seconds < 0 || args.seconds > audio.duration)
                    throw fault('InvalidTimeRange', 'Choose a position within this project.');
                audio.currentTime = args.seconds; value = {}; break;
            case 'loop': if (audio) audio.loop = args.loop === true; value = {}; break;
            case 'state': value = { position: audio?.currentTime ?? 0, duration: Number.isFinite(audio?.duration) ? audio.duration : 0,
                paused: audio?.paused ?? true, ended: audio?.ended ?? false }; break;
            case 'unload': stopAudio(); value = {}; break;
            default: throw fault('CapabilityUnavailable', 'This browser media operation is unavailable.');
        }
        return JSON.stringify({ ok: true, value });
    } catch (error) {
        return JSON.stringify(browserFailure(error));
    }
}
