# 9to1 Account Worker — current launch branch

Updated 16 November 2026. Read-only Cloudflare metadata confirms the owner-only identity Worker is serving version f7ed455e-709a-4e1e-98cb-5d68c6f3fe61 at 100 percent (deployment 95aedb62-7dff-4899-8097-635ee400d720, created 2026-11-15 17:38 UTC). Its live worker.mjs SHA-256 is 7123c42a9f2b13f38e17a72404df1b9d9d859d1e1607fa4123d73143338a5a07. Public registration remains held. Current outside source has not yet been reproduced byte-for-byte as that bundle; current src/pages.ts hash differs from the recorded validation hash and pinned local dry-runs did not match the live artifact. Do not claim an exact source-to-bundle mapping.

The 15 November local Workerd/D1 integration passed 248 assertions, including migration/restart idempotence. The later final-branding file run reported 163 tests: 161 passed, 2 failed (the known Linux-only custody fixtures), and 0 skipped. The earlier 130/132 file-test result is retained as historical and superseded by the final 163-test run.

Local initialization now applies canonical migrations0000–0006 with a local-only ledger. Do not regenerate or rewrite historical migration0001 for this additive release. Production migrations0003–0006 were applied through reviewed API calls with recovery bookmarks; local test configurations are never production binding authority. The production entrypoint remains `src/index.ts`; test bootstrap/mail capture are confined to `src/local.ts`.

The sections below are retained upstream architectural notes. Their historical statements that no deployment, email setup or D1 runtime validation exists are superseded by the current status above. Native.NET consumer compatibility remains separately unverified.

---

# CAKE ID Worker

This package is the proposed shared CAKE ID issuer for CAKE and 9to1. It uses Better Auth for password accounts, sessions, OAuth 2.1/OIDC, PKCE, token signing, password hashing, verification and recovery. D1 is the account and session store. It does not import WordPress users: the owner has explicitly excluded account transfer.

## Current state

The local Wrangler configs remain synthetic test-only inputs and are not production binding authority. The release also retains a build-only config and a receipt-bound config generator; neither contains deployable production secrets. The historical statement that no deployment settings or remote worker existed predates the 15 November owner-only release.

The maintained Better Auth OAuth provider enforces authorization-code PKCE and exact redirect matching. Dynamic Client Registration is disabled. Operators must register clients through the privileged path. ID tokens identify the public client; API access tokens are resource-bound and short lived. Domain permissions and entitlements are not copied into tokens. Resource API handlers check scopes, the canonical UUID subject, and active session state on every request. The issuer includes the configured auth path, matching discovery and signed tokens. Resource scope policy includes the approved OIDC scopes so public-client flows can receive an ID token alongside a resource-bound access token. Privileged client/resource administration reads the private role from the canonical D1 account row; it never trusts a user/session output role.

The administrator account page includes a separate exact-host registration path for the protected Present preview. See [Connect the protected Present preview](PRESENT_CLIENT_REGISTRATION.md) for the maintained owner flow, metadata reconciliation and validation limits.

`src/local.ts` is a test-only entrypoint. It applies the library-generated schema to Wrangler's local D1 emulator and captures verification/reset messages in a local outbox. It has a test-only administrator bootstrap guarded by an ephemeral key; the production entrypoint does not contain this route. The outbox is not an email provider and proves no real delivery.

## Shared 9to1 account API contract

The issuer uses the canonical OAuth scopes `cake:account:read`, `cake:profile:read`, `cake:profile:write`, `cake:sessions:read`, and `cake:sessions:revoke`. The API route names correspond to the 9to1 `WorkerAccountApiClient` routes: `GET /api/account/current`, `GET/PATCH /api/account/profile`, `GET /api/account/sessions`, `POST /api/account/signout`, `DELETE /api/account/sessions/{id}`, and `POST /api/account/revoke-other-sessions`. Registration requires nonblank Name and Username at the API boundary. Optional profile fields remain nullable; partial updates preserve omitted fields. Reserved usernames can be renamed only by the canonical account explicitly bound in the existing trusted reservation row; this package does not provision that binding. Profile writes use `expectedRevision` and an allowlisted `fields` object; the Worker applies the update with a database compare-and-swap and returns HTTP 409 for stale revisions. OAuth scopes are coarse gates; 9to1's resource servers remain responsible for their exact app/action/ownership permissions. No CAKE/9to1 app permission is inferred from a username, plan, or OAuth scope.

Bearer API requests still require resource-audience tokens, route scopes, canonical AccountID subjects, and active server-side sessions; records and mutations remain subject-scoped. Local Worker/D1 integration passed 248 assertions on 15 November, but native .NET consumer compatibility remains unverified. The live release record confirms owner-only sign-in; the current source-to-bundle byte mapping remains unresolved as stated above.

## Account theme preferences

