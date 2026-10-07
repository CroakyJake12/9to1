using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

internal sealed partial class NativeCanonicalTaskSceneReadiness
{
    /// <summary>Issue a compatibility/read-currentness child for the exact acquired Dev
    /// page before its constructor publishes content. This transfers no actor/command/
    /// model authority, and does not borrow the originating widget's active host.</summary>
    internal ICuiSceneReadiness BindOriginalDevelopment(DeveloperProjectWorkbenchPage actualPage)
    {
        ArgumentNullException.ThrowIfNull(actualPage);
        _work.DemandAdmission();
        var originGeneration = _page.OriginalReadinessGeneration;
        if (!_page.IsOriginalReadinessPresentationCurrent(_host, originGeneration) || !IsBoundToOriginalPage(_page))
            throw new InvalidOperationException("The SAME originating Task page is no longer current for Dev child acquisition.");
        var context = actualPage.OriginalReadinessContext;
        if (context.TaskId != _taskId || context.ExecutionId != _runId || context.ContextId != _contextId)
            throw new InvalidOperationException("The actual acquired Dev page belongs to a different original Task/Run/context.");
        _windowLifetime.ThrowIfCancellationRequested();
        if (_startup is not null || _sameProcessHome is not null) _connectionLifetime.ThrowIfCancellationRequested();
        return new NativeCanonicalDevelopmentSceneReadiness(this, actualPage);
    }

    // Only this privately retained original Task binder constructs the child. Its
    // currentness follows the actual Dev host after tab handoff; borrowed window/
    // connection/actor/context sources remain the SAME original objects.
    private sealed class NativeCanonicalDevelopmentSceneReadiness : ICuiSceneReadiness,
        IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
    {
        private readonly DeveloperProjectWorkbenchPage _page;
        private readonly CuiSceneHost _host;
        private readonly SpaceTaskWorkspaceService _source;
        private readonly IHomeNativeStartupSession? _startup;
        private readonly HomeNativeWindowsComposition? _sameProcessHome;
        private readonly IServiceProvider? _sameProcessProvider;
        private readonly IAuthenticatedResourceActorSource _actors;
        private readonly CancellationToken _connectionLifetime, _windowLifetime;
        private readonly DesktopOriginalWorkLifetime _work = new(
            static () => Task.CompletedTask, static () => Task.CompletedTask);
        private readonly object _gate = new();
        [ThreadStatic] private static List<NativeCanonicalDevelopmentSceneReadiness>? _actualSourceCallbacks;
        private readonly Guid _spaceId, _contextId;
        private readonly Guid? _taskId, _runId;
        private AuthenticatedResourceActor? _originalActor;

        internal NativeCanonicalDevelopmentSceneReadiness(NativeCanonicalTaskSceneReadiness actualIssuer,
            DeveloperProjectWorkbenchPage actualPage)
        {
            _page = actualPage; _host = actualPage.OriginalHost;
            _source = actualIssuer._source;
            _startup = actualIssuer._startup; _actors = actualIssuer._actors;
            _sameProcessHome = actualIssuer._sameProcessHome; _sameProcessProvider = actualIssuer._sameProcessProvider;
            _connectionLifetime = actualIssuer._connectionLifetime; _windowLifetime = actualIssuer._windowLifetime;
            (_spaceId, _contextId, _taskId, _runId) =
                (actualIssuer._spaceId, actualIssuer._contextId, actualIssuer._taskId, actualIssuer._runId);
            lock (actualIssuer._gate) _originalActor = actualIssuer._originalActor;
            // No callbacks, readiness check, frame acquisition or captured generation0.
        }

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
            if (!ReferenceEquals(_host, _page.OriginalHost)) throw new InvalidOperationException("The original Dev native host was replaced.");
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
                throw new InvalidOperationException("An actual native Dev readiness callback cannot join its encompassing retirement.");
            _work.DemandExternalClose();
        }
        public void RequestRetirement() => _work.RequestRetirement();
        public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    }
}
