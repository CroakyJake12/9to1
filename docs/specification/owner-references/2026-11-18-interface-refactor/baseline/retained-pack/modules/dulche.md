# Dulche runtime, requests and execution services

Module ID: `dulche` · 9to1 Development Specification v3

## Prerequisites

- **Platform, app registry and host integration** — Canonical app registry, artifact routes, native/browser embedding, lifecycle and semantic UI/API parity.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Dulche is the shared AI/runtime broker, owning model routing, request execution, Task/Runs, tool actions and integrations with memory and subagents. Its lifecycle and results are observable through typed contracts rather than interface simulations.

Current owner overrides: V3-RECON-001.

## Layouts & visible states

- **V3-DULCHE-LAY-01** — Expose Starting, Ready, Degraded, Restarting and Stopped accurately where relevant. An unavailable broker must not disable unrelated local editing.
- **V3-DULCHE-LAY-02** — Show request admission, active execution, waiting approval/input, paused, cancelled, completed and failed as distinct states; elapsed time represents measured activity where specified.

## Features & behaviour

- **V3-DULCHE-FEA-01** — Use canonical requests and ordered eligible model routes. Filter capabilities, privacy, entitlements and resources before choosing a route. Local-only must never silently fall back to cloud.
- **V3-DULCHE-FEA-02** — Keep fallback routing distinct from tool retry and external-effect retry. Durable execution records support idempotent recovery, cancellation and inspection.
- **V3-DULCHE-FEA-03** — Preserve retained runtime APIs, transport, multi-engine/Strata and embedded service contracts. UI work must not silently replace them with a new backend.
- **V3-DULCHE-FEA-04** — Treat supplied content as data with provenance; shared-context services, safety checks and action dispatch remain within the trust boundary defined in retained clauses.

## Canonical entities & persistence

- **V3-DULCHE-ENT-01** — Persist request IDs, TaskID, RunID, ActionGraphID, admission configuration, model/route identity, status events and output references. A trace is a structured runtime record, not a fabricated progress feed.
- **V3-DULCHE-ENT-02** — Maintain durable queue and checkpoint state separately from transient view state. Reopening a view attaches to existing execution.

## Permissions & recovery

- **V3-DULCHE-PER-01** — Propagate current grants, budgets and revocation to tools and child tasks. Stop/interrupt acknowledges actual runtime state and does not promise an already-committed external effect can be undone.
- **V3-DULCHE-PER-02** — Resume from safe checkpoints; represent partial output, degraded providers and uncertain external outcomes explicitly.

## Acceptance checks

- **V3-DULCHE-ACC-01** — Test admission refusal without losing draft, provider failure/fallback, local-only routing, cancellation acknowledgement, restart recovery and idempotent tool handling.
- **V3-DULCHE-ACC-02** — Confirm semantic API operations and displayed task states match real runtime events.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

SPUI-194

## Technical reading map

- Dulche runtime, requests and execution services — Dulche runtime control; Permission and trust broker (part 01) — `technical/dulche-01.md` (2,385 words; V2-B0453, V2-B0454, V2-B0455…).
- Dulche runtime, requests and execution services — Result contract and metric availability; Response content and identity (part 02) — `technical/dulche-02.md` (2,271 words; V2-B2036, V2-B2037, V2-B2038…).
- Dulche runtime, requests and execution services — Persistent request execution; Context compaction and Context Summarisation (part 03) — `technical/dulche-03.md` (2,513 words; V2-B2083).
- Dulche runtime, requests and execution services — Dulche multi-engine inference runtime; Runtime abstraction and dispatcher (part 04) — `technical/dulche-04.md` (992 words; V2-B2248, V2-B2249, V2-B2250…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
