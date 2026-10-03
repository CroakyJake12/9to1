using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace Haven.Core.Mathematics;

/// <summary>Bounds admitted display preparation, separate from mathematical
/// grading and the persisted graph. Cooperative checks are not a hard CPU,
/// allocation or wall-time boundary for maintained parser/library calls.</summary>
public sealed record GraphPlotPreparationPolicy(int SamplesPerInterval = 128,
    int MaxTotalCoordinates = 16_384, int MaxSegments = 256,
    int MaxOriginalExclusions = 64, int MaxOperations = 100_000,
    double MaxDisplayMagnitude = 1_000_000,
    MathRationalEvaluationLimits? ExpressionLimits = null)
{
    public MathRationalEvaluationLimits EffectiveExpressionLimits => ExpressionLimits ?? new();
    public void Validate()
    {
        EffectiveExpressionLimits.Validate();
        if (SamplesPerInterval is < 2 or > 4096 || MaxTotalCoordinates is < 2 or > 100_000 ||
            MaxSegments is < 1 or > 4096 || MaxOriginalExclusions is < 1 or > 256 ||
            MaxOperations is < 1 or > 10_000_000 || !double.IsFinite(MaxDisplayMagnitude) ||
            MaxDisplayMagnitude is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(GraphPlotPreparationPolicy));
    }
}

/// <summary>A rounded transient display coordinate. Never a canonical answer,
/// graph mutation, domain/equivalence proof or persisted graph primitive.</summary>
public readonly record struct GraphPlotDisplayPoint(double X, double Y);

