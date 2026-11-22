# 10. Decisions & adoption

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.quf4eijpf3g

<a id="h.3nmg7m154zyc"></a>

# Decisions, source history and repository adoption

<a id="h.ueao7q554u44"></a>

## 10.1 Resolved carry-over conflicts

The following resolutions are part of this baseline. They follow the final active [Atlas](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html) refinements and the owner's direction to build a replacement specification. They are not requests for workers to choose again between competing old documents.

RES-001 — Home belongs to Haven, but Dashboard and New Tab remain two distinct canonical surfaces. Preserve the full Home navigation, common CUI styles, reusable widget types and cross-app source references. The final Atlas v3.3 Dashboard-to-New-Tab render override is superseded and must be removed for Atlas v4; restore the distinct original Dashboard briefing and retain the modern final New Tab composition. Do not share a single LayoutID or route alias between them.

RES-002 — Haven owns hosted chrome. The final tab/address/tool composition supersedes earlier duplicated application headers and the older hosted “three tiers” wording. Standalone composition still supplies its required controls through the same app implementation.

RES-003 — The model picker has a compact dropdown and a separate expanding settings view. The compact view keeps Main, Voice and Vision, visible Reasoning, short named model rows and distinct Context, Model and Assistant/Specialist disclosures. More settings exposes detailed configuration over the same selections and shared context draft. Earlier All/Local/Cloud/Groups dropdown definitions are historical. Context changes remain pending until explicit Apply and reload; neither the preview's saved value nor a legacy immediate preference setter proves runtime application.

RES-004 — The active contextual editor access labels are Read-only and Take actions. Older Read-only/Write wording does not require a competing access mode. Human Edit/View, layout locking, source grants and approval for a specific effect remain separate.

RES-005 — Ordinary action controls use the selected shared accent family. Earlier fixed app colours and multi-hue Primary/Secondary/Tertiary instructions cannot override the final action palette. Authored media, icons, semantic status colours and custom theme data retain their separate boundaries.

RES-006 — Use the corrected active theme materials and current built-in theme identities. Reference image pages guide design and original assets; they do not install those screenshots, register every named style as a built-in or carry dated worker implementation schedules into the product.

RES-007 — Boards uses the compact shared chrome, its native page model and the final New page footer action. Earlier generic Add wording is not a reason to rename the current control or rebuild the page engine. The Add toolbar and New page footer have their distinct current purposes.

RES-008 — This Product Specification and its adopted interaction contracts are the source of product requirements. Preserve compatible implementation facts, source provenance, technical schemas, existing capabilities and historical evidence through explicit reconciliation. Documentation that conflicts with the approved contract is not a second authority and must be updated or removed according to AUTH-011.

<a id="h.34pezxowra69"></a>

## 10.2 Decision register and resolved implementation policies

Each entry states whether its policy is resolved in this expanded contract or remains bounded and open. Resolved entries are implementation requirements; they do not require another approval merely because they originally appeared in a decision list. An open item blocks only the affected new policy or migration, while unrelated work continues.

DEC-001 — Custom theme composition. The final action palette requires one hue for ordinary action roles, while Create Theme exposes independently authored Primary/Secondary/Tertiary materials. Decide how inherited/custom gradients, patterns, transparency and semantic colours compose with a selected shared accent. Current contract: use the shared accent for ordinary chrome, preserve saved custom data and builder controls, and do not destructively rewrite authored materials. A solution must show preview/cancel, inheritance and migration results.

DEC-002 — Reasoning binding (Resolved). Preserve the Atlas requested control at 0–100 in ten-point steps and store RequestedEffort separately from EffectiveEffort. A versioned adapter-advertised mapping to supported stable LevelIDs is authoritative. When only a semantically ordered list of supported LevelIDs is provided, use the deterministic mapping in RUN-103; a count, lexical ordering or enum integer alone cannot establish semantic order. With no usable mapping, reasoning is unavailable; one advertised level is fixed. Requested zero never invents Off. Expose the effective level and test every requested slider position against actual adapter arguments. The fallback and tie rule are explicit new engineering choices; they are not a claim that the pinned legacy code already implements them.

