using Avalonia.Media.Imaging;

namespace HavenOS.Images;

public sealed partial class PictureEditorSession
{
    /// <summary>Moves the SAME shared object through the canonical layer owner; the source remains editable.</summary>
    public Bitmap MoveVectorToLayer(PictureCompositionTarget vector, PictureCompositionTarget destination)
    {
        DemandComposition(vector, PictureCompositionTargetKind.Vector);
        DemandComposition(destination, PictureCompositionTargetKind.Layer);
        return ApplyComposition("Move shared vector to layer", document => PictureCompositionAdapter.Mutate(document, vector.GraphRevision,
            (owner, page) => owner.MoveObjectToLayer(PictureCompositionAdapter.Request(owner), page, vector.TargetId, destination.TargetId)));
    }
}
