# Account, identity and resource entitlements — Business add-ons — pricing, seats and resource billing; Entitlements and usage API (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0365 · v2 lines 1999–2020

Business add-ons — pricing, seats and resource billing
Business costs an additional USD $5 per month per organisation and supports a maximum of 50 seats. Business + costs an additional USD $15 per month per organisation and has no subscription seat cap. Neither add-on has a separate per-seat fee. Purchased AI usage/credits, storage and other funded resources remain separately configured and may be pooled or allocated individually; unlimited seats do not mean unlimited resources.
Both add-ons include 9to1 Admin and the functional Business capabilities specified below. Business + includes the Business capability set; further exclusive Business + capabilities are OPEN and MUST NOT be invented from its name. Pricing displays, quotes, invoices and entitlement APIs MUST distinguish the organisation add-on, seat capacity and resource charges. A base-resource quote in another currency MUST NOT be silently relabelled as USD; the trusted billing quote must make currency/conversion explicit.
Billing state MUST store AddOnID, add-on definition version, OrgID, seat-limit value or explicit Unlimited, effective dates and applicable account-bound grants separately from user resource configurations. No client-supplied role, username or plan label can self-grant Business. The free Business entitlement for Jacob remains governed by the Profiles subsection and does not authorise paid resource purchases or third-party spending.
Changing Business + to Business with more than 50 active members MUST require a seat-remediation preview or retain a visibly constrained pending-downgrade state; it must not silently remove users or delete shared content. Add-on cancellation and billing failure MUST have explicit entitlement/recovery states. Account-level billing, export and recovery remain available through appropriate authorised routes even when normal Business editing is unavailable. Exact grace periods, proration and suspended/pending-seat accounting remain OPEN unless defined in the canonical billing policy.
Entitlements and usage API
The subscription/account layer must expose supported first-party APIs for the information and actions available to the user, following the global API-parity rule. The core surface includes:
9to1.Account.GetCurrent();
9to1.Account.GetEntitlements();
9to1.Account.ListSessions();
9to1.Account.RevokeSession(sessionID);
9to1.Account.RevokeAllOtherSessions();
9to1.Subscription.GetPlan();
9to1.Subscription.GetUsage();
9to1.Subscription.GetDustBalance();
9to1.Subscription.GetStorageUsage();
9to1.Subscription.GetSiteUsage();
9to1.Subscription.ListPresets();
9to1.Subscription.PreviewConfiguration(config);
9to1.Subscription.OpenCheckout(config);
9to1.Subscription.OpenManage();
9to1.Subscription.GetPersonalAPIStatus();

### V2-B0366 · v2 lines 2023–2023

Billing-changing calls do not accept raw payment-card details. OpenCheckout/OpenManage hand off to an authenticated trusted CAKEMODS billing flow. Read APIs return the server-authoritative account state rather than trusting client-maintained counters.

### V2-B0367 · v2 lines 2026–2027

Server-authoritative enforcement
All paid/cloud usage is enforced server-side. The open-source client is never the authority for remaining Dust, model access, storage, site count, billing state or quotas. Custom/forked clients use the same live account entitlements and cannot bypass limits by changing local code.

### V2-B0368 · v2 lines 2030–2030

For cloud AI, the trusted gateway checks identity, organisation policy where applicable, model entitlement, hard limits and Dust before execution. Usage follows a reserve → run → settle model so concurrent requests cannot overspend the same balance. Provider keys and commercial billing logic remain server-side.

### V2-B0369 · v2 lines 2033–2033

Short-lived or one-time signed capability tokens may be used internally for narrowly scoped operations, but they do not become reusable secrets embedded in applications. Server-side storage/site operations perform the same authoritative entitlement checks.

### V2-B0370 · v2 lines 2036–2037

Personal API access
Accounts with at least 80,000 allocated AI Dust unlock Personal API access. Personal API usage consumes the same account Dust allowance as normal 9to1 cloud-AI usage; it is not a separate unlimited pool.

### V2-B0371 · v2 lines 2040–2040

Personal API access is for the account holder’s own external integrations and workflows. It is not a public-facing resale allowance and does not grant permission to redistribute CAKE provider credentials or resell the underlying service.

### V2-B0372 · v2 lines 2043–2044

Web subscription behaviour
Web and desktop clients MUST resolve account state from the same CAKE ID and entitlement service. For the same account and organisation context, plan identity, Dust balance, hosted-storage usage, site quota/usage and applicable feature entitlements MUST be consistent across official clients, subject only to explicitly documented platform capability differences.

### V2-B0373 · v2 lines 2049–2049

Where a future web feature requires material hosted compute that is not available on the free/local product - for example hosted Mini Computer virtual machines - it may be offered as a subscription-backed server capability with explicit resource limits. The browser UI must never pretend that local-only or unavailable server compute exists.

