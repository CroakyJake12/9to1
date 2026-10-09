using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Compatibility/read-currentness observation on the platform-supplied original
/// Home connection and Task profile. This is not a rendered-frame, installation, resource,
/// model, tool or Task-command grant. Root owns the borrowed startup and actor services.</summary>
internal sealed partial class NativeCanonicalTaskSceneReadiness : ICuiSceneReadiness,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
{
    private readonly SpaceTaskWidgetPage _page;
    private readonly CuiSceneHost _host;
    private readonly SpaceTaskWorkspaceService _source;
    private readonly IHomeNativeStartupSession? _startup;
    private readonly HomeNativeWindowsComposition? _sameProcessHome;
    private readonly IServiceProvider? _sameProcessProvider;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly CancellationToken _connectionLifetime, _windowLifetime;
    // Borrowed startup/actor services are not disposed here. The same factor joins
    // every admitted check; this route has no additional stop/cleanup producer.
    private readonly DesktopOriginalWorkLifetime _work = new(
        static () => Task.CompletedTask, static () => Task.CompletedTask);
    private readonly object _gate = new();
    [ThreadStatic] private static List<NativeCanonicalTaskSceneReadiness>? _actualSourceCallbacks;
    private readonly Guid _spaceId, _contextId;
    private readonly Guid? _taskId, _runId;
    private AuthenticatedResourceActor? _originalActor;

    private NativeCanonicalTaskSceneReadiness(SpaceTaskWidgetPage actualPage,
        IHomeNativeStartupSession? originalStartup, IAuthenticatedResourceActorSource originalTaskActors,
        CancellationToken originalConnectionLifetime, CancellationToken originalWindowLifetime)
    {
        _page = actualPage; _host = actualPage.OriginalHost;
        _source = actualPage.OriginalTaskSource;
        (_spaceId, _contextId, _taskId, _runId) = actualPage.OriginalReadinessContext;
        _startup = originalStartup; _actors = originalTaskActors;
        _connectionLifetime = originalConnectionLifetime; _windowLifetime = originalWindowLifetime;
    }

    internal static NativeCanonicalTaskSceneReadiness BindOriginal(SpaceTaskWidgetPage actualPage,
        IHomeNativeStartupSession? originalStartup, IAuthenticatedResourceActorSource originalTaskActors,
        CancellationToken originalConnectionLifetime, CancellationToken originalWindowLifetime)
    {
        ArgumentNullException.ThrowIfNull(actualPage);
        ArgumentNullException.ThrowIfNull(originalTaskActors);
        if (!originalWindowLifetime.CanBeCanceled || originalStartup is not null && !originalConnectionLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual owning window and original attached connection lifetimes.");
        originalWindowLifetime.ThrowIfCancellationRequested();
        if (originalStartup is not null) originalConnectionLifetime.ThrowIfCancellationRequested();
        // No source callbacks or checks during construction. Page captures this actual
        // child before any later publication/currentness check or native activation.
        return new(actualPage, originalStartup, originalTaskActors, originalConnectionLifetime, originalWindowLifetime);
    }
    internal static NativeCanonicalTaskSceneReadiness BindOriginalSameProcess(SpaceTaskWidgetPage actualPage,
        IServiceProvider originalProvider, HomeNativeWindowsComposition sameHome,
        IAuthenticatedResourceActorSource originalTaskActors, CancellationToken originalAppLifetime,
        CancellationToken originalWindowLifetime)
    {
        ArgumentNullException.ThrowIfNull(actualPage); ArgumentNullException.ThrowIfNull(originalTaskActors);
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalProvider, sameHome);
        // Canonical Task authority keeps its original host actor; Home is observed separately.
        if (!ReferenceEquals(originalProvider.GetService(typeof(Haven.Infrastructure.HostLocalTaskActorSource)), originalTaskActors))
            throw new UnauthorizedAccessException("Retain the actual configured canonical Task actor source.");
        if (!originalAppLifetime.CanBeCanceled || !originalWindowLifetime.CanBeCanceled)
            throw new ArgumentException("Borrow the actual App and original native window work lifetimes.");
        originalAppLifetime.ThrowIfCancellationRequested(); originalWindowLifetime.ThrowIfCancellationRequested();
        return new(actualPage, originalProvider, sameHome, originalTaskActors,
            originalAppLifetime, originalWindowLifetime);
    }

