# Browser account transport

This adapter binds only the acknowledged CAKE ID account routes from `cloud/cake-id-auth/src/resource-api.ts` and `README.md` at commit `7cc52df0411e0e9e617918f6c5158283cc3199fc`. Root obtained Team C acknowledgement at coordination readback `41e0ccc`. It preserves server wire bodies and error codes; it owns no authentication, billing, permissions, entitlements or account cache.

The application owner must inject an explicit HTTPS API **origin**, a short-lived resource-audience bearer token supplier and a private-context invalidation callback. There are no issuer, client ID, redirect, resource or token defaults. The API origin must have no userinfo, path prefix, query or fragment. HTTP requires explicit isolated-test mode and literal loopback/localhost. The token supplier is the acknowledged OAuth client owner's responsibility. Provider credentials must never be supplied here.

Before account, session or organisation switching, await `invalidatePrivateContext(reason)` and discard all old private artifacts, account/profile views, caches and token context. Wire `onPrivateContextInvalidated` to the browser application's `ResetPrivateContext` / `Program.PrivateContextInvalidated` boundary supplied by B1, together with the account UI's own cleanup. Required cleanup failure blocks a session mutation. Revalidate the server session before registering fresh private surface adapters, including BFCache restoration. A 401 or missing token invalidates outstanding requests and calls the same boundary. Old generation results are discarded even if a transport ignores cancellation. Session mutations conservatively clear private views before dispatch.

Operations: `getCurrent`, `getProfile`, `updateProfile(expectedRevision, fields)`, `listSessions`, `signOut`, `revokeSession`, `revokeAllOtherSessions`. Every request accepts `{ signal }`. Profile updates forward only the specified fields, including explicit `null` clears, and do not fill omissions. Server revision checks, validation/reservation, authorisation and data normalization remain authoritative. Session mutation success requires the protocol's 204; ordinary reads/profile writes require validated 200 JSON. Return values distinguish transport/protocol/cancellation failures from source-defined errors and preserve conflict revision in `error.body`.

Profile updates read and validate the caller's scalar fields once into an independent plain snapshot before asynchronous token acquisition. Later caller edits and serialization hooks cannot alter the dispatched patch. The acknowledged Worker CAS statement updates a matching revision by exactly one; success requires that exact successor revision. The expected revision and its successor must both be safely representable integers, so an update at `Number.MAX_SAFE_INTEGER` is rejected before token acquisition or dispatch.

No request retries mutations automatically. Session mutations capture the current short-lived bearer before cleanup can clear/switch the token supplier, then invalidate private views before dispatch; concurrent context changes block dispatch. The captured bearer lives only on that call stack. Fetch uses `credentials: omit`, `cache: no-store`, `redirect: error` and `referrerPolicy: no-referrer`; credentials are never sent to redirected origins. This module stores no token or user records, makes no authentication request itself and does not parse token claims as authority.

Local checks:

```sh
node --test apps/Web/Auth/account-api-client.test.js
node --test apps/Web/Auth/account-api-client.profile-review.test.js
```

These fake-response and isolated loopback transport fixtures verify client logic only. They do not establish OAuth issuer, provider, real browser, account-session, UI, permissions, hosted service or parity acceptance. Staging issuer/API resource/public client/redirect, real delivery, verified reserved-account provisioning, and integration with C2's issuer remain absent/unverified. Profile icon is currently the server's HTTPS URL field; an icon-upload contract and username-availability action are not present in this acknowledged route set. Do not invent them or declare full profile acceptance.
