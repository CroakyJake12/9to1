using System.Collections;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual direct continuation/inspection source stages and cancellation combination.
/// The existing actor/provider Rig is controlled; no native host final-clean acceptance.</summary>
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Actual_inspection_raw_faulted_read_preserves_its_OCE_sibling_and_whole_process_driver()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        var rows = new ProcessInspectionRows(rig.TaskRows);
        var coordinator = new TaskExecutionCoordinator(rows, rig.Events, admissionAuthority: rig.Authority);
        var task = await coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "controlled inspection source",
            TaskExecutionDurability.PersistedPlan, [], token);
        var first = new OperationCanceledException("actual raw read faulted OCE");
        var sibling = new IOException("actual raw read independent sibling");
        var raw = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([first, sibling]);
        rows.OriginalRead = raw.Task;
        var inspection = coordinator.InspectOriginalRecoveryAsync(task.TaskId, task.ExecutionId, token);
        var failure = await Record.ExceptionAsync(() => inspection);
        Assert.NotNull(failure);
        Assert.True(inspection.IsFaulted);
        Assert.False(inspection.IsCanceled);
        Assert.Contains(Leaves(failure!), value => ReferenceEquals(value, first));
        Assert.Contains(Leaves(failure!), value => ReferenceEquals(value, sibling));
        var actual = Assert.Single(ReadProcessMember<IEnumerable>(coordinator, "_originalProcessStages").Cast<object>(),
            source => ReferenceEquals(ReadProcessMember<Task>(source, "ActualDriver"), inspection));
        Assert.Same(inspection, ReadProcessMember<Task>(actual, "ActualDriver"));
        Assert.Contains(ReadProcessMember<IReadOnlyList<Task>>(actual, "ActualSources"), value => ReferenceEquals(value, raw.Task));
        var close = coordinator.CloseAndSuspendOriginalProducersAsync();
        var closedFailure = await Record.ExceptionAsync(() => close);
        Assert.NotNull(closedFailure);
        Assert.True(close.IsFaulted);
        Assert.Contains(Leaves(closedFailure!), value => ReferenceEquals(value, first));
        Assert.Contains(Leaves(closedFailure!), value => ReferenceEquals(value, sibling));
        Assert.Equal(task.TaskId, (await rig.TaskRows.GetAsync(task.TaskId, token))!.TaskId);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Provider.Starts);
    }

    [Fact]
    public async Task Actual_inspection_read_callback_restoring_old_context_refuses_own_process_join_without_new_work()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        var restored = ExecutionContext.Capture()!;
        var rows = new ProcessInspectionRows(rig.TaskRows);
        var coordinator = new TaskExecutionCoordinator(rows, rig.Events, admissionAuthority: rig.Authority);
        var task = await coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "controlled read callback",
            TaskExecutionDurability.PersistedPlan, [], token);
        Exception? refused = null;
        rows.OnOriginalRead = () => ExecutionContext.Run(restored, _ =>
        { refused = Record.Exception(() => { coordinator.CloseAndSuspendOriginalProducersAsync(); }); }, null);
        var inspection = await coordinator.InspectOriginalRecoveryAsync(task.TaskId, task.ExecutionId, token);
        Assert.IsType<InvalidOperationException>(refused);
        Assert.Equal(task.TaskId, inspection.Snapshot.TaskId);
        Assert.Equal(task.ExecutionId, inspection.Snapshot.ExecutionId);
        Assert.Equal(TaskRunOriginalRecoveryAvailability.OriginalUnavailable, inspection.Availability);
        Assert.Empty(inspection.OriginalWork);
        Assert.Equal(task.PersistenceRevision, inspection.Snapshot.PersistenceRevision);
        await coordinator.CloseAndSuspendOriginalProducersAsync().WaitAsync(token);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Provider.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_continuation_preserves_caller_and_later_enumeration_cancellation_before_inspection(bool cancelLaterEnumeration)
    {
        var runner = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        using var caller = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var neverAcquiredRead = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.TaskRows.OverrideRead = neverAcquiredRead.Task;
        var source = rig.Service.ContinueUnstartedOriginalAsync(Guid.NewGuid(), Guid.NewGuid(), caller.Token);
        var iterator = source.GetAsyncEnumerator(enumeration.Token);
        if (cancelLaterEnumeration) enumeration.Cancel(); else caller.Cancel();
        var actualMove = iterator.MoveNextAsync().AsTask();
        var cause = await Record.ExceptionAsync(() => actualMove.WaitAsync(runner));
        Assert.NotNull(cause);
        Assert.True(actualMove.IsCanceled);
        Assert.False(actualMove.IsFaulted);
        Assert.False(rig.TaskRows.OverrideReadEntered.Task.IsCompleted);
        Assert.False(neverAcquiredRead.Task.IsCompleted);
        Assert.Null(rig.Service.CurrentCanonicalTask);
        Assert.Empty(rig.Conversations.Messages);
        Assert.Equal(0, rig.Client.Dispatches);
        var dispose = iterator.DisposeAsync().AsTask();
        _ = await Record.ExceptionAsync(() => dispose.WaitAsync(runner));
        Assert.True(dispose.IsCompleted);
        var close = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
        _ = await Record.ExceptionAsync(() => close.WaitAsync(runner));
        Assert.True(close.IsCompleted);
        Assert.False(neverAcquiredRead.Task.IsCompleted);
    }

    private sealed class ProcessInspectionRows(ITaskExecutionRepository original) : ITaskExecutionRepository
    {
        internal Task<TaskExecutionSnapshot?>? OriginalRead = null;
        internal Action? OnOriginalRead = null;
        public Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token) => original.UpsertAsync(value, token);
        public Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken token)
        { OnOriginalRead?.Invoke(); return OriginalRead ?? original.GetAsync(taskId, token); }
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken token) => original.GetByContextAsync(contextId, token);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => original.GetResumableAsync(token);
    }
}
