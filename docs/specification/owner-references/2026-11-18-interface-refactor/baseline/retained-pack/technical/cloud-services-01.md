# Cloud services, infrastructure and billing contracts — Baseline, authority and deployment permissions; Production architecture and ownership (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2171 · v2 lines 12755–12775

Pass 3: Sol Happy
SH-01 — Purpose and required outcome
Sol Happy is the “systems online” development pass.
The pass brings the 9to1 ecosystem onto its real production infrastructure: public websites, browser applications, CAKE ID authentication, subscription billing, hosted files, hosted websites, cloud-backed application services and operational management.
The required outcome is a connected service that a user can access from a fresh device without a development environment. A user MUST be able to visit the website, create or access their account, open an eligible application in their browser, create and save real work, continue that work in a desktop application, purchase an available subscription and use the resources that subscription provides.
Public production services MUST NOT depend on a developer’s PC remaining online, a temporary development tunnel or a manually started local server.
Sol Happy includes completing the production integration of existing features. It MUST NOT become a separate, reduced product that merely resembles the desktop ecosystem.
All existing mandatory product, donor-parity, accessibility, privacy, security, API and interoperability requirements remain active. Pass 3 does not retrospectively excuse missing requirements from earlier passes. The existing definition of implementation-complete continues to require validated behaviour rather than source presence, successful builds or rendered interfaces.
SH-02 — Baseline, authority and deployment permissions
Before implementation begins, the coordinator MUST identify the authoritative repository, current accepted baseline, active workstreams, existing deployments and relevant infrastructure accounts. Existing work, credentials, production configuration and user data MUST be preserved.
The pass MUST maintain a requirement ledger using VERIFIED, IMPLEMENTED-UNVERIFIED, MISSING and BLOCKED, with evidence tied to the exact tested revision, environment and platform. Existing acceptance evidence MUST be re-evaluated where changes affect its validity.
This specification remains the product authority. Infrastructure notes, repository documentation and worker reports MUST NOT introduce competing requirements or silently narrow the pass.
Development, staging and production MUST be distinguishable. Test payments, test accounts, preview deployments and development storage MUST NOT be presented as live production services.
Approval to develop this pass is not blanket approval to spend money or change live production routing. Enabling billable services, increasing spending limits, making live test charges, changing production DNS, retiring WordPress or deleting production resources requires explicit authorisation covering the relevant action.
Independent implementation and testing MUST continue while a particular external approval is pending. A pending approval remains a visible deployment gate, not an excuse to stop unrelated work.
Sol Happy’s execution model and deadline MUST be specified separately. Terra-form’s deadline and temporary Android/Go fallback MUST NOT automatically become Pass 3 rules.
SH-03 — Production architecture and ownership
Cloudflare is the intended production hosting platform for the public websites, web applications and hosted sites, including edge/API delivery. Backblaze B2 is the intended primary object-storage platform for hosted files, file bytes, assets and durable deployment artifacts. Stripe provides subscription payment processing and associated billing services.
These providers MUST NOT replace 9to1’s product ownership boundaries.
CAKE ID and the shared account services own identity, sessions and entitlement evaluation. Files owns canonical file identity, storage operations, sharing, versions and synchronisation. Sites owns website authoring and deployment orchestration. Stripe supplies verified payment and subscription facts; it does not become the file-permission or application-authorisation system.
The proposed production service mapping is:

### V2-B2172 · v2 lines 12778–12789

Responsibility
	Proposed implementation
	Public website and browser-application delivery
	Cloudflare-hosted, revision-pinned frontend deployments
	Trusted API and integration entry points
	Cloudflare Workers with explicitly scoped service bindings
	File bytes, assets and deployment artifacts
	Backblaze B2 via its S3-compatible API, separated by purpose, environment, bucket and access policy
	Structured metadata and coordinated state
	D1 and/or Durable Objects, selected against the required transaction and consistency contracts
	Deferred processing and delivery
	Queues and the existing job orchestration contracts
