using System.Text.Json;
using Avalonia.Media.Imaging;
using HavenOS.Home.Core;

namespace HavenOS.Images;

public sealed partial class PictureEditorSession
{
    public Bitmap RotateVector(PictureCompositionTarget target, double clockwiseDegrees) =>
        EditVectorTransform(target, "Rotate shared vector", original => PictureVectorTransformEditor.Rotate(original, clockwiseDegrees));

    public Bitmap MirrorVector(PictureCompositionTarget target, bool horizontal) =>
        EditVectorTransform(target, horizontal ? "Mirror shared vector horizontally" : "Mirror shared vector vertically",
            original => PictureVectorTransformEditor.Mirror(original, horizontal));

    private Bitmap EditVectorTransform(PictureCompositionTarget target, string name,
        Func<HomeProductivityObject, HomeProductivityObject> edit)
    {
        DemandComposition(target, PictureCompositionTargetKind.Vector);
        return ApplyComposition(name, document => PictureCompositionAdapter.Mutate(document, target.GraphRevision,
            (owner, pageId) =>
            {
                var item = owner.GetArtifactSnapshot().Pages.Single(page => page.PageId == pageId)
                    .Objects.Single(item => item.ObjectId == target.TargetId);
                var original = PictureCompositionAdapter.ReadVector(item);
                var updated = edit(original);
                return owner.UpdateSharedObject(PictureCompositionAdapter.Request(owner), pageId,
                    item with { SharedPayload = JsonSerializer.SerializeToElement(updated) });
            }));
    }
}
