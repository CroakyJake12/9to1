// Public registration owned by the existing protected preview host. The maintained
// issuer and verified account API establish identity; this configuration does not.
(() => {
    if (window.location.origin !== 'https://present-4581358882177bc7.9to1.uk'
        || window.nineToOneBrowserConfiguration !== undefined) return;

    window.nineToOneBrowserConfiguration = Object.freeze({
        account: Object.freeze({
            issuer: 'https://cake-id-release-validation.jcbailey008.workers.dev/api/auth',
            apiResource: 'https://cake-id-release-validation.jcbailey008.workers.dev',
            clientId: 'bWvasfNBgzpIMztdIFoApeoYrPqmrYsL',
            redirectUri: 'https://present-4581358882177bc7.9to1.uk/oauth/callback',
            scopes: 'openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke',
        }),
    });
})();
