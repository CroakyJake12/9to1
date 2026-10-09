using System.Text.Json;
using Avalonia.Media.Imaging;

namespace HavenOS.Images;

public sealed partial class PictureEditorSession
{
    public Bitmap AddVectorAnchor(PictureVectorNodeTarget target, double x, double y, out PictureVectorNodeTarget added)
    {
        ArgumentNullException.ThrowIfNull(target); DemandComposition(target.Vector, PictureCompositionTargetKind.Vector);
        _ = CaptureVectorNode(target.Vector, target.PathId, target.SubpathId, target.NodeId);
        var item = PictureCompositionAdapter.Read(Document).Pages[0].Objects.Single(item => item.ObjectId == target.Vector.TargetId);
        var prepared = PictureVectorAnchorEditor.Add(PictureCompositionAdapter.ReadVector(item), target, x, y, out var newId);
        var result = PublishAnchorEdit(target, prepared, "Add shared vector anchor");
        try
        {
            added = CaptureVectorNode(CaptureComposition(target.Vector.TargetId, PictureCompositionTargetKind.Vector), target.PathId, target.SubpathId, newId);
            return result;
        }
        catch (Exception originalFailure)
        {
            try { result.Dispose(); }
            catch (Exception retirementFailure) { throw new AggregateException(originalFailure, retirementFailure); }
            throw;
        }
    }
    public Bitmap DeleteVectorAnchor(PictureVectorNodeTarget target)
    {
        ArgumentNullException.ThrowIfNull(target); DemandComposition(target.Vector, PictureCompositionTargetKind.Vector);
        _ = CaptureVectorNode(target.Vector, target.PathId, target.SubpathId, target.NodeId);
        var item = PictureCompositionAdapter.Read(Document).Pages[0].Objects.Single(item => item.ObjectId == target.Vector.TargetId);
        var prepared = PictureVectorAnchorEditor.Delete(PictureCompositionAdapter.ReadVector(item), target);
        return PublishAnchorEdit(target, prepared, "Delete shared vector anchor");
    }
    private Bitmap PublishAnchorEdit(PictureVectorNodeTarget target, HavenOS.Home.Core.HomeProductivityObject prepared, string name) =>
        ApplyComposition(name, document => PictureCompositionAdapter.Mutate(document, target.Vector.GraphRevision, (owner, pageId) =>
        {
            var item = owner.GetArtifactSnapshot().Pages.Single(page => page.PageId == pageId).Objects.Single(item => item.ObjectId == target.Vector.TargetId);
            return owner.UpdateSharedObject(PictureCompositionAdapter.Request(owner), pageId,
                item with { SharedPayload = JsonSerializer.SerializeToElement(prepared) });
        }));
}
