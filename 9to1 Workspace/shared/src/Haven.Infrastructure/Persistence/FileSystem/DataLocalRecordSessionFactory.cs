using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Actual local Data-session composition. The host passes its existing profile actor
/// source; this factory neither signs in nor turns another profile's files into local ownership.</summary>
public sealed class DataLocalRecordSessionFactory(DataWorkbookRepository repository, IAuthenticatedResourceActorSource actors)
{
    public async Task<DataRecordSession> CreateAsync(string profileID, string title, CancellationToken token = default,
        CancellationToken ownerLifetime = default)
    {
        var authority = await DataLocalWorkbookAuthority.BindAsync(repository, actors, profileID, token).ConfigureAwait(false);
        var session = new DataRecordSession(repository, authority, actors, ownerLifetime);
        try { await session.CreateAsync(DataRecordSession.NewRecordDatabase(title), token).ConfigureAwait(false); return session; }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public async Task<DataRecordSession> OpenAsync(string profileID, Guid workbookID, CancellationToken token = default,
        CancellationToken ownerLifetime = default)
    {
        var authority = await DataLocalWorkbookAuthority.BindAsync(repository, actors, profileID, token).ConfigureAwait(false);
        var reference = await authority.ResolveLocalAsync(workbookID, token).ConfigureAwait(false);
        var session = new DataRecordSession(repository, authority, actors, ownerLifetime);
        try { await session.OpenAsync(reference, token).ConfigureAwait(false); return session; }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
