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
            if (captured.GraphID != _graph.GraphID || captured.GraphRevision != _graph.Revision)
                throw new InvalidOperationException("RevisionConflict");
            if (captured.Actions.Length == 0) throw new InvalidDataException("EmptyGraphResponse");
            if (captured.Actions.Any(x => !_graph.ResponseTools.Contains(x.Tool)))
                throw new InvalidOperationException("GraphToolNotDeclared");
            var expressions = _graph.Expressions.ToDictionary(x => x.ExpressionID);
            foreach (var expression in captured.Expressions)
            {
                if (expressions.TryGetValue(expression.ExpressionID, out var original) && original != expression)
                    throw new InvalidOperationException("MathExpressionRevisionConflict");
                expressions[expression.ExpressionID] = expression;
            }
            var primitives = _graph.Primitives.ToList();
            foreach (var action in captured.Actions)
            {
                var index = primitives.FindIndex(x => x.PrimitiveID == action.Primitive.PrimitiveID);
                if (action.Tool == GraphResponseTool.ManipulatePrimitive)
                {
                    if (index < 0) throw new KeyNotFoundException("GraphPrimitiveNotFound");
                    if (primitives[index].GetType() != action.Primitive.GetType())
                        throw new InvalidDataException("GraphManipulationTypeChanged");
                    primitives[index] = action.Primitive;
                }
                else
                {
                    if (index >= 0) throw new InvalidOperationException("GraphPrimitiveIdentityConflict");
                    primitives.Add(action.Primitive);
                }
            }
            var next = MathObjectCodec.Capture(_graph with { Revision = checked(_graph.Revision + 1),
                Expressions = expressions.Values.ToArray(), Primitives = primitives.ToArray() }, _limits);
            _graph = next;
            return Snapshot();
        }
    }
}
