using Haven.Core.Media;
using System.Security.Cryptography;

namespace HavenOS.Apps.Wave;

/// <summary>Canonical Files identity survives rename/restart; resolved paths exist only while leases are held.</summary>
public sealed partial class WaveFilesProjectService(IMediaAssetSourceResolver sources, IMediaAudioDecoder? audioDecoder = null)
{
    private readonly IMediaAssetSourceResolver _sources = sources;
    private readonly IMediaAudioDecoder? _audioDecoder = audioDecoder;
    public async Task<MediaEngineResult<WaveProject>> ImportAsync(WaveProject project, long expectedRevision,
        Guid trackID, string fileID, string? expectedSourceRevision, double timelineStartSeconds,
        CancellationToken cancellationToken = default)
    {
        if (project.Revision != expectedRevision) return Failure<WaveProject>(MediaEngineErrorCode.RevisionConflict);
        try
        {
            if (!Guid.TryParse(fileID, out var hostedID) || hostedID == Guid.Empty) return Failure<WaveProject>(MediaEngineErrorCode.UnsupportedSource);
            project = project with { Tracks = project.Tracks.Select(track => track with { Clips = track.Clips.ToList() }).ToList() };
            WaveProjectStore.Validate(project);
            var assetID = MediaAssetId.New();
            var resolved = await _sources.ResolveAsync(fileID, assetID, expectedSourceRevision, cancellationToken).ConfigureAwait(false);
            if (!resolved.IsSuccess) return MediaEngineResult<WaveProject>.Failure(resolved.Error!);
            await using var lease = resolved.Value!;
            if (!ValidLease(lease, assetID, fileID, expectedSourceRevision)) return Failure<WaveProject>(MediaEngineErrorCode.RevisionConflict);
            cancellationToken.ThrowIfCancellationRequested();
            var sourceHash = await HashAsync(lease.Source.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false);
            WaveProject imported;
            WaveAudioDerivation? derivation = null;
            try { imported = WaveProjectStore.AddWavClip(project, trackID, lease.Source.SourceUri.LocalPath, timelineStartSeconds); }
            catch (Exception error) when (_audioDecoder is not null && (error is InvalidDataException or NotSupportedException))
            {
                var decoded = await _audioDecoder.DecodeAsync(lease, cancellationToken).ConfigureAwait(false);
                if (!decoded.IsSuccess) return MediaEngineResult<WaveProject>.Failure(decoded.Error!);
                await using var decodedLease = decoded.Value!;
                if (!ValidDecoded(decodedLease, lease, sourceHash)) return Failure<WaveProject>(MediaEngineErrorCode.RevisionConflict);
                imported = WaveProjectStore.AddWavClip(project, trackID, decodedLease.Source.TemporaryWavePath, timelineStartSeconds);
                if (imported.Tracks.Single(track => track.TrackId == trackID).Clips[^1].SourceSha256 != decodedLease.Source.DecodedSha256)
                    return Failure<WaveProject>(MediaEngineErrorCode.RevisionConflict);
                derivation = new(decodedLease.Source.DecodedSha256, decodedLease.Source.DecodeProfile, decodedLease.Source.Runtime);
            }
            if (await HashAsync(lease.Source.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false) != sourceHash)
                return Failure<WaveProject>(MediaEngineErrorCode.RevisionConflict);
            var tracks = imported.Tracks.Select(track => track.TrackId != trackID ? track : track with
            {
                Clips = track.Clips.Select((clip, index) => index != track.Clips.Count - 1 ? clip : clip with
                { SourceReferenceId = assetID.Value, SourcePath = string.Empty, SourceFileID = fileID,
                    SourceRevisionID = lease.Source.SourceRevisionId, SourceSha256 = sourceHash, AudioDerivation = derivation }).ToList()
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
        var decodedLeases = new Dictionary<Guid, MediaAudioDecodedLease>();
        string? stagingOutput = null;
        try
        {
            WaveProjectStore.Validate(snapshot);
            foreach (var group in clips.GroupBy(clip => clip.SourceReferenceId))
            {
                var clip = group.First();
                if (string.IsNullOrWhiteSpace(clip.SourceFileID) || string.IsNullOrWhiteSpace(clip.SourceRevisionID))
                    return Failure<long>(MediaEngineErrorCode.SourceUnavailable); // Legacy paths require explicit Files relink.
                if (group.Any(other => other.SourceFileID != clip.SourceFileID || other.SourceRevisionID != clip.SourceRevisionID
                    || other.SourceSha256 != clip.SourceSha256 || other.AudioDerivation != clip.AudioDerivation)) return Failure<long>(MediaEngineErrorCode.RevisionConflict);
                var resolved = await _sources.ResolveAsync(clip.SourceFileID, new(clip.SourceReferenceId), clip.SourceRevisionID,
                    cancellationToken).ConfigureAwait(false);
                if (!resolved.IsSuccess) return MediaEngineResult<long>.Failure(resolved.Error!);
                var lease = resolved.Value!;
                leases.Add(clip.SourceReferenceId, lease);
                if (!ValidLease(lease, new(clip.SourceReferenceId), clip.SourceFileID, clip.SourceRevisionID)) return Failure<long>(MediaEngineErrorCode.RevisionConflict);
                if (!string.Equals(await HashAsync(lease.Source.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false),
                    clip.SourceSha256, StringComparison.OrdinalIgnoreCase)) return Failure<long>(MediaEngineErrorCode.RevisionConflict);
                if (clip.AudioDerivation is { } expectedDerivation)
                {
                    if (_audioDecoder is null) return Failure<long>(MediaEngineErrorCode.BackendUnavailable);
                    var decoded = await _audioDecoder.DecodeAsync(lease, cancellationToken).ConfigureAwait(false);
                    if (!decoded.IsSuccess) return MediaEngineResult<long>.Failure(decoded.Error!);
                    var decodedLease = decoded.Value!;
                    decodedLeases.Add(clip.SourceReferenceId, decodedLease);
                    if (!ValidDecoded(decodedLease, lease, clip.SourceSha256)
                        || expectedDerivation != new WaveAudioDerivation(decodedLease.Source.DecodedSha256,
                            decodedLease.Source.DecodeProfile, decodedLease.Source.Runtime))
                        return Failure<long>(MediaEngineErrorCode.RevisionConflict);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var materialized = snapshot with { Tracks = snapshot.Tracks.Select(track => track with
            { Clips = track.Clips.Select(clip => decodedLeases.TryGetValue(clip.SourceReferenceId, out var decoded)
                ? clip with { SourcePath = decoded.Source.TemporaryWavePath, SourceSha256 = decoded.Source.DecodedSha256 }
                : clip with { SourcePath = leases[clip.SourceReferenceId].Source.SourceUri.LocalPath }).ToList() }).ToList() };
            // Existing exact PCM implementation preserves mute/solo/gain/pan/fades and verifies source SHA256.
            var destination = Path.GetFullPath(filesResolvedOutputPath);
            stagingOutput = Path.Combine(Path.GetDirectoryName(destination)!, ".wave-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            var frames = await Task.Run(() => WaveProjectExporter.ExportPcm16(materialized, stagingOutput, cancellationToken), cancellationToken).ConfigureAwait(false);
            foreach (var group in clips.GroupBy(clip => clip.SourceReferenceId))
                if (!string.Equals(await HashAsync(leases[group.Key].Source.SourceUri.LocalPath, cancellationToken).ConfigureAwait(false),
                    group.First().SourceSha256, StringComparison.OrdinalIgnoreCase))
                    return Failure<long>(MediaEngineErrorCode.RevisionConflict);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stagingOutput, destination, overwrite: false);
            stagingOutput = null;
            return MediaEngineResult<long>.Success(frames);
        }
        catch (OperationCanceledException) { return Failure<long>(MediaEngineErrorCode.OperationCancelled); }
        catch (InvalidDataException) { return Failure<long>(MediaEngineErrorCode.RevisionConflict); }
        catch (UnauthorizedAccessException) { return Failure<long>(MediaEngineErrorCode.PermissionDenied); }
        catch (IOException) { return Failure<long>(MediaEngineErrorCode.ExportFailed); }
        finally
        {
            if (stagingOutput is not null)
            {
                try { File.Delete(stagingOutput); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (var lease in decodedLeases.Values) await lease.DisposeAsync().ConfigureAwait(false);
            foreach (var lease in leases.Values) await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool ValidDecoded(MediaAudioDecodedLease decoded, MediaAssetReadLease source, string sourceHash) =>
        decoded.Source.AssetID == source.Source.AssetId && decoded.Source.FileID == source.Source.HostedItemId
        && decoded.Source.SourceRevision == source.Source.SourceRevisionId && decoded.Source.SourceSha256 == sourceHash;
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
    }

    private static bool ValidLease(MediaAssetReadLease lease, MediaAssetId assetID, string fileID, string? revision) =>
        Guid.TryParse(fileID, out var hostedID) && hostedID != Guid.Empty && lease.Source.HostedItemId == hostedID
        && lease.Source.AssetId == assetID && lease.Source.SourceUri.IsFile && !string.IsNullOrWhiteSpace(lease.Source.SourceRevisionId)
        && (revision is null || lease.Source.SourceRevisionId == revision);
    private static MediaEngineResult<T> Failure<T>(MediaEngineErrorCode code) => MediaEngineResult<T>.Failure(new(code,
        "Files asset access, revision or export target could not be verified.", "9to1.Wave.Files", null, true,
        code is not (MediaEngineErrorCode.RevisionConflict or MediaEngineErrorCode.PermissionDenied)));
}
