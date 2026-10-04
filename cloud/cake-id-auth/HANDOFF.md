# CAKE ID Migration Handoff

Last updated: 2026-10-02

## Objective and ownership

Build one shared CAKE ID issuer for CAKE and 9to1 on Cloudflare Workers and D1, using the authoritative 9to1 Development Specification and Astra's current account/API contracts. Existing WordPress users and data are explicitly excluded from transfer. The package in this directory is the isolated Worker/D1 implementation scope; do not edit or push to Astra's active branch as a shortcut.

## Work preserved here

- Better Auth 1.7.7 provides the OAuth/OIDC provider, authorization-code flow, PKCE, password hashing, sessions, email verification and password reset.
- D1 migrations include Better Auth's OAuth/account schema and reserved-name/login-rate-limit tables. The account's canonical identity is a UUID; plan and product permissions are not placed in OAuth scopes or tokens.
- Login attempts are rate-limited by normalized identifier and source IP: eight checks per 15 minutes, then a 30-minute lock. Repeated requests during the lock return after one D1 read and do not rewrite the same limiter row. Requests still reach the Worker and count as requests when deployed. The browser coalesces duplicate submissions while one is pending.
- The resource routes match Astra's current `WorkerAccountApiClient`: `/api/account/current`, `/api/account/profile`, `/api/account/sessions`, `/api/account/signout`, `/api/account/sessions/{id}`, and `/api/account/revoke-other-sessions`. They enforce route scopes, token resource/issuer, UUID subject, active D1 session, owner scoping, and profile revision compare-and-swap.
- Verification and reset email callbacks capture messages only in the local test outbox. Production fails closed without an approved delivery adapter.
- The package has loopback-only Wrangler configs, generated migrations, and a synthetic local D1 integration harness. There are no deploy settings, Cloudflare credentials, test credentials, or real clients in source.

## Verified and unverified

- `npm run typecheck`, `npm run schema:validate`, and `npm run build:ui` passed before this handoff. Re-run typecheck after the last API-contract edits.
- The full local Wrangler+D1 integration does not run on this Windows ARM64 host: x64-emulated Workerd fails at D1 before reaching auth flows. The suite exits without creating `.dev.vars` or local test state. Run it on a supported native Workerd host before calling it verified.
- A prior isolated Workerd password-hash test passed functionally, but ran under x64 emulation and took about 34 minutes overall; its hash and verify timings were not qualified. Repeat on native Workerd to measure intended-runtime CPU before deciding production limits.
- No Cloudflare staging Worker or D1 database has been provisioned. There is no deployed issuer/discovery URL, API base URL, public client registration, allowed redirect URI, or deployed test account.
- No verification/reset email has been delivered to an external inbox. Email delivery is not configured.
- The 9to1 repository contains its shared OIDC token client and `WorkerAccountApiClient` contract on Astra's active draft PR, but end-to-end issuer/client/API integration remains unverified.
- The existing Cloudflare `cake` Worker is present in Production, but the dashboard overview showed no public URL or resource bindings; no evidence identifies it as the CAKE ID issuer. A $10 default billing alert is notification-only, not a usage cap. No account settings were changed.
- Production DNS and the live WordPress site remain unchanged. The static Cloudflare preview is not the required 9to1 Sites publishing pipeline.

## Resume checklist

1. Pull the backup branch and install dependencies with `npm ci`.
2. Run typecheck, schema validation, UI build, the full local D1 integration, and the instrumented password runtime test on a supported native Workerd host. Record local test results separately from any deployed integration.
3. Review `resource-api.ts` and `integration.mjs` against the latest Astra client contract, then wire a verified `WorkerAccountApiClient` contract test to the Worker routes.
4. Obtain renewed explicit authorization before any Cloudflare deployment or D1 provisioning. Keep production DNS and WordPress unchanged. Do not enable email delivery, add DNS records, or incur new service charges without the required approval.
5. After authorized isolated staging, create synthetic accounts there and record the actual discovery/issuer URL, API resource URL, public client ID, and exact redirects. Test real mail delivery explicitly. Never infer these values from loopback settings.
6. Finish the 9to1 Sites authoring and publishing pipeline integration; the standalone static preview does not complete the website migration.

## GitHub context

The latest observed Astra integration PR is an open draft targeting `main`. Preserve its active branch and its in-progress files. This backup should be a separate branch containing only `cloud/cake-id-auth/` so the source can be resumed without depending on this laptop.
