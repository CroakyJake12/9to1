# Models, discovery and universal model routing — Usage data contract; MODEL REASONING EFFORT DISPLAY (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2203 · v2 lines 13052–13055

LE-06 — Usage data contract
The floating usage bar is anchored to the bottom of the chat-list area without covering list items or the composer. It shows the actual configured 9to1 windows and any limits a connected provider can measure; daily, weekly and monthly are supported window types, not permission to invent a limit the account does not have. Compact summaries expand into source-labelled details.
Each measure identifies provider, connection/account, resource/unit, used amount, limit when known, period/reset semantics, observed-at time and freshness/availability state. Use existing account/provider APIs; billing and quota enforcement remain server-authoritative. Multiple accounts and resources MUST NOT be combined into an unexplained percentage. Preserve Dulche's null/Na/zero distinction: unavailable is not zero or unlimited.
Refresh from existing change events or bounded provider polling, respecting rate limits. Stale, offline, exhausted and provider-unavailable states must be visible; switching account removes the old account's values. Acceptance covers a real 9to1 allowance, a measurable external limit, unavailable metrics, a reset and an account switch; UI tests must prove the bar does not obstruct narrow or scaled layouts.

### COMPANION-B0015 · companion lines 95–102

SPUI-133 — MODEL REASONING EFFORT DISPLAY
The percentage attached to the model name in the Spaces Chat compact picker (for example “Qwen 3.6 • 100%”) is the currently selected REASONING EFFORT LEVEL for that model/route, NOT quota, remaining usage, context-window fill, confidence, CPU/GPU utilisation or provider health.
The selected effective model's REAL provider/Dulche capability metadata determines the number N of discrete supported effort levels and whether reasoning is enabled/disabled or configurable. Show N distinct user-facing percentage choices, each mapped one-to-one, in increasing order, onto the N actual advertised effort levels:
  N=3: 35%, 70%, 100%.
  N=4: 25%, 50%, 75%, 100%.
  N=5: 20%, 40%, 60%, 80%, 100%.
For other N, present evenly distributed, legible percentages for each distinct advertised level without inventing unsupported options. The listed 3/4/5 mappings are canonical. Percentages are normalised UI LABELS for discrete provider-specific effort rankings, NOT a literal continuous strength measurement nor a supported percentage of model parameters or context tokens. Displayed options MUST remain bijective to advertised reasoning levels; selecting the first displayed preset MUST invoke the first actual level and not the second. Bind selection to the exact model/provider's stable level identity or ordinal, not to an arbitrary percentage passed through an incompatible floor(p*N/100) formula. When the user changes models, discover the new model's own reasoning/effort options and show only the eligible ones; validate inherited and explicit selections. If a model offers an independent reasoning enabled/disabled switch, honour and display its real on/off state. If no reasoning-effort control exists, do not fabricate a 100% control or suggest that reasoning is configurable. If a provider supplies effort tiers but no on/off switch, do not invent one. Setting changes must affect the actual subsequent Dulche requests and survive the appropriate request/conversation override lifecycle. Fail clearly for unsupported effort settings, never silently assume the backend accepted them.
IMPLEMENTATION NOTE: Existing V2 low-level SetReasoningLevel prose uses min(n−1, floor(p*n/100)) for arbitrary percentage inputs. That function would map the first displayed 35% (for three levels) to the SECOND tier and the first displayed 20% (for five tiers) to the SECOND tier. The canonical user-facing selector must therefore map directly to discrete provider effort LevelIDs/ordinals, not route preset labels through that old formula. Any arbitrary-percent legacy API compatibility must be separately specified/versioned rather than causing incorrect or duplicated preset selection.

### COMPANION-B0016 · companion lines 105–111

SPUI-134 — USAGE + CONTEXT PROGRESS CIRCLE
A separate compact PROGRESS CIRCLE sits on the top header, preferably immediately LEFT of the Universal Model Picker. This distinct indicator reports two independent metrics, rendered in DIFFERENT theme-accessible colours:
  • Usage: actual usage against the relevant account/provider's applicable limit/window, when measurable.
  • Context window: consumed effective context relative to the chosen model/route's applicable usable context capacity, where measurable.
