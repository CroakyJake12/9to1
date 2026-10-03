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
    /// <summary>Narrows an existing original guard. The extra registered issuer check must never reenter Files.</summary>
    public FilesCommitAuthorityGuard WithAdditionalCurrentCheck(Func<CancellationToken, ValueTask<bool>> originalAdmissionCurrent)
    {
        ArgumentNullException.ThrowIfNull(originalAdmissionCurrent);
        return new(ActorId, async token => await _isCurrent(token).ConfigureAwait(false)
            && await originalAdmissionCurrent(token).ConfigureAwait(false)
            && await _isCurrent(token).ConfigureAwait(false));
    }

    internal async ValueTask ValidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await _isCurrent(cancellationToken).ConfigureAwait(false))
            throw new FilesCommitAuthorityChangedException();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

internal sealed class FilesCommitAuthorityChangedException()
    : UnauthorizedAccessException("The authenticated commit authority changed before publication.") { }
