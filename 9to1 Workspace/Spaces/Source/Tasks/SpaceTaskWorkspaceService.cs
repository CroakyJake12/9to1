using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Tasks;

/// <summary>
/// Reads and steers the existing canonical task in an existing Space conversation.
/// This service borrows profile-scoped repositories and the original coordinator;
/// it does not create tasks, conversations, attempts, permissions, or a task store.
/// The presentation host must retain the returned original and its own dispatch work.
/// Browser composition additionally requires its genuine authenticated context owner.
/// </summary>
public sealed class SpaceTaskWorkspaceService(
    SpaceRegistry spaces, IConversationRepository conversations, TaskExecutionCoordinator canonical)
{
    public bool HasConfiguredCommandOwner => canonical.HasCommandAuthority;
    public bool HasAuthorizedTaskCensus => canonical.HasTaskObservationCensus;
    private readonly ConditionalWeakTable<TaskContextSelection, object> _selections = new();

    /// <summary>Reads the same canonical store under its actual observation owner. The
    /// cursor is opaque and every candidate is freshly authorized before any context read.</summary>
    public async Task<AuthorizedTaskContextPage> ReadAllTasksPageAsync(int maximumCandidates,
        TaskObservationPageCursor? cursor, CancellationToken token,
        Action<Action>? originalSourceCallbackScope = null)
    {
        var page = await Join(() => canonical.ReadAuthorizedTaskPageAsync(maximumCandidates, cursor, token), originalSourceCallbackScope).ConfigureAwait(false);
        if (page.Disposition != TaskObservationDisposition.Authorized)
            throw new UnauthorizedAccessException("The actual Task observation owner is unavailable for this principal.");
        var rows = new List<AuthorizedTaskContextObservation>();
        foreach (var task in page.Tasks)
            rows.Add(await ReadAuthorizedContextAsync(task.TaskId, null, token, originalSourceCallbackScope).ConfigureAwait(false));
        return new(Array.AsReadOnly(rows.OrderByDescending(row => row.HasObservedLiveExecution)
            .ThenByDescending(row => row.Snapshot.UpdatedAt).ThenBy(row => row.Snapshot.TaskId).ToArray()),
            page.Next, page.HasUnavailableContexts);
    }

    /// <summary>Resolves the EXACT issued Task/Run, including an older task sharing a
    /// context. Never substitutes GetByContext's latest task or creates a conversation.</summary>
    public bool IsIssuedSelection(TaskContextSelection selection) => selection is not null &&
        ReferenceEquals(selection.Issuer, this) && _selections.TryGetValue(selection, out _);

    public Task<AuthorizedTaskContextObservation> ReadSelectionAsync(TaskContextSelection selection,
        CancellationToken token, Action<Action>? originalSourceCallbackScope = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!IsIssuedSelection(selection))
            throw new UnauthorizedAccessException("This actual task-context source did not issue the selection.");
        return ReadAuthorizedContextAsync(selection.TaskId, selection, token, originalSourceCallbackScope);
    }

    /// <summary>Fresh owner-authorized read of one actual Task, issuing its exact current
    /// selection without substituting a newer task sharing the same conversation.</summary>
    public Task<AuthorizedTaskContextObservation> ReadTaskAsync(Guid taskId, CancellationToken token,
        Action<Action>? originalSourceCallbackScope = null)
    {
        RequireId(taskId, nameof(taskId));
        return ReadAuthorizedContextAsync(taskId, null, token, originalSourceCallbackScope);
    }

    private async Task<AuthorizedTaskContextObservation> ReadAuthorizedContextAsync(Guid taskId,
        TaskContextSelection? selected, CancellationToken token, Action<Action>? scope)
    {
        var commandIdentity = false;
        async Task<TaskExecutionSnapshot> ReadTask()
        {
            var result = await Join(() => canonical.ReadAuthorizedTaskObservationAsync(taskId, token), scope).ConfigureAwait(false);
            if (result.Disposition == TaskObservationDisposition.Denied)
                throw new UnauthorizedAccessException("The current principal cannot observe this original task.");
            var task = result.Snapshot ?? throw new InvalidOperationException("The original authorized task is unavailable.");
            if (result.Disposition != TaskObservationDisposition.Authorized || task.TaskId != taskId)
                throw new InvalidDataException("The actual task owner returned an invalid observation.");
            if (selected is not null && (task.ExecutionId != selected.ExecutionId || task.ContextId != selected.ContextId || task.OwnerBinding != selected.OwnerBinding))
                throw new InvalidOperationException("The selected original Task/Run or owner changed; refresh the Tasks list.");
            commandIdentity = result.HasCurrentCommandIdentity;
            return task;
        }
        var before = await ReadTask().ConfigureAwait(false);
        var conversation = await Join(() => conversations.GetAsync(before.ContextId, token), scope).ConfigureAwait(false);
        if (conversation is not null && (conversation.Id != before.ContextId || conversation.SpaceId == Guid.Empty))
            throw new InvalidDataException("The actual task context returned a different identity or invalid origin.");
        var origin = conversation?.SpaceId;
        if (selected is not null && (selected.OriginSpaceId != origin || selected.HadConversation != (conversation is not null)))
            throw new InvalidOperationException("The original task context or Space membership changed; refresh the Tasks list.");
        SpaceDefinition? space = null;
        if (origin is { } spaceId)
        {
            space = await Join(() => spaces.ReadExistingAsync(spaceId, token), scope).ConfigureAwait(false);
            if (space is not null && space.Id != spaceId) throw new InvalidDataException("The task origin returned a different Space.");
        }
        var currentConversation = await Join(() => conversations.GetAsync(before.ContextId, token), scope).ConfigureAwait(false);
        if ((currentConversation is null) != (conversation is null) || currentConversation?.Id != conversation?.Id || currentConversation?.SpaceId != origin)
            throw new InvalidOperationException("The actual task context changed during observation.");
        var current = await ReadTask().ConfigureAwait(false); // Fresh actual principal after context reads.
        if (current.ExecutionId != before.ExecutionId || current.ContextId != before.ContextId || current.OwnerBinding != before.OwnerBinding)
            throw new InvalidOperationException("The original task owner changed during context observation.");
        TaskRunActivityObservation? activity = null;
        if (commandIdentity && current.State == TaskExecutionLifecycle.Running &&
            current.Attempts.LastOrDefault() is { State: TaskRunAttemptState.Admitted or TaskRunAttemptState.Running } attempt)
        {
            var admission = await Join(() => canonical.TryGetIssuedAttemptAsync(current.TaskId, current.ExecutionId, attempt.Id, token), scope).ConfigureAwait(false);
            if (admission is not null)
            {
                if (admission.Snapshot.TaskId != current.TaskId || admission.Snapshot.ExecutionId != current.ExecutionId ||
                    admission.Snapshot.ContextId != current.ContextId || admission.AttemptId != attempt.Id)
                    throw new InvalidDataException("A different actual task attempt was returned for this selection.");
                var observed = await Join(() => canonical.ObserveOriginalRunActivityAsync(current.TaskId, current.ExecutionId, token), scope).ConfigureAwait(false);
                if (observed.TaskId != current.TaskId || observed.ExecutionId != current.ExecutionId || observed.ContextId != current.ContextId)
                    throw new InvalidDataException("The actual activity witness belongs to a different original task.");
                current = await ReadTask().ConfigureAwait(false);
                if (observed.PersistenceRevision == current.PersistenceRevision) activity = observed;
            }
        }
        TaskRunOriginalTaskPermissionReview? permission = null;
        if (commandIdentity)
        {
            var observed = await Join(() => canonical.ReadOriginalTaskPermissionReviewAsync(current.TaskId, current.ExecutionId, token), scope).ConfigureAwait(false);
            if (observed?.CanonicalTaskContext is { } context && context.TaskId == current.TaskId &&
                context.ExecutionId == current.ExecutionId && context.ContextId == current.ContextId &&
                context.PersistenceRevision == current.PersistenceRevision) permission = observed;
        }
        var issued = selected ?? new TaskContextSelection(this, current.TaskId, current.ExecutionId, current.ContextId,
            origin, currentConversation is not null, current.OwnerBinding!);
        if (selected is null) _selections.Add(issued, new object());
        return new(issued, currentConversation, space?.Revision, space?.Name, current,
            TaskExecutionProjection.From(current)) { HasCurrentCommandIdentity = commandIdentity, Activity = activity, PermissionReview = permission };
    }

    public async Task<FollowUpDecision> SubmitSelectedFollowUpAsync(TaskContextSelection selection,
        long expectedPersistenceRevision, string instruction, TaskFollowUpMode mode, CancellationToken token,
        Action<Action>? originalSourceCallbackScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var current = await ReadSelectionAsync(selection, token, originalSourceCallbackScope).ConfigureAwait(false);
        if (current.Snapshot.PersistenceRevision != expectedPersistenceRevision)
            throw new InvalidOperationException("The original task changed before this follow-up; refresh first.");
        var reply = await Join(() => canonical.SubmitFollowUpAsync(selection.TaskId, instruction, mode, null, null, token), originalSourceCallbackScope).ConfigureAwait(false);
        if (reply.Snapshot.TaskId != selection.TaskId || reply.Snapshot.ExecutionId != selection.ExecutionId || reply.Snapshot.ContextId != selection.ContextId)
            throw new InvalidDataException("The actual follow-up belongs to a different Task/Run/context.");
        return reply;
    }

    /// <summary>The limit is explicit; the returned page is not a completeness claim.</summary>
    public Task<IReadOnlyList<SpaceTaskObservation>> ReadPageAsync(
        Guid spaceId, int maximumConversations = 40, CancellationToken cancellationToken = default) =>
        ReadPageAsync(spaceId, maximumConversations, cancellationToken, null);

    public async Task<IReadOnlyList<SpaceTaskObservation>> ReadPageAsync(
        Guid spaceId, int maximumConversations, CancellationToken cancellationToken,
        Action<Action>? originalSourceCallbackScope)
    {
        RequireId(spaceId, nameof(spaceId));
        if (maximumConversations is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumConversations));
        var space = await Join(() => spaces.ReadExistingAsync(spaceId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        DemandSpace(space, spaceId);
        var rows = await Join(() => conversations.GetBySpaceAsync(spaceId, maximumConversations, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        if (rows.Count > maximumConversations || rows.Any(row => row.SpaceId != spaceId) ||
            rows.Select(row => row.Id).Distinct().Count() != rows.Count)
            throw new InvalidDataException("The original Space conversation page violated its identity or bound.");
        var observations = new List<SpaceTaskObservation>();
        foreach (var row in rows.Where(IsTask).OrderByDescending(row => row.UpdatedAt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Re-read membership instead of granting access from the detached list.
            observations.Add(await ReadAsync(spaceId, row.Id, cancellationToken, originalSourceCallbackScope).ConfigureAwait(false));
        }
        // Opening/renaming a conversation cannot manufacture task activity or priority.
        // Until a current activity witness exists, use the actual task's recorded update.
        return Array.AsReadOnly(observations.OrderByDescending(row => row.HasObservedLiveExecution)
            .ThenByDescending(row => row.Snapshot?.UpdatedAt ?? row.Conversation.CreatedAt)
            .ThenBy(row => row.Conversation.Id).ToArray());
    }

    public Task<SpaceTaskObservation> ReadAsync(
        Guid spaceId, Guid originalConversationId, CancellationToken cancellationToken = default) =>
        ReadAsync(spaceId, originalConversationId, cancellationToken, null);

    public async Task<SpaceTaskObservation> ReadAsync(
        Guid spaceId, Guid originalConversationId, CancellationToken cancellationToken,
        Action<Action>? originalSourceCallbackScope)
    {
        RequireId(spaceId, nameof(spaceId));
        RequireId(originalConversationId, nameof(originalConversationId));
        var space = await Join(() => spaces.ReadExistingAsync(spaceId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        DemandSpace(space, spaceId);
        var conversation = await Join(() => conversations.GetAsync(originalConversationId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        DemandConversation(conversation, spaceId, originalConversationId);
        var snapshot = await Join(() => canonical.GetByContextAsync(originalConversationId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        DemandSnapshot(snapshot, originalConversationId);
        var originalRetained = false;
        if (snapshot?.Attempts.LastOrDefault() is { State: TaskRunAttemptState.Admitted or TaskRunAttemptState.Running } attempt)
        {
            var issued = await Join(() => canonical.TryGetIssuedAttemptAsync(snapshot.TaskId, snapshot.ExecutionId,
                attempt.Id, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
            if (issued is not null && (issued.Snapshot.TaskId != snapshot.TaskId ||
                issued.Snapshot.ContextId != originalConversationId || issued.Snapshot.ExecutionId != snapshot.ExecutionId ||
                issued.AttemptId != attempt.Id))
                throw new InvalidDataException("A different original task attempt was returned.");
            originalRetained = issued is not null;
        }
        TaskRunActivityObservation? activity = null;
        if (snapshot is { State: TaskExecutionLifecycle.Running } && originalRetained && canonical.HasCommandAuthority)
        {
            var actualActivity = await Join(() => canonical.ObserveOriginalRunActivityAsync(snapshot.TaskId,
                snapshot.ExecutionId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
            if (actualActivity.TaskId != snapshot.TaskId || actualActivity.ExecutionId != snapshot.ExecutionId ||
                actualActivity.ContextId != originalConversationId)
                throw new InvalidDataException("The actual live task observation belongs to another context or run.");
            // Never apply a newer/older live witness to a different displayed revision.
            if (actualActivity.PersistenceRevision == snapshot.PersistenceRevision) activity = actualActivity;
        }
        // Membership can change while the task read or issued-attempt read is pending.
        // These are observation checks, not a physical transaction or mutation grant.
        var currentConversation = await Join(() => conversations.GetAsync(originalConversationId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        DemandConversation(currentConversation, spaceId, originalConversationId);
        var currentSpace = await Join(() => spaces.ReadExistingAsync(spaceId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        DemandSpace(currentSpace, spaceId);
        if (currentSpace!.Revision != space!.Revision)
            throw new SpaceRevisionConflictException(spaceId, space.Revision, currentSpace.Revision);
        cancellationToken.ThrowIfCancellationRequested();
        // A saved owner binding is provenance, not current-principal authority. The
        // conversation-keyed path must ask the canonical observation owner afresh
        // before exposing an actionable permission request.
        var currentIdentity = false;
        if (snapshot is { } observedTask)
        {
            var authorized = await Join(() => canonical.ReadAuthorizedTaskObservationAsync(observedTask.TaskId,
                cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
            currentIdentity = authorized.Disposition == TaskObservationDisposition.Authorized &&
                authorized.HasCurrentCommandIdentity && authorized.Snapshot is { } currentTask &&
                currentTask.TaskId == observedTask.TaskId && currentTask.ExecutionId == observedTask.ExecutionId &&
                currentTask.ContextId == observedTask.ContextId && currentTask.OwnerBinding == observedTask.OwnerBinding &&
                currentTask.PersistenceRevision == observedTask.PersistenceRevision;
        }
        var permission = snapshot?.OwnerBinding is not null && currentIdentity && canonical.HasCommandAuthority
            ? await Join(() => canonical.ReadOriginalTaskPermissionReviewAsync(snapshot.TaskId, snapshot.ExecutionId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false)
            : null;
        return new(spaceId, currentSpace.Revision, currentConversation!, snapshot,
            snapshot is null ? null : TaskExecutionProjection.From(snapshot), originalRetained) { HasCurrentCommandIdentity = currentIdentity, Activity = activity,
                PermissionReview = permission?.CanonicalTaskContext.PersistenceRevision == snapshot?.PersistenceRevision ? permission : null };
    }

    public async Task<TaskRunOriginalTaskPermissionReview> ReadPermissionReviewAsync(ITaskContextObservation selected,
        CancellationToken token, Action<Action>? originalSourceCallbackScope = null)
    {
        var current = await ReadExactTaskContextAsync(selected, token, originalSourceCallbackScope).ConfigureAwait(false);
        return current.PermissionReview ?? throw new InvalidOperationException("This task has no current source-owned permission review.");
    }
    private async Task<ITaskContextObservation> ReadExactTaskContextAsync(ITaskContextObservation selected,
        CancellationToken token, Action<Action>? sourceScope)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ITaskContextObservation current = selected switch
        {
            AuthorizedTaskContextObservation issued => await ReadSelectionAsync(issued.Selection, token, sourceScope).ConfigureAwait(false),
            SpaceTaskObservation space => await ReadAsync(space.SpaceId, space.Conversation.Id, token, sourceScope).ConfigureAwait(false),
            _ => throw new UnauthorizedAccessException("An actual original task-context observation is required.")
        };
        if (current.ContextId != selected.ContextId || current.Snapshot?.TaskId != selected.Snapshot?.TaskId ||
            current.Snapshot?.ExecutionId != selected.Snapshot?.ExecutionId ||
            current.Snapshot?.PersistenceRevision != selected.Snapshot?.PersistenceRevision ||
            current.OriginSpaceId != selected.OriginSpaceId)
            throw new InvalidOperationException("The selected original Task/Run/context changed; refresh first.");
        return current;
    }
    public async Task<TaskRunOriginalTaskPermissionResponse> RespondPermissionReviewAsync(ITaskContextObservation selected,
        TaskRunOriginalTaskPermissionReview reviewed, bool approve, CancellationToken token,
        Action<Action>? originalSourceCallbackScope = null)
    {
        var current = await ReadExactTaskContextAsync(selected, token, originalSourceCallbackScope).ConfigureAwait(false);
        if (current.Snapshot is not { } task || task.TaskId != reviewed.CanonicalTaskContext.TaskId ||
            task.ExecutionId != reviewed.CanonicalTaskContext.ExecutionId || task.ContextId != reviewed.CanonicalTaskContext.ContextId ||
            task.PersistenceRevision != reviewed.CanonicalTaskContext.PersistenceRevision)
            throw new InvalidOperationException("The original task changed after this review; review again before responding.");
        return await Join(() => canonical.RespondOriginalTaskPermissionAsync(reviewed, approve, token), originalSourceCallbackScope).ConfigureAwait(false);
    }

    /// <summary>Queues or steers the same Task/Run. It does not resume or redispatch a provider.</summary>
    public Task<FollowUpDecision> SubmitFollowUpAsync(
        Guid spaceId, Guid originalConversationId, Guid expectedTaskId, Guid expectedExecutionId,
        long expectedPersistenceRevision, string instruction, TaskFollowUpMode mode,
        CancellationToken cancellationToken = default) => SubmitFollowUpAsync(spaceId, originalConversationId,
            expectedTaskId, expectedExecutionId, expectedPersistenceRevision, instruction, mode, cancellationToken, null);

    public async Task<FollowUpDecision> SubmitFollowUpAsync(
        Guid spaceId, Guid originalConversationId, Guid expectedTaskId, Guid expectedExecutionId,
        long expectedPersistenceRevision, string instruction, TaskFollowUpMode mode,
        CancellationToken cancellationToken, Action<Action>? originalSourceCallbackScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        RequireId(expectedTaskId, nameof(expectedTaskId));
        RequireId(expectedExecutionId, nameof(expectedExecutionId));
        var observation = await ReadAsync(spaceId, originalConversationId, cancellationToken, originalSourceCallbackScope).ConfigureAwait(false);
        var snapshot = observation.Snapshot ?? throw new InvalidOperationException("No canonical task exists for this conversation.");
        if (snapshot.TaskId != expectedTaskId || snapshot.ExecutionId != expectedExecutionId ||
            snapshot.PersistenceRevision != expectedPersistenceRevision)
            throw new InvalidOperationException("The original Task/Run observation changed; refresh before submitting this instruction.");
        if (snapshot.OwnerBinding is null || !canonical.HasCommandAuthority)
            throw new InvalidOperationException("The genuine current task-command owner is unavailable.");
        // The original coordinator performs fresh actor/command authorization and its
        // repository CAS. These saved identities and the configuration flag are no grant.
        var reply = await Join(() => canonical.SubmitFollowUpAsync(snapshot.TaskId, instruction, mode,
            null, null, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        if (reply.Snapshot.TaskId != expectedTaskId || reply.Snapshot.ContextId != originalConversationId ||
            reply.Snapshot.ExecutionId != expectedExecutionId)
            throw new InvalidDataException("The canonical reply belongs to a different Task/Run.");
        return reply; // Actual durable reply, before any presentation callback.
    }

    private static bool IsTask(Conversation row) => row.Mode == HavenMode.Tasks &&
        !row.IsArchived && row.Kind is not (ConversationKind.Call or ConversationKind.AutomationRun or ConversationKind.Training);
    private static void DemandSpace(SpaceDefinition? space, Guid id)
    {
        if (space is null || space.Id != id || space.IsArchived)
            throw new InvalidOperationException("The original active Space is unavailable.");
    }
    private static void DemandConversation(Conversation? row, Guid space, Guid context)
    {
        if (row is null || row.Id != context || row.SpaceId != space || !IsTask(row))
            throw new InvalidOperationException("This original Tasks conversation is not an active member of the Space.");
    }
    private static void DemandSnapshot(TaskExecutionSnapshot? snapshot, Guid context)
    {
        if (snapshot is not null && (snapshot.ContextId != context || snapshot.TaskId == Guid.Empty ||
            snapshot.ExecutionId == Guid.Empty || snapshot.PersistenceRevision < 0))
            throw new InvalidDataException("The canonical task observation has invalid or foreign identities.");
    }
    private static void RequireId(Guid value, string name)
    { if (value == Guid.Empty) throw new ArgumentException("An existing nonempty identity is required.", name); }
    private static async Task<T> Join<T>(Func<Task<T>> source, Action<Action>? originalSourceCallbackScope)
    {
        // Only a synchronous, in-process caller-owned custody scope. It conveys
        // no actor, permission, persistence ACK or business-completion authority.
        Task<T>? actual = null;
        Exception? acquisitionFailure = null;
        var callbackThread = Environment.CurrentManagedThreadId;
        var scopeOpen = 1;
        var acquisitions = 0;
        void AcquireOnce()
        {
            if (Environment.CurrentManagedThreadId != callbackThread || Volatile.Read(ref scopeOpen) == 0 ||
                Interlocked.CompareExchange(ref acquisitions, 1, 0) != 0)
                throw new InvalidOperationException("The original source scope must synchronously acquire its one actual Task.");
            actual = source();
        }
        try
        {
            if (originalSourceCallbackScope is null) AcquireOnce();
            else originalSourceCallbackScope(AcquireOnce);
        }
        catch (Exception error) { acquisitionFailure = error; }
        finally { Volatile.Write(ref scopeOpen, 0); }
        if (actual is null)
        {
            var error = acquisitionFailure ?? new InvalidOperationException("No original source Task was returned.");
            // A synchronous source-thrown OCE is a fault, not a canceled Task.
            if (error is OperationCanceledException) throw new AggregateException("Original source acquisition failed.", error);
            ExceptionDispatchInfo.Capture(error).Throw();
        }
        T result = default!;
        Exception? terminalFailure = null;
        try { result = await actual!.ConfigureAwait(false); }
        catch (Exception error) { terminalFailure = error; }
        if (acquisitionFailure is null)
        {
            if (terminalFailure is null) return result;
            if (actual!.Exception is { } group) throw group;
            ExceptionDispatchInfo.Capture(terminalFailure).Throw(); // Actual canceled Task remains canceled.
        }
        // The scope may fail AFTER the callback captured a real Task. Always
        // independently join that SAME Task before exposing acquisition failure.
        var causes = new List<Exception> { acquisitionFailure! };
        if (actual!.Exception is { InnerExceptions.Count: > 0 } originalGroup)
            foreach (var cause in originalGroup.InnerExceptions)
                if (!causes.Any(value => ReferenceEquals(value, cause))) causes.Add(cause);
        else if (actual.Exception is { } opaqueGroup) causes.Add(opaqueGroup);
        else if (terminalFailure is not null && !causes.Any(value => ReferenceEquals(value, terminalFailure))) causes.Add(terminalFailure);
        throw new AggregateException("Original source acquisition and independent terminal join failed.", causes);
    }

}

/// <summary>A detached read observation. OriginalAttemptRetained is not activity or command authority.</summary>
public sealed record SpaceTaskObservation(
    Guid SpaceId, long SpaceRevision, Conversation Conversation, TaskExecutionSnapshot? Snapshot,
    TaskExecutionProjection? Projection, bool OriginalAttemptRetained) : ITaskContextObservation
{
    // This observation is positive only when the canonical observation owner freshly
    // authorized the current principal for this exact Task/Run/context/revision. A
    // saved owner binding is provenance, not current identity or command authority.
    public bool HasCurrentCommandIdentity { get; init; }
    public Guid ContextId => Conversation.Id;
    public Guid? OriginSpaceId => SpaceId;
    public long? OriginSpaceRevision => SpaceRevision;
    public Conversation? OriginalConversation => Conversation;
    public string Title => Conversation.Title;
    public TaskRunOriginalTaskPermissionReview? PermissionReview { get; init; }
    public TaskRunActivityObservation? Activity { get; init; }
    public bool HasObservedLiveExecution => Activity is { IsLive: true } live && Snapshot is { State: TaskExecutionLifecycle.Running } task &&
        live.TaskId == task.TaskId && live.ExecutionId == task.ExecutionId && live.ContextId == Conversation.Id &&
        live.PersistenceRevision == task.PersistenceRevision;
    public string ExecutionObservation => Snapshot is null ? "No canonical task has been started for this conversation."
        : HasObservedLiveExecution ? $"Live original execution observed at {Activity!.ObservedAt:O}; recorded task: {Snapshot.State}."
        : Snapshot.Attempts.Count == 0 ? $"Saved task: {Snapshot.State}. No provider attempt is recorded."
        : OriginalAttemptRetained ? $"Saved task: {Snapshot.State}. Original attempt admission is retained; this is not a live activity witness."
        : $"Saved task: {Snapshot.State}. Original attempt custody is unavailable; recovery requires its owning service.";
    public string RecoveryObservation => Snapshot?.RecoveryObservation is { } recovery
        ? $"{recovery.ReasonCode}; iterator {recovery.IteratorOutcome}; settlement {recovery.SettlementOutcome}. This does not authorize replay."
        : "No recovery observation is recorded. Provider continuation requires its original owner.";
}

/// <summary>Common detached presentation data; none of these fields is a read or command grant.</summary>
public interface ITaskContextObservation
{
    bool HasCurrentCommandIdentity { get; }
    Guid ContextId { get; }
    Guid? OriginSpaceId { get; }
    long? OriginSpaceRevision { get; }
    Conversation? OriginalConversation { get; }
    string Title { get; }
    TaskExecutionSnapshot? Snapshot { get; }
    TaskExecutionProjection? Projection { get; }
    TaskRunOriginalTaskPermissionReview? PermissionReview { get; }
    TaskRunActivityObservation? Activity { get; }
    bool HasObservedLiveExecution { get; }
    string ExecutionObservation { get; }
    string RecoveryObservation { get; }
}

/// <summary>An exact source-issued selection, not a permission or execution lease.
/// The original nullable Space and absence of a conversation are retained as observed.</summary>
public sealed class TaskContextSelection
{
    internal SpaceTaskWorkspaceService Issuer { get; }
    public Guid TaskId { get; }
    public Guid ExecutionId { get; }
    public Guid ContextId { get; }
    public Guid? OriginSpaceId { get; }
    internal bool HadConversation { get; }
    internal TaskExecutionOwnerBinding OwnerBinding { get; }
    internal TaskContextSelection(SpaceTaskWorkspaceService issuer, Guid taskId, Guid executionId,
        Guid contextId, Guid? originSpaceId, bool hadConversation, TaskExecutionOwnerBinding binding)
    { Issuer = issuer; TaskId = taskId; ExecutionId = executionId; ContextId = contextId;
      OriginSpaceId = originSpaceId; HadConversation = hadConversation; OwnerBinding = binding; }
}

public sealed record AuthorizedTaskContextPage(IReadOnlyList<AuthorizedTaskContextObservation> Tasks,
    TaskObservationPageCursor? Next, bool HasUnavailableContexts);

public sealed record AuthorizedTaskContextObservation(TaskContextSelection Selection,
    Conversation? OriginalConversation, long? OriginSpaceRevision, string? OriginSpaceName,
    TaskExecutionSnapshot Snapshot, TaskExecutionProjection Projection) : ITaskContextObservation
{
    public bool HasCurrentCommandIdentity { get; init; }
    public Guid ContextId => Selection.ContextId;
    public Guid? OriginSpaceId => Selection.OriginSpaceId;
    public string Title => OriginalConversation?.Title ?? Snapshot.PromptSummary;
    // A saved state supplies no producer or active-source witness. Only the actual
    // current source observation, scoped to this exact revision, can establish activity.
    public TaskRunOriginalTaskPermissionReview? PermissionReview { get; init; }
    public TaskRunActivityObservation? Activity { get; init; }
    public bool HasObservedLiveExecution => Activity is { IsLive: true } live && Snapshot.State == TaskExecutionLifecycle.Running &&
        live.TaskId == Snapshot.TaskId && live.ExecutionId == Snapshot.ExecutionId && live.ContextId == ContextId &&
        live.PersistenceRevision == Snapshot.PersistenceRevision;
    public string ExecutionObservation => HasObservedLiveExecution
        ? $"Live original execution observed at {Activity!.ObservedAt:O}; recorded task: {Snapshot.State}."
        : $"Saved task: {Snapshot.State}. Current execution is not established by this observation.";
    public string RecoveryObservation => Snapshot.RecoveryObservation is { } recovery
        ? $"{recovery.ReasonCode}; iterator {recovery.IteratorOutcome}; settlement {recovery.SettlementOutcome}. This does not authorize replay."
        : "No recovery observation is recorded. Provider continuation requires its original owner.";
}
