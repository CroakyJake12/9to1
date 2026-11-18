# Artifacts, local persistence and recovery

Module ID: `persistence` · 9to1 Development Specification v3

## Prerequisites

- **Platform, app registry and host integration** — Canonical app registry, artifact routes, native/browser embedding, lifecycle and semantic UI/API parity.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.

## Overview

Applications persist canonical artifacts and revisions independently of their display surfaces. Local saving, replication, export and publishing are different state machines with honest visible receipts.

## Layouts & visible states

- **V3-PERSISTENCE-LAY-01** — Use explicit states such as local draft, changes pending, saving locally, saved locally, sync pending, synced, failed, read-only and conflict.
- **V3-PERSISTENCE-LAY-02** — Generated files distinguish saved native artifact, pending export, verified downloadable bytes and failed export. Do not show successful download for a preview-only object.

## Features & behaviour

- **V3-PERSISTENCE-FEA-01** — Apply typed commands with expected revision, transactional validation and meaningful undo/redo. A restored view does not replay previously executed external actions.
- **V3-PERSISTENCE-FEA-02** — Autosave and explicit save must survive interruption with recoverable draft/checkpoint semantics. Optional sync cannot be required for basic local editing.
- **V3-PERSISTENCE-FEA-03** — Keep exports reproducible from a known revision with declared format/range/options; preserve native content when a secondary export fails.

## Canonical entities & persistence

- **V3-PERSISTENCE-ENT-01** — Use stable artifact/object IDs, revisions, source references, timestamps and provenance. Preview caches and rendered exports are derived from canonical content.
- **V3-PERSISTENCE-ENT-02** — Separate view state from authored state and runtime state from source configuration. A duplicate/fork receives a distinct identity with traceable ancestry.

## Permissions & recovery

- **V3-PERSISTENCE-PER-01** — Resolve collisions and revisions explicitly. Avoid silent overwrite, duplicate send or side-effect replay after reconnect/retry.
- **V3-PERSISTENCE-PER-02** — Delete, remove-from-collection, detach and uninstall are distinct operations and require the correct owner semantics.

## Acceptance checks

- **V3-PERSISTENCE-ACC-01** — Interrupt local save/export/sync, restart, reopen and verify canonical content and pending work recover.
- **V3-PERSISTENCE-ACC-02** — Verify reference removal never deletes an unrelated source file and failed derived export retains the native revision.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

SECTION-03, UX27-DATAFLOW-001, UX27-DATAFLOW-002, UX27-DATAFLOW-003, UX27-DATAFLOW-004, UX27-DATAFLOW-005, UX27-DATAFLOW-006

## Technical reading map

