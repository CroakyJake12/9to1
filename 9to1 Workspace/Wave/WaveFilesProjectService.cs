using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

/// <summary>Canonical Files identity survives rename/restart; resolved paths exist only while leases are held.</summary>
public sealed class WaveFilesProjectService(IMediaAssetSourceResolver sources)
{
    public async Task<MediaEngineResult<WaveProject>> ImportAsync(WaveProject project, long expectedRevision,
        Guid trackID, string fileID, string? expectedSourceRevision, double timelineStartSeconds,
        CancellationToken cancellationToken = default)
    {
        if (project.Revision != expectedRevision) return Failure<WaveProject>(MediaEngineErrorCode.RevisionConflict);
        try
        {
            if (!Guid.TryParse(fileID, out var hostedID) || hostedID == Guid.Empty) return Failure<WaveProject>(MediaEngineErrorCode.UnsupportedSource);
            project = project with { Tracks = project.Tracks.Select(track => track with { Clips = track.Clips.ToList() }).ToList() };
            var assetID = MediaAssetId.New();
            var resolved = await sources.ResolveAsync(fileID, assetID, expectedSourceRevision, cancellationToken).ConfigureAwait(false);
            if (!resolved.IsSuccess) return MediaEngineResult<WaveProject>.Failure(resolved.Error!);
            await using var lease = resolved.Value!;
            if (!ValidLease(lease, assetID, fileID, expectedSourceRevision)) return Failure<WaveProject>(MediaEngineErrorCode.RevisionConflict);
            cancellationToken.ThrowIfCancellationRequested();
            var imported = WaveProjectStore.AddWavClip(project, trackID, lease.Source.SourceUri.LocalPath, timelineStartSeconds);
            var tracks = imported.Tracks.Select(track => track.TrackId != trackID ? track : track with
            {
                Clips = track.Clips.Select((clip, index) => index != track.Clips.Count - 1 ? clip : clip with
                { SourceReferenceId = assetID.Value, SourcePath = string.Empty, SourceFileID = fileID,
                    SourceRevisionID = lease.Source.SourceRevisionId }).ToList()
            }).ToList();
            return MediaEngineResult<WaveProject>.Success(imported with { Tracks = tracks });
        }
        catch (OperationCanceledException) { return Failure<WaveProject>(MediaEngineErrorCode.OperationCancelled); }
        catch (NotSupportedException) { return Failure<WaveProject>(MediaEngineErrorCode.UnsupportedSource); }
        catch (InvalidDataException) { return Failure<WaveProject>(MediaEngineErrorCode.UnsupportedSource); }
        catch (UnauthorizedAccessException) { return Failure<WaveProject>(MediaEngineErrorCode.PermissionDenied); }
        catch (IOException) { return Failure<WaveProject>(MediaEngineErrorCode.SourceUnavailable); }

    }

    public async Task<MediaEngineResult<long>> ExportPcm16Async(WaveProject project, long expectedRevision,
        string filesResolvedOutputPath, CancellationToken cancellationToken = default)
    {
        if (project.Revision != expectedRevision) return Failure<long>(MediaEngineErrorCode.RevisionConflict);
        // Freeze all mutable lists before the first host await; source metadata is revision-pinned.
        var snapshot = project with { Tracks = project.Tracks.Select(track => track with { Clips = track.Clips.ToList() }).ToList() };
        var clips = snapshot.Tracks.SelectMany(track => track.Clips).ToArray();
        var leases = new Dictionary<Guid, MediaAssetReadLease>();
        try
        {
            foreach (var group in clips.GroupBy(clip => clip.SourceReferenceId))
            {
                var clip = group.First();
                if (string.IsNullOrWhiteSpace(clip.SourceFileID) || string.IsNullOrWhiteSpace(clip.SourceRevisionID))
                    return Failure<long>(MediaEngineErrorCode.SourceUnavailable); // Legacy paths require explicit Files relink.
                if (group.Any(other => other.SourceFileID != clip.SourceFileID || other.SourceRevisionID != clip.SourceRevisionID
                    || other.SourceSha256 != clip.SourceSha256)) return Failure<long>(MediaEngineErrorCode.RevisionConflict);
                var resolved = await sources.ResolveAsync(clip.SourceFileID, new(clip.SourceReferenceId), clip.SourceRevisionID,
                    cancellationToken).ConfigureAwait(false);
                if (!resolved.IsSuccess) return MediaEngineResult<long>.Failure(resolved.Error!);
                var lease = resolved.Value!;
                leases.Add(clip.SourceReferenceId, lease);
                if (!ValidLease(lease, new(clip.SourceReferenceId), clip.SourceFileID, clip.SourceRevisionID)) return Failure<long>(MediaEngineErrorCode.RevisionConflict);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var materialized = snapshot with { Tracks = snapshot.Tracks.Select(track => track with
            { Clips = track.Clips.Select(clip => clip with { SourcePath = leases[clip.SourceReferenceId].Source.SourceUri.LocalPath }).ToList() }).ToList() };
            // Existing exact PCM implementation preserves mute/solo/gain/pan/fades and verifies source SHA256.
            var frames = await Task.Run(() => WaveProjectExporter.ExportPcm16(materialized, filesResolvedOutputPath, cancellationToken), cancellationToken).ConfigureAwait(false);
            return MediaEngineResult<long>.Success(frames);
        }
        catch (OperationCanceledException) { return Failure<long>(MediaEngineErrorCode.OperationCancelled); }
        catch (InvalidDataException) { return Failure<long>(MediaEngineErrorCode.RevisionConflict); }
        catch (UnauthorizedAccessException) { return Failure<long>(MediaEngineErrorCode.PermissionDenied); }
        catch (IOException) { return Failure<long>(MediaEngineErrorCode.ExportFailed); }
        finally { foreach (var lease in leases.Values) await lease.DisposeAsync().ConfigureAwait(false); }
    }

    private static bool ValidLease(MediaAssetReadLease lease, MediaAssetId assetID, string fileID, string? revision) =>
        Guid.TryParse(fileID, out var hostedID) && hostedID != Guid.Empty && lease.Source.HostedItemId == hostedID
        && lease.Source.AssetId == assetID && lease.Source.SourceUri.IsFile && !string.IsNullOrWhiteSpace(lease.Source.SourceRevisionId)
        && (revision is null || lease.Source.SourceRevisionId == revision);
    private static MediaEngineResult<T> Failure<T>(MediaEngineErrorCode code) => MediaEngineResult<T>.Failure(new(code,
        "Files asset access, revision or export target could not be verified.", "9to1.Wave.Files", null, true,
        code is not (MediaEngineErrorCode.RevisionConflict or MediaEngineErrorCode.PermissionDenied)));
}
