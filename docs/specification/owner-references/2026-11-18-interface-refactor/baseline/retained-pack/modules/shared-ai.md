# Shared AI controls and artifact collaboration

Module ID: `shared-ai` · 9to1 Development Specification v3

## Prerequisites

- **CUI, themes and shared interaction system** — Typed UI components, theme tokens/materials, layout, focus, motion, accessibility and shared control states.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Dulche runtime, requests and execution services** — Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.
- **Global Assistants, Specialists and collaborators** — Persistent global Assistant/Specialist identities, Teams, canonical conversations and editor collaboration.

## Overview

Contextual AI is shared across app surfaces, with canonical model routing, scoped artifact context and global Assistants/Specialists. Its visual affordances remain contextual to the owning app and never replace the app’s own editor model.

Current owner overrides: V3-OWNER-002, V3-OWNER-006, V3-OWNER-008.

## Layouts & visible states

- **V3-SHARED-AI-LAY-01** — Editor AI bars expose Read-only/Write and actual target context. Spaces exposes Conversation permissions instead. Keep AI bars clear of bottom tools, timelines and active popovers.
- **V3-SHARED-AI-LAY-02** — Header collaborator avatars and pickers show real configured Assistant/Specialist identities and their current artifact roles. Review has an inspectable proposed change state before application.
- **V3-SHARED-AI-LAY-03** — Use the canonical model picker and notification access; do not duplicate hidden provider routing controls per app.

## Features & behaviour

- **V3-SHARED-AI-FEA-01** — Read-only AI may explain, inspect and propose within granted scope without mutating the artifact. Write remains constrained by current permission and any required action approvals.
- **V3-SHARED-AI-FEA-02** — Collaborator proposals use owning-app semantic edits, expected revisions, diff/preview and meaningful undo. Accept/reject applies a defined change set, not arbitrary narrated success.
- **V3-SHARED-AI-FEA-03** — Show task/tool-level activity, approval blockers, evidence and output links. Thinking text is a concise work summary, never private internal chain-of-thought.
- **V3-SHARED-AI-FEA-04** — Provide Steer/Stop/Queue/Resume semantics only for valid runtime states and maintain the request draft until admission succeeds.

## Canonical entities & persistence

- **V3-SHARED-AI-ENT-01** — Link user request, Assistant/Specialist, TaskID/RunID, target artifact/revision, proposal and receipt. Opening the same collaborator from Home or an editor preserves identity.
- **V3-SHARED-AI-ENT-02** — A collaborator presence badge is a view of canonical task/role state rather than a second chat store or independent agent runtime.

## Permissions & recovery

- **V3-SHARED-AI-PER-01** — Do not expand context or write scope automatically when adding a collaborator. Display inherited, restricted, waiting approval and unavailable states truthfully.
- **V3-SHARED-AI-PER-02** — Recover partial work without reapplying accepted changes or claiming failed exports are available.

## Acceptance checks

- **V3-SHARED-AI-ACC-01** — Exercise a read-only explanation, scoped proposal, review/reject, accepted revision and stale-target conflict in two different editors.
- **V3-SHARED-AI-ACC-02** — Verify toolbar overlap, focus restoration, reduced motion and mobile access to the same permission controls.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Shared AI controls and artifact collaboration — Chat bar — expanded-mode formatting and emoji controls; Chat-bar control acceptance (part 01) — `technical/shared-ai-01.md` (1,872 words; V2-B0312, V2-B0313, V2-B0314…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
