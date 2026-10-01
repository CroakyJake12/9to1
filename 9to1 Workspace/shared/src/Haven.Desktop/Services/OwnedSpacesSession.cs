using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>One displayed Spaces session bound to the same authenticated actor for its lifetime.</summary>
internal sealed class OwnedSpacesSession
{
    private readonly SpaceLocalStoreAuthority _authority;
    private readonly AuthenticatedResourceActor _actor;

    private OwnedSpacesSession(IVersionedSettingsStore settings, SpaceLocalStoreAuthority authority,
        AuthenticatedResourceActor actor)
    {
        _authority = authority;
        _actor = actor;
        Registry = new SpaceRegistry(settings, token => authority.CaptureWriteAdmissionForActorAsync(actor, token));
    }

    public SpaceRegistry Registry { get; }

    public static async Task<OwnedSpacesSession> OpenAsync(IVersionedSettingsStore settings,
        SpaceLocalStoreAuthority authority, IAuthenticatedResourceActorSource actors, CancellationToken token)
    {
        var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Open Home to recover the current profile.");
        await authority.CaptureWriteAdmissionForActorAsync(actor, token).ConfigureAwait(false);
        return new(settings, authority, actor);
    }

    // Capturing admission only observes authority; it does not publish settings or grant access.
    // Registry mutations repeat this capture and perform their own final checks under the settings lease.
    public async Task RequireCurrentAccessAsync(CancellationToken token)
    {
        await _authority.CaptureWriteAdmissionForActorAsync(_actor, token).ConfigureAwait(false);
    }
}
