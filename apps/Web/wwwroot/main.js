import { createBrowserPlatform } from './browser-platform.js';

const platform = createBrowserPlatform(window, document);
const isCompatible = typeof WebAssembly === 'object' && typeof WebAssembly.instantiate === 'function'
    && typeof BigInt === 'function' && typeof globalThis.fetch === 'function'
    && typeof globalThis.ResizeObserver === 'function';

try {
    if (!isCompatible) throw new Error('BrowserCapabilityUnavailable');
    const { dotnet } = await import('./_framework/dotnet.js');
    const runtime = await dotnet.create();
    runtime.setModuleImports('nineToOneBrowser', platform);
    const config = runtime.getConfig();
    const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
    let unsubscribe;
    unsubscribe = platform.subscribe(exports.NineToOne.Web.Program.LocationChanged,
        () => { unsubscribe(); exports.NineToOne.Web.Program.CloseShell(); },
        exports.NineToOne.Web.Program.PrivateContextInvalidated);
    await dotnet.run();
} catch {
    platform.showStatus(isCompatible ? 'BrowserRuntimeUnavailable' : 'BrowserCapabilityUnavailable',
        isCompatible ? '9to1 could not start. Check your connection and reload the page.'
            : 'This browser cannot run 9to1. Use a browser with WebAssembly and modern JavaScript support.');
}
