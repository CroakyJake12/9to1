# 4. Data, memory & trust

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.lz5gnfevfm8d

<a id="h.dg9u8qdcyhmy"></a>

# Data, memory and trust

Canonical identity, local durability, shared permission authority and explicit service receipts apply across every app. The clauses here state the contract directly; source notes identify reference material without incorporating it as another authority.

<a id="h.ona4u8t9e5l9"></a>

## 4.1 Artifacts, saving, export and recovery

DATA-001 — The owning app maintains canonical artifact identity and revision. Views, collection memberships, conversation links and embeds normally reference that artifact. Imported copy, fork, move, export and deletion have explicit different consequences. A fork receives a new identity and retains ancestry. Removing a reference or closing a tab does not delete its source.

DATA-002 — Expose actual draft, unsaved changes, saving locally, saved locally, sync pending, synced, failed, read-only and conflict states. Each state is justified by the appropriate store or provider. A rendered preview, queued sync or successful UI event is insufficient evidence of saving. Preserve local drafts/checkpoints across interruption, and show their recovery status without silently replacing newer work.

DATA-003 — Mutations identify the target and expected revision and commit through explicit transactions. Manual commands, context-menu actions, shortcuts and AI-applied changes use the same owning operation. Valid undo/redo applies to that operation's supported transaction scope. A stale revision refreshes or opens a conflict; it never overwrites concurrent work silently. View changes and live presence do not create authored-content revisions.

DATA-004 — An AI proposal records its author/run, target, expected revision, proposed semantic change and review status. Apply rechecks permission and revision before an undoable commit where the app supports it. Reject leaves the source unchanged. Data requires explicit approval for every AI mutation; no broad Take actions setting or collaborator invitation removes that exception.

DATA-005 — Export records the source revision, format, selected range/options, destination, status and resulting bytes. The native source and exported file remain distinct. Download is available only for the confirmed output. If export fails, preserve the native artifact and offer retry/save-copy from the intended revision. Retry cannot silently regenerate unrelated completed content or substitute the document's later revision.

DATA-006 — Do not add arbitrary global content caps merely to simplify rendering or saving. Expose genuine format, security, quota and device constraints before destructive work. No silent truncation is allowed. Large documents use incremental parsing/updates, virtualised views, bounded caches and cancellable streamed operations while retaining searchable and exportable content outside the current viewport.

DATA-007 — Indexes, thumbnails, previews and caches are derived data. Cleanup or repair must not discard the only canonical file, Den record, unsaved change, authorised offline copy or required recovery checkpoint. A cleanup preview identifies the category and consequence. Rebuildable data can be regenerated without changing artifact identity, permissions or content.

DATA-008 — Home Library is an index and organisational view, not a second storage authority. Files preserves provider/resource identity, FileID, revision and operation receipts. A cached listing confers no access; preview, open, export, move and deletion recheck the selected resource. Batch operations expose each item's actual outcome. Collision options identify the affected target and consequence. Membership removal is distinct from file deletion.

Acceptance: crash during save, recover the draft, conflict with another revision and preserve both. Open the same artifact through multiple collections, remove one membership and retain the source. Fail export after native save; retry the correct revision. Clear caches, then reopen a large artifact without truncation or lost identity. Check that every Ready download corresponds to actual complete bytes.

[Atlas anchors: files, files-grid, files-context, files-organisation, home-library, states, boards-recovery, spaces-generated-files, spaces-export-recovery, collaboration-review, data-approval, settings-storage.](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/design/9to1_Interface_Atlas_v3.3_Editable.html)

<a id="h.ho9u96jerpz8"></a>

### 4.1.1 Canonical records, revisions and transactions

STORE-001 — An ArtifactRecord identifies NamespaceID, ArtifactID, OwnerAppID, native type, schema version, current committed RevisionID, ownership/access reference, lifecycle state and supported locations. A location is a storage/provider reference, not a replacement identity. Each immutable revision records its parent revision or merge parents, author/operation provenance, content reference and creation time. A working DraftID records its base revision, unsaved edits and recovery checkpoint. View state such as zoom, selected tab and scroll position is stored separately and does not produce authored-content revisions. Revision equality is an identity comparison; a later timestamp alone cannot prove that one revision contains another.

The owning app resolves these records for hosted, standalone and AI operations. A file imported independently receives a new artifact identity with source provenance. A reference retains the original identity and checks its current grants. A fork creates a new identity and ancestry link. A provider without stable object IDs requires an explicit mapping and relocation policy; a path rename must not silently create an unrelated duplicate while an old reference remains live.

