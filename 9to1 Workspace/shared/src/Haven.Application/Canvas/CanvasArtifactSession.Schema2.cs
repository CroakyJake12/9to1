using System.Text.Json;
using Haven.Application.Canvas;

namespace Haven.Application;

public sealed partial class CanvasArtifactSession
{
    private const int MaximumStrokeReplacements = CanvasStructuredStrokeTransactionLimits.MaximumEntriesPerSet;
    private const int MaximumDetachedStrokePoints = CanvasStructuredStrokeTransactionLimits.MaximumAggregateSampleAndSegmentEntries;

    /// <summary>One canonical stroke-set/order/history transaction. Caller proposals are detached
    /// before fingerprinting; the genuine donor settings callback cannot run on denied/no-op/replay.</summary>
    public CanvasApiResult<CanvasMutationResult> ReplaceStructuredStrokes(
        CanvasMutationRequest request, Guid pageId, IReadOnlyList<CanvasInkStroke> replacements,
        IReadOnlyList<Guid> removedStrokeIds, IReadOnlyList<Guid> fullStrokeOrder,
        Func<CanvasDocumentSettings>? captureAccompanyingDocumentSettings = null)
    {
        const string action = "Ink.ReplaceStructured";
        lock (_gate)
        {
            var error = ValidateRequest(request, action, pageId);
            if (error is not null) return CanvasApiResult<CanvasMutationResult>.Failure(error);
            if (_applyingMutation) return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action,
                "A preparation callback cannot mutate the same canonical session.", pageId);
            if (!CaptureBounded(replacements, MaximumStrokeReplacements, out var supplied)
                || !CaptureBounded(removedStrokeIds, MaximumStrokeReplacements, out var removed))
                return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Stroke replacement sets are oversized or unavailable.", pageId);
            var detached = new List<CanvasInkStroke>();
            var pointCount = 0;
            try
            {
                foreach (var stroke in supplied)
                {
                    if (stroke is null || stroke.Samples is null || stroke.Samples.Count > MaximumDetachedStrokePoints)
                        return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Stroke provenance is unavailable or oversized.", pageId);
                    CanvasStrokePathGeometry? geometry = null;
                    if (stroke.PathGeometry is not null && !CanvasStrokePathCapture.TryCapture(stroke.PathGeometry, out geometry))
                        return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Authoritative path geometry is invalid.", pageId);
                    pointCount = checked(pointCount + stroke.Samples.Count + (geometry?.Segments.Count ?? 0));
                    if (pointCount > MaximumDetachedStrokePoints)
                        return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Detached stroke payload exceeds the bounded transaction budget.", pageId);
                    detached.Add(CloneStroke(stroke with { PathGeometry = geometry }));
                }
            }
            catch (Exception exception) when (exception is CanvasArtifactFormatException or JsonException or InvalidOperationException or OverflowException)
            { return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Stroke proposals failed detached validation.", pageId); }
            if (detached.Select(s => s.StrokeId).Distinct().Count() != detached.Count
                || removed.Any(id => id == Guid.Empty) || removed.Distinct().Count() != removed.Count
                || detached.Any(s => removed.Contains(s.StrokeId)))
                return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Stroke IDs must be nonempty, unique and disjoint.", pageId);

            int expectedOrderCount;
            if (_idempotency.TryGetValue(request.OperationId, out var cached) && cached.StructuredStrokeOrderCount is { } retainedCount)
                expectedOrderCount = retainedCount; // Preserve exact input bound after a prior deletion or later page changes.
            else
            {
                var page = _artifact.Pages.FirstOrDefault(p => p.PageId == pageId);
                if (page is null) return Failure<CanvasMutationResult>(CanvasApiErrorCode.NotFound, action, "Canvas page was not found.", pageId);
                if (removed.Any(id => !page.Strokes.Any(s => s.StrokeId == id)))
                    return Failure<CanvasMutationResult>(CanvasApiErrorCode.NotFound, action, "A removed stroke was not found on the page.", pageId);
                expectedOrderCount = checked(page.Strokes.Count - removed.Count
                    + detached.Count(s => !page.Strokes.Any(old => old.StrokeId == s.StrokeId)));
            }
            if (!CaptureBounded(fullStrokeOrder, expectedOrderCount, out var order) || order.Count != expectedOrderCount)
                return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Full stroke order must match the exact proposed page count.", pageId);
            var payload = new { pageId, replacements = detached.OrderBy(s => s.StrokeId).ToArray(),
                removedStrokeIds = removed.Order().ToArray(), fullStrokeOrder = order.ToArray() };
            var result = Mutate(request, action, pageId, payload, artifact =>
            {
                var index = artifact.Pages.FindIndex(p => p.PageId == pageId);
                if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
                var page = artifact.Pages[index];
                var strokes = page.Strokes.ToDictionary(s => s.StrokeId);
                foreach (var id in removed)
                {
                    if (!strokes.TryGetValue(id, out var stroke)) return MutationFailure(CanvasApiErrorCode.NotFound, "Removed stroke was not found.");
                    if (page.Layers.First(l => l.LayerId == stroke.LayerId).IsLocked)
                        return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Ink on locked layers cannot be replaced.");
                    strokes.Remove(id);
                }
                foreach (var proposal in detached)
                {
                    var layer = page.Layers.FirstOrDefault(l => l.LayerId == proposal.LayerId);
                    if (layer is null) return MutationFailure(CanvasApiErrorCode.NotFound, "Replacement stroke layer was not found.");
                    if (layer.IsLocked) return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Ink on locked layers cannot be replaced.");
                    if (strokes.TryGetValue(proposal.StrokeId, out var old))
                    {
                        if (old.LayerId != proposal.LayerId) return MutationFailure(CanvasApiErrorCode.UnsupportedFeature, "Stroke replacement cannot reassign layers.");
                        if (SameStrokeContent(old, proposal)) continue; // Preserve unchanged stroke revision and object.
                        if (old.RevisionId == proposal.RevisionId)
                            return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Changed existing strokes require a fresh revision identity.");
                    }
                    strokes[proposal.StrokeId] = proposal;
                }
                if (order.Distinct().Count() != order.Count || !strokes.Keys.ToHashSet().SetEquals(order))
                    return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Full stroke order must contain every resulting stroke exactly once.");
                if (removed.Count == 0 && strokes.Count == page.Strokes.Count && order.SequenceEqual(page.StrokeOrder)
                    && page.Strokes.All(s => strokes.TryGetValue(s.StrokeId, out var proposed) && SameStrokeContent(s, proposed)))
                    return MutationNoChange();
                var candidate = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(artifact));
                if (strokes.Values.Any(s => s.PathGeometry is not null)) candidate.SchemaVersion = 2;
                candidate.Pages[index] = page with { Strokes = order.Select(id => strokes[id]).ToList(),
                    StrokeOrder = order.ToList(), RevisionId = Guid.NewGuid() };
                try { CanvasArtifactCodec.SerializeSnapshot(candidate); }
                catch (Exception exception) when (exception is CanvasArtifactFormatException or JsonException)
                { return MutationFailure(CanvasApiErrorCode.InvalidArgument, "The complete candidate failed validation before donor preparation."); }
                var settings = captureAccompanyingDocumentSettings?.Invoke();
                if (captureAccompanyingDocumentSettings is not null && settings is null)
                    return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Accompanying donor settings are required.");
                if (settings is not null)
                    candidate.DocumentSettings = JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
                CanvasArtifactCodec.SerializeSnapshot(candidate); // No current mutation before detached settings serialize/validate.
                artifact.SchemaVersion = candidate.SchemaVersion;
                artifact.Pages[index] = candidate.Pages[index];
                artifact.DocumentSettings = candidate.DocumentSettings;
                return MutationChanged([pageId, .. detached.Select(s => s.StrokeId), .. removed]);
            });
            if (result.IsSuccess && _idempotency.TryGetValue(request.OperationId, out var issued))
                _idempotency[request.OperationId] = issued with { StructuredStrokeOrderCount = order.Count };
            return result;
        }
    }
    private static bool SameStrokeContent(CanvasInkStroke a, CanvasInkStroke b) =>
        JsonSerializer.Serialize(a with { RevisionId = Guid.Empty }) == JsonSerializer.Serialize(b with { RevisionId = Guid.Empty });
    private static bool CaptureBounded<T>(IEnumerable<T>? input, int maximum, out List<T> captured)
    {
        captured = [];
        if (input is null || maximum < 0) return false;
        foreach (var value in input)
        {
            if (captured.Count == maximum) return false; // At most maximum+one sentinel consumed, never caller Count.
            captured.Add(value);
        }
        return true;
    }
}

