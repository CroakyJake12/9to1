// Device-local I/O. Editable document and raster authority stay in unchanged owner C#.
let database, pendingPicker;
let dirty = false;
const databaseName = '9to1-picture-local-v1';
const maximumBytes = 24 * 1024 * 1024;
globalThis.addEventListener?.('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
// Only this actual module producer can authorize a custom fault message.
const moduleFaults = new WeakSet();
function fault(code, message) {
    const error = new Error(message); error.code = code;
    moduleFaults.add(error); return error;
}
const moduleFaultCodes = new Set(['CapabilityUnavailable', 'StorageBlocked', 'InvalidArgument',
    'CapacityExceeded', 'RevisionConflict', 'StorageFailed', 'OperationBusy', 'OperationCancelled', 'DocumentNotFound']);
function browserFailure(error) {
    const fallback = { ok: false, code: 'StorageFailed', message: 'The browser operation failed. Your prior saved document is intact.' };
    try {
        const ownCode = error?.code;
        const custom = moduleFaults.has(error) && typeof ownCode === 'string' && moduleFaultCodes.has(ownCode);
        const name = custom ? undefined : error?.name;
        const ownMessage = custom ? error?.message : undefined;
        const code = custom ? ownCode : name === 'QuotaExceededError' ? 'StorageFull'
            : name === 'NotAllowedError' || name === 'SecurityError' ? 'PermissionDenied' : 'StorageFailed';
        return { ok: false, code, message: custom && typeof ownMessage === 'string' ? ownMessage
            : code === 'StorageFull' ? 'Local storage is full. Your unsaved edits are retained.'
            : code === 'PermissionDenied' ? 'The browser refused this operation. Your saved document is intact.'
            : fallback.message };
    } catch { return fallback; }
}

function openDatabase() {
    if (!globalThis.indexedDB) throw fault('CapabilityUnavailable', 'Local Picture storage is unavailable in this browser.');
    if (database) return Promise.resolve(database);
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => {
            request.result.createObjectStore('documents', { keyPath: 'documentId' });
            request.result.createObjectStore('summaries', { keyPath: 'documentId' });
        };
        request.onerror = () => reject(request.error);
        request.onblocked = () => reject(fault('StorageBlocked', 'Close another Picture tab to upgrade local storage.'));
        request.onsuccess = () => {
            database = request.result;
            database.onversionchange = () => { database.close(); database = null; };
            resolve(database);
        };
    });
}
async function read(storeName, key) {
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        const transaction = db.transaction(storeName, 'readonly');
        const request = key === undefined ? transaction.objectStore(storeName).getAll() : transaction.objectStore(storeName).get(key);
        request.onsuccess = () => resolve(request.result ?? null);
        request.onerror = () => reject(request.error);
    });
}
async function commit({ bundle, expectedRevision }) {
    if (!bundle || typeof bundle.documentId !== 'string' || !Number.isSafeInteger(bundle.revision) || bundle.revision < 0
        || !Number.isSafeInteger(expectedRevision) || expectedRevision < -1 || typeof bundle.documentJson !== 'string'
        || typeof bundle.sourceBase64 !== 'string' || !Array.isArray(bundle.undo) || !Array.isArray(bundle.redo))
        throw fault('InvalidArgument', 'The local Picture package is invalid.');
    if (new TextEncoder().encode(JSON.stringify(bundle)).length > maximumBytes)
        throw fault('CapacityExceeded', 'This local adapter stores packages up to 24 MiB. Saved state is intact.');
    const db = await openDatabase();
    return new Promise((resolve, reject) => {
        let failure;
        const transaction = db.transaction(['documents', 'summaries'], 'readwrite');
        const store = transaction.objectStore('documents'); const request = store.get(bundle.documentId);
        request.onsuccess = () => {
            const previous = request.result;
            if ((previous ? previous.revision : -1) !== expectedRevision || previous && bundle.revision <= previous.revision) {
                failure = fault('RevisionConflict', 'This document changed in another tab. Your unsaved edits are retained.');
                transaction.abort(); return;
            }
            store.put(bundle);
            transaction.objectStore('summaries').put({ documentId: bundle.documentId, name: bundle.name, revision: bundle.revision });
        };
        transaction.oncomplete = () => resolve({ revision: bundle.revision });
        transaction.onerror = () => reject(failure ?? transaction.error);
        transaction.onabort = () => reject(failure ?? transaction.error ?? fault('StorageFailed', 'The local save was interrupted.'));
    });
}
function encode(bytes) {
    let value = '';
    for (let offset = 0; offset < bytes.length; offset += 8192) value += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
    return btoa(value);
}
function pick() {
    if (pendingPicker) throw fault('OperationBusy', 'A file chooser is already open.');
    return new Promise((resolve, reject) => {
        const input = document.createElement('input'); input.type = 'file'; input.accept = '.png,image/png'; input.hidden = true; document.body.append(input);
        let completed = false;
        const finish = (error, value) => { if (completed) return; completed = true; pendingPicker = null; input.remove(); error ? reject(error) : resolve(value); };
        pendingPicker = () => finish(fault('OperationCancelled', 'The file chooser was cancelled.'));
        input.addEventListener('cancel', pendingPicker, { once: true });
        input.addEventListener('change', async () => {
            try {
                const file = input.files?.[0]; if (!file) throw fault('OperationCancelled', 'No image was selected.');
                if (file.size > 8 * 1024 * 1024) throw fault('CapacityExceeded', 'Choose a PNG of at most 8 MiB for this adapter.');
                finish(null, { name: file.name, base64: encode(new Uint8Array(await file.arrayBuffer())) });
            } catch (error) { finish(error); }
        }, { once: true });
        input.click();
    });
}
function download({ name, base64 }) {
    const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: 'image/png' }));
    const link = document.createElement('a'); link.href = url; link.download = name; document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000); return { initiated: true };
}
export function setDirty(value) { dirty = value === true; }
export function release() { pendingPicker?.(); dirty = false; }
export async function invoke(action, argumentsJson) {
    try {
        const args = JSON.parse(argumentsJson); let value;
        switch (action) {
            case 'list': value = (await read('summaries')).sort((a, b) => a.name.localeCompare(b.name)); break;
            case 'open': value = await read('documents', args.documentId); if (!value) throw fault('DocumentNotFound', 'This local document was not found.'); break;
            case 'commit': value = await commit(args); break;
            case 'pick': value = await pick(); break;
            case 'download': value = download(args); break;
            default: throw fault('CapabilityUnavailable', 'This browser Picture operation is unavailable.');
        }
        return JSON.stringify({ ok: true, value });
    } catch (error) {
        return JSON.stringify(browserFailure(error));
    }
}