STORE-002 — A content mutation validates the typed change, target ownership, expected revision, permission and format invariants within the owning transaction boundary. Its receipt contains OperationID, base/result revisions, affected object IDs and actual undo capability. A conflict preserves the submitted change and returns the observed revision for comparison or an explicitly supported merge. Retrying a stale write with the new revision is a new semantic decision, not an automatic transport retry. Locks, where needed, have owner, scope and bounded expiry; an abandoned editor cannot permanently block a document.

Multi-object commands declare whether they are atomic. A transaction within one owner either commits its declared complete set or returns no committed change. A cross-service workflow declares ordered stages and per-stage receipts; it cannot claim atomicity without a coordinated commit protocol. Compensation is an authorised domain operation with its own outcome, not an assumption that deletion reverses every effect. Undo/redo records the semantic operation and supported preconditions. If another editor changed the affected object, offer the valid inverse or a conflict instead of replacing the entire document with an obsolete snapshot.

<a id="h.o4eutqd2ryk6"></a>

### 4.1.2 Local durability and independent delivery states

STORE-003 — Local save commits through the owning store's crash-consistent revision protocol. Write/stage data and necessary journal records before publishing the new current revision. After interruption, recovery selects a complete prior or new revision, or exposes a recoverable draft with an honest gap; it never assembles an apparently successful hybrid. The local receipt identifies the committed revision and storage location. Reporting Saved requires that receipt, not a scheduled write, a refreshed thumbnail or the absence of an exception in the view.

Maintain independent local, replication and export state. A document may be Saved locally, Sync pending and Export failed simultaneously. Each channel retains its own JobID/OperationID, intended revision, confirmed progress and error. Cloud unavailability cannot prevent ordinary local editing or relabel a confirmed local commit as lost. Autosave and explicit Save share the owner transaction and deduplication rules. Closing a surface waits for or explicitly resolves its own unsaved work; a failed close preparation restores the editable session and visible choice rather than leaving an invisible modal or an unclosable window.

STORE-004 — Recovery checkpoints identify ArtifactID, base revision, draft generation, writer/device, last durable checkpoint and any unfinished transaction. Restart presents eligible recovered work without overwriting a newer canonical revision. Opening, comparing, saving as a new revision/copy and discarding recovery are distinct operations. Discard removes the selected checkpoint only after the user-intended result is established; it does not discard other users' changes or a newer checkpoint. Recovery of a view, conversation or document never replays external messages, commands or approvals merely because they appeared in its previous UI.

<a id="h.uq1ghg41f1ng"></a>

### 4.1.3 Interchange, conversion and verified export

STORE-005 — Cross-app copy uses a versioned rich-object interchange contract. Its envelope contains source app/type/revision, object identities and relative references, typed payloads, attachments, supported semantic capabilities and conversion outcomes. Preserve rich text, table data/styles, graph expressions, image references and ink when supported. An independently copied object receives a new identity while retaining source provenance. Unsupported optional properties are retained opaquely where safe or listed in a concrete conversion result. Converting an editable object to pixels is a disclosed conversion choice, not a successful rich copy.

Import validates actual format/version and structure before committing a new artifact. Keep the source intact. Archive paths cannot escape the staging scope; links, expansion, recursion and resource consumption obey explicit importer limits. These defensive limits derive from the format and host security policy, not an arbitrary universal document cap. Imported executable content, external links and model instructions remain untrusted data. A partial import cannot become a canonical document simply because a parser produced its first page.

STORE-006 — An ExportJob pins SourceArtifactID/RevisionID, selected range, format/version, conversion options, destination, OperationID and output state. Preview, rendering, serialisation, writing and verification are distinguishable stages. Complete means the intended output bytes exist at the confirmed destination and have passed the format's appropriate structural checks. A download link resolves those bytes; it does not regenerate the latest source revision under the old filename. Cancel/failure removes or quarantines incomplete output without replacing a prior valid file. If the destination committed just before cancellation, report that confirmed outcome rather than deleting a successful export without a separate request.

Large inputs and outputs use incremental processing, stable pagination and bounded working memory. Progress identifies its unit and whether total work is known. Cancellation preserves the native artifact and a usable retry specification. Reopen exported native content and compare meaningful structures/content for acceptance; nonzero size is insufficient. Lossy exports disclose affected content. Export to an external provider is also a disclosure operation under section 4.4.

<a id="h.6zll90mrjpl"></a>

### 4.1.4 Migration, references and derived data

