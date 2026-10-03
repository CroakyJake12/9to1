using System.Text;
using Haven.Core.Mathematics;
using Xunit;

namespace Haven.Core.Tests;

public sealed class SharedMathObjectTests
{
    private static MathExpression Expression(string text = "x") => new(Guid.NewGuid(), 1, text);

    [Fact]
    public void All_eleven_typed_answers_round_trip_stable_identity_revision_and_representation()
    {
        var first = Expression(); var second = Expression("1");
        MathAnswerValue[] values = [new NumericMathAnswer("1.20", "m"), new ExpressionMathAnswer(first),
            new EquationMathAnswer(first, second), new InequalityMathAnswer(first, MathRelation.LessThan, second),
            new IntervalMathAnswer(null, false, 1, true), new CoordinateMathAnswer([1, 2]),
            new VectorMathAnswer([first, second]), new MatrixMathAnswer([[first, second], [second, first]]),
            new SetMathAnswer([first, second]), new MultipleExpressionsMathAnswer([first, second]),
            new FreeWorkingMathAnswer("Working remains attached to this answer", [first])];
        foreach (var value in values)
        {
            var answer = new MathAnswer(Guid.NewGuid(), 7, value);
            var encoded = MathObjectCodec.Encode(answer); var reopened = MathObjectCodec.Decode<MathAnswer>(encoded);
            Assert.Equal(answer.AnswerID, reopened.AnswerID); Assert.Equal(7, reopened.Revision);
            Assert.Equal(value.GetType(), reopened.Value.GetType()); Assert.Equal(encoded, MathObjectCodec.Encode(reopened));
        }
        Assert.Equal("1.20", Assert.IsType<NumericMathAnswer>(MathObjectCodec.Capture(new MathAnswer(Guid.NewGuid(), 1, values[0])).Value).Literal);
    }

