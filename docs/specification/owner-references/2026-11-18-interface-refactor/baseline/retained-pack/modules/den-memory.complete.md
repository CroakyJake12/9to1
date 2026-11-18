# Den, memory and knowledge banks

Module ID: `den-memory` · 9to1 Development Specification v3

## Prerequisites

- **Privacy, data boundaries and context isolation** — Privacy scopes, consent, local/remote boundaries, source provenance, retention and revocation.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.
- **Dulche runtime, requests and execution services** — Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.

## Overview

Den stores persistent assistant/context knowledge through canonical scoped records and brokered access. Memory policy, explicit knowledge sources and background learning are independently controlled and inspectable.

## Layouts & visible states

- **V3-DEN-MEMORY-LAY-01** — Expose the exact memory frequency choices Never, Sometimes, Often and Always with clear effective scope and ownership. Background Learning is a separate opt-in.
- **V3-DEN-MEMORY-LAY-02** — Show relevant saved memories/knowledge-bank sources with inspect, correct, delete and provenance controls; do not imply inaccessible hidden content was used.

## Features & behaviour

- **V3-DEN-MEMORY-FEA-01** — Preserve source-defined versioned manifests, journals, namespaces and encrypted data contracts. Model references must not duplicate model weights.
- **V3-DEN-MEMORY-FEA-02** — Never automatically create or reinforce durable memory from Temporary chats. Whether a historical unresolved case permits reading existing memories must not be silently decided by a decorative toggle.
- **V3-DEN-MEMORY-FEA-03** — Frequency changes influence future memory behaviour; they do not delete existing records. Learning enablement does not grant external disclosure or unrelated app access.
- **V3-DEN-MEMORY-FEA-04** — Keep personal, Space, Assistant, team and organisation memory boundaries with scoped retrieval and relevant context selection.

## Canonical entities & persistence

- **V3-DEN-MEMORY-ENT-01** — Maintain record IDs, versions, ownership, provenance, timestamps, permitted scopes and correction/deletion history. Cache invalidation and indexes honour deletions and revocation.
- **V3-DEN-MEMORY-ENT-02** — Distinguish canonical memory records from ephemeral request context, retrieval snippets and learned summaries.

## Permissions & recovery

- **V3-DEN-MEMORY-PER-01** — All reads/writes go through the authorised broker; imported content cannot inject permission changes or private-state rules.
- **V3-DEN-MEMORY-PER-02** — Surface inaccessible, stale, conflicting and failed records with safe recovery rather than silently using unverified context.

## Acceptance checks

- **V3-DEN-MEMORY-ACC-01** — Exercise memory frequency transitions, temporary-chat no-write, explicit correction/deletion and cross-scope denial.
- **V3-DEN-MEMORY-ACC-02** — Verify an Assistant answer’s source receipt reflects actually accessed authorised memory and no hidden private reasoning.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

DEN-PRIV-001, DEN-PRIV-002, DEN-PRIV-003, DEN-PRIV-004, DEN-PRIV-005, DEN-PRIV-006, DEN-PRIV-007, DEN-PRIV-008

## Technical reading map

- Den, memory and knowledge banks — Dulche Den; Model catalogue (part 01) — `technical/den-memory-01.md` (1,984 words; V2-B2084, V2-B2085, V2-B2086…).
- Den, memory and knowledge banks — Background Learning; Eligible learning classes (part 02) — `technical/den-memory-02.md` (966 words; V2-B2108).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.


# Retained technical requirements

# Den, memory and knowledge banks — Dulche Den; Model catalogue (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2084 · v2 lines 11865–11867

Dulche Den
Purpose
Dulche Den is Dulche's persistent, cross-harness storage standard. It allows Dulche state to remain available across compatible apps and harnesses rather than being tied to one running endpoint or one application session.

### V2-B2085 · v2 lines 11870–11871

Model catalogue
A user's Den can maintain their model catalogue using stable model IDs alongside user-facing names. IDs provide unambiguous model references; names are intended for human-facing use and may be duplicated, which is why name lookup returns a list rather than assuming a single match.

### V2-B2086 · v2 lines 11874–11875

Cross-app state
Den is also intended to provide persistent storage for cross-app context, agents and related Dulche state so compatible 9to1 surfaces can share the same underlying information instead of maintaining isolated copies.

### V2-B2087 · v2 lines 11878–11888

