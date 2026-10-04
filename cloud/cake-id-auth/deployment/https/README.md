This additive anonymous HTTPS driver targets ONLY the actual isolated `cake-id-release-validation.jcbailey008.workers.dev` issuer, deployment713c1695-7e21-489b-bc97-906450972cfa, maintained producer source d643d723. It changes no producer, binding, secret, migration or deployment. Root remains the provider writer. Run only after the exact hostname is permitted and the root operator deliberately enables the isolated endpoint.

From `cloud/cake-id-auth`:

```
node --check deployment/https/validate.mjs
node deployment/https/validate.test.mjs
node deployment/https/validate.mjs /workspace/team-c/evidence/c2/https-anonymous-NEW.json
```

The output path must be new. Transport/assertion failures and independent evidence-write failures remain separately observable; existing evidence is never overwritten. Requests omit credentials, never follow redirects and have15-second abort signals. Output retains status/CORS/path metadata, not response bodies, cookies or tokens. No secrets are required.

Assertions follow maintained integration/public-cors semantics: exact issuer/discovery endpoints/S256/code, public-only JWKS, exact-path public CORS without credential grant, foreign/opaque origin denial, public page security headers, unauthenticated account denial, malformed reset rejection and unknown-client/invalid-code token rejection. The malformed reset has no password/token and is rejected by pinned library body validation before consuming recovery tokens. No verification/recovery request, registration, login or administrative request is sent. Discovery/JWKS may initialise the library's own issuer signing key if none exists; this is maintained server GET behaviour, not a credential or account fixture.

Offline controls deliberately reject controlled transport loss, redirects, wildcard CORS, foreign discovery issuer and output overwrite while retaining both original and write exceptions. They are driver validation only. The actual HTTPS phase does not demonstrate registration, delivery, valid PKCE token exchange, account switching, profile/session mutation, organisation membership, browser/native continuity or production readiness.

A later authenticated phase needs a separately approved isolated D1 fixture plan: two fictional canonical accounts verified through an approved delivery/identity authority, one exact operator-approved public-client registration/HTTPS callback and audience, private credential custody, actual PKCE/refresh/current-session tests and fixture retention/cleanup policy. Production source lacks local-test bootstrap endpoints by design. Never insert local issuer keys, fabricate auth_revision, promote a real identity, bind CroakyJake, turn on outbox capture or invent a native consumer fallback to make that phase pass.
