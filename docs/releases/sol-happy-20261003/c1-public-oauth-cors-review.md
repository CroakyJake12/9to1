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

Root subsequently held c560 before execution: a throwing stopWorker could mask a simultaneous original assertion failure. Source ACK superseded by exact `de3506973eec8f6014a3d1c617ef18a607219e56` lifecycle correction. Independently reviewed every delta and ran `node tests/integration-lifecycle.test.mjs` (exit0). AggregateError retains the actual original and cleanup Error objects and cleanup cause; refused strict stop still prevents state deletion. Independently verified original assertion body 27,783 bytes SHA256 `e9406fdb224761e57d0ec5a03e31720025caac04da96a5eddff453141e0e9bc1`. This closes the concrete Error masking finding; no historical integration runtime execution or root issuance is implied by source ACK.

## Issuer-origin regression correction (runtime pending)

Root subsequently reproduced an issuer-origin regression in 4d when the configured public allowlist contained only the client origin. Earlier ACK is qualified by that finding. Independently reviewed all lines of `78dc9ef..04ff6067d212bb9e8547a5ff93b859d9fc473118`: production gate exempts only exact request Origin equal to request URL origin, restoring original provider handling without a new CORS grant or trusted-origin/CSRF changes. Foreign/opaque origins remain subject to the existing denial. New test executes genuine issuer cookie/S256 exchange, JWT signature/audience, current account and session checks through the maintained fixture transport; the wrapper explicitly sets issuer Origin/cookies. Source ACK only; independent issuer-only-allowlist replay awaits exclusive port8798 custody.

Independent exact04ff issuer-only replay completed: `XDG_CONFIG_HOME=/workspace/team-c/evidence/c1/xdg CAKE_BROWSER_FIXTURE_SMOKE=1 CAKE_BROWSER_FIXTURE_ISSUER_ORIGIN_TEST=1 node tests/browser-fixture.mjs` exit0, 27 actual Workerd/D1 assertions. Both issuer-origin invalid-code requests, with and without cookie, return provider 400 invalid_grant with no CORS grant; genuine cookie-bearing S256 exchange, signed JWT audience, current account and matching session pass. Strict receipt true/true with five kernel reaps; .dev.vars removed and8798 released to C2. Raw /workspace/team-c/evidence/c1/public-cors-review/issuer-origin-04ff.log. Bounded regression closure only; C2 full historical222 run and production/browser acceptance remain separate.
