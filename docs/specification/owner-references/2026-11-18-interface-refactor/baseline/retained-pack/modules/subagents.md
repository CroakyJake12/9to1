# Subagents, delegation and task trees

Module ID: `subagents` · 9to1 Development Specification v3

## Prerequisites

- **Dulche runtime, requests and execution services** — Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Subagents are bounded child executions within a canonical task tree. They expose useful progress, blockers, evidence and outputs without disclosing private internal reasoning or creating independent permission authority.

Current owner overrides: V3-OWNER-008.

## Layouts & visible states

- **V3-SUBAGENTS-LAY-01** — Show parent/child relationships, task objective, configured identity/route, state, scope, budget and outputs. A child detail view provides high-level work events and actionable blockers.
- **V3-SUBAGENTS-LAY-02** — Use real progress/task status; do not invent completion percentages, measured timings or successful tool results.

## Features & behaviour

- **V3-SUBAGENTS-FEA-01** — Delegate scoped context and only the intersection of current parent grants. Enforce depth, concurrency, budget and capability limits.
- **V3-SUBAGENTS-FEA-02** — Support structured joins, result aggregation, cancellation/pause propagation and partial-failure handling. A child’s failure must not erase completed sibling artifacts.
- **V3-SUBAGENTS-FEA-03** — Relevance-based Assistant participation and persistent Teams are separate from anonymous worker delegation; preserve canonical identities when configured collaborators are used.

## Canonical entities & persistence

- **V3-SUBAGENTS-ENT-01** — Persist parent TaskID/RunID, child IDs, delegation inputs, scope/version, status events, outputs and join receipts.
- **V3-SUBAGENTS-ENT-02** — Reconnect to existing child runs after view/runtime restart without launching duplicate work or replaying external actions.

## Permissions & recovery

- **V3-SUBAGENTS-PER-01** — Revocation and budget changes propagate through the tree. An exhausted budget cannot be hidden by spawning another child or bypassing a required review.
- **V3-SUBAGENTS-PER-02** — Expose retry scope and uncertain external results; retry only safe or idempotent work with renewed grants when needed.

## Acceptance checks

- **V3-SUBAGENTS-ACC-01** — Test concurrent children, mixed success/failure, waiting approval, cancelled parent, scope revocation and resumed inspection.
- **V3-SUBAGENTS-ACC-02** — Confirm outputs open the canonical owning artifacts and the activity view contains no private chain-of-thought.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Subagents, delegation and task trees — Subagents and parallelisation; Terminology and boundary (part 01) — `technical/subagents-01.md` (1,836 words; V2-B1963, V2-B1964, V2-B1965…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
