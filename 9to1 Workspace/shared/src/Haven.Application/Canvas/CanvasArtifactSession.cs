using System.Security.Cryptography;
using System.Text.Json;

namespace Haven.Application;

public enum CanvasApiErrorCode
{
    NotFound,
    InvalidArgument,
    PermissionDenied,
    RevisionConflict,
    CapabilityUnavailable,
    UnsupportedFeature,
    FormatIncompatible,
    IoError,
    RuntimeError,
    Cancelled,
    OperationIdConflict,
    ConfirmationRequired,
    HistoryUnavailable
}

public sealed record CanvasActorContext(string CallerId, string DisplayName, string? Origin = null);

public sealed record CanvasMutationRequest(
    Guid BaseRevisionId,
    Guid OperationId,
    CanvasActorContext ActorContext);

public sealed record CanvasApiError(
    CanvasApiErrorCode Code,
    string Message,
    string Action,
    string? TargetId,
    bool IsRecoverable,
    bool CanRetry,
    IReadOnlyDictionary<string, string>? Details = null);

public sealed record CanvasApiResult<T>(T? Value, CanvasApiError? Error)
{
    public bool IsSuccess => Error is null;
    public static CanvasApiResult<T> Success(T value) => new(value, null);
    public static CanvasApiResult<T> Failure(CanvasApiError error) => new(default, error);
}

public sealed record CanvasMutationResult(
    Guid RevisionId,
    IReadOnlyList<Guid> ChangedIds,
    IReadOnlyList<string> Warnings);

public sealed record CanvasPageListResult(
    IReadOnlyList<CanvasPageSummary> Items,
    int? NextOffset,
    int TotalCount);

public sealed record CanvasPageSummary(
    Guid PageId,
    int Order,
    CanvasPageBounds? Bounds,
    string BackgroundKind,
    Guid RevisionId);

public sealed record CanvasChangeEvent(
    Guid EventId,
    Guid RevisionId,
    Guid OperationId,
    string CallerId,
    string Action,
    IReadOnlyList<Guid> ChangedIds,
    DateTimeOffset OccurredAt);

public sealed record CanvasChangePage(
    IReadOnlyList<CanvasChangeEvent> Events,
    long NextCursor,
    bool HasMore);

public sealed record CanvasModeConversionPreview(
    Guid PreviewId,
    Guid ArtifactId,
    Guid BaseRevisionId,
    CanvasDocumentMode SourceMode,
    CanvasDocumentMode TargetMode,
    CanvasPageBounds? TargetBounds,
    IReadOnlyList<Guid> CrossingObjectIds,
    IReadOnlyList<Guid> CrossingStrokeIds,
    IReadOnlyList<string> Warnings,
    bool RequiresExplicitConfirmation);

/// <summary>
/// Revision-checked Canvas domain operations over the canonical artifact.
/// A host must place public API calls behind Home's permission broker and
/// persist committed snapshots through Files; this class owns document rules,
/// semantic history, operation idempotency, and meaningful change events.
/// </summary>
public sealed class CanvasArtifactSession
{
    public const string CanonicalApiNamespace = "9to1.Canvas";
    public const int MaximumPageSize = 500;
    private const int MaximumHistoryEntries = 128;
    private const int MaximumIdempotencyEntries = 512;
    private const int MaximumEventEntries = 2048;

    private static readonly JsonSerializerOptions FingerprintOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly List<byte[]> _undoHistory = [];
    private readonly List<byte[]> _redoHistory = [];
    private readonly Dictionary<Guid, IdempotencyEntry> _idempotency = [];
    private readonly Queue<Guid> _idempotencyOrder = [];
    private readonly List<CanvasChangeEvent> _events = [];
    private CanvasArtifact _artifact;
    private long _eventCursor;

    public CanvasArtifactSession(CanvasArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var bytes = CanvasArtifactCodec.Serialize(artifact);
        _artifact = CanvasArtifactCodec.Deserialize(bytes);
    }

    public Guid ArtifactId
    {
        get { lock (_gate) return _artifact.ArtifactId; }
    }

