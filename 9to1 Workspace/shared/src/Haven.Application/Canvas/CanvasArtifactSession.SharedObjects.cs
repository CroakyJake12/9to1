using System.Text.Json;

namespace Haven.Application;

// Additive source-owned shared graph transactions. Storage/Home consent remains
// with the existing artifact owner; CanvasActorContext is request metadata only.
public sealed partial class CanvasArtifactSession
{
    private const int MaximumSharedObjectTransaction = 512;
    private const int MaximumSharedObjectTransactionBytes = 16 * 1024 * 1024;
    private const int MaximumSelectedObjectTranslation = 512;

    /// <summary>Translates one canonical selected set in one revision/history transaction.
    /// All members are validated before any geometry is changed; referenced payload ownership is preserved.</summary>
    public CanvasApiResult<CanvasSelectedSetTranslationResult> TranslateSelectedObjects(
        CanvasMutationRequest request, Guid pageId, IReadOnlyList<Guid> objectIds, double deltaX, double deltaY)
    {
        const string action = "Objects.TranslateSelection";
        if (pageId == Guid.Empty || !double.IsFinite(deltaX) || !double.IsFinite(deltaY)
            || !CaptureBounded(objectIds, MaximumSelectedObjectTranslation, out var capturedIds)
            || capturedIds.Count < 2 || capturedIds.Any(id => id == Guid.Empty)
            || capturedIds.Distinct().Count() != capturedIds.Count)
            return Failure<CanvasSelectedSetTranslationResult>(CanvasApiErrorCode.InvalidArgument, action,
                "Selected-object translation requires at least two unique object IDs and finite deltas.", pageId);

        var stableIds = capturedIds.ToArray();
        var mutation = Mutate(request, action, pageId,
            new { pageId, objectIds = stableIds, deltaX, deltaY }, artifact =>
            {
                var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
                if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
                var page = artifact.Pages[pageIndex];
                var objectsById = page.Objects.ToDictionary(item => item.ObjectId);
                var selected = new List<CanvasObject>(stableIds.Length);
                foreach (var objectId in stableIds)
                {
                    if (!objectsById.TryGetValue(objectId, out var item))
                        return MutationFailure(CanvasApiErrorCode.NotFound, "A selected Canvas object was not found.");
                    selected.Add(item);
                }

                // Check the complete set before preparing replacement objects, so a single
                // hidden/locked member denies the whole action without a partial move.
                foreach (var item in selected)
                {
                    var layer = page.Layers.FirstOrDefault(candidate => candidate.LayerId == item.LayerId);
                    if (layer is null)
                        return MutationFailure(CanvasApiErrorCode.NotFound, "A selected object's layer was not found.");
                    if (!LayerAllowsEditing(layer))
                        return MutationFailure(CanvasApiErrorCode.PermissionDenied,
                            "Selected objects on hidden or locked layers cannot be translated.");
                }

                if (deltaX == 0 && deltaY == 0) return MutationNoChange();
                var translated = new Dictionary<Guid, CanvasObject>(selected.Count);
                foreach (var item in selected)
                {
                    var geometry = item.Geometry with { X = item.Geometry.X + deltaX, Y = item.Geometry.Y + deltaY };
                    translated.Add(item.ObjectId, item with
                    {
                        Geometry = geometry,
                        RevisionId = Guid.NewGuid()
                    });
                }

                var objects = page.Objects.Select(item => translated.TryGetValue(item.ObjectId, out var replacement)
                    ? replacement : item).ToList();
                artifact.Pages[pageIndex] = page with { Objects = objects, RevisionId = Guid.NewGuid() };
                return MutationChanged([pageId, .. stableIds]);
            });

        if (!mutation.IsSuccess) return CanvasApiResult<CanvasSelectedSetTranslationResult>.Failure(mutation.Error!);
        var changed = mutation.Value!.ChangedIds.Where(id => id != pageId).ToArray();
        return CanvasApiResult<CanvasSelectedSetTranslationResult>.Success(new(
            mutation.Value.RevisionId, changed, deltaX, deltaY, mutation.Value.Warnings));
    }

