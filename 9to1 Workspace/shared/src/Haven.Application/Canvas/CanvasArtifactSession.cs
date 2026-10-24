using Haven.Application.Canvas;
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

public sealed record CanvasObjectListResult(
    IReadOnlyList<CanvasObject> Items,
    int? NextOffset,
    int TotalCount);

public sealed record CanvasStrokeListResult(
    IReadOnlyList<CanvasInkStroke> Items,
    int? NextOffset,
    int TotalCount);

public sealed record CanvasLayerListResult(
    IReadOnlyList<CanvasLayer> Items,
    int? NextOffset,
    int TotalCount);

public enum CanvasObjectSearchCoverage
{
    AccessibilityAndReferenceMetadataOnly
}

public sealed record CanvasObjectSearchQuery(
    Guid PageId,
    string? Text = null,
    string? ObjectTypeId = null,
    Guid? LayerId = null,
    int Offset = 0,
    int PageSize = 100);

public sealed record CanvasObjectSearchHit(
    Guid ObjectId,
    string ObjectTypeId,
    Guid LayerId,
    bool LayerVisible,
    bool LayerLocked,
    CanvasRect Geometry,
    string? AccessibleName,
    string? AccessibleDescription,
    string? AccessibleAltText,
    CanvasSharedObjectReference? SharedObjectRef,
    Guid RevisionId);

/// <summary>Canvas searches typed metadata only. Opaque shared payload text is not
/// scanned; Coverage and UnsearchedPayloadObjectCount make that limitation explicit.</summary>
public sealed record CanvasObjectSearchResult(
    IReadOnlyList<CanvasObjectSearchHit> Items,
    int? NextOffset,
    int TotalCount,
    CanvasObjectSearchCoverage Coverage,
    int UnsearchedPayloadObjectCount);

public sealed record CanvasSelectedSetTranslationResult(
    Guid RevisionId,
    IReadOnlyList<Guid> ChangedObjectIds,
    double DeltaX,
    double DeltaY,
    IReadOnlyList<string> Warnings);

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
public sealed partial class CanvasArtifactSession
{
    private bool _applyingMutation;
    public const string CanonicalApiNamespace = "9to1.Canvas";
    public const int MaximumPageSize = 500;
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
    private long _discardedEarlierHistoryEntries;

    public CanvasArtifactSession(CanvasArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var bytes = CanvasArtifactCodec.Serialize(artifact);
        _artifact = CanvasArtifactCodec.Deserialize(bytes);
        if (_artifact.SemanticHistory is { } history)
        {
            _undoHistory.AddRange(history.Undo.Select(frame => Convert.FromBase64String(frame.PayloadBase64)));
            _redoHistory.AddRange(history.Redo.Select(frame => Convert.FromBase64String(frame.PayloadBase64)));
            _discardedEarlierHistoryEntries = history.DiscardedEarlierEntries;
            _artifact.SemanticHistory = null;
        }
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
        lock (_gate)
        {
            var snapshot = Clone(_artifact);
            snapshot.SemanticHistory = CaptureHistory();
            return snapshot;
        }
    }

    private ProductivitySnapshotHistory CaptureHistory()
    {
        ProductivityHistorySnapshot Frame(byte[] bytes)
        {
            var snapshot = CanvasArtifactCodec.Deserialize(bytes);
            return ProductivitySnapshotHistory.Capture(bytes, snapshot.RevisionId.ToString("D"));
        }
        return new ProductivitySnapshotHistory
        {
            OwnerFormat = CanvasArtifactFile.CanonicalFormat,
            ArtifactId = _artifact.ArtifactId,
            CurrentRevision = _artifact.RevisionId.ToString("D"),
            CurrentSnapshotHash = Convert.ToHexString(SHA256.HashData(CanvasArtifactCodec.SerializeSnapshot(_artifact))),
            Undo = Array.AsReadOnly(_undoHistory.Select(Frame).ToArray()),
            Redo = Array.AsReadOnly(_redoHistory.Select(Frame).ToArray()),
            DiscardedEarlierEntries = _discardedEarlierHistoryEntries
        };
    }

