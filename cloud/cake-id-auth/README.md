# CAKE ID Worker

This package is the proposed shared CAKE ID issuer for CAKE and 9to1. It uses Better Auth for password accounts, sessions, OAuth 2.1/OIDC, PKCE, token signing, password hashing, verification and recovery. D1 is the account and session store. It does not import WordPress users: the owner has explicitly excluded account transfer.

## Current state

Only `wrangler.local.jsonc` and `wrangler.password-test.jsonc` are provided. They are loopback-only local test configurations with synthetic data. They contain no production account, issuer, client, redirect URI, database, mail, or deployment settings. There is deliberately no deploy script or remote binding. Do not infer live deployment details from local test values.

The maintained Better Auth OAuth provider enforces authorization-code PKCE and exact redirect matching. Dynamic Client Registration is disabled. Operators must register clients through the privileged path. ID tokens identify the public client; API access tokens are resource-bound and short lived. Domain permissions and entitlements are not copied into tokens. Resource API handlers check scopes, the canonical UUID subject, and active session state on every request. The issuer includes the configured auth path, matching discovery and signed tokens. Resource scope policy includes the approved OIDC scopes so public-client flows can receive an ID token alongside a resource-bound access token. Privileged client/resource administration reads the private role from the canonical D1 account row; it never trusts a user/session output role.

`src/local.ts` is a test-only entrypoint. It applies the library-generated schema to Wrangler's local D1 emulator and captures verification/reset messages in a local outbox. It has a test-only administrator bootstrap guarded by an ephemeral key; the production entrypoint does not contain this route. The outbox is not an email provider and proves no real delivery.

## Shared 9to1 account API contract

The issuer uses the canonical OAuth scopes `cake:account:read`, `cake:profile:read`, `cake:profile:write`, `cake:sessions:read`, and `cake:sessions:revoke`. The API routes match the current 9to1 `WorkerAccountApiClient` contract: `GET /api/account/current`, `GET/PATCH /api/account/profile`, `GET /api/account/sessions`, `POST /api/account/signout`, `DELETE /api/account/sessions/{id}`, and `POST /api/account/revoke-other-sessions`. Profile writes use `expectedRevision` and an allowlisted `fields` object; the Worker applies the update with a database compare-and-swap and returns HTTP 409 for stale revisions. OAuth scopes are coarse gates; 9to1's resource servers remain responsible for their exact app/action/ownership permissions. No CAKE/9to1 app permission is inferred from a username, plan, or OAuth scope.

Bearer API requests must present a valid resource-audience token, the required route scope, a canonical AccountID UUID subject, and an active server-side session row. The current account/profile/session records are always selected by that verified subject. Profile and session mutations are owner-scoped. Session revocation removes associated OAuth access and refresh token rows before removing the session. The 9to1 OIDC/API client source exists on Astra's active branch, but this package has not yet passed a real cross-process or deployed integration with it.

## Local workflow

1. `npm ci`
2. `npm run schema:generate` regenerates `migrations/0001_better_auth.sql` from the pinned Better Auth configuration using Node's in-memory SQLite driver. Review and commit schema changes with library upgrades.
3. `npm run schema:validate` executes both versioned SQL migrations against an in-memory SQLite database and checks the expected account, OAuth, and guardrail tables. This validates SQL syntax and schema shape only; it does not emulate Workers or D1.
4. `npm run test` is designed to run the full isolated Wrangler Worker and local D1 authentication scenario with random test credentials, a temporary auth secret, and a local email capture sink. It never contacts Cloudflare's remote API. The current Windows ARM64 host cannot execute the D1 integration: Workerd runs only through x64 emulation here, and its D1 binding fails before the auth flow. The test exits with a clear skip before creating `.dev.vars` or test state. This is a host limitation; the integration remains unverified until run on a supported native Workerd host.
5. `npm run test:password-runtime` isolates Better Auth's default password hash and verify calls in local Workerd without D1 or account data. It checks the maintained library's 16-byte salt and 64-byte derived key and reports operation timings; it does not replace the full D1 authentication integration. On the current x64-emulated Windows ARM64 host the first functional run eventually passed, but the overall command took about 34 minutes and did not separate hash time from startup/shutdown. Treat the performance result as unqualified; rerun the instrumented check on a supported native Workerd host before sizing production CPU limits.
6. `npm run build:ui` builds the browser client code used by the sign-in, recovery, and consent screens.

The versioned `migrations/0000_cake_guardrails.sql` and `migrations/0001_better_auth.sql` are intended for future D1 environments. They have not been applied to Cloudflare or any remote database. The local test wrapper initializes its ephemeral D1 database itself; applying those migrations through Wrangler's local D1 CLI validates SQL syntax only and does not substitute for running the Worker integration suite.

Password hashing uses Better Auth's maintained scrypt default. The isolated Workerd hash/verify check can run on this host; the full auth scenario is still blocked by local D1. Rate limiting counts eight credential checks per normalized email or username and hashed source IP in 15 minutes, then blocks for 30 minutes before password verification. While locked, repeat attempts return after one D1 read without writing the same limiter row again. A browser form permits only one submit in flight. The Worker uses Cloudflare’s maintained CF-Connecting-IP header for the library’s source limits. Sign-in routes use the durable D1 counter rather than an additional process-local library counter; other library limits remain enabled. Each HTTP attempt still reaches the Worker when deployed; throttling prevents repeated password work and unnecessary limiter writes, not Worker invocation billing.

## Deployment blockers

- No isolated Cloudflare staging Worker or D1 database has been provisioned. Deployment remains held by the owner's instruction.
- No production/staging issuer, API resource URL, public client ID, or redirect URI exists yet. These must be derived after a staging deployment and application-owner approval.
- Verification and password-reset messages have not been delivered externally. Cloudflare Email Sending would require separate setup and DNS records; it is not enabled. The Worker fails closed for email actions unless an approved delivery adapter is configured.
- End-to-end testing between this issuer and the 9to1 `WorkerAccountApiClient` remains outstanding. The shared 9to1 permission actor and its server-side app/action/ownership checks remain the authority; this Worker does not mint product permissions or entitlements.
- Production D1 migrations, backups, recovery policy, initial privileged account setup, and the verified reserved `croakyjake` AccountID/Business entitlement binding require review before any staging/prod use. No identities or entitlements are created here.

Production DNS, the WordPress site, Sites authoring/publishing, and account contents are untouched.
