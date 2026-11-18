# Cross-app acceptance and evidence

Module ID: `acceptance` · 9to1 Development Specification v3

## Prerequisites

- **Authority, scope and implementation reading map** — Current source precedence, narrow owner overrides, source accounting and the required module structure.
- **Platform, app registry and host integration** — Canonical app registry, artifact routes, native/browser embedding, lifecycle and semantic UI/API parity.
- **CUI, themes and shared interaction system** — Typed UI components, theme tokens/materials, layout, focus, motion, accessibility and shared control states.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Acceptance verifies real behaviour, compatibility, permission boundaries and recovery across apps. The supplied scenario chapters remain retained; mock-up review and source coverage are design evidence only.

## Layouts & visible states

- **V3-ACCEPTANCE-LAY-01** — Exercise actual screens at supported desktop, narrow/mobile and native/browser/OS hosts, including focus, scroll, popup placement, contrast and reduced motion.
- **V3-ACCEPTANCE-LAY-02** — Include real empty/loading/unavailable/permission/approval/error/conflict and recovery states, not only the attractive success screenshot.

## Features & behaviour

- **V3-ACCEPTANCE-FEA-01** — Run source-backed end-to-end scenarios through canonical APIs and visible UI. Evidence must connect requested action, actual state mutation/result, persisted reopen and meaningful failure handling.
- **V3-ACCEPTANCE-FEA-02** — Validate UI/API parity, app ownership, shared theme consistency, local/manual workflows and donor feature coverage where required.
- **V3-ACCEPTANCE-FEA-03** — Shared prerequisite tests may be reused with explicit version/evidence references; do not substitute a shallow screenshot test for substantive backend behaviour.

## Canonical entities & persistence

- **V3-ACCEPTANCE-ENT-01** — Record test/scenario IDs, build/version, environment, input/target revision, observed result and evidence. Preserve failed/partial outcomes distinctly from pass.
- **V3-ACCEPTANCE-ENT-02** — Source acceptance IDs remain stable for traceability even when current UI placement changes.

## Permissions & recovery

- **V3-ACCEPTANCE-PER-01** — Permission/security and independent-review gates remain mandatory; time/review budget exhaustion cannot turn an untested result into VERIFIED.
- **V3-ACCEPTANCE-PER-02** — Reruns and test fixtures must not repeat consequential real external actions without their defined safe test scope.

## Acceptance checks

- **V3-ACCEPTANCE-ACC-01** — Cover canonical artifact opened in multiple hosts, offline/restart, grant revocation, concurrent revision, failure/retry and derived export verification.
- **V3-ACCEPTANCE-ACC-02** — Reconcile every current module’s acceptance list with retained source scenarios and explicitly document gaps rather than asserting completeness from the atlas.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

UX27-ACCEPT-001, UX27-TEST-090, UX27-OTHER-001, UX27-OTHER-002, UX27-TEST-091, UX27-OTHER-003, UX27-OTHER-004, UX27-TEST-092, UX27-OTHER-005, UX27-TEST-093, UX27-OTHER-006, UX27-TEST-094, UX27-OTHER-007, UX27-TEST-095, UX27-OTHER-008, UX27-OTHER-009, UX27-TEST-096, UX27-OTHER-010, UX27-OTHER-011, UX27-TEST-097, UX27-OTHER-012, UX27-OTHER-017, UX27-TEST-098, UX27-OTHER-013, UX27-OTHER-014, UX27-TEST-099, UX27-OTHER-015, UX27-OTHER-016, UX27-OTHER-018, SPUI-199, SPUI-200

## Technical reading map

- Cross-app acceptance and evidence — 9to1 V2 — Acceptance scenarios, 27 October 2026; Other apps (part 01) — `technical/acceptance-01.md` (1,354 words; V2-B2391, V2-B2392, V2-B2849…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
