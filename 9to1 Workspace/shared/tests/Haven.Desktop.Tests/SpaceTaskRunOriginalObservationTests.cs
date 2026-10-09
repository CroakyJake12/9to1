using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Tasks;
using Xunit;

namespace Haven.Desktop.Tests;

// SAME real canonical caller/provider/permission fixtures and SAME source-issued host.
// These managed consumer controls are not native frame or complete page integration proof.
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Space_observer_parent_close_detaches_actual_wait_while_same_hosted_provider_remains_owned()
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
        CancellationToken actualBusiness = default;
        rig.Capture.ApprovedProviderBody = actual => { actualBusiness = actual; entered.TrySetResult(); return held.Task; };
        Task<TaskRunOriginalResumeObservationLease>? acquisition = null;
        using var command = CancellationTokenSource.CreateLinkedTokenSource(token);
        var presentation = new ActualSpaceResumePresentation(rig.Tasks, original,
            () => acquisition = rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, command.Token));
        Task<TaskRunOriginalResumeObservationResult>? producer = null;
        try
        {
            await entered.Task.WaitAsync(token);
            var lease = await acquisition!.WaitAsync(token); producer = ActualHostedProducer(lease);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            Assert.Equal(original.TaskId, lease.CanonicalTaskContext.TaskId);
            Assert.Equal(original.ExecutionId, lease.CanonicalTaskContext.ExecutionId);
            command.Cancel(); // Original command wait does not become the business token.
            presentation.RequestRetirement();
            var close = presentation.CloseAndDrainAsync();
            await close.WaitAsync(token);
            Assert.Same(close, presentation.CloseAndDrainAsync());
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(presentation.Original.IsCompletedSuccessfully);
            var result = await presentation.Original.WaitAsync(token);
            Assert.True(result is null || result.Disposition == TaskRunOriginalResumeObservationDisposition.ObservationDetached);
            Assert.True(presentation.Child!.OriginalClose!.IsCompletedSuccessfully);
            Assert.False(producer.IsCompleted);
            Assert.False(held.Task.IsCompleted);
            Assert.False(actualBusiness.IsCancellationRequested);
            Assert.Equal(TaskExecutionLifecycle.Running, (await rig.Tasks.GetAsync(original.TaskId, token))!.State);
            held.TrySetResult(false);
            var terminal = await producer.WaitAsync(token);
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ProducerTerminal, terminal.Disposition);
            Assert.Equal(TaskExecutionLifecycle.Completed, terminal.State);
            Assert.Equal(original.TaskId, terminal.CanonicalTaskContext!.TaskId);
            Assert.Equal(original.ExecutionId, terminal.CanonicalTaskContext.ExecutionId);
            Assert.Equal(user, Assert.Single(rig.Conversations.Messages, row => row.Role == MessageRole.User));
            Assert.Equal(1, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            held.TrySetResult(false);
            await presentation.JoinForFixtureAsync();
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
        }
    }

    [Fact]
    public async Task Space_late_actual_lease_capture_after_view_seal_is_drained_without_business_cancel()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(); await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, row => row.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token); rig.Client.RunApprovedOwnedFrame = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Capture.ApprovedProviderBody = _ => { entered.TrySetResult(); return held.Task; };
        var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
        var producer = ActualHostedProducer(lease);
        var rawAcquisition = new TaskCompletionSource<TaskRunOriginalResumeObservationLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = new ActualSpaceResumePresentation(rig.Tasks, original, () => { factoryEntered.TrySetResult(); return rawAcquisition.Task; });
        try
        {
            await entered.Task.WaitAsync(token);
            await factoryEntered.Task.WaitAsync(token);
            presentation.RequestRetirement(); var close = presentation.CloseAndDrainAsync();
            Assert.False(close.IsCompleted); Assert.False(rawAcquisition.Task.IsCompleted);
            Assert.False(presentation.Original.IsCompleted); Assert.False(producer.IsCompleted);
            rawAcquisition.TrySetResult(lease);
            await close.WaitAsync(token);
            Assert.True(rawAcquisition.Task.IsCompletedSuccessfully);
            Assert.Null(await presentation.Original.WaitAsync(token)); // Acquired then retired; not a business result.
            Assert.True(presentation.Child!.OriginalClose!.IsCompletedSuccessfully);
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ObservationDetached, (await lease.WaitAsync()).Disposition);
            Assert.False(producer.IsCompleted);
            Assert.False(held.Task.IsCompleted);
            Assert.True(rig.Tasks.IsIssuedOriginalRunResumeObservation(lease));
            held.TrySetResult(false); await producer.WaitAsync(token);
            Assert.Equal(original.TaskId, rig.Service.CurrentCanonicalTask!.TaskId);
            Assert.Equal(original.ExecutionId, rig.Service.CurrentCanonicalTask.ExecutionId);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally { rawAcquisition.TrySetResult(lease); held.TrySetResult(false); await presentation.JoinForFixtureAsync(); _ = await Record.ExceptionAsync(() => producer); }
    }

    [Fact]
    public async Task Space_actual_post_await_snapshot_callback_restored_context_cannot_join_its_view_observer()
    {
        var token = TestContext.Current.CancellationToken; var beforeView = ExecutionContext.Capture()!;
        await using var rig = new Rig(); await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, row => row.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token); rig.Client.RunApprovedOwnedFrame = true;
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Capture.ApprovedProviderBody = _ => { entered.TrySetResult(); return held.Task; };
        ActualSpaceResumePresentation? presentation = null;
        Exception? refusal = null; Task<TaskRunOriginalResumeObservationLease>? acquisition = null;
        EventHandler<TaskExecutionSnapshot> actualCallback = (_, snapshot) =>
        {
            if (snapshot.TaskId != original.TaskId || snapshot.ExecutionId != original.ExecutionId || snapshot.State != TaskExecutionLifecycle.Running) return;
            ExecutionContext.Run(beforeView, _ =>
            {
                presentation!.RequestRetirement();
                refusal = Record.Exception(() => presentation.CloseAndDrainAsync().GetAwaiter().GetResult());
            }, null);
        };
        rig.Tasks.SnapshotChanged += actualCallback;
        presentation = new(rig.Tasks, original,
            () => acquisition = rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token), startImmediately: false);
        Task<TaskRunOriginalResumeObservationResult>? producer = null;
        try
        {
            presentation.Start();
            await entered.Task.WaitAsync(token);
            var lease = await acquisition!.WaitAsync(token); producer = ActualHostedProducer(lease);
            Assert.IsType<InvalidOperationException>(refusal);
            await presentation.CloseAndDrainAsync().WaitAsync(token);
            Assert.True(presentation.Child!.OriginalClose!.IsCompletedSuccessfully);
            Assert.False(producer.IsCompleted); Assert.False(held.Task.IsCompleted);
            Assert.Equal(original.TaskId, lease.CanonicalTaskContext.TaskId);
            held.TrySetResult(false); await producer.WaitAsync(token);
            Assert.Equal(original.ExecutionId, rig.Service.CurrentCanonicalTask!.ExecutionId);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            rig.Tasks.SnapshotChanged -= actualCallback;
            presentation.Start(); held.TrySetResult(false); await presentation.JoinForFixtureAsync();
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
        }
    }

    [Fact]
    public async Task Space_observer_rejects_copied_source_lease_without_a_business_completion_or_new_task()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(); await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, row => row.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token); rig.Client.RunApprovedOwnedFrame = true;
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Capture.ApprovedProviderBody = _ => held.Task;
        var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token);
        var producer = ActualHostedProducer(lease);
        var copied = (TaskRunOriginalResumeObservationLease)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(lease, null)!;
        var presentation = new ActualSpaceResumePresentation(rig.Tasks, original, () => Task.FromResult(copied));
        try
        {
            Assert.False(rig.Tasks.IsIssuedOriginalRunResumeObservation(copied));
            Assert.NotNull(await Record.ExceptionAsync(() => presentation.Original.WaitAsync(token)));
            Assert.True(presentation.Original.IsFaulted); Assert.False(presentation.Original.IsCanceled);
            Assert.NotNull(await Record.ExceptionAsync(() => presentation.CloseAndDrainAsync().WaitAsync(token)));
            Assert.False(producer.IsCompleted); Assert.False(held.Task.IsCompleted);
            Assert.Equal(original.TaskId, (await rig.Tasks.GetAsync(original.TaskId, token))!.TaskId);
            Assert.Equal(1, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
        }
        finally { lease.RequestOriginalObservationRetirement(); _ = await Record.ExceptionAsync(lease.DetachAndDrainAsync); held.TrySetResult(false); await presentation.JoinForFixtureAsync(); _ = await Record.ExceptionAsync(() => producer); }
    }

    [Fact]
    public async Task Space_actual_observation_wait_retains_faulted_OCE_and_provider_sibling_in_parent_drain()
    {
        var token = TestContext.Current.CancellationToken;
        var rig = new Rig();
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oce = new OperationCanceledException("SAME actual faulted provider original, not view cancellation");
        var sibling = new IOException("SAME actual provider sibling");
        ActualSpaceResumePresentation? presentation = null;
        Task<TaskRunOriginalResumeObservationResult>? producer = null;
        try
        {
            await rig.RunAsync(false); var original = rig.Service.CurrentCanonicalTask!;
            var request = Assert.Single(rig.Stream, row => row.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
            await rig.Owner.ApproveOriginalAsync(request.Id, token); rig.Client.RunApprovedOwnedFrame = true;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Capture.ApprovedProviderBody = _ => { entered.TrySetResult(); return held.Task; };
            Task<TaskRunOriginalResumeObservationLease>? acquisition = null;
            presentation = new(rig.Tasks, original,
                () => acquisition = rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token));
            await entered.Task.WaitAsync(token);
            var lease = await acquisition!.WaitAsync(token); producer = ActualHostedProducer(lease);
            held.TrySetException([oce, sibling]);
            var failure = await Record.ExceptionAsync(() => presentation.Original.WaitAsync(token));
            Assert.NotNull(failure); Assert.True(presentation.Original.IsFaulted); Assert.False(presentation.Original.IsCanceled);
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, oce));
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, sibling));
            var close = presentation.CloseAndDrainAsync();
            var closeFailure = await Record.ExceptionAsync(() => close.WaitAsync(token));
            Assert.NotNull(closeFailure); Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
            Assert.Contains(Leaves(closeFailure!), cause => ReferenceEquals(cause, oce));
            Assert.Contains(Leaves(closeFailure!), cause => ReferenceEquals(cause, sibling));
            Assert.NotNull(await Record.ExceptionAsync(() => producer.WaitAsync(token)));
            Assert.True(producer.IsFaulted); Assert.True(held.Task.IsFaulted);
            var suspended = (await rig.Tasks.GetAsync(original.TaskId, token))!;
            Assert.Equal(original.TaskId, suspended.TaskId); Assert.Equal(original.ExecutionId, suspended.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, suspended.State); Assert.NotNull(suspended.RecoveryObservation);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            held.TrySetException([oce, sibling]);
            if (presentation is not null) await presentation.JoinForFixtureAsync();
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
            _ = await Record.ExceptionAsync(async () => await rig.DisposeAsync());
        }
    }

    [Fact]
    public async Task Space_request_from_inherited_actual_business_callback_detaches_only_observation_before_producer_terminal()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(); await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var request = Assert.Single(rig.Stream, row => row.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(request.Id, token); rig.Client.RunApprovedOwnedFrame = true;
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Capture.ApprovedProviderBody = _ => { entered.TrySetResult(); return held.Task; };
        ActualSpaceResumePresentation? presentation = null;
        Task<TaskRunOriginalResumeObservationLease>? acquisition = null;
        var requested = false;
        EventHandler<TaskExecutionSnapshot> actualCallback = (_, snapshot) =>
        {
            if (snapshot.TaskId != original.TaskId || snapshot.ExecutionId != original.ExecutionId || snapshot.State != TaskExecutionLifecycle.Running) return;
            requested = true;
            presentation!.RequestRetirement(); // Actual business EC inherited by the request driver; no context suppression.
        };
        rig.Tasks.SnapshotChanged += actualCallback;
        presentation = new(rig.Tasks, original,
            () => acquisition = rig.Tasks.StartObservedOriginalRunResumeAsync(original.TaskId, original.ExecutionId, token), startImmediately: false);
        Task<TaskRunOriginalResumeObservationResult>? producer = null;
        try
        {
            presentation.Start(); await entered.Task.WaitAsync(token);
            var lease = await acquisition!.WaitAsync(token); producer = ActualHostedProducer(lease);
            Assert.True(requested);
            // Host11c's broad business guard is an explicitly retained SOURCE HOLD.
            // This owning control requires Data's genuine observation-only join correction.
            var close = presentation.CloseAndDrainAsync(); await close.WaitAsync(token);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(presentation.Child!.OriginalClose!.IsCompletedSuccessfully);
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ObservationDetached, (await lease.WaitAsync()).Disposition);
            Assert.False(producer.IsCompleted); Assert.False(held.Task.IsCompleted);
            Assert.Equal(TaskExecutionLifecycle.Running, (await rig.Tasks.GetAsync(original.TaskId, token))!.State);
            held.TrySetResult(false); await producer.WaitAsync(token);
            Assert.Equal(original.TaskId, rig.Service.CurrentCanonicalTask!.TaskId);
            Assert.Equal(original.ExecutionId, rig.Service.CurrentCanonicalTask.ExecutionId);
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            rig.Tasks.SnapshotChanged -= actualCallback;
            presentation.Start(); held.TrySetResult(false); await presentation.JoinForFixtureAsync();
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
        }
    }

    private sealed class ActualSpaceResumePresentation
    {
        private readonly DesktopOriginalWorkLifetime _work;
        private readonly TaskCompletionSource _begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SpaceOriginalRunResumeObservation? Child;
        internal Task<TaskRunOriginalResumeObservationResult?> Original { get; }
        internal ActualSpaceResumePresentation(TaskExecutionCoordinator source, TaskExecutionSnapshot same,
            Func<Task<TaskRunOriginalResumeObservationLease>> actualFactory, bool startImmediately = true)
        {
            _work = new(() => { _begin.TrySetResult(); Child?.RequestRetirement(); return Task.CompletedTask; },
                () => Child?.CloseAndDrainAsync() ?? Task.CompletedTask);
            Original = _work.RunAsync(async actual =>
            {
                await _begin.Task.ConfigureAwait(false);
                Child = new(source, actual, same.TaskId, same.ExecutionId, same.ContextId, actualFactory, () => _work.IsRetiring);
                Child.BeginOriginalAcquisition();
                return await actual.AwaitAsync(Child.ActualObservation).ConfigureAwait(false);
            });
            if (startImmediately) Start();
        }
        internal void Start() => _begin.TrySetResult();
        internal void RequestRetirement() => _work.RequestRetirement();
        internal Task CloseAndDrainAsync() { Child?.DemandExternalClose(); return _work.CloseAndDrainAsync(); }
        internal async Task JoinForFixtureAsync()
        {
            RequestRetirement(); _ = await Record.ExceptionAsync(() => Original);
            _ = await Record.ExceptionAsync(CloseAndDrainAsync);
        }
    }
}
