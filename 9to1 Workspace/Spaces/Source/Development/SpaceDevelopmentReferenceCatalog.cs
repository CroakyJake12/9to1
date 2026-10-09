using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Tasks;

namespace HavenOS.Apps.Spaces.Development;

/// <summary>A saved selection and read observation, never an execution or resource grant.</summary>
public sealed record SpaceDevelopmentReferenceRow(Guid SpaceId, long SpaceRevision,
    Guid ContextReferenceId, SpaceDevelopmentLink Link, string SpaceName, string ProjectName,
    long TaskRevision, Guid? CheckpointId)
{
    // Both parts are existing persisted identities. Forked Spaces may retain the same row ID.
    public string SelectionKey => $"{SpaceId:D}/{ContextReferenceId:D}";
    public string Title => ProjectName;
    public string Summary => $"{SpaceName} · project {Link.Project.ProjectId:D}, revision {Link.Project.ProjectRevision}";
    public string TaskLabel => $"Task {Link.TaskId:D} · run {Link.ExecutionId:D} · saved revision {TaskRevision}";
    public string CheckpointLabel => CheckpointId is { } id ? $"Acknowledged checkpoint {id:D}" : "No acknowledged checkpoint is recorded.";
}

/// <summary>Bounded observations of explicitly selected existing Spaces. This is not a complete
/// repository/project inventory and does not certify current native Home or command readiness.</summary>
public sealed record SpaceDevelopmentReferencePage(IReadOnlyList<SpaceDevelopmentReferenceRow> Rows,
    int SelectedSpaceCount, int ObservedReferenceCount, int ExcludedOwnerCount, int UnavailableSpaceCount,
    int MaximumReferences)
{
    public string Coverage => $"Read {SelectedSpaceCount} selected existing Spaces and {ObservedReferenceCount} saved Dev references; " +
        $"{ExcludedOwnerCount} references have no matching recorded task owner; {UnavailableSpaceCount} Spaces are missing or archived. " +
        $"Limit {MaximumReferences} references. Saved state does not authorize an action.";
}

/// <summary>Reads the existing Space reference store and SAME canonical Task/Dev owners.
/// The native owner supplies its actual configured Task actor source and existing Space census;
/// that selection is not a whitelist grant. Each row is revalidated before publication and open.
/// This service has no Create/Fork/Begin/Resume/Execute/permission or mutation path.</summary>
public sealed class SpaceDevelopmentReferenceCatalog
{
    private readonly SpaceRegistry spaces;
    private readonly SpaceTaskWorkspaceService tasks;
    private readonly SpaceDevelopmentWorkspace development;
    private readonly IAuthenticatedResourceActorSource actors;

    public SpaceDevelopmentReferenceCatalog(SpaceRegistry originalSpaces, SpaceTaskWorkspaceService originalTasks,
        SpaceDevelopmentWorkspace originalDevelopment, IAuthenticatedResourceActorSource originalActors)
    {
        spaces = originalSpaces ?? throw new ArgumentNullException(nameof(originalSpaces));
        tasks = originalTasks ?? throw new ArgumentNullException(nameof(originalTasks));
        development = originalDevelopment ?? throw new ArgumentNullException(nameof(originalDevelopment));
        actors = originalActors ?? throw new ArgumentNullException(nameof(originalActors));
    }

    /// <summary>Pure borrowed-source preflight; this never requests retirement of global Dev work.</summary>
    public void DemandExternalOriginalRetirementJoin() => development.DemandExternalOriginalRetirementJoin();

    public const int MaximumSelectedSpaces = 64;
    public const int MaximumSavedReferences = 128;

    public bool IsBoundToOriginalComposition(SpaceRegistry sameSpaces, SpaceTaskWorkspaceService sameTasks,
        SpaceDevelopmentWorkspace sameDevelopment, IAuthenticatedResourceActorSource sameActors) =>
        spaces is not null && tasks is not null && development is not null && actors is not null &&
        ReferenceEquals(spaces, sameSpaces) && ReferenceEquals(tasks, sameTasks) &&
        ReferenceEquals(development, sameDevelopment) && ReferenceEquals(actors, sameActors);