STORE-007 — A schema migration records input/output schema versions, compatibility requirements, migration identity and status. Validate a staged result before atomically adopting it; retain an appropriate recoverable source until success is established. Unknown optional fields survive round trips when the schema allows preservation. Unknown required security or semantic features fail safely or open a supported read-only view; they must not be stripped to manufacture compatibility. Encryption/key changes and format migrations retain independent failure information. A migrated file that cannot be decrypted or reopened has not completed migration.

STORE-008 — Embedded content declares Owned or Referenced semantics and its actual owner. Deleting a host can place its owned content in recoverable Trash according to that owner's policy; it cannot permanently delete separately owned referenced files through incidental cascade. Remove from collection, detach embed, delete artifact, uninstall app and purge are separate actions. Restore preserves identity/lineage and resolves destination collisions explicitly. Moving a file outside managed sync coverage changes that relationship; the app must not move it back silently. Broken or revoked references retain only permitted diagnostic metadata.

Indexes, thumbnails, embeddings, previews and caches carry source identity/revision and scope. Their invalidation follows canonical revision, deletion and grant events. Cache eviction cannot delete the only canonical content, unsaved draft, required journal or promised offline copy. Reference counts and ownership distinguish reusable model weights from user artifacts. Search/recent lists stop offering deleted or denied content as openable. Rebuilding derived state cannot restore a tombstoned item or widen its former scope.

STORE-009 — Acceptance injects interruption before commit, after commit but before acknowledgment, during export and during migration. Verify one committed revision, duplicate-operation reconciliation and recoverable drafts. Concurrently edit two replicas and retain both branches. Move a file, remove a reference, delete its host and restore with a name collision; verify each affects the declared identities only. Round-trip representative rich objects, list conversion losses, and confirm that denied sources are absent from previews/search after invalidation.

Source basis: files, files-organisation, home-library, boards-recovery, spaces-generated-files, spaces-export-recovery, collaboration-review, settings-storage; modules/[persistence.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/persistence.complete.md) and the persistence portions of [den-memory.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/den-memory.complete.md). Logical record names above are not assertions of an unchanged historical storage ABI.

<a id="h.2xavtmvfiuog"></a>

## 4.2 Identity, memory and account resources

DATA-020 — Preserve canonical account identity when a name, username, icon or optional profile field changes. Name and Username are required; Icon, Pronouns and Job are optional and clearable. Use authenticated identity for ownership and grants, not a friendly name. Account sessions, secure credentials, entitlements and resource ledgers remain separate from editable profile presentation.

DATA-021 — Prices, AI allowance, storage allocation, usage and plan thresholds come from the authoritative account service. A plan label does not calculate an entitlement. Missing values remain unavailable. Account storage, device disk, RAM/VRAM and provider usage are different resources. Sensitive plan/checkout changes use the trusted authenticated account flow, with confirmed status before reporting success. Sign-out/revocation does not erase local artifacts or drafts.

DATA-022 — A persistent Assistant, reusable Specialist definition and temporary subagent are different entities. The same Assistant identity, configuration and permanent Main conversation are reachable through Home, Spaces and editors. A Specialist invocation creates a scoped participant/run under its definition. Appearance or entry-point changes do not clone these identities. Collaboration with one Assistant does not disclose another Assistant's private memory.

DATA-023 — Den memory records retain stable identity/version, owner, provenance, timestamps, permitted scopes and correction/deletion history. Durable memory is distinct from request context, retrieved excerpts and derived summaries. Model references do not duplicate weights. Memory Frequency is exactly Never, Sometimes, Often or Always and affects future autonomous memory creation; it does not delete existing records.

DATA-024 — Background Learning is separately opted into. Persistent Memory, Background Learning and disclosure permissions are independent. Temporary conversations never create, update or reinforce durable memory while temporary. Their Personalised/Unpersonalised choice is set before the first message: Personalised may use existing authorised memories, custom instructions and eligible plugins; Unpersonalised uses none of those. Account/workspace restrictions and source permissions remain authoritative. DEC-004 and SPC-084–086 define the adopted controls and conversion behaviour.

DATA-025 — Context retrieval observes current personal, Assistant, conversation, Space, team and organisation boundaries. Record the provenance of sources actually used. Correcting/deleting memory and revoking a source update eligible indexes/caches and expose pending replica cleanup. Do not claim inaccessible devices have already erased their data. Raw credentials, encryption keys and recovery material are not model context; a serialised context format is not itself encryption.

Acceptance: change profile presentation and verify unchanged ownership. Revoke one session without deleting a draft. Open one Assistant through all supported entry points, amend memory frequency, correct/delete a record and run a Temporary chat. Verify no durable memory writes, no unauthorised cross-Assistant access and honest cleanup receipts. Display missing entitlement data without manufacturing a number.

