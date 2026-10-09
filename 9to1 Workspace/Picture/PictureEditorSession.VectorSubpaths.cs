using System.Text.Json;
using Avalonia.Media.Imaging;

namespace HavenOS.Images;

public sealed partial class PictureEditorSession
{
    public Bitmap SetVectorSubpathClosed(PictureVectorNodeTarget target, bool closed)
    {
        ArgumentNullException.ThrowIfNull(target); DemandComposition(target.Vector, PictureCompositionTargetKind.Vector);
        _ = CaptureVectorNode(target.Vector, target.PathId, target.SubpathId, target.NodeId);
        var originalItem = PictureCompositionAdapter.Read(Document).Pages[0].Objects.Single(item => item.ObjectId == target.Vector.TargetId);
        var original = PictureCompositionAdapter.ReadVector(originalItem); var prepared = PictureVectorSubpathEditor.SetClosed(original, target, closed);
        return ApplyComposition(closed ? "Close shared vector subpath" : "Open shared vector subpath", document =>
        {
            var page = PictureCompositionAdapter.Read(document).Pages[0];
            if (page.Layers.Single(layer => layer.LayerId == originalItem.LayerId).IsLocked)
                throw new InvalidOperationException("PermissionDenied: the selected shape’s layer is locked.");
            if (ReferenceEquals(original, prepared)) return document.CompositionState;
            return PictureCompositionAdapter.Mutate(document, target.Vector.GraphRevision, (owner, pageId) =>
            {
                var item = owner.GetArtifactSnapshot().Pages.Single(page => page.PageId == pageId).Objects.Single(item => item.ObjectId == target.Vector.TargetId);
                return owner.UpdateSharedObject(PictureCompositionAdapter.Request(owner), pageId, item with { SharedPayload = JsonSerializer.SerializeToElement(prepared) });
            });
        });
    }
}