DEC-003 — Context scope and application (Resolved). The picker and host context popup share one requested context policy and pending draft for the current conversation/request scope. Saved conversations persist that policy and per-role selections; Temporary conversations retain them only within their permitted lifetime. Home defaults initialise new scopes without mutating active conversations. Actual allocation, capacity and usage belong to each applicable runtime session. Non-token routes declare NotApplicable. Apply validates a positive whole-number request, snapshots affected active sessions and revisions, and commits only after actual acknowledgments; unloaded roles validate on admission rather than loading automatically. Failed or partial application preserves the attempt and reports per-session state. RUN-104/105 define this adopted production scope and compatibility migration; prototype storage and the inspected global setter are not evidence that it already ships.

DEC-004 — Temporary-chat personalisation (Resolved by owner, 22 November 2026). Expose Temporary as a toggle inside ordinary Chat, not a separate destination or response mode. Its associated ChatGPT-style Personalised/Unpersonalised setting lives in the common Options panel and is chosen before the first message. Personalised permits eligible existing memory, custom instructions and plugins; Unpersonalised excludes them. Neither mode creates or updates durable memory while temporary, and the choice cannot change once the conversation starts. Account/workspace policy and individual grants always constrain effective access. Use the last eligible choice for the account/workspace; absent a saved choice, initialise Personalised only where policy allows, clearly showing the state before sending. Before first send, the Temporary toggle can change without conversion. After a Temporary conversation starts, switching it off requests the same explicit Save conversion to regular locally durable history; cancel or failure retains Temporary. A committed regular chat cannot be retroactively made Temporary. Successful saving applies normal conversation settings. This adopts the interaction pattern, not OpenAI retention periods or service-specific processing promises. SPC-084–086 define controls and acceptance.

DEC-005 — Study evidence policy. The four progress statuses and attributed evidence are specified; numerical proficiency thresholds, evidence weighting and review intervals require a deliberate policy. Use deterministic, explainable recommendation ordering without claiming an invented score proves mastery. The policy must distinguish source evidence, an estimate and a user correction.

DEC-006 — Historical message editing (Resolved). User-message editing provides three distinct operations: Replace messages below supersedes downstream messages only in the current active branch/view while retaining recoverable history; Update context and new messages only records a prospective correction without rewriting existing answers; Edit in branch creates a new versioned branch and preserves the original. Changes retain MessageID, BranchID and TaskID provenance and notify affected in-flight work. Narrative history edits never reverse external effects or erase action receipts. Irreversible privacy erasure remains a separate scoped storage operation. This policy deliberately restates retained [spaces.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/spaces.complete.md), SPUI-183; SPC-030–039 define implementation and acceptance.

DEC-007 — Industrial and Metallic. Keep the registered Metallic theme identity. Industrial reference material does not establish a rename, alias or replacement; resolve its role before a theme migration or additional built-in is published.

DEC-008 — Dreaming and preference-driven reinforcement learning (Resolved for the defined feature). Dreaming is a required capability, optionally enabled in Settings, that uses a main model and review agent to practise scenarios while both the AI and computer are idle and performs actual reinforcement-learning updates to supported model weights or compatible trainable adapters. Eligible pairwise response preferences feed versioned datasets and reward learning that contributes to policy updates. The required implementation includes a verified local training path; support is model/backend-specific and never implies access to hosted inference-only weights or silent cloud training. Enabled policy defines data scope, idle/resource limits and automatic or manual candidate promotion. Independent control, checkpoints, held-out evaluation, serving validation and rollback are mandatory; increased training reward is not guaranteed improvement. LEARN-001–012 define the implementation and acceptance. The owner's 22 November 2026 Q4 decision confirms Train's initial fine-tuning, Dreaming, evaluation and checkpoint-management scope. General pretraining and arbitrary direct manual weight editing are deferred beyond the initial scope and require a separately approved contract; they are not prerequisites for Dreaming.

<a id="h.fhusii6cn7op"></a>

## 10.3 Engineering confirmations

