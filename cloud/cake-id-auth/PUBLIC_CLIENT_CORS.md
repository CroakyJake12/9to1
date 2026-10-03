# Public-client CORS boundary

Owner request: `b5-maintained-public-client-cors-20261003` in Team B coordination commit `c2234f0d0306b10c628b4edae5feeed67761f819`. Original maintained source/fixture is `aedc29ec3a0a960e1ded36c0443f1efda4fcf36d`. Actual Workerd discovery returned 200 without ACAO from the exact configured HTTPS client origin; the preserved negative failed on that missing header and strictly reaped the original fixture family.

Only these exact public paths gain a browser transport boundary:

- GET `/api/auth/.well-known/openid-configuration` and GET `/api/auth/jwks`.
- POST `/api/auth/oauth2/token`; the public browser client uses form encoding, `credentials:omit`, no client secret, exact redirect, explicit resource and S256 PKCE.
- OPTIONS on those exact paths requires an explicitly configured valid serialized HTTP(S) Origin, the route's actual method and only `content-type` requested headers. Foreign/opaque/wildcard/malformed origins, wrong methods and unapproved headers are denied. Path prefixes and private session/login routes do not inherit public CORS.

`ALLOWED_WEB_ORIGINS` remains the existing exact configuration authority. No new origin default or wildcard/reflection policy is introduced. Public success and actual library error responses receive the exact allowed origin and `Vary: Origin`, preserving other Vary values. Public replies omit `Access-Control-Allow-Credentials`. Explicit foreign-Origin token POSTs are denied before token processing; Origin-less server requests retain the existing library path. Public metadata remains public data, but foreign origins receive no readable CORS grant. Account API CORS continues through its existing authenticated boundary.

Pinned Better Auth 1.7.7 `api/middlewares/origin-check.mjs` validates cookie-bearing requests in `validateOrigin`; the token route is not a first-login form route and credential-omitted public token exchange therefore requires no `trustedOrigins` expansion or CSRF bypass. Sign-in's `formCsrfMiddleware` force-validates first-login origins too. `auth.ts` stays unchanged with trustedOrigins `[baseURL]`; no disableOriginCheck/disableCSRFCheck option is introduced. Real tests retain 403 INVALID_ORIGIN for both first-login and cookie-bearing sign-in from the client origin, and for cookie-bearing client-origin token POSTs. Provider-maintained PKCE, client registration, signing, issuer/audience, canonical subject/sid, scopes and current-session checks remain authoritative.

Receiver startup uses this proposal's exact reviewed source ref and the existing `BROWSER_FIXTURE.md` workflow. Each receiver launches its own local fixture and private credentials; C's loopback files/listeners are not cross-chat credentials. Existing fixture settings remain issuer `http://127.0.0.1:8798/api/auth`, API origin AND audience `http://127.0.0.1:8798`, exact web callback `https://client.example.test:5096/callback`, B's isolated browser host mapping/test certificate. Port 5095 remains unstarted. No public DNS, global certificate trust, provider configuration or deployment is changed.

From this proposal's `cloud/cake-id-auth` directory:

```sh
npm ci --cache /workspace/team-c/evidence/c2/npm-cache
npm run typecheck
npm run schema:validate
python3 -B tests/linux-fixture-custodian.test.py
XDG_CONFIG_HOME=/workspace/team-c/evidence/c2/xdg \
CAKE_BROWSER_FIXTURE_SMOKE=1 CAKE_BROWSER_FIXTURE_PUBLIC_CORS_TEST=1 \
node tests/browser-fixture.mjs
```

The fixture's test-only hook exercises actual discovery/JWKS, allowed/foreign preflight and token errors, maintained client login/consent/code exchange, consumed wrong PKCE, signed ID/resource token validation with real returned JWKS, stable subject/session access, genuine wrong-audience and insufficient-scope denial, account API CORS errors, cookie/first-login CSRF denial, sign-out and old-token denial. The existing reviewed fork/pidfd/subreaper custody helper is unchanged; its strict receipt and successful exit remain mandatory before deleting private state. No token/response double or authentication-revision fabrication is used in these runtime tests.

This is local actual Worker/D1 protocol coverage with Node transport, not actual browser callback/profile/session completion, installed SDK/native/.NET or deployed acceptance. B owns the complete browser consent/composition/journey and will stop any old fixture through its original custodian before replacing it. Strict .NET RSA and genuine auth_revision semantics, Files actor adapter, real delivery and real reserved identity/org/provider gates remain separate unresolved seams.