### V2-B0374 · v2 lines 2052–2081

Business features — functional specification
BIZ-01 — Organisation boundary and shared state
Business is an organisation collaboration and management capability set, not merely labels on pricing cards. It comprises Team Space and an organisation drive; organisation Connect; cross-account device Mesh; pooled or individually partitioned usage; team-wide Agents; and device-level policy controls for managed 9to1-OS Business accounts. 9to1 Admin supplies their management interface, while the existing apps retain canonical ownership of their domain data.
Every organisation-scoped operation MUST carry an explicit OrgID and authenticated membership/delegation context. Personal and organisation work remain separately identifiable in switchers, search, storage, AI context and billing. Joining an organisation MUST NOT merge personal files, chats, memories, devices or credentials into it. Moving/sharing personal material requires a deliberate authorised operation with destination ownership, access and billing impact shown. Leaving one organisation cannot revoke or destroy unrelated personal work or membership elsewhere.
BIZ-02 — Team Space and organisation drive
Each organisation MUST support a canonical shared Team Space backed by organisation-owned shared storage, not an independent copy of every member’s files. Space membership, role/group access, instructions, permitted models/tools and shared Agent bindings are managed through the existing Spaces model. Files, Pages, Boards and other app artifacts retain stable canonical IDs and their owning formats; references/embeds do not create silent forks.
The organisation drive MUST support folders, create/upload/open/edit where the owning app supports it, search, move/rename, version history, sharing/access review, archive/delete/recover and authorised export. Access may be assigned to organisation roles/groups or explicitly scoped members. Effective object access is checked by storage, search/indexing, previews, AI retrieval and exports, not just the Files UI. External sharing requires policy permission and explicit recipient/link scope; revocation must remove future server access and invalidate applicable derived access paths.
Shared-storage accounting MUST identify the owning organisation and charge canonical stored data once according to the storage policy rather than once per viewer. Access removal preserves organisation-owned artifacts and transfers responsibilities where necessary; it does not give administrators ownership of a departed member’s unrelated personal files. Restore/export workflows retain identity and permissions deliberately rather than accidentally republishing private content.
BIZ-03 — Organisation Connect
Connect MUST provide an organisation directory and scoped organisational groups/channels and contact endpoints, with role-controlled membership, creation and management. Where shared business identities or communication endpoints are configured, delegates act through named authorised roles with auditable attribution, not password sharing or undetectable impersonation. Inviting an external correspondent to communicate is distinct from making them an organisation seat or allocating paid usage.
Organisation communications MUST use Connect’s canonical conversations, contacts, calls and message identities. Agent email/call/text endpoints resolve to the same canonical team Agent and its permissions. External providers and device-backed transports require actual supported connections and credentials; provisioning paid endpoints needs separate approval. Membership or admin status does not create blanket permission to read private/E2EE content, export it to a model or bypass participant consent and retention controls.
BIZ-04 — Cross-account device Mesh
Business Mesh MUST allow explicitly authorised devices belonging to different organisation members to share supported compute/storage capabilities through Home/Mesh. Enrolment records MUST bind DeviceID, device/account owner, OrgID, verified trust/key material reference, supported capabilities, resource limits and grant expiry/revocation state. Organisation membership alone must not automatically pair a personal device or expose its files, GPU or network.
A resource grant MUST specify permitted consumers/workloads, CPU/GPU/memory/storage/concurrency budgets and permitted data locations. Job admission checks both requester permissions and the contributing device’s current policy. Data/secrets are scoped to the job and authorised endpoint; device contribution does not grant unrelated file, screen, microphone or personal-session access. Sensitive/local-only work cannot silently migrate to a different member’s device or a cloud provider without an authorised processing route.
Mesh MUST expose availability, queued/running jobs, utilisation, failure and cancellation/revocation outcomes. Revocation blocks new work and cancels or safely terminates existing work where supported; it must not claim to undo already completed actions or erase copies outside its actual control. A disconnected device remains unavailable/pending, never fictitiously running a workload. Windows and 9to1-OS expose only supported native capabilities; web can manage grants and observe jobs without pretending to supply privileged local resources.
BIZ-05 — Shared and individual usage pools
An organisation MUST be able to use a shared funded AI/resource pool or individually partition its purchased allocation by member/role. Partitioning is accounting over the same authoritative ledger, not duplicate credits. Store PoolID, source allocation, period, remaining/reserved/settled amounts, allocation owner and applicable policy. Member views show their effective allowance; authorised administrators can inspect aggregate and scoped consumption without exposing private work content.
Requests reserve capacity before execution and settle actual usage afterwards, releasing unused reservation according to the backend contract. The organisation total, individual ceiling and role ceiling all apply where configured. Parallel requests, retries and delayed provider reports MUST NOT spend the same balance twice. Changing pool mode preserves in-flight reservations and history and requires a validated allocation plan; insufficient funds or ambiguous settlement blocks unsafe transfer rather than inventing capacity.
Role-based rollover permission defined in Admin MUST be applied to eligible credit lots at rollover/expiry transitions and be visible in member balances. Changes preserve original lot provenance and settled charges. The product MUST distinguish rolled-over credits from newly purchased allocation and show the effective expiry. No personal balance is swept into an organisation pool merely because its owner joins; no organisation-funded balance becomes a departed member’s personal credit without an explicit supported transfer policy.
BIZ-06 — Team-wide Agents
A team Agent is a canonical Dulche Agent with organisation ownership/scope and configurable discovery, invocation, authoring and administration grants. Eligible members MUST be able to discover and invoke it from supported Spaces/app pickers and configured Connect channels without creating separate private copies. Shared configuration and approved knowledge are distinct from per-user conversations and private memories; access to one member’s context cannot leak into another member’s response.
Agent publishing/versioning, ownership transfer, required Plugins, allowed models, tool scopes, approval boundaries, schedules, budgets and concurrency limits MUST be enforced by Dulche/Home and the owning services. An Agent cannot use its owner’s wider authority to bypass the requesting member’s restrictions unless an explicit, separately authorised delegated service capability covers that operation. Persistent scheduled work records the service principal, scope and funding source and is re-evaluated on role, policy or membership changes.
Pausing, revoking or archiving a team Agent MUST stop new authorised dispatch, preserve auditable run history and report what happened to queued/running work. Sensitive credentials remain secure references; publishing an Agent is not permission to reveal them. AI Studio, Admin and runtime surfaces all edit or reference the same Agent identity and versioned definition.
BIZ-07 — Managed 9to1-OS accounts and device policy
Business MUST provide device-level administrative controls for explicitly enrolled managed 9to1-OS Business accounts. Controls MUST cover supported app/package and Plugin installation/enablement restrictions, allowed features and Automations, required organisation/role Plugins, enforced settings, default-model/routing policy and Mesh-resource participation. Existing compatibility-framework and privileged-operation restrictions continue to apply beneath every launch surface.
The OS policy agent MUST verify policy origin/version, apply only the enrolled managed scope and report Applied, Pending, Failed, Unsupported or NonCompliant status with reasons. Local UI changes, direct API calls and alternate launch paths cannot bypass enforced settings. Offline endpoints retain their last valid policy and cannot claim a newly published policy was applied without acknowledgement. Revocation and next-contact reconciliation must be explicit.
Managed account policy is not automatic whole-device ownership. Personal users, personal artifacts and unenrolled devices remain outside the organisation’s authority. Destructive remote actions, full-device wipe, arbitrary privileged shell access and surveillance are not implicitly authorised by Business. Other operating systems may expose supported Mesh and app policy, but they must not claim complete 9to1-OS endpoint-management parity without an explicit capability implementation.
BIZ-08 — Cross-app enforcement and acceptance
Permissions, policy, membership and credit changes MUST propagate through supported authoritative services and invalidate affected cached grants before further protected server actions. Published configuration, delivered configuration and acknowledged enforcement remain different states. Long-running rollout, export and reassignment use resumable jobs with per-target results. Downgrade, service outage or partial rollout must preserve data and fail closed for newly unauthorised actions.
Acceptance MUST demonstrate a shared artifact edited through two apps without forking; denied access absent from search, preview and AI context; organisation Connect without access to personal messages; explicit two-account Mesh enrolment, bounded execution and revocation; shared-pool concurrency and individual ceilings; role-dependent rollover; a team Agent that cannot leak another member’s private context; and OS restrictions enforced below UI across restart/offline/reconnect. Each feature requires equivalent authorised API coverage, persistence, accessibility, failure/recovery and actual end-to-end evidence. Inclusion on a pricing card alone is not conformance.

### V2-B0502 · v2 lines 2512–2512

Account and subscription surface

### V2-B0503 · v2 lines 2515–2515

Home MUST provide the primary native CAKE ID account/subscription surface. It displays authenticated user identity, organisation/workspace context where applicable, current configured subscription/preset source, EntitlementPriceMonthly, EffectiveThresholds, EffectiveEntitlements, AI Dust balance/allocation/usage, storage allocation/usage, site limits/usage and active sessions/devices using server-authoritative values.

### V2-B0504 · v2 lines 2518–2518

Home MUST NOT recalculate paid entitlements locally from a displayed tier label. Checkout, plan changes and sensitive billing management hand off to the trusted authenticated CAKEMODS billing flow defined by the Accounts & Subscription contract. Sign-out/session revocation actions MUST preserve local user data according to each app’s offline/storage policy rather than deleting it by default.
