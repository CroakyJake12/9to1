# Go, universal search and entity navigation

Module ID: `go-search` · 9to1 Development Specification v3

## Prerequisites

- **Platform, app registry and host integration** — Canonical app registry, artifact routes, native/browser embedding, lifecycle and semantic UI/API parity.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Go is the shared universal search/navigation surface across Home, Browse and 9to1OS. It resolves real applications, commands, artifacts and context through permission-aware indexes and explicit Default/AI search modes.

## Layouts & visible states

- **V3-GO-SEARCH-LAY-01** — Use a focused overlay with query, category/source-aware results, keyboard navigation and clear target actions. Keep ordinary search and AI-assisted search visibly distinct.
- **V3-GO-SEARCH-LAY-02** — Expose availability, source owner and relevant location instead of presenting inaccessible resources as actionable results.

## Features & behaviour

- **V3-GO-SEARCH-FEA-01** — Index canonical entity references and registered commands; recheck access when opening or executing. Search text alone does not authorise a command.
- **V3-GO-SEARCH-FEA-02** — Keep app launch, open artifact, navigate location and request AI actions semantically distinct. Contextual search must not leak unrelated app data.
- **V3-GO-SEARCH-FEA-03** — Preserve source-defined indexing, deduplication, ranking and tombstone contracts rather than scanning private stores directly from UI.

## Canonical entities & persistence

- **V3-GO-SEARCH-ENT-01** — Use canonical search/result IDs, source artifact/entity IDs and index revisions. Search caches are derived, not an independent document store.
- **V3-GO-SEARCH-ENT-02** — Deleted/revoked sources propagate tombstones and safe redaction to indexes.

## Permissions & recovery

- **V3-GO-SEARCH-PER-01** — Handle unavailable devices, offline sources and stale results with clear recovery. Do not execute a stale action before checking current policy.
- **V3-GO-SEARCH-PER-02** — Closing the overlay preserves the underlying application’s draft/focus state.

## Acceptance checks

- **V3-GO-SEARCH-ACC-01** — Search by keyboard, open an app and artifact, then revoke/remove the source and verify it cannot be reopened from stale results.
- **V3-GO-SEARCH-ACC-02** — Test AI/default mode switching, narrow layout, Escape focus return and unavailable Mesh targets.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Go, universal search and entity navigation — Service registry and compatibility handshake; Default surface (part 01) — `technical/go-search-01.md` (839 words; V2-B0509, V2-B0510, V2-B0511…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
