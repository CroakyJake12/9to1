# Reconciling a claimed billing receipt

The recovery reader loads the claimed event under its current unexpired lease and checks the configured account/mode before a bounded provider read. A reconciled receipt records what the provider returned. Subsequent entitlement decisions require their own authorised owner and idempotent operation. Keep original failure and later recovery evidence for the same receipt. [Reconciliation implementation](src/provider-reconciliation.mjs)

The caller claims the exact event before invoking `readClaimedStripeState`. The reader checks the account at `GET /v1/account`, reads the specific event and referenced invoice or subscription, then rechecks the same lease and immutable receipt facts. The read leaves the inbox disposition with its owner.

The server must supply an authenticated `transport.fetch(Request)` capability,
the exact expected merchant, explicit Stripe API version, response-byte bound,
whole-read timeout and clock. No production transport, secret or merchant is
configured by this proposal. Requests use only `https://api.stripe.com`, GET,
`redirect: error` and an abort signal; the capability must respect these values.
The request never carries the receipt's claim token. At most three provider
requests execute, without automatic retries. Each response is bounded to the
explicit configured size (supported maximum 1 MiB); the explicit whole-read
timeout has a supported maximum of 120 seconds. Configuration is captured before
awaiting. Provider error bodies and raw exception text are discarded. Only immutable
module-created refusals have a private diagnostic brand; constructing the public
error class in an external transport, stream, database or clock cannot make its
private message a trusted output.

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
