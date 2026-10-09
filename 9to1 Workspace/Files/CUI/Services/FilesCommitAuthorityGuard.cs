namespace HavenOS.Files;

/// <summary>Final trusted host identity check for an already authorized commit. This is not a grant.
/// The predicate runs while the Files metadata lease is held: it must only read the current trusted
/// actor/host state and must never read Files, resolve Files resources, or acquire this store again.</summary>
public sealed class FilesCommitAuthorityGuard
{
    private readonly Func<CancellationToken, ValueTask<bool>> _isCurrent;
    public string ActorId { get; }
    public FilesCommitAuthorityGuard(string actorId, Func<CancellationToken, ValueTask<bool>> isCurrentAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ActorId = actorId;
        _isCurrent = isCurrentAsync ?? throw new ArgumentNullException(nameof(isCurrentAsync));
    }
    internal async ValueTask ValidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<bool> actual;
        try { actual = _isCurrent(cancellationToken).AsTask(); }
        catch (OperationCanceledException original)
        { throw new AggregateException("The synchronous original authority source returned no canceled original Task.", original); }
        bool current;
        try { current = await actual.ConfigureAwait(false); }
        catch when (actual.IsFaulted) { throw actual.Exception!; }
        if (!current) throw new FilesCommitAuthorityChangedException();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

internal sealed class FilesCommitAuthorityChangedException()
    : UnauthorizedAccessException("The authenticated commit authority changed before publication.") { }
