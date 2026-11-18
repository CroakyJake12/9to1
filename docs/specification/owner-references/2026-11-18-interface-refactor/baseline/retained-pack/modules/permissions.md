# Permissions, approvals and execution scope

Module ID: `permissions` · 9to1 Development Specification v3

## Prerequisites

- **Platform, app registry and host integration** — Canonical app registry, artifact routes, native/browser embedding, lifecycle and semantic UI/API parity.
- **Privacy, data boundaries and context isolation** — Privacy scopes, consent, local/remote boundaries, source provenance, retention and revocation.

## Overview

Permissions intersect platform policy, account/org policy, app capability grants and the current operation scope. A visual control shows effective authority; it never creates authority merely by being enabled.

Current owner overrides: V3-OWNER-006.

## Layouts & visible states

- **V3-PERMISSIONS-LAY-01** — Keep Spaces Conversation permissions separate from editor AI Read-only/Write and document Edit/Suggest/View. Name the active scope and blocked reason visibly.
- **V3-PERMISSIONS-LAY-02** — Approval requests identify the proposed action, target, revision, data/source scope and consequence. Pending approval is distinct from paused, denied and failed.

## Features & behaviour

- **V3-PERMISSIONS-FEA-01** — Use typed action grants and recheck at execution. A narrower conversation override cannot widen durable app or organisation grants.
- **V3-PERMISSIONS-FEA-02** — Require action approval wherever the source specifies it, including every AI mutation in Data. A reviewed Plan may grant only its declared scope; unrelated operations remain outside it.
- **V3-PERMISSIONS-FEA-03** — Distinguish provider OAuth scopes, installed capability availability, user grants and one-operation approval. Installing or enabling a Plugin/Skill does not automatically authorise its data access.
- **V3-PERMISSIONS-FEA-04** — Preserve review and independent acceptance gates when budgets run out; continue unrelated permitted work without pretending a required review happened.

## Canonical entities & persistence

- **V3-PERMISSIONS-ENT-01** — Persist approval/action IDs, grant scope, policy source, expiry, revocation and audit outcome. Grants reference canonical targets rather than display names.
- **V3-PERMISSIONS-ENT-02** — An admitted request records the effective policy/version and subsequent permission changes for recoverable audit.

## Permissions & recovery

- **V3-PERMISSIONS-PER-01** — Reject stale target revisions and revoked grants with actionable recovery. Retry must not duplicate external effects or reuse an expired approval.
- **V3-PERMISSIONS-PER-02** — Redact inaccessible resources from pickers, suggestions and exports; show safe unavailable metadata only where authorised.

## Acceptance checks

- **V3-PERMISSIONS-ACC-01** — Test Read-only proposals without mutation, Write with action-specific approval, mid-run revocation, stale revisions and denied provider scopes.
- **V3-PERMISSIONS-ACC-02** — Verify keyboard and mobile permission controls show the same effective scope as desktop and never imply broader access.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

SPUI-200, SPUI-188, SPUI-199

## Technical reading map

- Permissions, approvals and execution scope — App APIs and automation permissions; API parity requirement (part 01) — `technical/permissions-01.md` (1,705 words; V2-B0132, V2-B0133, V2-B0134…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