Den privacy, encryption and consent — mandatory security contract
DEN-PRIV-001 — Encryption: Den personal data, including memory, context, conversations, embeddings, indexes, journals, attachments, backups and snapshots, MUST use authenticated encryption at rest. Keys MUST be held in an OS-backed secure key store or equivalent, never hardcoded or logged. Exports MUST be encrypted by default; authorised plaintext export requires explicit informed confirmation.
DEN-PRIV-002 — Opt-in continuity: Cross-app, cross-Agent, cross-harness, cross-device and cross-provider access to Den personal context MUST be disabled by default. Each additional scope requires explicit, granular, informed user consent. Installing an app, signing in, selecting a model or enabling Den persistence MUST NOT silently enable sharing.
DEN-PRIV-003 — App permissions: Every consuming app, Agent, Assistant, Plugin, MCP, connector or tool MUST receive explicit, scoped user authorisation to request personal Den context. Grants MUST distinguish read, write, create-memory, cross-app reuse and share/export as relevant; show recipient, purpose, scope and expiry; support inspection, revocation and deletion. Apps cannot acquire broader privileges through another app or an AI model.
DEN-PRIV-004 — Model-only semantic access: Personal Den content may be disclosed only as minimum-necessary context directly to the explicitly authorised model inference request through the trusted Dulche broker, and NOT as raw data to other application APIs, Plugins, MCPs, tools, telemetry, analytics, arbitrary app code or external services. Broker-side permission and data-minimisation checks MUST enforce this boundary. Model access is not a general permission to export data. Cloud inference requires separate explicit consent describing the third-party processor; no covert fallback from local to cloud.
DEN-PRIV-005 — No onward disclosure: Models/Agents MUST be instructed not to quote, forward, paraphrase, embed in tool arguments or otherwise relay protected Den content to other app components, tools, APIs, third parties, other users or recipients without specific user authorisation. Crucially, instructions alone are insufficient: Dulche MUST enforce tool-input/output and action-argument information-flow restrictions and deny unapproved onward transmission. Sensitive context must not be exposed in generated UI, logs, traces or diagnostics by default.
DEN-PRIV-006 — User control: Users MUST be able to see which apps/models can access which Den scopes, view why data was used, grant and revoke access, selectively forget/delete entries and disable cross-app continuity without disabling all local functionality. Consent must be separate from a generic terms-of-service agreement; changes to access scope require renewed consent.
DEN-PRIV-007 — Lifecycle and security: Encrypt data in transit where applicable, minimise plaintext lifetime in memory, protect against prompt injection and confused-deputy privilege escalation, segregate personal/team/app namespaces, define retention and secure deletion, and ensure exports/imports, synchronisation, backups and recovery preserve permissions. Protect secrets independently from Den memory.
DEN-PRIV-008 — Acceptance: Tests MUST verify default-deny cross-app isolation, per-app prompts and revocation, encrypted on-disk files/backups, prohibited tool/API exfiltration, prompt-injection resistance, cloud-provider opt-in, permission-aware retrieval, and correct behaviour when permissions change mid-run. Failing any security gate blocks acceptance. This privacy contract takes precedence over any earlier Den wording that could imply automatic cross-app availability.
Storage structure and portability
Den is a directory-based persistent store with a versioned den.json manifest, structured records, a transaction journal and separately stored or referenced large objects. Portable records use UTF-8 JSON; local databases and indexes may accelerate access but must not be the only readable copy or create an incompatible private format. Exports contain authorised records and explicitly selected attachments. Reference model weights and external Files documents by identity/content hash rather than copying them by default. No special bundle extension is mandated.

### V2-B2088 · v2 lines 11891–11891

The manifest identifies DenID, FormatVersion, SchemaVersion, minimum reader/writer versions, namespaces, storage locations and required capabilities. Validate manifests, paths, sizes and hashes before import. Importing never executes code or automatically enables agents, tools, connections or permissions.

### V2-B2089 · v2 lines 11894–11895

Identity, namespaces and model records
Every model, agent, conversation, context object, workflow and request has a stable ID independent of name/path. Separate personal, team, organisation and app namespaces; device paths and hardware settings stay device-local. Shared namespaces are opt-in.

### V2-B2090 · v2 lines 11898–11898

Model records contain logical ID, display name, aliases, provider/source, immutable revision/artifact hash, family, format, quantisation, modalities/capabilities, context/output limits, ordered reasoning levels, configuration overrides, installation references, timestamps, provenance and replacement history. Parameters, active MoE parameters, storage and hardware metrics use Runtime's availability rules. Local paths are not portable identities.

### V2-B2091 · v2 lines 11901–11901

ID lookup is unambiguous in its namespace. Name/property lookup returns all matches, never silently chooses a duplicate. Aliases are explicit namespace-scoped references. Model replacement may retain logical ID/name but records changed artifact/provider identity, retained/reset settings and rollback history.