RECOMMENDED IMPLEMENTATION (VISUAL PROPOSAL): two concentric arcs/rings sharing one compact circular control, one per metric, with stroke colours tied to CUI semantic tokens. This is better than adding the unrelated measurements into one cumulative pie or percentage. The user only confirmed two differently coloured measures in a progress circle; exact ring geometry/colours are subject to normal visual review.
Hovering (or keyboard focusing / clicking / tapping) shows which colour is Usage versus Context, with labelled values, units, relevant provider/account/model, scope, reset period where applicable, observed freshness and estimated/unavailable states. Colour is never the only semantic distinction; labels and accessibility descriptions remain usable with reduced motion and contrast themes. Provider-level usage is not inferred from model reasoning percentage. Context progress should be grounded in the actual effective model and conversation/request token budget; if the model's effective usable context differs from advertised max, identify the applicable denominator. Do not falsely claim exact utilisation where context compression, hidden provider overhead or unavailable telemetry prevents measurement. Show unknown/stale values honestly rather than converting them to 0% or 100%.
Respect V2 LE-06 floating, detailed usage bar and provider usage metrics as a separate existing surface: the new circle is a concise TOP-HEADER summary/access point, not a reason to remove the sidebar's more comprehensive provider/quota information. Usage windows for different providers/resources must not be silently combined into a misleading aggregate. Theme, collapsed sidebar, Tab View, floating Summary and responsive layout must keep this control reachable and non-obstructive.

### COMPANION-B0017 · companion lines 114–116

SPUI-135 — ACCEPTANCE
In real running Spaces: (a) model with 3 efforts gives 35/70/100 and each maps to first/second/third actual provider effort; (b) 4 and 5 tier models map all available choices once; (c) model without configurable effort does not display fabricated tiers; (d) on/off toggle, if provider-supported, changes effective model configuration; (e) change model while draft/conversation retained; (f) usage/context circle renders two distinct tracked values with colour legend on hover/focus, shows accurate units/source and limits, handles unknown and stale data without fabricated numbers; (g) neither usage nor context changes the meaning of model-picker 100%; (h) test reduced motion, keyboard and small viewport. Verification requires backend request/capability records and genuine UI evidence, not mock values.
END 13 OCTOBER OWNER-CONFIRMED SELECTOR CORRECTION

### COMPANION-B0018 · companion lines 119–120

13 OCTOBER 2026 — OWNER-CONFIRMED USAGE VISIBILITY AND USER CONFIGURABLE MODEL/PROVIDER LIMITS
STATUS: CONFIRMED FOR OWNER'S SPECIFIED SEMANTICS. APPEND TO SPUI-133–135; DO NOT REPLACE ANY EXISTING CUI, PROVIDER SECURITY OR LE-06 OBLIGATIONS.

### COMPANION-B0019 · companion lines 123–129

