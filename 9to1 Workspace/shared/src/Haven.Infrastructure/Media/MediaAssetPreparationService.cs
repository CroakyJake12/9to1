using System.Security.Cryptography;
using Haven.Application;
using Haven.Core.Media;

namespace Haven.Infrastructure.Media;

/// <summary>Consumes the actual Files retained-revision owner. No path supplied by a document or UI
/// can become a source. The host supplies an explicit byte budget; integrity checks grant no access.</summary>
public sealed class MediaAssetPreparationService : IMediaAssetPreparationService
{
    private readonly IMediaRetainedAssetSourceResolver _sources;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly long _maximumSourceBytes;

    public MediaAssetPreparationService(IMediaRetainedAssetSourceResolver sources, IAuthenticatedResourceActorSource actors,
        long maximumSourceBytes)
    {
        if (maximumSourceBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumSourceBytes));
        _sources = sources; _actors = actors; _maximumSourceBytes = maximumSourceBytes;
    }

    public Task<MediaEngineResult<PreparedMediaAssetLease>> PrepareAsync(MediaAssetReference reference,
        CancellationToken cancellationToken = default) => PrepareCoreAsync(reference, null, cancellationToken);

    private async Task<MediaEngineResult<PreparedMediaAssetLease>> PrepareCoreAsync(MediaAssetReference reference,
        AuthenticatedResourceActor? requiredActor, CancellationToken token)
    {
        MediaAssetReadLease? lease = null;
        try
        {
            ArgumentNullException.ThrowIfNull(reference); reference.Validate(); token.ThrowIfCancellationRequested();
            var actor = await _actors.GetCurrentAsync(token).ConfigureAwait(false);
            if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) || string.IsNullOrWhiteSpace(actor.AuthenticationRevision)
                || actor.AccountId == Guid.Empty || actor.OrganisationId == Guid.Empty || actor.OrganisationId is not null && actor.AccountId is null
                || requiredActor is not null && actor != requiredActor) return Fail(MediaEngineErrorCode.PermissionDenied);
            var resolved = await _sources.ResolveRetainedAsync(reference.FileID.ToString("D"), reference.AssetID,
                reference.RevisionID, token).ConfigureAwait(false);
            if (!resolved.IsSuccess) return MediaEngineResult<PreparedMediaAssetLease>.Failure(resolved.Error!);
            lease = resolved.Value ?? throw new InvalidDataException("Files returned no retained source lease.");
            if (!reference.MatchesIdentity(lease.Source)) return Fail(MediaEngineErrorCode.RevisionConflict);
            // Remote playback needs its owning materialisation/transport capability; a URI alone
            // does not permit this local preparation path to fetch network content.
            if (!lease.Source.SourceUri.IsFile) return Fail(MediaEngineErrorCode.UnsupportedSource);
            long bytes; string hash;
            await using (var stream = new FileStream(lease.Source.SourceUri.LocalPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                bytes = stream.Length;
                if (bytes > _maximumSourceBytes) return Fail(MediaEngineErrorCode.UnsupportedSource);
                if (reference.ExpectedSizeBytes is { } size && size != bytes) return Fail(MediaEngineErrorCode.RevisionConflict);
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
                if (stream.Length != bytes || reference.ExpectedSHA256 is { } expected && !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                    return Fail(MediaEngineErrorCode.RevisionConflict);
            }
            if (await _actors.GetCurrentAsync(token).ConfigureAwait(false) != actor) return Fail(MediaEngineErrorCode.PermissionDenied);
            var capturedIntegrity = reference with { ExpectedSHA256 = hash, ExpectedSizeBytes = bytes };
            var prepared = new PreparedMediaAssetLease(reference, lease, hash, bytes,
                cancellation => RevalidateAsync(capturedIntegrity, actor, cancellation));
            lease = null; // Ownership transferred only after every verification succeeded.
            return MediaEngineResult<PreparedMediaAssetLease>.Success(prepared);
        }
        catch (OperationCanceledException) { return Fail(MediaEngineErrorCode.OperationCancelled); }
        catch (UnauthorizedAccessException) { return Fail(MediaEngineErrorCode.PermissionDenied); }
        catch (InvalidDataException) { return Fail(MediaEngineErrorCode.UnsupportedSource); }
        catch (IOException) { return Fail(MediaEngineErrorCode.SourceUnavailable); }
        catch (ArgumentException) { return Fail(MediaEngineErrorCode.UnsupportedSource); }
        finally { if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false); }
    }

    private async ValueTask<bool> RevalidateAsync(MediaAssetReference reference, AuthenticatedResourceActor actor, CancellationToken token)
    {
        var checkedSource = await PrepareCoreAsync(reference, actor, token).ConfigureAwait(false);
        if (!checkedSource.IsSuccess) return false;
        await using var current = checkedSource.Value!;
        return true;
    }

    private static MediaEngineResult<PreparedMediaAssetLease> Fail(MediaEngineErrorCode code) =>
        MediaEngineResult<PreparedMediaAssetLease>.Failure(new(code, "The retained media source could not be prepared.",
            "media.asset.prepare", null, true, code != MediaEngineErrorCode.RevisionConflict));
}
