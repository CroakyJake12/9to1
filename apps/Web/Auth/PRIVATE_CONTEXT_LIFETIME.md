# Private account lifetime

The browser root synchronously removes private route admission, sensitive native bindings and accessibility references before cancellation callbacks. Every asynchronous owner, factory, command and native reset has a completion receipt registered before callbacks. Replacement waits for all issued resets; cleanup failures remain observable and prevent new private registration. Device-local owners retain their existing canonical sessions.

Account API authentication failures acknowledge the immediate fence and remain owned until their request settles. Their native owner drains those requests, avoiding a cleanup task waiting on itself. Explicit external invalidation also waits for the native reset ledger. Only the existing trusted sign-in and confirmed session mutation may transfer their original continuation to the real configured broker; reads and profile writes remain view-owned and caller-cancellable.

Terminal shell release calls the native revoke-only export before account cancellation. It awaits the real module/broker operations, including a token exchange that outlives cancellation of its outer sign-in, then joins any later native reset. A refused dirty close retains the shell and account module. Token expiry uses the same immediate native fence before API abort, followed by its full native join. Issuer, token validation, identity, server revision, permissions and profile CAS authority remain in the maintained clients and service.

Supporting evidence includes fourteen strict native root checks, twelve native account lifetime checks, seventeen root ordering/failure controls, ten API controls, six configured-client controls and thirteen broker controls. These unit/native fixtures do not establish provider or browser acceptance. Original failures are preserved in the Team B continuation evidence.

The historical browser sign-in failure passed the CUI view lifetime as caller cancellation, closing the popup when private reset disposed that loader before issuer dispatch. The current CUI loader supplies `CuiActionDispatchLifetime` to `ICuiLifetimeAwareActionDispatcher`, and the Account Settings and Home adapters consume its separate view and caller tokens. This preserves explicit caller cancellation while an acknowledged sign-in or confirmed session mutation outlives its own view cleanup. Genuine sign-in/profile/session, elapsed provider token expiry, authenticated BFCache, and the JSExport/JSImport composition still require fresh exact-candidate validation; the source contract does not establish those runtime results.


## Non-route private infrastructure lifetime

`BrowserSurfaceRegistry.RegisterPrivateLifetime` enrols the exact
`IBrowserPrivateContextParticipant` for account-bound storage/coordinator
infrastructure that owns no page. It creates no route, action availability,
identity or permission grant. Registering the same owner twice, including as a
route owner, is refused. A draft-owning participant still supplies the existing
`IBrowserCloseParticipant` preparation contract.

Private reset detaches this same cohort with private route owners, fences every
owner synchronously before callbacks, and joins their actual asynchronous drain.
Replacement remains refused while those original drains are pending or failed.
Terminal close also revokes the whole detached cohort before cleanup, preserves
late acknowledged outcomes, and retains cleanup failures. Synchronous clear
refuses these asynchronous lifetimes. Device-local editor owners retain the
existing private-reset behavior.

Source-linked non-route controls are separate from signed-session, canonical
Task permission, real IndexedDB and browser workflow acceptance. Enrollment
alone cannot make Home, Dev, a provider or an action available.
