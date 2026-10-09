using System.Collections;
using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual existing Agent/Chat/coordinator originals with the explicitly synthetic
/// actor, repositories and provider from the owning fixture. No installed runtime/account acceptance.</summary>
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Issued_observation_returns_only_the_actual_producer_terminal_result_and_same_wait()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var source = Assert.IsAssignableFrom<IAgentRunOriginalObservationSource>(service);
        var lease = source.StartObservedOriginalRun(definition.Id, "Actual source-issued observation", TestContext.Current.CancellationToken);
        var actualWait = lease.WaitOriginalObservationAsync();
        Assert.Same(actualWait, lease.WaitOriginalObservationAsync());
        var observed = await actualWait.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var returned = Assert.IsType<AgentRun>(observed.TerminalRun);
        Assert.Equal(AgentRunObservationDisposition.ProducerTerminal, observed.Disposition);
        Assert.Equal(AgentRunStatus.Suspended, returned.Status);
        Assert.Equal(returned, await rows.GetAsync(returned.Id, TestContext.Current.CancellationToken));
        Assert.Null(await service.GetRecordedInvocationEvidenceAsync(returned.Id, TestContext.Current.CancellationToken));
        var actual = Assert.Single(ReadObservedOriginals(service));
        Assert.True(ReadObserved<Task<AgentRun>>(actual, "Producer").IsCompletedSuccessfully);
        Assert.Same(ReadObserved<Task<AgentRun>>(actual, "ActualProducer"),
            Assert.Single(ReadObserved<IReadOnlyList<Task>>(ReadObserved<object>(actual, "ProducerCustody"), "Sources")));
        Assert.True(source.IsIssuedOriginalObservation(lease));
        await lease.DetachAndDrainOriginalObservationAsync();
        Assert.True(source.IsIssuedOriginalObservation(lease));
    }

    [Fact]
    public async Task Detach_during_same_resumed_provider_cleanup_leaves_business_original_pending_and_uncancelled()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var paused = await service.RunAsync(definition.Id, "Same canonical business producer", TestContext.Current.CancellationToken);
        await rig.Owner.ApproveOriginalAsync(Assert.Single(rig.RemediationRows.Rows.Values).Id, TestContext.Current.CancellationToken);
        rig.Client.RunApprovedOwnedFrame = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.NextDisposeEntered = entered;
        rig.Client.OriginalDispose = cleanup.Task;
        var lease = service.StartObservedOriginalRetry(paused.Id, CancellationToken.None);
        var owned = Assert.Single(ReadObservedOriginals(service));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(ReadObserved<Task<AgentRun>>(owned, "ActualProducer").IsCompleted);
            lease.RequestOriginalObservationRetirement();
            var drain = lease.DetachAndDrainOriginalObservationAsync();
            Assert.Same(drain, lease.DetachAndDrainOriginalObservationAsync());
            await drain.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var detached = await lease.WaitOriginalObservationAsync();
            Assert.Equal(AgentRunObservationDisposition.ObservationDetached, detached.Disposition);
            Assert.Null(detached.TerminalRun);
            Assert.False(ReadObserved<Task<AgentRun>>(owned, "ActualProducer").IsCompleted);
            Assert.False(ReadObserved<Task<AgentRun>>(owned, "Producer").IsCompleted);
            Assert.False(cleanup.Task.IsCompleted);
            var same = (await rig.Tasks.GetAsync(paused.CanonicalTask!.TaskId, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskExecutionLifecycle.Running, same.State);
            Assert.Equal(paused.CanonicalTask.ExecutionId, same.ExecutionId);
            Assert.Equal(TaskRunAttemptState.Running, Assert.Single(same.Attempts).State);
            Assert.True(service.IsIssuedOriginalObservation(lease));
        }
        finally
        {
            cleanup.TrySetResult();
            _ = await Record.ExceptionAsync(() => ReadObserved<Task<AgentRun>>(owned, "Producer").WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        }
        var completed = await ReadObserved<Task<AgentRun>>(owned, "Producer").WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(paused.Id, completed.Id);
        Assert.Equal(paused.CanonicalTask!.TaskId, completed.CanonicalTask!.TaskId);
        Assert.Equal(paused.CanonicalTask.ExecutionId, completed.CanonicalTask.ExecutionId);
        Assert.Equal(AgentRunStatus.Completed, completed.Status);
        Assert.NotNull(await service.GetRecordedInvocationEvidenceAsync(completed.Id, TestContext.Current.CancellationToken));
        Assert.Single(rows.Values);
        Assert.Equal(1, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Synchronous_RunChanged_can_request_and_join_presentation_detach_without_joining_its_producer()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var callbackRan = false;
        var producerWasPending = false;
        service.RunChanged += value =>
        {
            if (callbackRan) return;
            callbackRan = true;
            var observation = Assert.Single(ReadObservedOriginals(service));
            ReadObserved<AgentRunOriginalObservationLease>(observation, "Lease").RequestOriginalObservationRetirement();
            ReadObserved<AgentRunOriginalObservationLease>(observation, "Lease").DetachAndDrainOriginalObservationAsync()
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            producerWasPending = !ReadObserved<Task<AgentRun>>(observation, "Producer").IsCompleted;
        };
        var lease = service.StartObservedOriginalRun(definition.Id, "Actual callback withdrawal", TestContext.Current.CancellationToken);
        var actual = Assert.Single(ReadObservedOriginals(service));
        var returned = await ReadObserved<Task<AgentRun>>(actual, "Producer").WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(callbackRan);
        Assert.True(producerWasPending);
        Assert.Equal(AgentRunStatus.Suspended, returned.Status);
        Assert.Equal(AgentRunObservationDisposition.ObservationDetached,
            (await lease.WaitOriginalObservationAsync()).Disposition);
        Assert.Empty(ReadObserved<IEnumerable>(service, "OriginalObservationFailures"));
        Assert.True(service.IsIssuedOriginalObservation(lease));
    }

    [Fact]
    public async Task Detach_before_late_actual_discovery_fault_preserves_all_business_faults_and_failed_capacity()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var raw = new TaskCompletionSource<IReadOnlyList<ModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, definition) = CreateAgentCaller(rig, rows, _ => new ControlledAgentDiscovery(() =>
        { entered.TrySetResult(); return raw.Task; }));
        var lease = service.StartObservedOriginalRun(definition.Id, "Actual discovery custody", TestContext.Current.CancellationToken);
        var original = Assert.Single(ReadObservedOriginals(service));
        var first = new OperationCanceledException("Exact late faulted discovery cause, caller token remains live");
        var second = new IOException("Exact direct discovery sibling");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            lease.RequestOriginalObservationRetirement();
            await lease.DetachAndDrainOriginalObservationAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(ReadObserved<Task<AgentRun>>(original, "ActualProducer").IsCompleted);
            Assert.Equal(AgentRunObservationDisposition.ObservationDetached,
                (await lease.WaitOriginalObservationAsync()).Disposition);
        }
        finally
        {
            raw.TrySetException([first, second]);
            _ = await Record.ExceptionAsync(() => ReadObserved<Task<AgentRun>>(original, "Producer").WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        }
        var failure = await Record.ExceptionAsync(() => ReadObserved<Task<AgentRun>>(original, "Producer").WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.NotNull(failure);
        Assert.Contains(Leaves(failure), cause => ReferenceEquals(cause, first));
        Assert.Contains(Leaves(failure), cause => ReferenceEquals(cause, second));
        Assert.True(ReadObserved<Task<AgentRun>>(original, "ActualProducer").IsFaulted);
        Assert.True(ReadObserved<Task<AgentRun>>(original, "Producer").IsFaulted);
        Assert.False(ReadObserved<Task<AgentRun>>(original, "Producer").IsCanceled);
        Assert.False(ReadObserved<bool>(original, "CanRetireHealthyAdmission"));
        Assert.Same(original, Assert.Single(ReadObservedOriginals(service)));
        Assert.Empty(rows.Values);
        Assert.True(service.IsIssuedOriginalObservation(lease));
    }

    [Fact]
    public async Task Nonwithdrawn_actual_canceled_producer_keeps_wait_and_detach_canceled_without_business_success()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var raw = Task.FromCanceled<IReadOnlyList<ModelDescriptor>>(cancelled.Token);
        var (service, definition) = CreateAgentCaller(rig, rows, _ => new ControlledAgentDiscovery(() => raw));
        var lease = service.StartObservedOriginalRun(definition.Id, "Canceled actual source", TestContext.Current.CancellationToken);
        var wait = lease.WaitOriginalObservationAsync();
        var failure = await Record.ExceptionAsync(() => wait);
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.True(wait.IsCanceled);
        var original = Assert.Single(ReadObservedOriginals(service));
        Assert.True(ReadObserved<Task<AgentRun>>(original, "ActualProducer").IsCanceled);
        var drain = lease.DetachAndDrainOriginalObservationAsync();
        Assert.IsAssignableFrom<OperationCanceledException>(await Record.ExceptionAsync(() => drain));
        Assert.True(drain.IsCanceled);
        Assert.False(ReadObserved<bool>(original, "CanRetireHealthyAdmission"));
        Assert.Empty(rows.Values);
    }

    [Fact]
    public async Task Issuance_is_exact_source_provenance_not_public_result_or_a_foreign_owner()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var (foreign, _) = CreateAgentCaller(rig, rows);
        var lease = service.StartObservedOriginalRun(definition.Id, "Exact observation source", TestContext.Current.CancellationToken);
        await lease.WaitOriginalObservationAsync();
        await lease.DetachAndDrainOriginalObservationAsync();
        Assert.True(service.IsIssuedOriginalObservation(lease));
        Assert.False(foreign.IsIssuedOriginalObservation(lease));
        Assert.Empty(typeof(AgentRunOriginalObservationLease).GetConstructors());
        Assert.DoesNotContain(typeof(AgentRunOriginalObservationLease).GetProperties(), property => typeof(Task).IsAssignableFrom(property.PropertyType));
        Assert.Equal(0, (int)AgentRunObservationDisposition.ProducerTerminal);
        Assert.Equal(1, (int)AgentRunObservationDisposition.ObservationDetached);
    }

    [Fact]
    public async Task Healthy_observed_business_cycles_retire_admission_slots_while_exact_issuance_survives()
    {
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        AgentRunOriginalObservationLease? firstLease = null;
        for (var index = 0; index < 136; index++)
        {
            rig.Client.RunApprovedOwnedFrame = false;
            var lease = service.StartObservedOriginalRun(definition.Id, $"Healthy original cycle {index}", TestContext.Current.CancellationToken);
            firstLease ??= lease;
            var paused = Assert.IsType<AgentRun>((await lease.WaitOriginalObservationAsync()).TerminalRun);
            Assert.Equal(AgentRunStatus.Suspended, paused.Status);
            var samePermission = rig.RemediationRows.Rows.Values.Single(value => value.ExecutionId == paused.CanonicalTask!.ExecutionId);
            await rig.Owner.ApproveOriginalAsync(samePermission.Id, TestContext.Current.CancellationToken);
            rig.Client.RunApprovedOwnedFrame = true;
            var resumed = service.StartObservedOriginalRetry(paused.Id, TestContext.Current.CancellationToken);
            var completed = Assert.IsType<AgentRun>((await resumed.WaitOriginalObservationAsync()).TerminalRun);
            Assert.Equal(AgentRunStatus.Completed, completed.Status);
            Assert.Equal(paused.Id, completed.Id);
            Assert.Equal(paused.CanonicalTask!.TaskId, completed.CanonicalTask!.TaskId);
            Assert.Equal(paused.CanonicalTask.ExecutionId, completed.CanonicalTask.ExecutionId);
            Assert.NotNull(await service.GetRecordedInvocationEvidenceAsync(completed.Id, TestContext.Current.CancellationToken));
            Assert.Single(ReadObservedOriginals(service));
        }
        Assert.Equal(136, rows.Values.Count);
        Assert.Equal(136, rig.Client.Dispatches);
        Assert.NotNull(firstLease);
        Assert.True(service.IsIssuedOriginalObservation(firstLease));
        await firstLease.DetachAndDrainOriginalObservationAsync();
        Assert.True(service.IsIssuedOriginalObservation(firstLease));
    }

    // Private source observation only, using the actual instance. The genuine test assembly
    // receives no new friend access, public producer Task or authority factory.
    private static IReadOnlyList<object> ReadObservedOriginals(AgentTaskRuntimeService source) =>
        ReadObserved<IEnumerable>(source, "ActualOriginalObservations").Cast<object>().ToArray();

    private static T ReadObserved<T>(object original, string member)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = original.GetType();
        var value = type.GetField(member, flags)?.GetValue(original)
            ?? type.GetProperty(member, flags)?.GetValue(original)
            ?? throw new InvalidOperationException($"The actual private observation member {member} is missing.");
        return (T)value;
    }
}