CONF-001 — Before the V100/dual-inference experiment, declare its intended topology: coordinated agents, independent concurrent inference endpoints, or one workload distributed across devices. Include supported capabilities, protected workloads, controller survival, stop/recovery and the proof required for that topology. This is a prerequisite to a valid hardware result, not a claim that the target has already been achieved.

CONF-002 — Establish numerical performance budgets from actual measured workloads and resources. Record baseline, environment, tradeoff and the resulting accepted limit. Motion timings may use the Atlas's explicitly draft values as a reversible design starting point; they are not hardware performance targets and do not require a new owner decision for every routine tuning change.

CONF-003 — Inspect current CUI grammar, app command contracts, storage schemas and runtime protocols before a compatibility change. Restate the supported exact signature in implementation documentation and test the migration. Existing compatible methods do not need to be reinvented or reapproved merely because this product spec describes their behaviour at a higher level. A proposed replacement must be concrete before it displaces a functioning public contract.

CONF-004 — Source branches, model hosts, SDKs, environment variables, process ownership and resource guards are live operational context. Discover them from the receiving repository/environment. Do not substitute paths, PIDs, ports, quotas or dates copied from this document's historical references. Preserve valid current safeguards and other workers' changes.

<a id="h.62n9u1t9a0gs"></a>

## 10.4 Qwen's subsequent repository reference pass

ADOPT-001 — Adopt this [native Google Doc](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk) as the active specification location. Preserve the pinned editable Atlas v3.3 as input provenance; use the newly authored v4 as the owner's editable review candidate. The owner-returned v4.1 is the intended design input for the next pass, after its exact source, differences and unresolved decisions are captured and reconciled. Do not label the review candidate or an anticipated v4.1 as already accepted. Qwen's subsequent task is to reconcile repository references against the then-adopted specification and Atlas pair. This document does not claim that repository adoption has happened. Older Google Docs and the 18 November package remain recoverable reference snapshots; never reuse their original source hashes for modified content.

ADOPT-002 — Read the entire new document, its decisions and relevant Atlas refinements before changing references. Inventory authored Markdown and other active authority pointers, including root and nested AGENTS files, READMEs, workflow guides, starter prompts, handoffs, feature inventories, provenance/manifest files and generated exports. Distinguish authored guidance from donor documentation, licences, vendored/generated build output and historical evidence. Record exclusions; a keyword hit list is not a claim that every file has been reviewed.

ADOPT-003 — Classify each affected file and clause: current operating procedure; current product pointer; compatible implementation reference; historical evidence; obsolete authority/instruction; generated artifact; or genuine unresolved conflict. Read the whole affected context before changing it. Preserve legitimate operational safeguards, licence notices, source attribution and technical data-format compatibility. A global search-and-replace of version numbers, product names or backend names is not sufficient.

ADOPT-004 — Maintain a reconciliation ledger with path, original commit/blob or checksum, exact disputed claim, new requirement/Screen ID, classification, correction, disposition and validation. Keep original text recoverable through Git history or an exact archived snapshot and record its location. Retirement means clearly withdrawing active authority, not losing provenance. For old audits, add an as-of/superseded-status note or later follow-up rather than rewriting their original observations as if they were made today.

ADOPT-005 — Make the root entry concise and unambiguous: current specification URL/revision, Atlas path/hash, where to find current operational procedures, how conflicts are recorded and how to load the relevant domain contract. Nested instructions may add scoped operating details but must not declare a previous spec “sole authority”. Historical process instructions belong in dated records, with a clear statement that they are not the current execution contract.

ADOPT-006 — Start with known stale authority pointers in README.md, older sections of AGENTS.md, docs/development/workflow.md, local handoff files, nested app AGENTS files and source-precedence/start-here documents. These are a prioritised starting set, not an exhaustive current inventory. The known issue is contradictory authority and dated workflow wording; the worker must inspect current versions and preserve compatible safeguards rather than treating every old sentence as wrong.

ADOPT-007 — Export the replacement native document with all tabs, headings, tables and links into a new versioned repository snapshot. Capture the exact native document ID, revision, retrieval timestamp, Atlas source identity and code baseline. Generate Markdown/HTML, requirement indexes and screen mappings from that one captured source; do not maintain independent hand-edited “current” copies. The old builder is an implementation reference whose assumptions must be inspected, not a command to overwrite its old package blindly.