- Artifacts, local persistence and recovery — Shared persistence, interoperability and local service boundaries; Shared core is not compulsory cloud setup (part 01) — `technical/persistence-01.md` (651 words; V2-B1203, V2-B1204, V2-B1205…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.


# Retained technical requirements

# Artifacts, local persistence and recovery — Shared persistence, interoperability and local service boundaries; Shared core is not compulsory cloud setup (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1203 · v2 lines 5255–5256

<!-- UX27:BEGIN SECTION-03 -->
## 3. Shared persistence, interoperability and local service boundaries

### V2-B1204 · v2 lines 5258–5258

### UX27-DATAFLOW-001 — Shared core is not compulsory cloud setup

### V2-B1205 · v2 lines 5260–5260

**O/B.** Home Core and the Shared Productivity Engine may be packaged dependencies as V2 requires; their existence does not require the user to open a visible Home window, connect a cloud account, select an AI model or configure the Files app before ordinary local document work. Required local services start through the supported installation/runtime contract and provide a useful scoped error if genuinely missing.

### V2-B1206 · v2 lines 5262–5262

Keep local storage adapters behind canonical owning-app interfaces. Do not respond to the no-Files-prerequisite correction by creating an incompatible Boards-only object engine or bypassing permission checks. Optional account/provider configuration appears only when the user invokes the corresponding network feature.

### V2-B1207 · v2 lines 5264–5264

### UX27-DATAFLOW-002 — Artifact opening and ownership

### V2-B1208 · v2 lines 5266–5266

**B/D.** An open request resolves type, canonical identity, revision, owner app, access scope and location. The receiving surface shows whether it is editing the original, an authorised reference, or an explicit independent copy. Open in App preserves identity; Save a Copy deliberately creates independent owned content. A thumbnail, cached preview or failed hydration must not be treated as the canonical artifact.

### V2-B1209 · v2 lines 5268–5268

Cross-app copy uses the existing versioned ProductivityObjectBundle. Preserve rich text, table data/styles, graph expressions, image references and ink where supported. Unsupported properties remain preserved or are disclosed in an explicit conversion result; silently flattening an editable object to pixels is not interoperability.

### V2-B1210 · v2 lines 5270–5270

### UX27-DATAFLOW-003 — Save and sync are separate transactions

### V2-B1211 · v2 lines 5272–5272

**B/D.** Local commit, remote upload and remote semantic conflict resolution have separate states and receipts. A local document remains usable when cloud sync is paused or unavailable. Retry/resume uses the same pending revision identity where appropriate and must not create a new duplicate cloud document each time. Display a failed sync as failed sync, not as lost local save.

### V2-B1212 · v2 lines 5274–5274

A user who moves a file outside a managed sync folder chooses a different storage relationship; do not silently move it back. Explain changed sync coverage while preserving content and identity. Connected cloud services retain their real quota/access restrictions without imposing a fictional universal document-size cap.

### V2-B1213 · v2 lines 5276–5276

### UX27-DATAFLOW-004 — Crash recovery and interrupted close

### V2-B1214 · v2 lines 5278–5278

**B/D.** Maintain a revision-aware recovery journal/snapshot through the owning storage layer. On restart, identify unfinished documents by stable identity, show recoverable revisions and let the user open/compare/discard them intentionally. A failed save must not poison all future close attempts. If close preparation fails, restore the same editable session and visible decision state instead of leaving an invisible modal or permanently unclosable window.

### V2-B1215 · v2 lines 5280–5280

Test interruption before/during/after a durable commit and while an embedded artifact is saving. The recovered result must be a consistent prior/new revision with an honest gap report. Do not assert recovery merely because the process launches again.

### V2-B1216 · v2 lines 5282–5282

### UX27-DATAFLOW-005 — Import/export completion and safety

### V2-B1217 · v2 lines 5284–5284

**B/D.** Import detects actual supported format/version, validates structure and preserves the original until successful. Export states the format, destination, conversion limitations and real completion state. Cancellation or failure removes/quarantines incomplete output without corrupting the prior valid file. Large artifacts stream/chunk according to the scalability annex.

### V2-B1218 · v2 lines 5286–5286

Treat file content and imported metadata as untrusted data, not instructions to the Agent or permission grants. Validate archive paths, expansion limits and resource use through the existing security contract. Reopening an exported native artifact and independently checking its contents is part of acceptance; a nonzero file size alone is not proof.

### V2-B1219 · v2 lines 5288–5288

### UX27-DATAFLOW-006 — References and deletion

### V2-B1220 · v2 lines 5290–5290

**B.** Every embedded artifact declares BoardOwned/DocumentOwned versus Referenced ownership as applicable. Deleting a host container removes its owned content according to recoverable Trash semantics but cannot permanently delete separately owned referenced files without a distinct authorised action. Broken references retain useful metadata and recovery affordances without leaking revoked content.

### V2-B1221 · v2 lines 5292–5294

Restore operations preserve lineage and resolve name/location collisions explicitly. Global search, recent items and notifications update from canonical lifecycle events rather than keeping dead entries that appear openable indefinitely.
<!-- UX27:END SECTION-03 -->
Productivity Apps
