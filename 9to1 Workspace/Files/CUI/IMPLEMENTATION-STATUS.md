# Files lane acceptance checklist

Scope source: `work/spec_chunks/worker-19.md`, Files section. This checklist distinguishes implemented local behavior from contracts and unverified integrations. **A contract or adapter seam does not count as a passed behavior.**

## Directly validated in the Files CUI slice

- [x] Stable hosted item IDs are independent of names and parent/path projections; rename/move projections preserve the same ID.
- [x] Provider capability checks require every requested capability and return a structured unsupported-operation result.
- [x] Versioned state reads reject unknown schema versions; writes stage beside the destination, flush the file, and atomically replace it.
- [x] The offline structural-operation journal persists insertion order across instances, deduplicates an identical OperationID, rejects replay with changed arguments, and enforces legal terminal transitions with structured errors.
- [x] The sync cursor persists across a new store instance and rejects cursor regression.
- [x] Materialization registration verifies the actual file size and SHA-256 before marking bytes available, explicitly associates the local path with the canonical item and remote revision, rejects paths outside the sync root and existing reparse-point traversal, and retains identity when the mapping is moved.
- [x] Eviction eligibility rejects pending local changes, mismatched remote revisions, and Always Available items; pin state survives a local edit and successful sync in the local metadata record.
- [x] Folder colour is persisted by stable folder identity and can be reset.
- [x] Drag descriptors preserve stable item/location identity and the explicit copy/move intent.
- [x] Provider location registration, pagination, availability and capability reporting have direct checks.
- [x] Donor provenance names upstream revision `21d407b51dbc2b1bd2733ef615d727345d5dd16f`; the parity manifest classifies required donor feature groups and records the CUI gap.

## Release acceptance still open

- [ ] CUI donor layout and workflow parity across tabs, history, location tree, views, context actions, properties, previews, search, clipboard, drag/drop, archives, transfers, settings, accessibility and session restore. The manifest is a classification record, not parity evidence.
- [ ] 9to1 Drive hosted provider, quota/allocation, web/desktop canonical-object continuity, browser-authorised local transfer and Home/account authorization.
- [ ] Server-ordered push change feed, live multi-client propagation, durable reconnect reconciliation, event deduplication and ordinary metadata latency.
- [ ] Optimistic structural mutations with rollback/reconciliation against current permissions and revisions.
- [ ] Hydration jobs, content streaming, progress/failure UI, offline opening, explicit manual sync controls and cache manager integration.
- [ ] Actual Free up space execution tied to provider-verified durable revisions and atomic protection against concurrent local writes.
- [ ] Offline folder create/rename/move/delete/restore routed through the durable journal, then server reconciliation after reconnect.
- [ ] Deterministic concurrent structural conflicts and raw binary conflict preservation without timestamp last-write-wins.
- [ ] Resumable/chunked transfer jobs, durable checkpoints, pause/resume/cancel, restart continuation, conflict policies and final integrity verification against provider content.
- [ ] Durable hosted versions, restore-as-new-revision, Trash/restore/purge execution and elevated purge impact approval.
- [ ] Effective ACL inheritance calculation, move previews, direct/inherited grant editing, shared-with-me canonical references, live revocation and optional revocable link shares.
- [ ] Home first-party action registration and permission-broker execution/audit. Current feature-provider interface does not adapt to the active Home permission request submission contract; awaiting Home owner/coordinator direction.
- [ ] Canonical Write/Present/Data/Boards/Canvas creation and Stack here/from-folder through owning-app APIs; current Files-side typed router seams are not connected to owning apps.
- [ ] Owning-app revision commits and `LocationChanged` events verified with real Write/Present/Data/Boards/Canvas/Stack/Media providers.
- [ ] Home shared search index publication and permission rechecks, plus read-only preview renderers and native-type open routing.
- [ ] Files contextual AI bar and exact revision-scoped privacy handoff. Metadata-only context contract exists; no model handoff implementation is connected.
- [ ] Large-hierarchy incremental performance, full Windows and 9to1-OS package validation, web capability validation, accessible UI journeys, and end-to-end persistence/restart tests.

## External integration requests

- Home contract owner: supported permission-provider adapter to the active `HomePermissionRequestSubmission` / `HomePermissionAuthorization` implementation.
- Home / 9to1 Drive owners: stable hosted storage, event stream, quota, revision, transfer, ACL and principal-ID contracts.
- Write, Present, Data, Boards, Canvas and Stack owners: creation/registration APIs and durable revision-commit interfaces.
- Home search owner: Files entity registration, cursor/index and permission recheck contract.

No completion claim is made while these unchecked acceptance requirements remain.
