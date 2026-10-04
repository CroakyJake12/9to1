using System.Text.Json;

namespace Haven.Application.NodeGraph;

public sealed record GraphEditResult(bool Succeeded, string Code, GraphDocument Document, IReadOnlyList<GraphDiagnostic> Diagnostics);

/// <summary>
/// Typed retained draft editing and history; no execution and no resource-authority grant.
/// The owning Home API must authorise the canonical GraphID and expected revision before each operation.
/// </summary>
public sealed class NodeGraphDraftEditor
{
    private readonly NodeGraphSchemaRegistry _registry;
    private readonly Stack<GraphDocument> _undo = new();
    private readonly Stack<GraphDocument> _redo = new();
    private GraphDocument _current;
    public NodeGraphDraftEditor(GraphDocument document, NodeGraphSchemaRegistry registry)
    { _registry = registry; _current = Copy(document); }
    public GraphDocument Current => Copy(_current);

    public GraphEditResult AddNode(long expectedRevision, GraphNode node) => Edit(expectedRevision,
        document => document with { Nodes = document.Nodes.Append(CopyNode(node)).ToArray() });
    public GraphEditResult RemoveNode(long expectedRevision, Guid nodeId)
    {
        if (!_current.Nodes.Any(node => node.NodeId == nodeId)) return Result(false, expectedRevision == _current.Revision ? "graph.node.not-found" : "graph.revision.conflict", []);
        return Edit(expectedRevision, document =>
    {
        var removed = document.Nodes.SingleOrDefault(node => node.NodeId == nodeId);
        if (removed is null) throw new InvalidOperationException("The requested node is not present in this draft.");
        var ports = removed.Ports.Select(port => port.PortId).ToHashSet();
        return document with { Nodes = document.Nodes.Where(node => node.NodeId != nodeId).ToArray(),
            Connections = document.Connections.Where(connection => !ports.Contains(connection.InputPortId) && !ports.Contains(connection.OutputPortId)).ToArray() };
    });
    }
    public GraphEditResult SetConfiguration(long expectedRevision, Guid nodeId, JsonElement configuration)
    {
        if (!_current.Nodes.Any(node => node.NodeId == nodeId)) return Result(false, expectedRevision == _current.Revision ? "graph.node.not-found" : "graph.revision.conflict", []);
        return Edit(expectedRevision, document => document with { Nodes = document.Nodes.Select(node => node.NodeId == nodeId ? node with { Configuration = configuration.Clone() } : node).ToArray() });
    }
    public GraphEditResult Connect(long expectedRevision, GraphConnection connection) => Edit(expectedRevision,
        document => document with { Connections = document.Connections.Append(connection).ToArray() });
    public GraphEditResult Disconnect(long expectedRevision, Guid connectionId)
    {
        if (!_current.Connections.Any(connection => connection.ConnectionId == connectionId))
            return Result(false, expectedRevision == _current.Revision ? "graph.connection.not-found" : "graph.revision.conflict", []);
        return Edit(expectedRevision, document => document with
        { Connections = document.Connections.Where(connection => connection.ConnectionId != connectionId).ToArray() });
    }
    public GraphEditResult Undo(long expectedRevision) => Restore(expectedRevision, _undo, _redo);
    public GraphEditResult Redo(long expectedRevision) => Restore(expectedRevision, _redo, _undo);

    private GraphEditResult Edit(long expectedRevision, Func<GraphDocument, GraphDocument> change)
    {
        if (expectedRevision != _current.Revision) return Result(false, "graph.revision.conflict", []);
        if (_current.State != GraphRevisionState.Draft) return Result(false, "graph.draft.required", []);
        if (_current.Revision == long.MaxValue) return Result(false, "graph.revision.exhausted", []);
        var candidate = Copy(change(Copy(_current))) with { Revision = _current.Revision + 1 };
        var errors = _registry.Validate(candidate);
        if (errors.Count != 0) return Result(false, "graph.validation.failed", errors);
        if (_undo.Count >= 256) return Result(false, "graph.history.limit", []);
        _undo.Push(_current); _redo.Clear(); _current = candidate;
        return Result(true, "graph.draft.changed", []);
    }
    private GraphEditResult Restore(long expectedRevision, Stack<GraphDocument> from, Stack<GraphDocument> to)
    {
        if (expectedRevision != _current.Revision) return Result(false, "graph.revision.conflict", []);
        if (_current.State != GraphRevisionState.Draft) return Result(false, "graph.draft.required", []);
        if (from.Count == 0) return Result(false, "graph.history.empty", []);
        if (_current.Revision == long.MaxValue) return Result(false, "graph.revision.exhausted", []);
        var candidate = Copy(from.Peek()) with { Revision = _current.Revision + 1 };
        var errors = _registry.Validate(candidate);
        if (errors.Count != 0) return Result(false, "graph.history.migration-required", errors);
        from.Pop(); to.Push(_current); _current = candidate;
        return Result(true, "graph.draft.changed", []);
    }
    private GraphEditResult Result(bool succeeded, string code, IReadOnlyList<GraphDiagnostic> errors) => new(succeeded, code, Current, errors.ToArray());
    private static GraphNode CopyNode(GraphNode node) => node with { Configuration = node.Configuration.Clone(), Ports = node.Ports.ToArray() };
    private static GraphDocument Copy(GraphDocument graph) => graph with { Nodes = graph.Nodes.Select(CopyNode).ToArray(), Connections = graph.Connections.ToArray() };
}
