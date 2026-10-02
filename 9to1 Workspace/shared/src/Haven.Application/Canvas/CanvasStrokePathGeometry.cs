using System.Text.Json.Serialization;

namespace Haven.Application.Canvas;

/// <summary>Authoritative donor geometry; original CanvasInkStroke.Samples remain input provenance.
/// No tilt or timestamps are inferred for smoothed donor elements.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CanvasStrokePathElement([property: JsonRequired] double X, [property: JsonRequired] double Y, [property: JsonRequired] double Pressure);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CanvasStrokePathPoint([property: JsonRequired] double X, [property: JsonRequired] double Y);
public enum CanvasStrokePathSegmentKind { Line, Quadratic, Cubic }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CanvasStrokePathSegment
{
    public required CanvasStrokePathSegmentKind Kind { get; init; }
    public required CanvasStrokePathElement End { get; init; }
    public CanvasStrokePathPoint? Control1 { get; init; }
    public CanvasStrokePathPoint? Control2 { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CanvasStrokePathGeometry
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    public required CanvasStrokePathElement Start { get; init; }
    public required List<CanvasStrokePathSegment> Segments { get; init; }
}