Atlas anchors: account, settings-account, settings-subscriptions, settings-account-storage, home-assistants, home-specialists, spaces-assistant-alex, ai-studio, settings-models, settings-privacy.

<a id="h.swtckcc2676j"></a>

### 4.2.1 Identity and resource ownership

STORE-101 — AccountID, DeviceID, AssistantID, SpecialistDefinitionID, ConversationID and invocation RunID identify different entities. Changing a name, username, icon or presentation location preserves the owning identity. A persistent Assistant references its versioned configuration, authorised memory namespace and permanent Main conversation. A Specialist definition is reusable configuration; each invocation has a scoped participant/run and does not automatically acquire another Assistant's memory. Home, Spaces and editors resolve the same Assistant rather than cloning it at each entry point. Guest, personal, team and organisation ownership must be explicit when resolving an otherwise identical visible name.

Profile data, authentication sessions, credential references, entitlement snapshots and resource ledgers have separate stores and lifetimes. A ResourceLedger identifies resource category, authority, measured/allocated amount and unit, observation time, applicable interval and freshness. Storage bytes, device memory, model context, service allowance and money are never interchangeable. Unknown entitlement values remain unknown; a plan label cannot generate them. Sign-out revokes applicable sessions and future service calls while preserving recoverable local drafts under their existing ownership and protection.

<a id="h.gyhiumepiyhb"></a>

### 4.2.2 Den records and protected context flow

STORE-102 — A Den package is a versioned logical collection of scoped context, conversation records, memory, attachments, journals and derived indexes. Its manifest identifies namespace/owner, schema version, minimum compatible reader/writer, required capabilities and record/index generations. Physical directories and filenames are implementation details. Portable references use canonical IDs and source revisions; machine-specific paths require a resolvable external reference. Model references identify immutable artifacts and do not silently copy model weights into each Den or backup.

Protect personal Den content, indexes and recovery material with authenticated encryption at rest and dedicated key management. Hold keys through the supported secure-storage boundary, never in prompts, package metadata, ordinary logs or an exported settings file. An unavailable key or failed integrity check yields Locked or a scoped recovery failure; it never triggers a plaintext fallback. Export is encrypted by default for protected Den content. An explicitly chosen plaintext export states which content becomes readable and its destination; it does not change the source's protection or future export defaults. Do not claim end-to-end encryption for a route whose service can decrypt the content.

STORE-103 — A ContextEnvelope identifies RequestID, recipient model/adapter and processing locality, source references/revisions, selected excerpts, provenance, applicable grants, retention and expiry. Assemble only currently eligible material. Retrieved excerpts, execution summaries, durable memory and canonical source artifacts remain distinct records. A permitted model context read does not grant a plugin, tool, generated interface or diagnostic sink unrestricted access to raw personal Den data. Onward transfer of protected context into tool arguments or UI outputs is evaluated for that recipient and purpose through the broker; a natural-language instruction to keep secrets is insufficient enforcement.

Cross-app, cross-Assistant, cross-device and cross-provider context continuity requires applicable scope. A shared identity catalogue may reveal eligible Assistant definitions without exposing their private conversations. User-authorised memory inspection/editing uses its trusted owner surface and existing permissions; the privacy boundary must not prevent the user from managing their own records. Credentials, keys and recovery secrets are excluded from model context even when an app itself can use them to perform an authorised operation.

<a id="h.scmnl9goz3gd"></a>

### 4.2.3 Memory lifecycle, correction and retention

STORE-104 — A MemoryEntry contains MemoryID, namespace/owner, typed subject/value, provenance and source references, confidence where meaningful, permitted scopes, status, creation/update revisions, correction/supersession links and retention rule. Distinguish user-authored facts, user corrections and inferred candidates. A later inference cannot silently override a user correction or locked value. Candidate extraction, eligibility, deduplication, merge and commit are separate stages; only the owning store's commit creates durable memory. Record the contributing RequestID/RunID without storing private internal model reasoning.

Never disables autonomous memory creation; it does not delete records or disable an explicit authorised memory-management command. Sometimes commits conservatively selected durable information. Often considers normally useful durable information. Always evaluates eligible interactions for useful memory; it does not store every utterance or bypass relevance, privacy, deduplication and retention. Frequency is a policy input, not a timer, numeric quota or guarantee that every request writes something. Changing it affects future decisions and exposes the current setting consistently across entry points.

