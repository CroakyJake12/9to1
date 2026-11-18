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
