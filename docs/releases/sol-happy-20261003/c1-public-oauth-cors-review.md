# Public OAuth CORS peer review

Reviewed immutable C2 proposal `4d94626ae3f0cb50939eea318f41b325a1a74116`, all four changed files, against `aedc29ec3a0a960e1ded36c0443f1efda4fcf36d`. Bounded source/runtime ACK; root alone decides adoption.

Production changes affect only src/index.ts. The exact discovery, JWKS and token paths receive an explicitly configured serialized HTTP(S) origin grant, without Allow-Credentials. Preflight checks the endpoint method and content-type-only headers; foreign/opaque token origins fail before exchange. Vary merging preserves existing response values. Auth configuration, trusted origins, resource API, provider lock and custody helper are unchanged. Installed Better Auth 1.7.7 origin-check middleware continues validating cookie-bearing mutation requests and first-login form origins; the token endpoint remains the provider POST endpoint with its original PKCE/client/resource handling. No CSRF skip or client-registration substitution was introduced. No actionable bounded regression found.

Independent replay from /workspace/team-c/c2-public-cors/cloud/cake-id-auth:

```sh
XDG_CONFIG_HOME=/workspace/team-c/evidence/c1/xdg CAKE_BROWSER_FIXTURE_SMOKE=1 CAKE_BROWSER_FIXTURE_PUBLIC_CORS_TEST=1 node tests/browser-fixture.mjs
```

Exit 0; 117 executed assertions against actual Workerd/D1/provider. Controls cover discovery/JWKS, allowed/foreign/opaque origins, preflight methods/headers, actual invalid exchange errors, bad verifier/code consumption, genuine PKCE exchange and JWT validation, API audience/scopes/session access, cookie/form CSRF denial, sign-out revocation. Raw log: /workspace/team-c/evidence/c1/public-cors-review/runtime.log. Strict custody receipt reports strictReaped and originalsDisappeared true with five kernel reaps. Owned .dev.vars removed; port8798 released to C2. No credentials printed or retained in this review.

The original negative runtime evidence remains in C2 evidence. This is Node transport with actual local services, not a browser navigation/rendering acceptance, deployed HTTPS issuer proof, .NET algorithm compatibility proof, or global service acceptance. Configured public origins remain an operator-owned allowlist.

## Original integration custody adapter

Additional source ACK: immutable `c560b02fbf067490205e6111536f97bc153784cc`. Complete original-to-adapter diff reviewed. Original integration.mjs and pidfd custodian remain byte-identical to AEDC. Independently verified 27,982-byte domain execution body SHA256 `815221d9eb88c9836f79c8c276e836504d8984c481501ada0e97335335f5d4d2`; entire 29,112-byte schema-through-final-cleanup suffix also identical. Only Linux capability preflight, private directory mode, reviewed custodian launch/control/receipt stop and readiness spawn-error handling differ. Awaited strict stop precedes original state deletion; no numeric process signals. Linux-only variance is explicit. No source blocker to separately root-authorized historical integration execution; this review does not claim that execution passed.
