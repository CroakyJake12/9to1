using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>A supplied in-process composition over the original guarded Spaces owner.
/// It neither creates Home authority nor converts Core read access into a write decision.</summary>
public sealed class OwnedSpacesSession
{
    private readonly IResourceStoreIdentitySource _identities;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IResourceStoreOwnershipAuthority _ownership;

    private OwnedSpacesSession(IServiceProvider provider, IVersionedSettingsStore settings,
        IResourceStoreIdentitySource identities, IAuthenticatedResourceActorSource actors,
        IResourceStoreOwnershipAuthority ownership, SpaceLocalStoreAuthority authority)
    {
        Provider = provider;
        Settings = settings;
        Authority = authority;
        _identities = identities;
        _actors = actors;
        _ownership = ownership;
        Registry = new SpaceRegistry(settings, authority.CaptureWriteAdmissionAsync);
    }

    public IServiceProvider Provider { get; }
    public IVersionedSettingsStore Settings { get; }
    public SpaceLocalStoreAuthority Authority { get; }
    public SpaceRegistry Registry { get; }

    public static OwnedSpacesSession AttachOriginal(IServiceProvider originalProvider,
        IVersionedSettingsStore originalSettings)
    {
        ArgumentNullException.ThrowIfNull(originalProvider);
        ArgumentNullException.ThrowIfNull(originalSettings);
        var identities = originalProvider.GetService(typeof(IResourceStoreIdentitySource)) as IResourceStoreIdentitySource;
        var actors = originalProvider.GetService(typeof(IAuthenticatedResourceActorSource)) as IAuthenticatedResourceActorSource;
        var ownership = originalProvider.GetService(typeof(IResourceStoreOwnershipAuthority)) as IResourceStoreOwnershipAuthority;
        var authority = originalProvider.GetService(typeof(SpaceLocalStoreAuthority)) as SpaceLocalStoreAuthority;
        if (originalSettings is not IVersionedSettingsGuardedCompareExchange || identities is null ||
            !ReferenceEquals(originalProvider.GetService(typeof(IVersionedSettingsStore)), originalSettings) ||
            !ReferenceEquals(identities, originalSettings) || actors is null ||
            ownership is not IResourceStoreOwnershipReceiptAuthority || authority is null ||
            !authority.MatchesOriginalInputs(identities, actors, ownership))
            throw new UnauthorizedAccessException("The original guarded Spaces composition is unavailable.");
        var result = new OwnedSpacesSession(originalProvider, originalSettings, identities, actors, ownership, authority);
        result.RequireCurrent();
        return result;
    }

    public void RequireCurrent()
    {
        if (!ReferenceEquals(Provider.GetService(typeof(IVersionedSettingsStore)), Settings) ||
            !ReferenceEquals(Provider.GetService(typeof(IResourceStoreIdentitySource)), _identities) ||
            !ReferenceEquals(Provider.GetService(typeof(IAuthenticatedResourceActorSource)), _actors) ||
            !ReferenceEquals(Provider.GetService(typeof(IResourceStoreOwnershipAuthority)), _ownership) ||
            !ReferenceEquals(Provider.GetService(typeof(SpaceLocalStoreAuthority)), Authority) ||
            !Authority.MatchesOriginalInputs(_identities, _actors, _ownership))
            throw new UnauthorizedAccessException("The original Spaces composition changed.");
    }
}
