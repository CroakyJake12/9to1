using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace Haven.Core.Mathematics;

/// <summary>Validate a complete typed response against its original canonical question.
/// A display projection has a separate stable identity and the held response revision;
/// it never impersonates a question version or supplies a permission grant.</summary>
public static class GraphResponseProjection
{
    public static GraphDefinition Apply(GraphDefinition question, GraphResponse response,
        MathServiceLimits? limits = null)
    {
        var captured = MathObjectCodec.Capture(response, limits);
        var candidate = ApplyPayload(question, captured, limits);
        // Fixed UTF-8 derivation domain: question ID, exact question revision, response ID.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"9to1.graph-response-projection.v1:{captured.GraphID:D}:{captured.GraphRevision:D}:{captured.ResponseID:D}")));
        var projectionID = new Guid(hash.AsSpan(0, 16));
        if (projectionID == Guid.Empty || projectionID == candidate.GraphID)
            throw new InvalidDataException("GraphProjectionIdentityConflict");
        return MathObjectCodec.Capture(candidate with { GraphID = projectionID, Revision = captured.Revision }, limits);
    }

    /// <summary>An owning question editor can commit this validated candidate at its exact
    /// question revision. Returning a candidate does not grant authorization or persist it.</summary>
    public static GraphDefinition ApplyToQuestion(GraphDefinition question, GraphResponse response,
        MathServiceLimits? limits = null)
    {
        var candidate = ApplyPayload(question, response, limits);
        return MathObjectCodec.Capture(candidate with { Revision = checked(candidate.Revision + 1) }, limits);
    }

    private static GraphDefinition ApplyPayload(GraphDefinition question, GraphResponse response,
        MathServiceLimits? limits)
    {
        var graph = MathObjectCodec.Capture(question, limits);
        var captured = MathObjectCodec.Capture(response, limits);
        if (captured.GraphID != graph.GraphID || captured.GraphRevision != graph.Revision)
            throw new InvalidOperationException("RevisionConflict");
        if (captured.Actions.Length == 0) throw new InvalidDataException("EmptyGraphResponse");
        if (captured.Actions.Any(x => !graph.ResponseTools.Contains(x.Tool)))
            throw new InvalidOperationException("GraphToolNotDeclared");
        var expressions = graph.Expressions.ToDictionary(x => x.ExpressionID);
        foreach (var expression in captured.Expressions)
        {
            if (expressions.TryGetValue(expression.ExpressionID, out var original) && original != expression)
                throw new InvalidOperationException("MathExpressionRevisionConflict");
            expressions[expression.ExpressionID] = expression;
        }
        var primitives = graph.Primitives.ToList();
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
        return MathObjectCodec.Capture(graph with {
            Expressions = expressions.Values.ToArray(), Primitives = primitives.ToArray() }, limits);
    }
}
