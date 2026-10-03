using Haven.Core.Mathematics;

namespace Haven.Application.Mathematics;

/// <summary>Atomic typed graph edits with exact graph/expression revisions and declared tools.
/// Host authorization and persistence remain with the owning product's shared services.</summary>
public sealed class GraphEditorSession
{
    private readonly object _gate = new();
    private readonly MathServiceLimits _limits;
    private GraphDefinition _graph;
    public GraphEditorSession(GraphDefinition initial, MathServiceLimits? limits = null)
    {
        _limits = limits ?? new(); _graph = MathObjectCodec.Capture(initial, _limits);
    }
    public GraphDefinition Snapshot()
    {
        lock (_gate) return MathObjectCodec.Capture(_graph, _limits);
    }
    public GraphDefinition Apply(GraphResponse response)
    {
        var captured = MathObjectCodec.Capture(response, _limits);
        lock (_gate)
        {
            var next = GraphResponseProjection.ApplyToQuestion(_graph, captured, _limits);
            _graph = next;
            return Snapshot();
        }
    }

    /// <summary>Replace an owned definition at its exact current revision. A changed body
    /// advances the same graph identity; an unchanged body does not invent a new revision.</summary>
    public GraphDefinition ReplaceDefinition(long expectedRevision, GraphDefinition replacement)
    {
        var captured = MathObjectCodec.Capture(replacement, _limits);
        lock (_gate)
        {
            if (_graph.Revision != expectedRevision || captured.Revision != expectedRevision)
                throw new InvalidOperationException("RevisionConflict");
            if (captured.GraphID != _graph.GraphID)
                throw new InvalidOperationException("GraphIdentityConflict");
            if (MathObjectCodec.Encode(_graph, _limits).SequenceEqual(MathObjectCodec.Encode(captured, _limits)))
                return Snapshot();
            var next = MathObjectCodec.Capture(captured with { Revision = checked(expectedRevision + 1) }, _limits);
            _graph = next;
            return Snapshot();
        }
    }
}