    public CanvasApiResult<CanvasMutationResult> InsertSharedObjects(CanvasMutationRequest request, Guid pageId,
        IReadOnlyList<CanvasObject> objects)
    {
        const string action = "Objects.InsertShared";
        if (!CaptureBounded(objects, MaximumSharedObjectTransaction, out var supplied) || supplied.Count == 0)
            return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Shared object insertion requires a bounded nonempty set.", pageId);
        var detached = new List<CanvasObject>();
        long bytes = 0;
        try
        {
            foreach (var suppliedObject in supplied)
            {
                if (suppliedObject is null) throw new InvalidDataException("Shared object is missing.");
                var payload = JsonSerializer.SerializeToUtf8Bytes(suppliedObject);
                bytes = checked(bytes + payload.Length);
                if (bytes > MaximumSharedObjectTransactionBytes) throw new InvalidDataException("Shared object insertion exceeds its payload bound.");
                detached.Add(JsonSerializer.Deserialize<CanvasObject>(payload) ?? throw new InvalidDataException("Shared object is missing."));
            }
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or OverflowException)
        { return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Shared object proposals failed detached validation.", pageId); }
        return Mutate(request, action, pageId, new { pageId, objects = detached }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "The canonical page is unavailable.");
            var page = artifact.Pages[pageIndex];
            var identities = EnumerateEntityIds(artifact).ToHashSet();
            foreach (var item in detached)
            {
                if (item.ObjectId == Guid.Empty || !identities.Add(item.ObjectId))
                    return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Inserted objects need distinct new canonical identities.");
                var layer = page.Layers.FirstOrDefault(layer => layer.LayerId == item.LayerId);
                if (layer is null) return MutationFailure(CanvasApiErrorCode.NotFound, "The insertion layer is unavailable.");
                if (!LayerAllowsEditing(layer)) return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects cannot be inserted on a hidden or locked layer.");
                if (string.IsNullOrWhiteSpace(item.ObjectTypeId) || item.SharedPayload is not { ValueKind: JsonValueKind.Object })
                    return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Shared insertion requires an explicit canonical object type and retained payload.");
            }
            artifact.Pages[pageIndex] = page with { Objects = page.Objects.Concat(detached).ToList(),
                ObjectOrder = page.ObjectOrder.Concat(detached.Select(item => item.ObjectId)).ToList(), RevisionId = Guid.NewGuid() };
            return MutationChanged([pageId, .. detached.Select(item => item.ObjectId)]);
        });
    }

    public CanvasApiResult<CanvasMutationResult> UpdateSharedObject(CanvasMutationRequest request, Guid pageId,
        CanvasObject proposed)
    {
        const string action = "Objects.UpdateShared";
        if (proposed is null) return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "The proposed shared object is unavailable.", pageId);
        CanvasObject detached;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(proposed);
            if (bytes.Length > MaximumSharedObjectTransactionBytes) throw new InvalidDataException("Shared object update exceeds its payload bound.");
            detached = JsonSerializer.Deserialize<CanvasObject>(bytes) ?? throw new InvalidDataException("Shared object is missing.");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        { return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Shared object update failed detached validation.", proposed.ObjectId); }
        return Mutate(request, action, proposed.ObjectId, new { pageId, proposed = detached }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "The canonical page is unavailable.");
            var page = artifact.Pages[pageIndex];
            var objectIndex = page.Objects.FindIndex(item => item.ObjectId == detached.ObjectId);
            if (objectIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "The canonical shared object is unavailable.");
            var original = page.Objects[objectIndex];
            if (!LayerAllowsEditing(page.Layers.Single(layer => layer.LayerId == original.LayerId)))
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects on a hidden or locked layer cannot be edited.");
            if (original.LayerId != detached.LayerId || original.ObjectTypeId != detached.ObjectTypeId)
                return MutationFailure(CanvasApiErrorCode.UnsupportedFeature, "Use the owning layer-transfer or explicit conversion action to change placement family.");
            if (original.SharedObjectRef != detached.SharedObjectRef)
                return MutationFailure(CanvasApiErrorCode.UnsupportedFeature, "Use an explicit owning-app reference action to retarget a shared artifact.");
            if (original.SharedObjectRef is not null
                && JsonSerializer.Serialize(original.SharedPayload) != JsonSerializer.Serialize(detached.SharedPayload))
                return MutationFailure(CanvasApiErrorCode.UnsupportedFeature, "Edit referenced shared content through its owning-app actions.");
            if (detached.SharedPayload is not { ValueKind: JsonValueKind.Object })
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "The updated shared payload is unavailable.");
            if (JsonSerializer.Serialize(original with { RevisionId = Guid.Empty }) == JsonSerializer.Serialize(detached with { RevisionId = Guid.Empty }))
                return MutationNoChange();
            var objects = page.Objects.ToList();
            objects[objectIndex] = detached with { RevisionId = Guid.NewGuid() };
            artifact.Pages[pageIndex] = page with { Objects = objects, RevisionId = Guid.NewGuid() };
            return MutationChanged(pageId, detached.ObjectId);
        });
    }

    /// <summary>Removes the Canvas-owned spatial object only. A SharedObjectRef's
    /// owning artifact remains untouched and may be removed only by its owner API.</summary>
    public CanvasApiResult<CanvasMutationResult> DeleteObject(
        CanvasMutationRequest request, Guid pageId, Guid objectId) =>
        Mutate(request, "Objects.Delete", objectId, new { pageId, objectId }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "The canonical page is unavailable.");
            var page = artifact.Pages[pageIndex];
            var objectIndex = page.Objects.FindIndex(item => item.ObjectId == objectId);
            if (objectIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "The canonical shared object is unavailable.");
            var item = page.Objects[objectIndex];
            if (!LayerAllowsEditing(page.Layers.Single(layer => layer.LayerId == item.LayerId)))
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects on hidden or locked layers cannot be deleted.");
            var objects = page.Objects.ToList();
            objects.RemoveAt(objectIndex);
            artifact.Pages[pageIndex] = page with
            {
                Objects = objects,
                ObjectOrder = page.ObjectOrder.Where(id => id != objectId).ToList(),
                RevisionId = Guid.NewGuid()
            };
            return MutationChanged(pageId, objectId);
        });

    /// <summary>Creates a new Canvas placement while retaining the source shared
    /// object reference and payload. The caller supplies its Canvas-owned geometry.</summary>
    public CanvasApiResult<CanvasMutationResult> DuplicateObject(
        CanvasMutationRequest request, Guid pageId, Guid objectId, CanvasRect geometry) =>
        Mutate(request, "Objects.Duplicate", objectId, new { pageId, geometry }, artifact =>
        {
            var pageIndex = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (pageIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "The canonical page is unavailable.");
            if (geometry is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Duplicate placement geometry is required.");
            var page = artifact.Pages[pageIndex];
            var objectIndex = page.Objects.FindIndex(item => item.ObjectId == objectId);
            if (objectIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "The canonical shared object is unavailable.");
            var source = page.Objects[objectIndex];
            if (!LayerAllowsEditing(page.Layers.Single(layer => layer.LayerId == source.LayerId)))
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects on hidden or locked layers cannot be duplicated.");
            var identities = EnumerateEntityIds(artifact).ToHashSet();
            Guid duplicateId;
            do duplicateId = Guid.NewGuid(); while (!identities.Add(duplicateId));
            var duplicate = source with { ObjectId = duplicateId, RevisionId = Guid.NewGuid(), Geometry = geometry };
            artifact.Pages[pageIndex] = page with
            {
                Objects = page.Objects.Append(duplicate).ToList(),
                ObjectOrder = page.ObjectOrder.Append(duplicateId).ToList(),
                RevisionId = Guid.NewGuid()
            };
            return MutationChanged(pageId, duplicateId);
        });
}
