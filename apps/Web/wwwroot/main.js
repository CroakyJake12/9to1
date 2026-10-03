import { createBrowserPlatform } from './browser-platform.js';
import { createBrowserAccessibility } from './browser-accessibility.js';
import * as waveBrowser from './wave-browser.js';
import { createConfiguredAccounts, handleOAuthPopupCallback } from './configured-accounts.bundle.js';
import { createNotesModule } from './notes-indexeddb.js';
import * as writePackages from './write-packages.js';

const platform = createBrowserPlatform(window, document);
let accessibility;
let unsubscribe;
let accounts;
let owner;
let notes;
const isCompatible = typeof WebAssembly === 'object' && typeof WebAssembly.instantiate === 'function'
    && typeof BigInt === 'function' && typeof globalThis.fetch === 'function'
    && typeof globalThis.ResizeObserver === 'function';

try {
    // These optional fields are public registration data supplied by the host.
    // The maintained issuer and current-account API establish identity.
    const accountConfiguration = window.nineToOneBrowserConfiguration?.account ?? null;
    if (!handleOAuthPopupCallback(accountConfiguration)) {
        accounts = createConfiguredAccounts({ configuration: accountConfiguration, window,
            onPrivateContextInvalidated: () => { accessibility?.clear(); owner?.PrivateContextInvalidated(); },
            onVerifiedIdentity: () => owner?.OwnedAccountContextChanged(),
            onFailure: () => platform.showStatus('AuthenticationRequired', 'Sign-in did not complete. Try signing in again.') });
        notes = createNotesModule();
        if (!isCompatible) throw new Error('BrowserCapabilityUnavailable');
        const { dotnet } = await import('./_framework/dotnet.js');
        const runtime = await dotnet.create();
        runtime.setModuleImports('nineToOneBrowser', platform);
        runtime.setModuleImports('nineToOneWave', waveBrowser);
        runtime.setModuleImports('nineToOneAccounts', accounts);
        runtime.setModuleImports('nineToOneNotes', notes);
        runtime.setModuleImports('nineToOneWritePackages', writePackages);
        const config = runtime.getConfig();
        const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
        owner = exports.NineToOne.Web.Program;
        await accounts.prepare();
        accessibility = createBrowserAccessibility(window, document, owner);
        unsubscribe = platform.subscribe(owner.LocationChanged,
            () => { accessibility.dispose(); unsubscribe(); accounts.dispose(); owner.PrivateContextInvalidated(); },
            () => { accessibility.clear(); accounts.invalidatePrivateContext('page_restored').catch(() => {
                owner.PrivateContextInvalidated();
                platform.showStatus('PermissionRequired', 'Sign in before reopening private content.');
            }); }, owner.HasUnsavedChanges);
        await runtime.runMain(config.mainAssemblyName, []);
    }
} catch (error) {
    accessibility?.dispose();
    unsubscribe?.();
    accounts?.dispose();
    console.error('9to1 browser startup failed.', error);
    platform.showStatus(isCompatible ? 'BrowserRuntimeUnavailable' : 'BrowserCapabilityUnavailable',
        isCompatible ? '9to1 could not start. Check your connection and reload the page.'
            : 'This browser cannot run 9to1. Use a browser with WebAssembly and modern JavaScript support.');
}
