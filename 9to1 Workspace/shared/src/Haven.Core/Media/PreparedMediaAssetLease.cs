using System.Text.Json.Serialization;

namespace Haven.Core.Media;

/// <summary>An operation-scoped, verified source lease. Persist Reference only. The host must dispose
/// on scope/profile invalidation; consumers revalidate before user playback actions. This is not a
/// native audio/video presentation surface or a reusable permission grant.</summary>
public sealed class PreparedMediaAssetLease : IAsyncDisposable
{
    private MediaAssetReadLease? _lease;
    private readonly Func<CancellationToken, ValueTask<bool>> _revalidate;
    public MediaAssetReference Reference { get; }
    public string ObservedSHA256 { get; }
    public long ObservedSizeBytes { get; }
    [JsonIgnore]
    public MediaAssetSource Source => Volatile.Read(ref _lease)?.Source ?? throw new ObjectDisposedException(nameof(PreparedMediaAssetLease));

    public PreparedMediaAssetLease(MediaAssetReference reference, MediaAssetReadLease lease, string observedSHA256,
        long observedSizeBytes, Func<CancellationToken, ValueTask<bool>> revalidate)
    {
        reference.Validate();
        var integrity = reference with { ExpectedSHA256 = observedSHA256, ExpectedSizeBytes = observedSizeBytes };
        integrity.Validate();
        if (!reference.MatchesIdentity(lease.Source)
            || reference.ExpectedSHA256 is { } hash && !string.Equals(hash, observedSHA256, StringComparison.OrdinalIgnoreCase)
            || reference.ExpectedSizeBytes is { } size && size != observedSizeBytes)
            throw new InvalidDataException("Prepared media differs from its retained reference.");
        Reference = reference; _lease = lease; ObservedSHA256 = observedSHA256; ObservedSizeBytes = observedSizeBytes;
        _revalidate = revalidate ?? throw new ArgumentNullException(nameof(revalidate));
    }

    public async ValueTask<bool> RevalidateAsync(CancellationToken token = default)
    {
        if (Volatile.Read(ref _lease) is null) return false;
        if (!await _revalidate(token).ConfigureAwait(false))
        {
            await DisposeAsync().ConfigureAwait(false);
            return false;
        }
        return Volatile.Read(ref _lease) is not null;
    }

    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _lease, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public interface IMediaAssetPreparationService
{
    Task<MediaEngineResult<PreparedMediaAssetLease>> PrepareAsync(MediaAssetReference reference,
        CancellationToken cancellationToken = default);
}