    private void TrimHistory()
    {
        long bytes = _undoHistory.Sum(frame => (long)frame.Length) + _redoHistory.Sum(frame => (long)frame.Length);
        while (_undoHistory.Count + _redoHistory.Count > ProductivitySnapshotHistory.MaximumEntries ||
            bytes > ProductivitySnapshotHistory.MaximumPayloadBytes)
        {
            var source = _undoHistory.Count > 0 ? _undoHistory : _redoHistory;
            bytes -= source[0].Length;
            source.RemoveAt(0);
            if (_discardedEarlierHistoryEntries < long.MaxValue) _discardedEarlierHistoryEntries++;
        }
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

    public CanvasApiResult<CanvasLayerListResult> ListLayers(Guid pageId, int offset = 0, int pageSize = 100)
    {
        lock (_gate)
        {
            if (offset < 0 || pageSize is < 1 or > MaximumPageSize)
                return Failure<CanvasLayerListResult>(CanvasApiErrorCode.InvalidArgument, "Layers.List", "Offset and page size are outside the supported range.", pageId);
            var page = _artifact.Pages.FirstOrDefault(candidate => candidate.PageId == pageId);
            if (page is null)
                return Failure<CanvasLayerListResult>(CanvasApiErrorCode.NotFound, "Layers.List", "Canvas page was not found.", pageId);

            var items = page.LayerOrder.Skip(offset).Take(pageSize)
                .Select(id => page.Layers.First(layer => layer.LayerId == id))
                .Select(CloneLayerForRead).ToArray();
            var nextOffset = offset + items.Length;
            return CanvasApiResult<CanvasLayerListResult>.Success(new CanvasLayerListResult(
                items, nextOffset < page.LayerOrder.Count ? nextOffset : null, page.LayerOrder.Count));
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

    public CanvasApiResult<CanvasObject> GetObject(Guid pageId, Guid objectId)
    {
        lock (_gate)
        {
            var page = _artifact.Pages.FirstOrDefault(candidate => candidate.PageId == pageId);
            if (page is null)
                return Failure<CanvasObject>(CanvasApiErrorCode.NotFound, "Objects.Get", "Canvas page was not found.", pageId);
            var item = page.Objects.FirstOrDefault(candidate => candidate.ObjectId == objectId);
            return item is null
                ? Failure<CanvasObject>(CanvasApiErrorCode.NotFound, "Objects.Get", "Canvas object was not found.", objectId)
                : CanvasApiResult<CanvasObject>.Success(CloneObject(item));
        }
    }

    public CanvasApiResult<CanvasObjectListResult> ListObjects(Guid pageId, int offset = 0, int pageSize = 100)
    {
        lock (_gate)
        {
            if (offset < 0 || pageSize is < 1 or > MaximumPageSize)
                return Failure<CanvasObjectListResult>(CanvasApiErrorCode.InvalidArgument, "Objects.List", "Offset and page size are outside the supported range.", pageId);
            var page = _artifact.Pages.FirstOrDefault(candidate => candidate.PageId == pageId);
            if (page is null)
                return Failure<CanvasObjectListResult>(CanvasApiErrorCode.NotFound, "Objects.List", "Canvas page was not found.", pageId);

            var items = page.ObjectOrder.Skip(offset).Take(pageSize)
                .Select(id => page.Objects.First(item => item.ObjectId == id))
                .Select(CloneObject).ToArray();
            var nextOffset = offset + items.Length;
            return CanvasApiResult<CanvasObjectListResult>.Success(new CanvasObjectListResult(
                items, nextOffset < page.ObjectOrder.Count ? nextOffset : null, page.ObjectOrder.Count));
        }
    }

    /// <summary>Searches typed object/accessibility/reference metadata within one page.
    /// SharedPayload is never inspected; the result explicitly reports metadata-only coverage.</summary>
    public CanvasApiResult<CanvasObjectSearchResult> SearchObjects(CanvasObjectSearchQuery query)
    {
        const string action = "Objects.Search";
        if (query is null || query.PageId == Guid.Empty || query.Offset < 0
            || query.PageSize is < 1 or > MaximumPageSize
            || query.Text is not null && string.IsNullOrWhiteSpace(query.Text)
            || query.ObjectTypeId is not null && string.IsNullOrWhiteSpace(query.ObjectTypeId)
            || query.Text is null && query.ObjectTypeId is null && query.LayerId is null)
            return Failure<CanvasObjectSearchResult>(CanvasApiErrorCode.InvalidArgument, action,
                "Search requires a page, a bounded page size, and text or a typed object/layer filter.", query?.PageId);

        lock (_gate)
        {
            var page = _artifact.Pages.FirstOrDefault(candidate => candidate.PageId == query.PageId);
            if (page is null)
                return Failure<CanvasObjectSearchResult>(CanvasApiErrorCode.NotFound, action, "Canvas page was not found.", query.PageId);

            var text = query.Text?.Trim();
            bool Contains(string? candidate) => text is not null
                && candidate?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
            var objectsById = page.Objects.ToDictionary(item => item.ObjectId);
            var items = new List<CanvasObjectSearchHit>(query.PageSize);
            var totalCount = 0;
            foreach (var objectId in page.ObjectOrder)
            {
                var item = objectsById[objectId];
                if (query.ObjectTypeId is not null && !string.Equals(item.ObjectTypeId, query.ObjectTypeId, StringComparison.Ordinal)
                    || query.LayerId is { } layerId && item.LayerId != layerId)
                    continue;
                if (text is not null
                    && !Contains(item.ObjectTypeId)
                    && !Contains(item.Accessibility.Name)
                    && !Contains(item.Accessibility.Description)
                    && !Contains(item.Accessibility.AltText)
                    && !Contains(item.SharedObjectRef?.OwnerAppId)
                    && !Contains(item.SharedObjectRef?.ObjectTypeId))
                    continue;

                if (totalCount >= query.Offset && items.Count < query.PageSize)
                {
                    var detached = CloneObject(item);
                    var layer = page.Layers.First(candidate => candidate.LayerId == item.LayerId);
                    items.Add(new CanvasObjectSearchHit(detached.ObjectId, detached.ObjectTypeId,
                        detached.LayerId, layer.IsVisible, layer.IsLocked, detached.Geometry,
                        detached.Accessibility.Name, detached.Accessibility.Description,
                        detached.Accessibility.AltText, detached.SharedObjectRef, detached.RevisionId));
                }
                totalCount++;
            }

            var nextOffset = query.Offset + items.Count;
            var unsearchedPayloadObjectCount = page.Objects.Count(item => item.SharedPayload is not null);
            return CanvasApiResult<CanvasObjectSearchResult>.Success(new CanvasObjectSearchResult(
                items, nextOffset < totalCount ? nextOffset : null, totalCount,
                CanvasObjectSearchCoverage.AccessibilityAndReferenceMetadataOnly,
                unsearchedPayloadObjectCount));
        }
    }

    public CanvasApiResult<CanvasStrokeListResult> ListStrokes(Guid pageId, int offset = 0, int pageSize = 100)
    {
        lock (_gate)
        {
            if (offset < 0 || pageSize is < 1 or > MaximumPageSize)
                return Failure<CanvasStrokeListResult>(CanvasApiErrorCode.InvalidArgument, "Ink.List", "Offset and page size are outside the supported range.", pageId);
            var page = _artifact.Pages.FirstOrDefault(candidate => candidate.PageId == pageId);
            if (page is null)
                return Failure<CanvasStrokeListResult>(CanvasApiErrorCode.NotFound, "Ink.List", "Canvas page was not found.", pageId);

            var items = page.StrokeOrder.Skip(offset).Take(pageSize)
                .Select(id => page.Strokes.First(stroke => stroke.StrokeId == id))
                .Select(CloneStrokeForRead).ToArray();
            var nextOffset = offset + items.Length;
            return CanvasApiResult<CanvasStrokeListResult>.Success(new CanvasStrokeListResult(
                items, nextOffset < page.StrokeOrder.Count ? nextOffset : null, page.StrokeOrder.Count));
        }
    }

    public CanvasApiResult<CanvasInkStroke> GetStroke(Guid pageId, Guid strokeId)
    {
        lock (_gate)
        {
            var page = _artifact.Pages.FirstOrDefault(candidate => candidate.PageId == pageId);
            if (page is null)
                return Failure<CanvasInkStroke>(CanvasApiErrorCode.NotFound, "Ink.Get", "Canvas page was not found.", pageId);
            var stroke = page.Strokes.FirstOrDefault(candidate => candidate.StrokeId == strokeId);
            return stroke is null
                ? Failure<CanvasInkStroke>(CanvasApiErrorCode.NotFound, "Ink.Get", "Canvas stroke was not found.", strokeId)
                : CanvasApiResult<CanvasInkStroke>.Success(CloneStrokeForRead(stroke));
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
            if (bounds is null || !ValidBounds(bounds))
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Page bounds must contain a positive finite size and orientation.");
            var page = artifact.Pages[index];
            if (page.Bounds == bounds) return MutationNoChange();
            artifact.Pages[index] = page with { Bounds = bounds, RevisionId = Guid.NewGuid() };
            return MutationChanged(page.PageId);
        });

    public CanvasApiResult<CanvasMutationResult> SetPageBackground(
        CanvasMutationRequest request,
        Guid pageId,
        CanvasBackgroundDefinition background)
    {
        if (background is null)
            return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, "Pages.SetBackground", "Page background is required.", pageId);
        CanvasBackgroundDefinition detached;
        try
        {
            detached = JsonSerializer.Deserialize<CanvasBackgroundDefinition>(JsonSerializer.SerializeToUtf8Bytes(background))
                ?? throw new JsonException("Page background could not be detached.");
        }
        catch (JsonException)
        { return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, "Pages.SetBackground", "Page background could not be detached.", pageId); }

        return Mutate(request, "Pages.SetBackground", pageId, detached, artifact =>
        {
            var index = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[index];
            if (JsonSerializer.Serialize(page.Background) == JsonSerializer.Serialize(detached))
                return MutationNoChange();
            artifact.Pages[index] = page with { Background = detached, RevisionId = Guid.NewGuid() };
            return MutationChanged(page.PageId);
        });
    }

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
            if (!LayerAllowsEditing(page.Layers.First(layer => layer.LayerId == item.LayerId)))
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects on hidden or locked layers cannot be moved.");
            var nextTransform = transform ?? item.Transform;
            if (item.Geometry == geometry && item.Transform == nextTransform) return MutationNoChange();
            var objects = page.Objects.ToList();
            objects[objectIndex] = item with
            {
                Geometry = geometry,
                Transform = nextTransform,
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
            if (!LayerAllowsEditing(page.Layers.First(layer => layer.LayerId == item.LayerId))
                || !LayerAllowsEditing(destination))
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects cannot be moved from hidden or locked layers or into hidden or locked layers.");
            if (item.LayerId == destinationLayerId) return MutationNoChange();
            var objects = page.Objects.ToList();
            objects[objectIndex] = item with { LayerId = destinationLayerId, RevisionId = Guid.NewGuid() };
            artifact.Pages[pageIndex] = page with { Objects = objects, RevisionId = Guid.NewGuid() };
            return MutationChanged(pageId, objectId, destinationLayerId);
        });

