using Haven.Core;

namespace Haven.Application;

/// <summary>A display scope over the same canonical tasks. Selecting a scope never
/// changes task origin, ownership, execution, or authority to read another context.</summary>
public enum TaskShownScope
{
    TaskSpaceOnly = 0,
    AllTasks = 1
}

/// <summary>Bounded raw IDs from the SAME task store. Candidates and the stable cursor
/// grant no visibility: the coordinator must freshly authorize every selected task.</summary>
public sealed record TaskObservationCandidatePage(IReadOnlyList<Guid> TaskIds, Guid? NextTaskId);

/// <summary>Optional read-only paging; adapters without a real census cannot advertise All Tasks.
/// Ordering is the canonical task key, independent of mutable activity timestamps. New inserts
/// before an issued cursor appear on a fresh first page; this is not an immutable snapshot.</summary>
public interface ITaskExecutionObservationCensus
{
    Task<TaskObservationCandidatePage> ReadTaskCandidatesAsync(int maximumItems, Guid? afterTaskId,
        CancellationToken cancellationToken);
}

public enum TaskObservationDisposition { Authorized, Denied, Missing, Unavailable }
public sealed record TaskObservationReadResult(TaskObservationDisposition Disposition, TaskExecutionSnapshot? Snapshot)
{
    /// <summary>Presentation only: current binding identity permits asking the original
    /// command owner for availability. It is never a command or continuation grant.</summary>
    public bool HasCurrentCommandIdentity { get; init; }
}

