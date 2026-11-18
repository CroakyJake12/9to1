# External integrations and provider connections

Module ID: `integrations` · 9to1 Development Specification v3

## Prerequisites

- **Account, identity and resource entitlements** — Canonical CAKE account identity, secure sessions, server entitlements and resource allocation state.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

External integrations connect canonical 9to1 capabilities to authorised provider accounts. They preserve provider provenance and distinguish live references, imported copies, cached data and synchronised state.

## Layouts & visible states

- **V3-INTEGRATIONS-LAY-01** — Show provider account, granted scopes, connection health, last confirmed sync, pending operations and reconnect controls.
- **V3-INTEGRATIONS-LAY-02** — Separate the integration manager from Connect conversations and Mail. A linked provider can contribute capabilities across apps without becoming a new app clone.

## Features & behaviour

- **V3-INTEGRATIONS-FEA-01** — Use explicit OAuth/provider authorisation and the typed capability registry. Read, create, update, delete, sync and import operations retain their distinct consequences.
- **V3-INTEGRATIONS-FEA-02** — Preserve source identity and normal conflict semantics. Reconnect/retry uses idempotent queues where supported and does not duplicate messages or file mutations.
- **V3-INTEGRATIONS-FEA-03** — Report provider limits, unsupported operations and uncertain outcomes accurately; a local queued action is not a remote success.

## Canonical entities & persistence

- **V3-INTEGRATIONS-ENT-01** — Persist connection/account IDs, resource IDs, scope/version, sync tokens, mappings, pending operation IDs and last confirmed remote receipts.
- **V3-INTEGRATIONS-ENT-02** — Cache entries carry freshness and permission provenance; provider revocation invalidates eligible access.

## Permissions & recovery

- **V3-INTEGRATIONS-PER-01** — Check both provider scopes and 9to1 grants at use time. Never infer access to a second account from an existing provider session.
- **V3-INTEGRATIONS-PER-02** — Offer recovery for expired auth, network loss, quota, conflicts and partial batch success without silently overwriting remote changes.

## Acceptance checks

- **V3-INTEGRATIONS-ACC-01** — Test connect→authorise→read/write→revoke, offline queue, duplicate retry and concurrent remote edit.
- **V3-INTEGRATIONS-ACC-02** — Verify UI status corresponds to actual provider receipts and cached data is labelled when stale.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

UX27-OTHER-014

## Technical reading map

- External integrations and provider connections — External App Integrations; Core connection model (part 01) — `technical/integrations-01.md` (1,790 words; V2-B2151, V2-B2324, V2-B2325…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
