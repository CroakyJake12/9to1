using System.Text.Json;
using Haven.Core.Forms;

namespace Haven.Core.Mathematics;

public sealed record GraphCoordinateMarkingRule(Guid RuleID, Guid ExpectedPrimitiveID,
    decimal CoordinateTolerance = 0, bool AllowReversedLine = true, Guid? QuestionPrimitiveID = null);

/// <summary>Explicit coordinate correspondence, not screenshots or guessed symbolic
/// equivalence. Unavailable function/region equivalence returns NeedsReview with provenance.</summary>
public static class GraphMarking
{
    public static MathMarkingResult Evaluate(GraphDefinition expected, GraphResponse response,
        GraphCoordinateMarkingRule rule, MathServiceLimits? limits = null)
    {
        MathObjectCodec.Validate(expected, limits); MathObjectCodec.Validate(response, limits);
        if (rule.RuleID == Guid.Empty || rule.ExpectedPrimitiveID == Guid.Empty || rule.CoordinateTolerance < 0)
            throw new ArgumentException("InvalidGraphMarkingRule", nameof(rule));
        if (expected.GraphID != response.GraphID || expected.Revision != response.GraphRevision)
            throw new InvalidOperationException("RevisionConflict");
        var target = expected.Primitives.SingleOrDefault(x => x.PrimitiveID == rule.ExpectedPrimitiveID)
            ?? throw new KeyNotFoundException("GraphPrimitiveNotFound");
        if (response.Actions.Length != 1)
            return new(MathMarkingOutcome.NeedsReview, rule.RuleID, "coordinate-correspondence", "SinglePrimitiveResponseRequired");
        if (!expected.ResponseTools.Contains(response.Actions[0].Tool))
            return new(MathMarkingOutcome.Incorrect, rule.RuleID, "declared-graph-tools", "GraphToolNotDeclared");
        if (response.Actions[0].Tool == GraphResponseTool.ManipulatePrimitive &&
            (response.Actions[0].TargetPrimitiveID != target.PrimitiveID ||
             response.Actions[0].Primitive.PrimitiveID != target.PrimitiveID ||
             response.Actions[0].Primitive.GetType() != target.GetType()))
            return new(MathMarkingOutcome.Incorrect, rule.RuleID, "canonical-graph-target", "GraphManipulationTargetMismatch");
        var actual = response.Actions[0].Primitive;
        bool? matches = (target, actual) switch
        {
            (GraphPoint left, GraphPoint right) => Same(left.Position, right.Position, rule),
            (GraphLine left, GraphLine right) => Same(left.Start, right.Start, rule) && Same(left.End, right.End, rule) ||
                rule.AllowReversedLine && Same(left.Start, right.End, rule) && Same(left.End, right.Start, rule),
            (GraphCoordinateTable left, GraphCoordinateTable right) => SameSequence(left.Points, right.Points, rule),
            (GraphCurve left, GraphCurve right) => SameSequence(left.Points, right.Points, rule),
            (GraphRegion, GraphRegion) or (GraphFunction, GraphFunction) or (GraphEquation, GraphEquation) or
                (GraphInequality, GraphInequality) => null,
            _ => false
        };
        return new(matches is null ? MathMarkingOutcome.NeedsReview : matches.Value ? MathMarkingOutcome.Correct : MathMarkingOutcome.Incorrect,
            rule.RuleID, "Forms.NumberWithinTolerance/coordinate-correspondence",
            matches is null ? "MathematicalGraphEquivalenceUnavailable" : null);
    }
    /// <summary>The private expected graph has its own identity. The response is admitted
    /// against the untouched owner-held question, then compared with private coordinates.</summary>
    public static MathMarkingResult Evaluate(GraphDefinition question, GraphDefinition expected,
        GraphResponse response, GraphCoordinateMarkingRule rule, MathServiceLimits? limits = null)
    {
        ValidatePolicy(question, expected, rule, limits);
        MathObjectCodec.Validate(response, limits);
        if (question.GraphID != response.GraphID || question.Revision != response.GraphRevision)
            throw new InvalidOperationException("RevisionConflict");
        if (response.Actions.Length != 1)
            return new(MathMarkingOutcome.NeedsReview, rule.RuleID, "coordinate-correspondence", "SinglePrimitiveResponseRequired");
        var action = response.Actions[0];
        if (!question.ResponseTools.Contains(action.Tool))
            return new(MathMarkingOutcome.Incorrect, rule.RuleID, "declared-graph-tools", "GraphToolNotDeclared");
        try { _ = GraphResponseProjection.Apply(question, response, limits); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { return new(MathMarkingOutcome.Incorrect, rule.RuleID, "canonical-question-response", error.Message); }
        var target = expected.Primitives.Single(x => x.PrimitiveID == rule.ExpectedPrimitiveID);
        if (action.Tool == GraphResponseTool.ManipulatePrimitive)
        {
            if (rule.QuestionPrimitiveID is not { } questionTarget || action.TargetPrimitiveID != questionTarget
                || action.Primitive.PrimitiveID != questionTarget || action.Primitive.GetType() != target.GetType())
                return new(MathMarkingOutcome.Incorrect, rule.RuleID, "canonical-graph-target", "GraphManipulationTargetMismatch");
        }
        else if (rule.QuestionPrimitiveID is not null)
            return new(MathMarkingOutcome.Incorrect, rule.RuleID, "canonical-graph-target", "GraphManipulationTargetNotApplicable");
        var actual = action.Primitive;
        bool? matches = (target, actual) switch
        {
            (GraphPoint left, GraphPoint right) => Same(left.Position, right.Position, rule),
            (GraphLine left, GraphLine right) => Same(left.Start, right.Start, rule) && Same(left.End, right.End, rule) ||
                rule.AllowReversedLine && Same(left.Start, right.End, rule) && Same(left.End, right.Start, rule),
            (GraphCoordinateTable left, GraphCoordinateTable right) => SameSequence(left.Points, right.Points, rule),
            (GraphCurve left, GraphCurve right) => SameSequence(left.Points, right.Points, rule),
            (GraphRegion, GraphRegion) or (GraphFunction, GraphFunction) or (GraphEquation, GraphEquation) or
                (GraphInequality, GraphInequality) => null,
            _ => false
        };
        return new(matches is null ? MathMarkingOutcome.NeedsReview : matches.Value ? MathMarkingOutcome.Correct : MathMarkingOutcome.Incorrect,
            rule.RuleID, "Forms.NumberWithinTolerance/coordinate-correspondence",
            matches is null ? "MathematicalGraphEquivalenceUnavailable" : null);
    }

    public static void ValidatePolicy(GraphDefinition question, GraphDefinition expected,
        GraphCoordinateMarkingRule rule, MathServiceLimits? limits = null)
    {
        MathObjectCodec.Validate(question, limits); MathObjectCodec.Validate(expected, limits);
        if (question.GraphID == expected.GraphID)
            throw new InvalidDataException("PrivateExpectedGraphRequiresDistinctIdentity");
        if (rule.RuleID == Guid.Empty || rule.ExpectedPrimitiveID == Guid.Empty || rule.CoordinateTolerance < 0
            || rule.QuestionPrimitiveID == Guid.Empty)
            throw new ArgumentException("InvalidGraphMarkingRule", nameof(rule));
        var target = expected.Primitives.SingleOrDefault(x => x.PrimitiveID == rule.ExpectedPrimitiveID)
            ?? throw new KeyNotFoundException("GraphPrimitiveNotFound");
        if (rule.QuestionPrimitiveID is { } questionTarget)
        {
            if (!question.ResponseTools.Contains(GraphResponseTool.ManipulatePrimitive))
                throw new InvalidDataException("GraphManipulationPolicyToolNotDeclared");
            var original = question.Primitives.SingleOrDefault(x => x.PrimitiveID == questionTarget)
                ?? throw new KeyNotFoundException("GraphQuestionPrimitiveNotFound");
            if (original.GetType() != target.GetType())
                throw new InvalidDataException("GraphExpectedPrimitiveTypeMismatch");
        }
        else if (!question.ResponseTools.Any(tool => MathObjectCodec.IsToolPayloadCompatible(tool, target)))
            throw new InvalidDataException("GraphExpectedPrimitiveToolMismatch");
    }

    private static bool SameSequence(GraphCoordinate[] left, GraphCoordinate[] right, GraphCoordinateMarkingRule rule) =>
        left.Length == right.Length && left.Zip(right).All(pair => Same(pair.First, pair.Second, rule));
    private static bool Same(GraphCoordinate left, GraphCoordinate right, GraphCoordinateMarkingRule rule) =>
        SameNumber(left.X, right.X, rule) && SameNumber(left.Y, right.Y, rule);
    private static bool SameNumber(decimal expected, decimal actual, GraphCoordinateMarkingRule rule) =>
        FormMarking.Evaluate(JsonSerializer.SerializeToElement(actual),
            [new(rule.RuleID, FormMarkingRuleKind.NumberWithinTolerance, 1, ExpectedNumber: expected,
                Tolerance: rule.CoordinateTolerance)], 1).Outcome == FormMarkingOutcome.Correct;
}
