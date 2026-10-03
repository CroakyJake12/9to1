using Haven.Application.Mathematics;
using Haven.Core.Mathematics;
using Xunit;

namespace Haven.Core.Tests;

public sealed class SharedMathEditorSessionTests
{
    private static GraphDefinition Graph() => new(Guid.NewGuid(), 1, new(-10, 10, -10, 10), [], [],
        [GraphResponseTool.PlacePoint, GraphResponseTool.ManipulatePrimitive, GraphResponseTool.PlotFunction]);
    private static GraphResponse Response(GraphDefinition graph, params GraphResponseAction[] actions) =>
        new(Guid.NewGuid(), 1, graph.GraphID, graph.Revision, [], actions);

    [Fact]
    public void Point_then_manipulation_preserves_canonical_ID_and_captured_arrays_without_aliasing()
    {
        var initial = Graph(); var session = new GraphEditorSession(initial);
        var id = Guid.NewGuid(); var point = new GraphPoint(id, new(1, 2));
        var response = Response(initial, new(GraphResponseTool.PlacePoint, point));
        var added = session.Apply(response);
        Assert.Equal(initial.GraphID, added.GraphID); Assert.Equal(2, added.Revision);
        Assert.Equal(id, Assert.Single(added.Primitives).PrimitiveID);
        response.Actions[0] = new(GraphResponseTool.PlacePoint, new GraphPoint(Guid.NewGuid(), new(999, 999)));
        added.Primitives[0] = new GraphPoint(Guid.NewGuid(), new(999, 999));
        var original = Assert.IsType<GraphPoint>(Assert.Single(session.Snapshot().Primitives));
        Assert.Equal(point, original);
        var moved = session.Apply(Response(session.Snapshot(), new(GraphResponseTool.ManipulatePrimitive,
            point with { Position = new(3, 4) }, id)));
        Assert.Equal(id, Assert.Single(moved.Primitives).PrimitiveID); Assert.Equal(3, moved.Revision);
        Assert.Equal(new GraphCoordinate(3, 4), Assert.IsType<GraphPoint>(moved.Primitives[0]).Position);
    }

    [Fact]
    public void Stale_foreign_undeclared_and_partially_invalid_operations_leave_entire_graph_unchanged()
    {
        var initial = Graph(); var session = new GraphEditorSession(initial); var before = MathObjectCodec.Encode(session.Snapshot());
        var point = new GraphPoint(Guid.NewGuid(), new(1, 2));
        var stale = Response(initial, new(GraphResponseTool.PlacePoint, point)) with { GraphRevision = 7 };
        Assert.Throws<InvalidOperationException>(() => session.Apply(stale));
        Assert.Throws<InvalidOperationException>(() => session.Apply(stale with { GraphID = Guid.NewGuid(), GraphRevision = 1 }));
        Assert.Throws<InvalidOperationException>(() => session.Apply(Response(initial,
            new(GraphResponseTool.DrawLine, new GraphLine(Guid.NewGuid(), new(0, 0), new(1, 1))))));
        var missing = new GraphPoint(Guid.NewGuid(), new(0, 0));
        Assert.Throws<KeyNotFoundException>(() => session.Apply(Response(initial,
            new(GraphResponseTool.PlacePoint, point),
            new(GraphResponseTool.ManipulatePrimitive, missing, missing.PrimitiveID))));
        Assert.Equal(before, MathObjectCodec.Encode(session.Snapshot()));
    }

    [Fact]
    public void Graph_expression_references_require_exact_revisions_and_conflicts_are_atomic()
    {
        var initial = Graph(); var session = new GraphEditorSession(initial);
        var expression = new MathExpression(Guid.NewGuid(), 1, "x^2");
        var function = new GraphFunction(Guid.NewGuid(), new(expression.ExpressionID, 2));
        var before = MathObjectCodec.Encode(initial);
        var response = Response(initial, new(GraphResponseTool.PlotFunction, function)) with { Expressions = [expression] };
        Assert.Throws<InvalidDataException>(() => session.Apply(response)); Assert.Equal(before, MathObjectCodec.Encode(session.Snapshot()));
        response = response with { Actions = [new(GraphResponseTool.PlotFunction, function with { Expression = new(expression.ExpressionID, 1) })] };
        var valid = session.Apply(response); Assert.Equal(expression, Assert.Single(valid.Expressions));
        var conflicting = Response(valid, new(GraphResponseTool.PlotFunction,
            new GraphFunction(Guid.NewGuid(), new(expression.ExpressionID, 1)))) with { Expressions = [expression with { LaTeX = "x^3" }] };
        var exact = MathObjectCodec.Encode(session.Snapshot());
        Assert.Throws<InvalidOperationException>(() => session.Apply(conflicting)); Assert.Equal(exact, MathObjectCodec.Encode(session.Snapshot()));
    }
}
