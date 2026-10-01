using Haven.Core.Media;

namespace HavenOS.Apps.Motion;

public static class MotionMediaAssetReferences
{
    public static MediaAssetReference RetainedSource(MotionAssetReference asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!Guid.TryParse(asset.FileId, out var fileID) || fileID == Guid.Empty || string.IsNullOrWhiteSpace(asset.SourceRevisionID))
            throw new InvalidOperationException("The Motion asset has no retained canonical Files reference.");
        var reference = new MediaAssetReference(new(asset.AssetId), fileID, asset.SourceRevisionID);
        reference.Validate();
        return reference;
    }
}
