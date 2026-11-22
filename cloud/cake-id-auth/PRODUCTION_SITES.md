# Production website client registrations

Authorised administrators use the private production-sites page to review and register selected website clients. Read the state endpoint for current registrations and pair the selected origin and redirect with the requested site. The Worker independently checks administrator access for the operation; maintain the Cloudflare Access policy on both administrative routes. [Registration owner](src/production-sites.ts)

This change adds the private `/account/production-sites` administrator page and
its read-only `/account/production-sites/state` endpoint. Keep both behind the
existing Cloudflare Access administrator policy. The Worker independently checks
the current session and canonical D1 administrator role.

The page has separate explicit actions for these public OAuth clients:

| Name | Exact callback |
| --- | --- |
| CAKE website | `https://cakemods.com/oauth/callback` |
| 9to1 website | `https://9to1.uk/oauth/callback` |

Each action reads current owner metadata, uses the maintained same-origin
`POST /api/auth/oauth2/create-client`, then verifies the stored metadata. Existing
matching clients are reused. Conflicts, missing administrator authority and
unknown outcomes stop the action. A lost reply is reconciled without replaying
the write. Web Locks coordinate tabs in the same browser; they do not provide
server-wide exactly-once registration across independent browsers. Perform setup
from one administrator browser at a time. Reload after any inconclusive result.

The resulting public client IDs must be configured in the corresponding website
Worker. They are not credentials. No existing client, issuer, resource, account,
database binding or permission is migrated by this source change. No direct SQL
registration or test-only administrator bootstrap is used.

## Public authentication boundary

Preserve the existing issuer and API resource while onboarding these websites.
New websites can use a server-side OAuth client with PKCE and server-side token
storage. Direct browser API use additionally requires exact origin grants.

For a public release, review narrow Cloudflare Access path exceptions for the
user flow against the deployed pinned Better Auth router: `/sign-in`, `/sign-up`,
`/forgot-password`, `/reset-password`, `/consent`, `/assets/auth-ui.js`, and the
exact provider routes for sign-in, sign-up, verification, password reset,
authorize, consent, public client metadata, token exchange and sign-out.
Preserve the library's session, origin and CSRF checks. Do not make a blanket
`/api/auth/*` exception. `/api/auth/oauth2/create-client`, other client/resource
administration and `/account/production-sites*` remain administrator-only.

Real email verification/reset delivery, production OAuth callbacks, account
profile/session/theme persistence and sign-out still require hosted acceptance.
Local tests do not establish that those deployment gates passed.

The public signup and recovery entrypoints accept only `app=cake` or `app=nine`.
Verification and reset links keep this selector on the issuer. Successful
standalone sign-in then starts the selected website's `/auth/sign-in` PKCE flow.
No arbitrary return URL is accepted; the existing default administrator flow
still returns to `/account`. Password fields preserve their exact input,
including leading or trailing spaces; other profile fields remain trimmed.
