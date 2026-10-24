using Haven.Application.Canvas;

namespace HavenOS.Apps.Canvas;

/// <summary>Minimal immutable semantic selection metadata from the actual mapped native
/// revision. This observation is never a Files/Home grant or a persisted selection.</summary>
public sealed record CanvasStrokeSelectionEntry(Guid StrokeId, int Position, int Count, string Colour,
    double Width, string LayerName, bool IsLocked, bool CanDelete, bool CanTranslate)
{
    public string Summary => $"Stroke {Position} of {Count}, {Colour}, width {Width}, {LayerName}{(IsLocked ? ", locked" : "")}";
}
public sealed record CanvasStrokeSelectionSnapshot(Guid ArtifactId, Guid RevisionId,
    IReadOnlyList<CanvasStrokeSelectionEntry> Strokes);

/// <summary>The exact retained donor penpath for a transient selection contour. It describes
/// the real curve, not an estimated paint-footprint bounding rectangle or new document object.</summary>
public sealed record CanvasSelectionPath(Guid ArtifactId, Guid RevisionId, Guid StrokeId, CanvasStrokePathGeometry Geometry);

/// <summary>Typed accelerated-surface input port consumed by the canonical CUI. Previewing
/// selection/movement does not mutate ink; only the original Home request port can commit.</summary>
public interface ICanvasSelectionInput
{
    event EventHandler? Changed;
    bool CanCapture { get; }
    bool HasTarget { get; }
    bool CanMove { get; }
    bool CanDelete { get; }
    bool HasMovePreview { get; }
    bool HasSubmittedOperation { get; }
    string TargetSummary { get; }
    string MoveSummary { get; }
    string Status { get; }
    string FeedbackStatus { get; }
    void ChooseTarget(int direction);
    void ClearTarget();
    void AdjustMove(double deltaX, double deltaY);
    Task RequestMoveAsync(CancellationToken cancellationToken = default);
    Task RequestDeleteAsync(CancellationToken cancellationToken = default);
}