STORE-105 — Temporary conversation state blocks every durable-memory creation, reinforcement, merge or promotion path, including delayed summarisation and background extraction. Propagate the originating temporary scope to descendants and queued workers so closing the visible chat cannot evade it. Existing-memory reads require Personalised mode and current scope/grants under DEC-004; Unpersonalised excludes durable-memory retrieval, custom instructions and plugins. Temporary content does not enter Mesh, cloud storage or a permanent conversation without explicit supported conversion. A permitted remote inference route is separate from storage placement. Saving creates a new eligibility boundary under normal memory/learning settings; old temporary extraction jobs remain blocked and cannot silently resume or acquire backdated permission.

Background Learning has independent opt-in, sources, recipient, retention and cancellation. It creates authorised derived knowledge; it is not implied permission for model fine-tuning or a permanent raw interaction log. A personal interest record and a subject KnowledgeBank are different objects. A bank identifies topic, entries, provenance, confidence/freshness and storage policy. Retrieval selects relevant authorised entries rather than attaching the entire bank. Turning learning off prevents new learning work and reports already committed records under their own deletion controls.

STORE-106 — Correct/delete operations use expected revision and stable operation identity. Invalidate affected retrieval indexes, embeddings, summaries and future context caches; a tombstone prevents an outdated index or replica from recreating the deleted value. Keep only authorised minimal lineage needed for reconciliation, with its own retention. Deletion reports canonical commit, local derived cleanup, managed replica acknowledgments and pending offline peers separately. It cannot retract a request already processed by an external provider or promise erasure on an inaccessible device. Subsequent requests must cease using the revoked/deleted source within the managed system.

Checkpoint transfer preserves compatible conversation state, completed tool receipts, pending obligations and source references. It does not serialize arbitrary live GPU/KV state or grant permission to replay a completed operation. Backup restoration validates identity, schema, encryption and tombstones before adoption; an old backup cannot silently reverse current revocation. Memory management acceptance covers correction followed by inference, all four frequency settings, delayed Temporary extraction, source revocation after retrieval, encrypted restore and deletion while one replica is offline.

Source basis: account, home-assistants, home-specialists, spaces-assistant-alex, ai-studio, settings-account, settings-account-storage, settings-models and settings-privacy; modules/den-memory.complete.md, [models.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/models.complete.md) and [privacy.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/privacy.complete.md). Temporary personalisation is resolved by the owner on 22 November 2026 under DEC-004; source boundaries, current grants and the independent durable-memory write prohibition remain required.

<a id="h.zcgs4zv1zalf"></a>

## 4.3 Mesh, placement and remote access

DATA-030 — Devices have stable authenticated DeviceIDs and explicit pairing/trust records. A reachable or paired device does not expose every file, secret, conversation, model or runtime. Home and Settings show the same roster and grants. Personal and organisation namespaces remain distinct.

DATA-031 — Storage placement and execution locality are separate. A conversation can remain locally stored while an explicitly permitted remote model processes authorised context. New conversations default to Local unless an eligible alternative is explicitly chosen. Changing placement identifies the destination, affected items and permission checks. Transfers and cleanup remain pending until the relevant stores confirm them.

DATA-032 — Replicas retain manifests, source revisions, transfer status and relevant task references. Offline state is not current authoritative confirmation. Preserve independently edited revisions, reconcile pending work on reconnect, and never replay completed external effects. Revocation prevents future authorised use and reports what is confirmed versus still pending on an offline peer.

DATA-033 — Remote execution checks caller scope and target-side grants and capabilities. Pairing is not remote-desktop or arbitrary-command consent. Viewing, input, clipboard, file forwarding and device forwarding have separate grants. Closing a viewer does not shut down its machine. Connection loss preserves recoverable local work and shows unavailable/queued operations honestly.

Acceptance: pair two devices, authorise one namespace and deny another. Interrupt a transfer, edit both copies, reconnect and preserve the conflict. Revoke an offline peer and observe pending acknowledgment. Run an authorised remote inference route with local conversation storage, verifying that the two locations are labelled independently.

Atlas anchors: home-mesh, settings-mesh, settings-storage, settings-privacy, mini-computer, spaces-manage.

<a id="h.o615m5ha1n5o"></a>

### 4.3.1 Authenticated devices and independent locations

STORE-201 — A DeviceRecord contains stable authenticated DeviceID, owner/namespace, identity-key reference, trust relationship revision, supported protocol/capability versions and last verified contact. Friendly name, address and reachability are mutable observations. Pairing establishes an authenticated encrypted relationship for explicitly selected scopes; mere discovery confers no trust. Home and Settings use this same record. A peer's advertised capability is checked against the current target service and policy when invoked, not treated as a permanent entitlement.

