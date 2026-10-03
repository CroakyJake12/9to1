# Isolated actual Worker fixture for browser account transport

This helper uses the maintained `src/local.ts` Worker/D1 entrypoint, Better Auth and OAuth provider 1.7.7, canonical generated accounts, local outbox verification, and existing guarded synthetic admin promotion to register a public S256-PKCE client. It adds no production service, public endpoint, schema, policy, or authentication-revision claim. Source parent is `142ea1c61d31f77de98e8e0a2d0c7ae8142b0b84`, runtime-equivalent to independently reviewed `3e1b9462fa59dea815de8d1f92b13acd88ef8bda`.

The reusable custody checker requires Linux `/proc`, Python pidfd signaling/waitid support and verified kernel subreaper enrollment; unsupported authority exits 77 before creating secrets. Run from `cloud/cake-id-auth` in the isolated fixture worktree:

```sh
npm ci --cache /workspace/team-c/evidence/c2/npm-cache
XDG_CONFIG_HOME=/workspace/team-c/evidence/c2/xdg node tests/browser-fixture.mjs
```

The ready line prints only configuration and the private manifest path. Read that manifest in the test process; never print, screenshot, commit, or put its credentials, cookie values, local test key, OAuth codes, or tokens in chat/evidence. `.dev.vars` and `.local-run/<random>/browser-fixture-private.json` are ignored, exclusive-created mode 0600; the random directory is mode 0700. Both verified nonadmin accounts are generated fictional `example.test` identities with random passwords. The separate synthetic operator only registers this local client. CroakyJake remains reserved with no real owner binding or grant. Email capture stays in local D1, with no external delivery. Ctrl-C/SIGTERM requests shutdown over the private custodian stdin channel. The Python helper enrolls as a subreaper before spawning Wrangler, captures creator birth/group/session identity and descendant ancestry, verifies each opened pidfd against `/proc` before admitting it, and signals only stable kernel pidfds. Numeric PIDs/PGIDs are never signal targets. Escaped-session descendants remain tracked through genealogy and subreaper adoption. Cleanup requires kernel `waitid(P_PIDFD)` reaping, `ECHILD`, disappearance of original recorded births, an explicit `strictReaped/originalsDisappeared` receipt and successful custodian exit. Unknown/changed creator or descendant identity, unsupported authority, spawn error or drain timeout returns a nonzero failure and retains fixture state. The ready line alone is not a drain/cleanup acknowledgment. Only proven drain authorizes deletion of this helper's secrets and random D1 state.

The earlier `4111ef55` helper only established live-process quiescence, treating Z/X as exited; that is distinct from strict reaping and is superseded. Its failures and review hold remain in evidence. This successor's own subreaper must actually reap its own descendants; it does not signal/reap PID 1's pre-existing orphans or relax the original SDK driver's strict guard. The mechanism is a fixture-only helper, not native SDK acceptance.

Six isolated real-process controls exercise strict reaping, changed birth/session/creator denial before signals, drain timeout without a success receipt and an escaped-session descendant's adoption/reaping:

```sh
python3 -B tests/linux-fixture-custodian.test.py
```


Root allocated issuer port 8798 and B page port 5096; 5095 remains optional/unstarted. Check listeners before launch. While ready, C owns 8798 and its private fixture files; B owns its page listener 5096. Coordinate shutdown through root and do not run the destructive integration suite concurrently on 8798. This helper preserves D1 state during browser navigation/reload and repeated account/session actions until shutdown.

Configuration:

- Issuer: `http://127.0.0.1:8798/api/auth`. Discovery: `/api/auth/.well-known/openid-configuration`; use its actual authorization/token/JWKS endpoints. Tokens must come from the maintained code/consent/token routes, never be locally fabricated.
- API origin AND configured resource audience: `http://127.0.0.1:8798`. This deliberately overrides the historical integration audience identifier `http://127.0.0.1:5095` using the existing isolated `.dev.vars` knob; 5095 was not an API listener.
- Browser origin: `https://client.example.test:5096`. Exact registered callback: `https://client.example.test:5096/callback`. The pinned provider rejects ALL loopback-host redirects for `application_type:web`, including HTTPS. B must serve its isolated HTTPS page on loopback and use browser-local host resolution for `client.example.test` plus isolated test-certificate trust. No DNS or global certificate trust change is authorized. Do not relabel a browser client as native or relax provider validation. A Node fixture that merely inspects callback JSON is not full browser callback acceptance.
- Existing CORS knob permits issuer origin and that exact B origin only. Smoke verifies allowed preflight 204 and foreign-origin 403.
- Private manifest supplies actual generated public client ID, canonical AccountIDs, verified account credentials, discovery and scopes. Authorize with exact callback, fresh state/nonce, S256 challenge and `resource` equal to API origin; exchange using verifier, same callback, client ID and resource. Client has no secret. Keep state/nonce/PKCE checks in the browser owner implementation.
- Scopes: `openid profile email offline_access cake:account:read cake:profile:read cake:profile:write cake:sessions:read cake:sessions:revoke`. API routes require their separate coarse scopes and active canonical session on every request.

Browser `AccountApiClient` can use its existing explicit `allowLoopbackForIsolatedTests:true` for this HTTP API origin; no fetch-origin rewriting, proxy, test bearer, or invented actor header is needed. Actual methods: GET `/api/account/current` (direct object), GET/PATCH `/api/account/profile` (`{profile}`), GET `/api/account/sessions` (`{sessions}`), POST `/api/account/signout`, DELETE `/api/account/sessions/{id}`, POST `/api/account/revoke-other-sessions` (mutations 204). PATCH requires `{expectedRevision,fields}`; optional null clears and omissions retain canonical Worker semantics, success advances revision exactly one. AccountID and sid remain issuer-owned, and JWKS-backed audience/session enforcement remains unchanged.

For a disposable bootstrap check (creates real local D1 data then cleans up):

```sh
XDG_CONFIG_HOME=/workspace/team-c/evidence/c2/xdg CAKE_BROWSER_FIXTURE_SMOKE=1 node tests/browser-fixture.mjs
```

Bootstrap smoke establishes guarded local provisioning, exact supported client registration, discovery and CORS only. Actual browser-adapter durable profile/session journeys remain for B/C continuation. This does not establish deployed/provider/delivery/native acceptance. Strict .NET RSA key compatibility and genuine `auth_revision` lifecycle semantics remain unresolved; neither consumer nor producer has been weakened or fabricated. The existing legacy Files actor resolver is a separate authority and cannot consume these tokens without an acknowledged adapter contract.
