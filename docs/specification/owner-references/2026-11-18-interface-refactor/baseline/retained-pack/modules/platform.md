# Platform, app registry and host integration

Module ID: `platform` · 9to1 Development Specification v3

## Prerequisites

- **Authority, scope and implementation reading map** — Current source precedence, narrow owner overrides, source accounting and the required module structure.

## Overview

The ecosystem shares canonical applications, artifacts and services across native, browser, mobile and 9to1OS hosts. Host presentation may differ while object identity, permissions and typed actions remain the same.

Current owner overrides: V3-OWNER-010.

## Layouts & visible states

- **V3-PLATFORM-LAY-01** — Expose authentic app launch/create/open/import/recents surfaces separately from active editor workspaces. A launch page must not remain as a decorative dashboard above an open document.
- **V3-PLATFORM-LAY-02** — Keep a familiar Home house icon in app headers. Respect host-safe top regions, viewport insets and narrow-screen drawer behaviour.
- **V3-PLATFORM-LAY-03** — Distinguish installed, available, offline, unsupported, starting, ready and failed states; do not make a static tile imply a working runtime.

## Features & behaviour

- **V3-PLATFORM-FEA-01** — Register app capabilities and routes once; Home, Browse, OS and Launcher use that registry. Opening an artifact resolves its registered owner and stable target, including embedded AppSurfaces.
- **V3-PLATFORM-FEA-02** — Offer manual local editing without requiring sign-in, a connected Files provider or a running AI service unless the underlying operation itself needs it.
- **V3-PLATFORM-FEA-03** — Preserve the retained platform/backend language, donor reuse and UI/API parity requirements. CUI wraps existing powerful engines rather than replacing them with a reduced demonstration.
- **V3-PLATFORM-FEA-04** — Integrate typed creation, selection, edits, exports, task events and deep links. Automation uses these semantic APIs, not simulated pointer interaction.
- **V3-PLATFORM-FEA-05** — Native documents/projects must not impose arbitrary global character, word, page, slide, block, object or file-byte caps merely to simplify rendering or saves. Real device, quota, format, address-space and security limits remain explicit; never truncate silently.
- **V3-PLATFORM-FEA-06** — Prioritise current input, caret, selection, scroll, drag and undo over distant work. Virtualise visible/nearby pages, slides, cells, blocks, nodes and time ranges, use indexed deep navigation and bounded adaptive caches, and keep unrendered content canonical, searchable, accessible and exportable.
- **V3-PLATFORM-FEA-07** — Use incremental dependency-aware layout/calculation and addressable/chunked persistence; do not reserialise or rerender an entire large artifact on each keystroke. Stream/resume/cancel large import/export with integrity checks, honest progress and safe resource-pressure recovery. Lower preview fidelity or buffers before document fidelity.

## Canonical entities & persistence

- **V3-PLATFORM-ENT-01** — Keep stable AppID, artifact ID, TabID, route, revision and request identities. Do not duplicate documents when moving between native, embedded and browser views.
- **V3-PLATFORM-ENT-02** — Draft, active configuration, current view state and published output are separate records. Closing a view does not implicitly cancel an admitted background Task/Run.

## Permissions & recovery

- **V3-PLATFORM-PER-01** — Check permissions at execution and dereference time, including cached/search and embedded surfaces. Explicitly distinguish unavailable capabilities from denied permissions.
- **V3-PLATFORM-PER-02** — Preserve unsaved work through host navigation, process restart and session restore; Save/Discard/Cancel must refer to actual dirty state.

## Acceptance checks

- **V3-PLATFORM-ACC-01** — Open the same artifact through Home, Files, a host tab and a Space; verify stable identity, current revision and consistent grants.
- **V3-PLATFORM-ACC-02** — Test app launch→create/open→edit→save→reopen, narrow layout, offline recovery and unsupported-platform states.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

APP-AC-001, APP-AC-002, APP-AC-003, APP-AC-004, APP-AC-005, APP-AC-006, APP-AC-007, APP-AC-008, APP-AC-009, LAUNCH-001, LAUNCH-002, LAUNCH-003, LAUNCH-004, LAUNCH-005, LAUNCH-006, TF-X01, TF-X02, TF-X03, TF-X04, TF-X05, LE-12, LE-13, LE-14, UX27-OTHER-018, SECTION-13, UX27-TEST-100, UX27-SHARED-020, UX27-ACCEPT-005, UX27-TEST-101, UX27-MAIN-001, UX27-MAIN-002, UX27-MAIN-003, UX27-ACCEPT-001, UX27-ACCEPT-002, UX27-ACCEPT-004, UX27-TEST-102, UX27-AUTH-003, UX27-AUTH-006, UX27-COVERAGE-001, UX27-COVERAGE-002, UX27-COVERAGE-004, UX27-ACCEPT-003, UX27-ACCEPT-006, SCALE-001, SCALE-002, SCALE-003, SCALE-004, SCALE-005, SCALE-006, SCALE-007, SCALE-008, SCALE-010, SCALE-011, OTHER-APPS-SCALE-201, SCALE-AC-201, SCALE-AC-202, SCALE-009

## Technical reading map

- Platform, app registry and host integration — General Rules; Specification authority (part 01) — `technical/platform-01.md` (2,116 words; V2-B0093, V2-B0094, V2-B0095…).
- Platform, app registry and host integration — Launch Screens; Distinguish screen purposes (part 02) — `technical/platform-02.md` (1,517 words; V2-B0325, V2-B0326, V2-B0327…).
- Platform, app registry and host integration — Cross-app delivery and acceptance; Embeds, Boards pages and conversational editing (part 03) — `technical/platform-03.md` (2,400 words; V2-B2170, V2-B2246, V2-B2283…).
- Platform, app registry and host integration — Integration; Large board keeps every block and stroke (part 04) — `technical/platform-04.md` (1,192 words; V2-B2338, V2-B2900, V2-B2901…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
