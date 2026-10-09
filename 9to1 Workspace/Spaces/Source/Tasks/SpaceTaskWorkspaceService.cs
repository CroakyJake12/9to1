using System.Runtime.ExceptionServices;
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
        return observations.AsReadOnly();
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
        // Membership can change while the task read or issued-attempt read is pending.
        // These are observation checks, not a physical transaction or mutation grant.
        var currentConversation = await Join(() => conversations.GetAsync(originalConversationId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        DemandConversation(currentConversation, spaceId, originalConversationId);
        var currentSpace = await Join(() => spaces.ReadExistingAsync(spaceId, cancellationToken), originalSourceCallbackScope).ConfigureAwait(false);
        DemandSpace(currentSpace, spaceId);
        if (currentSpace!.Revision != space!.Revision)
            throw new SpaceRevisionConflictException(spaceId, space.Revision, currentSpace.Revision);
        cancellationToken.ThrowIfCancellationRequested();
        return new(spaceId, currentSpace.Revision, currentConversation!, snapshot,
            snapshot is null ? null : TaskExecutionProjection.From(snapshot), originalRetained);
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
    TaskExecutionProjection? Projection, bool OriginalAttemptRetained)
{
    public string ExecutionObservation => Snapshot is null ? "No canonical task has been started for this conversation."
        : Snapshot.Attempts.Count == 0 ? $"Saved task: {Snapshot.State}. No provider attempt is recorded."
        : OriginalAttemptRetained ? $"Saved task: {Snapshot.State}. Original attempt admission is retained; this is not a live activity witness."
        : $"Saved task: {Snapshot.State}. Original attempt custody is unavailable; recovery requires its owning service.";
    public string RecoveryObservation => Snapshot?.RecoveryObservation is { } recovery
        ? $"{recovery.ReasonCode}; iterator {recovery.IteratorOutcome}; settlement {recovery.SettlementOutcome}. This does not authorize replay."
        : "No recovery observation is recorded. Provider continuation requires its original owner.";
}
