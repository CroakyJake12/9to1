# Stripe webhook receipt foundation

This Team C-owned Cloudflare Worker adapter is an incomplete billing foundation.
It does not activate subscriptions, grant resources, purchase anything or calculate
prices. `src/stripe-webhook.mjs` exports the actual Worker `fetch` entry point for
`POST /webhooks/stripe`. No deployment or commercial activation has occurred.

The operator must explicitly bind `BILLING_DB` (D1 API), provide a secret reference
for `STRIPE_WEBHOOK_SECRET`, and configure `STRIPE_MODE` (`test` or `live`),
`SIGNATURE_TOLERANCE_SECONDS` and `MAX_WEBHOOK_BYTES` as positive integers.
Each environment needs its own database and signing secret. None is supplied here.
Apply the versioned `schema/0001-stripe-inbox.sql` migration only in an authorised
isolated environment. D1/coordination selection remains OPEN under SH-13; the
injected D1-compatible interface is not evidence that a production choice passed.

Signatures verify the original bytes, with timestamp tolerance and rotating v1
signatures. Mode mismatch fails closed. An atomic unique receipt precedes HTTP
202; a repeated event cannot create a second receipt, and changed bytes under the
same ID produce a conflict. Body size is bounded while streaming. Database faults
return retryable 503 without exposing exception text, secrets or provider content.

Only event ID/type/creation time, mode, payload digest and processing metadata
persist: current Stripe state must be fetched by verified ID. Sensitive invoice
payloads are not copied into this receipt store. Retention and provider event
availability require an approved policy before launch. No receipt-reading API is
exposed to ordinary clients.

`inbox-processing.mjs` has atomic exclusive lease claims, attempt counts and
conditional completion/failure. Expired ownership cannot complete, and every
claim explicitly requires reconciliation. A crashed/failed consumer must fetch
current provider state and check canonical business-effect identities before
retrying. **No consumer or scheduler exists yet.** This inbox alone does not make
entitlement effects idempotent: a future canonical consumer must durably commit
its effect and idempotency marker together and reconcile uncertain outcomes.
Receipt/claim status never represents payment, entitlement or provisioning state.

## Validation and limitations

Run from the repository root:

```sh
node --test cloud/billing/test/*.test.mjs
```

Node 24 provides the real SQLite database used by tests and Web Crypto. The small
D1 API adapter changes database-call syntax only; production receipt and claim
code runs against real isolated durable SQLite files. Test signatures use an
isolated local test secret. This validates local receipt behavior, not Stripe
provider delivery, deployed D1 behavior, Cloudflare runtime concurrency, real
reconciliation, checkout, subscription lifecycle or resource accounting.

Requirement map (canonical revision recorded in Team C evidence):

| Requirement | Local evidence | Release state |
| --- | --- | --- |
| SH-09 raw-body verified durable events | C3-WH-01/02/03/06/08 | IMPLEMENTED-UNVERIFIED in deployed environment |
| SH-02 test/live distinction | C3-WH-04/10 | IMPLEMENTED-UNVERIFIED with real provider configuration |
| SH-09 receipt replay/conflict handling | C3-WH-01/05 | IMPLEMENTED-UNVERIFIED; business effects MISSING |
| SH-09 retry/recovery processing authority | C3-WH-09/10 | IMPLEMENTED-UNVERIFIED; consumer/scheduler MISSING |
| SH-09 unordered subscription reconciliation | C3-WH-07 proves receipt cannot apply changes | MISSING current-object provider reconciliation |
| SH-09 authenticated quote, catalogue, checkout, activation, invoices, lifecycle | No implementation | MISSING; commercial policy additionally BLOCKED by SH-13 |
| Subscription/Admin reserve/run/settle, hard ceilings, allocations, rollover | No ledger implementation | MISSING; unresolved periods/rollover/currency policies BLOCKED |
| SH-12 real deployed billing lifecycle/replay | No credentials or deployment | BLOCKED, no test outcome counted PASS |

No prices, currencies, rollover, grace periods, allocations, canonical identities
or production bindings have been invented. Shared product contracts are unchanged.

`listReconciliationCandidates(db, mode, now, limit)` provides read-only, mode-scoped discovery of pending, reconciliation-required and expired-claim receipts. The explicit limit is 1–100, and ordering is accepted timestamp then unique event ID. A candidate can become unavailable immediately; only `claimEvent` acquires ownership. This API does not fetch current provider objects, run a scheduler, apply canonical effects or acknowledge completion. Canonical provider bindings, trusted verifiers/policies and an approved effect transport remain required and unconfigured.
