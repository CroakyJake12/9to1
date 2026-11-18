# Generative UI and live artifact output

Module ID: `generative-ui` · 9to1 Development Specification v3

## Prerequisites

- **CUI, themes and shared interaction system** — Typed UI components, theme tokens/materials, layout, focus, motion, accessibility and shared control states.
- **Dulche runtime, requests and execution services** — Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.

## Overview

Generative UI uses typed safe CUI components bound to inspectable state/actions. It can appear inline in Spaces and as a canonical output, while app-owned documents and exports retain their real artifact semantics.

Current owner overrides: V3-OWNER-008.

## Layouts & visible states

- **V3-GENERATIVE-UI-LAY-01** — Render meaningful interactive forms, charts, tables, selectors and progress states, with a visible connection to the active task and sources.
- **V3-GENERATIVE-UI-LAY-02** — Live Present/Canvas progress shows semantic work on the canonical artifact and high-level completed/current actions. Generated file cards show name, owner, revision, format and truthful availability.

## Features & behaviour

- **V3-GENERATIVE-UI-FEA-01** — Validate generated component schemas and actions against the safe registry. Arbitrary text, HTML or code is not automatically trusted UI or an executable permission grant.
- **V3-GENERATIVE-UI-FEA-02** — Keep ephemeral conversational state separate from promoted durable artifacts. A promotion/save action creates or updates the owning artifact through its typed API.
- **V3-GENERATIVE-UI-FEA-03** — Open resolves the canonical artifact; Download requires verified durable bytes. Failed PDF/export retains the saved native source and offers retry from the known revision.
- **V3-GENERATIVE-UI-FEA-04** — Preserve interaction state and accessibility during streaming updates; invalid component output must yield a recoverable error without deleting prior valid content.

## Canonical entities & persistence

- **V3-GENERATIVE-UI-ENT-01** — Persist component/state/action IDs where needed, schema version, source Task/Run, artifact ID/revision and export receipt. Generated controls cannot maintain a hidden parallel copy of app state.
- **V3-GENERATIVE-UI-ENT-02** — Store a durable export manifest only after bytes are materialized and verified; a rendered preview is not sufficient.

## Permissions & recovery

- **V3-GENERATIVE-UI-PER-01** — Use current app grants for every generated action. Sandbox untrusted content, constrain external links/actions and recheck revoked source access.
- **V3-GENERATIVE-UI-PER-02** — Retry idempotently and distinguish export-only failure from native-save failure; never rerun unrelated completed generation work automatically.

## Acceptance checks

- **V3-GENERATIVE-UI-ACC-01** — Test interactive state changes, invalid schema, source revocation, narrow layout, keyboard accessibility and reload recovery.
- **V3-GENERATIVE-UI-ACC-02** — Verify Open/Download receipts and failed-export recovery against real bytes and the same canonical source revision.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Generative UI and live artifact output — Generative UI; Rendering and trust boundary (part 01) — `technical/generative-ui-01.md` (1,108 words; V2-B2107).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