StoragePlacement identifies the authoritative store and selected replicas for an artifact or conversation. ExecutionPlacement identifies where the actual model/tool operation runs. PresentationLocation identifies the surface viewing it. Changing one does not implicitly change the others. A conversation stored locally may use an authorised remote model; its labels must describe both facts. New conversations retain the stated Local default. Temporary conversations and private namespaces do not join replication merely because devices share an account.

<a id="h.xlt8dvfi6xww"></a>

### 4.3.2 Transfer, replica convergence and conflicts

STORE-202 — A ReplicaManifest identifies namespace, source and target DeviceIDs, included object IDs, revisions, tombstones, schema requirements and authorised scope revision. A TransferJob records intended manifest generation, verified chunks/objects, progress, error and completion acknowledgment. Stage and integrity-check transferred content before adopting it. Resume verifies the same source revision and scope; a changed source cannot be spliced into earlier chunks. Completion means the target owner accepted the declared revision, not that the sender finished writing to a socket.

STORE-203 — Offline replicas show last confirmed revision/contact and pending changes, not an unqualified current badge. Reconnect compares revision ancestry or another declared causal mechanism; wall-clock timestamps alone do not resolve concurrent edits. Nonconflicting changes may merge through the artifact owner's tested merge rules. Conflicting branches remain available with authors, bases and affected objects until an explicit resolution creates a resulting revision. Duplicate deliveries deduplicate by operation/revision identity. Replication of a Run or Task record does not schedule it again, transmit its approvals as universal grants or repeat external effects.

Revocation immediately denies future broker-authorised use, invalidates applicable sessions and cancels or quarantines uncommitted transfers. Already committed remote copies receive the supported deletion/revocation request and truthful acknowledgment state. An offline device remains pending. A permission change during transfer requires renewed eligibility before adoption; bytes received earlier do not establish continued access. Restoring an old trust database cannot silently reauthorise a revoked peer.

<a id="h.wubpliw1txho"></a>

### 4.3.3 Remote sessions and control

STORE-204 — A RemoteInvocation carries canonical caller and target identities, action/schema version, scope, OperationID, relevant TaskID/RunID and expected target revisions. Both origin and target enforce applicable policy. Authentication to a device does not authorise arbitrary shell, desktop control or access to its entire local account. Only operations whose owner declares safe offline admission/replay may queue while unavailable; consequential unknown outcomes require reconciliation before retry.

A remote session identifies the actual machine/runtime, session generation, connection status, current input owner and granted capabilities. View, input control, clipboard, file forwarding and device forwarding are separately granted. Reconnect proves continuation of the same session or explicitly reports a new one. Closing the viewer detaches the view; shutdown, process stop and session termination are separate typed operations. Connection loss releases local capture/input forwarding appropriately and preserves recoverable local work. A failed remote launch does not disable unrelated local editors.

STORE-205 — Validate pairing and identity rename, denied namespace, transfer interruption/resume, concurrent edits, schema mismatch and revoked target. Exercise local storage with remote inference, remote viewing without input permission, clipboard denial and reconnect after an operation acknowledgment is lost. Verify that no UI labels stale replicas as authoritative, no queued run executes twice, and closing the viewer leaves the machine's independent work intact.

Source basis: home-mesh, settings-mesh, settings-storage, settings-privacy, mini-computer and spaces-manage; modules/[mesh.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/mesh.complete.md) and den-memory.complete.md.

<a id="h.795fryiblopj"></a>

## 4.4 Permission and action boundaries

TRUST-001 — Effective permission is the intersection of platform/device policy, account/organisation rules, app grants, source ACLs, current resource/conversation scope, delegated scope and provider capabilities. A model-generated instruction, enabled control, retrieved page, attachment or tool result is data, not an execution grant. Trust records use stable caller identity and verified origin/content identity where relevant.

TRUST-002 — Document View/Edit/Suggest, editor AI Read-only/Take actions, conversation permission preferences, provider OAuth scope and exact action approval are separate controls. Read-only may inspect and propose within scope. Take actions permits eligible requests under existing rules; it is not unrestricted consent. A conversation can narrow inherited permissions but cannot widen a blocked account grant or source ACL.

TRUST-003 — Approval identifies the exact caller, action, target, expected revision, scope and consequence. Bind the recorded receipt to that operation and relevant expiry/policy. Recheck at execution and on resume/retry. A changed target, revision, caller or action cannot reuse an old approval silently. Cancel/dismiss leaves the prior permission scope and source state unchanged.