/// <summary>One explicitly separated, finite display path. The constructor owns
/// its copy; native renderers must copy again if a maintained API retains arrays.</summary>
public sealed class GraphPlotDisplaySegment
{
    public Guid PrimitiveID { get; }
    public MathExpressionReference Expression { get; }
    public bool IncludeBoundary { get; }
    public IReadOnlyList<GraphPlotDisplayPoint> Points { get; }
    public GraphPlotDisplaySegment(Guid primitiveID, MathExpressionReference expression,
        IEnumerable<GraphPlotDisplayPoint> points, bool includeBoundary = true)
    {
        var owned = points.ToArray();
        if (primitiveID == Guid.Empty || expression.ExpressionID == Guid.Empty || expression.Revision < 1 ||
            owned.Length < 2 || owned.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException("InvalidGraphPlotDisplaySegment");
        PrimitiveID = primitiveID; Expression = expression;
        IncludeBoundary = includeBoundary;
        Points = new ReadOnlyCollection<GraphPlotDisplayPoint>(owned);
    }
}

/// <summary>A sampled display fill, bounded to the viewport and separated at
/// every classified original hole. It is not an exact region answer or a proof
/// that a point satisfies the inequality. Boundary semantics remain explicit.</summary>
public sealed class GraphPlotDisplayRegion
{
    public Guid PrimitiveID { get; }
    public MathExpressionReference Expression { get; }
    public bool IncludeBoundary { get; }
    public IReadOnlyList<GraphPlotDisplayPoint> Boundary { get; }
    public GraphPlotDisplayRegion(Guid primitiveID, MathExpressionReference expression,
        bool includeBoundary, IEnumerable<GraphPlotDisplayPoint> boundary)
    {
        var owned = boundary.ToArray();
        if (primitiveID == Guid.Empty || expression.ExpressionID == Guid.Empty || expression.Revision < 1 ||
            owned.Length < 4 || owned.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException("InvalidGraphPlotDisplayRegion");
        PrimitiveID = primitiveID; Expression = expression; IncludeBoundary = includeBoundary;
        Boundary = new ReadOnlyCollection<GraphPlotDisplayPoint>(owned);
    }
}

/// <summary>Exact original rational exclusion, preserved before any display
/// conversion. ExactAbscissa is an observational numerator/denominator wire
/// value, not an independently persisted mathematical expression.</summary>
public sealed record GraphPlotOriginalExclusion(Guid PrimitiveID,
    MathExpressionReference Expression, string ExactAbscissa, double DisplayAbscissa);
public sealed record GraphPlotPreparationRefusal(Guid PrimitiveID, string Diagnostic);

/// <summary>Observational output bound to the entire canonical graph body and
/// exact source expression versions. It does not impersonate a GraphDefinition
/// version or change its ID/revision. A different body under the same ID/revision
/// fails publication through the native consumer's complete digest check.</summary>
public sealed class PreparedGraphPlot
{
    private readonly GraphDefinition _snapshot;
    public Guid GraphID => _snapshot.GraphID;
    public long GraphRevision => _snapshot.Revision;
    public string GraphBodyDigest { get; }
    public string Implementation { get; }
    public GraphPlotPreparationPolicy Policy { get; }
    public IReadOnlyList<GraphPlotDisplaySegment> Segments { get; }
    public IReadOnlyList<GraphPlotDisplayRegion> ShadedRegions { get; }
    public IReadOnlyList<GraphPlotOriginalExclusion> OriginalExclusions { get; }
    public IReadOnlyList<GraphPlotPreparationRefusal> Refusals { get; }
    public GraphDefinition CaptureSource() => MathObjectCodec.Capture(_snapshot);
    public PreparedGraphPlot(GraphDefinition snapshot, GraphPlotPreparationPolicy policy,
        IEnumerable<GraphPlotDisplaySegment> segments,
        IEnumerable<GraphPlotOriginalExclusion> exclusions,
        IEnumerable<GraphPlotPreparationRefusal> refusals, string implementation,
        IEnumerable<GraphPlotDisplayRegion>? shadedRegions = null)
    {
        policy.Validate(); _snapshot = MathObjectCodec.Capture(snapshot); Policy = policy;
        GraphBodyDigest = Digest(_snapshot); Implementation = implementation;
        var ownedSegments = segments.ToArray(); var ownedExclusions = exclusions.ToArray();
        var ownedRefusals = refusals.ToArray();
        var ownedRegions = shadedRegions?.ToArray() ?? [];
        if (ownedSegments.Length + ownedRegions.Length > policy.MaxSegments ||
            ownedSegments.Sum(segment => segment.Points.Count) + ownedRegions.Sum(region => region.Boundary.Count) > policy.MaxTotalCoordinates ||
            ownedExclusions.Length > policy.MaxOriginalExclusions ||
            ownedSegments.Any(segment => segment.Points.Any(point => !WithinViewport(point))) ||
            ownedRegions.Any(region => region.Boundary.Any(point => !WithinViewport(point))))
            throw new InvalidDataException("GraphPlotPreparedBudgetExceeded");
        var sources = _snapshot.Primitives.ToDictionary(primitive => primitive.PrimitiveID);
        foreach (var segment in ownedSegments)
        {
            if (!sources.TryGetValue(segment.PrimitiveID, out var primitive) ||
                ExpressionOf(primitive) != segment.Expression ||
                segment.IncludeBoundary != (primitive is not GraphInequality inequality || inequality.IncludeBoundary))
                throw new InvalidDataException("GraphPlotPreparedSourceMismatch");
        }
        foreach (var region in ownedRegions)
        {
            if (!sources.TryGetValue(region.PrimitiveID, out var primitive) || primitive is not GraphInequality inequality ||
                inequality.Expression != region.Expression || inequality.IncludeBoundary != region.IncludeBoundary)
                throw new InvalidDataException("GraphPlotPreparedSourceMismatch");
        }
        if (ownedExclusions.Any(item => !double.IsFinite(item.DisplayAbscissa) ||
            !sources.TryGetValue(item.PrimitiveID, out var primitive) ||
            ExpressionOf(primitive) != item.Expression) ||
            ownedRefusals.Any(item => !sources.ContainsKey(item.PrimitiveID)))
            throw new InvalidDataException("GraphPlotPreparedSourceMismatch");
        Segments = new ReadOnlyCollection<GraphPlotDisplaySegment>(ownedSegments);
        ShadedRegions = new ReadOnlyCollection<GraphPlotDisplayRegion>(ownedRegions);
        OriginalExclusions = new ReadOnlyCollection<GraphPlotOriginalExclusion>(ownedExclusions);
        Refusals = new ReadOnlyCollection<GraphPlotPreparationRefusal>(ownedRefusals);
        bool WithinViewport(GraphPlotDisplayPoint point) =>
            Math.Abs(point.X) <= policy.MaxDisplayMagnitude && Math.Abs(point.Y) <= policy.MaxDisplayMagnitude &&
            point.X >= (double)_snapshot.Axes.XMinimum && point.X <= (double)_snapshot.Axes.XMaximum &&
            point.Y >= (double)_snapshot.Axes.YMinimum && point.Y <= (double)_snapshot.Axes.YMaximum;
    }
    private static MathExpressionReference? ExpressionOf(GraphPrimitive primitive) => primitive switch
    {
        GraphFunction function => function.Expression, GraphEquation equation => equation.Expression,
        GraphInequality inequality => inequality.Expression, _ => null
    };
    public static string Digest(GraphDefinition graph) => Convert.ToHexStringLower(
        SHA256.HashData(MathObjectCodec.Encode(graph)));
}
