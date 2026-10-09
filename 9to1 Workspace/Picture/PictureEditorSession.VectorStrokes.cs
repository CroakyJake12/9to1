using System.Text.Json;
using Avalonia.Media.Imaging;
using Haven.Core;

namespace HavenOS.Images;

public sealed partial class PictureEditorSession
{
    public Bitmap SetVectorStroke(PictureVectorPathTarget target, DocumentVectorStroke stroke) =>
        EditVectorStroke(target, stroke, null);

    internal Bitmap SetVectorStrokeFields(PictureVectorPathTarget target, DocumentVectorStroke stroke, IReadOnlySet<string> changedFields) =>
        EditVectorStroke(target, stroke, changedFields);

    private Bitmap EditVectorStroke(PictureVectorPathTarget target, DocumentVectorStroke stroke, IReadOnlySet<string>? changedFields)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(stroke);
        DemandComposition(target.Vector, PictureCompositionTargetKind.Vector);
        return ApplyComposition("Style shared vector stroke", document => PictureCompositionAdapter.Mutate(document, target.Vector.GraphRevision,
            (owner, pageId) =>
            {
                var item = owner.GetArtifactSnapshot().Pages.Single(page => page.PageId == pageId)
                    .Objects.Single(item => item.ObjectId == target.Vector.TargetId);
                var updated = PictureVectorStrokeEditor.Apply(PictureCompositionAdapter.ReadVector(item), target.PathId, stroke, changedFields);
                return owner.UpdateSharedObject(PictureCompositionAdapter.Request(owner), pageId,
                    item with { SharedPayload = JsonSerializer.SerializeToElement(updated) });
            }));
    }
}