TRUST-004 — Grant narrowing, revocation and blocking govern future protected actions and truthfully report operations already in flight. Trust duration never disables protected-action rules. Record the admitted policy and subsequent changes needed to explain an outcome. Auditing excludes secrets and private internal reasoning.

TRUST-005 — Storage, model routing, external disclosure, memory, learning, telemetry and diagnostics have independent controls. Cloud-provider configuration is not blanket disclosure consent. Diagnostic exports identify their scope and redact secrets/unnecessary personal data; local export and upload are separate actions. Optional data collection has explicit controls and privacy-protective defaults.

TRUST-006 — Camera, screen, microphone and transcript retention are separately selected and permissioned. An enabled microphone does not imply camera/screen access. Stop removes the actual live source at the runtime boundary; a closed or visually disabled capture control must not continue supplying context. Transcript retention is not inferred from capture permission.

TRUST-007 — AI Studio test profiles, simulations and draft definitions cannot grant live account authority. Activating a versioned Agent definition remains separate from account-level authorisation. Terminal review binds an approved command to its actual shell, session, working directory and privilege context; copying a command into an editor does not execute it. Computer use requires its applicable explicit invocation and target scope and is not a permission bypass when a typed app API fails.

TRUST-008 — Browser-origin and browser-profile permissions remain separate from native application grants. Resolve site permissions against the actual selected origin/profile, not its display title or an internal-looking address. Web content and chrome imitation cannot obtain a native app capability. An engine switch, inspector view or navigation does not transfer a site grant into broader device or account authority.

Acceptance: revoke a source after retrieval, alter a target revision after approval and retry an expired action. Confirm fresh review/denial. Attempt cross-Assistant memory access and cloud fallback under Local only. Stop capture and verify the underlying source stops. Test a draft Agent and inspect a proposed terminal command without executing either effect. Confirm Data still requires per-mutation review under Take actions.

Atlas anchors: settings-permissions, spaces-permissions, mobile-permissions, settings-privacy, settings-sandbox, data-approval, vision-session-sources, ai-studio-permissions, terminal-command-review.

<a id="h.gj8q2arpgdmx"></a>

### 4.4.1 Shared authority and authenticated callers

SEC-001 — Home's permission broker resolves policy; the target owner enforces it at execution. Use one effective decision across UI, AI tools, automations, plugins, MCP and remote invocation. Effective authority is the intersection described by TRUST-001. A disabled UI control is a presentation consequence, not the enforcement layer. An approved model plan, local execution, enabled plugin, paired device or successful OAuth login does not independently grant a native app action. Missing broker connectivity fails the protected operation with a recoverable scoped error; it does not disable unrelated local reads or imply that the Home window must remain visible.

SEC-002 — A CallerIdentity distinguishes authenticated account/principal, registered app/package, version/content identity, execution instance and delegated parent. Trust uses verified origin/signature or an appropriate tamper-evident project/package identity; editable labels and filesystem paths are not sufficient. A materially changed package, signer, project content or delegated identity invalidates the relevant trust match. Provider account, browser origin/profile, native application and remote device identities remain separate even if all display the same friendly name. The service establishes identity from its authenticated boundary, not fields a model can write.

<a id="h.76y01vlxf6xt"></a>

### 4.4.2 Capability grants and exact action review

SEC-003 — A GrantRecord identifies GrantID, subject, issuer/authority, capability/action scope, allowed targets/namespaces, conditions, duration/expiry, policy revision and revocation state. Read, propose, write, disclose, execute, capture and administer are distinguishable capabilities. Document View/Edit/Suggest, AI Read-only/Take actions, conversation preferences, provider OAuth, source ACLs and action approval are separate inputs. A narrower conversation grant restricts inherited authority; it cannot widen a source ACL or account block. Existing valid ordinary grants are reusable within their scope; every action does not acquire an invented new approval ceremony.

The target action catalogue declares semantic risk, external effects, reversibility and mandatory review conditions. The caller cannot lower these classifications. Assess a batch's aggregate consequence and causal target set; splitting one protected bulk operation into many small calls cannot evade review. Permission requests may group compatible scopes only when caller, trust boundary and consequences remain understandable. Adding a target, data category or capability after review requires evaluation of the expanded scope.

SEC-004 — A required ApprovalRecord binds DecisionID to authenticated caller, ActionID/version, OperationID, canonical targets, expected revisions, material arguments or a verifiable canonical digest, destination/recipient, applicable shell/runtime context, policy revision, expiry and decision. The review presents the meaningful effect: what changes or leaves the device, where, for whom and with what irreversible consequences. Unknown affected counts are labelled unknown; a preview must not invent an exact count from a partial listing. Do not expose a meaningless hash as the user's only description.