    public Guid CurrentRevisionId
    {
        get { lock (_gate) return _artifact.RevisionId; }
    }

    public CanvasArtifact GetArtifactSnapshot()
    {
        lock (_gate) return Clone(_artifact);
    }

    public CanvasApiResult<CanvasPageListResult> ListPages(int offset = 0, int pageSize = 100)
    {
        lock (_gate)
        {
            if (offset < 0 || pageSize is < 1 or > MaximumPageSize)
                return Failure<CanvasPageListResult>(CanvasApiErrorCode.InvalidArgument, "Pages.List", "Offset and page size are outside the supported range.");

            var pages = _artifact.PageOrder
                .Select((id, index) => (Page: _artifact.Pages.First(page => page.PageId == id), Index: index))
                .Skip(offset)
                .Take(pageSize)
                .Select(item => new CanvasPageSummary(
                    item.Page.PageId,
                    item.Index,
                    item.Page.Bounds,
                    item.Page.Background.Kind,
                    item.Page.RevisionId))
                .ToArray();
            var nextOffset = offset + pages.Length;
            return CanvasApiResult<CanvasPageListResult>.Success(new CanvasPageListResult(
                pages,
                nextOffset < _artifact.PageOrder.Count ? nextOffset : null,
                _artifact.PageOrder.Count));
        }
    }

    public CanvasApiResult<CanvasPage> GetPage(Guid pageId)
    {
        lock (_gate)
        {
            var page = _artifact.Pages.FirstOrDefault(candidate => candidate.PageId == pageId);
            return page is null
                ? Failure<CanvasPage>(CanvasApiErrorCode.NotFound, "Pages.Get", "Canvas page was not found.", pageId)
                : CanvasApiResult<CanvasPage>.Success(ClonePage(page));
        }
    }