ADOPT-008 — Verify native/export parity: every tab and paragraph included; native tables and their cells retained; exact requirement and Screen IDs preserved; heading anchors and links resolve; no truncation, duplicate authority blocks or unresolved template placeholders; computed checksums describe the actual final bytes. Record the screen manifest comparison and the distinction between product surfaces and design-reference entries. Do not reuse an input checksum after editing output.

ADOPT-009 — Update active source-precedence, README/starter, manifest/provenance and relevant domain links in the same coherent adoption change. Identify the one active revision and the superseded historical packages. Leave any source that cannot be reconciled explicitly flagged with its exact unresolved clause and affected operation. Do not claim a completed repository-wide pass if only the priority files were reviewed.

ADOPT-010 — Perform the documentation pass under the receiving repository's actual source-custody and coordination rules. Keep code changes separate unless required to repair an affected generated-document pipeline or broken reference, and record those changes distinctly. Do not restart inference, change hardware topology, revive dated schedules, send messages, deploy or remove user work merely because historical instructions contain those actions. The current pass is documentation adoption.

ADOPT-011 — Report changed files, resolved conflicts, preserved historical locations, native/export revision parity, link/index/checksum results and remaining blockers. A clean adoption makes it clear what each worker should follow next. It does not mark the product implemented or its runtime/hardware verified.

<a id="h.ssh13n373or8"></a>

## 10.5 Ready-to-use task for Qwen

Reconcile the repository's outdated authority pointers against the active 9to1 Product Specification and the currently adopted editable Interface Atlas. Preserve global safety and operational instructions that remain compatible; correct misleading product claims, remove wholly superseded instruction files under the reviewed source workflow, and read the active decision register and relevant requirement sections before implementation.

Inventory and fully read affected active instructions. Prioritise root README and AGENTS, workflow and handoff documents, nested instructions, generated specification exports and provenance. Classify each disputed clause; record its exact original source and the governing new requirement or Atlas Screen ID. Preserve compatible implementation details, current source-custody/resource safeguards, licences, historical evidence, functioning extra capabilities and user data. Do not solve conflicts through mass deletion or indiscriminate replacement.

Create a new versioned native-source snapshot and generate its exports/indexes together. Update active pointers coherently, archive or clearly mark superseded authority, and verify all tabs, tables, requirements, screen IDs, links and checksums against the captured source. Keep exact originals recoverable. Report what was reviewed and changed, what is now authoritative and any bounded unresolved conflict. This pass updates documentation; it does not execute historical operational actions or claim implementation acceptance.

<a id="h.6s5ij2djoepp"></a>

## 10.6 Maintenance rules

MAINT-001 — A change to product behaviour updates the owning requirement, relevant Atlas state or explicit design-pending record, decision history and affected acceptance. Keep shared requirements in their owning chapter and reference them from apps. Avoid accumulating contradictory amendment blocks after the old text; replace or explicitly supersede the affected clause.

MAINT-002 — Keep generated exports tied to a captured native revision. If the native source changes before exports are refreshed, show the lag honestly and identify the active authoring revision. A saved document is not automatically a published repository snapshot.

MAINT-003 — Keep examples labelled. New mock data, person names, model names, quotas, devices, balance figures and sample documents remain fixtures until backed by the relevant source/service. An example can establish layout without establishing a price, capacity, integration or shipped feature.

MAINT-004 — Keep execution history out of the permanent product contract. Dated test reports, worker handoffs and machine observations link to requirements as evidence. They do not alter product authority or act as standing instructions to future workers.

Naming adoption — Qwen must update visible Tasks-Space labels to Actions and classify Study, Translate and Actions as Spaces-owned destinations/configurations. Preserve SpaceID, ConversationID, existing routes and legacy aliases. Do not globally rename ordinary task records, TaskRuns, Planner tasks or Present’s Actions graph. Update corresponding Atlas display labels/build notes deliberately; the pinned original remains a provenance snapshot.