SPUI-136 — SEPARATION OF CONTEXT AND REMOTE USAGE DISPLAY
The compact Spaces top-header progress-circle control remains immediately left of the Universal Model Picker when both are applicable. Its CONTEXT occupancy metric is separate from USAGE. Reasoning percentage beside model name (e.g. 100%) remains exclusively the currently selected model reasoning-effort tier and MUST NOT be re-used for quota or usage.
(a) LOCAL MODEL: The usage meter/arc MUST be HIDDEN. Context-window progress MAY remain visible when actual effective context capacity and consumption are known. The absence of a usage arc for local models must not imply absence of RAM/VRAM/runtime constraints; those have their own diagnostics. Do not display the M warning merely because a model is local.
(b) REMOTE/CLOUD MODEL WITH VERIFIED PROVIDER USAGE DATA: Show usage as a coloured progress arc only where a meaningful, trustworthy measured numerator and denominator are both available for the applicable scope/window. It can coexist with the differently coloured context metric. Tooltip/focus details identify scope (provider or model), resource/unit, window, value, limit, provider account, freshness/reset if applicable, and data provenance.
(c) REMOTE/CLOUD MODEL WITH NO PROVIDER USAGE DATA: HIDE the usage meter/arc and show an accessible compact “M” marker, indicating that the model is LIKELY METERED / may count towards an external allowance or billing but provider usage telemetry is unavailable. “M” is NOT itself evidence that billing is occurring. Hover/focus/tap explains “Provider usage unavailable; this model may still be metered”, plus any separately recorded 9to1-only custom budget state. M must not be displayed as a bogus zero percentage or a guessing ring. Keep independently measurable CONTEXT indicator visible, if available.
(d) Only one reliable metric in a circular indicator is allowed: display only that metric; do not render an empty second ring as though it is a measurable zero. If neither is measurable, show an informative unavailable state rather than fabricated measurements. Do not mix local usage with remote billing, and do not infer provider quotas from local tokens alone.
(e) Provider usage summaries, the separate LE-06 comprehensive sidebar usage UI and the Home Models usage limits are linked surfaces, not conflicting duplicate sources of truth. Hidden compact usage ring does NOT disable user-configured limit enforcement or local request accounting.

### COMPANION-B0020 · companion lines 132–137

SPUI-137 — LIMIT SCOPE: PROVIDER AND INDIVIDUAL MODEL
Home's canonical model/provider management MUST support USAGE LIMITS at BOTH independently selectable scopes:
  PROVIDER-SCOPED limit — applies to requests attributed to the specified configured provider/provider account and cumulatively covers all its applicable models/aliases/routes; identity is by stable ProviderID + Credential/Account Scope, never simply a display name. A separate provider account must not accidentally share or consume another account's budget.
  MODEL-SCOPED limit — applies only to the specific provider-scoped canonical ModelID (including aliases resolving to that identity), independent of other models from the same provider. Model-specific scope must remain unambiguous if multiple providers expose similar model names.
Both scopes can be active simultaneously. Each request updates relevant counters at most once and is checked against EVERY applicable limit; model and provider counts represent distinct aggregates, not additive charges. The effective allowed action is governed by all active applicable constraints. A model limit never overrides/weakens a provider limit, provider hard limit, account entitlement, organisational policy or external billing/provider quota. Provider budgets can be set/edited in Provider Settings; model budgets can be set/edited in that model's Home settings. A model editor may display inherited provider caps as read-only or navigable, clearly separated from model-specific caps.
Provider quotas reported by the provider and OWNER-DEFINED 9to1 custom limits are distinct provenance/source categories, not two representations of the same limit. Present source, scope and enforcement behaviour clearly.

### COMPANION-B0021 · companion lines 140–150

