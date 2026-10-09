using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

public static class WaveMediaAssetReferences
{
    /// <summary>Projects the clip's original Files source, never its ephemeral decoded derivative.</summary>
    public static MediaAssetReference RetainedSource(WaveClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!Guid.TryParse(clip.SourceFileID, out var fileID) || fileID == Guid.Empty
            || string.IsNullOrWhiteSpace(clip.SourceRevisionID))
            throw new InvalidOperationException("The Wave clip has no retained canonical Files reference.");
        var reference = new MediaAssetReference(new(clip.SourceReferenceId), fileID, clip.SourceRevisionID, clip.SourceSha256);
        reference.Validate();
        return reference;
    }
}
