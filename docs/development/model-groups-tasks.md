# Model Groups development tasks

Feature: `DULCHE-MODEL-GROUPS-V2`

Date: 16 October 2026

Phase: Shared Frameworks → Dulche
Overall feature status: WORKING; implementation and independent acceptance pending.

The persistent queue is [GitHub issue #34](https://github.com/CroakyJake12/9to1/issues/34), linked to [existing master queue #21](https://github.com/CroakyJake12/9to1/issues/21). This file is its repository mirror, not a competing task system. Existing tasks, ownership and priority remain unchanged. Root Codex owns the specification/backlog checkpoint; implementation ownership is assigned when dependencies and capacity permit.

Authority: [Development Specification V2](https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit) and the approved 16 October user handoff. [Reconciled proposal](../dulche/model-groups-v2.md) contains all MG-01–MG-18 and the seven acceptance tests. Authenticated master edits failed with HTTP 400 FAILED_PRECONDITION; master incorporation is **BLOCKED**, not completed.

| ID | Task | Status | Dependencies / completion evidence |
|---|---|---|---|
| MG-T01 | Incorporate into authoritative V2 | BLOCKED | Reconciled proposal saved; master API rejects both guarded batch and minimal insertion. Requires successful authorised write and exact read-back. |
| MG-T02 | Shared Model Group contracts | WORKING — queued, execution not started | MG-T01; existing Dulche/Home/Den/registry contract review. Concrete and virtual identities, immutable selection/config revisions. |
| MG-T03 | Den persistence and synchronisation | WORKING — queued, execution not started | MG-T02; encrypted Den, namespace consent, secure references, sync/revision/conflict/recovery and deletion semantics. |
| MG-T04 | Canonical registry and routing | WORKING — queued, execution not started | MG-T02, MG-T03; eligibility-first routes, capabilities, permission/privacy/entitlement policy. |
| MG-T05 | Lazy atomic send-time resolution | WORKING — queued, execution not started | MG-T04; canonical request/Task/Run snapshots and history. No selection-side load/unload/restart or model-memory allocation. |
| MG-T06 | Multi-backend runtime coordination | WORKING — queued, execution not started | MG-T05; genuine llama.cpp, Strata, cloud and applicable modality adapters; residency/reuse/resources/cancellation. |
| MG-T07 | Shared CUI model/reasoning selector | WORKING — queued, execution not started | MG-T04, MG-T05; named profiles and percentage mappings; accessible responsive shared CUI. |
| MG-T08 | Group management UI/API | WORKING — queued, execution not started | MG-T03, MG-T07; create/edit/duplicate/reorder/delete/reload with canonical IDs and revisions. |
| MG-T09 | Compatible app integration | WORKING — queued, execution not started | MG-T06, MG-T07, MG-T08 and app readiness; Chat/Spaces/Dev/Assistants/Write/Present and other applicable surfaces. |
| MG-T10 | Automated acceptance and regression | WORKING — queued, execution not started | Test design from MG-T02; execution against corresponding implementation and real providers; MG-01–MG-18 individually evidenced. |
| MG-T11 | Independent visual/functional acceptance | WORKING — queued, execution not started | MG-T09, MG-T10; exact artifacts/platforms, authorised models/accounts/devices and independent reviewer; all seven acceptance tests. |
| MG-T12 | Regression and release readiness | WORKING — queued, execution not started | MG-T11; applicable failures closed, standalone selection/routes/reasoning/lifecycle preserved; reproducible evidence. |

## Scheduling and reconciliation

This feature follows the existing shared-framework dependency order. It does not move ahead of active critical work merely because it was registered today. Contract/test design may proceed independently where it does not conflict with current owners; integrated execution waits for its dependencies.

Model Groups are virtual identities with selected profiles, distinct from ordered fallback routes. Group selection cannot authorise cloud context transfer or Den sharing. The original admitted group/profile snapshot stays immutable; permitted fallback is a separately attributed attempt under captured policy. Profile percentages map routes, not universal reasoning or intelligence. Explicit model load/unload remains a separate action.

No unresolved product requirement was identified in this reconciliation. Implementation must inspect and reuse actual service contracts; provider/device capability availability remains a dependency, not invented compatibility. Secure Den sync, shared registry/residency, provider adapters, CUI selector and canonical request history must be verified before feature completion.

## Checkpoint evidence

- Retrieved V2 title: `9to1 Development Specification v2`.
- Inspected Home → Universal Model Picker; Frameworks → Dulche; Dulche Den including DEN-PRIV-001–008; OS → Universal Model Picker.
- Proposed master locations: Dulche Model Groups section before Voice, plus Home picker, Den catalogue and OS picker cross-references. **None claimed saved to master.**
- Independent read-only review found coverage complete; clarified in-flight configuration immutability before attempting the write.
- GitHub issue #34 saved and read back; MG-T01–MG-T12 registered with dependencies.
- Repository commit records only this task mirror, the proposal and master-write evidence; concurrent application implementation is excluded.
- Product implementation tests and acceptance: NOT EXECUTED for Model Groups. The checkpoint does not establish COMPLETED or VERIFIED feature status.
