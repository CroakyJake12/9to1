# Account, identity and resource entitlements — Accounts & Subscription; CAKE ID and authentication (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0336 · v2 lines 1857–1859

Accounts & Subscription
CAKE ID and authentication
CAKE ID is the shared identity used across CAKE and 9to1. A normal CAKE ID is separate from a WordPress user account; WordPress may remain linked for site administration, but ordinary 9to1 users must not need WordPress accounts.

### V2-B0337 · v2 lines 1862–1862

9to1 web apps authenticate against CAKEMODS/CAKE ID using a browser-based OAuth 2.0 Authorization Code flow with PKCE or an equivalent secure public-client flow. Desktop, web and open-source/custom clients are treated as untrusted public clients: they receive short-lived user access tokens and never contain provider keys, billing secrets or entitlement-signing secrets.

### V2-B0338 · v2 lines 1865–1865

The server is authoritative for account state, sessions, entitlements, organisation policy, Dust, storage/site quotas, billing and provider credentials. A modified or self-built client can change its own interface or features but cannot grant itself paid entitlements, bypass quotas or mint trusted usage.

### V2-B0339 · v2 lines 1868–1868

Authentication/session operations must support account sign-in/sign-out, current-user/profile lookup, active-session/device inspection and revocation. Sensitive account or billing changes use trusted CAKEMODS web flows rather than accepting raw payment credentials through the open-source client.

### V2-B0340 · v2 lines 1871–1898