SPUI-138 — HOME MODEL DETAILS UI (OWNER'S EXAMPLE)
Home → AI/Models → select model → Model Settings MUST expose a readable CUI-owned details/editor surface, including:
  Name: GPT 6 Astra (ILLUSTRATIVE ALIAS/EXAMPLE; NOT A VERIFIED OFFERING)
  Path: | Remote ▾ | https://openai.com/models (ILLUSTRATIVE URI; NOT AN ASSERTION THAT IT IS A VALID INFERENCE ENDPOINT)
  ID: gpt-astra (ILLUSTRATIVE provider-scoped model identifier)
  Key: Click to see (2FA Required)
  Context: 670000 tokens (ILLUSTRATIVE CAPACITY, not a verified model specification)
  Usage Limit: | 15 | tokens ▾ | per | week ▾ |
  + Add Limit
These are reference UI field contents, not factual external model metadata. The Path row includes configured source kind (e.g. Local/Remote) and URI/location appropriate to the model runtime. A URL that is an informational product page, rather than a valid API endpoint, must be diagnosed honestly when used as a connection path. Name can be user-friendly while canonical model/provider identity remains stable. Context shows configured, effective, and source-verified status where relevant; it must not pretend a user-entered context number is guaranteed by backend.
API keys/tokens MUST remain masked and securely stored, never included in logs, screenshots, crash traces or model context. “Click to see” reveals only after explicit authenticated action with fresh TWO-FACTOR authentication as the owner requires; reject reveal when unavailable. Follow Home shared credential/permission service, revocation and auto re-mask lifecycle. Distinguish Reveal, Replace, Test Connection and Remove actions. Never expose secrets through plaintext tooltips, accessibility descriptions or hidden fields.

### COMPANION-B0022 · companion lines 153–162

SPUI-139 — REPEATING USAGE LIMIT EDITOR
Both Model Settings and Provider Settings MUST provide a compact, editable, row-based usage-limit list with an explicit + Add Limit action.
Each Limit includes at least:
  Limit value: validated non-negative numeric bound appropriate to the metric.
  Metric/unit selector: Total Tokens, Input Tokens, Output Tokens, Tool Calls, Messages, Elapsed Time, and additional versioned extensible applicable resource metrics if supported.
  Period: “per” selected duration/window e.g. Day, Week, Month, or another supported configured interval.
  Scope/owner: Model-specific OR Provider/account-wide, visibly identified.
  Source: Provider-reported/Hard, User-defined Hard (locally enforced), or User-defined Advisory/Warning where supported.
  Active state, effective reset/timezone or rolling-window basis, observed/tracked usage, validity and error state.
Rows may be added, edited, disabled or removed with applicable confirmation and audit. Multiple limits of different units and periods coexist. They apply independently (e.g. 15 messages/week model cap AND 200,000 input tokens/month provider cap). Do NOT sum incommensurate units, roll distinct provider accounts together, or present a single magic combined remaining percentage. Limit scopes may be inherited by models from a provider, and both user-defined and provider-native limits must show which scopes apply. Tool-call definition, request/message counting and Elapsed Time semantics must have explicit stable accounting rules (e.g. actual elapsed wall-clock duration versus billable processing time) and honest provider capability support.

### COMPANION-B0023 · companion lines 165–169

SPUI-140 — COUNTING, ENFORCEMENT, FALLBACK, AND LIMIT TRANSPARENCY
User custom budgets MUST provide useful consumption control even when external providers do not expose quota telemetry. 9to1 may count its own authenticated request activity and measured token/tool/message/time metrics via Dulche, while labelling those values as “9to1-observed only” or Estimated when appropriate. It MUST NOT claim those numbers include all outside-provider usage or represent a real provider balance. In accordance with SPUI-136, lack of PROVIDER usage data still hides the compact remote usage arc and uses M, even when local tracked custom-budget usage can be inspected in Home or the indicator's detail surface.
When a custom limit is enforceable for 9to1-originated work, the router MUST check it before starting applicable requests and enforce it with authoritative shared counters for concurrency and retries, not merely grey out the UI. High-risk conflicting provider-side uncertainty must not be passed off as reliable hard cap. Advisory mode may warn without blocking; Hard mode blocks according to declared scope and period. Prior completed/pending actual usage must not be duplicated on retry, fallback or reconnection. When limits cannot be reliably counted/enforced, show “tracking unavailable” or require advisory mode; never promise a hard guarantee.
Provider usage signals can be delayed or partial; show observed-at/freshness and actual reason when a cap is unavailable/exhausted. Usage is attributed to the provider/model that ACTUALLY served the request after any authorised fallback, not simply the originally highlighted selection. Each applicable provider and model budget applies to whichever real backend processes the request, with no unauthorised local-to-cloud fallback.
Provider caps and user caps are independently enforced. Reaching one applicable cap blocks eligible traffic according to the relevant hard policy, even if another allowance still remains. A user-defined limit cannot increase a provider quota, paid entitlement or administrator restriction. Configurable warnings may trigger before thresholds, but their exact preset percentages are not fixed here.