    private NativeCanonicalTaskSceneReadiness(SpaceTaskWidgetPage actualPage, IServiceProvider originalProvider,
        HomeNativeWindowsComposition sameHome, IAuthenticatedResourceActorSource originalTaskActors,
        CancellationToken originalAppLifetime, CancellationToken originalWindowLifetime)
        : this(actualPage, null, originalTaskActors, originalAppLifetime, originalWindowLifetime)
    { _sameProcessProvider = originalProvider; _sameProcessHome = sameHome; }

    internal bool IsBoundToOriginalPage(SpaceTaskWidgetPage actual) => ReferenceEquals(_page, actual) &&
        ReferenceEquals(_host, actual.OriginalHost) && ReferenceEquals(_source, actual.OriginalTaskSource);

    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
        new(_work.RunAsync(original => CheckOriginalAsync(original, cancellationToken)));

    private async Task<CuiSceneAvailability> CheckOriginalAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        var generation = _page.OriginalReadinessGeneration;
        original.BindPublicationGuard(() => _page.IsOriginalReadinessPresentationCurrent(_host, generation));
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, caller, _windowLifetime, _connectionLifetime);
        var token = scope.Token;
        DemandOriginal(original, token);
        if (_sameProcessHome is not null)
            return await CheckSameProcessOriginalAsync(original, token).ConfigureAwait(false);
        if (_startup is null)
            return new(CuiSceneAvailabilityState.Unavailable, "HomeStartupAttachmentUnavailable",
                "Connect to the required original Home service. No installation or Task authority is inferred from this unavailable attachment.");
        var actor = await ReadActualActorAsync(original, token);
        if (actor is null) return new(CuiSceneAvailabilityState.Unavailable, "TaskProfileUnavailable", "The original current Task profile is unavailable.");
        lock (_gate)
        {
            if (_originalActor is null) _originalActor = actor;
            else if (_originalActor != actor) throw new UnauthorizedAccessException("The original native Task profile/authentication changed.");
        }
        var before = await ReadActualContextAsync(original, token);
        DemandActorBinding(before, actor);
        var home = await original.AwaitAsync(AcquireActualSource(original, () => _startup.CheckAsync(token))).ConfigureAwait(false);
        DemandOriginal(original, token);
        if (await ReadActualActorAsync(original, token) != actor)
            throw new UnauthorizedAccessException("The original Task actor changed during Home compatibility observation.");
        var after = await ReadActualContextAsync(original, token);
        DemandActorBinding(after, actor);
        if (before.SpaceRevision != after.SpaceRevision || before.Snapshot?.OwnerBinding != after.Snapshot?.OwnerBinding)
            throw new InvalidOperationException("The original Task context or owner changed during native compatibility observation.");
        if (await ReadActualActorAsync(original, token) != actor)
            throw new UnauthorizedAccessException("The original Task actor changed before readiness publication.");
        DemandOriginal(original, token);
        if (!Enum.IsDefined(home.State) || string.IsNullOrWhiteSpace(home.Code) || string.IsNullOrWhiteSpace(home.Message))
            throw new InvalidDataException("The original startup supplied invalid compatibility observation metadata.");
        return new(home.CanStartNormally ? CuiSceneAvailabilityState.Ready : CuiSceneAvailabilityState.Unavailable,
            home.Code, home.Message); // SAME actual compatibility observation, never a frame/permission witness.
    }
    private async Task<CuiSceneAvailability> CheckSameProcessOriginalAsync(DesktopOriginalWorkLifetime.Original original,
        CancellationToken token)
    {
        var actor = await ReadActualActorAsync(original, token);
        if (actor is null) return new(CuiSceneAvailabilityState.Unavailable, "TaskProfileUnavailable", "The original current Task profile is unavailable.");
        lock (_gate)
        {
            if (_originalActor is null) _originalActor = actor;
            else if (_originalActor != actor) throw new UnauthorizedAccessException("The original canonical Task profile/authentication changed.");
        }
        var before = await ReadActualContextAsync(original, token);
        DemandActorBinding(before, actor);
        void OwnSource(Action body) => AcquireActualSource(original, () => { DemandOriginal(original, token); body(); return true; });
        var home = await original.AwaitAsync(AcquireActualSource(original, () =>
            WindowsHomeSameProcessRuntimeObservation.CheckAsync(_sameProcessProvider!, _sameProcessHome!,
                _connectionLifetime, _windowLifetime, OwnSource, () => DemandOriginal(original, token), token))).ConfigureAwait(false);
        DemandOriginal(original, token);
        if (await ReadActualActorAsync(original, token) != actor)
            throw new UnauthorizedAccessException("The canonical Task actor changed during same-process Home observation.");
        var after = await ReadActualContextAsync(original, token);
        DemandActorBinding(after, actor);
        if (before.SpaceRevision != after.SpaceRevision || before.Snapshot?.OwnerBinding != after.Snapshot?.OwnerBinding)
            throw new InvalidOperationException("The SAME original Task context/owner changed during Home observation.");
        if (await ReadActualActorAsync(original, token) != actor)
            throw new UnauthorizedAccessException("The canonical Task actor changed before native publication.");
        DemandOriginal(original, token);
        var final = AcquireActualSource(original, () => WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(
            _sameProcessProvider!, _sameProcessHome!, home));
        DemandOriginal(original, token);
        return final;
    }

    private async Task<AuthenticatedResourceActor?> ReadActualActorAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        DemandOriginal(original, token);
        var actual = AcquireActualSource(original, () => _actors.GetCurrentAsync(token).AsTask()); // SAME ValueTask converted once.
        var actor = await original.AwaitAsync(actual).ConfigureAwait(false);
        DemandOriginal(original, token);
        if (actor is not null && (string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision))) throw new InvalidDataException("The current Task actor has no real profile/authentication binding.");
        return actor;
    }
    private async Task<SpaceTaskObservation> ReadActualContextAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        DemandOriginal(original, token);
        void OwnRawSource(Action callback) => AcquireActualSource(original, () =>
        { DemandOriginal(original, token); callback(); return true; });
        var actual = AcquireActualSource(original, () => _source.ReadAsync(_spaceId, _contextId, token, OwnRawSource));
        var current = await original.AwaitAsync(actual).ConfigureAwait(false);
        DemandOriginal(original, token);
        if (current.SpaceId != _spaceId || current.Conversation.Id != _contextId || current.Conversation.Mode != HavenMode.Tasks ||
            current.Snapshot?.TaskId != _taskId || current.Snapshot?.ExecutionId != _runId)
            throw new InvalidOperationException("A different original Task/Run/context now occupies the native view.");
        return current;
    }
    private static void DemandActorBinding(SpaceTaskObservation observation, AuthenticatedResourceActor actor)
    {
        // Historical/unstarted records without owner provenance remain observation only.
        // Compare the saved stable owner identity, as the maintained portable reopening owner does.
        // Historical AuthenticationRevision remains audit provenance. Fresh actor observations
        // still compare the complete current actor around Home/context reads; no command is granted.
        if (observation.Snapshot?.OwnerBinding is { } owner &&
            (owner.ActorId != actor.ActorId || owner.ProfileId != actor.ProfileId || owner.AccountId != actor.AccountId ||
             owner.OrganisationId != actor.OrganisationId))
            throw new UnauthorizedAccessException("The recorded Task owner differs from the actual current Task actor.");
    }
    private void DemandOriginal(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); original.DemandPublication();
        if (!IsBoundToOriginalPage(_page)) throw new InvalidOperationException("The original Task native host was replaced.");
        token.ThrowIfCancellationRequested(); original.DemandPublication();
    }
    private T AcquireActualSource<T>(DesktopOriginalWorkLifetime.Original original, Func<T> callback)
    {
        var stack = _actualSourceCallbacks ??= []; stack.Add(this);
        try { return callback(); }
        catch (Exception error)
        {
            original.Retain(error);
            if (error is OperationCanceledException) throw new AggregateException("Original native readiness source faulted synchronously.", error);
            throw;
        }
        finally { stack.RemoveAt(stack.Count - 1); }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_actualSourceCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual native Task readiness callback cannot join its encompassing retirement.");
        _work.DemandExternalClose();
    }
    // The page's private stop driver captures this published child driver after its
    // request. It does not expose an external self-join to an admitted callback.
    internal Task RequestOriginalRetirementTask()
    {
        _work.RequestRetirement();
        return _work.OriginalClose ?? throw new InvalidOperationException("No original readiness retirement was published.");
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
