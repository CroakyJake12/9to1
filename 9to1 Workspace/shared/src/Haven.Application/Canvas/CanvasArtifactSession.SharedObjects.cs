using System.Text.Json;

namespace Haven.Application;

// Additive source-owned shared graph transactions. Storage/Home consent remains
// with the existing artifact owner; CanvasActorContext is request metadata only.
public sealed partial class CanvasArtifactSession
{
    private const int MaximumSharedObjectTransaction = 512;
    private const int MaximumSharedObjectTransactionBytes = 16 * 1024 * 1024;

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
                if (layer.IsLocked) return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects cannot be inserted on a locked layer.");
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
            if (page.Layers.Single(layer => layer.LayerId == original.LayerId).IsLocked)
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Objects on a locked layer cannot be edited.");
            if (original.LayerId != detached.LayerId || original.ObjectTypeId != detached.ObjectTypeId)
                return MutationFailure(CanvasApiErrorCode.UnsupportedFeature, "Use the owning layer-transfer or explicit conversion action to change placement family.");
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
}