### V2-B2092 · v2 lines 11904–11905

Sessions, context and checkpoints
Den persists messages/revisions, authorised context, agent definitions, requests/results, tool records, approvals and checkpoints as distinct record types. Runtime executes work; Den stores information needed to inspect or resume it.

### V2-B2093 · v2 lines 11908–11908

Portability covers supported messages, context, configuration, definitions and history, not arbitrary backend caches, GPU state or undisclosed internal model state. Checkpoints declare backend/model compatibility. Distinguish a continuation attempt from exact resumption and never automatically repeat completed external actions.

### V2-B2094 · v2 lines 11911–11911

Context records identify source, owning app, timestamps, author/inference provenance, access scope and retention. Distinguish user-authored, imported and inferred context. Sharing a conversation does not automatically share its referenced sources, secrets, attachments or private inferences.

### V2-B2095 · v2 lines 11914–11942

Persistent Memory
Persistent Memory is a core Dulche Den subsystem for durable cross-session information about the user, recurring entities, preferences, decisions, projects and other scoped context that Dulche should be able to retrieve in future conversations or authorised surfaces. It is separate from Background Learning.
Memory is not one opaque prompt file. A persistent MemoryEntry should carry stable identity, content or structured value, subject/entity, memory type/category, scope, source/provenance, confidence where applicable, created/updated state, supersession/correction relationships, retention state and user-edited/locked state where supported.
Scopes
Memory can be scoped so unrelated Agents/apps do not receive unnecessary context. Supported scopes should include global/user memory plus narrower project, Agent, app/surface or other explicit contexts where appropriate. Retrieval MUST respect both scope and permission.
Creation and retrieval
Dulche may create or update memories from eligible conversations/context according to the user's Memory Frequency and memory policy. Retrieval is relevance-based: only memories useful to the active request/context should be injected, rather than the complete memory store.
Explicit user corrections supersede contradictory earlier memories. Den MUST retain enough provenance/revision information to distinguish an explicit user statement from an inference and to avoid repeatedly resurfacing a memory that has been corrected, superseded or deleted.
Memory Frequency
Settings MUST expose a Memory Frequency preference with exactly four user-facing levels: Never, Sometimes, Often and Always. The selected level controls how readily Dulche autonomously creates, reinforces or updates Persistent Memory from eligible non-temporary interactions.
* Never: Dulche MUST NOT autonomously create, reinforce or update Persistent Memory from ordinary interactions. Direct user-invoked memory-management actions are separate explicit operations.
* Sometimes: use a conservative threshold and retain only clearly durable, repeatedly useful or high-value information.
* Often: use a lower threshold and remember useful preferences, decisions, entities and ongoing context more readily when they are likely to help future responses.
* Always: evaluate every eligible interaction for durable memory and normally retain information that satisfies the memory policy, while still deduplicating noise, respecting scope/retention rules and avoiding ineligible information.
Changing Memory Frequency affects future autonomous memory behaviour; it MUST NOT silently delete existing memories. The setting is independent of Background Learning.
Temporary chats in Spaces
Spaces can provide Temporary Chats. Persistent Memory creation is disabled inside a Temporary Chat regardless of the user's Memory Frequency. While a chat is temporary, Dulche MUST NOT autonomously create, update, reinforce, merge or promote Persistent Memory from that conversation.
Any explicit action that would save new Persistent Memory from a Temporary Chat must be unavailable or clearly report that memory creation is disabled for temporary chats. Temporary-chat memory-write suppression MUST NOT be bypassed by an Agent, plugin, Skill, tool or Background Learning pipeline.
This rule governs memory creation/writes. Whether existing authorised memories are retrieved into a temporary chat is a separate context/privacy policy and MUST NOT be inferred from the memory-write prohibition.
Management and lifecycle
Users must be able to inspect, search, edit/correct, supersede and delete persistent memories through the appropriate settings/Den surfaces. Deletion and retention use Den's normal lifecycle, sync and purge contracts. Memory export/import must preserve scopes, provenance and deletion/supersession semantics rather than flattening entries into plain prose.
Persistent Memory acceptance requirements
* Persistent Memory exists as a distinct Den record system and is not substituted by Background Learning, session context or Agent-only memory.
* Memory retrieval is relevance-, scope- and permission-aware.
* Never/Sometimes/Often/Always control autonomous memory creation/reinforcement with the semantics above.
* Temporary Chats in Spaces cannot create, update or reinforce Persistent Memory regardless of frequency or calling surface.
* Corrections/supersession are durable and user-manageable, and explicit user statements remain distinguishable from inference.
Permissions and secrets
Authorise access per principal, namespace and object/action. Reading context, modifying an agent, executing tools and administering Den are separate permissions enforced below the UI. Revocation applies to future reads/searches/executions; it does not erase copies already disclosed.

