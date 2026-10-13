# Maintained public-client bridge

`configured-accounts.bundle.js` composes `browser-public-client.js`, the reviewed `account-api-client.js`, and the existing `Services/account-service.js`. The issuer and registration authority remain Team C's maintained CAKE ID implementation at `aedc29ec3a0a960e1ded36c0443f1efda4fcf36d`, with its `BROWSER_FIXTURE.md` handoff at `dc1d60eec2bb4edcdce2ba5b04883d711e692678`. No issuer, client, redirect, account, permission, entitlement or auth-revision defaults are created here.

The host supplies optional `window.nineToOneBrowserConfiguration.account` with exactly these public fields: `issuer`, `apiResource`, `clientId`, `redirectUri`, `scopes`, and optional boolean `allowLoopbackForIsolatedTests`. Null configuration retains the truthful unavailable service. HTTPS is required except explicitly authorized isolated loopback HTTP issuer/resource use. The callback is HTTPS and exactly matches the configured same-origin redirect path. Public configuration must not contain passwords, client secrets, test keys, codes or tokens.

The existing protected preview supplies its registered public client in
`wwwroot/host-configuration.js` before the application module starts. It applies
only to the exact preview origin and preserves any explicitly supplied host
configuration. Other hosts, including local smoke runs, remain unconfigured.
The client was registered through the maintained administrator flow and its
public metadata was reconciled with the exact issuer resource and callback.
This configuration enables the real sign-in flow; it does not supply a signed-in
identity or establish account acceptance.

The browser host selects `signInMode: 'redirect'`; other existing consumers retain
the popup default. Same-tab navigation preserves the host's existing
`Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy:
require-corp`. The original popup transport requires an opener-preserving host
and is not used by this preview.

Host sequence:

1. Import the bundle before .NET startup and call `captureOAuthRedirectCallback(accountConfiguration)`. On the exact configured callback path, it removes the one-use sessionStorage transaction and the callback query before network exchange or application startup. It returns an opaque receipt. Missing, malformed, expired, future-dated, wrong-state, duplicate-state or differently configured transactions cannot resume sign-in. An ordinary new application load clears any abandoned transaction.
2. Create the configured module with `signInMode: 'redirect'`, `canRedirect` reading the real owner's unsaved state, and the existing private invalidation/verified/failure callbacks. After owner exports are available, await `module.prepare()`, subscribe the private lifecycle and run the actual application. If there is a callback receipt, call `module.resumeRedirect(receipt)` after startup. Successful token and current-account verification replaces the private presentation through the same existing account owner. Failure remains signed out.
3. The redirect broker creates fresh S256/state/nonce. Only a ten-minute, exact-configuration-bound `{state, nonce, verifier, createdAt, returnFragment}` transaction and its format/binding metadata are stored in per-tab sessionStorage. Account data, authorization codes, access tokens and refresh tokens are never persisted. Storage failure blocks navigation. The broker checks actual unsaved owner state both before cleanup and immediately before leaving. Return navigation is restricted to an original local fragment under `/`; no external return URL is accepted.
4. Departure cancellation joins the existing private owner and retains the bounded handoff for the new document. The callback consumes its receipt once, checks its age again after startup, and reuses the existing exchange verification: exact callback origin/path/state, discovery issuer, signed token issuer/audience, ID nonce/authorized party, stable subject, access session ID and the real current-account response. A callback alone grants nothing. Refresh tokens are discarded, and access tokens remain in memory only.
5. Sign-out, self revocation, expiry, failure and private context invalidation clear the in-memory credential supplier and drain the original operations. Reload after callback consumption cannot replay the handoff. BFCache restoration retains the existing private invalidation contract. A fresh account/profile read supplies Task/Spaces identity; sessionStorage never supplies an identity, permission or auth revision.

The host's initial CSS and module URLs are origin-root paths so the real
`/oauth/callback` SPA route loads those same resources. Registration and this
transport implementation still require real hosted sign-in, consent, account
boundary, persistence and recovery acceptance. Local negative controls do not
establish those gates.

The broker and account API bind the captured current `globalThis.fetch` to `globalThis`, because browser-native fetch requires its Window receiver. This also covers the configured module's forwarded native default. Injected transports that differ from the current global fetch retain their existing consumer-instance receiver and request arguments. A later replacement of global fetch does not change the captured transport. The receiver controls preserve cancellation, cleanup, denial and request policy; they do not establish sign-in acceptance. A production correction requires a fresh maintained Web publication and actual browser run against that new source and sealed tuple. Existing publication and failure receipts retain their original identities.

Rebuild using the acknowledged maintained fixture dependencies (its lock pins JOSE 6.2.12 and esbuild 0.28.1):

```sh
node apps/Web/Auth/build-public-client.mjs /workspace/team-b-c2-fixture/cloud/cake-id-auth/node_modules
node --test apps/Web/Auth/browser-public-client.test.js
node --test apps/Web/Auth/fetch-receiver.test.js
node --test apps/Web/Auth/browser-redirect-transaction.test.js
```

The build rejects other dependency versions and contains no runtime configuration. Node tests use scripted discovery and negative popup/token inputs; they are supporting unit evidence, not issuer, Worker/D1, browser/CUI, session, hosted-service or full parity acceptance. Real maintained-issuer browser acceptance remains pending and must retain exact source/build identities and strict fixture cleanup receipts. Icon upload, username availability, billing/entitlement and native RSA/auth-revision service contracts remain outstanding.

`Tests/run-account-redirect-controls.cjs` runs actual Chromium with local HTTPS
endpoints and a denial-only provider. It verifies same-tab navigation, unchanged
cross-origin isolation, callback consumption, reload and replay denial. It never
returns a token or registers an account, and is not hosted authentication
acceptance. Supply a freshly generated local test certificate/key and a new
evidence directory; private production credentials are not inputs.

The inference bridge uses only original verified profile observations and the
memory-held bearer, on the two fixed same-origin `/api/dulche/` paths. Each
exchange belongs to the same private account lifetime. Inference-originated 401
invalidates credentials and fences native ownership immediately, while its
full cleanup join is retained by the account module. The original HTTP operation
must terminate before that encompassing native drain can finish. Terminal
account disposal joins the retained work and preserves later cleanup failures.
The browser stream bridge returns `{retired:true}` only for its own current-owner
guard or an exact privately issued retirement/abort receipt. A matching error
name or code does not authorize this terminal. Cleanup failures retain their
original identity. These are lifecycle observations, never authentication or
execution permissions; the native governed turn and real service still decide
whether inference may run.