    public CanvasApiResult<CanvasMutationResult> RenameArtifact(CanvasMutationRequest request, string displayName) =>
        Mutate(request, "Artifact.Rename", _artifact.ArtifactId, displayName, artifact =>
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "DisplayName cannot be empty.");
            var normalized = displayName.Trim();
            if (string.Equals(artifact.DisplayName, normalized, StringComparison.Ordinal))
                return MutationNoChange();
            artifact.DisplayName = normalized;
            return MutationChanged(artifact.ArtifactId);
        });

    public CanvasApiResult<CanvasMutationResult> CreatePage(CanvasMutationRequest request, int? insertAt = null) =>
        Mutate(request, "Pages.Create", null, insertAt, artifact =>
        {
            if (artifact.CanvasMode != CanvasDocumentMode.Paged)
                return MutationFailure(CanvasApiErrorCode.UnsupportedFeature, "Pages can only be added to a paged Canvas artifact.");
            var insertionIndex = insertAt ?? artifact.PageOrder.Count;
            if (insertionIndex < 0 || insertionIndex > artifact.PageOrder.Count)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Page insertion order is outside the current page range.");

            var template = artifact.Pages.FirstOrDefault();
            var layer = new CanvasLayer { Name = "Layer 1" };
            var page = new CanvasPage
            {
                Bounds = template?.Bounds ?? new CanvasPageBounds(0, 0, 794, 1123),
                Background = template?.Background ?? new CanvasBackgroundDefinition(),
                Layers = [layer],
                LayerOrder = [layer.LayerId]
            };
            artifact.Pages.Add(page);
            artifact.PageOrder.Insert(insertionIndex, page.PageId);
            return MutationChanged(page.PageId);
        });

    public CanvasApiResult<CanvasMutationResult> ReorderPage(CanvasMutationRequest request, IReadOnlyList<Guid> pageOrder) =>
        Mutate(request, "Pages.Reorder", null, pageOrder, artifact =>
        {
            if (pageOrder is null || pageOrder.Count != artifact.PageOrder.Count
                || pageOrder.Distinct().Count() != pageOrder.Count
                || !artifact.PageOrder.ToHashSet().SetEquals(pageOrder))
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Page order must include every page exactly once.");
            if (artifact.PageOrder.SequenceEqual(pageOrder)) return MutationNoChange();
            artifact.PageOrder = pageOrder.ToList();
            return MutationChanged(pageOrder.ToArray());
        });

    public CanvasApiResult<CanvasMutationResult> SetPageBounds(
        CanvasMutationRequest request,
        Guid pageId,
        CanvasPageBounds bounds) =>
        Mutate(request, "Pages.SetBounds", pageId, bounds, artifact =>
        {
            var index = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            if (artifact.CanvasMode != CanvasDocumentMode.Paged)
                return MutationFailure(CanvasApiErrorCode.UnsupportedFeature, "Infinite Canvas pages do not have artificial bounds.");
            var page = artifact.Pages[index];
            artifact.Pages[index] = page with { Bounds = bounds, RevisionId = Guid.NewGuid() };
            return MutationChanged(page.PageId);
        });

    public CanvasApiResult<CanvasMutationResult> SetPageBackground(
        CanvasMutationRequest request,
        Guid pageId,
        CanvasBackgroundDefinition background) =>
        Mutate(request, "Pages.SetBackground", pageId, background, artifact =>
        {
            var index = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            if (background is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Page background is required.");
            var page = artifact.Pages[index];
            artifact.Pages[index] = page with { Background = background, RevisionId = Guid.NewGuid() };
            return MutationChanged(page.PageId);
        });

    public CanvasApiResult<CanvasMutationResult> CreateLayer(
        CanvasMutationRequest request,
        Guid pageId,
        string name,
        int? insertAt = null) =>
        Mutate(request, "Layers.Create", pageId, new { name, insertAt }, artifact =>
        {
            var index = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            if (string.IsNullOrWhiteSpace(name)) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Layer name cannot be empty.");
            var page = artifact.Pages[index];
            var insertionIndex = insertAt ?? page.LayerOrder.Count;
            if (insertionIndex < 0 || insertionIndex > page.LayerOrder.Count)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Layer insertion order is outside the current layer range.");
            var layer = new CanvasLayer { Name = name.Trim() };
            var layers = page.Layers.ToList();
            layers.Add(layer);
            var order = page.LayerOrder.ToList();
            order.Insert(insertionIndex, layer.LayerId);
            artifact.Pages[index] = page with { Layers = layers, LayerOrder = order, RevisionId = Guid.NewGuid() };
            return MutationChanged(page.PageId, layer.LayerId);
        });

    public CanvasApiResult<CanvasMutationResult> RenameLayer(
        CanvasMutationRequest request,
        Guid pageId,
        Guid layerId,
        string name) =>
        Mutate(request, "Layers.Rename", layerId, new { pageId, name }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            if (string.IsNullOrWhiteSpace(name)) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Layer name cannot be empty.");
            var page = artifact.Pages[pageIndex];
            var layerIndex = page.Layers.FindIndex(layer => layer.LayerId == layerId);
            if (layerIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas layer was not found.");
            var layer = page.Layers[layerIndex];
            var layers = page.Layers.ToList();
            layers[layerIndex] = layer with { Name = name.Trim(), RevisionId = Guid.NewGuid() };
            artifact.Pages[pageIndex] = page with { Layers = layers, RevisionId = Guid.NewGuid() };
            return MutationChanged(pageId, layerId);
        });

    public CanvasApiResult<CanvasMutationResult> SetLayerVisibility(
        CanvasMutationRequest request,
        Guid pageId,
        Guid layerId,
        bool isVisible) =>
        Mutate(request, "Layers.SetVisibility", layerId, new { pageId, isVisible }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[pageIndex];
            var layerIndex = page.Layers.FindIndex(layer => layer.LayerId == layerId);
            if (layerIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas layer was not found.");
            var layer = page.Layers[layerIndex];
            if (layer.IsVisible == isVisible) return MutationNoChange();
            var layers = page.Layers.ToList();
            layers[layerIndex] = layer with { IsVisible = isVisible, RevisionId = Guid.NewGuid() };
            artifact.Pages[pageIndex] = page with { Layers = layers, RevisionId = Guid.NewGuid() };
            return MutationChanged(pageId, layerId);
        });

    public CanvasApiResult<CanvasMutationResult> SetLayerLocked(
        CanvasMutationRequest request,
        Guid pageId,
        Guid layerId,
        bool isLocked) =>
        Mutate(request, "Layers.SetLocked", layerId, new { pageId, isLocked }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[pageIndex];
            var layerIndex = page.Layers.FindIndex(layer => layer.LayerId == layerId);
            if (layerIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas layer was not found.");
            var layer = page.Layers[layerIndex];
            if (layer.IsLocked == isLocked) return MutationNoChange();
            var layers = page.Layers.ToList();
            layers[layerIndex] = layer with { IsLocked = isLocked, RevisionId = Guid.NewGuid() };
            artifact.Pages[pageIndex] = page with { Layers = layers, RevisionId = Guid.NewGuid() };
            return MutationChanged(pageId, layerId);
        });

    public CanvasApiResult<CanvasMutationResult> MoveLayer(CanvasMutationRequest request, Guid pageId, Guid layerId, int toIndex) =>
        Mutate(request, "Layers.Reorder", layerId, new { pageId, toIndex }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[pageIndex];
            var fromIndex = page.LayerOrder.IndexOf(layerId);
            if (fromIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas layer was not found.");
            if (toIndex < 0 || toIndex >= page.LayerOrder.Count)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Layer order is outside the current layer range.");
            if (fromIndex == toIndex) return MutationNoChange();
            var order = page.LayerOrder.ToList();
            order.RemoveAt(fromIndex);
            order.Insert(toIndex, layerId);
            artifact.Pages[pageIndex] = page with { LayerOrder = order, RevisionId = Guid.NewGuid() };
            return MutationChanged(pageId, layerId);
        });

    public CanvasApiResult<CanvasMutationResult> MoveObject(
        CanvasMutationRequest request,
        Guid pageId,
        Guid objectId,
        CanvasRect geometry,
        CanvasTransform? transform = null) =>
        Mutate(request, "Objects.Transform", objectId, new { pageId, geometry, transform }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[pageIndex];
            var objectIndex = page.Objects.FindIndex(item => item.ObjectId == objectId);
            if (objectIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas object was not found.");
            if (geometry is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Object geometry is required.");
            var item = page.Objects[objectIndex];
            if (page.Layers.First(layer => layer.LayerId == item.LayerId).IsLocked)
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects on a locked layer cannot be moved.");
            var objects = page.Objects.ToList();
            objects[objectIndex] = item with
            {
                Geometry = geometry,
                Transform = transform ?? item.Transform,
                RevisionId = Guid.NewGuid()
            };
            artifact.Pages[pageIndex] = page with { Objects = objects, RevisionId = Guid.NewGuid() };
            return MutationChanged(pageId, objectId);
        });

    public CanvasApiResult<CanvasMutationResult> MoveObjectToLayer(
        CanvasMutationRequest request,
        Guid pageId,
        Guid objectId,
        Guid destinationLayerId) =>
        Mutate(request, "Layers.MoveObjects", objectId, new { pageId, destinationLayerId }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[pageIndex];
            var objectIndex = page.Objects.FindIndex(item => item.ObjectId == objectId);
            if (objectIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas object was not found.");
            var destination = page.Layers.FirstOrDefault(layer => layer.LayerId == destinationLayerId);
            if (destination is null) return MutationFailure(CanvasApiErrorCode.NotFound, "Destination layer was not found.");
            var item = page.Objects[objectIndex];
            if (page.Layers.First(layer => layer.LayerId == item.LayerId).IsLocked || destination.IsLocked)
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects cannot be moved from or into a locked layer.");
            if (item.LayerId == destinationLayerId) return MutationNoChange();
            var objects = page.Objects.ToList();
            objects[objectIndex] = item with { LayerId = destinationLayerId, RevisionId = Guid.NewGuid() };
            artifact.Pages[pageIndex] = page with { Objects = objects, RevisionId = Guid.NewGuid() };
            return MutationChanged(pageId, objectId, destinationLayerId);
        });

    public CanvasApiResult<CanvasMutationResult> AddStructuredStroke(
        CanvasMutationRequest request,
        Guid pageId,
        CanvasInkStroke stroke) =>
        Mutate(request, "Ink.CreateFromSamples", stroke?.StrokeId, stroke, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            if (stroke is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Structured stroke is required.");
            var page = artifact.Pages[pageIndex];
            if (!page.Layers.Any(layer => layer.LayerId == stroke.LayerId))
                return MutationFailure(CanvasApiErrorCode.NotFound, "Stroke layer was not found on the target page.");
            if (page.Layers.First(layer => layer.LayerId == stroke.LayerId).IsLocked)
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Ink cannot be added to a locked layer.");
            if (page.Strokes.Any(item => item.StrokeId == stroke.StrokeId))
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Stroke ID already exists on the page.");
            var strokes = page.Strokes.Append(CloneStroke(stroke)).ToList();
            artifact.Pages[pageIndex] = page with
            {
                Strokes = strokes,
                StrokeOrder = page.StrokeOrder.Append(stroke.StrokeId).ToList(),
                RevisionId = Guid.NewGuid()
            };
            return MutationChanged(pageId, stroke.StrokeId);
        });

    public CanvasApiResult<CanvasMutationResult> Undo(CanvasMutationRequest request) =>
        MutateHistory(request, "History.Undo", undo: true);

    public CanvasApiResult<CanvasMutationResult> Redo(CanvasMutationRequest request) =>
        MutateHistory(request, "History.Redo", undo: false);

    public CanvasApiResult<CanvasModeConversionPreview> PreviewModeConversion(
        CanvasDocumentMode targetMode,
        CanvasPageBounds? targetBounds = null)
    {
        lock (_gate)
        {
            if (!Enum.IsDefined(targetMode))
                return Failure<CanvasModeConversionPreview>(CanvasApiErrorCode.InvalidArgument, "DocumentSettings.PreviewModeConversion", "Target mode is not supported.");
            if (targetMode == CanvasDocumentMode.Paged && targetBounds is null)
                return Failure<CanvasModeConversionPreview>(CanvasApiErrorCode.InvalidArgument, "DocumentSettings.PreviewModeConversion", "Paged conversion requires proposed page bounds.");
            if (targetBounds is not null && !ValidBounds(targetBounds))
                return Failure<CanvasModeConversionPreview>(CanvasApiErrorCode.InvalidArgument, "DocumentSettings.PreviewModeConversion", "Proposed page bounds are invalid.");

            var crossingObjects = new List<Guid>();
            var crossingStrokes = new List<Guid>();
            if (_artifact.CanvasMode == CanvasDocumentMode.Infinite && targetMode == CanvasDocumentMode.Paged && targetBounds is { } bounds)
            {
                foreach (var page in _artifact.Pages)
                {
                    crossingObjects.AddRange(page.Objects.Where(item => !InsidePage(item.Geometry, item.Transform, bounds)).Select(item => item.ObjectId));
                    crossingStrokes.AddRange(page.Strokes.Where(stroke => !StrokeInsidePage(stroke, bounds)).Select(stroke => stroke.StrokeId));
                }
            }

            var warnings = crossingObjects.Count + crossingStrokes.Count == 0
                ? Array.Empty<string>()
                : [$"{crossingObjects.Count} object(s) and {crossingStrokes.Count} stroke(s) extend beyond the proposed page bounds; their stored geometry will be preserved."];
            return CanvasApiResult<CanvasModeConversionPreview>.Success(new CanvasModeConversionPreview(
                Guid.NewGuid(),
                _artifact.ArtifactId,
                _artifact.RevisionId,
                _artifact.CanvasMode,
                targetMode,
                targetBounds,
                crossingObjects,
                crossingStrokes,
                warnings,
                RequiresExplicitConfirmation: targetMode != _artifact.CanvasMode));
        }
    }

    public CanvasApiResult<CanvasMutationResult> ConfirmModeConversion(
        CanvasMutationRequest request,
        CanvasModeConversionPreview preview,
        bool explicitConfirmation) =>
        Mutate(request, "DocumentSettings.ConvertMode", preview?.ArtifactId, preview, artifact =>
        {
            if (preview is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "A mode conversion preview is required.");
            if (preview.ArtifactId != artifact.ArtifactId || preview.BaseRevisionId != artifact.RevisionId
                || preview.SourceMode != artifact.CanvasMode)
                return MutationFailure(CanvasApiErrorCode.RevisionConflict, "Canvas changed after the conversion preview; create a new preview.", canRetry: true);
            if (preview.TargetMode == artifact.CanvasMode) return MutationNoChange();
            if (!explicitConfirmation)
                return MutationFailure(CanvasApiErrorCode.ConfirmationRequired, "Mode conversion requires explicit confirmation.");
            if (preview.TargetMode == CanvasDocumentMode.Paged && (preview.TargetBounds is null || !ValidBounds(preview.TargetBounds)))
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Paged conversion requires valid page bounds.");

            artifact.CanvasMode = preview.TargetMode;
            for (var index = 0; index < artifact.Pages.Count; index++)
            {
                var page = artifact.Pages[index];
                artifact.Pages[index] = page with
                {
                    Bounds = preview.TargetMode == CanvasDocumentMode.Paged ? preview.TargetBounds : null,
                    RevisionId = Guid.NewGuid()
                };
            }
            return MutationChanged([artifact.ArtifactId, .. artifact.PageOrder]);
        });

    public CanvasApiResult<CanvasChangePage> GetChanges(long afterCursor = 0, int pageSize = 100)
    {
        lock (_gate)
        {
            if (afterCursor < 0 || pageSize is < 1 or > MaximumPageSize)
                return Failure<CanvasChangePage>(CanvasApiErrorCode.InvalidArgument, "Events.GetChanges", "Change cursor or page size is invalid.");
            var matching = _events.Where((_, index) => index + 1 > afterCursor).Take(pageSize).ToArray();
            var nextCursor = matching.Length == 0 ? afterCursor : _events.IndexOf(matching[^1]) + 1L;
            var hasMore = _events.Any((_, index) => index + 1 > nextCursor);
            return CanvasApiResult<CanvasChangePage>.Success(new CanvasChangePage(matching, nextCursor, hasMore));
        }
    }

    private CanvasApiResult<CanvasMutationResult> Mutate(
        CanvasMutationRequest request,
        string action,
        Guid? targetId,
        object? input,
        Func<CanvasArtifact, MutationPlan> apply)
    {
        CanvasChangeEvent? change = null;
        CanvasApiResult<CanvasMutationResult> result;
        lock (_gate)
        {
            var requestError = ValidateRequest(request, action, targetId);
            if (requestError is not null) return CanvasApiResult<CanvasMutationResult>.Failure(requestError);
            var fingerprint = Fingerprint(action, targetId, request.ActorContext.CallerId, input);
            if (_idempotency.TryGetValue(request.OperationId, out var prior))
            {
                if (!string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal))
                    return Failure<CanvasMutationResult>(CanvasApiErrorCode.OperationIdConflict, action, "OperationID was already used for a different action or payload.", targetId);
                return CanvasApiResult<CanvasMutationResult>.Success(prior.Result);
            }
            if (request.BaseRevisionId != _artifact.RevisionId)
                return Failure<CanvasMutationResult>(CanvasApiErrorCode.RevisionConflict, action, "Canvas revision changed before the mutation was applied.", targetId, canRetry: true);

            var before = CanvasArtifactCodec.Serialize(_artifact);
            var plan = apply(_artifact);
            if (plan.Error is not null)
            {
                _artifact = CanvasArtifactCodec.Deserialize(before);
                return CanvasApiResult<CanvasMutationResult>.Failure(ToApiError(plan.Error, action, targetId));
            }
            if (plan.ChangedIds.Count == 0)
            {
                var noChange = new CanvasMutationResult(_artifact.RevisionId, [], []);
                CacheOperation(request.OperationId, fingerprint, noChange);
                return CanvasApiResult<CanvasMutationResult>.Success(noChange);
            }

            _artifact.RevisionId = Guid.NewGuid();
            var issues = CanvasArtifactCodec.Validate(_artifact);
            if (issues.Count > 0)
            {
                _artifact = CanvasArtifactCodec.Deserialize(before);
                return Failure<CanvasMutationResult>(
                    CanvasApiErrorCode.InvalidArgument,
                    action,
                    "Mutation would create an invalid Canvas artifact.",
                    targetId,
                    details: new Dictionary<string, string> { ["validationIssueCount"] = issues.Count.ToString() });
            }

            _undoHistory.Add(before);
            if (_undoHistory.Count > MaximumHistoryEntries) _undoHistory.RemoveAt(0);
            _redoHistory.Clear();
            var mutation = new CanvasMutationResult(_artifact.RevisionId, plan.ChangedIds.Distinct().ToArray(), plan.Warnings);
            CacheOperation(request.OperationId, fingerprint, mutation);
            change = AddEvent(request, action, mutation);
            result = CanvasApiResult<CanvasMutationResult>.Success(mutation);
        }
        return result;
    }

    private CanvasApiResult<CanvasMutationResult> MutateHistory(CanvasMutationRequest request, string action, bool undo)
    {
        CanvasApiResult<CanvasMutationResult> result;
        lock (_gate)
        {
            var requestError = ValidateRequest(request, action, _artifact.ArtifactId);
            if (requestError is not null) return CanvasApiResult<CanvasMutationResult>.Failure(requestError);
            var fingerprint = Fingerprint(action, _artifact.ArtifactId, request.ActorContext.CallerId, null);
            if (_idempotency.TryGetValue(request.OperationId, out var prior))
            {
                if (!string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal))
                    return Failure<CanvasMutationResult>(CanvasApiErrorCode.OperationIdConflict, action, "OperationID was already used for a different action.", _artifact.ArtifactId);
                return CanvasApiResult<CanvasMutationResult>.Success(prior.Result);
            }
            if (request.BaseRevisionId != _artifact.RevisionId)
                return Failure<CanvasMutationResult>(CanvasApiErrorCode.RevisionConflict, action, "Canvas revision changed before the history operation was applied.", _artifact.ArtifactId, canRetry: true);

            var source = undo ? _undoHistory : _redoHistory;
            var destination = undo ? _redoHistory : _undoHistory;
            if (source.Count == 0)
                return Failure<CanvasMutationResult>(CanvasApiErrorCode.HistoryUnavailable, action, undo ? "No Canvas change can be undone." : "No Canvas change can be redone.", _artifact.ArtifactId);

            destination.Add(CanvasArtifactCodec.Serialize(_artifact));
            var restoredBytes = source[^1];
            source.RemoveAt(source.Count - 1);
            _artifact = CanvasArtifactCodec.Deserialize(restoredBytes);
            _artifact.RevisionId = Guid.NewGuid();
            var changedIds = EnumerateEntityIds(_artifact).Distinct().ToArray();
            var mutation = new CanvasMutationResult(_artifact.RevisionId, changedIds, []);
            CacheOperation(request.OperationId, fingerprint, mutation);
            AddEvent(request, action, mutation);
            result = CanvasApiResult<CanvasMutationResult>.Success(mutation);
        }
        return result;
    }

    private CanvasApiError? ValidateRequest(CanvasMutationRequest request, string action, Guid? targetId)
    {
        if (request is null || request.OperationId == Guid.Empty || request.BaseRevisionId == Guid.Empty
            || request.ActorContext is null || string.IsNullOrWhiteSpace(request.ActorContext.CallerId))
            return new CanvasApiError(CanvasApiErrorCode.InvalidArgument, "Mutation request requires a BaseRevisionID, OperationID, and actor identity.", action, targetId?.ToString("D"), false, false);
        return null;
    }

    private void CacheOperation(Guid operationId, string fingerprint, CanvasMutationResult result)
    {
        if (_idempotency.ContainsKey(operationId)) return;
        _idempotency.Add(operationId, new IdempotencyEntry(fingerprint, result));
        _idempotencyOrder.Enqueue(operationId);
        while (_idempotencyOrder.Count > MaximumIdempotencyEntries)
            _idempotency.Remove(_idempotencyOrder.Dequeue());
    }

    private CanvasChangeEvent AddEvent(CanvasMutationRequest request, string action, CanvasMutationResult mutation)
    {
        var entry = new CanvasChangeEvent(
            Guid.NewGuid(), mutation.RevisionId, request.OperationId, request.ActorContext.CallerId,
            action, mutation.ChangedIds, DateTimeOffset.UtcNow);
        _events.Add(entry);
        _eventCursor++;
        if (_events.Count > MaximumEventEntries) _events.RemoveAt(0);
        return entry;
    }

    private static string Fingerprint(string action, Guid? targetId, string callerId, object? input)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { action, targetId, callerId, input }, FingerprintOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static CanvasArtifact Clone(CanvasArtifact artifact) => CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(artifact));

    private static CanvasPage ClonePage(CanvasPage page)
    {
        var artifact = CanvasArtifact.Create();
        artifact.PageOrder = [page.PageId];
        artifact.Pages = [page];
        return CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(artifact)).Pages[0];
    }

    private static CanvasInkStroke CloneStroke(CanvasInkStroke stroke)
    {
        var page = CanvasArtifact.Create().Pages[0] with { Strokes = [stroke], StrokeOrder = [stroke.StrokeId] };
        var artifact = CanvasArtifact.Create();
        artifact.Pages = [page];
        artifact.PageOrder = [page.PageId];
        return CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(artifact)).Pages[0].Strokes[0];
    }

    private static bool ValidBounds(CanvasPageBounds bounds) =>
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
        && double.IsFinite(bounds.Width) && bounds.Width > 0
        && double.IsFinite(bounds.Height) && bounds.Height > 0
        && !string.IsNullOrWhiteSpace(bounds.Orientation);

    private static bool InsidePage(CanvasRect geometry, CanvasTransform transform, CanvasPageBounds bounds)
    {
        var left = geometry.X + transform.TranslateX;
        var top = geometry.Y + transform.TranslateY;
        return left >= bounds.X && top >= bounds.Y
            && left + geometry.Width <= bounds.X + bounds.Width
            && top + geometry.Height <= bounds.Y + bounds.Height;
    }

    private static bool StrokeInsidePage(CanvasInkStroke stroke, CanvasPageBounds bounds)
    {
        if (stroke.Samples.Count == 0) return true;
        return stroke.Samples.All(sample => sample.X >= bounds.X && sample.X <= bounds.X + bounds.Width
            && sample.Y >= bounds.Y && sample.Y <= bounds.Y + bounds.Height);
    }

    private static IEnumerable<Guid> EnumerateEntityIds(CanvasArtifact artifact)
    {
        yield return artifact.ArtifactId;
        foreach (var page in artifact.Pages)
        {
            yield return page.PageId;
            foreach (var layer in page.Layers) yield return layer.LayerId;
            foreach (var item in page.Objects) yield return item.ObjectId;
            foreach (var stroke in page.Strokes) yield return stroke.StrokeId;
        }
    }

    private static MutationPlan MutationChanged(params Guid[] ids) => new(ids, [], null);
    private static MutationPlan MutationNoChange() => new([], [], null);
    private static MutationPlan MutationFailure(CanvasApiErrorCode code, string message, bool canRetry = false) =>
        new([], [], new MutationError(code, message, canRetry));

    private static CanvasApiError ToApiError(MutationError error, string action, Guid? targetId) =>
        new(error.Code, error.Message, action, targetId?.ToString("D"), error.IsRecoverable, error.CanRetry);

    private static CanvasApiResult<T> Failure<T>(
        CanvasApiErrorCode code,
        string action,
        string message,
        Guid? targetId = null,
        bool canRetry = false,
        IReadOnlyDictionary<string, string>? details = null) =>
        CanvasApiResult<T>.Failure(new CanvasApiError(code, message, action, targetId?.ToString("D"), canRetry, canRetry, details));

    private sealed record MutationPlan(IReadOnlyList<Guid> ChangedIds, IReadOnlyList<string> Warnings, MutationError? Error);
    private sealed record MutationError(CanvasApiErrorCode Code, string Message, bool CanRetry, bool IsRecoverable = true);
    private sealed record IdempotencyEntry(string Fingerprint, CanvasMutationResult Result);
}