    public CanvasApiResult<CanvasMutationResult> AddStructuredStroke(
        CanvasMutationRequest request,
        Guid pageId,
        CanvasInkStroke stroke,
        Func<CanvasDocumentSettings>? captureAccompanyingDocumentSettings = null) =>
        Mutate(request, "Ink.CreateFromSamples", stroke?.StrokeId, new { pageId, stroke }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            if (stroke is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Structured stroke is required.");
            var page = artifact.Pages[pageIndex];
            if (!page.Layers.Any(layer => layer.LayerId == stroke.LayerId))
                return MutationFailure(CanvasApiErrorCode.NotFound, "Stroke layer was not found on the target page.");
            if (!LayerAllowsEditing(page.Layers.First(layer => layer.LayerId == stroke.LayerId)))
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Ink cannot be added to a hidden or locked layer.");
            if (page.Strokes.Any(item => item.StrokeId == stroke.StrokeId))
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Stroke ID already exists on the page.");
            CanvasInkStroke clonedStroke;
            try { clonedStroke = CloneStroke(stroke); }
            catch (CanvasArtifactFormatException)
            { return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Structured stroke failed semantic validation."); }
            var candidate = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(artifact));
            if (clonedStroke.PathGeometry is not null) candidate.SchemaVersion = 2;
            candidate.Pages[pageIndex] = page with
            {
                Strokes = page.Strokes.Append(clonedStroke).ToList(),
                StrokeOrder = page.StrokeOrder.Append(clonedStroke.StrokeId).ToList(),
                RevisionId = Guid.NewGuid()
            };
            try { CanvasArtifactCodec.SerializeSnapshot(candidate); }
            catch (Exception exception) when (exception is CanvasArtifactFormatException or JsonException)
            { return MutationFailure(CanvasApiErrorCode.InvalidArgument, "The complete stroke candidate failed validation before donor preparation."); }
            var settings = captureAccompanyingDocumentSettings?.Invoke();
            if (captureAccompanyingDocumentSettings is not null && settings is null)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Accompanying document settings are required.");
            if (settings is not null)
                candidate.DocumentSettings = JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            artifact.SchemaVersion = candidate.SchemaVersion;
            artifact.Pages[pageIndex] = candidate.Pages[pageIndex];
            artifact.DocumentSettings = candidate.DocumentSettings;
            return MutationChanged(pageId, stroke.StrokeId);
        });

