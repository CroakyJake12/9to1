# Models, discovery and universal model routing — CONTEXT AND USAGE PRESENTATION; MANDATORY ACCEPTANCE (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### COMPANION-B0024 · companion lines 172–174

SPUI-141 — CONTEXT AND USAGE PRESENTATION
The circular indicator's context progress uses effective context capacity for the actual selected/request-effective model, not the illustrative “Context: 670000 tokens” merely because that number is in a settings form. Unknown hidden provider overhead, context summarisation/retrieval and unobserved tokens require an estimated/unknown label. Usage colours are distinct from context colours in every CUI theme, with accessible text labels and focus/touch interaction. When multiple meaningful usage limits apply, any compact usage summary must clearly identify which ONE limit it depicts, while additional applicable limits are inspectable; never average tokens with elapsed time or combine providers without explanatory per-source details.
No part of this usage system changes the already owner-confirmed discrete reasoning-effort percentages of the Universal Model Picker.

### COMPANION-B0025 · companion lines 177–189

SPUI-142 — MANDATORY ACCEPTANCE
Verify in identified real Home/Spaces and Dulche builds:
1. Local model: no usage arc, truthful context arc when measured and no false M.
2. Remote provider with valid usage/limit telemetry: real top-bar usage metric, different context colour, correct tooltip scope/account/window/refresh.
3. Remote with unavailable provider usage: no usage arc, M marker with likely-metered warning, context still usable, no fabricated provider numbers, with locally observed custom budget separately inspectable where available.
4. Define a provider-wide limit and show it applied across two of that provider account's models, without touching a different provider/account.
5. Define an additional model-specific cap, verify BOTH are respected and independently displayed, including different units/windows.
6. Add, edit, disable, remove and persist multiple limits, including tokens, input/output tokens, messages, tool calls and elapsed time; validate windows/reset semantics, limits and count attribution.
7. Verify custom hard cap blocks outgoing eligible requests from 9to1; provider hard cap and entitlements cannot be overridden; advisory warning never falsely claims enforcement.
8. Verify model/provider fallback records actual consumption exactly once, correct account, no privacy violations and no duplicate charges through retries.
9. Test key reveal only via fresh 2FA, storage/re-mask and log redaction; invalid remote path handled without simulated connection.
10. Check responsiveness, user CUI theme, keyboard/touch, unavailable/stale telemetry, zero vs unknown and LE-06 detail parity. Capture build identity, actual provider/API evidence and UI screenshots; report NOT RUN or BLOCKED rather than manufacturing observations.
END 13 OCTOBER 2026 USAGE SCOPE / MODEL SETTINGS AMENDMENT.

### COMPANION-B0026 · companion lines 192–193

13 OCTOBER 2026 — OWNER-CONFIRMED USAGE CIRCLE LIMIT PRIORITY AND PINNING
STATUS: CONFIRMED BEHAVIOURAL DIRECTION. Supplements SPUI-136–142 and the master V2 incorporation by reference. No implemented behaviour is claimed.

### COMPANION-B0027 · companion lines 196–202

