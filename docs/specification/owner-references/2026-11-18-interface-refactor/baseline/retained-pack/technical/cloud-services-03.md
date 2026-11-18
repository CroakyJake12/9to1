# Cloud services, infrastructure and billing contracts — Fresh-device onboarding; Web/desktop continuity (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2174 · v2 lines 12882–12903

Journey
	Required evidence
	Fresh-device onboarding
	A new browser session can register or sign in, reach eligible apps and save persistent work without local Home or development tooling.
	Web/desktop continuity
	An artifact created on one surface opens and edits on the other with the same canonical identity, content, permissions and history.
	Application parity
	Every in-scope app passes its applicable browser workflows, accessibility checks and recorded capability matrix.
	Storage integrity
	Interrupted and concurrent uploads resume or fail safely; quotas remain correct; no corrupt file is reported as complete.
	Subscription lifecycle
	Checkout, verified activation, configuration changes, renewal failure and cancellation produce the correct account and resource states.
	Billing replay
	Duplicate, delayed and reordered events cannot duplicate charges, allocations or entitlement transitions.
	Publishing
	A real Sites project deploys, serves its required dynamic behaviour, verifies its domain and rolls back without losing source.
	Isolation
	Cross-account access, revoked sharing and malicious customer content cannot expose another account’s files, sessions or administrative services.
	Recovery
	A tested restore and redeployment recover the service from preserved canonical state.
	Independent operation
	Public websites and cloud-hosted services remain operational without the developer’s PC and survive representative customer-workload failures.

### V2-B2175 · v2 lines 12906–12919

Payment failure scenarios MUST first be exercised using appropriate test facilities. Live-mode validation requires an authorised merchant configuration and explicit approval for any actual charge. Test-mode success alone MUST NOT be reported as proof of live billing.
Visual and interaction testing MUST cover populated workloads, narrow layouts, keyboard operation, screen-reader semantics, 200% scaling, themes, reduced motion and failure/recovery states.
SH-13 — Decisions required before affected functionality goes live
The following items remain explicit launch decisions rather than implementation guesses.
Commercial catalogue and lifecycle policy. Finalise all offered resource allocations, usage periods, rollover rules, configuration prices, downgrade behaviour, grace periods and retention consequences. The existing specification leaves material allocations and billing policies unresolved. Undefined configurations MUST NOT be offered for purchase.
Currency and entitlement-price calculation. Define how tax, discounts, promotional credits and non-monthly billing affect EntitlementPriceMonthly. Resolve the existing GBP resource pricing and USD Business add-ons through an approved billing policy. Stripe requires prices within a single multi-product subscription to use the same currency; silent relabelling or conversion is not acceptable. (docs.stripe.com)
Production service configuration. Confirm the account-authentication implementation, transactional delivery provider, database/coordination boundaries, supported runtime capabilities and environment ownership. Existing compatible infrastructure MUST be inspected before replacement.
Customer-site browser isolation. Approve the isolation mechanism for active content under the specified path-based public addresses. Any change to the canonical URL model requires an explicit specification amendment.
Operational and launch parameters. Confirm supported browser versions, measurable performance/capacity targets, recovery objectives, infrastructure budgets, merchant readiness and production cutover authorisation.
These decisions MUST block only their affected release paths. They MUST NOT be silently resolved by a worker, hidden behind defaults or used to stop independent implementation.
Release exit gate
Sol Happy is complete only when the required online services are implemented, integrated, deployed to their authorised environments and validated against the applicable specification.
Implemented, tested, deployed and commercially enabled MUST remain distinguishable states.
The final evidence ledger MUST identify the tested release, deployed service versions, functioning user journeys and every remaining blocker or approved scoped exception. No service may be described as online merely because its interface exists, a provider account has been connected or a test request succeeded.