public sealed partial class CanvasArtifactSession
{
    public CanvasApiResult<CanvasMutationResult> DeleteStructuredStrokes(
        CanvasMutationRequest request, Guid pageId, IReadOnlyList<Guid> strokeIds,
        Func<CanvasDocumentSettings>? captureAccompanyingDocumentSettings = null)
    {
        const string action = "Ink.DeleteStructuredBatch";
        if (!CaptureBounded(strokeIds, MaximumStrokeReplacements, out var ids)
            || ids.Count == 0 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            return Failure<CanvasMutationResult>(CanvasApiErrorCode.InvalidArgument, action, "Batch deletion requires one bounded unique set of existing stroke IDs.", pageId);
        var detached = ids.Order().ToArray();
        return Mutate(request, action, pageId, new { pageId, strokeIds = detached }, artifact =>
        {
            var index = artifact.Pages.FindIndex(p => p.PageId == pageId);
            if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[index];
            foreach (var id in detached)
            {
                var stroke = page.Strokes.FirstOrDefault(s => s.StrokeId == id);
                if (stroke is null) return MutationFailure(CanvasApiErrorCode.NotFound, "A batch stroke was not found on the page.");
                if (page.Layers.First(l => l.LayerId == stroke.LayerId).IsLocked)
                    return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Ink on locked layers cannot be deleted.");
            }
            var removed = detached.ToHashSet();
            var candidate = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(artifact));
            candidate.Pages[index] = page with
            {
                Strokes = page.Strokes.Where(s => !removed.Contains(s.StrokeId)).ToList(),
                StrokeOrder = page.StrokeOrder.Where(id => !removed.Contains(id)).ToList(),
                RevisionId = Guid.NewGuid()
            };
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            var settings = captureAccompanyingDocumentSettings?.Invoke();
            if (captureAccompanyingDocumentSettings is not null && settings is null)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Accompanying donor settings are required.");
            if (settings is not null)
                candidate.DocumentSettings = JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            artifact.Pages[index] = candidate.Pages[index];
            artifact.DocumentSettings = candidate.DocumentSettings;
            return MutationChanged([pageId, .. detached]);
        });
    }
}