SPUI-143 — DEFAULT LIMIT SELECTION (NO USER PIN)
The Spaces top-header USAGE progress circle presents the single MOST IMMEDIATELY CONSTRAINING applicable limit by the owner's two-stage decision rule, not the highest consumed percentage:
1. DEFAULT PREFERENCE: a SHORTER usage window takes precedence over a longer one (e.g. a five-hour limit is displayed before a weekly limit even if the five-hour cap is 20% used and the weekly cap is 80% used). Compare actual configured duration/windows, not the displayed percentage consumed.
2. SHORTER-WINDOW EXCEPTION: where an applicable longer-window cap has LESS ACTUAL REMAINING ALLOWANCE than the shorter-window cap IN COMPARABLE UNITS, the longer-window cap wins as it will constrain further equivalent use first. Example: weekly has 3,000 TOKENS left, five-hour cap has 5,000 TOKENS left; display weekly despite its longer window. The priority should account for imminent reset when its occurrence makes that cap non-blocking before the expected consumption.
3. Provider-wide and model-specific limits from both provider-reported and owner-defined settings MUST participate if they apply to the current effective model/provider account and have valid measurable data. Honor the source/telemetry visibility rules of SPUI-136: a local model hides usage; a remote model without provider-reported usage hides the compact usage arc and shows M rather than fabricating a fraction, even if a user custom cap can be separately inspected/enforced.
4. Never directly compare raw numeric remaining allowances of INCOMPATIBLE UNITS: tokens are not messages, time is not tool calls. A different-unit cap can outrank another only if a supported, transparent and sufficiently grounded forecast indicates the next effective blocking cap; otherwise use the time-window preference without pretending units are commensurable.
5. Actual blocking rules remain independent: all applicable enforceable caps are checked, regardless of which is displayed in the circle. Display choice must never switch off enforcement of hidden limits.

### COMPANION-B0028 · companion lines 205–209

SPUI-144 — USER-PINNED LIMIT SELECTION
The user MUST be able to PIN exactly one applicable usage limit as the PREFERRED limit displayed in the usage circle. Pin controls belong in the usage breakdown/details popover and/or the corresponding Home model/provider usage settings. Visually distinguish selected pinned limit by a pin symbol and accessible label. Persist a stable canonical LimitID scoped to the relevant user/profile/provider account and model applicability (not the display name or an array index). Display the pinned limit by default regardless of another limit's percent filled, shorter reset window or routine dynamic selection.
PIN OVERRIDE — The pinned choice MUST be temporarily displaced ONLY when another enforceable applicable limit is credibly predicted or known to cause a meaningful, SOONER BLOCK of requests/use than the pinned limit. Example: user pinned five-hour tokens with 5,000 tokens remaining, but a weekly token limit has only 3,000 tokens remaining and is going to be exhausted first; display the weekly limit as an EMERGENT CONSTRAINT. A cap at 80% consumption does NOT override a pinned cap merely because its percentage exceeds the pinned cap's 20%. Account for reset windows and actual effective usage/forecast evidence, not percentages alone. If another limit cannot be confidently assessed, do not pretend there is an emergency.
Do NOT silently delete the stored pin just because an emergency temporarily takes the circle. Surface a clear “Temporarily showing weekly limit; five-hour limit remains pinned” notice, optionally with a warning/urgent symbol. Once the emergency has genuinely cleared (or the competing cap resets/is no longer the next blocker), restore the user's original pinned display automatically if eligible. If user explicitly unpins, the limit is permanently removed/unavailable, their scope changes or its permissions expire, fall back to default selection and explain why the saved preference is not currently active.
A pinned advisory/warning budget MAY be shown as a selected favourite but it MUST NOT supersede an actual imminent HARD cap capable of blocking requests. Multiple simultaneous candidates must be prioritised deterministically; no flickering/ring thrashing on every small telemetry update. Pin does not authorise external usage or change hard-quota enforcement.

### COMPANION-B0029 · companion lines 212–214

SPUI-145 — TIME-TO-BLOCK EVIDENCE AND TOOLTIP
For comparable resource units, an actual lower remaining count can show which limit is encountered first under equivalent continued use. Where the system models projected exhaustion over time (especially across different units), it MUST use observable usage rate, active configured reset/window duration and request-specific measured or bounded costs where available, and label forecasts as estimated. Do not invent numerical thresholds, predict exact timing without sufficient data, or claim a remote provider allowance is known when it is not. The precise default urgency threshold/forecast confidence needed for “about to block” is STILL AN OPEN IMPLEMENTATION CHOICE; it must be explicitly decided before final hard-coded emergency-transition acceptance. Known imminent rejection of the current/next eligible request is sufficient to override a pin regardless of the exact warning horizon.
On hover/focus/tap, the usage indicator must reveal which active LIMIT is selected, the scope (provider-wide/model-specific), resource unit, window, actual remaining, current use/limit and reset if known, measurement source and freshness, PINNED status and, when temporarily overridden, the PINNED target plus reason the emergency blocker is currently displayed. Provide access to a full list of other applicable limits and their source/accuracy. Do not disguise usage/context ring identities or put the reasoning effort % in this tooltip as a usage gauge.