9to1 welcome and first-run flow
A fresh 9to1 desktop/Home profile MUST use a short, skippable welcome flow before normal Home use. This flow is separate from the fuller 9to1-OS installer/OOBE contract and MUST NOT require a CAKE ID or local-model download in order to reach local 9to1 functionality.
Step 1 — Welcome
Show “Welcome to” with the 9to1 logo as the dominant visual and one primary Start button. The first screen SHOULD remain deliberately minimal and MUST NOT present an unrelated setup questionnaire.
Step 2 — Sign In?
Show the prompt “Sign In?” with supporting copy explaining that signing in provides access to cloud and connection features. The primary Sign In action opens the system browser at 9to1.uk/auth using the existing secure public-client authentication contract. Successful authentication MUST return the resulting authorised session to the initiating 9to1 profile without exposing provider secrets to the client. A clearly visible Not Now action continues in local-only mode without disabling otherwise eligible local functionality.
Step 3 — Local model discovery
After the sign-in decision, 9to1 MUST search the supported local-model/runtime registry for an installed model that satisfies the current minimum local-AI capability policy. If at least one eligible sufficiently current/capable local model is already available, the model-download screen is skipped.
If no eligible local model is found, show “Download Models?” with supporting copy explaining that local models provide unlimited on-device AI usage. The screen presents up to three currently recommended models as selectable model cards and shows inline download progress for active downloads. The user MAY continue without downloading a model.
The recommended-model list MUST NOT be hard-coded into the shipped client. 9to1 MUST retrieve a small versioned recommendation manifest hosted under 9to1.uk. Each recommendation identifies the intended model/source and Hugging Face download location plus the metadata required by the existing Dulche local-model verification/install path. This allows the recommended set to change without requiring a 9to1 client update. Failure to retrieve the manifest MUST degrade to an honest unavailable state rather than fabricate recommendations.
Completion and repeat behaviour
After the sign-in/model decisions are complete or skipped, continue directly into Home; no additional mandatory completion page is required. The completion state MUST persist for that profile so ordinary returning users are not shown the welcome flow again. Sign-in state, local models and equivalent settings remain manageable later through their normal Home/Settings surfaces.
Profiles — customisation and Jacob's reserved account
Profile customisation MUST include changing the display name and username through the profile/account UI and equivalent authorised APIs. Username availability, uniqueness and reservation checks MUST apply consistently across UI, APIs, registration and rename flows. Changes MUST preserve the canonical CAKE ID/UserID, ownership, history, permissions and entitlements; mutable names MUST NOT become identity or authorisation keys.
The exact handle @CroakyJake MUST be reserved exclusively for Jacob. Registration and rename flows MUST prevent other accounts from claiming it, including case variants that would bypass the reservation. The reservation MUST be bound through trusted provisioning to Jacob's verified canonical account ID, not awarded to whichever account first requests that text. Production identifiers MUST be verified rather than guessed.
Jacob's verified account MUST automatically receive all Business features free of charge, including subsequently added Business features, without requiring a paid Business subscription. This is an account-bound entitlement: changing a display name or username MUST NOT grant it to another person or remove it from Jacob. Reservation and entitlement administration MUST be privileged, auditable operations, separate from ordinary profile editing. Free Business access does not bypass security permissions or authorise third-party spending.
Profile customisation — required and optional fields
The profile editor MUST include the following fields. An asterisk (*) denotes a required field; only Name and Username are required within this field set.
Name* — REQUIRED. The user's profile/display name, extending the existing display-name customisation requirement.
Username* — REQUIRED. The user's account handle, subject to the existing availability, uniqueness and reservation rules.
Icon — OPTIONAL. The user's profile icon, which the user can add, change or remove.
Pronouns — OPTIONAL. User-provided pronouns, which the user can add, change or clear.
Job — OPTIONAL. The user's job/job title. This field is especially relevant in a Business context but MUST also be available to ordinary personal users; it MUST NOT be Business-only or mandatory for Business users.
The UI MUST visibly mark Name and Username with an asterisk and explain that * denotes required. Icon, Pronouns and Job MUST be allowed to remain unset and to be cleared without preventing an otherwise valid profile from being saved. Name and Username MUST NOT be saved as empty or whitespace-only values. Optional personal information MUST NOT be inferred or populated without the user's input.
The equivalent authorised profile APIs MUST expose the same five fields and apply the same required/optional validation rules. Partial updates MUST preserve fields omitted from the request rather than clearing them. Existing canonical account identity, ownership, permissions, username reservation and account-bound Business entitlement requirements remain unchanged, including Jacob's reserved @CroakyJake handle and free Business features.
Acceptance MUST verify that a profile can be saved with valid Name and Username while Icon, Pronouns and Job are unset; all three optional fields can subsequently be added, changed and cleared; Job is available to both personal and Business users without becoming required; blank required fields produce validation errors; and equivalent authorised API operations preserve the same behaviour and account identity.
Profile API, provisioning and acceptance
Extend the existing account/profile APIs for profile reads and partial updates, icon management and username availability. Reservation and account-bound Business-entitlement provisioning MUST remain privileged, auditable operations. APIs MUST expose the same validation as the UI, preserve omitted fields and return structured conflicts or validation failures. Ordinary clients MUST NOT self-grant Business access. Test rename without identity or ownership loss, rejected unauthorised @CroakyJake claims and case variants, and automatic free Business access for Jacob’s verified account only; security permissions and third-party spending approvals still apply.

### V2-B0341 · v2 lines 1901–1902

Subscription model
9to1 uses a preset-first subscription model with optional customisation. Subscription state has two separate dimensions: price-threshold entitlements and resource configuration. The two MUST be stored and evaluated independently.

### V2-B0342 · v2 lines 1905–1905

Price-threshold entitlements determine which feature/model/site capabilities the account unlocks as its canonical configured monthly price crosses defined thresholds. Resource configuration determines the account’s allocated quantities, including AI Dust, storage, hosted-site capacity and other metered resources.

### V2-B0343 · v2 lines 1908–1908

Named plans Free, Boost, Plus, Pro, Ultra, Ultra+ and Max serve both as threshold names and as preset configuration names. Their canonical ascending PlanLevel order is Free = 0, Boost = 1, Plus = 2, Pro = 3, Ultra = 4, Ultra+ = 5 and Max = 6. APIs, billing logic and entitlement checks MUST use stable PlanID/PlanLevel rather than inferring ordering from display names. A preset is a convenience starting configuration; the preset label MUST NOT be treated as the sole source of entitlement truth.