/// <summary>Opaque paging position issued by this actual coordinator. It exposes no
/// inaccessible task identity and grants no authority; every page is freshly authorized.</summary>
public sealed class TaskObservationPageCursor
{
    internal TaskExecutionCoordinator Issuer { get; }
    internal Guid AfterTaskId { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal TaskObservationPageCursor(TaskExecutionCoordinator issuer, Guid afterTaskId, AuthenticatedResourceActor actor)
    { Issuer = issuer; AfterTaskId = afterTaskId; Actor = actor; }
}
public sealed record TaskObservationPage(IReadOnlyList<TaskExecutionSnapshot> Tasks,
    TaskObservationPageCursor? Next, bool HasUnavailableContexts)
{
    public TaskObservationDisposition Disposition { get; init; } = TaskObservationDisposition.Authorized;
}

internal sealed class TaskObservationPrincipal(TaskRunPermissionAuthority issuer, AuthenticatedResourceActor actor)
{
    internal TaskRunPermissionAuthority Issuer { get; } = issuer;
    internal AuthenticatedResourceActor Actor { get; } = actor;
}

internal interface ITaskRunObservationAuthority
{
    Task<TaskObservationPrincipal?> CaptureObservationPrincipalAsync(CancellationToken token);
    Task ValidateObservationPrincipalAsync(TaskObservationPrincipal principal, CancellationToken token);
    bool HasCurrentCommandIdentity(TaskExecutionSnapshot snapshot, TaskObservationPrincipal principal);
    Task<TaskObservationDisposition> AuthorizeObservationAsync(TaskExecutionSnapshot snapshot, TaskObservationPrincipal principal, CancellationToken token);
}

public sealed partial class TaskRunPermissionAuthority : ITaskRunObservationAuthority
{
    async Task<TaskObservationPrincipal?> ITaskRunObservationAuthority.CaptureObservationPrincipalAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_sync) if (_originalAdmissionSealed) return null;
        var actual = RunDelegationCallback(() => _actors.GetCurrentAsync(token).AsTask(), token);
        var actor = await ObserveOriginalPermissionReadAsync(actual).ConfigureAwait(false);
        return actor is null || !ValidActor(actor) ? null : new TaskObservationPrincipal(this, actor);
    }
    async Task ITaskRunObservationAuthority.ValidateObservationPrincipalAsync(TaskObservationPrincipal principal, CancellationToken token)
    {
        if (!ReferenceEquals(principal.Issuer, this)) throw new UnauthorizedAccessException("A different original task owner issued the observation principal.");
        lock (_sync) if (_originalAdmissionSealed) throw new ObjectDisposedException(nameof(TaskRunPermissionAuthority));
        var actual = RunDelegationCallback(() => _actors.GetCurrentAsync(token).AsTask(), token);
        if (await ObserveOriginalPermissionReadAsync(actual).ConfigureAwait(false) != principal.Actor)
            throw new UnauthorizedAccessException("The actual task observation principal changed; refresh this view.");
    }
    async Task<TaskObservationDisposition> ITaskRunObservationAuthority.AuthorizeObservationAsync(TaskExecutionSnapshot snapshot, TaskObservationPrincipal principal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await ((ITaskRunObservationAuthority)this).ValidateObservationPrincipalAsync(principal, token).ConfigureAwait(false);
        if (snapshot.OwnerBinding is not { } binding || binding.TaskId != snapshot.TaskId ||
            binding.ExecutionId != snapshot.ExecutionId || binding.ContextId != snapshot.ContextId)
            return TaskObservationDisposition.Unavailable;
        lock (_sync)
        {
            if (_originalAdmissionSealed) return TaskObservationDisposition.Unavailable;
            if (_owners.TryGetValue(snapshot.TaskId, out var owner) && owner.Binding != binding)
                return TaskObservationDisposition.Denied;
        }
        // A returned missing/different actor is a real denial. A faulted/canceled actor
        // source is NOT a denial and retains its full original failure payload.
        if (principal.Actor.ActorId != binding.ActorId || principal.Actor.ProfileId != binding.ProfileId ||
            principal.Actor.AccountId != binding.AccountId || principal.Actor.OrganisationId != binding.OrganisationId)
            return TaskObservationDisposition.Denied;
        // Historical authentication revision is audit provenance for READS, as in the
        // existing native reopening owner. No old command/admission lease is renewed.
        // A child task additionally needs its genuine retained delegation owner; recorded
        // parent links alone cannot reconstruct scope. Preserve its original validation.
        if (snapshot.ParentDelegation is not null)
        {
            lock (_sync)
                if (!_owners.TryGetValue(snapshot.TaskId, out var owner) || owner.Activation.IsSealed ||
                    owner.Binding != binding || principal.Actor.AuthenticationRevision != binding.AuthenticationRevision)
                    return TaskObservationDisposition.Unavailable;
            await ValidateTaskCommandAsync(snapshot, "task:observe-original-context", token).ConfigureAwait(false);
        }
        await ((ITaskRunObservationAuthority)this).ValidateObservationPrincipalAsync(principal, token).ConfigureAwait(false);
        return TaskObservationDisposition.Authorized;
    }
    bool ITaskRunObservationAuthority.HasCurrentCommandIdentity(TaskExecutionSnapshot snapshot, TaskObservationPrincipal principal)
    {
        if (!ReferenceEquals(principal.Issuer, this) || snapshot.OwnerBinding is not { } binding ||
            principal.Actor != new AuthenticatedResourceActor(binding.ActorId, binding.ProfileId, binding.AccountId,
                binding.OrganisationId, binding.AuthenticationRevision)) return false;
        lock (_sync) return !_originalAdmissionSealed &&
            (!_owners.TryGetValue(snapshot.TaskId, out var owner) || !owner.Activation.IsSealed && owner.Binding == binding);
    }
}

public sealed partial class TaskExecutionCoordinator
{
    public bool HasTaskObservationCensus => repository is ITaskExecutionObservationCensus && _admissionAuthority is ITaskRunObservationAuthority;

    public Task<TaskObservationReadResult> ReadAuthorizedTaskObservationAsync(Guid taskId,
        CancellationToken cancellationToken) => StartOriginalProcessStage("read-authorized-task-observation", cancellationToken,
            async token =>
            {
                if (_admissionAuthority is not ITaskRunObservationAuthority authority) return new(TaskObservationDisposition.Unavailable, null);
                var principal = await RequireOriginalProcessStage().Await(() => authority.CaptureObservationPrincipalAsync(token)).ConfigureAwait(false);
                if (principal is null) return new(TaskObservationDisposition.Denied, null);
                var result = await ReadAuthorizedTaskObservationBodyAsync(taskId, principal, token).ConfigureAwait(false);
                await RequireOriginalProcessStage().Await(() => authority.ValidateObservationPrincipalAsync(principal, token)).ConfigureAwait(false);
                return result;
            });

