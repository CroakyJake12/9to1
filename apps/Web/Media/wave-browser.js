// Browser platform storage only. Canonical project JSON is produced/validated by WaveProjectStore.
const databaseName = '9to1-wave-local-v1';
let database;
let audio;
let audioUrl;
let dirty = false;
let pendingPicker;
const maximumBytes = 32 * 1024 * 1024;

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
export function release() { pendingPicker?.(); stopAudio(); dirty = false; }

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
            case 'play':
                stopAudio();
                audioUrl = URL.createObjectURL(new Blob([bytesFromBase64(args.base64)], { type: 'audio/wav' }));
                audio = new Audio(audioUrl); audio.loop = args.loop === true;
                await audio.play(); value = { playing: true }; break;
            case 'resume':
                if (!audio) throw fault('CapabilityUnavailable', 'No playback is loaded.');
                await audio.play(); value = { playing: true }; break;
            case 'pause': audio?.pause(); value = {}; break;
            case 'stop': if (audio) { audio.pause(); audio.currentTime = 0; } value = {}; break;
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