### V2-B0344 · v2 lines 1911–1911

Local models and core local tools are not metered through cloud AI Dust. The Free configuration MUST retain useful local/core functionality while cloud AI, hosted storage/sites and other server-funded resources remain subject to server-authoritative entitlements and quotas.

### V2-B0345 · v2 lines 1914–1914

Price thresholds

### V2-B0346 · v2 lines 1917–1917

Let EntitlementPriceMonthly be the canonical monthly price assigned by the subscription backend to the account’s current configuration. The backend MUST compute effective threshold entitlements from this value; clients MUST NOT infer them from the displayed preset name.

### V2-B0347 · v2 lines 1920–1920

Thresholds are cumulative. For every threshold T where EntitlementPriceMonthly >= T.Price, all entitlements defined by T are added to the account’s effective entitlement set. Therefore a Max-priced configuration also receives all lower-threshold grants, including Ultra grants, unless a future threshold explicitly defines a non-cumulative exception.

### V2-B0348 · v2 lines 1923–1923

Selecting a preset does not bypass threshold evaluation. If a user customises a preset and the resulting configured price crosses a different threshold, the backend MUST recalculate the effective threshold set from the resulting EntitlementPriceMonthly.

### V2-B0349 · v2 lines 1926–1933

The currently defined commercial thresholds are:
* Free — £0/month. Unlocks access to select free cloud models and unlimited local/core tools.
* Boost — £7.99/month. Entry paid cloud-AI tier. It unlocks CAKE-funded cloud models within the Boost model-cost band, benchmarked against Luna-class pricing rather than tied to one specific named model.
* Plus — £19.99/month. Adds a materially larger cloud-AI/resource allowance than Boost while retaining access to the Boost model-cost band and any other models priced within that effective band. Plus exists as the regular-use tier between Boost and Pro.
* Pro — £34.99/month. Adds a higher CAKE-funded model-cost band benchmarked against Sol-class pricing, while retaining all lower-band model access. Pro also permits materially higher cloud-AI, storage and hosted-site allocations than Plus.
* Ultra — £69.99/month. Adds the highest standard CAKE-funded model-cost band, benchmarked against Astra-class pricing, while retaining all lower-band model access.
* Ultra+ — £99.99/month. Retains at least the complete Ultra model-access set and enables materially larger cloud-AI, storage, hosted-site and concurrency allocations than Ultra.
* Max — £149.99/month. Retains at least the complete Ultra+ model-access set and enables the highest standard preset cloud-AI, storage, hosted-site and concurrency allocations. Because thresholds are cumulative, Max-priced configurations retain every lower-threshold entitlement.

### V2-B0350 · v2 lines 1936–1937

Cloud model entitlement MUST be expressed through versioned effective model-cost/capability bands rather than a permanent allow-list of model brand names. References such as Luna-class, Sol-class and Astra-class are pricing/capability benchmarks for the current commercial design, not exclusive model identities. Any CAKE-funded model whose effective provider cost and capability profile fit within the user's unlocked band may be made available, subject to provider availability, safety/privacy policy and backend configuration. Replacing a benchmark model with another comparably priced model MUST NOT require renaming the subscription tier.
OPEN: the backend still needs one canonical rule defining how temporary discounts, promotional credits, tax and non-monthly billing cadence affect EntitlementPriceMonthly. Until that rule is defined, threshold eligibility MUST be calculated only by the server and MUST NOT be reproduced locally by clients.

### V2-B0351 · v2 lines 1940–1940

Preset configurations

### V2-B0352 · v2 lines 1943–1943

Free, Boost, Plus, Pro, Ultra, Ultra+ and Max also exist as pre-made subscription configurations. A preset MUST define explicit resource values such as allocated AI Dust, storage allocation, hosted-site limits, concurrency/burst limits where applicable and any other metered resources supported by the subscription system.