    [Fact]
    public void Strict_codec_rejects_future_schema_unknown_fields_duplicate_properties_and_unknown_union()
    {
        var value = Expression(); var bytes = MathObjectCodec.Encode(value); var json = Encoding.UTF8.GetString(bytes);
        Assert.Throws<InvalidDataException>(() => MathObjectCodec.Decode<MathExpression>(Encoding.UTF8.GetBytes(json.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2"))));
        Assert.Throws<System.Text.Json.JsonException>(() => MathObjectCodec.Decode<MathExpression>(Encoding.UTF8.GetBytes(json[..^1] + ",\"Future\":true}")));
        Assert.Throws<InvalidDataException>(() => MathObjectCodec.Decode<MathExpression>(Encoding.UTF8.GetBytes(json[..^1] + ",\"Revision\":7}")));
        var answer = Encoding.UTF8.GetString(MathObjectCodec.Encode(new MathAnswer(Guid.NewGuid(), 1, new NumericMathAnswer("1"))));
        Assert.Throws<System.Text.Json.JsonException>(() => MathObjectCodec.Decode<MathAnswer>(Encoding.UTF8.GetBytes(answer.Replace("\"numeric\"", "\"unknown\""))));
    }

    [Fact]
    public void Capture_detaches_mutable_payloads_and_bounds_full_use_of_configured_arrays()
    {
        var coordinates = new decimal[] { 1, 2 }; var original = new MathAnswer(Guid.NewGuid(), 1, new CoordinateMathAnswer(coordinates));
        var captured = MathObjectCodec.Capture(original); coordinates[0] = 999;
        Assert.Equal(1, Assert.IsType<CoordinateMathAnswer>(captured.Value).Coordinates[0]);
        var limits = new MathServiceLimits(MaxGraphPoints: 4096);
        var points = Enumerable.Range(0, limits.MaxGraphPoints).Select(i => new GraphCoordinate(i, i)).ToArray();
        var graph = new GraphDefinition(Guid.NewGuid(), 1, new(0, 4096, 0, 4096), [],
            [new GraphCurve(Guid.NewGuid(), points)], [GraphResponseTool.DrawCurve]);
        Assert.Equal(4096, Assert.IsType<GraphCurve>(Assert.Single(MathObjectCodec.Capture(graph, limits).Primitives)).Points.Length);
        Assert.Throws<InvalidDataException>(() => MathObjectCodec.Capture(graph with {
            Primitives = [graph.Primitives[0], new GraphPoint(Guid.NewGuid(), new(0, 0))] }, limits));
        Assert.Throws<InvalidDataException>(() => MathObjectCodec.Encode(Expression(new string('x', 8193))));
        Assert.Throws<InvalidDataException>(() => MathObjectCodec.Encode(new MathAnswer(Guid.NewGuid(), 1,
            new MatrixMathAnswer(Enumerable.Range(0, 33).Select(row => Enumerable.Range(0, 32).Select(column => Expression()).ToArray()).ToArray()))));
    }

    [Theory]
    [InlineData("1.20", "1.2", true)]
    [InlineData("1.20e2", "120", true)]
    [InlineData("-0.05", "-0.05", true)]
    [InlineData("1", "1.0000000000000000000000000001", false)]
    public void Exact_numeric_rules_use_actual_value_without_discarding_the_submitted_literal(string actual, string expected, bool correct)
    {
        var rule = new MathNumericRule(Guid.NewGuid(), MathNumericLiteral.Read(expected).Value, MathNumericComparison.Exact);
        var answer = new MathAnswer(Guid.NewGuid(), 1, new NumericMathAnswer(actual));
        var result = MathMarking.Evaluate(answer, rule);
        Assert.Equal(correct ? MathMarkingOutcome.Correct : MathMarkingOutcome.Incorrect, result.Outcome);
        Assert.Equal("Forms.NumberWithinTolerance", result.Provenance);
        Assert.Equal(actual, Assert.IsType<NumericMathAnswer>(MathObjectCodec.Capture(answer).Value).Literal);
    }

    [Fact]
    public void Precision_units_percentage_and_absolute_tolerance_have_explicit_boundaries()
    {
        var rule = new MathNumericRule(Guid.NewGuid(), 10, MathNumericComparison.PercentageTolerance,
            2, SignificantFigures: 3, DecimalPlaces: 1, Units: "m");
        MathMarkingResult Mark(string number, string? units = "m") => MathMarking.Evaluate(new(Guid.NewGuid(), 1, new NumericMathAnswer(number, units)), rule);
        Assert.Equal(MathMarkingOutcome.Correct, Mark("10.2").Outcome);
        Assert.Equal(MathMarkingOutcome.Incorrect, Mark("10.3").Outcome);
        Assert.Equal("DecimalPlacesMismatch", MathMarking.Evaluate(new(Guid.NewGuid(), 1, new NumericMathAnswer("10.00", "m")),
            rule with { SignificantFigures = null }).Diagnostic);
        Assert.Equal("UnitsMismatch", Mark("10.0", "cm").Diagnostic);
        var ambiguous = MathMarking.Evaluate(new(Guid.NewGuid(), 1, new NumericMathAnswer("1200")),
            new(Guid.NewGuid(), 1200, MathNumericComparison.Exact, SignificantFigures: 2));
        Assert.Equal(MathMarkingOutcome.NeedsReview, ambiguous.Outcome);
        Assert.Equal("AmbiguousSignificantFigures", ambiguous.Diagnostic);
        var tiny = MathMarking.Evaluate(new(Guid.NewGuid(), 1, new NumericMathAnswer("1e-28")),
            new(Guid.NewGuid(), 0.0000000000000000000000000001m, MathNumericComparison.PercentageTolerance, 1));
        Assert.Equal(MathMarkingOutcome.NeedsReview, tiny.Outcome); Assert.Equal("ToleranceRangeUnsupported", tiny.Diagnostic);
        var absolute = new MathNumericRule(Guid.NewGuid(), 10, MathNumericComparison.AbsoluteTolerance, .2m);
        Assert.Equal(MathMarkingOutcome.Correct, MathMarking.Evaluate(new(Guid.NewGuid(), 1, new NumericMathAnswer("10.2")), absolute).Outcome);
        Assert.Equal(MathMarkingOutcome.Incorrect, MathMarking.Evaluate(new(Guid.NewGuid(), 1, new NumericMathAnswer("10.2001")), absolute).Outcome);
    }

    [Theory]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("1e29")]
    [InlineData("01")]
    [InlineData("NaN")]
    [InlineData("1.")]
    public void Unsupported_numeric_precision_or_representation_is_refused_instead_of_rounded(string value) =>
        Assert.Throws<InvalidDataException>(() => MathNumericLiteral.Read(value));

    [Fact]
    public void Graph_marking_compares_typed_coordinates_and_refuses_unavailable_equivalence()
    {
        var point = new GraphPoint(Guid.NewGuid(), new(1, 2));
        var graph = new GraphDefinition(Guid.NewGuid(), 4, new(-10, 10, -10, 10), [], [point], [GraphResponseTool.PlacePoint]);
        GraphResponse Response(decimal x) => new(Guid.NewGuid(), 1, graph.GraphID, graph.Revision, [],
            [new(GraphResponseTool.PlacePoint, new GraphPoint(Guid.NewGuid(), new(x, 2)))]);
        var rule = new GraphCoordinateMarkingRule(Guid.NewGuid(), point.PrimitiveID, .1m);
        Assert.Equal(MathMarkingOutcome.Correct, GraphMarking.Evaluate(graph, Response(1.1m), rule).Outcome);
        Assert.Equal(MathMarkingOutcome.Incorrect, GraphMarking.Evaluate(graph, Response(1.1001m), rule).Outcome);
        Assert.Throws<InvalidOperationException>(() => GraphMarking.Evaluate(graph, Response(1) with { GraphRevision = 3 }, rule));
        var expression = Expression("x^2"); var function = new GraphFunction(Guid.NewGuid(), new(expression.ExpressionID, 1));
        graph = graph with { Expressions = [expression], Primitives = [function], ResponseTools = [GraphResponseTool.PlotFunction] };
        var response = new GraphResponse(Guid.NewGuid(), 1, graph.GraphID, graph.Revision, [expression], [new(GraphResponseTool.PlotFunction, function)]);
        Assert.Equal(MathMarkingOutcome.NeedsReview, GraphMarking.Evaluate(graph, response, rule with { ExpectedPrimitiveID = function.PrimitiveID }).Outcome);
    }

    [Fact]
    public void Graph_manipulation_marking_requires_actual_graded_target_identity_and_type()
    {
        var point = new GraphPoint(Guid.NewGuid(), new(1, 2));
        var other = new GraphPoint(Guid.NewGuid(), new(3, 4));
        var graph = new GraphDefinition(Guid.NewGuid(), 2, new(-10, 10, -10, 10), [],
            [point, other], [GraphResponseTool.ManipulatePrimitive]);
        var rule = new GraphCoordinateMarkingRule(Guid.NewGuid(), point.PrimitiveID);
        GraphResponse Response(GraphPrimitive primitive) => new(Guid.NewGuid(), 1, graph.GraphID, graph.Revision, [],
            [new(GraphResponseTool.ManipulatePrimitive, primitive, primitive.PrimitiveID)]);
        var missing = Response(point with { PrimitiveID = Guid.NewGuid() });
        MathObjectCodec.Validate(missing); // structurally valid, but no canonical target exists
        Assert.Equal(MathMarkingOutcome.Incorrect, GraphMarking.Evaluate(graph, missing, rule).Outcome);
        Assert.Equal("GraphManipulationTargetMismatch", GraphMarking.Evaluate(graph, missing, rule).Diagnostic);
        Assert.Equal(MathMarkingOutcome.Incorrect, GraphMarking.Evaluate(graph,
            Response(other with { Position = point.Position }), rule).Outcome);
        Assert.Equal(MathMarkingOutcome.Incorrect, GraphMarking.Evaluate(graph,
            Response(new GraphLine(point.PrimitiveID, point.Position, new(3, 4))), rule).Outcome);
        Assert.Equal(MathMarkingOutcome.Correct, GraphMarking.Evaluate(graph, Response(point), rule).Outcome);
    }
}
