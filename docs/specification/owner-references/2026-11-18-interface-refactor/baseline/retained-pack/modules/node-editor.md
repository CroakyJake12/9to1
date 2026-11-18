# Node Editor — shared graph framework

Module ID: `node-editor` · 9to1 Development Specification v3

## Prerequisites

- **CUI, themes and shared interaction system** — Typed UI components, theme tokens/materials, layout, focus, motion, accessibility and shared control states.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.

## Overview

The Home-owned Node Graph Framework is the common editor, validator and runtime presentation system used by Automations, AI Studio and custom Spaces. Apps supply domain node semantics; they do not create competing graph engines.

## Layouts & visible states

- **V3-NODE-EDITOR-LAY-01** — Expose typed nodes, ports, connections, palette/search, selection/grouping, pan/zoom, validation, properties and real runtime highlights.
- **V3-NODE-EDITOR-LAY-02** — Recognisable categories include Trigger, Condition, Event, Logic, App and Files. Use accessible theme-aware category treatments and labels rather than colour alone.

## Features & behaviour

- **V3-NODE-EDITOR-FEA-01** — Reuse stable graph schemas and registered node APIs. Validate types, missing dependencies, cycles, bounded execution, policy intersections and unsupported nodes before activation.
- **V3-NODE-EDITOR-FEA-02** — Support undo/redo, copy/paste, keyboard/touch manipulation, graph search and contextual AI edits through typed commands.
- **V3-NODE-EDITOR-FEA-03** — Preview, validate and activate are separate operations. Invalid draft logic must not replace the last known-good active revision.

## Canonical entities & persistence

- **V3-NODE-EDITOR-ENT-01** — Persist GraphID, NodeID, PortID, connection IDs, schema/version, draft/active revisions and runtime references. View pan/zoom is separate from graph logic.
- **V3-NODE-EDITOR-ENT-02** — App-specific settings and graph projections share the same canonical configuration; switching representations cannot erase hidden logic.

## Permissions & recovery

- **V3-NODE-EDITOR-PER-01** — Graphs execute only authorised app/Dulche/Files actions under the shared permission model. A new node cannot manufacture a model backend, memory broker, scheduler or privilege.
- **V3-NODE-EDITOR-PER-02** — Rollback changes future configuration; it does not pretend to undo external effects already performed.

## Acceptance checks

- **V3-NODE-EDITOR-ACC-01** — Round-trip a graph through simplified settings and Expert view without losing nodes, IDs or advanced behaviour.
- **V3-NODE-EDITOR-ACC-02** — Test invalid ports/cycles, revoked tools, disconnected dependencies, draft recovery and genuine runtime highlighting.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Node Editor — shared graph framework — Node Graph Framework (part 01) — `technical/node-editor-01.md` (241 words; V2-B0539, V2-B0540, V2-B0541…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.