### V2-B2096 · v2 lines 11945–11945

Store credential references, not plaintext API keys, passwords, private keys or tokens in records, exports or logs. Resolve references through secure credential storage on the relevant device/service. Protect sensitive storage and transport, document key ownership/recovery and redact sensitive log payloads without falsifying whether an action occurred.

### V2-B2097 · v2 lines 11948–11949

Concurrent writes and recovery
Use atomic transactions or crash-safe journalled updates. Mutable objects have revisions; updates supply expected revisions and return Conflict instead of overwriting newer data. Support concurrent readers and coordinated writes with bounded locking. Operation IDs make safely retryable writes idempotent.

### V2-B2098 · v2 lines 11952–11952

Validate before commit, preserve the previous valid record until commit and recover/quarantine incomplete writes after crashes. Export consistent snapshots. Caches and indexes are rebuildable, not the sole authority.

### V2-B2099 · v2 lines 11955–11956

Synchronisation and conflicts
Cross-device/team sync is optional and namespace-scoped. Track IDs, revisions, origin device and deletion tombstones. Offline changes wait for an authorised sync target. Merge compatible disjoint edits; preserve conflicting versions for review. Wall-clock timestamps alone never choose the winner.

### V2-B2100 · v2 lines 11959–11959

Concurrent conversation edits create explicit revisions or alternate continuations. Sync does not execute stored requests or replay tools. Device-local paths, secrets and hardware settings are not portable values. Recheck permissions on reconnection and record deletion/restoration/revocation explicitly.

### V2-B2101 · v2 lines 11962–11963

Lifecycle and storage policy
Support create/open, inspect/search/update, export/import, backup/restore, soft-delete and explicit permanent purge. Retention is configurable and visible by namespace/category; do not assume indefinite retention or silently discard work under storage pressure. Soft-delete preserves recoverable revisions until expiry. Purge covers managed replicas/indexes/derived records under policy, with explicit limits for offline copies and external backups.

### V2-B2102 · v2 lines 11966–11966

Large files use content-addressed blobs or authorised external references. Reference counting prevents deletion while another owner needs an artifact. Distinguish quotas for metadata, logs, caches, attachments and model weights. Den metadata does not make a temporary Pull permanent: Install or an explicit pin creates persistent ownership.

### V2-B2103 · v2 lines 11969–11970

Compatibility and migration
Publish versioned schemas, examples and conformance tests. Compatible writers preserve unknown optional fields; unknown security-critical fields or required capabilities block unsafe writes. Validate, back up and transactionally migrate schemas. Unsupported newer versions may open read-only when safe, never discard fields to force a downgrade.

### V2-B2104 · v2 lines 11973–11973

Round-trip null for empty metrics and Na for provider-unavailable metrics, serialised as "Na"; measured zero stays 0. Permission-filter search/index results and include revision/provenance.

### V2-B2105 · v2 lines 11976–11977

Supporting APIs and acceptance
Preserve Dulche.Den.GetModelByID(id) and Dulche.Den.ListModelsByName(name). Add compatible create/open, scoped search, revision-checked update, export/import, sync, restore and purge APIs with documented signatures and failures.

### V2-B2106 · v2 lines 11980–11980

Acceptance covers cross-harness round-tripping, duplicate names, concurrent edits, offline conflicts, crash recovery, revocation, safe import, secret-free export, model replacement/rollback, retention/restore and migrations without data loss.


# Den, memory and knowledge banks — Background Learning; Eligible learning classes (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2108 · v2 lines 12033–12067

