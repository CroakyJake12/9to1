import { createBrowserPlatform } from './browser-platform.js';
import { createBrowserAccessibility } from './browser-accessibility.js';
import * as waveBrowser from './wave-browser.js';
import * as pictureBrowser from './picture-browser.js';
import { createConfiguredAccounts, handleOAuthPopupCallback } from './configured-accounts.bundle.js';
import { createNotesModule } from './notes-indexeddb.js';
import * as writePackages from './write-packages.js';
import { createPresentModule } from './present-indexeddb.js';
import { createPrivateContextLifecycle } from './browser-private-context.js';
import { createTaskExecutionHost } from './task-execution-host.js';

const platform = createBrowserPlatform(window, document);
let accessibility;
let unsubscribe;
let accounts;
let owner;
let notes;
let present;
let taskStorage;
let released = false;
let releaseTask;
const verifiedOwnerChange = Symbol('verified owner change');
const terminalRelease = Symbol('terminal private release');
const cleanupFailed = () => platform.showStatus('PrivateContextCleanupFailed',
    'Private content closed, but cleanup did not complete. Reload before signing in.');
const lifecycle = createPrivateContextLifecycle({
    beginOwnerReset: operation => operation === terminalRelease ? owner.RevokePrivateContext()
        : operation === verifiedOwnerChange ? owner.OwnedAccountContextChanged() : owner.PrivateContextInvalidated(),
    clearPresentation: () => { accessibility?.clear(); },
    onFailure: cleanupFailed,
});

// Terminal release is externally owned. Request-originated invalidation joins
// only native reset work, so it cannot wait on its own JS/native request cycle.
function releasePrivateAccountContext() {
    if (releaseTask) return releaseTask;
    let resolve, reject;
    releaseTask = new Promise((yes, no) => { resolve = yes; reject = no; });
    releaseTask.catch(() => {});
    released = true; // Prevent new private replacement before any callback.
    const errors = [];
    try { lifecycle.begin(terminalRelease); } catch (error) { errors.push(error); }
    for (const cleanup of [() => accessibility?.dispose(), () => unsubscribe?.()]) {
        try { cleanup(); } catch (error) { lifecycle.holdFailure(error); errors.push(error); }
    }
    let accountDrain;
    try {
        accountDrain = accounts.disposeAsync();
        if (typeof accountDrain?.then !== 'function') throw new TypeError('Actual account disposal must return its drain task.');
    } catch (error) { lifecycle.holdFailure(error); errors.push(error); }
    let presentDrain;
    try { presentDrain = present?.dispose(); }
    catch (error) { lifecycle.holdFailure(error); errors.push(error); }
    let taskStorageDrain;
    try { taskStorageDrain = taskStorage?.dispose(); }
    catch (error) { lifecycle.holdFailure(error); errors.push(error); }
    Promise.allSettled([lifecycle.join(), accountDrain, presentDrain, taskStorageDrain]).then(async settled => {
        for (const result of settled) if (result.status === 'rejected') errors.push(result.reason);
        // A real broker continuation may issue another native reset after the
        // first join settles. Join it once all broker work has truly settled.
        try { await lifecycle.join(); } catch (error) { errors.push(error); }
        if (errors.length) {
            for (const error of errors) lifecycle.holdFailure(error);
            reject(new AggregateError(errors, 'Terminal private cleanup failed.'));
        }
        else resolve();
    });
    return releaseTask;
}

async function replaceVerifiedOwner() {
    const version = lifecycle.version;
    await lifecycle.join();
    if (released || version !== lifecycle.version) throw new DOMException('Private context changed.', 'AbortError');
    const expectedVersion = version + 1;
    lifecycle.begin(verifiedOwnerChange);
    await lifecycle.join();
    if (released || expectedVersion !== lifecycle.version) throw new DOMException('Private context changed.', 'AbortError');
}
const isCompatible = typeof WebAssembly === 'object' && typeof WebAssembly.instantiate === 'function'
    && typeof BigInt === 'function' && typeof globalThis.fetch === 'function'
    && typeof globalThis.ResizeObserver === 'function';

try {
    // These optional fields are public registration data supplied by the host.
    // The maintained issuer and current-account API establish identity.
    const accountConfiguration = window.nineToOneBrowserConfiguration?.account ?? null;
    if (!handleOAuthPopupCallback(accountConfiguration)) {
        accounts = createConfiguredAccounts({ configuration: accountConfiguration, window,
            beginPrivateContextInvalidation: reason => lifecycle.begin(released ? terminalRelease : reason),
            onPrivateContextInvalidated: () => lifecycle.join(),
            onVerifiedIdentity: replaceVerifiedOwner,
            onFailure: () => platform.showStatus('AuthenticationRequired', 'Sign-in did not complete. Try signing in again.') });
        notes = createNotesModule();
        present = createPresentModule();
        taskStorage = createTaskExecutionHost();
        if (!isCompatible) throw new Error('BrowserCapabilityUnavailable');
        const { dotnet } = await import('./_framework/dotnet.js');
        const runtime = await dotnet.create();
        runtime.setModuleImports('nineToOneBrowser', { ...platform, releasePrivateAccountContext });
        runtime.setModuleImports('nineToOneWave', waveBrowser);
        runtime.setModuleImports('nineToOnePicture', pictureBrowser);
        runtime.setModuleImports('nineToOneAccounts', accounts);
        runtime.setModuleImports('nineToOneTaskIdentity', {
            readCurrent: id => accounts.readTaskIdentity(id),
            confirmCurrent: id => accounts.confirmTaskIdentity(id),
            cancel: id => accounts.cancelTaskIdentity(id),
            release: id => accounts.releaseTaskIdentity(id),
        });
        runtime.setModuleImports('nineToOneTaskExecution', taskStorage);
        runtime.setModuleImports('nineToOneNotes', notes);
        runtime.setModuleImports('nineToOnePresent', present);
        runtime.setModuleImports('nineToOneWritePackages', writePackages);
        const config = runtime.getConfig();
        const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
        owner = exports.NineToOne.Web.Program;
        await accounts.prepare();
        accessibility = createBrowserAccessibility(window, document, owner);
        unsubscribe = platform.subscribe(owner.LocationChanged,
            () => { releasePrivateAccountContext().catch(cleanupFailed); },
            () => { accounts.invalidatePrivateContext('page_restored').catch(() => {
                cleanupFailed();
            }); }, owner.HasUnsavedChanges);
        await runtime.runMain(config.mainAssemblyName, []);
    }
} catch (error) {
    if (accounts) {
        try { await releasePrivateAccountContext(); }
        catch { cleanupFailed(); }
    }
    console.error('9to1 browser startup failed.', error);
    platform.showStatus(isCompatible ? 'BrowserRuntimeUnavailable' : 'BrowserCapabilityUnavailable',
        isCompatible ? '9to1 could not start. Check your connection and reload the page.'
            : 'This browser cannot run 9to1. Use a browser with WebAssembly and modern JavaScript support.');
}
