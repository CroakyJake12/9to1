namespace Haven.Application.Canvas;

/// <summary>Detached bounded admission of actual geometry. No mutation or donor callback occurs here.</summary>
internal static class CanvasStrokePathCapture
{
    internal const int MaximumSegments = CanvasStructuredStrokeTransactionLimits.MaximumSegmentsPerPath;
    internal static bool TryCapture(CanvasStrokePathGeometry? input, out CanvasStrokePathGeometry? captured)
    {
        captured = null;
        if (input is null || input.SchemaVersion != 1 || !Valid(input.Start) || input.Segments is null) return false;
        var segments = new List<CanvasStrokePathSegment>();
        foreach (var segment in input.Segments)
        {
            if (segments.Count == MaximumSegments || segment is null || !Valid(segment.End)) return false;
            var valid = segment.Kind switch
            {
                CanvasStrokePathSegmentKind.Line => segment.Control1 is null && segment.Control2 is null,
                CanvasStrokePathSegmentKind.Quadratic => Valid(segment.Control1) && segment.Control2 is null,
                CanvasStrokePathSegmentKind.Cubic => Valid(segment.Control1) && Valid(segment.Control2),
                _ => false
            };
            if (!valid) return false;
            segments.Add(new CanvasStrokePathSegment
            {
                Kind = segment.Kind,
                End = new(segment.End.X, segment.End.Y, segment.End.Pressure),
                Control1 = segment.Control1 is { } first ? new(first.X, first.Y) : null,
                Control2 = segment.Control2 is { } second ? new(second.X, second.Y) : null
            });
        }
        if (segments.Count == 0) return false;
        captured = new CanvasStrokePathGeometry
        {
            Start = new(input.Start.X, input.Start.Y, input.Start.Pressure), Segments = segments
        };
        return true;
    }
    private static bool Valid(CanvasStrokePathElement? value) => value is not null
        && double.IsFinite(value.X) && double.IsFinite(value.Y)
        && double.IsFinite(value.Pressure) && value.Pressure >= 0 && value.Pressure <= 1;
    private static bool Valid(CanvasStrokePathPoint? value) => value is not null
        && double.IsFinite(value.X) && double.IsFinite(value.Y);
}