    private async Task<TaskObservationReadResult> ReadAuthorizedTaskObservationBodyAsync(Guid taskId, TaskObservationPrincipal principal, CancellationToken token)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("An actual task ID is required.", nameof(taskId));
        if (_admissionAuthority is not ITaskRunObservationAuthority authority)
            return new(TaskObservationDisposition.Unavailable, null);
        var stage = RequireOriginalProcessStage();
        var before = await stage.Await(() => repository.GetAsync(taskId, token)).ConfigureAwait(false);
        if (before is null) return new(TaskObservationDisposition.Missing, null);
        if (before.TaskId != taskId || before.ContextId == Guid.Empty || before.ExecutionId == Guid.Empty)
            throw new InvalidDataException("The original task store returned a different or incomplete task identity.");
        var access = await stage.Await(() => authority.AuthorizeObservationAsync(before, principal, token)).ConfigureAwait(false);
        if (access != TaskObservationDisposition.Authorized) return new(access, null);
        var current = await stage.Await(() => repository.GetAsync(taskId, token)).ConfigureAwait(false);
        if (current is null) return new(TaskObservationDisposition.Missing, null);
        if (current.TaskId != before.TaskId || current.ContextId != before.ContextId || current.ExecutionId != before.ExecutionId || current.OwnerBinding != before.OwnerBinding)
            throw new InvalidDataException("The task identity or owner changed during the actual observation read.");
        access = await stage.Await(() => authority.AuthorizeObservationAsync(current, principal, token)).ConfigureAwait(false);
        return access == TaskObservationDisposition.Authorized
            ? new(access, current) { HasCurrentCommandIdentity = authority.HasCurrentCommandIdentity(current, principal) }
            : new(access, null);
    }

    public Task<TaskObservationPage> ReadAuthorizedTaskPageAsync(int maximumCandidates,
        TaskObservationPageCursor? cursor, CancellationToken cancellationToken) =>
        StartOriginalProcessStage("read-authorized-task-page", cancellationToken, async token =>
        {
            if (maximumCandidates is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumCandidates));
            if (cursor is not null && !ReferenceEquals(cursor.Issuer, this))
                throw new UnauthorizedAccessException("Another task owner issued this paging position.");
            if (repository is not ITaskExecutionObservationCensus census || _admissionAuthority is not ITaskRunObservationAuthority authority)
                throw new NotSupportedException("The actual task store and current observation owner cannot enumerate All Tasks.");
            var stage = RequireOriginalProcessStage();
            var principal = await stage.Await(() => authority.CaptureObservationPrincipalAsync(token)).ConfigureAwait(false);
            if (principal is null) return new TaskObservationPage([], null, true) { Disposition = TaskObservationDisposition.Denied };
            if (cursor is not null && cursor.Actor != principal.Actor)
                throw new UnauthorizedAccessException("The task paging principal changed; start from the first page.");
            var page = await stage.Await(() => census.ReadTaskCandidatesAsync(maximumCandidates, cursor?.AfterTaskId, token)).ConfigureAwait(false);
            if (page is null || page.TaskIds is null || page.TaskIds.Count > maximumCandidates || page.TaskIds.Any(id => id == Guid.Empty) ||
                page.TaskIds.Distinct().Count() != page.TaskIds.Count || page.NextTaskId == Guid.Empty ||
                page.NextTaskId is { } next && (page.TaskIds.Count == 0 || page.TaskIds[^1] != next))
                throw new InvalidDataException("The actual task census violated its identity, cursor or explicit bound.");
            var previousKey = cursor?.AfterTaskId.ToString("D");
            foreach (var candidate in page.TaskIds)
            {
                var key = candidate.ToString("D");
                if (previousKey is not null && StringComparer.Ordinal.Compare(previousKey, key) >= 0)
                    throw new InvalidDataException("The actual task census did not advance in canonical task-key order.");
                previousKey = key;
            }
            var authorized = new List<TaskExecutionSnapshot>();
            var unavailable = false;
            foreach (var id in page.TaskIds)
            {
                var observed = await ReadAuthorizedTaskObservationBodyAsync(id, principal, token).ConfigureAwait(false);
                if (observed.Disposition == TaskObservationDisposition.Authorized) authorized.Add(observed.Snapshot!);
                else unavailable = true;
            }
            await stage.Await(() => authority.ValidateObservationPrincipalAsync(principal, token)).ConfigureAwait(false);
            return new TaskObservationPage(Array.AsReadOnly(authorized.ToArray()),
                page.NextTaskId is { } after ? new TaskObservationPageCursor(this, after, principal.Actor) : null, unavailable);
        });
}
