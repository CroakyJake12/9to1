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
