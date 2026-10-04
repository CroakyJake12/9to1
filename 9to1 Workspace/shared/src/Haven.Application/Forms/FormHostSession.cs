namespace Haven.Application;

/// <summary>Issuer-created native host services bound to the actual actor and settings root at opening.
/// The session supplies no authority: every operation still checks actual Home ownership and commit admission.</summary>
public sealed class FormHostSession : IDisposable
{
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IResourceStoreIdentitySource _identities;
    private readonly AuthenticatedResourceActor _originalActor;
    private readonly Guid _originalStore;
    private int _revoked;
    private FormHostSession(IVersionedSettingsStore settings, IResourceStoreIdentitySource identities,
        IFormStoreAuthority authority, IFormProjectPublicationValidator validator,
        IAuthenticatedResourceActorSource actors, AuthenticatedResourceActor originalActor, Guid originalStore, TimeProvider clock)
    {
        _actors = actors; _identities = identities; _originalActor = originalActor; _originalStore = originalStore;
        var boundActors = new OriginalActor(actors, originalActor, () => Volatile.Read(ref _revoked) == 0);
        var boundIdentity = new OriginalIdentity(identities, originalStore, () => Volatile.Read(ref _revoked) == 0);
        Publications = new(settings, boundIdentity, authority, validator, clock, boundActors);
        Authoring = new(Publications, clock);
        Responses = new(Publications, settings, boundIdentity, authority, boundActors, clock);
    }
    public FormPublicationService Publications { get; }
    public FormAuthoringService Authoring { get; }
    public FormResponseSessionService Responses { get; }

    internal static async Task<FormHostSession> CreateAsync(IVersionedSettingsStore settings,
        IResourceStoreIdentitySource identities, IFormStoreAuthority authority, IFormProjectPublicationValidator validator,
        IAuthenticatedResourceActorSource? actors, AuthenticatedResourceActor expectedActor, TimeProvider clock, CancellationToken token)
    {
        if (actors is null || !ReferenceEquals(settings, identities))
            throw new UnauthorizedAccessException("The Forms host requires the actual same settings identity and canonical actor.");
        var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The Forms host requires an authenticated actor.");
        if (actor != expectedActor) throw new UnauthorizedAccessException("The originating Forms open actor changed.");
        var identity = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty
            || await actors.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The original Forms host context changed while opening.");
        var session = new FormHostSession(settings, identities, authority, validator, actors, actor, identity.StoreId, clock);
        await session.RequireCurrentAsync(token).ConfigureAwait(false);
        return session;
    }
    public void Dispose() => Interlocked.Exchange(ref _revoked, 1);

    public async Task RequireCurrentAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _revoked) != 0, this);
        if (await _actors.GetCurrentAsync(token).ConfigureAwait(false) != _originalActor)
            throw new UnauthorizedAccessException("The original Forms host actor changed.");
        var identity = await _identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        var finalActor = await _actors.GetCurrentAsync(token).ConfigureAwait(false);
        if (Volatile.Read(ref _revoked) != 0 || identity.SchemaVersion != 1 || identity.StoreId != _originalStore
            || finalActor != _originalActor)
            throw new UnauthorizedAccessException("The original Forms host actor or settings root changed.");
    }
    // Actor checks must not read settings: existing final admission invokes these under the settings lease.
    private sealed class OriginalActor(IAuthenticatedResourceActorSource actual, AuthenticatedResourceActor original, Func<bool> active)
        : IAuthenticatedResourceActorSource
    {
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            if (!active()) return null;
            var current = await actual.GetCurrentAsync(token).ConfigureAwait(false);
            return active() && current == original ? current : null;
        }
    }
    private sealed class OriginalIdentity(IResourceStoreIdentitySource actual, Guid original, Func<bool> active) : IResourceStoreIdentitySource
    {
        public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token)
        {
            if (!active()) throw new UnauthorizedAccessException("The Forms host session ended.");
            var current = await actual.GetStoreIdentityAsync(token).ConfigureAwait(false);
            if (!active() || current.SchemaVersion != 1 || current.StoreId != original)
                throw new UnauthorizedAccessException("The original Forms host settings root changed.");
            return current;
        }
    }
}
