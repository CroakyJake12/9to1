namespace Haven.Core.Media;

/// <summary>Observed backend evidence, separate from the maintained donor source commit.
/// This records the executable/version, not a complete OS/library reproducibility manifest.</summary>
public sealed record MediaAudioDecoderEvidence(string Engine, string ObservedVersion, string ExecutableSha256);

/// <summary>Precision-preserving decoded audio is an operation-scoped derivation of the same Files revision.
/// Its path must not become project/source identity or an independently owned source file.</summary>
public sealed record MediaAudioDecodedSource(MediaAssetId AssetID, Guid FileID, string SourceRevision,
    string SourceSha256, string DecodedSha256, string DecodeProfile, MediaAudioDecoderEvidence Runtime,
    string TemporaryWavePath);

public sealed class MediaAudioDecodedLease : IAsyncDisposable
{
    private Func<ValueTask>? _release;
    public MediaAudioDecodedSource Source { get; }
    public MediaAudioDecodedLease(MediaAudioDecodedSource source, Func<ValueTask> release)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _release, null)?.Invoke() ?? ValueTask.CompletedTask;
}

public interface IMediaAudioDecoder
{
    /// <summary>The caller retains the immutable Files read lease until this operation completes.
    /// No source ownership or permission is established by decoding.</summary>
    Task<MediaEngineResult<MediaAudioDecodedLease>> DecodeAsync(MediaAssetReadLease source,
        CancellationToken cancellationToken = default);
}
