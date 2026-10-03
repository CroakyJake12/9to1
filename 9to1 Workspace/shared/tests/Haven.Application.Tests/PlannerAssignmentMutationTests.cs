using Haven.Core;

namespace Haven.Application.Tests;

public sealed class PlannerAssignmentMutationTests
{
    [Fact]
    public async Task Completion_requires_override_and_reopening_required_work_preserves_identity()
    {
        var repository = new Repository();
        var service = new PlannerStructuredEntityService(repository);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var item = new PlannerAssignmentItem(Guid.NewGuid(), id, null, "Required", null,
            PlannerAssignmentItemKind.Required, false, null, null, 1, now);
        var assignment = new PlannerAssignment(id, "Deadline", null, PlannerAssignmentStatus.NotStarted,
            null, now.AddDays(2), null, null, null, "owner", "personal", [], [item], [], [], [], 1, now, now);
        await service.SaveAssignmentAsync(assignment, null, cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAssignmentAsync(id, 1, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, repository.Entity!.Revision);
        await service.CompleteAssignmentAsync(id, 1, true, cancellationToken: TestContext.Current.CancellationToken);
        var completed = await service.GetAssignmentAsync(id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(PlannerAssignmentStatus.Completed, completed!.Status);
        Assert.Equal(2, completed.Revision);
        await Assert.ThrowsAsync<PlannerRevisionConflictException>(() => service.ReopenAssignmentAsync(id, 1, cancellationToken: TestContext.Current.CancellationToken));
        await service.SetAssignmentItemCompleteAsync(id, item.AssignmentItemId, false, 2, cancellationToken: TestContext.Current.CancellationToken);
        var reopened = await service.GetAssignmentAsync(id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(id, reopened!.AssignmentId);
        Assert.Equal(item.AssignmentItemId, reopened.Items.Single().AssignmentItemId);
        Assert.Equal(PlannerAssignmentStatus.InProgress, reopened.Status);
        Assert.Null(reopened.CompletedAt);
        Assert.Equal(3, reopened.Revision);
        await service.SetAssignmentItemCompleteAsync(id, item.AssignmentItemId, true, 3, cancellationToken: TestContext.Current.CancellationToken);
        await service.CompleteAssignmentAsync(id, 4, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(PlannerAssignmentStatus.Completed, (await service.GetAssignmentAsync(id, cancellationToken: TestContext.Current.CancellationToken))!.Status);
    }

    private sealed class Repository : IPlannerStructuredEntityRepository
    {
        public PlannerStructuredEntityEnvelope? Entity;
        public Task<PlannerStructuredEntityEnvelope?> GetAsync(PlannerStructuredEntityKind kind, Guid id, CancellationToken cancellationToken) => Task.FromResult(Entity);
        public Task<PlannerStructuredEntityEnvelope> UpsertAsync(PlannerStructuredEntityEnvelope entity, long? expectedRevision, string? eventType, string? occurrenceId, string eventPayloadJson, CancellationToken cancellationToken)
        {
            if (Entity?.Revision != expectedRevision) throw new PlannerRevisionConflictException(entity.Kind, entity.Id, expectedRevision, Entity?.Revision);
            Entity = entity with { Revision = expectedRevision is null ? 1 : expectedRevision.Value + 1 };
            return Task.FromResult(Entity);
        }
        public Task<PlannerStructuredEntityPage> ListAsync(PlannerStructuredEntityQuery query, CancellationToken cancellationToken) => Task.FromResult(new PlannerStructuredEntityPage(Entity is null ? [] : [Entity], null));
        public Task<PlannerStructuredEntityEnvelope> SetDeletedAsync(PlannerStructuredEntityKind kind, Guid id, long expectedRevision, DateTimeOffset? deletedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerAutomationEventRecord>> GetPendingAutomationEventsAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlannerAutomationEventRecord>>([]);
        public Task MarkAutomationEventPublishedAsync(Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordAutomationEventFailureAsync(Guid eventId, string error, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
