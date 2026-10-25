using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

/// <summary>Exact source-issued review of a privately retained Ask. Display data grants no
/// permission, execution or replay; every response rechecks this original owner and scope.</summary>
public sealed class TaskRunOriginalTaskPermissionReview
{
    internal TaskExecutionCoordinator Issuer { get; }
    internal TaskRunInvocationCustody Original { get; }
    internal TaskRunCloudPermissionRemediationOwner Source { get; }
    internal TaskExecutionOwnerBinding Owner { get; }
    public ProviderExecutionContext CanonicalTaskContext { get; }
    public RemediationRequest Request { get; }
    public bool CanRespond { get; }
    public PermissionDecision? AcknowledgedDecision { get; }
    internal TaskRunOriginalTaskPermissionReview(TaskExecutionCoordinator issuer, TaskRunInvocationCustody original,
        TaskRunCloudPermissionRemediationOwner source, TaskExecutionSnapshot task, TaskRunOriginalPermissionReview review)
    {
        Issuer = issuer; Original = original; Source = source; Owner = review.Owner;
        CanonicalTaskContext = new(task.TaskId, task.ContextId, task.ExecutionId, task.Attempts.LastOrDefault()?.Id, task.PersistenceRevision);
        Request = review.Request; CanRespond = review.CanRespond; AcknowledgedDecision = review.AcknowledgedDecision;
    }
}
public sealed record TaskRunOriginalTaskPermissionResponse(Guid TaskId, Guid ExecutionId, Guid ContextId,
    Guid RequestId, PermissionDecision Decision);

public sealed partial class TaskExecutionCoordinator
{
    private readonly ConditionalWeakTable<TaskRunOriginalTaskPermissionReview, object> _issuedPermissionReviews = new();

    public Task<TaskRunOriginalTaskPermissionReview?> ReadOriginalTaskPermissionReviewAsync(Guid taskId,
        Guid expectedExecutionId, CancellationToken cancellationToken) =>
        StartOriginalProcessStage("review-original-task-permission", cancellationToken, async token =>
        {
            var stage = RequireOriginalProcessStage();
            var before = await stage.Await(() => ReadAuthorizedTaskObservationAsync(taskId, token)).ConfigureAwait(false);
            if (before.Disposition == TaskObservationDisposition.Denied)
                throw new UnauthorizedAccessException("The current principal cannot review this task.");
            if (before.Snapshot is not { } task || !before.HasCurrentCommandIdentity) return null;
            RequireRun(task, expectedExecutionId);
            var original = TryGetOriginalRunControlSource(task);
            if (original is null || original.TryReadOriginalPublishedPermission() is not { } published) return null;
            var publication = await stage.Await(() => published.Publication).ConfigureAwait(false);
            var review = await stage.Await(() => published.Owner.ReviewOriginalAsync(publication.Id, token)).ConfigureAwait(false);
            if (review.Request.Id != publication.Id || review.Request.ExecutionId != task.ExecutionId || review.Owner != task.OwnerBinding)
                throw new UnauthorizedAccessException("The actual permission review belongs to a different original Task/Run.");
            var current = await stage.Await(() => ReadAuthorizedTaskObservationAsync(taskId, token)).ConfigureAwait(false);
            if (current.Snapshot is not { } fresh || !current.HasCurrentCommandIdentity || fresh.OwnerBinding != task.OwnerBinding ||
                fresh.ContextId != task.ContextId || fresh.ExecutionId != task.ExecutionId || !ReferenceEquals(TryGetOriginalRunControlSource(fresh), original))
                throw new UnauthorizedAccessException("The original task owner changed during permission review.");
            var issued = new TaskRunOriginalTaskPermissionReview(this, original, published.Owner, fresh, review);
            _issuedPermissionReviews.Add(issued, new object());
            return issued;
        });

    public Task<TaskRunOriginalTaskPermissionResponse> RespondOriginalTaskPermissionAsync(
        TaskRunOriginalTaskPermissionReview reviewed, bool approve, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (!ReferenceEquals(reviewed.Issuer, this) || !_issuedPermissionReviews.TryGetValue(reviewed, out _))
            throw new UnauthorizedAccessException("This Task owner did not issue the exact permission review.");
        return StartOriginalProcessStage<TaskRunOriginalTaskPermissionResponse>("respond-original-task-permission", cancellationToken, async token =>
        {
            var stage = RequireOriginalProcessStage();
            var expected = reviewed.CanonicalTaskContext;
            var fresh = await stage.Await(() => ReadOriginalTaskPermissionReviewAsync(expected.TaskId, expected.ExecutionId, token)).ConfigureAwait(false);
            if (fresh is null || !fresh.CanRespond || fresh.Request.Id != reviewed.Request.Id || fresh.Owner != reviewed.Owner ||
                !ReferenceEquals(fresh.Original, reviewed.Original) || !ReferenceEquals(fresh.Source, reviewed.Source) ||
                fresh.CanonicalTaskContext.ContextId != expected.ContextId || fresh.CanonicalTaskContext.PersistenceRevision != expected.PersistenceRevision)
                throw new InvalidOperationException("The actual task or permission scope changed; review the current request before responding.");
            PermissionDecision decision;
            if (approve)
            {
                // The existing owner alone obtains the actual permission response. After
                // this explicit admission its original task is independently joined even
                // when the borrowing view withdraws. Approval NEVER resumes the provider.
                _ = await stage.Await(() => fresh.Source.ApproveOriginalAsync(fresh.Request.Id, token)).ConfigureAwait(false);
                var actual = stage.Invoke(() => fresh.Source.GetOriginalDecision(fresh.Request.Id))
                    ?? throw new InvalidDataException("The permission owner returned no actual decision task.");
                decision = await stage.Await(() => actual, owningCleanup: true).ConfigureAwait(false);
            }
            else decision = await stage.Await(() => fresh.Source.DenyOriginalAsync(fresh.Request.Id, token)).ConfigureAwait(false);
            return new(expected.TaskId, expected.ExecutionId, expected.ContextId, fresh.Request.Id, decision);
        });
    }
}
