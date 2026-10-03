import { createBrowserPlatform } from './browser-platform.js';
import { createBrowserAccessibility } from './browser-accessibility.js';
import * as waveBrowser from './wave-browser.js';
import { createAccountModule } from './account-service.js';

const platform = createBrowserPlatform(window, document);
let accessibility;
let unsubscribe;
// The account owner supplies a reviewed client configuration. Unconfigured calls fail explicitly.
const accounts = createAccountModule();
const isCompatible = typeof WebAssembly === 'object' && typeof WebAssembly.instantiate === 'function'
    && typeof BigInt === 'function' && typeof globalThis.fetch === 'function'
    && typeof globalThis.ResizeObserver === 'function';

try {
    if (!isCompatible) throw new Error('BrowserCapabilityUnavailable');
    const { dotnet } = await import('./_framework/dotnet.js');
    const runtime = await dotnet.create();
    runtime.setModuleImports('nineToOneBrowser', platform);
    runtime.setModuleImports('nineToOneWave', waveBrowser);
    runtime.setModuleImports('nineToOneAccounts', accounts);
    const config = runtime.getConfig();
    const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
    const owner = exports.NineToOne.Web.Program;
    accessibility = createBrowserAccessibility(window, document, owner);
    unsubscribe = platform.subscribe(owner.LocationChanged,
        () => { accessibility.dispose(); unsubscribe(); accounts.dispose(); owner.CloseShell(); },
        () => { accessibility.clear(); owner.PrivateContextInvalidated(); });
    await runtime.runMain(config.mainAssemblyName, []);
} catch (error) {
    accessibility?.dispose();
    unsubscribe?.();
    accounts.dispose();
    console.error('9to1 browser startup failed.', error);
    platform.showStatus(isCompatible ? 'BrowserRuntimeUnavailable' : 'BrowserCapabilityUnavailable',
        isCompatible ? '9to1 could not start. Check your connection and reload the page.'
            : 'This browser cannot run 9to1. Use a browser with WebAssembly and modern JavaScript support.');
}
