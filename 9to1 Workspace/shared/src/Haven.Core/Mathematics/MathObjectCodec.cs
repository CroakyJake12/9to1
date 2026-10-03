using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haven.Core.Mathematics;

/// <summary>Strict structural persistence for shared math objects. Syntax/equivalence are
/// adapter capabilities, not inferred from a successful JSON round trip.</summary>
public static class MathObjectCodec
{
    public static byte[] Encode<T>(T value, MathServiceLimits? limits = null) where T : class
    {
        limits ??= new(); limits.Validate(); Validate(value, limits);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options(limits));
        if (bytes.Length > limits.MaxSerializedBytes) throw new InvalidDataException("MathObjectTooLarge");
        return bytes;
    }

    public static T Decode<T>(ReadOnlySpan<byte> bytes, MathServiceLimits? limits = null) where T : class
    {
        limits ??= new(); limits.Validate();
        if (bytes.Length == 0 || bytes.Length > limits.MaxSerializedBytes) throw new InvalidDataException("MathObjectTooLarge");
        using (var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = limits.MaxObjectDepth }))
            RejectDuplicateProperties(document.RootElement);
        var result = JsonSerializer.Deserialize<T>(bytes, Options(limits)) ?? throw new InvalidDataException("MissingMathObject");
        Validate(result, limits);
        return result;
    }

    public static T Capture<T>(T value, MathServiceLimits? limits = null) where T : class => Decode<T>(Encode(value, limits), limits);

    public static void Validate(object? value, MathServiceLimits? limits = null)
    {
        limits ??= new(); limits.Validate();
        switch (value)
        {
            case MathExpression expression: ValidateExpression(expression, limits); break;
            case MathAnswer answer:
                Identity(answer.AnswerID, answer.Revision, answer.SchemaVersion);
                ValidateAnswer(answer.Value, limits);
                var contained = AnswerExpressions(answer.Value).GroupBy(x => x.ExpressionID);
                if (contained.Any(group => group.Distinct().Skip(1).Any()))
                    throw new InvalidDataException("ConflictingMathExpressionIdentity");
                break;
            case GraphDefinition graph: ValidateGraph(graph, limits); break;
            case GraphResponse response: ValidateResponse(response, limits); break;
            default: throw new InvalidDataException("UnsupportedMathObject");
        }
    }

    private static JsonSerializerOptions Options(MathServiceLimits limits) => new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = limits.MaxObjectDepth,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("DuplicateMathProperty");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }

    private static void Identity(Guid id, long revision, int schema)
    {
        if (schema != 1) throw new InvalidDataException("UnsupportedMathSchema");
        if (id == Guid.Empty || revision < 1) throw new InvalidDataException("InvalidMathIdentity");
    }
    private static void Text(string? text, MathServiceLimits limits, bool required = false)
    {
        if (text is null || text.Length > limits.MaxSourceCharacters || required && string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("InvalidMathText");
    }
    private static void ValidateExpression(MathExpression? expression, MathServiceLimits limits)
    {
        if (expression is null) throw new InvalidDataException("MissingMathExpression");
        Identity(expression.ExpressionID, expression.Revision, expression.SchemaVersion);
        Text(expression.LaTeX, limits, true); Text(expression.AccessibleDescription, limits);
    }
    private static void Expressions(MathExpression[]? expressions, MathServiceLimits limits, bool requireUnique)
    {
        if (expressions is null || expressions.Length > limits.MaxAnswerItems || expressions.Any(x => x is null))
            throw new InvalidDataException("InvalidMathExpressions");
        foreach (var expression in expressions) ValidateExpression(expression, limits);
        if (requireUnique && expressions.Select(x => x.ExpressionID).Distinct().Count() != expressions.Length)
            throw new InvalidDataException("DuplicateMathExpressionIdentity");
    }
    private static void ValidateAnswer(MathAnswerValue? answer, MathServiceLimits limits)
    {
        switch (answer)
        {
            case NumericMathAnswer number:
                _ = MathNumericLiteral.Read(number.Literal);
                if (number.Units is { } units) Text(units, limits, true);
                break;
            case ExpressionMathAnswer expression: ValidateExpression(expression.Expression, limits); break;
            case EquationMathAnswer equation:
                ValidateExpression(equation.Left, limits); ValidateExpression(equation.Right, limits); break;
            case InequalityMathAnswer inequality:
                if (!Enum.IsDefined(inequality.Relation)) throw new InvalidDataException("InvalidMathRelation");
                ValidateExpression(inequality.Left, limits); ValidateExpression(inequality.Right, limits); break;
            case IntervalMathAnswer interval:
                if (interval.Lower is null && interval.IncludeLower || interval.Upper is null && interval.IncludeUpper ||
                    interval.Lower > interval.Upper || interval.Lower == interval.Upper &&
                    interval.Lower is not null && (!interval.IncludeLower || !interval.IncludeUpper))
                    throw new InvalidDataException("InvalidMathInterval");
                break;
            case CoordinateMathAnswer coordinate:
                if (coordinate.Coordinates is null || coordinate.Coordinates.Length is not (2 or 3))
                    throw new InvalidDataException("InvalidMathCoordinate");
                break;
            case VectorMathAnswer vector: Expressions(vector.Components, limits, false); RequireNonempty(vector.Components); break;
            case MatrixMathAnswer matrix:
                if (matrix.Rows is null || matrix.Rows.Length == 0 || matrix.Rows.Length > limits.MaxAnswerItems ||
                    matrix.Rows.Any(x => x is null || x.Length == 0) ||
                    matrix.Rows.Any(x => x.Length != matrix.Rows[0].Length) ||
                    matrix.Rows.Sum(x => (long)x.Length) > limits.MaxMatrixCells)
                    throw new InvalidDataException("InvalidMathMatrix");
                foreach (var row in matrix.Rows) Expressions(row, limits, false);
                break;
            case SetMathAnswer set: Expressions(set.Elements, limits, false); break;
            case MultipleExpressionsMathAnswer multiple: Expressions(multiple.Expressions, limits, false); RequireNonempty(multiple.Expressions); break;
            case FreeWorkingMathAnswer working: Text(working.Text, limits); Expressions(working.Expressions, limits, false); break;
            default: throw new InvalidDataException("UnsupportedMathAnswer");
        }
    }
    private static void RequireNonempty<T>(T[] values)
    {
        if (values.Length == 0) throw new InvalidDataException("EmptyMathAnswer");
    }
    private static IEnumerable<MathExpression> AnswerExpressions(MathAnswerValue value) => value switch
    {
        ExpressionMathAnswer expression => [expression.Expression],
        EquationMathAnswer equation => [equation.Left, equation.Right],
        InequalityMathAnswer inequality => [inequality.Left, inequality.Right],
        VectorMathAnswer vector => vector.Components,
        MatrixMathAnswer matrix => matrix.Rows.SelectMany(x => x),
        SetMathAnswer set => set.Elements,
        MultipleExpressionsMathAnswer multiple => multiple.Expressions,
        FreeWorkingMathAnswer working => working.Expressions,
        _ => []
    };
    private static void ValidateGraph(GraphDefinition graph, MathServiceLimits limits)
    {
        Identity(graph.GraphID, graph.Revision, graph.SchemaVersion);
        if (graph.Axes is null || graph.Axes.XMinimum >= graph.Axes.XMaximum ||
            graph.Axes.YMinimum >= graph.Axes.YMaximum || graph.Axes.XGridSpacing <= 0 || graph.Axes.YGridSpacing <= 0)
            throw new InvalidDataException("InvalidGraphAxes");
        Text(graph.Axes.XLabel, limits); Text(graph.Axes.YLabel, limits); Text(graph.AccessibleDescription, limits);
        Expressions(graph.Expressions, limits, true);
        if (graph.Primitives is null || graph.Primitives.Length > limits.MaxGraphPrimitives ||
            graph.Primitives.Any(x => x is null) || graph.ResponseTools is null ||
            graph.ResponseTools.Any(x => !Enum.IsDefined(x)) || graph.ResponseTools.Distinct().Count() != graph.ResponseTools.Length)
            throw new InvalidDataException("InvalidGraphObjects");
        var ids = new HashSet<Guid>(); var points = 0;
        foreach (var primitive in graph.Primitives)
        {
            if (!ids.Add(primitive.PrimitiveID)) throw new InvalidDataException("DuplicateGraphPrimitiveIdentity");
            ValidatePrimitive(primitive, limits, graph.Expressions, ref points);
        }
    }
    private static void ValidateResponse(GraphResponse response, MathServiceLimits limits)
    {
        Identity(response.ResponseID, response.Revision, response.SchemaVersion);
        if (response.GraphID == Guid.Empty || response.GraphRevision < 1) throw new InvalidDataException("InvalidGraphReference");
        Expressions(response.Expressions, limits, true);
        if (response.Actions is null || response.Actions.Length > limits.MaxGraphPrimitives || response.Actions.Any(x => x is null))
            throw new InvalidDataException("InvalidGraphResponseActions");
        var ids = new HashSet<Guid>(); var points = 0;
        foreach (var action in response.Actions)
        {
            if (action.Primitive is null || !Enum.IsDefined(action.Tool) || !ids.Add(action.Primitive.PrimitiveID))
                throw new InvalidDataException("InvalidGraphResponseAction");
            if (action.Tool == GraphResponseTool.ManipulatePrimitive)
            {
                if (action.TargetPrimitiveID is null || action.TargetPrimitiveID == Guid.Empty ||
                    action.TargetPrimitiveID != action.Primitive.PrimitiveID)
                    throw new InvalidDataException("InvalidGraphManipulationTarget");
            }
            else if (action.TargetPrimitiveID is not null || !ToolMatches(action.Tool, action.Primitive))
                throw new InvalidDataException("GraphToolPayloadMismatch");
            ValidatePrimitive(action.Primitive, limits, null, ref points);
        }
    }
    private static bool ToolMatches(GraphResponseTool tool, GraphPrimitive primitive) => (tool, primitive) switch
    {
        (GraphResponseTool.PlacePoint, GraphPoint) or (GraphResponseTool.DrawLine, GraphLine) or
        (GraphResponseTool.DrawCurve, GraphCurve) or (GraphResponseTool.SubmitCoordinates, GraphCoordinateTable) or
        (GraphResponseTool.PlotFunction, GraphFunction) or (GraphResponseTool.PlotEquation, GraphEquation) or
        (GraphResponseTool.PlotInequality, GraphInequality) or (GraphResponseTool.IdentifyRegion, GraphRegion) => true,
        _ => false
    };
    private static void ValidatePrimitive(GraphPrimitive? primitive, MathServiceLimits limits,
        MathExpression[]? expressions, ref int pointCount)
    {
        if (primitive is null || primitive.PrimitiveID == Guid.Empty) throw new InvalidDataException("InvalidGraphPrimitive");
        MathExpressionReference? reference = null;
        GraphCoordinate[]? points = null; var minimum = 0;
        switch (primitive)
        {
            case GraphFunction function:
                reference = function.Expression; Text(function.Variable, limits, true);
                if (function.Domain is { } domain && domain.Minimum >= domain.Maximum) throw new InvalidDataException("InvalidGraphDomain");
                break;
            case GraphEquation equation: reference = equation.Expression; break;
            case GraphInequality inequality: reference = inequality.Expression; break;
            case GraphPoint: pointCount = checked(pointCount + 1); break;
            case GraphLine line:
                if (line.Start == line.End) throw new InvalidDataException("DegenerateGraphLine");
                pointCount = checked(pointCount + 2); break;
            case GraphCoordinateTable table: points = table.Points; minimum = 1; break;
            case GraphCurve curve: points = curve.Points; minimum = 2; break;
            case GraphRegion region: points = region.Boundary; minimum = 3; break;
            default: throw new InvalidDataException("UnsupportedGraphPrimitive");
        }
        if (primitive is GraphFunction or GraphEquation or GraphInequality)
        {
            if (reference is null || reference.ExpressionID == Guid.Empty || reference.Revision < 1)
                throw new InvalidDataException("InvalidGraphExpressionReference");
            if (expressions is not null && !expressions.Any(x => x.ExpressionID == reference.ExpressionID && x.Revision == reference.Revision))
                throw new InvalidDataException("MissingGraphExpressionRevision");
        }
        if (primitive is GraphCoordinateTable or GraphCurve or GraphRegion)
        {
            if (points is null || points.Length < minimum || points.Length > limits.MaxGraphPoints)
                throw new InvalidDataException("InvalidGraphPoints");
            pointCount = checked(pointCount + points.Length);
        }
        if (pointCount > limits.MaxGraphPoints) throw new InvalidDataException("GraphPointBudgetExceeded");
    }
}
