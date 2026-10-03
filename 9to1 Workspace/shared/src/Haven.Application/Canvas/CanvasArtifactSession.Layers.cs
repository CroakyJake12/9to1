using System.Text.Json;
using Haven.Application.Canvas;
namespace Haven.Application;
public sealed partial class CanvasArtifactSession
{
    /// <summary>Exact new layer identity is captured before owning review; this
    /// canonical hook grants no Home/Files permission or donor role adoption.</summary>
    public CanvasApiResult<CanvasMutationResult> CreateLayerWithDonor(
        CanvasMutationRequest request, Guid pageId, Guid newLayerId, string name, int? insertAt,
        Func<CanvasArtifact, CanvasDocumentSettings> captureDonorSettings)
    {
        ArgumentNullException.ThrowIfNull(captureDonorSettings);
        return Mutate(request, "Layers.Create", pageId, new { name, insertAt, newLayerId }, artifact =>
        {
            var index = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            if (newLayerId == Guid.Empty || artifact.Pages.SelectMany(page => page.Layers).Any(layer => layer.LayerId == newLayerId)
                || string.IsNullOrWhiteSpace(name)) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "A unique new layer identity and name are required.");
            var page = artifact.Pages[index];var position = insertAt ?? page.LayerOrder.Count;
            if (position < 0 || position > page.LayerOrder.Count) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Layer insertion order is outside the current layer range.");
            var candidate = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(artifact));
            var layer = new CanvasLayer { LayerId = newLayerId, Name = name.Trim() };
            var layers = candidate.Pages[index].Layers.ToList();layers.Add(layer);
            var order = candidate.Pages[index].LayerOrder.ToList();order.Insert(position, newLayerId);
            candidate.Pages[index] = candidate.Pages[index] with { Layers = layers, LayerOrder = order, RevisionId = Guid.NewGuid() };
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            var settings = captureDonorSettings(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(candidate)));
            if (settings is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Donor settings are required.");
            candidate.DocumentSettings = JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            artifact.Pages[index] = candidate.Pages[index];artifact.DocumentSettings = candidate.DocumentSettings;
            return MutationChanged(pageId, newLayerId);
        });
    }

    /// <summary>Canonical layer reorder with a detached, prevalidated donor preparation.
    /// This hook conveys no Home/Files authority. The owning document authenticates
    /// its original native mapping and commits donor state only after this returns.</summary>
    public CanvasApiResult<CanvasMutationResult> ReorderLayerWithDonor(
        CanvasMutationRequest request, Guid pageId, Guid layerId, int toIndex,
        Func<CanvasArtifact, CanvasDocumentSettings> captureDonorSettings)
    {
        ArgumentNullException.ThrowIfNull(captureDonorSettings);
        return Mutate(request, "Layers.Reorder", layerId, new { pageId, toIndex }, artifact =>
        {
            var index = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[index];
            var from = page.LayerOrder.IndexOf(layerId);
            if (from < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas layer was not found.");
            if (toIndex < 0 || toIndex >= page.LayerOrder.Count)
                return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Layer order is outside the current layer range.");
            if (from == toIndex) return MutationNoChange();
            var candidate = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(artifact));
            var order = page.LayerOrder.ToList();
            order.RemoveAt(from); order.Insert(toIndex, layerId);
            candidate.Pages[index] = candidate.Pages[index] with { LayerOrder = order, RevisionId = Guid.NewGuid() };
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            // Delegate sees a second private clone: aliases cannot alter the proposal.
            var settings = captureDonorSettings(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(candidate)));
            if (settings is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Donor settings are required.");
            candidate.DocumentSettings = JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            artifact.Pages[index] = candidate.Pages[index];
            artifact.DocumentSettings = candidate.DocumentSettings;
            return MutationChanged(pageId, layerId);
        });
    }
    /// <summary>Move genuine canonical ink between exact unlocked layers; the
    /// owning donor callback updates native rank without changing stroke identity.</summary>
    public CanvasApiResult<CanvasMutationResult> MoveStrokeToLayerWithDonor(
        CanvasMutationRequest request, Guid pageId, Guid strokeId, Guid destinationLayerId,
        Func<CanvasArtifact, CanvasDocumentSettings> captureDonorSettings)
    {
        ArgumentNullException.ThrowIfNull(captureDonorSettings);
        return Mutate(request, "Layers.MoveObjects", strokeId, new { pageId, destinationLayerId }, artifact =>
        {
            var index = artifact.Pages.FindIndex(page => page.PageId == pageId);
            if (index < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canvas page was not found.");
            var page = artifact.Pages[index];var strokeIndex = page.Strokes.FindIndex(stroke => stroke.StrokeId == strokeId);
            if (strokeIndex < 0) return MutationFailure(CanvasApiErrorCode.NotFound, "Canonical ink was not found.");
            var stroke = page.Strokes[strokeIndex];var destination = page.Layers.FirstOrDefault(layer => layer.LayerId == destinationLayerId);
            if (destination is null) return MutationFailure(CanvasApiErrorCode.NotFound, "Destination layer was not found.");
            if (page.Layers.First(layer => layer.LayerId == stroke.LayerId).IsLocked || destination.IsLocked)
                return MutationFailure(CanvasApiErrorCode.PermissionDenied, "Ink cannot be moved from or into a locked layer.");
            if (stroke.LayerId == destinationLayerId) return MutationNoChange();
            var candidate = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(artifact));
            var strokes = candidate.Pages[index].Strokes.ToList();
            strokes[strokeIndex] = strokes[strokeIndex] with { LayerId = destinationLayerId, RevisionId = Guid.NewGuid() };
            candidate.Pages[index] = candidate.Pages[index] with { Strokes = strokes, RevisionId = Guid.NewGuid() };
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            var settings = captureDonorSettings(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(candidate)));
            if (settings is null) return MutationFailure(CanvasApiErrorCode.InvalidArgument, "Donor settings are required.");
            candidate.DocumentSettings = JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            artifact.Pages[index] = candidate.Pages[index];artifact.DocumentSettings = candidate.DocumentSettings;
            return MutationChanged(pageId, strokeId, destinationLayerId);
        });
    }

    public CanvasApiResult<CanvasMutationResult> SetLayerVisibilityWithDonor(CanvasMutationRequest request, Guid pageId,
        Guid layerId, bool isVisible, Func<CanvasArtifact,CanvasDocumentSettings> captureDonorSettings) =>
        SetLayerStateWithDonor(request,pageId,layerId,isVisible,false,captureDonorSettings);

    public CanvasApiResult<CanvasMutationResult> SetLayerLockedWithDonor(CanvasMutationRequest request, Guid pageId,
        Guid layerId, bool isLocked, Func<CanvasArtifact,CanvasDocumentSettings> captureDonorSettings) =>
        SetLayerStateWithDonor(request,pageId,layerId,isLocked,true,captureDonorSettings);

    private CanvasApiResult<CanvasMutationResult> SetLayerStateWithDonor(CanvasMutationRequest request, Guid pageId,
        Guid layerId, bool value, bool locked, Func<CanvasArtifact,CanvasDocumentSettings> captureDonorSettings)
    {
        ArgumentNullException.ThrowIfNull(captureDonorSettings);
        object arguments=locked ? new {pageId,isLocked=value} : (object)new {pageId,isVisible=value};
        return Mutate(request,locked ? "Layers.SetLocked" : "Layers.SetVisibility",layerId,arguments,artifact=>
        {
            var index=artifact.Pages.FindIndex(page=>page.PageId==pageId);
            if(index<0)return MutationFailure(CanvasApiErrorCode.NotFound,"Canvas page was not found.");
            var page=artifact.Pages[index];var layerIndex=page.Layers.FindIndex(layer=>layer.LayerId==layerId);
            if(layerIndex<0)return MutationFailure(CanvasApiErrorCode.NotFound,"Canvas layer was not found.");
            var layer=page.Layers[layerIndex];
            if((locked ? layer.IsLocked : layer.IsVisible)==value)return MutationNoChange();
            var candidate=CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(artifact));
            var layers=candidate.Pages[index].Layers.ToList();
            layers[layerIndex]=locked ? layer with {IsLocked=value,RevisionId=Guid.NewGuid()} : layer with {IsVisible=value,RevisionId=Guid.NewGuid()};
            candidate.Pages[index]=candidate.Pages[index] with {Layers=layers,RevisionId=Guid.NewGuid()};
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            var settings=captureDonorSettings(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(candidate)));
            if(settings is null)return MutationFailure(CanvasApiErrorCode.InvalidArgument,"Donor settings are required.");
            candidate.DocumentSettings=JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            artifact.Pages[index]=candidate.Pages[index];artifact.DocumentSettings=candidate.DocumentSettings;
            return MutationChanged(pageId,layerId);
        });
    }

    /// <summary>Deletes an exact unlocked layer and its canonical contents from a
    /// detached proposal. The donor owner must delete corresponding native keys;
    /// ordinary canonical Undo/Redo restores the captured native settings too.</summary>
    public CanvasApiResult<CanvasMutationResult> DeleteLayerWithDonor(CanvasMutationRequest request, Guid pageId,
        Guid layerId, Func<CanvasArtifact,CanvasDocumentSettings> captureDonorSettings)
    {
        ArgumentNullException.ThrowIfNull(captureDonorSettings);
        return Mutate(request,"Layers.Delete",layerId,new {pageId},artifact=>
        {
            var index=artifact.Pages.FindIndex(page=>page.PageId==pageId);
            if(index<0)return MutationFailure(CanvasApiErrorCode.NotFound,"Canvas page was not found.");
            var page=artifact.Pages[index];var layer=page.Layers.FirstOrDefault(item=>item.LayerId==layerId);
            if(layer is null)return MutationFailure(CanvasApiErrorCode.NotFound,"Canvas layer was not found.");
            if(layer.IsLocked)return MutationFailure(CanvasApiErrorCode.PermissionDenied,"A locked layer cannot be deleted.");
            if(page.Layers.Count<=1)return MutationFailure(CanvasApiErrorCode.InvalidArgument,"The document requires a remaining layer.");
            var candidate=CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(artifact));
            var proposed=candidate.Pages[index];
            var removedObjects=proposed.Objects.Where(item=>item.LayerId==layerId).Select(item=>item.ObjectId).ToHashSet();
            var removedStrokes=proposed.Strokes.Where(item=>item.LayerId==layerId).Select(item=>item.StrokeId).ToHashSet();
            candidate.Pages[index]=proposed with
            {
                Layers=proposed.Layers.Where(item=>item.LayerId!=layerId).ToList(),
                LayerOrder=proposed.LayerOrder.Where(id=>id!=layerId).ToList(),
                Objects=proposed.Objects.Where(item=>item.LayerId!=layerId).ToList(),
                ObjectOrder=proposed.ObjectOrder.Where(id=>!removedObjects.Contains(id)).ToList(),
                Strokes=proposed.Strokes.Where(item=>item.LayerId!=layerId).ToList(),
                StrokeOrder=proposed.StrokeOrder.Where(id=>!removedStrokes.Contains(id)).ToList(),
                RevisionId=Guid.NewGuid()
            };
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            var settings=captureDonorSettings(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.SerializeSnapshot(candidate)));
            if(settings is null)return MutationFailure(CanvasApiErrorCode.InvalidArgument,"Donor settings are required.");
            candidate.DocumentSettings=JsonSerializer.Deserialize<CanvasDocumentSettings>(JsonSerializer.Serialize(settings))!;
            CanvasArtifactCodec.SerializeSnapshot(candidate);
            artifact.Pages[index]=candidate.Pages[index];artifact.DocumentSettings=candidate.DocumentSettings;
            return MutationChanged(pageId,layerId);
        });
    }

}
