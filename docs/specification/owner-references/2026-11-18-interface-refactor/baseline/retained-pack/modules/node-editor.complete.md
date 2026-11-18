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


# Retained technical requirements

# Node Editor — shared graph framework — Node Graph Framework (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0539 · v2 lines 2675–2675

Node Graph Framework

### V2-B0540 · v2 lines 2678–2678

Home owns the shared Node Graph Framework used by Automations and Space graph editors. It is an editing/schema/validation/execution-visualisation framework, not a second automation or AI runtime. Product surfaces register domain-specific events, actions and node types; Dulche and owning apps execute the resulting authorised runtime actions.

### V2-B0541 · v2 lines 2681–2681

The framework MUST provide a versioned graph data model, stable GraphID/NodeID/PortID identity, node registry, typed input/output ports, typed connections, node configuration schemas, pan/zoom/selection, grouping/comments, copy/paste/duplicate, undo/redo, validation, Draft/Active graph revision support, execution-state overlays and contextual AI-bar integration.

### V2-B0542 · v2 lines 2684–2684

Connections MUST be type-checked. Incompatible ports cannot connect unless an explicit registered conversion node exists. Nodes and graph actions MUST reference canonical stable app/capability/entity IDs rather than screen coordinates or UI implementation details.

### V2-B0543 · v2 lines 2687–2687

The framework MUST support product-defined capability profiles. For example, built-in Spaces receive a constrained Events & Extensions node registry while user-created Spaces and Automations may receive broader registries. A capability profile cannot expose an action prohibited by the product/security contract.

### V2-B0544 · v2 lines 2690–2690

Graph mutation from the contextual AI bar MUST use typed graph APIs. AI may add/remove/configure nodes and connections only within the current capability profile and permissions, and MUST NOT simulate mouse/keyboard graph editing.

### V2-B0545 · v2 lines 2693–2702

9to1.NodeGraph.Get(graphID);
9to1.NodeGraph.Create(config);
9to1.NodeGraph.AddNode(graphID, nodeType, config?);
9to1.NodeGraph.RemoveNode(graphID, nodeID);
9to1.NodeGraph.SetProperty(graphID, nodeID, property, value);
9to1.NodeGraph.Connect(graphID, outputPortID, inputPortID);
9to1.NodeGraph.Disconnect(graphID, connectionID);
9to1.NodeGraph.Validate(graphID, revision?);
9to1.NodeGraph.Activate(graphID, revision);
9to1.NodeGraph.ListNodeTypes(capabilityProfile);

### V2-B0546 · v2 lines 2705–2705

Node Graph Framework compatibility and migration MUST be versioned. When a node/hook schema is retired, affected graphs enter a diagnosable migration/needs-attention state rather than silently dropping nodes or connections.
