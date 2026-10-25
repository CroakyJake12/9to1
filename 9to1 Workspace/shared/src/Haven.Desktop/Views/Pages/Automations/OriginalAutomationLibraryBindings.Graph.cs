#if !ANDROID
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Automations;

/// <summary>Read-only inspection of the SAME source-issued saved graph snapshot.
/// These private presentation targets grant no mutation, publication or execution.</summary>
internal sealed partial class OriginalAutomationLibraryBindings
{
    private const int GraphPageSize = 24;
    private sealed record GraphSnapshot(RowTarget Selection, ScheduledGraphAutomationPayload Payload,
        AutomationGraphDefinition Graph);
    private sealed record GraphNodeTarget(GraphSnapshot Snapshot, AutomationGraphNodeDefinition Node);
    private sealed record GraphNodeView(string Key, string Title, string Category, string Identity, object Target);
    private sealed record GraphLinkView(string Key, string Title, string Detail, object Target);
    private sealed record GraphLine(string Key, string Title, string Detail);
    private GraphSnapshot? _graphSnapshot;
    private IReadOnlyList<GraphNodeView> _graphRows = [];
    private IReadOnlyList<GraphLinkView> _graphLinks = [];
    private IReadOnlyList<GraphLine> _graphDiagnostics = [], _graphAllDiagnostics = [], _graphPorts = [], _graphParameters = [];
    private AutomationGraphNodeDefinition? _graphSelectedNode;
    private string _graphSearch = "", _graphStatus = "", _graphIdentity = "", _graphDigest = "",
        _graphNodeTitle = "Select a node to inspect its stored settings", _graphNodeIdentity = "",
        _graphNodeAction = "", _graphNodeDevice = "", _graphPortSummary = "", _graphFieldSummary = "";
    private int _graphPage, _graphMatchCount, _graphLinkPage, _graphConnectionCount, _graphDiagnosticPage;
    private bool _hasStoredGraph;
    private bool CanInspectGraph => !_retiring && !_busy && _graphSnapshot is { } snapshot &&
        ReferenceEquals(_selectedTarget, snapshot.Selection) && snapshot.Selection.Epoch == _epoch && _issued.Contains(snapshot.Selection);
    private void ClearGraphInspection()
    {
        _graphSnapshot = null; _graphSelectedNode = null; _hasStoredGraph = false;
        _graphRows = []; _graphLinks = []; _graphDiagnostics = []; _graphAllDiagnostics = []; _graphPorts = []; _graphParameters = [];
        _graphSearch = _graphStatus = _graphIdentity = _graphDigest = _graphNodeIdentity = _graphNodeAction =
            _graphNodeDevice = _graphPortSummary = _graphFieldSummary = "";
        _graphNodeTitle = "Select a node to inspect its stored settings";
        _graphPage = _graphMatchCount = _graphLinkPage = _graphConnectionCount = _graphDiagnosticPage = 0;
    }
    private void LoadGraphInspection(RowTarget selection)
    {
        ClearGraphInspection(); var saved = selection.Row.Value;
        _hasStoredGraph = ScheduledGraphAutomationPayloadCodec.IsPayload(saved.Instruction);
        if (!_hasStoredGraph)
        {
            _graphStatus = "This saved automation contains plain instructions, with no embedded graph snapshot.";
            return;
        }
        if (CanonicalAutomationGraphReferenceCodec.IsReference(saved.Instruction))
        {
            _graphStatus = "This saved definition references a canonical Home graph. The actual current graph must be observed through its protected source; the reference alone grants no readiness.";
            return;
        }
        _graphDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(saved.Instruction))).ToLowerInvariant();
        if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(saved.Instruction, out var payload) ||
            !AutomationGraphCodec.TryDeserialize(payload.GraphJson, out var graph))
        {
            _graphStatus = "The stored graph payload could not be read. Its saved bytes are preserved; no replacement graph was created.";
            return;
        }
        if (!TryRetainStoredConnectionIds(payload.GraphJson, graph, out graph))
        {
            _graphStatus = "The stored graph has ambiguous connection data. Its original settings are preserved.";
            return;
        }
        _graphIdentity = $"Automation {saved.Id:D} · Workflow {payload.WorkflowId:D} · Trigger {payload.TriggerNodeId:D} · Payload schema {payload.Version} · Graph schema {graph.Version}";
        // Inspection is deliberately bounded independently of persistence. Large
        // saved definitions are preserved, never truncated into a different graph.
        if (graph.Nodes.Count > 512 || graph.Edges.Count > 2048)
        {
            _graphStatus = $"This saved graph has {graph.Nodes.Count} nodes and {graph.Edges.Count} connections. The inspector supports up to 512 nodes and 2048 connections; saved settings are preserved.";
            return;
        }
        if (graph.Nodes.Any(node => node.Parameters is null || node.EffectivePorts.Any(port => port is null)) ||
            graph.Edges.Any(edge => edge.FromPortId is null || edge.ToPortId is null))
        {
            _graphStatus = "The stored graph contains a missing parameter, port or connection value. Its original settings are preserved.";
            return;
        }
        var snapshot = new GraphSnapshot(selection, payload, graph); _graphSnapshot = snapshot;
        var issues = AutomationGraphRunner.ValidateStructure(graph).Select(issue =>
            (issue.Code, issue.Message, issue.NodeId, issue.EdgeId)).ToList();
        foreach (var edge in graph.Edges.Where(edge => edge.Id == Guid.Empty))
            issues.Add(("edge.id.unavailable", "A connection has no stored non-empty ID. The inspector has not generated a replacement identity.", null, null));
        issues.AddRange(AutomationGraphEditorAdapter.ValidateConfiguration(graph).Select(issue =>
            (issue.Code, issue.Message, issue.NodeId, issue.EdgeId)));
        if (payload.WorkflowId != saved.Id)
            issues.Add(("graph.workflow.identity", "The embedded workflow ID differs from this saved Automation ID. Inspect the original linkage before any future publication.", null, null));
        if (!graph.Nodes.Any(node => node.Id == payload.TriggerNodeId))
            issues.Add(("graph.trigger.missing", "The saved trigger does not identify a node in this graph.", null, null));
        _graphAllDiagnostics = Array.AsReadOnly(issues.Select((issue, index) => new GraphLine("graph-issue-" + index,
            issue.Code, issue.Message + (issue.NodeId is { } node ? $" · Node {node:D}" : "") +
                (issue.EdgeId is { } edge ? $" · Connection {edge:D}" : ""))).ToArray());
        _graphStatus = $"Saved snapshot: {graph.Nodes.Count} nodes · {graph.Edges.Count} connections · {issues.Count} structure/configuration diagnostic(s). Permissions, live capabilities and execution readiness have not been checked.";
        RefreshGraphRows(); RefreshGraphDiagnostics();
    }
    private void LoadCanonicalGraphInspection(RowTarget selection, Haven.Application.NodeGraph.GraphDocument graph)
    {
        if (!ReferenceEquals(DemandRow(selection), _selectedTarget) ||
            !CanonicalAutomationGraphReferenceCodec.TryDeserialize(selection.Row.Value.Instruction, out var reference) ||
            graph.GraphId != reference.GraphId || graph.Revision != reference.DraftRevision)
            throw new UnauthorizedAccessException("The actual current saved selection and source-observed canonical graph must agree.");
        ClearGraphInspection(); _hasStoredGraph = true;
        _graphIdentity = $"Automation {selection.Row.Value.Id:D} · Canonical graph {graph.GraphId:D} · Revision {graph.Revision} · {graph.State} · Schema {graph.SchemaVersion} · Profile {graph.CapabilityProfileId}";
        _graphDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(graph))).ToLowerInvariant();
        _graphStatus = $"Actual canonical Home Draft: {graph.Nodes.Count} nodes · {graph.Connections.Count} connections. This first saved step is disabled and unscheduled; it cannot run.";
        _graphNodeTitle = graph.Nodes.Count == 0 ? "This saved Draft has no nodes yet" : "Canonical graph inspection requires its registered editor";
        _graphAllDiagnostics = [new("canonical-draft-empty", "draft.incomplete", "An empty saved Draft has no runnable graph. Configure supported nodes and a schedule before execution can be admitted.")];
        RefreshGraphDiagnostics();
    }
    private void LoadCanonicalGraphRecoveryInspection(RowTarget selection,
        ICanonicalAutomationOriginalGraphPublicationRecovery recovery)
    {
        if (recovery.AutomationId != selection.Row.Value.Id || recovery.PublishedGraph.GraphId != recovery.GraphId)
            throw new UnauthorizedAccessException("The recovery observation does not match the selected Automation and Home graph.");
        ClearGraphInspection(); _hasStoredGraph = true;
        var graph = recovery.PublishedGraph;
        _graphIdentity = $"Automation {recovery.AutomationId:D} · Home graph {recovery.GraphId:D} · Published revision {recovery.PublishedGraphRevision} · {recovery.Kind}";
        _graphDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(graph))).ToLowerInvariant();
        _graphStatus = $"Home records an audited {recovery.Kind} publication of {graph.Nodes.Count} nodes and {graph.Connections.Count} connections. The saved Automation still points to revision {recovery.ExpectedGraphRevision}; review recovery to bind the existing Home revision. No graph will be republished.";
        _graphNodeTitle = "Published Home graph is retained for recovery";
        _graphAllDiagnostics = [new("graph-publication-binding-pending", "binding.recovery.required",
            "The durable Home graph publication is audited, but its separate Automation definition binding did not complete.", null, null)];
        RefreshGraphDiagnostics();
    }
    private object? GraphValue(string path) => path switch
    {
        "HasStoredGraph" => _hasStoredGraph, "GraphStatus" => _graphStatus,
        "GraphIdentity" => _graphIdentity, "GraphDigest" => _graphDigest,
        "GraphNodes" => _graphRows, "GraphNodeSearch" => _graphSearch,
        "CanInspectGraph" => CanInspectGraph, "GraphDiagnostics" => _graphDiagnostics,
        "GraphNodeTitle" => _graphNodeTitle, "GraphNodeIdentity" => _graphNodeIdentity,
        "GraphNodeAction" => _graphNodeAction, "GraphNodeDevice" => _graphNodeDevice,
        "GraphPorts" => _graphPorts, "GraphParameters" => _graphParameters,
        "GraphPortSummary" => _graphPortSummary, "GraphFieldSummary" => _graphFieldSummary,
        "GraphConnections" => _graphLinks,
        "GraphDiagnosticPageLabel" => _graphAllDiagnostics.Count == 0 ? "No structure or saved-configuration diagnostics" :
            $"Diagnostics {_graphDiagnosticPage * GraphPageSize + 1}–{Math.Min((_graphDiagnosticPage + 1) * GraphPageSize, _graphAllDiagnostics.Count)} of {_graphAllDiagnostics.Count}",
        "CanGraphDiagnosticsBack" => GraphActionAvailable("graph-diagnostics-back"),
        "CanGraphDiagnosticsNext" => GraphActionAvailable("graph-diagnostics-next"),
        "GraphPageLabel" => _graphMatchCount == 0 ? "No matching nodes" : $"Nodes {_graphPage * GraphPageSize + 1}–{Math.Min((_graphPage + 1) * GraphPageSize, _graphMatchCount)} of {_graphMatchCount}",
        "GraphConnectionPageLabel" => _graphSelectedNode is null ? "" : _graphConnectionCount == 0
            ? "No connections for this node" : $"Connections {_graphLinkPage * GraphPageSize + 1}–{Math.Min((_graphLinkPage + 1) * GraphPageSize, _graphConnectionCount)} of {_graphConnectionCount}",
        "CanGraphBack" => GraphActionAvailable("graph-back"), "CanGraphNext" => GraphActionAvailable("graph-next"),
        "CanGraphLinksBack" => GraphActionAvailable("graph-links-back"), "CanGraphLinksNext" => GraphActionAvailable("graph-links-next"),
        "ShowPlainInstruction" => !_editGraph, _ => null
    };
    private bool GraphActionAvailable(string action) => CanInspectGraph && (action switch
    {
        "graph-node" => true, "graph-back" => _graphPage > 0,
        "graph-next" => (_graphPage + 1) * GraphPageSize < _graphMatchCount,
        "graph-links-back" => _graphLinkPage > 0,
        "graph-links-next" => (_graphLinkPage + 1) * GraphPageSize < _graphConnectionCount,
        "graph-diagnostics-back" => _graphDiagnosticPage > 0,
        "graph-diagnostics-next" => (_graphDiagnosticPage + 1) * GraphPageSize < _graphAllDiagnostics.Count, _ => false
    });
    private bool SetGraphValue(string path, string text)
    {
        if (path != "GraphNodeSearch" || text.Length > 256 || !CanInspectGraph) return false;
        _work.RunSynchronous(original => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        { _graphSearch = text; _graphPage = 0; RefreshGraphRows(); Changed(); return true; }));
        return true;
    }
    private void RunGraphAction(string action, object? parameter)
    {
        if (!GraphActionAvailable(action)) throw new InvalidOperationException("Open the current saved graph before inspecting a node.");
        _work.RunSynchronous(original => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            if (action == "graph-node")
            {
                if (parameter is not GraphNodeTarget target || !ReferenceEquals(target.Snapshot, _graphSnapshot) ||
                    !ReferenceEquals(DemandRow(target.Snapshot.Selection), _selectedTarget) ||
                    !target.Snapshot.Graph.Nodes.Any(node => ReferenceEquals(node, target.Node)))
                    throw new InvalidOperationException("This node belongs to an older saved graph snapshot. Reopen the current automation.");
                SelectGraphNode(target.Node);
            }
            else
            {
                if (parameter is not null) throw new ArgumentException("Graph paging does not accept a node target.");
                if (action == "graph-back") { _graphPage--; RefreshGraphRows(); }
                else if (action == "graph-next") { _graphPage++; RefreshGraphRows(); }
                else if (action == "graph-links-back") { _graphLinkPage--; RefreshGraphLinks(); }
                else if (action == "graph-links-next") { _graphLinkPage++; RefreshGraphLinks(); }
                else if (action == "graph-diagnostics-back") { _graphDiagnosticPage--; RefreshGraphDiagnostics(); }
                else if (action == "graph-diagnostics-next") { _graphDiagnosticPage++; RefreshGraphDiagnostics(); }
            }
            Changed(); return true;
        }));
    }
    private void RefreshGraphDiagnostics() => _graphDiagnostics = Array.AsReadOnly(_graphAllDiagnostics
        .Skip(_graphDiagnosticPage * GraphPageSize).Take(GraphPageSize).ToArray());
    private void RefreshGraphRows()
    {
        if (_graphSnapshot is not { } snapshot) return;
        var nodes = snapshot.Graph.Nodes.Where(node => string.IsNullOrWhiteSpace(_graphSearch) ||
            new[] { node.Title, node.Category, node.ActionKey, node.Id.ToString("D"), node.DeviceTarget?.DisplayName, node.DeviceTarget?.Id }
                .Any(value => value?.Contains(_graphSearch, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
        _graphMatchCount = nodes.Length;
        _graphRows = Array.AsReadOnly(nodes.Skip(_graphPage * GraphPageSize).Take(GraphPageSize).Select(node =>
            new GraphNodeView(node.Id.ToString("N"), Display(node.Title, node.Category), node.Category ?? "(missing category)",
                node.Id.ToString("D") + (node.Id == snapshot.Payload.TriggerNodeId ? " · saved trigger" : ""),
                new GraphNodeTarget(snapshot, node))).ToArray());
    }
    private void SelectGraphNode(AutomationGraphNodeDefinition node)
    {
        _graphSelectedNode = node; _graphLinkPage = 0;
        _graphNodeTitle = Display(node.Title, node.Category); _graphNodeIdentity = node.Id.ToString("D");
        _graphNodeAction = "Stored action: " + (string.IsNullOrWhiteSpace(node.ActionKey) ? "none" : Display(node.ActionKey));
        _graphNodeDevice = node.DeviceTarget is { } device
            ? $"Stored target: {Display(device.DisplayName)} · {device.Id} · {device.Platform}" : "Stored device target: none";
        _graphPortSummary = node.Ports is { Count: > 0 } ? "Ports explicitly stored in this graph."
            : "No ports are explicitly stored. The current Automation model supplies the default flow ports shown below.";
        _graphPorts = Array.AsReadOnly(node.EffectivePorts.Take(64).Select((port, index) => new GraphLine("graph-port-" + index,
            Display(port.Label, port.Id), $"{port.Id} · {port.Direction} · {port.DataType} · " +
                (port.AllowsMultipleConnections ? "multiple connections allowed" : "single connection"))).ToArray());
        _graphParameters = Array.AsReadOnly(node.Parameters.Take(64).Select((pair, index) => new GraphLine("graph-parameter-" + index,
            Display(pair.Key), IsSensitiveKey(pair.Key) ? "Hidden sensitive value" : pair.Value is null ? "(null)" : Display(pair.Value))).ToArray());
        _graphFieldSummary = $"Showing {Math.Min(node.EffectivePorts.Count, 64)} of {node.EffectivePorts.Count} effective ports and {Math.Min(node.Parameters.Count, 64)} of {node.Parameters.Count} stored parameters. Long values are shortened for display; sensitive parameter values are hidden.";
        RefreshGraphLinks();
    }
    private void RefreshGraphLinks()
    {
        if (_graphSnapshot is not { } snapshot || _graphSelectedNode is not { } selected) return;
        var edges = snapshot.Graph.Edges.Select((edge, index) => (edge, index))
            .Where(value => value.edge.FromNodeId == selected.Id || value.edge.ToNodeId == selected.Id).ToArray();
        _graphConnectionCount = edges.Length;
        _graphLinks = Array.AsReadOnly(edges.Skip(_graphLinkPage * GraphPageSize).Take(GraphPageSize).Select(value =>
        {
            var edge = value.edge; var outgoing = edge.FromNodeId == selected.Id;
            var other = snapshot.Graph.Nodes.First(node => node.Id == (outgoing ? edge.ToNodeId : edge.FromNodeId));
            return new GraphLinkView("graph-connection-" + value.index,
                (outgoing ? "To " : "From ") + Display(other.Title, other.Category),
                (edge.Id == Guid.Empty ? "No stored non-empty connection ID" : $"Connection {edge.Id:D}") + $" · {edge.FromPortId} → {edge.ToPortId}" +
                    (string.IsNullOrWhiteSpace(edge.Branch) ? "" : " · Branch " + Display(edge.Branch)) +
                    (string.IsNullOrWhiteSpace(edge.Label) ? "" : " · " + Display(edge.Label)), new GraphNodeTarget(snapshot, other));
        }).ToArray());
    }
    private bool TryGetGraphItemValue(object item, string path, out object? value)
    {
        value = item switch
        {
            GraphNodeView row when _graphRows.Any(current => ReferenceEquals(current, row)) => path switch
            { "Key" => row.Key, "Title" => row.Title, "Category" => row.Category, "Identity" => row.Identity,
                "Target" => row.Target, "CanOpen" => CanInspectGraph, _ => null },
            GraphLinkView link when _graphLinks.Any(current => ReferenceEquals(current, link)) => path switch
            { "Key" => link.Key, "Title" => link.Title, "Detail" => link.Detail, "Target" => link.Target, "CanOpen" => CanInspectGraph, _ => null },
            GraphLine line when _graphDiagnostics.Concat(_graphPorts).Concat(_graphParameters).Any(current => ReferenceEquals(current, line)) => path switch
            { "Key" => line.Key, "Title" => line.Title, "Detail" => line.Detail, _ => null }, _ => null
        };
        return value is not null;
    }
    private static bool TryRetainStoredConnectionIds(string json, AutomationGraphDefinition decoded, out AutomationGraphDefinition inspected)
    {
        // The maintained codec gives older missing edge IDs a new fallback UUID.
        // A read-only inspector must expose missing identity, never present that
        // generated value as an ID stored by the owner. No runtime graph is changed.
        inspected = decoded;
        using var document = JsonDocument.Parse(json);
        var collections = document.RootElement.EnumerateObject()
            .Where(property => property.Name.Equals("edges", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (collections.Length != 1 || collections[0].Value.ValueKind != JsonValueKind.Array) return false;
        var raw = collections[0].Value.EnumerateArray().ToArray();
        if (raw.Length != decoded.Edges.Count) return false;
        var edges = new AutomationGraphEdgeDefinition[raw.Length];
        for (var index = 0; index < raw.Length; index++)
        {
            if (raw[index].ValueKind != JsonValueKind.Object) return false;
            var ids = raw[index].EnumerateObject().Where(property => property.Name.Equals("id", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (ids.Length > 1) return false;
            var hasStoredId = ids.Length == 1 && ids[0].Value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(ids[0].Value.GetString(), out var stored) && stored != Guid.Empty;
            edges[index] = hasStoredId ? decoded.Edges[index] : decoded.Edges[index] with { Id = Guid.Empty };
        }
        inspected = decoded with { Edges = Array.AsReadOnly(edges) }; return true;
    }
    private static bool IsSensitiveKey(string key) => new[] { "password", "secret", "token", "authorization", "apikey", "api_key", "credential" }
        .Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase));
    private static string Display(string? value, string? fallback = null)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback ?? "(empty)" : value!;
        return text.Length <= 1024 ? text : text[..1024] + "…";
    }
}
#endif
