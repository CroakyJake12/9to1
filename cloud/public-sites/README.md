# CakeMods and 9to1 public websites

Fresh shared Cloudflare Worker project for cakemods.com and9to1.uk. Source is independent of WordPress. Public content is rendered on the server, with a small progressively enhanced community search and account client.

## Build and checks

Use Node24 and npm. On Windows use npm.cmd. `npm ci`, `npm run build`, `npm test`. `npm run dev` serves CakeMods at127.0.0.1:4173 and9to1 at4174. Local preview does not emulate signed-in sessions; authentication acceptance uses the hosted Worker and maintained 9to1 Account.

The build emits dist/worker.mjs. All CSS/client scripts are bundled with the Worker and addressed with a content-hash query. No WordPress/CDN assets or theme are needed.

## Identity

The existing Better Auth issuer and D1 are preserved. Production clients require exact callbacks https://cakemods.com/oauth/callback and https://9to1.uk/oauth/callback. Register each via the maintained administrator page `/account/production-sites`; never insert client rows directly. Configure the resulting nonsecret CAKE_CLIENT_ID and NINE_CLIENT_ID bindings.

SESSION_KEY is a Cloudflare secret, generated once in the provider; never put its value in source. OAuth state/verifier cookies are encrypted and bound to the site origin. Website sessions use random256-bit host-only HttpOnly/Secure cookies. Tokens stay in per-session SQLite Durable Objects. The DO serializes refresh rotation, persists an in-flight marker, expires unknown outcomes without replay and enforces a7-day session lifetime. Account API proxy routes are explicitly allowlisted and mutation requests require the exact origin.

The sibling `cloud/cake-id-auth` package contains the maintained production-client management and public app-return changes. It preserves existing accounts, scopes, clients and server-side role checks. No WordPress account import is performed.

## Deployment and rollback

Deployment is via the connected Cloudflare API, not credentials stored locally. Use the configured Cloudflare account. Worker cake-public-websites uses existing identity Worker cake-id-release-validation through a service binding restricted by application route handling. Preserve all bindings/secrets on updates.

Brochure-domain cutover has been completed separately from account admission. Public pages show the account-testing hold. Before enabling invited access, complete hosted signup/verification/reset, both OAuth clients, profile/theme persistence, sessions/revocation/signout, and administrator-denial tests. Keep public registration closed until the user's entity-registration condition is satisfied. Existing CakeMods origin A records192.0.78.163 and192.0.78.215 are the rollback destination. Existing identity version0b60de0e-d096-4653-9309-c444a75bdf35 is retained for code rollback. Restoring a Worker version does not restore D1 contents; additive migrations0003–0006 are applied without replacing user IDs or importing WordPress accounts. Password-reset fencing uses user.credentialGeneration; do not remove that column when rolling back code.

Use evidence/access-before.json to restore the exact previous Access application configuration if a scoped public-auth change fails. Do not expose administrator or dynamic client-registration paths. No auth codes, cookies, tokens, passwords or email verification links should be logged.

## Current state

See the root chat and deployment evidence. Both production domains serve the rebuilt brochure. Approved exact website clients are connected, Google sign-in has passed with the existing owner, and account admission remains held. ACCOUNTS_AVAILABLE=private-testing keeps public entry UI closed while issuer Access enforces owner-only testing. ACCOUNT_REGISTRATION_HOLD=true must remain set when published policy versions are enabled for the user-approved existing-account-only test. Broader account, payment and AI readiness has not been established.