    public CanvasApiResult<CanvasMutationResult> DeleteStructuredStroke(
        CanvasMutationRequest request, Guid pageId, Guid strokeId,
        Func<CanvasDocumentSettings>? captureAccompanyingDocumentSettings = null) =>
        MutateStructuredStroke(request, "Ink.Delete", pageId, strokeId, null,
            _ => null, captureAccompanyingDocumentSettings);

    public CanvasApiResult<CanvasMutationResult> TranslateStructuredStroke(
        CanvasMutationRequest request, Guid pageId, Guid strokeId, double deltaX, double deltaY,
        Func<CanvasDocumentSettings>? captureAccompanyingDocumentSettings = null) =>
        MutateStructuredStroke(request, "Ink.Translate", pageId, strokeId, new { deltaX, deltaY },
            stroke => stroke with
            {
                RevisionId = Guid.NewGuid(),
                Samples = stroke.Samples.Select(sample => sample with
                    { X = sample.X + deltaX, Y = sample.Y + deltaY }).ToList()
            }, captureAccompanyingDocumentSettings,
            double.IsFinite(deltaX) && double.IsFinite(deltaY), deltaX == 0 && deltaY == 0);

    private CanvasApiResult<CanvasMutationResult> MutateStructuredStroke(
        CanvasMutationRequest request, string action, Guid pageId, Guid strokeId, object? input,
        Func<CanvasInkStroke, CanvasInkStroke?> edit,
        Func<CanvasDocumentSettings>? captureAccompanyingDocumentSettings,
        bool validInput = true, bool noChange = false) =>
        Mutate(request, action, strokeId, new { pageId, input = validInput ? input : null }, artifact =>
        {
            if (!validInput)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Stroke translation must be finite.");
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[pageIndex];
            var strokeIndex = page.Strokes.FindIndex(stroke => stroke.StrokeId == strokeId);
            if (strokeIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas stroke was not found.");
            var stroke = page.Strokes[strokeIndex];
            if (!LayerAllowsEditing(page.Layers.First(layer => layer.LayerId == stroke.LayerId)))
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Ink on a hidden or locked layer cannot be edited.");
            if (noChange) return MutationNoChange();
            if (action == "Ink.Translate" && stroke.PathGeometry is not null)
                return MutationFailure(CanvasApiErrorCode.UnsupportedFeature, "Authoritative donor paths require an exact owner path replacement; original Samples are provenance.");
            CanvasInkStroke? replacement;
            try
            {
                replacement = edit(stroke);
                if (replacement is not null) replacement = CloneStroke(replacement);
            }
            catch (CanvasArtifactFormatException)
            { return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Edited stroke failed semantic validation."); }
            // No donor callback before revision, replay, lock and sample checks.
            // The returned snapshot shares this mutation's revision/history boundary.
            var settings = captureAccompanyingDocumentSettings?.Invoke();
            if (captureAccompanyingDocumentSettings is not null && settings is null)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Accompanying document settings are required.");
            var capturedSettings = settings is null ? null
                : JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            var strokes = page.Strokes.ToList();
            if (replacement is null) strokes.RemoveAt(strokeIndex);
            else strokes[strokeIndex] = replacement;
            artifact.Pages[pageIndex] = page with
            {
                Strokes = strokes,
                StrokeOrder = replacement is null ? page.StrokeOrder.Where(id => id != strokeId).ToList() : page.StrokeOrder,
                RevisionId = Guid.NewGuid()
            };
            if (capturedSettings is not null) artifact.DocumentSettings = capturedSettings;
            return MutationChanged(pageId, strokeId);
        });

    public CanvasApiResult<CanvasMutationResult> UpdateDocumentSettings(CanvasMutationRequest request, CanvasDocumentSettings settings) =>
        Mutate(request, "DocumentSettings.Update", _artifact.ArtifactId, settings, artifact =>
        {
            if (settings is null || settings.Properties is null)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Document settings are required.");
            var detached = JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            if (JsonSerializer.Serialize(artifact.DocumentSettings) == JsonSerializer.Serialize(detached))
                return MutationNoChange();
            artifact.DocumentSettings = detached;
            return MutationChanged(artifact.ArtifactId);
        });

    /// <summary>Optional owning donor preparation runs against a detached proposed restored snapshot,
    /// before canonical state/history changes. It conveys no permission and never runs on replay.</summary>
    public CanvasApiResult<CanvasMutationResult> Undo(CanvasMutationRequest request, Action<CanvasArtifact>? prepareRestoredSnapshot = null) =>
        MutateHistory(request, "History.Undo", undo: true, prepareRestoredSnapshot);

    public CanvasApiResult<CanvasMutationResult> Redo(CanvasMutationRequest request, Action<CanvasArtifact>? prepareRestoredSnapshot = null) =>
        MutateHistory(request, "History.Redo", undo: false, prepareRestoredSnapshot);

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
                : [$"{crossingObjects.Count} object(s) and {crossingStrokes.Count} stroke(s) may extend beyond the proposed page bounds (conservative geometry check); their stored geometry will be preserved."];
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
            // Retention may remove early entries, so cursors refer to the lifetime sequence,
            // never the current list index. An expired cursor requires a fresh snapshot.
            var firstRetainedCursor = _eventCursor - _events.Count;
            if (afterCursor < firstRetainedCursor)
                return Failure<CanvasChangePage>(CanvasApiErrorCode.RevisionConflict, "Events.GetChanges", "Change cursor expired; refresh the Canvas snapshot.", canRetry: true);
            if (afterCursor > _eventCursor)
                return Failure<CanvasChangePage>(CanvasApiErrorCode.InvalidArgument, "Events.GetChanges", "Change cursor is ahead of this Canvas session.");
            var offset = checked((int)(afterCursor - firstRetainedCursor));
            var matching = _events.Skip(offset).Take(pageSize).ToArray();
            var nextCursor = afterCursor + matching.Length;
            var hasMore = nextCursor < _eventCursor;
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
            if (_applyingMutation) return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "A preparation callback cannot mutate the same canonical session.", targetId);
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

            var before = CanvasArtifactCodec.SerializeSnapshot(_artifact);
            MutationPlan plan;
            try
            {
                _applyingMutation = true;
                plan = apply(_artifact);
            }
            catch
            {
                _artifact = CanvasArtifactCodec.Deserialize(before);
                throw;
            }
            finally { _applyingMutation = false; }
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
            _redoHistory.Clear();
            TrimHistory();
            var mutation = new CanvasMutationResult(_artifact.RevisionId, plan.ChangedIds.Distinct().ToArray(), plan.Warnings);
            CacheOperation(request.OperationId, fingerprint, mutation);
            change = AddEvent(request, action, mutation);
            result = CanvasApiResult<CanvasMutationResult>.Success(mutation);
        }
        return result;
    }

    private CanvasApiResult<CanvasMutationResult> MutateHistory(CanvasMutationRequest request, string action, bool undo, Action<CanvasArtifact>? prepareRestoredSnapshot)
    {
        CanvasApiResult<CanvasMutationResult> result;
        lock (_gate)
        {
            if (_applyingMutation) return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "A preparation callback cannot mutate the same canonical session.", _artifact.ArtifactId);
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

            var currentEntityIds = EnumerateEntityIds(_artifact).ToHashSet();
            var restored = CanvasArtifactCodec.Deserialize(source[^1]);
            restored.RevisionId = Guid.NewGuid();
            try
            {
                _applyingMutation = true;
                prepareRestoredSnapshot?.Invoke(Clone(restored));
            }
            finally { _applyingMutation = false; }
            destination.Add(CanvasArtifactCodec.SerializeSnapshot(_artifact));
            source.RemoveAt(source.Count - 1);
            TrimHistory();
            _artifact = restored;
            // History changes can remove entities as well as restore them. Report the
            // union so live selections/consumers can reconcile exact canonical IDs.
            var changedIds = currentEntityIds.Concat(EnumerateEntityIds(_artifact))
                .Distinct().ToArray();
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

    private static CanvasObject CloneObject(CanvasObject item) =>
        JsonSerializer.Deserialize<CanvasObject>(JsonSerializer.Serialize(item))
        ?? throw new CanvasArtifactFormatException(CanvasArtifactFormatErrorCode.InvalidDocument,
            "Canvas object could not be cloned for a bounded read.");

    private static CanvasLayer CloneLayerForRead(CanvasLayer layer) =>
        JsonSerializer.Deserialize<CanvasLayer>(JsonSerializer.Serialize(layer))
        ?? throw new CanvasArtifactFormatException(CanvasArtifactFormatErrorCode.InvalidDocument,
            "Canvas layer could not be cloned for a bounded read.");

    private static CanvasInkStroke CloneStrokeForRead(CanvasInkStroke stroke) =>
        JsonSerializer.Deserialize<CanvasInkStroke>(JsonSerializer.Serialize(stroke))
        ?? throw new CanvasArtifactFormatException(CanvasArtifactFormatErrorCode.InvalidDocument,
            "Canvas stroke could not be cloned for a bounded read.");

    private static CanvasPage ClonePage(CanvasPage page)
    {
        var artifact = CanvasArtifact.Create(mode: page.Bounds is null ? CanvasDocumentMode.Infinite : CanvasDocumentMode.Paged);
        artifact.PageOrder = [page.PageId];
        artifact.Pages = [page];
        return CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(artifact)).Pages[0];
    }

    private static CanvasInkStroke CloneStroke(CanvasInkStroke stroke)
    {
        var layer = new CanvasLayer { LayerId = stroke.LayerId };
        var page = new CanvasPage { Layers = [layer], LayerOrder = [layer.LayerId], Strokes = [stroke], StrokeOrder = [stroke.StrokeId] };
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
        if (stroke.PathGeometry is { } path)
        {
            // Affine-transformed Bezier control hull is conservative centerline containment,
            // not an exact painted brush/hitbox claim. Samples never render a fragment polyline.
            var radians = (stroke.Transform.RotationDegrees % 360) * Math.PI / 180;
            var cos = Math.Cos(radians); var sin = Math.Sin(radians);
            bool Inside(double x, double y)
            {
                var sx = x * stroke.Transform.ScaleX; var sy = y * stroke.Transform.ScaleY;
                var tx = sx * cos - sy * sin + stroke.Transform.TranslateX;
                var ty = sx * sin + sy * cos + stroke.Transform.TranslateY;
                return double.IsFinite(tx) && double.IsFinite(ty) && tx >= bounds.X && tx <= bounds.X + bounds.Width
                    && ty >= bounds.Y && ty <= bounds.Y + bounds.Height;
            }
            if (!Inside(path.Start.X, path.Start.Y)) return false;
            foreach (var segment in path.Segments)
                if (!Inside(segment.End.X, segment.End.Y)
                    || segment.Control1 is { } first && !Inside(first.X, first.Y)
                    || segment.Control2 is { } second && !Inside(second.X, second.Y)) return false;
            return true;
        }
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

    private static bool LayerAllowsEditing(CanvasLayer layer) => layer.IsVisible && !layer.IsLocked;

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
    private sealed record IdempotencyEntry(string Fingerprint, CanvasMutationResult Result, int? StructuredStrokeOrderCount = null);
}
