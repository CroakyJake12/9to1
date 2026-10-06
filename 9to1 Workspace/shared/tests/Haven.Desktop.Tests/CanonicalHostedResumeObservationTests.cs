using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    private static Task<TaskRunOriginalResumeObservationResult> ActualHostedProducer(TaskRunOriginalResumeObservationLease lease)
    {
        var actual = typeof(TaskRunOriginalResumeObservationLease).GetField("Original", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
        return Assert.IsAssignableFrom<Task<TaskRunOriginalResumeObservationResult>>(
            actual.GetType().GetField("Producer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actual));
    }

    [Fact]
    public async Task A_live_actual_business_callback_can_detach_only_its_observation_while_business_process_join_still_refuses()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token);
        rig.Client.RunApprovedOwnedFrame = true;
        var leaseReady = new TaskCompletionSource<TaskRunOriginalResumeObservationLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var detachedInsideBusiness = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken actualProviderToken = default;
        rig.Capture.ApprovedProviderBody = async actualToken =>
        {
            actualProviderToken = actualToken;
            var actualLease = await leaseReady.Task.ConfigureAwait(false);
            Assert.Throws<InvalidOperationException>(() => { rig.Tasks.DemandExternalOriginalProcessJoin(); });
            actualLease.RequestOriginalObservationRetirement();
            var actualDetach = actualLease.DetachAndDrainAsync();
            await actualDetach.ConfigureAwait(false);
            detachedInsideBusiness.SetResult(actualDetach);
            return await providerHeld.Task.ConfigureAwait(false);
        };
        var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
        leaseReady.SetResult(lease);
        var producer = ActualHostedProducer(lease);
        try
        {
            var actualDetach = await detachedInsideBusiness.Task.WaitAsync(token);
            Assert.Same(ActualHostedDetach(lease), actualDetach);
            Assert.True(actualDetach.IsCompletedSuccessfully);
            Assert.False(producer.IsCompleted);
            Assert.False(providerHeld.Task.IsCompleted);
            Assert.False(actualProviderToken.IsCancellationRequested);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ObservationDetached, (await lease.WaitAsync()).Disposition);
            var current = (await rig.Tasks.GetAsync(original.TaskId, token))!;
            Assert.Equal(original.TaskId, current.TaskId);
            Assert.Equal(original.ExecutionId, current.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Running, current.State);
            providerHeld.SetResult(false);
            Assert.Equal(TaskExecutionLifecycle.Completed, (await producer.WaitAsync(token)).State);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.Equal(0, rig.Tasks.LiveOriginalInvocationCount);
        }
        finally
        {
            leaseReady.TrySetResult(lease);
            providerHeld.TrySetResult(false);
            _ = await Record.ExceptionAsync(() => producer);
        }
    }

    [Theory]
    [InlineData("WaitCustody")]
    [InlineData("DetachCustody")]
    public async Task The_exact_issued_observer_source_refuses_its_own_physical_join_before_returning_existing_detach(string custodyField)
    {
        var token = TestContext.Current.CancellationToken;
        var rig = new Rig();
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<TaskRunOriginalResumeObservationResult>? producer = null;
        try
        {
            await rig.RunAsync(false);
            var original = rig.Service.CurrentCanonicalTask!;
            var request = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
            await rig.Owner.ApproveOriginalAsync(request.Id, token);
            rig.Client.RunApprovedOwnedFrame = true;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Capture.ApprovedProviderBody = _ => { entered.TrySetResult(); return held.Task; };
            var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
            producer = ActualHostedProducer(lease);
            await entered.Task.WaitAsync(token);
            var hosted = typeof(TaskRunOriginalResumeObservationLease).GetField("Original", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
            var exactCustody = hosted.GetType().GetField(custodyField, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hosted)!;
            var actualDetach = ActualHostedDetach(lease);
            var beforeContext = ExecutionContext.Capture()!;
            Task? improperlyReturned = null;
            var invoke = exactCustody.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(bool));
            var failure = Assert.Throws<TargetInvocationException>(() =>
            {
                _ = invoke.Invoke(exactCustody, [new Func<bool>(() =>
                {
                    // The SAME actual source's physical callback survives restored pre-source EC.
                    ExecutionContext.Run(beforeContext, _ => improperlyReturned = lease.DetachAndDrainAsync(), null);
                    return true;
                }), true]);
            });
            Assert.Null(improperlyReturned);
            Assert.Contains(Leaves(failure.InnerException!), cause => cause is InvalidOperationException);
            Assert.Same(actualDetach, ActualHostedDetach(lease));
            Assert.False(actualDetach.IsCompleted);
            Assert.False(producer.IsCompleted);
            lease.RequestOriginalObservationRetirement();
            held.SetResult(false);
            _ = await Record.ExceptionAsync(() => producer.WaitAsync(token));
            // This new negative deliberately faults the actual observer custody. Its original
            // cause remains inspectable; it never becomes a healthy process-drain receipt.
            _ = await Record.ExceptionAsync(() => lease.WaitAsync().WaitAsync(token));
            _ = await Record.ExceptionAsync(() => lease.DetachAndDrainAsync().WaitAsync(token));
            var causes = Assert.IsAssignableFrom<IReadOnlyList<Exception>>(exactCustody.GetType().GetProperty("Causes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(exactCustody));
            Assert.Contains(causes, cause => cause is InvalidOperationException);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            held.TrySetResult(false);
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
            _ = await Record.ExceptionAsync(async () => await rig.DisposeAsync());
        }
    }

    [Fact]
    public async Task Actual_hosted_resume_detaches_observation_while_the_same_provider_body_remains_owned_and_uncancelled()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var user = Assert.Single(rig.Conversations.Messages);
        var request = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token);
        rig.Client.RunApprovedOwnedFrame = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken actualProviderToken = default;
        rig.Capture.ApprovedProviderBody = actualToken =>
        { actualProviderToken = actualToken; entered.TrySetResult(); return held.Task; };
        var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
        var actualProducer = ActualHostedProducer(lease);
        try
        {
            await entered.Task.WaitAsync(token);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            Assert.Equal(original.TaskId, lease.CanonicalTaskContext.TaskId);
            Assert.Equal(original.ExecutionId, lease.CanonicalTaskContext.ExecutionId);
            Assert.Equal(original.ContextId, lease.CanonicalTaskContext.ContextId);
            lease.RequestOriginalObservationRetirement();
            var actualDetach = lease.DetachAndDrainAsync();
            await actualDetach.WaitAsync(token);
            Assert.Same(actualDetach, lease.DetachAndDrainAsync());
            var detached = await lease.WaitAsync().WaitAsync(token);
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ObservationDetached, detached.Disposition);
            Assert.Null(detached.CanonicalTaskContext);
            Assert.Null(detached.State);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            Assert.False(actualProducer.IsCompleted);
            Assert.False(held.Task.IsCompleted);
            Assert.False(actualProviderToken.IsCancellationRequested);
            var running = (await rig.Tasks.GetAsync(original.TaskId, token))!;
            Assert.Equal(TaskExecutionLifecycle.Running, running.State);
            Assert.Equal(TaskRunAttemptState.Running, Assert.Single(running.Attempts).State);
            Assert.Equal(original.ExecutionId, running.ExecutionId);
            held.SetResult(false);
            var terminal = await actualProducer.WaitAsync(token);
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ProducerTerminal, terminal.Disposition);
            Assert.Equal(TaskExecutionLifecycle.Completed, terminal.State);
            Assert.Equal(original.TaskId, terminal.CanonicalTaskContext!.TaskId);
            Assert.Equal(original.ExecutionId, terminal.CanonicalTaskContext.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Completed, rig.Service.CurrentCanonicalTask!.State);
            Assert.Equal(user, Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User));
            Assert.Single(rig.Service.CurrentCanonicalTask.RecoveryHistory);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.Equal(0, rig.Tasks.LiveOriginalInvocationCount);
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ObservationDetached, (await lease.WaitAsync()).Disposition);
        }
        finally { held.TrySetResult(false); _ = await Record.ExceptionAsync(() => actualProducer); }
    }

    [Fact]
    public async Task Returned_hosted_resume_keeps_its_business_lifetime_after_command_token_withdrawal_and_refuses_a_copied_lease()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token);
        rig.Client.RunApprovedOwnedFrame = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken actualProviderToken = default;
        rig.Capture.ApprovedProviderBody = actualToken =>
        { actualProviderToken = actualToken; entered.TrySetResult(); return held.Task; };
        using var command = CancellationTokenSource.CreateLinkedTokenSource(token);
        var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, command.Token);
        var actualProducer = ActualHostedProducer(lease);
        try
        {
            await entered.Task.WaitAsync(token);
            command.Cancel();
            Assert.False(actualProviderToken.IsCancellationRequested);
            Assert.False(actualProducer.IsCompleted);
            var copy = (TaskRunOriginalResumeObservationLease)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(lease, null)!;
            Assert.False(rig.Tasks.IsIssuedOriginalRunResumeObservation(copy));
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            held.SetResult(false);
            var observed = await lease.WaitAsync().WaitAsync(token);
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ProducerTerminal, observed.Disposition);
            Assert.Equal(TaskExecutionLifecycle.Completed, observed.State);
            Assert.Equal(original.TaskId, observed.CanonicalTaskContext!.TaskId);
            Assert.Equal(original.ExecutionId, observed.CanonicalTaskContext.ExecutionId);
            await actualProducer.WaitAsync(token);
            await lease.DetachAndDrainAsync().WaitAsync(token);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            Assert.Equal(TaskExecutionLifecycle.Completed, (await rig.Tasks.GetAsync(original.TaskId, token))!.State);
            Assert.Single((await rig.Tasks.GetAsync(original.TaskId, token))!.Attempts);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally { held.TrySetResult(false); _ = await Record.ExceptionAsync(() => actualProducer); }
    }

    [Fact]
    public async Task Detached_hosted_resume_retains_the_actual_faulted_provider_group_and_same_run_inspection_without_completion()
    {
        var token = TestContext.Current.CancellationToken;
        var rig = new Rig();
        Task<TaskRunOriginalResumeObservationResult>? actualProducer = null;
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exactOce = new OperationCanceledException("actual faulted provider, not view cancellation");
        var exactIo = new IOException("actual provider sibling");
        try
        {
            await rig.RunAsync(false);
            var original = rig.Service.CurrentCanonicalTask!;
            var request = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
            await rig.Owner.ApproveOriginalAsync(request.Id, token);
            rig.Client.RunApprovedOwnedFrame = true;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Capture.ApprovedProviderBody = _ => { entered.TrySetResult(); return held.Task; };
            var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
            actualProducer = ActualHostedProducer(lease);
            await entered.Task.WaitAsync(token);
            lease.RequestOriginalObservationRetirement();
            await lease.DetachAndDrainAsync().WaitAsync(token);
            Assert.False(actualProducer.IsCompleted);
            held.SetException([exactOce, exactIo]);
            var failure = await Record.ExceptionAsync(() => actualProducer.WaitAsync(token));
            Assert.NotNull(failure);
            Assert.True(actualProducer.IsFaulted);
            Assert.True(held.Task.IsFaulted);
            Assert.Same(exactOce, held.Task.Exception!.InnerExceptions[0]);
            Assert.Same(exactIo, held.Task.Exception.InnerExceptions[1]);
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, exactOce));
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, exactIo));
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ObservationDetached, (await lease.WaitAsync()).Disposition);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            var suspended = (await rig.Tasks.GetAsync(original.TaskId, token))!;
            Assert.Equal(original.TaskId, suspended.TaskId);
            Assert.Equal(original.ExecutionId, suspended.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, suspended.State);
            Assert.NotNull(suspended.RecoveryObservation);
            Assert.Single(suspended.RecoveryHistory);
            Assert.Single(suspended.Attempts);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.Equal(2, rig.Tasks.LiveOriginalInvocationCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token));
        }
        finally
        {
            held.TrySetException([exactOce, exactIo]);
            if (actualProducer is not null) _ = await Record.ExceptionAsync(() => actualProducer);
            // The actual failed frame remains failed during independent final cleanup; it is
            // neither retried nor declared settled by presentation-detach success.
            _ = await Record.ExceptionAsync(async () => await rig.DisposeAsync());
        }
    }

    [Fact]
    public async Task A_genuine_completed_host_and_detached_observer_join_the_same_published_process_cohort_without_self_join_or_replay()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token);
        rig.Client.RunApprovedOwnedFrame = true;
        var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
        var actual = ActualHostedProducer(lease);
        var observed = await lease.WaitAsync().WaitAsync(token);
        Assert.Equal(TaskRunOriginalResumeObservationDisposition.ProducerTerminal, observed.Disposition);
        Assert.Equal(TaskExecutionLifecycle.Completed, observed.State);
        await actual.WaitAsync(token);
        lease.RequestOriginalObservationRetirement();
        await lease.DetachAndDrainAsync().WaitAsync(token);
        Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
        rig.Tasks.RequestOriginalProcessRetirement();
        var actualFrames = rig.Runtime.CloseAndDrainAsync();
        var actualBusiness = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
        try
        {
            await actualFrames.WaitAsync(token);
            await actualBusiness.WaitAsync(token);
            Assert.Same(actualBusiness, rig.Tasks.CloseAndSuspendOriginalProducersAsync());
            Assert.True(actualBusiness.IsCompletedSuccessfully);
            Assert.True(actualFrames.IsCompletedSuccessfully);
            var current = (await rig.Tasks.GetAsync(original.TaskId, token))!;
            Assert.Equal(original.TaskId, current.TaskId);
            Assert.Equal(original.ContextId, current.ContextId);
            Assert.Equal(original.ExecutionId, current.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Completed, current.State);
            Assert.Null(current.RecoveryObservation);
            Assert.Single(current.RecoveryHistory);
            Assert.Single(current.Attempts);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.Equal(0, rig.Tasks.LiveOriginalInvocationCount);
        }
        finally
        {
            _ = await Record.ExceptionAsync(() => actualFrames);
            _ = await Record.ExceptionAsync(() => actualBusiness);
        }
    }

    private static Task ActualHostedDetach(TaskRunOriginalResumeObservationLease lease)
    {
        var original = typeof(TaskRunOriginalResumeObservationLease).GetField("Original", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
        return Assert.IsAssignableFrom<Task>(original.GetType().GetField("Detach", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original));
    }

    private static IReadOnlyList<Task> ActualHostedSourceStages(TaskExecutionCoordinator coordinator)
    {
        var originals = (System.Collections.IEnumerable)typeof(TaskExecutionCoordinator).GetField("_originalProcessStages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
        return originals.Cast<object>().SelectMany(original => (IReadOnlyList<Task>)original.GetType().GetProperty("ActualSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original)!).ToArray();
    }

    [Fact]
    public async Task Process_request_retires_the_prepublished_observer_driver_and_joins_it_without_any_view_detach()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token);
        rig.Client.RunApprovedOwnedFrame = true;
        var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
        var producer = ActualHostedProducer(lease);
        var detach = ActualHostedDetach(lease);
        Assert.Contains(ActualHostedSourceStages(rig.Tasks), source => ReferenceEquals(source, producer));
        Assert.Contains(ActualHostedSourceStages(rig.Tasks), source => ReferenceEquals(source, detach));
        await producer.WaitAsync(token);
        var observed = await lease.WaitAsync().WaitAsync(token);
        Assert.Equal(TaskRunOriginalResumeObservationDisposition.ProducerTerminal, observed.Disposition);
        Assert.False(detach.IsCompleted); // The actual source driver is already owned, not manufactured by closing.
        rig.Tasks.RequestOriginalProcessRetirement();
        var frames = rig.Runtime.CloseAndDrainAsync();
        var business = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
        try
        {
            await frames.WaitAsync(token);
            await business.WaitAsync(token);
            Assert.True(detach.IsCompletedSuccessfully);
            Assert.Same(detach, lease.DetachAndDrainAsync());
            lease.RequestOriginalObservationRetirement(); // Permanent provenance returns that SAME terminal original after seal.
            Assert.Same(detach, ActualHostedDetach(lease));
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            Assert.True(business.IsCompletedSuccessfully);
            var current = (await rig.Tasks.GetAsync(original.TaskId, token))!;
            Assert.Equal(original.TaskId, current.TaskId);
            Assert.Equal(original.ExecutionId, current.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Completed, current.State);
            Assert.Single(current.RecoveryHistory);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Tasks.LiveOriginalInvocationCount);
        }
        finally { _ = await Record.ExceptionAsync(() => frames); _ = await Record.ExceptionAsync(() => business); }
    }

    [Fact]
    public async Task Process_drain_retains_and_independently_joins_the_actual_faulted_observer_detach_group()
    {
        var token = TestContext.Current.CancellationToken;
        var rig = new Rig();
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oce = new OperationCanceledException("actual provider fault before observer retirement");
        var io = new IOException("actual provider sibling before observer retirement");
        Task<TaskRunOriginalResumeObservationResult>? producer = null;
        Task? frames = null;
        Task? business = null;
        try
        {
            await rig.RunAsync(false);
            var original = rig.Service.CurrentCanonicalTask!;
            var request = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
            await rig.Owner.ApproveOriginalAsync(request.Id, token);
            rig.Client.RunApprovedOwnedFrame = true;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Capture.ApprovedProviderBody = _ => { entered.TrySetResult(); return held.Task; };
            var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
            producer = ActualHostedProducer(lease);
            var detach = ActualHostedDetach(lease);
            await entered.Task.WaitAsync(token);
            Assert.False(detach.IsCompleted);
            held.SetException([oce, io]);
            Assert.NotNull(await Record.ExceptionAsync(() => producer.WaitAsync(token)));
            Assert.NotNull(await Record.ExceptionAsync(() => lease.WaitAsync().WaitAsync(token)));
            Assert.False(detach.IsCompleted);
            rig.Tasks.RequestOriginalProcessRetirement();
            frames = rig.Runtime.CloseAndDrainAsync();
            business = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
            var cause = await Record.ExceptionAsync(() => business.WaitAsync(token));
            Assert.NotNull(cause);
            Assert.True(detach.IsFaulted);
            Assert.False(detach.IsCanceled);
            Assert.Same(detach, lease.DetachAndDrainAsync());
            Assert.Contains(ActualHostedSourceStages(rig.Tasks), source => ReferenceEquals(source, detach));
            Assert.Contains(Leaves(cause!), error => ReferenceEquals(error, oce));
            Assert.Contains(Leaves(cause!), error => ReferenceEquals(error, io));
            Assert.True(business.IsFaulted);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            var current = (await rig.Tasks.GetAsync(original.TaskId, token))!;
            Assert.Equal(original.TaskId, current.TaskId);
            Assert.Equal(original.ExecutionId, current.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
            Assert.NotNull(current.RecoveryObservation);
            Assert.Single(current.RecoveryHistory);
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(2, rig.Tasks.LiveOriginalInvocationCount);
        }
        finally
        {
            held.TrySetException([oce, io]);
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
            if (frames is not null) _ = await Record.ExceptionAsync(() => frames);
            if (business is not null) _ = await Record.ExceptionAsync(() => business);
            _ = await Record.ExceptionAsync(async () => await rig.DisposeAsync());
        }
    }

}
