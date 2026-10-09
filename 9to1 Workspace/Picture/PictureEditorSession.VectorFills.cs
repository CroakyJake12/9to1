using System.Text.Json;
using Avalonia.Media.Imaging;
using Haven.Core;

namespace HavenOS.Images;

public sealed partial class PictureEditorSession
{
    public Bitmap SetVectorFillStyle(PictureVectorPathTarget target, DocumentVectorFill fill, DocumentVectorFillRule rule) =>
        EditVectorFill(target, fill, rule, null);
    internal Bitmap SetVectorFillFields(PictureVectorPathTarget target, DocumentVectorFill fill,
        DocumentVectorFillRule rule, IReadOnlySet<string> fields) => EditVectorFill(target, fill, rule, fields);
    private Bitmap EditVectorFill(PictureVectorPathTarget target, DocumentVectorFill fill,
        DocumentVectorFillRule rule, IReadOnlySet<string>? fields)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(fill);
        DemandComposition(target.Vector, PictureCompositionTargetKind.Vector);
        return ApplyComposition("Style shared vector fill", document => PictureCompositionAdapter.Mutate(document, target.Vector.GraphRevision,
            (owner, pageId) =>
            {
                var item = owner.GetArtifactSnapshot().Pages.Single(page => page.PageId == pageId)
                    .Objects.Single(item => item.ObjectId == target.Vector.TargetId);
                var updated = PictureVectorFillEditor.Apply(PictureCompositionAdapter.ReadVector(item), target.PathId, fill, rule, fields);
                return owner.UpdateSharedObject(PictureCompositionAdapter.Request(owner), pageId,
                    item with { SharedPayload = JsonSerializer.SerializeToElement(updated) });
            }));
    }
}
