using Haven.Application;

namespace HavenOS.Apps.Terminal;

/// <summary>Guards creation of the actual supplied PTY factory. It returns the original session,
/// not a parallel shell or an imitation session. The surface discovers its mandatory admission contract.</summary>
public sealed class HomeBoundTerminalSessionFactory(ITerminalInteractiveSessionFactory actualFactory,
    TerminalLocalEnvironmentAuthority authority, TerminalLocalEnvironmentLease lease) : ITerminalInteractiveSessionFactory, ITerminalSessionAdmission
{
    public IReadOnlyList<TerminalEnvironmentDescriptor> ListEnvironments()
    {
        RequireCurrent();
        return actualFactory.ListEnvironments().Where(item => item.Id == lease.EnvironmentId).ToArray();
    }
    public IReadOnlyList<TerminalShellProfile> ListShellProfiles(TerminalEnvironmentId? environmentId = null)
    {
        RequireCurrent();
        if (environmentId is { } requested && requested != lease.EnvironmentId) return [];
        return actualFactory.ListShellProfiles(lease.EnvironmentId);
    }
    public ITerminalSession Create(string initialDirectory, string? displayName = null)
    {
        RequireCurrent();
        var profile = actualFactory.ListShellProfiles(lease.EnvironmentId)
            .FirstOrDefault(item => item.State == TerminalEnvironmentConnectionState.Ready)
            ?? throw new InvalidOperationException("The owned Terminal environment has no available shell profile.");
        return CreateAsync(new(lease.EnvironmentId, profile.Id, initialDirectory, Title: displayName))
            .GetAwaiter().GetResult();
    }
    public ITerminalInteractiveSession Create(TerminalSessionStartRequest request)
        => CreateAsync(request).GetAwaiter().GetResult();
    public async Task<ITerminalInteractiveSession> CreateAsync(TerminalSessionStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EnvironmentId != lease.EnvironmentId)
            throw new UnauthorizedAccessException("The request targets a different Terminal environment.");
        await authority.RequireCurrentAsync(lease, ct).ConfigureAwait(false);
        var session = await actualFactory.CreateAsync(request, ct).ConfigureAwait(false);
        try
        {
            await RequireSessionAsync(session.Metadata, ct).ConfigureAwait(false);
            return session;
        }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public ValueTask RequireSessionAsync(TerminalSessionMetadata session, CancellationToken ct)
    {
        if (session.EnvironmentId != lease.EnvironmentId)
            throw new UnauthorizedAccessException("The session is not in the owned local Terminal environment.");
        return authority.RequireCurrentAsync(lease, ct);
    }
    private void RequireCurrent() => authority.RequireCurrentAsync(lease).AsTask().GetAwaiter().GetResult();
}