At dispatch and the authoritative commit boundary, recheck that caller, target, revision, arguments, destination, permission and expiry still match. Approval of a draft is not approval of a later modified version. Retry can reuse a still-valid decision only for the same intended effect and idempotency identity; permission does not prove that the prior attempt failed. OutcomeUnknown is reconciled before repeating a non-idempotent operation. A required decision unavailable in headless work produces Waiting for approval and a recoverable checkpoint, not automatic acceptance.

SEC-005 — Accept permits the reviewed scope; Decline rejects the current request; Block persists the appropriate caller/scope denial until changed through the authority. Dismiss/cancel adds no new grant. Temporary or durable trust affects eligible future decisions under its stated conditions, never mandatory protected-action review. Data's per-AI-mutation review remains mandatory even with Take actions, a trusted Agent or collaborator edit access. Terminal approval binds the actual shell, command, working directory, session and privilege context. Computer-use permission binds the target surface and action scope and cannot bypass a denied typed operation by clicking the same effect.

<a id="h.ep2mek97zptt"></a>

### 4.4.3 Revocation, source provenance and disclosure

SEC-006 — Permission/source changes publish a versioned invalidation to interested brokers, owners, caches and long-running work. Revalidate at meaningful protected boundaries, including queued admission, resumed work, retrieval/render of protected cached content and external dispatch. A stale cached Allow is not sufficient after revocation. Narrowing stops future protected steps and attempts supported cancellation of in-flight work; report effects already committed or disclosure already transmitted. Do not claim that revocation retroactively unsent bytes. Pending cleanup and unreachable peers remain visible as such.

SourceReferences carry canonical source identity/revision and relevant ACL/provenance rather than merely a title or copied text. Attachments, retrieved pages, documents, tool output and generated interfaces remain data within that source trust boundary. They cannot impersonate system policy, issue grants or convert embedded instructions into user intent. When content drives an action proposal, the broker validates the real target/action and applicable user authority independently. A citation rendered after access is revoked may show only permitted metadata, not an indefinitely cached private excerpt.

SEC-007 — Storage, inference processing, onward tool disclosure, memory, background learning, telemetry and diagnostics each declare purpose, recipient, scope, retention and applicable locality. A configured cloud account is not universal permission to transmit private content. Routing evaluates disclosure eligibility before invoking a provider or fallback. Diagnostic collection/export is a bounded operation with redaction and a stated destination; uploading is separate from creating a local report. Credentials and key material remain secure references. Useful audit events record action identity, source/target references, decision/policy revision, outcome and safe failure details while excluding secrets, unnecessary personal content and private internal model reasoning.

SEC-008 — Capture sessions identify source/device, microphone/camera/screen kind, active consumer, session generation, retention choice and actual runtime state. Requesting one source cannot enable another. Stop first revokes that session's delivery authority and tears down the underlying capture/subscription; late frames from the stopped generation are dropped before inference. A closed source indicator cannot leave an undisclosed active producer. Transcript retention is selected independently from live capture, and stopping capture does not fabricate deletion of an already retained transcript.

<a id="h.5qit54s6sg5q"></a>

### 4.4.4 Validation of the enforced boundary

SEC-009 — Validation invokes the owning command directly as well as through the visible control. Cover an altered caller label, changed package identity, revoked source, changed target revision, expanded recipient set, expired approval and lost mutation acknowledgment. Verify ordinary valid grants work without needless prompts while mandatory Data review persists. Exercise malicious instructions inside retrieved content, generated CUI with an unregistered action, Local-only routing during provider failure and a stopped capture source that emits one late frame. The pass condition is correct service-side effect/denial and truthful receipts, not merely a disabled button or a persuasive model refusal.

Source basis: settings-permissions, spaces-permissions, mobile-permissions, settings-privacy, settings-sandbox, data-approval, vision-session-sources, ai-studio-permissions and terminal-command-review; modules/[permissions.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/permissions.complete.md), privacy.complete.md, [integrations.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/integrations.complete.md) and [plugins-mcp.complete.md](https://github.com/CroakyJake12/9to1/blob/1f92cd888f59025cc684f0720163efdbc580f8e3/docs/specification/owner-references/2026-11-18-interface-refactor/baseline/retained-pack/modules/plugins-mcp.complete.md). Historical permission labels and any historical blanket trust bypass are superseded by the explicit current boundaries above.