Background Learning
Background Learning is an explicitly opt-in Dulche personalisation subsystem. It is separate from Persistent Memory. Enabling Background Learning allows Dulche to learn useful patterns, interests and reusable knowledge from eligible 9to1 activity over time; it does not itself enable or disable Persistent Memory and it does not imply continuous fine-tuning of the underlying base model.
Eligible learning classes
Background Learning can maintain three broad classes of learned information:
* 9to1 usage and project context: which authorised surfaces the user uses most, recurring workflows, recently or frequently active projects, apps commonly used together, important project terminology/entities and other usage patterns that improve contextual relevance.
* Interests and topic Knowledge Banks: recurring subjects the user appears to care about, together with reusable factual/reference material that can improve later answers and remain available offline. For example, repeated questions about cars may lead to an Interest record for cars and a local Cars Knowledge Bank containing useful terminology, concepts and sourced reference material.
* Other relevant personalisation information: additional permitted information that materially improves responses, such as preferred terminology, recurring goals, explanation preferences, established project conventions, frequently used tools, entity relationships and corrections that should influence future response behaviour.
Usage and project learning
The usage-learning layer SHOULD retain useful semantic state rather than an indiscriminate permanent clickstream. For example, it may learn that Dev and Stack are frequently used for a particular project or that a project is currently active. Raw activity used to derive these signals should be retained only to the extent required by the configured retention/privacy model.
Learned project context can help Dulche resolve ambiguous references, rank context candidates, suggest likely relevant surfaces and prepare authorised Agents with useful project context. It MUST remain scope-aware: project-specific learning does not automatically become unrelated global context.
Interest Knowledge Banks
Background Learning can create and maintain topic-specific KnowledgeBank entities. A KnowledgeBank can contain a stable ID, topic/title, entries, source/provenance metadata, confidence, freshness, creation/update state, storage/sync policy and enabled/disabled state. Knowledge Banks may be created automatically from recurring interests or explicitly managed by the user.
Knowledge Bank entries may include reusable summaries, terminology, relationships, reference facts and other material previously obtained from authorised local sources, user interactions or trusted retrieval. Factual entries MUST retain enough provenance/freshness information for Dulche to distinguish durable knowledge from information that may need refreshing.
Knowledge Banks are not the same as Persistent Memory. For example, 'the user is interested in cars' is personalisation/user context; a structured offline bank explaining engines, EV charging and vehicle terminology is subject knowledge. Dulche may use both when relevant, but they remain separately inspectable and manageable.
Structured learned state
Background Learning MUST store structured learned records rather than relying on one opaque free-form profile. Records should carry stable identity, category/type, subject/entity, learned value, provenance, confidence, creation/update state, last reinforcement/use where useful, freshness/expiry semantics and user-edited/locked state where applicable.
Explicit user corrections outrank contradictory inferred learning. If a user corrects an inferred preference, project status or interest, the system MUST revise/supersede the stale inference instead of allowing both to compete indefinitely as equally valid facts.
Selective retrieval
The complete learning store MUST NOT be injected into every request. Dulche must retrieve only learning context relevant to the active request, Agent, app and permission scope. Retrieval should use current entities/topics/project context to select the smallest useful set of records and Knowledge Bank material.
Privacy and cloud boundaries
Background Learning data is private by default and exists to personalise the user's 9to1 AI experience. It MUST NOT be sold, used to build advertising profiles or indiscriminately uploaded to third parties. Enabling Background Learning does not itself grant permission to transmit learned information to a cloud model/provider.
If a Dulche request uses a remote/cloud provider, disclosure of any learned context remains subject to the normal provider, privacy, permission and data-minimisation rules for that request. The implementation MUST distinguish permission to learn/store information from permission to disclose that information externally.
Any optional cross-device synchronisation of Background Learning state must be user-controlled and protected by the applicable authenticated/encrypted 9to1 sync/storage architecture. The feature MUST remain useful locally and must not require insecure cloud upload merely to personalise responses.
User controls
Users must be able to enable/disable Background Learning, pause new learning, inspect learned information, inspect and manage topic Knowledge Banks, correct inaccurate learning, delete individual learned records or a complete topic bank, clear learned categories and configure which apps/projects/categories may contribute where supported. Disabling learning stops creation/reinforcement of new learned state but MUST NOT silently delete existing Persistent Memory or unrelated user data.
Relationship to Persistent Memory
Persistent Memory and Background Learning are independent. Persistent Memory stores durable cross-session memories under the Memory policy; Background Learning is the opt-in process that derives usage patterns, interests, Knowledge Banks and other personalisation state. Neither feature may silently enable, disable, clear or substitute for the other.
Background Learning may identify information that would be useful as a durable memory, but any conversion into Persistent Memory must use the Memory subsystem and preserve that the source was an inference rather than an explicit user statement. Explicit Memory corrections remain authoritative over contradictory Background Learning inferences.
Background Learning acceptance requirements
* Background Learning is off until the user opts in and remains independently configurable from Persistent Memory.
* Eligible 9to1 usage/project patterns, interests/Knowledge Banks and other useful personalisation information can be learned as structured, inspectable state.
* Topic Knowledge Banks can provide relevant offline reference material and retain provenance/freshness sufficient to avoid presenting stale inferred facts as timeless truth.
* Only relevant learning context is retrieved for a request; the full learning store is not injected indiscriminately.
* Learned data is not sold, used for advertising profiles or uploaded to cloud providers merely because learning is enabled.
* Users can inspect, correct, scope and delete learning independently of Persistent Memory.
