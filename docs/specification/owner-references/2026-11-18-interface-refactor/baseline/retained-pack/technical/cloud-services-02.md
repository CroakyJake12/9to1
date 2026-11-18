# Cloud services, infrastructure and billing contracts — WordPress-to-Cloudflare website migration; CAKE ID and account services online (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2173 · v2 lines 12792–12879

Cloudflare and Backblaze provide distinct hosting, storage, coordination and task-processing building blocks. The exact combination MUST be validated against 9to1’s runtime, consistency, capacity and recovery requirements; provider boundaries MUST remain explicit rather than being collapsed for convenience. Eventually consistent KV MUST NOT be the sole authority for operations requiring immediate, transactional enforcement.
Public first-party websites MUST be independently deployable from customer projects and customer build workloads. A failed customer deployment or exhausted customer allocation MUST NOT take the main website offline.
Every environment MUST have explicit resource bindings, secret references, deployment identities and rollback procedures. Private credentials MUST NOT enter browser bundles, public assets, repository history or ordinary diagnostic output.
SH-04 — WordPress-to-Cloudflare website migration
The pass MUST migrate the production website away from its WordPress runtime dependency.
The intended architecture for cakemods.com and 9to1.com is:
Editable Sites project → canonical Files/Stack source → validated build → Cloudflare production deployment.
Sites remains the authoring layer; the public websites are served from their separate production deployments. Published output MUST NOT become the only surviving copy of the website. This follows the existing Sites source and deployment authority contract.
Before migration, the implementer MUST inventory the actual website’s pages, routes, assets, redirects, navigation, forms, account links, downloads and backend dependencies. Every retained capability MUST have a working destination in the replacement.
The migration MUST preserve required content and useful behaviour. It MUST NOT substitute screenshots, flattened pages or an unrelated redesign for the existing site and editor functionality. The cakemods site-plugin remains the donor reference for applicable authoring behaviour.
Moving DNS or placing Cloudflare in front of the existing WordPress server does not satisfy the migration requirement. The replacement MUST operate without WordPress supplying its ordinary public runtime, account backend or content-management operations.
Domain registration MUST NOT be transferred again merely to complete hosting migration. Existing DNS configuration MUST be inspected, and unrelated records—including mail and verification records—MUST be preserved.
The replacement MUST first be validated in a non-production environment. Cutover requires a recoverable backup, a verified destination, a plan for any writes during migration and an authorised rollback path. WordPress MUST NOT be deleted or cancelled simply because the new homepage loads.
There is no requirement to invent a legacy consumer-data migration programme for data that does not exist. Any real accounts, content, submissions or other records discovered during inspection MUST nevertheless be preserved or deliberately migrated.
SH-05 — CAKE ID and account services online
Desktop and web MUST use the same CAKE ID, account identity, organisation context and entitlement services. Ordinary users MUST NOT require WordPress accounts.
Authentication MUST preserve the existing secure public-client contract, including the specified browser-based authorisation flow or an explicitly equivalent secure implementation. Clients MUST NOT contain provider credentials, billing secrets or entitlement-signing secrets.
The supported registration, sign-in, sign-out, recovery, profile, session-inspection and session-revocation journeys MUST work against the production backend. Any journey that sends verification or recovery email MUST use a configured delivery service and expose delivery failures honestly.
Any transition from WordPress-linked authentication MUST use verified account mappings and recovery rules. Matching a mutable username MUST NOT transfer account ownership, administrative authority or paid entitlements.
Jacob’s reserved @CroakyJake handle and account-bound free Business feature grant MUST survive the transition. That grant MUST NOT become an exemption from resource accounting, security permissions or third-party spending approval.
Browser applications MUST use authenticated equivalents of Home’s shared services without requiring a local Home installation. Permission approval, trust, revocation and audit semantics MUST remain consistent with native applications.
Personal and organisation contexts MUST remain separate throughout navigation, storage, billing, search and AI access. Switching accounts or organisations MUST NOT expose cached private information from the previous context.
SH-06 — Full browser versions of the applications
Every non-exempt Workspace and AI application MUST have a functional browser version.
The target is the same application on the web, not a companion dashboard, viewer-only edition or simplified alternative. At equivalent viewport sizes, the browser version MUST preserve the desktop application’s information architecture, tools, editor structure, commands, menus, selection behaviour, settings and supported workflows.
Browser-specific adaptations are permitted for actual capability differences and responsive layouts. They MUST NOT justify avoidable feature loss.
Web implementations MUST preserve CUI’s public semantics, shared component contracts, canonical state identities and supported actions. A browser-specific runtime is permitted; an independently redesigned, incompatible UI framework is not the intended outcome.
Each app MUST support its applicable create, open, edit, save, reopen, history, import/export, sharing and recovery operations. The shared contextual AI bar MUST operate on the real semantic artifact through authorised owning-app APIs.
The web inventory MUST include the active Terra-form additions: Link, Prototype, Paths, Replay, Files’ organisation types and Variants in their owning applications. These retain their existing ownership and terminology; they MUST NOT be reorganised into different products for the web implementation.
Each application MUST have a capability matrix identifying supported browser operations, equivalent adaptations and exact remaining restrictions. A restriction MUST identify the missing capability, affected operation, user-visible behaviour and validation evidence.
A desktop-only feature is not sufficient reason to omit an otherwise useful web app. Mini Computer retains its existing web exemption unless the required hosted-compute capability is explicitly provided. Other missing native capabilities MUST NOT be represented as available merely because a browser interface exists.
Direct links, browser refresh, back/forward navigation and account restoration MUST preserve the intended application and artifact context. Unsupported browsers MUST receive a meaningful compatibility state rather than a broken interface.
SH-07 — Files and 9to1 Drive production hosting
9to1 Drive MUST become a real hosted storage service used by the web and desktop applications.
A file uploaded in the browser and opened on desktop MUST retain the same FileID and durable revision history. Local materialisation is a managed replica, not an unrelated imported copy. Files remains responsible for storage and filesystem operations; the owning application remains responsible for semantic document editing and collaboration.
Private file storage MUST remain private. Protected downloads and shares MUST use access-controlled routes that enforce current account, organisation and object permissions. Possession of an object key MUST NOT grant access.
Backblaze B2 is the canonical object-storage backend for 9to1 Drive file bytes and other private hosted objects. Buckets containing private user or deployment data MUST remain private. Direct public bucket/object URLs MUST NOT substitute for protected sharing. Production downloads MUST be mediated by access-controlled 9to1/Cloudflare routes or by narrowly scoped, short-lived signed URLs issued only after the owning service has authorised the request. B2 application keys MUST be least-privilege, scoped to the required bucket/prefix/capabilities, and MUST NOT be embedded in browser or desktop clients.
Transfers MUST have durable identities, resumable progress, cancellation, integrity validation and recoverable failure states. Upload completion MUST not be reported until both content and the authoritative metadata commit have succeeded. Interrupted operations MUST be reconciled without invisible orphan uploads or duplicated quota charges.
Storage enforcement MUST account for committed usage and in-flight reservations. Concurrent uploads MUST NOT overspend the same allocation.
Normal connected synchronisation MUST be push-based and incremental, with durable cursors and replay-safe reconciliation. Polling and full rescans remain recovery mechanisms rather than the ordinary propagation path.
Sharing, search, previews, AI retrieval, version restoration and Trash MUST operate on the same canonical objects and permissions. Restoring an earlier revision MUST preserve subsequent history. Ordinary deletion remains recoverable; permanent purge remains a distinct protected operation.
SH-08 — Sites publishing and customer hosting
Sites MUST publish real websites to Cloudflare through its existing deployment model.
A published deployment MUST identify its SiteID, source revision, build/configuration revision, environment, provider result and domain bindings. Preview and production MUST remain distinct.
Publishing MUST include actual asset delivery, route handling and any supported server-side functionality. Successful static deployment MUST NOT be used to claim that required forms, CMS, authentication or dynamic application behaviour also works.
Builds and customer-supplied code MUST execute within appropriately isolated, authorised environments. Customer code MUST NOT inherit the credentials or privileges of CAKE’s account, billing or administrative services.
A failed build or deployment MUST preserve the previous working deployment. Rollback MUST restore a known deployment without deleting newer source history. These behaviours extend the existing deployment and publishing safeguards.
First-party addresses, slug validation, reserved names, misleading-name review, custom-domain ownership verification and DNS/TLS readiness MUST implement the existing Sites contract. The default sites.9to1.uk/<site-slug> namespace MUST NOT silently become a different address scheme.
Browser isolation requires an explicit design decision. Different paths on the same hostname are not separate browser origins. The implementation MUST NOT assume that /site-a and /site-b isolate customer JavaScript, browser storage or other origin-scoped capabilities. A validated isolation design—or an explicitly approved namespace amendment—is required before arbitrary customer active content ships. (developer.mozilla.org)
Hosting limits MUST be enforced server-side. The existing Free-site inactivity policy MUST take affected sites offline without deleting their stored project data.
SH-09 — Real Stripe subscriptions and billing
Canonical plans and resource configuration
Stripe integration MUST implement the canonical subscription model rather than replace it with whatever is easiest to express in a pricing widget.
Preset identity, customised resource quantities, price-threshold entitlements and account-bound grants MUST remain distinct. The server MUST calculate effective access; clients MUST NOT grant resources from a displayed plan name or locally supplied price.
Existing preset definitions MUST remain versioned. Changing a commercial preset MUST NOT silently rewrite existing subscriber allocations. Unspecified quantities MUST remain unresolved rather than being invented during Stripe setup.
Checkout and activation
Checkout MUST begin from an authenticated, server-generated configuration quote. The quote MUST identify the account or organisation, resource configuration, currency, billing cadence, applicable charges and effective change timing.
The backend MUST bind Stripe customer, subscription, price and invoice identifiers to the correct canonical account or organisation. Usernames and client-supplied account claims MUST NOT determine that binding.
Returning to a success page MUST NOT itself activate paid resources. Activation requires verified payment/subscription state and a successful entitlement update.
A payment that succeeds while provisioning is delayed MUST produce a recoverable pending state. Retrying that operation MUST NOT charge the customer again.
Webhooks and reconciliation
Webhook processing MUST verify Stripe signatures against the raw request body, durably record accepted events and process them through retry-safe handlers.
The implementation MUST handle duplicate deliveries and out-of-order events. It MUST prevent both repeated event processing and repeated business effects, such as granting the same invoice’s allocation twice. Stripe does not guarantee event order, and its documentation explicitly requires duplicate-event handling. (docs.stripe.com)
The backend MUST support reconciliation against current Stripe objects after delivery failures or inconsistent local state. A delayed older event MUST NOT overwrite a newer valid subscription configuration.
Payment state, entitlement state and provisioning state MUST remain separately diagnosable.
Customer management and lifecycle
Users MUST be able to manage payment methods, inspect invoices, manage supported subscription configurations and cancel through authenticated billing flows.
Stripe’s customer portal MAY provide supported account-management functions. It MUST NOT be the only implementation of custom resource changes: Stripe documents limitations on updating subscriptions containing multiple products or usage-based billing. Those configurations require a compatible trusted CAKE billing flow. (docs.stripe.com)
Plan changes MUST preview the resulting price, resource allocation, effective date and any immediate charge or credit. Renewal failures, additional payment authentication, cancellation, refunds and disputes MUST map to explicit account states under the approved billing policy.
Billing failure or downgrade MUST NOT silently delete user files, site source or organisation content. Billing management, authorised export and recovery MUST remain available through the appropriate routes.
Business and Business + remain organisation add-ons with separately funded resources. Unlimited seat capacity MUST NOT be interpreted as unlimited storage or AI usage. Existing seat-remediation and account-bound grant rules remain applicable.
SH-10 — Cloud AI and connected application services
Cloud-backed features MUST use real provider connections, with credentials held by trusted services.
The AI gateway MUST enforce identity, organisation policy, model entitlement, resource limits and Dust before execution. It MUST support streaming, cancellation, provider failures and recoverable interrupted sessions.
Usage MUST follow the existing reserve → run → settle contract. Parallel requests MUST NOT spend the same balance twice, and uncertain provider outcomes MUST remain subject to reconciliation rather than arbitrary charging or credit creation. Local models and core local tools MUST NOT be charged as cloud AI usage.
Desktop, web and Personal API access MUST draw from the same applicable entitlement and usage services. Personal API access retains its existing eligibility and shared-allocation rules.
Every existing feature that declares an online dependency MUST have that dependency implemented and validated. This includes applicable collaboration, organisation services, provider connections, notifications, Forms responses, published integrations, Agents and Automations.
Execution location MUST be explicit. A browser task, a cloud-hosted task and a task requiring an enrolled desktop device are different capabilities. Closing a tab MUST NOT silently terminate a task advertised as cloud-hosted; an offline device MUST NOT be shown as executing work.
Long-running operations MUST expose durable job identity, progress, cancellation, retries and results. Reconnection MUST NOT repeat consequential actions. Persistent work MUST retain its authorised identity, scope and funding source.
SH-11 — Operations, security and release delivery
The production service MUST include health checks, actionable failure reporting, privacy-preserving diagnostics and alerts for material outages, failed billing processing, failed jobs and approaching resource limits.
Rate limits, account quotas and workload admission controls MUST prevent one customer from consuming another customer’s allocation or exhausting shared services without bounds. Spending alerts MUST NOT be treated as equivalent to an enforced spending ceiling.
Backups and recovery MUST cover canonical metadata, file revisions, account mappings, entitlement records, site source and deployment configuration—not merely a collection of uploaded bytes. Restoration MUST be tested, with documented recovery objectives.
The existing Privacy Ceiling applies to hosted services, provider integrations, telemetry, retention, exports and deletion propagation. Moving functionality online MUST NOT silently broaden AI access or weaken promised encryption.
Deployment pipelines MUST identify exact source revisions, validate changes before activation and support rollback. Database/schema and client updates MUST account for already-open browser sessions and older supported native clients.
Public download and update paths MUST provide real, verified release artifacts. Home and Launcher MUST continue using their shared package authority, integrity checks and staged activation rather than separate manually maintained update lists.
SH-12 — End-to-end acceptance
Acceptance MUST exercise the deployed services, not only mocks or isolated components.
