namespace HavenOS.Apps.Stacks.NativeUI;

/// <summary>Issued by the same authenticated Files/Home host. Listing a project grants no mutation or execution permission.</summary>
public interface IStackNativeProjectSource
{
    Task<IReadOnlyList<StackNativeProjectContext>> ListAuthorizedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Canonical engine plus an explicitly revalidated, host-issued read binding. No root path or actor is accepted from markup.</summary>
public sealed class StackNativeProjectContext
{
    private readonly StackActor _reader;
    private readonly Func<CancellationToken, Task<bool>> _isCurrent;
    public StackNativeProjectContext(Guid projectId, StackEngine engine, string authenticatedActorId,
        Func<CancellationToken, Task<bool>> isCurrent)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A canonical project identity is required.", nameof(projectId));
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedActorId);
        if (authenticatedActorId == StackActor.System.ActorId) throw new ArgumentException("A verified user binding is required.", nameof(authenticatedActorId));
        ProjectId = projectId; Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        _reader = new(authenticatedActorId, new HashSet<StackCapability> { StackCapability.ViewSource });
    }
    public Guid ProjectId { get; }
    internal StackEngine Engine { get; }
    internal StackActor Reader => _reader;
    internal async Task<string> DemandCurrentAsync(CancellationToken ct)
    {
        if (!await _isCurrent(ct).ConfigureAwait(false)) throw new UnauthorizedAccessException("The current Files/Home project binding is unavailable.");
        if ((await Engine.OpenProjectAsync(ct).ConfigureAwait(false)).ProjectId != ProjectId)
            throw new UnauthorizedAccessException("The canonical Stack project identity changed.");
        return Engine.LoadedRevisionToken ?? throw new UnauthorizedAccessException("A canonical revision token is unavailable.");
    }
}

public sealed class UnavailableStackNativeProjectSource : IStackNativeProjectSource
{
    public Task<IReadOnlyList<StackNativeProjectContext>> ListAuthorizedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StackNativeProjectContext>>([]);
}
