# Read-only Stripe recovery proposal

`readClaimedStripeState` composes the existing durable receipt/lease API with
bounded provider reads. A caller first obtains ownership through `claimEvent`.
The reader loads that exact mode/event/token with an unexpired lease, checks the
configured merchant through `GET /v1/account`, retrieves the receipt's event,
and fetches the current invoice or subscription referenced by that event. It
rechecks the same lease and immutable receipt facts before returning. No reader
SQL mutates the inbox, and no receipt is completed or failed automatically.

The server must supply an authenticated `transport.fetch(Request)` capability,
the exact expected merchant, explicit Stripe API version, response-byte bound,
whole-read timeout and clock. No production transport, secret or merchant is
configured by this proposal. Requests use only `https://api.stripe.com`, GET,
`redirect: error` and an abort signal; the capability must respect these values.
The request never carries the receipt's claim token. At most three provider
requests execute, without automatic retries. Each response is bounded to the
explicit configured size (supported maximum 1 MiB); the explicit whole-read
timeout has a supported maximum of 120 seconds. Configuration is captured before
awaiting. Provider error bodies and raw exception text are discarded.

Identity, event type/creation, mode, optional connected-account identity and
current-object ID/type/mode must agree. Unsupported event families or provider
contexts refuse explicitly. Expired/reclaimed ownership, cancellation, missing
events, malformed/oversized responses and database failure preserve receipt
state. A 404 never becomes cancellation, nonpayment, entitlement removal or a
processed receipt. Provider events are retrievable only within Stripe's stated
30-day window; older recovery needs an approved retention/reconciliation route.

The returned historical event and freshly retrieved current object are provider
observations. They are not an atomic provider snapshot, a signed webhook envelope,
`VerifiedBillingSettlement` or `VerifiedBusinessBillingTransition`. API retrieval
cannot recreate the original signed bytes or be compared to their digest by
JSON reserialization. This module never invokes or bypasses the canonical
`OrganisationBillingService`/`SubscriptionPurchaseService` trusted verifiers,
calculates a price, binds a canonical account, grants resources or acknowledges
business effects. The actual authenticated transport, trusted canonical binding
and verifier, approved policy and canonical effect transport remain required.
No HTTP route, scheduler, deployment, new database or charge is added.

Protocol basis: Stripe's public OpenAPI, repository `stripe/openapi`, immutable
commit `2d691abcb499470ffd7536614b8901348697c8cf`, `openapi/spec3.json`, API version
`2026-09-30.endive`, whole-source SHA256
`7cff4cc46d0654101a36a3302f7544640f5f73e4f3cad6f264f0e23c19fa1776`.
The four GET contracts are `/v1/account`, `/v1/events/{id}`,
`/v1/invoices/{invoice}`, `/v1/subscriptions/{subscription_exposed_id}`. The API
version used in local tests is an explicit fictional test configuration, not a
selected production API version or merchant policy.

Run `node --test cloud/billing/test/provider-reconciliation.test.mjs` from the
repository root. Controls use production receipt/lease/reader code and actual
file-backed SQLite, with original HMAC-verifiable fictional webhook fixtures and
an injected provider-response transport. They establish local recovery/refusal
behavior, not genuine Stripe authentication/delivery, native D1, canonical
business effects, paid billing acceptance or full release acceptance.
