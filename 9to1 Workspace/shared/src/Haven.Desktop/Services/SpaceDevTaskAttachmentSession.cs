using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Services;

/// <summary>A portable local Home-domain observer over the configured canonical owners.
/// This creates neither a Task/Run nor an installed/native session. Saved references and
/// returned observations convey no workspace, command, model or effect permission.</summary>
public sealed class SpaceDevTaskAttachmentSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HomeLocalDomainComposition _domain;
    private readonly SpaceRegistry _spaces;
    private readonly IConversationRepository _conversations;
    private readonly TaskExecutionCoordinator _canonical;
    private readonly DeveloperTaskWorkspaceService _developer;
    private readonly TaskRunPermissionAuthority _authority;
    private readonly HostLocalTaskActorSource _taskActors;
    private readonly SpaceTaskWorkspaceService _tasks;
    private readonly SpaceDevelopmentWorkspace _development;
    private readonly DesktopOriginalWorkLifetime _originals;
    private readonly List<Work> _active = [];
    private readonly List<SpaceOriginalRunResumeObservation> _observations = [];
    private int _observationReservations;
    [ThreadStatic] private static List<SpaceDevTaskAttachmentSession>? _physical;

    private sealed class Work(DesktopOriginalWorkLifetime.Original original)
    {
        internal readonly DesktopOriginalWorkLifetime.Original Original = original;
        internal readonly HashSet<Task> Sources = new(ReferenceEqualityComparer.Instance);
        internal volatile bool DomainStartPending;
        internal volatile bool CanonicalSourceEntered;
        internal volatile bool Live = true;
    }

    private SpaceDevTaskAttachmentSession(HomeLocalDomainComposition domain, SpaceRegistry spaces,
        IConversationRepository conversations, TaskExecutionCoordinator canonical,
        DeveloperTaskWorkspaceService developer, TaskRunPermissionAuthority authority,
        HostLocalTaskActorSource taskActors)
    {
        (_domain, _spaces, _conversations, _canonical, _developer) =
            (domain, spaces, conversations, canonical, developer);
        (_authority, _taskActors) = (authority, taskActors);
        _tasks = new(spaces, conversations, canonical);
        _development = new(spaces, _tasks, developer);
        _originals = new(StopOriginalObservationsAsync, JoinOriginalObservationsAsync);
    }

    /// <summary>Resolves the actual configured singleton graph. No substitute store,
    /// coordinator, actor, trust service, project or provider is constructed here.</summary>
    public static SpaceDevTaskAttachmentSession CreateOriginal(
        HomeLocalDomainComposition originalDomain, IServiceProvider originalConfiguredProvider)
    {
        ArgumentNullException.ThrowIfNull(originalDomain);
        ArgumentNullException.ThrowIfNull(originalConfiguredProvider);
        T Require<T>() where T : class => originalConfiguredProvider.GetService(typeof(T)) as T
            ?? throw new InvalidOperationException($"The configured portable owner {typeof(T).Name} is unavailable.");
        if (!ReferenceEquals(Require<HomeLocalDomainComposition>(), originalDomain) ||
            !originalDomain.IsBoundToOriginalComposition(Require<FileHomeCoreStateStore>(),
                Require<HomeLocalProfileIdentity>(), Require<HomePermissionTrustService>(),
                Require<ResourceAuthorizationService>(), Require<HomeResourceStoreOwnershipAuthority>(),
                Require<HomeResourceOperationBroker>()) ||
            !ReferenceEquals(Require<IAuthenticatedResourceActorSource>(), originalDomain.Profiles) ||
            !ReferenceEquals(Require<IHomeCoreStateStore>(), originalDomain.StateStore) ||
            !ReferenceEquals(Require<HomeLocalStoreOwnership>(), originalDomain.LocalStoreOwnership) ||
            !ReferenceEquals(Require<IResourceStoreOwnershipAuthority>(), originalDomain.Ownership) ||
            !ReferenceEquals(Require<IResourceStoreOwnershipReceiptAuthority>(), originalDomain.Ownership))
            throw new InvalidOperationException("The configured provider does not contain the SAME original Home domain.");
        var canonical = Require<TaskExecutionCoordinator>();
        var developer = Require<DeveloperTaskWorkspaceService>();
        var authority = Require<TaskRunPermissionAuthority>();
        var taskActors = Require<HostLocalTaskActorSource>();
        if (!ReferenceEquals(Require<ITaskRunAdmissionAuthority>(), authority) ||
            !canonical.HasOriginalAdmissionAuthority(authority) || !authority.HasOriginalTaskActorSource(taskActors))
            throw new InvalidOperationException("The configured canonical owner does not retain the SAME original Task authority and actor source.");
        if (!developer.IsBoundToOriginalCanonicalOwner(canonical))
            throw new InvalidOperationException("The configured Dev service belongs to a different original canonical owner.");
        return new(originalDomain, Require<SpaceRegistry>(), Require<IConversationRepository>(), canonical, developer, authority, taskActors);
    }

    /// <summary>Callback-free reference identity only; no actor or execution grant.</summary>
    public bool HasOriginalComposition(HomeLocalDomainComposition domain, SpaceRegistry spaces,
        IConversationRepository conversations, TaskExecutionCoordinator canonical,
        DeveloperTaskWorkspaceService developer) => ReferenceEquals(_domain, domain) &&
        ReferenceEquals(_spaces, spaces) && ReferenceEquals(_conversations, conversations) &&
        ReferenceEquals(_canonical, canonical) && ReferenceEquals(_developer, developer);

    public Task StartOriginalAsync(CancellationToken observationCancellationToken = default) =>
        Run(async work =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                work.Original.Token, observationCancellationToken);
            await DemandStartedActorsAsync(work, linked.Token).ConfigureAwait(false);
            work.Original.DemandPublication();
            return true;
        });

    /// <summary>Reopens the actual saved reference and SAME Task/Run/project. The initial
    /// send producer is never invoked. This receipt is an observation, not a private run factory.</summary>
    public Task<SpaceDevTaskAttachment> OpenOriginalAsync(Guid spaceId, Guid existingContextReferenceId,
        Guid expectedTaskId, Guid expectedExecutionId, CancellationToken observationCancellationToken = default) =>
        Run(async work =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(work.Original.Token, observationCancellationToken);
            var actor = await DemandStartedActorsAsync(work, linked.Token).ConfigureAwait(false);
            work.CanonicalSourceEntered = true;
            var view = await Acquire(work, () => _development.OpenAsync(spaceId, existingContextReferenceId,
                linked.Token, callback => Scope(work, callback))).ConfigureAwait(false);
            DemandView(view, expectedTaskId, expectedExecutionId, actor);
            await DemandSameActorsAsync(work, actor, linked.Token).ConfigureAwait(false);
            work.Original.DemandPublication();
            return new SpaceDevTaskAttachment(this, view, actor);
        });

    /// <summary>Only the maintained Space reference CAS writes metadata. Its actual durable
    /// ACK/failure remains owned by SpaceDevelopmentWorkspace, including acknowledged-refresh failure.</summary>
    public Task<SpaceDevTaskAttachment> AttachOriginalAsync(Guid spaceId, long expectedSpaceRevision,
        Guid originalConversationId, Guid expectedTaskId, Guid expectedExecutionId,
        DeveloperProjectReference originalProject, CancellationToken commandCancellationToken = default) =>
        Run(async work =>
        {
            var actor = await DemandStartedActorsAsync(work, commandCancellationToken).ConfigureAwait(false);
            work.Original.DemandPublication();
            work.CanonicalSourceEntered = true;
            var task = await Acquire(work, () => _tasks.ReadAsync(spaceId, originalConversationId,
                commandCancellationToken, callback => Scope(work, callback))).ConfigureAwait(false);
            DemandSnapshot(task.Snapshot, originalConversationId, expectedTaskId, expectedExecutionId, actor);
            await DemandSameActorsAsync(work, actor, commandCancellationToken).ConfigureAwait(false);
            var view = await Acquire(work, () => _development.AttachAsync(spaceId, expectedSpaceRevision,
                originalConversationId, expectedTaskId, expectedExecutionId, originalProject,
                commandCancellationToken, callback => Scope(work, callback, allowRetiring: true)), allowRetiring: true).ConfigureAwait(false);
            try
            {
                DemandView(view, expectedTaskId, expectedExecutionId, actor);
                await DemandSameActorsAsync(work, actor, commandCancellationToken).ConfigureAwait(false);
            }
            catch (Exception cause)
            {
                // Actual metadata publication already ACKed. Retain its exact row even
                // when a later actor/currentness check refuses presentation.
                var saved = view.Space.ContextReferences!.Single(row => row.ContextId == view.ContextReferenceId);
                throw new SpaceDevelopmentAttachmentAcknowledgedException(view.Space, saved, cause);
            }
            return new SpaceDevTaskAttachment(this, view, actor);
        });

    /// <summary>The caller supplies the original action context and explicit business token.
    /// Session retirement never cancels the admitted Dev operation or disposes its owners.</summary>
    public Task<DeveloperOperationResult<DeveloperActionObservation>> ExecuteOriginalAsync(
        SpaceDevTaskAttachment originalAttachment, DeveloperCanonicalActionContext originalAction,
        SpaceDeveloperCommand command, CancellationToken originalBusinessCancellationToken = default) =>
        Run(async work =>
        {
            var current = await RefreshOriginalAsync(work, originalAttachment, originalBusinessCancellationToken).ConfigureAwait(false);
            work.Original.DemandPublication();
            return await Acquire(work, () => _development.ExecuteAsync(current, originalAction, command,
                originalBusinessCancellationToken, callback => Scope(work, callback, allowRetiring: true)), allowRetiring: true).ConfigureAwait(false);
        });

    public Task<TaskRunOriginalRunControlAvailability> GetOriginalRunControlAvailabilityAsync(
        SpaceDevTaskAttachment originalAttachment, CancellationToken observationCancellationToken = default) =>
        Run(async work =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(work.Original.Token, observationCancellationToken);
            var view = await RefreshOriginalAsync(work, originalAttachment, linked.Token).ConfigureAwait(false);
            return await Acquire(work, () => _canonical.GetOriginalRunControlAvailabilityAsync(
                view.Link.TaskId, view.Link.ExecutionId, linked.Token)).ConfigureAwait(false);
        });

    public Task<FollowUpDecision> SubmitFollowUpOriginalAsync(SpaceDevTaskAttachment originalAttachment,
        string instruction, TaskFollowUpMode mode, CancellationToken commandCancellationToken = default) =>
        Run(async work =>
        {
            var view = await RefreshOriginalAsync(work, originalAttachment, commandCancellationToken).ConfigureAwait(false);
            work.Original.DemandPublication();
            return await Acquire(work, () => _tasks.SubmitFollowUpAsync(view.Space.Id, view.Link.ConversationId,
                view.Link.TaskId, view.Link.ExecutionId, view.Task.Snapshot!.PersistenceRevision, instruction, mode,
                commandCancellationToken, callback => Scope(work, callback, allowRetiring: true)), allowRetiring: true).ConfigureAwait(false);
        });

    public Task<TaskRunOriginalRunControlResult> PauseOriginalRunAsync(SpaceDevTaskAttachment originalAttachment,
        CancellationToken commandCancellationToken = default) => ControlOriginalAsync(originalAttachment, false, commandCancellationToken);
    public Task<TaskRunOriginalRunControlResult> StopOriginalRunAsync(SpaceDevTaskAttachment originalAttachment,
        CancellationToken commandCancellationToken = default) => ControlOriginalAsync(originalAttachment, true, commandCancellationToken);
    private Task<TaskRunOriginalRunControlResult> ControlOriginalAsync(SpaceDevTaskAttachment attachment,
        bool stop, CancellationToken token) => Run(async work =>
        {
            var view = await RefreshOriginalAsync(work, attachment, token).ConfigureAwait(false);
            work.Original.DemandPublication();
            return await Acquire(work, () => stop
                ? _canonical.StopOriginalRunAsync(view.Link.TaskId, view.Link.ExecutionId, token)
                : _canonical.PauseOriginalRunAsync(view.Link.TaskId, view.Link.ExecutionId, token), allowRetiring: true).ConfigureAwait(false);
        });

    /// <summary>Uses only the genuine live-private canonical observed-resume producer.
    /// Invoked cold history remains unavailable here until its separate protected producer is configured.</summary>
    public Task<SpaceDevTaskResumeObservation> StartObservedOriginalResumeAsync(
        SpaceDevTaskAttachment originalAttachment, CancellationToken commandCancellationToken = default) =>
        Run(async work =>
        {
            var view = await RefreshOriginalAsync(work, originalAttachment, commandCancellationToken).ConfigureAwait(false);
            work.Original.DemandPublication();
            lock (_gate)
            {
                if (_observationReservations >= 128)
                    throw new InvalidOperationException("Portable resume observation capacity requires external session retirement.");
                _observationReservations++;
            }
            var child = new SpaceOriginalRunResumeObservation(_canonical, work.Original,
                view.Link.TaskId, view.Link.ExecutionId, view.Link.ConversationId,
                () => _canonical.StartObservedOriginalRunResumeAsync(view.Link.TaskId, view.Link.ExecutionId, commandCancellationToken),
                () => _originals.IsRetiring);
            lock (_gate) _observations.Add(child); // Exact child before any actual business factory gate opens.
            child.BeginOriginalAcquisition();
            return new SpaceDevTaskResumeObservation(this, child);
        });

    private async Task<SpaceDeveloperView> RefreshOriginalAsync(Work work, SpaceDevTaskAttachment attachment, CancellationToken token)
    {
        if (attachment is null || !ReferenceEquals(attachment.Issuer, this))
            throw new InvalidOperationException("The SAME portable session did not issue this attachment.");
        var actor = await DemandStartedActorsAsync(work, token).ConfigureAwait(false);
        if (actor != attachment.Actors) throw new UnauthorizedAccessException("An original Task or Home actor observation changed.");
        work.CanonicalSourceEntered = true;
        var current = await Acquire(work, () => _development.OpenAsync(attachment.View.Space.Id,
            attachment.View.ContextReferenceId, token, callback => Scope(work, callback))).ConfigureAwait(false);
        DemandView(current, attachment.View.Link.TaskId, attachment.View.Link.ExecutionId, actor);
        if (current.Link != attachment.View.Link || current.Project.Reference != attachment.View.Project.Reference ||
            current.Space.Revision != attachment.View.Space.Revision)
            throw new InvalidOperationException("The original saved Space/project reference changed; reopen before acting.");
        await DemandSameActorsAsync(work, actor, token).ConfigureAwait(false);
        return current;
    }

    internal sealed record ActorObservations(AuthenticatedResourceActor Home, AuthenticatedResourceActor Task);
    private async Task<ActorObservations> DemandStartedActorsAsync(Work work, CancellationToken token)
    {
        work.DomainStartPending = true;
        try { await Acquire(work, () => _domain.StartOriginalAsync()).ConfigureAwait(false); }
        finally { work.DomainStartPending = false; }
        _domain.DemandOriginalStarted();
        var home = _domain.GetOriginalStartedActor();
        await DemandSameHomeActorAsync(work, home, token).ConfigureAwait(false);
        var task = await ReadOriginalTaskActorAsync(work, token).ConfigureAwait(false);
        var observed = new ActorObservations(home, task);
        await DemandSameActorsAsync(work, observed, token).ConfigureAwait(false);
        return observed;
    }
    private async Task<AuthenticatedResourceActor> ReadOriginalTaskActorAsync(Work work, CancellationToken token)
    {
        if (!_canonical.HasOriginalAdmissionAuthority(_authority) || !_authority.HasOriginalTaskActorSource(_taskActors))
            throw new UnauthorizedAccessException("The original canonical Task actor composition changed.");
        return await Acquire(work, () => _authority.ObserveOriginalTaskActorAsync(
            callback => Scope(work, callback), task => Retain(work, task), token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The actual configured Task actor is unavailable.");
    }
    private async Task DemandSameActorsAsync(Work work, ActorObservations actors, CancellationToken token)
    {
        await DemandSameHomeActorAsync(work, actors.Home, token).ConfigureAwait(false);
        if (await ReadOriginalTaskActorAsync(work, token).ConfigureAwait(false) != actors.Task)
            throw new UnauthorizedAccessException("The actual configured Task actor changed during observation.");
    }
    private async Task DemandSameHomeActorAsync(Work work, AuthenticatedResourceActor actor, CancellationToken token)
    {
        var current = await Acquire(work, () => _domain.Profiles.GetCurrentAsync(
            callback => Scope(work, callback), task => Retain(work, task), token).AsTask()).ConfigureAwait(false);
        _domain.DemandOriginalStarted();
        if (current != actor) throw new UnauthorizedAccessException("The actual local Home profile changed during observation.");
    }
    private static void DemandView(SpaceDeveloperView view, Guid taskId, Guid runId, ActorObservations actors)
    {
        if (view.Link.TaskId != taskId || view.Link.ExecutionId != runId)
            throw new UnauthorizedAccessException("The saved link belongs to a different canonical Task/Run.");
        DemandSnapshot(view.Task.Snapshot, view.Link.ConversationId, taskId, runId, actors);
    }
    private static void DemandSnapshot(TaskExecutionSnapshot? snapshot, Guid conversationId,
        Guid taskId, Guid runId, ActorObservations actors)
    {
        var actor = actors.Task; // Task authority and Home resource identity remain separate namespaces.
        if (taskId == Guid.Empty || runId == Guid.Empty || snapshot is null || snapshot.TaskId != taskId ||
            snapshot.ExecutionId != runId || snapshot.ContextId != conversationId ||
            snapshot.OwnerBinding is not { } owner || owner.TaskId != taskId || owner.ExecutionId != runId ||
            owner.ContextId != snapshot.ContextId || owner.ActorId != actor.ActorId || owner.ProfileId != actor.ProfileId ||
            owner.AccountId != actor.AccountId || owner.OrganisationId != actor.OrganisationId)
            throw new UnauthorizedAccessException("The saved canonical Task/Run belongs to a different Task owner or context.");
        // Historical AuthenticationRevision is provenance, not current actor authority.
        // Only the configured command/action/cold issuer can authorize new business work.
    }

    private Task<T> Run<T>(Func<Work, Task<T>> body) => _originals.RunAsync(original => Drive(original, body));
    private async Task<T> Drive<T>(DesktopOriginalWorkLifetime.Original original, Func<Work, Task<T>> body)
    {
        var work = new Work(original);
        lock (_gate) _active.Add(work);
        T result = default!;
        Task<T>? actualBody = null;
        var failed = false;
        try
        {
            // The encompassing body is always joined, including a body that acquired
            // a raw source immediately before observer retirement. Its nested sources
            // separately enforce productive admission and publication boundaries.
            Scope(work, () => actualBody = body(work), allowRetiring: true);
            result = await actualBody!.ConfigureAwait(false);
        }
        catch (Exception cause) { original.Capture(actualBody, cause); failed = true; }
        // A post-callback scope fault cannot abandon an already returned raw original.
        Task[] sources;
        lock (_gate) sources = work.Sources.ToArray();
        foreach (var actual in sources)
            try { await original.AwaitAsync(actual).ConfigureAwait(false); }
            catch (Exception cause) { original.Capture(actual, cause); failed = true; }
        lock (_gate) { work.Live = false; _active.Remove(work); }
        if (failed) original.ThrowRetained();
        return result;
    }
    private async Task<T> Acquire<T>(Work work, Func<Task<T>> factory, bool allowRetiring = false)
    {
        Task<T>? actual = null;
        Exception? prefix = null;
        try { Scope(work, () => { actual = factory(); Retain(work, actual); }, allowRetiring); }
        catch (Exception cause) { prefix = cause; work.Original.Retain(cause); }
        if (actual is null)
        {
            var error = prefix ?? new InvalidOperationException("The original portable source returned no Task.");
            if (error is OperationCanceledException) throw new AggregateException("Original portable source acquisition failed.", error);
            ExceptionDispatchInfo.Capture(error).Throw();
        }
        var result = await work.Original.AwaitAsync(actual!).ConfigureAwait(false);
        if (prefix is not null) work.Original.ThrowRetained();
        return result;
    }
    private async Task Acquire(Work work, Func<Task> factory)
    {
        Task? actual = null;
        Exception? prefix = null;
        try { Scope(work, () => { actual = factory(); Retain(work, actual); }); }
        catch (Exception cause) { prefix = cause; work.Original.Retain(cause); }
        if (actual is null)
        {
            var error = prefix ?? new InvalidOperationException("The original portable source returned no Task.");
            if (error is OperationCanceledException) throw new AggregateException("Original portable source acquisition failed.", error);
            ExceptionDispatchInfo.Capture(error).Throw();
        }
        await work.Original.AwaitAsync(actual!).ConfigureAwait(false);
        if (prefix is not null) work.Original.ThrowRetained();
    }
    private void Retain(Work work, Task actual)
    { ArgumentNullException.ThrowIfNull(actual); lock (_gate) work.Sources.Add(actual); }
    private void Scope(Work work, Action callback, bool allowRetiring = false)
    {
        if (!work.Live) throw new InvalidOperationException("The original portable source scope is terminal.");
        if (!allowRetiring) work.Original.DemandPublication();
        var owners = _physical ??= [];
        owners.Add(this);
        try { callback(); }
        catch (OperationCanceledException cause)
        { throw new AggregateException("A synchronous portable source callback failed.", cause); }
        finally { owners.RemoveAt(owners.Count - 1); }
        if (!allowRetiring) work.Original.DemandPublication();
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physical?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("The original portable source callback must return before joining its session.");
        _originals.DemandExternalClose();
        Work[] pending;
        SpaceOriginalRunResumeObservation[] children;
        lock (_gate) { pending = _active.Where(work => work.Live).ToArray(); children = _observations.ToArray(); }
        if (pending.Any(work => work.DomainStartPending)) _domain.DemandExternalOriginalProcessJoin();
        if (pending.Any(work => work.CanonicalSourceEntered))
        {
            _canonical.DemandExternalOriginalProcessJoin();
            _development.DemandExternalOriginalRetirementJoin();
        }
        foreach (var child in children) child.DemandExternalClose(); // Includes late exact children before existing close return.
    }
    public void RequestOriginalObservationRetirement() => _originals.RequestRetirement();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return _originals.CloseAndDrainAsync();
    }
    private Task StopOriginalObservationsAsync()
    {
        SpaceOriginalRunResumeObservation[] children;
        lock (_gate) children = _observations.ToArray();
        var causes = new List<Exception>();
        foreach (var child in children)
            try { _originals.RunCloseCallback(child.RequestRetirement); }
            catch (Exception cause) { causes.Add(cause); }
        return JoinChildrenAsync(children, causes);
    }
    private Task JoinOriginalObservationsAsync()
    {
        SpaceOriginalRunResumeObservation[] children;
        lock (_gate) children = _observations.ToArray();
        return JoinChildrenAsync(children, []);
    }
    private async Task JoinChildrenAsync(SpaceOriginalRunResumeObservation[] children, List<Exception> causes)
    {
        var actualCloses = new List<Task>();
        foreach (var child in children)
            try { _originals.RunCloseCallback(() => actualCloses.Add(child.CloseAndDrainAsync())); }
            catch (Exception cause) { causes.Add(cause); }
        foreach (var actual in actualCloses)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group) causes.AddRange(group.InnerExceptions);
                else causes.Add(cause);
            }
        if (causes.Count != 0)
            throw new AggregateException("Original portable observer closes failed.", causes.Distinct<Exception>(ReferenceEqualityComparer.Instance));
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    public sealed class SpaceDevTaskAttachment
    {
        internal SpaceDevTaskAttachmentSession Issuer { get; }
        internal ActorObservations Actors { get; }
        internal SpaceDevTaskAttachment(SpaceDevTaskAttachmentSession issuer, SpaceDeveloperView view, ActorObservations actors)
        { Issuer = issuer; View = view; Actors = actors; }
        public SpaceDeveloperView View { get; }
    }
    public sealed class SpaceDevTaskResumeObservation
    {
        private readonly SpaceDevTaskAttachmentSession _issuer;
        private readonly SpaceOriginalRunResumeObservation _actual;
        internal SpaceDevTaskResumeObservation(SpaceDevTaskAttachmentSession issuer, SpaceOriginalRunResumeObservation actual)
        { _issuer = issuer; _actual = actual; }
        public Task<TaskRunOriginalResumeObservationResult?> WaitAsync()
        { _actual.DemandExternalClose(); return _actual.ActualObservation; }
        public void RequestOriginalObservationRetirement() => _actual.RequestRetirement();
        public Task DetachAndDrainAsync() => _actual.CloseAndDrainAsync();
        public bool IsIssuedBy(SpaceDevTaskAttachmentSession original) => ReferenceEquals(original, _issuer);
    }
}