### V2-B0353 · v2 lines 1946–1946

Preset configuration and threshold entitlement state MUST remain separate server-side fields. Changing resource values MAY change the configured monthly price and therefore the unlocked threshold set, but MUST NOT mutate the canonical definition of the source preset.

### V2-B0354 · v2 lines 1949–1949

A customised plan MUST retain the concrete resource values selected by the user even when those values no longer match any named preset. The UI MAY display the source preset for context, but APIs MUST return the actual resource configuration rather than reconstructing it from a tier name.

### V2-B0355 · v2 lines 1952–1952

Preset definitions MUST be versioned. Existing accounts MUST retain an auditable reference to the preset version they originally selected plus subsequent customisation state. Updating a preset definition MUST NOT silently rewrite existing subscriber allocations.

### V2-B0356 · v2 lines 1955–1955

Known preset requirements are now: Boost is £7.99/month with light cloud-AI usage, small hosted storage, a maximum of 1 full hosted site and 3 single-page sites; Plus is £19.99/month with materially higher cloud-AI usage and storage, a maximum of 3 full hosted sites and 5 single-page sites. Pro (£34.99), Ultra (£69.99), Ultra+ (£99.99) and Max (£149.99) MUST each provide materially more included cloud-AI usage, storage and hosted-site capacity than the preceding paid preset. Exact AI Dust/storage/site quantities above Plus are not yet fully specified and MUST NOT be invented.

### V2-B0357 · v2 lines 1958–1958

Canonical subscription state

### V2-B0358 · v2 lines 1961–1972

The server-authoritative subscription object MUST expose at least the following fields:
* PresetID? — stable identifier of the preset used as the starting configuration, or null when no preset applies.
* PresetVersion? — version of the referenced preset, or null when no preset applies.
* IsCustomised — true when the current resource configuration differs from the referenced preset/version.
* EntitlementPriceMonthly — canonical monthly price used for threshold evaluation.
* EffectiveThresholds — ordered list/set of all currently unlocked thresholds.
* EffectiveEntitlements — resolved feature/model/site entitlements after threshold, account and organisation-policy evaluation.
* AIDustAllocated — allocated cloud-AI Dust for the current billing/configuration period.
* StorageAllocatedBytes — storage allocation in bytes.
* HostedSiteLimit — concurrent/full hosted-site allowance.
* SinglePageSiteLimit — additional single-page-site allowance after threshold/resource evaluation.
* AdditionalResources — versioned typed object/map for future metered resources. Clients MUST ignore unknown keys they do not understand while preserving them when round-tripping configuration.

### V2-B0359 · v2 lines 1975–1975

Free hosted-site inactivity

### V2-B0360 · v2 lines 1978–1978

For a Free hosted site, inactivity takes the site offline rather than deleting it. During the first 120 days the account MUST sign in at least once every 30 days to keep the free site online. After that period, the current rule requires at least one account sign-in per year. Taking a site offline MUST NOT delete its stored site data.

### V2-B0361 · v2 lines 1981–1981

Custom resource allocation

### V2-B0362 · v2 lines 1984–1987

The custom-plan editor includes the high-level choice “What do you want to use 9to1 for?” with these profiles:
* Full Workspace (Balanced)
* Prioritise AI Limits
* Prioritise Storage & Sites Limits

### V2-B0363 · v2 lines 1990–1993

Current £10 allocation examples are:
* Balanced: 250 GB storage + 35,000 AI Dust.
* AI-priority: 25 GB storage + 65,000 AI Dust.
* Storage/Sites-priority: 500 GB storage + 15,000 AI Dust.

### V2-B0364 · v2 lines 1996–1996

The current internal allocation baseline is £1 of plan budget = 10,000 AI Dust before commercial margin/profit. The server remains authoritative for the actual commercial conversion and account balance.