    public Task<SpaceDevelopmentReferencePage> ReadAsync(IReadOnlyList<Guid> originalStoredSpaceIds,
        int maximumReferences = MaximumSavedReferences, CancellationToken cancellationToken = default) =>
        ReadAsync(originalStoredSpaceIds, maximumReferences, cancellationToken, null);

    public async Task<SpaceDevelopmentReferencePage> ReadAsync(IReadOnlyList<Guid> originalStoredSpaceIds,
        int maximumReferences, CancellationToken token, Action<Action>? originalSourceCallbackScope)
    {
        ArgumentNullException.ThrowIfNull(originalStoredSpaceIds);
        // Copy before the first suspension: the caller cannot change this selected observation.
        var selected = originalStoredSpaceIds.ToArray();
        if (selected.Length > MaximumSelectedSpaces || selected.Any(id => id == Guid.Empty) ||
            selected.Distinct().Count() != selected.Length || maximumReferences is < 1 or > MaximumSavedReferences)
            throw new ArgumentException("Supply at most 64 distinct existing Space IDs and a reference limit from 1 through 128.");
        var actor = await ReadActorAsync(token, originalSourceCallbackScope).ConfigureAwait(false);
        var witnessedSpaces = new List<SpaceDefinition>();
        var rows = new List<SpaceDevelopmentReferenceRow>();
        var observed = 0; var excludedOwners = 0; var unavailableSpaces = 0;
        foreach (var id in selected)
        {
            var space = await Join(() => spaces.ReadExistingAsync(id, token), originalSourceCallbackScope).ConfigureAwait(false);
            await DemandSameActorAsync(actor, token, originalSourceCallbackScope).ConfigureAwait(false);
            if (space is null || space.IsArchived) { unavailableSpaces++; continue; }
            witnessedSpaces.Add(space);
            var references = (space.ContextReferences ?? []).Where(IsDevelopmentReference).ToArray();
            if (observed + references.Length > maximumReferences)
                throw new InvalidOperationException("This selected saved-reference observation exceeds its explicit bound; select fewer Spaces.");
            observed += references.Length;
            foreach (var reference in references)
            {
                var link = SpaceDevelopmentWorkspace.ReadLink(reference);
                var before = await Join(() => tasks.ReadAsync(id, link.ConversationId, token, originalSourceCallbackScope),
                    originalSourceCallbackScope).ConfigureAwait(false);
                await DemandSameActorAsync(actor, token, originalSourceCallbackScope).ConfigureAwait(false);
                DemandPair(before, link);
                if (!HasRecordedOwner(before.Snapshot!, actor)) { excludedOwners++; continue; }
                if (before.SpaceRevision != space.Revision)
                    throw new SpaceRevisionConflictException(id, space.Revision, before.SpaceRevision);
                // No foreign owner's project resolution, and no physical source/file traversal.
                var view = await Join(() => development.OpenAsync(id, reference.ContextId, token, originalSourceCallbackScope),
                    originalSourceCallbackScope).ConfigureAwait(false);
                await DemandSameActorAsync(actor, token, originalSourceCallbackScope).ConfigureAwait(false);
                DemandPair(view.Task, link);
                DemandOwner(view.Task.Snapshot!, actor);
                if (view.Space.Revision != space.Revision || view.Link != link || view.Project.Reference != link.Project)
                    throw new InvalidOperationException("The selected saved project reference changed during observation; refresh it.");
                rows.Add(Row(view));
            }
        }
        // Independent source reads may suspend. Verify the whole selected cohort again before
        // returning any row, and refresh same-ID canonical revisions rather than copy old state.
        foreach (var space in witnessedSpaces)
        {
            var current = await Join(() => spaces.ReadExistingAsync(space.Id, token), originalSourceCallbackScope).ConfigureAwait(false);
            await DemandSameActorAsync(actor, token, originalSourceCallbackScope).ConfigureAwait(false);
            if (current is null || current.IsArchived || current.Revision != space.Revision ||
                !(current.ContextReferences ?? []).SequenceEqual(space.ContextReferences ?? []))
                throw new InvalidOperationException("A selected Space changed during saved-reference observation; refresh it.");
        }
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var current = await Join(() => tasks.ReadAsync(row.SpaceId, row.Link.ConversationId, token, originalSourceCallbackScope),
                originalSourceCallbackScope).ConfigureAwait(false);
            await DemandSameActorAsync(actor, token, originalSourceCallbackScope).ConfigureAwait(false);
            DemandPair(current, row.Link); DemandOwner(current.Snapshot!, actor);
            if (current.SpaceRevision != row.SpaceRevision || current.Snapshot!.PersistenceRevision < row.TaskRevision)
                throw new InvalidOperationException("The saved task observation regressed or changed Space membership; refresh it.");
            rows[index] = row with { TaskRevision = current.Snapshot.PersistenceRevision, CheckpointId = current.Snapshot.CheckpointId };
        }
        await DemandSameActorAsync(actor, token, originalSourceCallbackScope).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new(rows.ToArray(), selected.Length, observed, excludedOwners, unavailableSpaces, maximumReferences);
    }

    /// <summary>Fresh read-only selection; a tuple or rendered row conveys no capability.</summary>
    public Task<SpaceDeveloperView> OpenAsync(SpaceDevelopmentReferenceRow captured,
        CancellationToken cancellationToken = default) => OpenAsync(captured, cancellationToken, null);

    public async Task<SpaceDeveloperView> OpenAsync(SpaceDevelopmentReferenceRow captured,
        CancellationToken token, Action<Action>? originalSourceCallbackScope)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(captured.Link);
        if (captured.SpaceId == Guid.Empty || captured.ContextReferenceId == Guid.Empty || captured.SpaceRevision < 1 || captured.TaskRevision < 1)
            throw new ArgumentException("An actual saved reference observation is required.");
        var actor = await ReadActorAsync(token, originalSourceCallbackScope).ConfigureAwait(false);
        var before = await Join(() => tasks.ReadAsync(captured.SpaceId, captured.Link.ConversationId, token, originalSourceCallbackScope),
            originalSourceCallbackScope).ConfigureAwait(false);
        await DemandSameActorAsync(actor, token, originalSourceCallbackScope).ConfigureAwait(false);
        DemandPair(before, captured.Link); DemandOwner(before.Snapshot!, actor);
        var current = await Join(() => development.OpenAsync(captured.SpaceId, captured.ContextReferenceId, token, originalSourceCallbackScope),
            originalSourceCallbackScope).ConfigureAwait(false);
        await DemandSameActorAsync(actor, token, originalSourceCallbackScope).ConfigureAwait(false);
        DemandPair(current.Task, captured.Link); DemandOwner(current.Task.Snapshot!, actor);
        if (current.Space.Revision != captured.SpaceRevision || current.Link != captured.Link ||
            current.Project.Reference != captured.Link.Project || current.Task.Snapshot!.PersistenceRevision < captured.TaskRevision)
            throw new InvalidOperationException("The saved project selection changed or task revision regressed; refresh before opening it.");
        token.ThrowIfCancellationRequested();
        return current; // SAME current view; the owning native Task page supplies actual readiness.
    }

    private static SpaceDevelopmentReferenceRow Row(SpaceDeveloperView view) => new(view.Space.Id, view.Space.Revision,
        view.ContextReferenceId, view.Link, view.Space.Name, view.Project.Project.Name,
        view.Task.Snapshot!.PersistenceRevision, view.Task.Snapshot.CheckpointId);
    private static bool IsDevelopmentReference(SpaceContextReference row) =>
        row.Kind == SpaceContextReferenceKind.ConnectedEntity && row.OwnerAppId == "dev";
    private static void DemandPair(SpaceTaskObservation current, SpaceDevelopmentLink link)
    {
        if (current.Conversation.Id != link.ConversationId || current.Snapshot is not { } task ||
            task.ContextId != link.ConversationId || task.TaskId != link.TaskId || task.ExecutionId != link.ExecutionId)
            throw new InvalidOperationException("A different canonical Task/Run/context occupies the saved reference.");
    }
    private static bool HasRecordedOwner(TaskExecutionSnapshot task, AuthenticatedResourceActor actor) =>
        task.OwnerBinding is { } owner && owner.TaskId == task.TaskId && owner.ContextId == task.ContextId && owner.ExecutionId == task.ExecutionId &&
        owner.ActorId == actor.ActorId && owner.ProfileId == actor.ProfileId && owner.AccountId == actor.AccountId && owner.OrganisationId == actor.OrganisationId &&
        !string.IsNullOrWhiteSpace(owner.AuthenticationRevision);
    private static void DemandOwner(TaskExecutionSnapshot task, AuthenticatedResourceActor actor)
    {
        // Historical authentication is provenance. Current actors are reobserved with full
        // equality around every suspension; only real command/action owners grant new work.
        if (!HasRecordedOwner(task, actor)) throw new UnauthorizedAccessException("The saved Task/Run has no matching current stable owner.");
    }
    private async Task<AuthenticatedResourceActor> ReadActorAsync(CancellationToken token, Action<Action>? scope)
    {
        var actor = await Join(() => actors.GetCurrentAsync(token).AsTask(), scope).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
            throw new UnauthorizedAccessException("The actual configured Task actor is unavailable.");
        return actor;
    }
    private async Task DemandSameActorAsync(AuthenticatedResourceActor original, CancellationToken token, Action<Action>? scope)
    {
        if (await ReadActorAsync(token, scope).ConfigureAwait(false) != original)
            throw new UnauthorizedAccessException("The actual configured Task actor changed during saved-reference observation.");
    }

    // Same bounded synchronous custody pattern as SpaceDevelopmentWorkspace: after a
    // returned raw Task is acquired, even a later source-scope fault must join it whole.
    private static async Task<T> Join<T>(Func<Task<T>> source, Action<Action>? scope)
    {
        Task<T>? actual = null; Exception? acquisition = null;
        var thread = Environment.CurrentManagedThreadId; var open = 1; var acquisitions = 0;
        void AcquireOnce()
        {
            if (Environment.CurrentManagedThreadId != thread || Volatile.Read(ref open) == 0 || Interlocked.CompareExchange(ref acquisitions, 1, 0) != 0)
                throw new InvalidOperationException("The original source scope must synchronously acquire its one actual Task.");
            actual = source();
        }
        try { if (scope is null) AcquireOnce(); else scope(AcquireOnce); }
        catch (Exception error) { acquisition = error; }
        finally { Volatile.Write(ref open, 0); }
        if (actual is null)
        {
            var error = acquisition ?? new InvalidOperationException("No original saved-reference source Task was acquired.");
            if (error is OperationCanceledException) throw new AggregateException("Original source acquisition failed.", error);
            ExceptionDispatchInfo.Capture(error).Throw();
        }
        T result = default!; Exception? terminal = null;
        try { result = await actual!.ConfigureAwait(false); } catch (Exception error) { terminal = error; }
        if (acquisition is null)
        {
            if (terminal is null) return result;
            if (actual!.Exception is { } whole) throw whole;
            ExceptionDispatchInfo.Capture(terminal).Throw(); // Genuine canceled Task remains canceled.
        }
        var causes = new List<Exception> { acquisition! };
        if (actual!.Exception is { } group) causes.Add(group);
        else if (terminal is not null && !ReferenceEquals(terminal, acquisition)) causes.Add(terminal);
        throw new AggregateException("Original saved-reference acquisition and independent terminal join failed.", causes);
    }
}
