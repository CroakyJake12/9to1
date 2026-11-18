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