### COMPANION-B0030 · companion lines 217–219

SPUI-146 — ACCEPTANCE
Independently test in actual Spaces/Home/Dulche product: (1) five-hour at 20% vs weekly 80% in comparable units but with MORE remaining weekly — choose five-hour by window length; (2) five-hour 5k tokens remaining vs weekly 3k — choose weekly as the tighter blocker; (3) pin five-hour and verify normal selection sticks despite weekly's greater percentage; (4) pin five-hour but a weekly limit is actually the next predicted/known blocking constraint — temporarily surface weekly with override explanation and retain stored pin; (5) clear emergency/reset and restore pinned five-hour automatically; (6) mixed provider/model cap scopes and hard/advisory flags; (7) incompatible units tokens/messages/time without bogus direct numerical comparisons; (8) telemetry unavailable/local-model hidden usage and M rules; (9) disappearance of a pinned limit, revoked account, model/provider switch, restart and persistence; (10) retry/fallback/tracking freshness and accessibility reduced-motion/keyboard/touch tooltip. No tests marked passed without actual evidence.
END — 13 OCT 2026 PRIORITY/PINNING AMENDMENT.

### COMPANION-B0031 · companion lines 222–223

13 OCTOBER 2026 — OWNER-APPROVED STUDY SPACE UI SPECIFICATION — NORMATIVE LINKAGE
Within the 9to1 Spaces application, the full “9to1 V2 — Study Space Detailed Interface Specification (13 Oct 2026)” is an OWNER-APPROVED companion requirement set, incorporated into this Spaces working specification by reference: https://docs.google.com/document/d/17Q_KpQjNgjEAtdVq9HwsnMYveFn6zfIbZBl3es5bptg/edit . It adds STUDY-UI-201–235 and STUDY-AC-201–219 to the existing V2 LE-08 Study state/recommendations, LE-09 Revision Bank, LE-10 Cards, LE-11 Planner Burrows, LE-12 APIs and existing CUI/theming/privacy gates. Required experiences: Study Home, detailed Subject Workspace, Study Session with Learn/Practise/Test/Review mode switch and visible My Sources Only / Wider Knowledge policy, improved evidence-based RAG progress with separate self-confidence, Revise These (three topics total, reason shown), more usable Revision Bank previews/search/create/canonical ownership, Planner scheduled review, real subject-adapted question/input/marking, accessibility and real tests. These are NOT merely proposals, but exact layout, marking weights and similar unresolved details remain open in the document. Nothing is claimed implemented or verified. Study AND Spaces remain WIP; any later owner-approved changes control their affected scope.

### COMPANION-B0032 · companion lines 228–229

13 OCTOBER 2026 — TASKS SPACE SIDEBAR SCOPE, TASK TILES, STATUS BADGES AND QUICK ACTIONS
STATUS: OWNER-CONFIRMED DIRECTION FOR TASKS UI, WITH CLEARLY MARKED OPEN DECISIONS. This supplements the canonical Spaces UI, CUI Professional/theme specifications and the existing Agentic Update Task/Run/dashboard/widget/verification contracts. This is a DESIGN SPECIFICATION, NOT evidence of implementation or passing tests. The owner supplied visual references: the T3 Code task list (large, metadata-rich task tiles) and a ChatGPT sidebar with priority sections. These illustrate task-list density, grouping and selection; DO NOT copy external branding or infer actual product functionality from screenshots. The general 9to1 CUI/Professional/Glow design is already specified elsewhere; do not create a competing Tasks theme.