`GET /api/account/theme` requires the existing `cake:profile:read` scope;
`PATCH /api/account/theme` requires `cake:profile:write`. Cookie callers retain
the issuer-origin mutation check. The body is `{expectedRevision, preference}`;
responses contain `{preference}`, with null for an account that has never saved
one. `preference` is the existing Home schema-v1 envelope with account scope 1,
verified AccountID, revision, theme, appearance, accessibility, accent choices
and optional `customThemesJson`. The latter is a validated declarative array
(maximum 32 designs / 1 MiB), with no executable content or remote assets.
Unknown envelope/accessibility fields survive older-client writes; explicit
accent maps and custom libraries replace their previous values, so removals work.

Forward-only migration `0002_account_theme_preference.sql` creates the account
row without changing existing IDs/profile revisions or migrating local data.
Apply it only through the reviewed backup/integrity/migration process for the
actual existing D1 binding. A successful preliminary JWT check is insufficient:
the SQL CAS itself checks current session ownership/expiry and bearer expiry.
Stale revisions return 409; a lost mutation response is an unknown outcome and
must be reconciled by reading, never blindly replayed.

The browser uses the original short-lived in-memory public-client token; no
credential is persisted in the preference cache. Account switch, signout and
token expiry synchronously clear theme observation and reject late replies.
These source and isolated-runtime checks do not prove hosted deployment or
cross-device account synchronization.

## Local workflow

1. `npm ci`
2. `npm run verify:runtime-dependencies` compares the installed Zod package's resolved `package.json` version and path with the exact package override and lock entry. It fails if the installed version is below 4.6.6 or differs from either pin. `npm run build:ui` runs this same gate before bundling the browser client.
3. `npm run build:worker -- --outdir <fresh absolute path outside this repository>` runs the runtime gate, then invokes the package-pinned Wrangler with the reviewed `deployment/wrangler.build.json` and `--dry-run --metafile`. The command only compiles; it does not upload. It writes a runtime preflight receipt alongside the fresh output directory. The output directory's parent must already exist, and the output path plus preflight/log paths must be new for each build.
4. `npm run schema:generate` regenerates `migrations/0001_better_auth.sql` from the pinned Better Auth configuration using Node's in-memory SQLite driver. Review and commit schema changes with library upgrades.
5. `npm run schema:validate` executes all versioned SQL migrations against an in-memory SQLite database and checks the expected account, OAuth, and guardrail tables. This validates SQL syntax and schema shape only; it does not emulate Workers or D1.
6. On 15 November, the maintained npm test run passed 248 assertions in local Wrangler Workerd and local D1, including migration bootstrap/restart. On 16 November, the Windows `tests/*.test.mjs` suite passed 165 tests with zero failures or skips after fixture portability and runtime-dependency-gate coverage. These are separate results; neither exercises remote APIs or external email delivery. The reusable Linux process-tree Workerd fixture itself remains Linux-only.
7. `npm run test:password-runtime` isolates Better Auth's default password hash and verify calls in local Workerd without D1 or account data. It checks the maintained library's 16-byte salt and 64-byte derived key and reports operation timings; it does not replace the full D1 authentication integration. On the current x64-emulated Windows ARM64 host the first functional run eventually passed, but the overall command took about 34 minutes and did not separate hash time from startup/shutdown. Treat the performance result as unqualified; rerun the instrumented check on a supported native Workerd host before sizing production CPU limits.
8. `npm run build:ui` builds the browser client code used by the sign-in, recovery, and consent screens after checking the installed runtime dependency.

Migrations 0000–0006 are versioned in this source candidate and were applied together to the isolated local D1 test database. The 15 November release notes state that migrations 0003–0006 were separately applied to the owner-only D1 after recovery bookmarks; this review did not read live database rows or independently replay those mutations. Preserve the existing recovery point before any future schema change.

Password hashing uses Better Auth maintained scrypt default. The 15 November local Workerd/D1 run exercised registration, verification capture, password reset, login, OAuth, profile/theme updates and session revocation. External verification/reset delivery remains unverified in this source-custody task; keep those flows fail-closed until the approved delivery path is separately validated.

## Current limits and remaining gates

- The owner-only identity Worker is deployed, and public account registration remains held. This package does not authorize a public audience change or a new client.
- The 15 November release notes state that the existing owner account and first-party website clients were exercised; this does not establish .NET WorkerAccountApiClient cross-process compatibility or product/action permission grants.
- External verification and password-reset email delivery is not established by the local outbox tests. Keep both actions fail-closed unless the approved delivery adapter and real recipient test are confirmed.
- The outside deployment notes say migrations 0003–0006 were applied after recovery bookmarks. Reconfirm the actual recovery point and migration receipt before any later schema action; no live D1 rows were accessed for this review.
- Current source and deployed bundle are not byte-identically reproduced by the recorded final source hashes plus the two bounded local dry-runs. Keep the deployment bundle hash and source tree as separate identities until a reproducible match is established.
- The two packages/test reports do not authorize any driver, DNS, billing, public signup, or AI change.

Production DNS, WordPress, Sites authoring/publishing, and account contents are unchanged.
