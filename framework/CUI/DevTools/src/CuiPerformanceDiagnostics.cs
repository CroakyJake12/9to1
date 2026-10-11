namespace Haven.CUI.DevTools;

/// <summary>
/// Rendering and performance diagnostics over the DevTools snapshot contract. Unavailable
/// telemetry is reported as Na, empty telemetry as null, and derived values are labelled as
/// estimates rather than presented as measurements.
/// </summary>
public sealed class CuiPerformanceDiagnostics
{
    public const string UnavailableTelemetry = "Na";

    public static async Task<CuiPerformanceReport> CaptureAsync(
        IElementDiagnosticsSnapshotProvider provider,
        IEnumerable<ElementId> elements,
        long treeRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(elements);
        if (treeRevision < 0) throw new ArgumentOutOfRangeException(nameof(treeRevision));

        var ordered = elements.Distinct().OrderBy(elementId => elementId.Value, StringComparer.Ordinal).ToArray();
        var snapshots = new Dictionary<ElementId, PerformanceSnapshot>();
        var unavailable = new List<ElementId>();

        foreach (var elementId in ordered)
        {
            var snapshot = await provider.CaptureAsync(elementId, treeRevision, cancellationToken);
            if (snapshot is null)
            {
                unavailable.Add(elementId);
                continue;
            }

            snapshots[elementId] = snapshot.Performance;
        }

        var captured = snapshots.Values;
        var hottest = captured.Count == 0
            ? null
            : captured.OrderByDescending(snapshot => snapshot.LastPaintMilliseconds)
                .ThenByDescending(snapshot => snapshot.LastMeasureMilliseconds)
                .ThenByDescending(snapshot => snapshot.LastArrangeMilliseconds)
                .First();

        return new CuiPerformanceReport(
            treeRevision,
            DiagnosticsCollections.Copy<ElementId, PerformanceSnapshot>(snapshots),
            DiagnosticsCollections.Copy(unavailable),
            hottest,
            captured.Count == 0 ? 0 : Math.Round(captured.Average(snapshot => snapshot.AverageFrameMilliseconds), 3),
            captured.Sum(snapshot => snapshot.DrawCommandCount),
            captured.Sum(snapshot => snapshot.EstimatedRetainedBytes),
            captured.Count == 0 ? 0 : captured.Max(snapshot => snapshot.LastMeasureMilliseconds));
    }
}

public sealed record CuiPerformanceReport(
    long TreeRevision,
    IReadOnlyDictionary<ElementId, PerformanceSnapshot> Snapshots,
    IReadOnlyList<ElementId> UnavailableElements,
    PerformanceSnapshot? HottestElement,
    double AverageFrameMilliseconds,
    long TotalDrawCommandCount,
    long EstimatedRetainedBytes,
    double SlowestMeasureMilliseconds)
{
    public string DescribeAvailability =>
        UnavailableElements.Count == 0
            ? "all elements reported telemetry"
            : $"{UnavailableElements.Count} element(s) reported {CuiPerformanceDiagnostics.UnavailableTelemetry}";

    public string DescribeEstimates =>
        $"{nameof(EstimatedRetainedBytes)} is an estimate, not a measured allocation";

    public string AvailabilityLabel(ElementId elementId) =>
        Snapshots.ContainsKey(elementId) ? "measured" : CuiPerformanceDiagnostics.UnavailableTelemetry;
}