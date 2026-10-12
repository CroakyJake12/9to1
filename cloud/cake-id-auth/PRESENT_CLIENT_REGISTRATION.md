# Connect the protected Present preview

The CAKE ID `/account` page has a separate **Connect Present preview** administrator action for the existing protected host. This source path prepares a public OAuth client; it does not deploy an issuer, register a hosted client automatically, or authenticate a user to Present.

Sign in through the existing CAKE ID page as an authorized administrator. The page shows the exact callback before registration:

`https://present-4581358882177bc7.9to1.uk/oauth/callback`

The button submits only after a fresh `/account/present-app` observation confirms the canonical administrator role and that this administrator has no existing matching client. It uses the maintained `/api/auth/oauth2/create-client` endpoint with the same browser session and CSRF checks. The definition is named `9to1 Present preview`, uses public authentication method `none`, web application type, authorization-code and refresh-token grants, response type `code`, and the canonical `AUTHORIZATION_SCOPES`. The maintained provider enforces public-client PKCE and consent, and associates the existing configured API resource. No client secret is generated or placed in browser configuration by this action.

A successful POST alone is insufficient. The action reads the owner metadata again and displays a public client ID only after exact callback, grants, scopes, resource, consent, PKCE and public-client attributes match. Conflicting metadata produces a review state without another registration. A lost response is reconciled with a read, never an automatic repeated POST. The button stays disabled until the account page is reloaded, and a same-origin browser lock suppresses overlapping registration attempts from multiple tabs.

The existing **Connect staging apps** action, staging redirect and Windows native redirect are unchanged and independent. Connecting Present does not create, edit or disable either of those clients. Metadata reads do not provision clients. Registration is never performed through a direct D1 insert or a borrowed session.

The actual returned public client ID is needed by the host-owned `window.nineToOneBrowserConfiguration.account` before the browser app starts, alongside the discovered issuer, existing resource and exact callback. Keep the current Access protection and callback path. Real hosted sign-in, consent and account-theme behavior remain a separate acceptance gate.

Validation commands:

- `node --test tests/present-registration.test.mjs`
- `npm run typecheck`
- `npm run build:ui`

The focused tests exercise read-only metadata against an isolated in-memory schema and the real browser action with controlled responses, including refusal and unknown outcomes. They do not claim hosted administrator authority. A local Workerd/Chromium journey must use only the existing synthetic fixture and its private credentials; never reuse those credentials or its test-only bootstrap in a deployment.
