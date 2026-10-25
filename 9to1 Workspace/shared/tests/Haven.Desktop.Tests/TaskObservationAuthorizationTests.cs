using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Task_observation_pages_actual_owned_tasks_by_exact_identity_without_changing_their_origin_or_latest_context()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        var context = rig.Conversation.Id; // This actual conversation has no Space origin.
        var first = await rig.Tasks.BeginAuthorizedAsync(context, Guid.NewGuid(), "first original task", TaskExecutionDurability.PersistedPlan, [], token);
        var second = await rig.Tasks.BeginAuthorizedAsync(context, Guid.NewGuid(), "second task in the same context", TaskExecutionDurability.PersistedPlan, [], token);
        Assert.True(rig.Tasks.HasTaskObservationCensus);
        var page = await rig.Tasks.ReadAuthorizedTaskPageAsync(1, null, token);
        var selected = Assert.Single(page.Tasks);
        Assert.NotNull(page.Next);
        var rest = await rig.Tasks.ReadAuthorizedTaskPageAsync(1, page.Next, token);
        Assert.Single(rest.Tasks); Assert.Null(rest.Next);
        Assert.Equal(new[] { first.TaskId, second.TaskId }.OrderBy(id => id.ToString("D"), StringComparer.Ordinal),
            page.Tasks.Concat(rest.Tasks).Select(task => task.TaskId));
        var exact = await rig.Tasks.ReadAuthorizedTaskObservationAsync(first.TaskId, token);
        Assert.Equal(TaskObservationDisposition.Authorized, exact.Disposition);
        Assert.Equal(first.TaskId, exact.Snapshot!.TaskId);
        Assert.Equal(first.ExecutionId, exact.Snapshot.ExecutionId);
        Assert.Equal(context, exact.Snapshot.ContextId);
        Assert.Equal(first.OwnerBinding, exact.Snapshot.OwnerBinding);
        Assert.Null(rig.Conversation.SpaceId);
        Assert.Empty(rig.Stream); Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(first.PersistenceRevision, exact.Snapshot.PersistenceRevision);
    }

    [Fact]
    public async Task Task_observation_denies_a_foreign_actor_and_refuses_cross_actor_or_cross_issuer_cursor_reuse()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await using var other = new Rig();
        var first = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "owned task", TaskExecutionDurability.PersistedPlan, [], token);
        _ = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "another owned task", TaskExecutionDurability.PersistedPlan, [], token);
        var page = await rig.Tasks.ReadAuthorizedTaskPageAsync(1, null, token);
        Assert.NotNull(page.Next);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.Tasks.ReadAuthorizedTaskPageAsync(1, page.Next, token));
        var owner = rig.Actors.Current!;
        rig.Actors.Current = owner with { ProfileId = "another-actual-profile" };
        var denied = await rig.Tasks.ReadAuthorizedTaskObservationAsync(first.TaskId, token);
        Assert.Equal(TaskObservationDisposition.Denied, denied.Disposition); Assert.Null(denied.Snapshot);
        var foreignPage = await rig.Tasks.ReadAuthorizedTaskPageAsync(100, null, token);
        Assert.Empty(foreignPage.Tasks);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Tasks.ReadAuthorizedTaskPageAsync(1, page.Next, token));
        rig.Actors.Current = owner;
        Assert.Equal(first.TaskId, (await rig.Tasks.ReadAuthorizedTaskObservationAsync(first.TaskId, token)).Snapshot!.TaskId);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_observation_preserves_actual_actor_source_faults_instead_of_returning_an_empty_denied_page(bool single)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        var task = await rig.Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "actual task", TaskExecutionDurability.PersistedPlan, [], token);
        var canceledFault = new OperationCanceledException("actual actor source faulted OCE");
        var sibling = new IOException("actual actor storage read failure");
        var actual = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        actual.SetException([canceledFault, sibling]);
        rig.Actors.OriginalRead = actual.Task;
        try
        {
            Task read = single ? rig.Tasks.ReadAuthorizedTaskObservationAsync(task.TaskId, token)
                : rig.Tasks.ReadAuthorizedTaskPageAsync(10, null, token);
            var error = await Record.ExceptionAsync(() => read);
            Assert.NotNull(error); Assert.True(read.IsFaulted);
            Assert.Contains(Leaves(error), leaf => ReferenceEquals(leaf, canceledFault));
            Assert.Contains(Leaves(error), leaf => ReferenceEquals(leaf, sibling));
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally { rig.Actors.OriginalRead = null; }
    }
}
