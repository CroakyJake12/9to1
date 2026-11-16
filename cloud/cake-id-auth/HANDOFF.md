# CAKE ID Migration Handoff

Historical baseline last updated: 2026-10-02; see current deployment and source-custody update below.

## Current deployment and source-custody update — 16 November 2026

- Cloudflare read-only metadata confirms active owner-only version f7ed455e-709a-4e1e-98cb-5d68c6f3fe61 at 100 percent traffic (deployment 95aedb62-7dff-4899-8097-635ee400d720, created 2026-11-15 17:38 UTC). Its fetched worker.mjs SHA-256 is 7123c42a9f2b13f38e17a72404df1b9d9d859d1e1607fa4123d73143338a5a07, matching the retained release record.
- Public registration remains held; sign-in was tested with the existing owner account only. This deployment is not general public signup or a grant of app/action permissions.
- Local checks: identity typecheck/UI build passed; 163 test files yielded 161 passed and 2 Linux fixture failures; a separate local Workerd/D1 integration passed 248 assertions. These are distinct evidence levels, not a fully passing suite or a current .NET integration result.
- The outside package notes say production migrations 0003–0006 were applied after recovery bookmarks. This handoff did not read live D1 rows. Preserve recovery evidence and verify the exact migration receipt before another schema action.
- Source-to-bundle byte identity is unresolved: current outside src/pages.ts hash differs from its final-branding test hash, and the pinned default and minified local dry-runs produced different module hashes from the live bundle. Preserve the live bundle hash and source tree as separate identities; do not claim exact correspondence.
- Main-branch source reconciliation remains review-only. No deployment, migration, login, driver change, or trained-model operation is part of source consolidation.

The remainder below preserves the 2 October 2026 handoff baseline. Statements below that claim there was no deployment or D1 runtime belong to that historical date and are superseded by this current update.
## Historical objective and ownership (2 October 2026)

Build one shared CAKE ID issuer for CAKE and 9to1 on Cloudflare Workers and D1, using the authoritative 9to1 Development Specification and Astra's current account/API contracts. Existing WordPress users and data are explicitly excluded from transfer. The package in this directory is the isolated Worker/D1 implementation scope; do not edit or push to Astra's active branch as a shortcut.

## Historical work preserved here

- Better Auth 1.7.7 provides the OAuth/OIDC provider, authorization-code flow, PKCE, password hashing, sessions, email verification and password reset.
- D1 migrations include Better Auth's OAuth/account schema and reserved-name/login-rate-limit tables. The account's canonical identity is a UUID; plan and product permissions are not placed in OAuth scopes or tokens.
- Login attempts are rate-limited by normalized identifier and source IP: eight checks per 15 minutes, then a 30-minute lock. Repeated requests during the lock return after one D1 read and do not rewrite the same limiter row. Requests still reach the Worker and count as requests when deployed. The browser coalesces duplicate submissions while one is pending.
- The resource routes match Astra's current `WorkerAccountApiClient`: `/api/account/current`, `/api/account/profile`, `/api/account/sessions`, `/api/account/signout`, `/api/account/sessions/{id}`, and `/api/account/revoke-other-sessions`. They enforce route scopes, token resource/issuer, UUID subject, active D1 session, owner scoping, and profile revision compare-and-swap.
- Verification and reset email callbacks capture messages only in the local test outbox. Production fails closed without an approved delivery adapter.
- The package has loopback-only Wrangler configs, generated migrations, and a synthetic local D1 integration harness. There are no deploy settings, Cloudflare credentials, test credentials, or real clients in source.

## Historical verification status (2 October 2026)

- `npm run typecheck`, `npm run schema:validate`, and `npm run build:ui` passed before this handoff. Re-run typecheck after the last API-contract edits.
- The full local Wrangler+D1 integration does not run on this Windows ARM64 host: x64-emulated Workerd fails at D1 before reaching auth flows. The suite exits without creating `.dev.vars` or local test state. Run it on a supported native Workerd host before calling it verified.
- A prior isolated Workerd password-hash test passed functionally, but ran under x64 emulation and took about 34 minutes overall; its hash and verify timings were not qualified. Repeat on native Workerd to measure intended-runtime CPU before deciding production limits.
- No Cloudflare staging Worker or D1 database has been provisioned. There is no deployed issuer/discovery URL, API base URL, public client registration, allowed redirect URI, or deployed test account.
- No verification/reset email has been delivered to an external inbox. Email delivery is not configured.
- The 9to1 repository contains its shared OIDC token client and `WorkerAccountApiClient` contract on Astra's active draft PR, but end-to-end issuer/client/API integration remains unverified.
- The existing Cloudflare `cake` Worker is present in Production, but the dashboard overview showed no public URL or resource bindings; no evidence identifies it as the CAKE ID issuer. A $10 default billing alert is notification-only, not a usage cap. No account settings were changed.
- Production DNS and the live WordPress site remain unchanged. The static Cloudflare preview is not the required 9to1 Sites publishing pipeline.

## Historical resume checklist (superseded by the current update above)

1. Pull the backup branch and install dependencies with `npm ci`.
2. Run typecheck, schema validation, UI build, the full local D1 integration, and the instrumented password runtime test on a supported native Workerd host. Record local test results separately from any deployed integration.
3. Review `resource-api.ts` and `integration.mjs` against the latest Astra client contract, then wire a verified `WorkerAccountApiClient` contract test to the Worker routes.
4. Obtain renewed explicit authorization before any Cloudflare deployment or D1 provisioning. Keep production DNS and WordPress unchanged. Do not enable email delivery, add DNS records, or incur new service charges without the required approval.
5. After authorized isolated staging, create synthetic accounts there and record the actual discovery/issuer URL, API resource URL, public client ID, and exact redirects. Test real mail delivery explicitly. Never infer these values from loopback settings.
6. Finish the 9to1 Sites authoring and publishing pipeline integration; the standalone static preview does not complete the website migration.

## Historical GitHub context

The latest observed Astra integration PR is an open draft targeting `main`. Preserve its active branch and its in-progress files. This backup should be a separate branch containing only `cloud/cake-id-auth/` so the source can be resumed without depending on this laptop.
