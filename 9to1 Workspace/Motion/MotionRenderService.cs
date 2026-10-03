using Haven.Core.Media;

namespace HavenOS.Apps.Motion;

/// <summary>Projects canonical Motion frames and identities into the shared renderer using Files-owned leases.</summary>
public sealed class MotionRenderService(IMediaAssetSourceResolver sources, IMediaTimelineRenderer renderer)
{
    public async Task<MediaEngineResult<MediaRenderOutput>> RenderAsync(MotionProject project, long expectedRevision,
        Guid sequenceID, Guid jobID, string filesResolvedOutputPath, IProgress<MediaRenderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Revision != expectedRevision) return Failure(MediaEngineErrorCode.RevisionConflict, "The Motion project changed before rendering.");
        project = project with { Sequences = project.Sequences.Select(sequence => sequence with
        { VideoTracks = sequence.VideoTracks.Select(track => track with { Elements = track.Elements.ToArray() }).ToArray() }).ToArray(),
            AssetReferences = project.AssetReferences.ToArray() };
        MotionProjectStore.Validate(project);
        var sequence = project.Sequences.FirstOrDefault(item => item.SequenceId == sequenceID);
        if (sequence is null) return Failure(MediaEngineErrorCode.UnsupportedSource, "The selected canonical sequence is unavailable.");
        // Capture immutable record arrays before awaiting resolution, so later UI edits cannot retarget the job.
        var tracks = sequence.VideoTracks.Select(track => track with { Elements = track.Elements.ToArray() }).ToArray();
        var assets = project.AssetReferences.ToDictionary(asset => asset.AssetId);
        var leases = new Dictionary<Guid, MediaAssetReadLease>();
        try
        {
            foreach (var id in tracks.SelectMany(track => track.Elements).Select(element => element.AssetId).Distinct())
            {
                var asset = assets[id];
                if (!Guid.TryParse(asset.FileId, out var hostedID) || hostedID == Guid.Empty)
                    return Failure(MediaEngineErrorCode.UnsupportedSource, "The asset requires a canonical Files identity before rendering.");
                var resolved = await sources.ResolveAsync(asset.FileId, new(id), asset.SourceRevisionID, cancellationToken).ConfigureAwait(false);
                if (!resolved.IsSuccess) return MediaEngineResult<MediaRenderOutput>.Failure(resolved.Error!);
                var lease = resolved.Value!;
                leases.Add(id, lease);
                if (lease.Source.HostedItemId != hostedID || lease.Source.AssetId.Value != id || string.IsNullOrWhiteSpace(lease.Source.SourceRevisionId)
                    || asset.SourceRevisionID is not null && asset.SourceRevisionID != lease.Source.SourceRevisionId)
                    return Failure(MediaEngineErrorCode.RevisionConflict, "Files returned a different canonical asset or source revision.");
            }
            var timebase = MediaTimebase.FramesPerSecond(sequence.FrameRateNumerator, sequence.FrameRateDenominator);
            var clips = tracks.SelectMany((track, layer) => track.Elements.Select(element =>
                new MediaRenderClip(new(element.ElementId, track.TrackId, new(element.AssetId), timebase.At(element.SourceIn),
                    timebase.At(element.Duration), timebase.At(element.TimelineStart)), MediaTrackKind.Video, layer, leases[element.AssetId].Source))).ToArray();
            return await renderer.RenderAsync(new(jobID, project.ProjectId, expectedRevision, sequence.SequenceId,
                sequence.Width, sequence.Height, sequence.FrameRateNumerator, sequence.FrameRateDenominator,
                clips, MediaRenderFormat.WebmVp8Video, filesResolvedOutputPath), progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var lease in leases.Values) await lease.DisposeAsync().ConfigureAwait(false);
        }

        MediaEngineResult<MediaRenderOutput> Failure(MediaEngineErrorCode code, string message) =>
            MediaEngineResult<MediaRenderOutput>.Failure(new(code, message, "9to1.Motion.Render", jobID.ToString("N"), true, code != MediaEngineErrorCode.RevisionConflict));
    }
}
