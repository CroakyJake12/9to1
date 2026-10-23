using System.Runtime.ExceptionServices;
using System.ComponentModel;
using System.Reflection;
using CakeOS.Cui.Runtime;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HomeObjectReference = HavenOS.Home.PermissionsTrustNotifications.HomeObjectReference;
using HomePermissionAuthorization = HavenOS.Home.PermissionsTrustNotifications.HomePermissionAuthorization;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using HomePermissionRequestState = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState;

namespace Haven.Desktop.Tests;

/// <summary>Actual retained Home CUI decisions over the physical canonical store.
/// Headless interaction is owning component evidence, not installed GNOME/native acceptance.</summary>
public sealed class HomeNativeApprovalDecisionContinuationTests
{
    [AvaloniaFact]
    public async Task Opening_and_focusing_pending_Home_never_complete_a_decision_or_grant_execution()
    {
        await using var rig = await Rig.CreateAsync();
        var request = await rig.RequestAsync();
        using var route = CancellationTokenSource.CreateLinkedTokenSource(rig.Token);
        var observation = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, route.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        Assert.False(observation.IsCompleted);
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
        Assert.False((await rig.Permissions.BeginExecutionAsync(request.RequestId, rig.Token)).IsAllowed);
        route.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observation.WaitAsync(rig.Token));
        Assert.True(observation.IsCanceled);
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
    }

    [AvaloniaFact]
    public async Task Actual_accept_decline_and_block_report_only_their_exact_canonical_decision()
    {
        foreach (var item in new[]
        {
            (Button: "approval-accept", State: HomePermissionRequestState.Approved),
            (Button: "approval-decline", State: HomePermissionRequestState.Denied),
            (Button: "approval-block", State: HomePermissionRequestState.Blocked)
        })
        {
            await using var rig = await Rig.CreateAsync();
            var request = await rig.RequestAsync();
            var observation = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
            await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
            var originalWindow = rig.Owner.OriginalWindow;
            Assert.False(observation.IsCompleted);
            rig.Click(item.Button);
            var actual = await observation.WaitAsync(rig.Token);
            Assert.Equal(request.RequestId, actual.RequestId);
            Assert.True(actual.DecisionRecorded); Assert.Equal(item.State, actual.State);
            Assert.Same(originalWindow, rig.Owner.OriginalWindow);
            Assert.Equal(item.State, (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
            Assert.Empty((await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).Grants);
            // Observation is informational: only the genuine broker can begin execution once.
            Assert.Equal(item.State == HomePermissionRequestState.Approved,
                (await rig.Permissions.BeginExecutionAsync(request.RequestId, rig.Token)).IsAllowed);
            Assert.False((await rig.Permissions.BeginExecutionAsync(request.RequestId, rig.Token)).IsAllowed);
        }
    }

    [AvaloniaFact]
    public async Task Another_request_decision_cannot_release_the_original_pending_wait()
    {
        await using var rig = await Rig.CreateAsync();
        var first = await rig.RequestAsync(); var second = await rig.RequestAsync();
        var firstObservation = rig.Owner.OpenAndObserveDecisionOriginalAsync(first.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(first.RequestId, rig.Token);
        var secondObservation = rig.Owner.OpenAndObserveDecisionOriginalAsync(second.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(second.RequestId, rig.Token);
        rig.Click("approval-decline");
        Assert.Equal(second.RequestId, (await secondObservation.WaitAsync(rig.Token)).RequestId);
        Assert.False(firstObservation.IsCompleted);
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await rig.Permissions.GetAuthorizationAsync(first.RequestId, rig.Token)).State);
        await rig.Owner.OpenOriginalAsync(first.RequestId, rig.Token);
        rig.Click("approval-accept");
        Assert.Equal(first.RequestId, (await firstObservation.WaitAsync(rig.Token)).RequestId);
    }

    [AvaloniaFact]
    public async Task Canceling_one_observation_keeps_shared_window_usable_and_late_observer_uses_real_receipt()
    {
        await using var rig = await Rig.CreateAsync();
        var first = await rig.RequestAsync();
        using var route = CancellationTokenSource.CreateLinkedTokenSource(rig.Token);
        var canceled = rig.Owner.OpenAndObserveDecisionOriginalAsync(first.RequestId, route.Token);
        await rig.Owner.OpenOriginalAsync(first.RequestId, rig.Token);
        var window = rig.Owner.OriginalWindow;
        route.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(rig.Token));
        var second = await rig.RequestAsync();
        var current = rig.Owner.OpenAndObserveDecisionOriginalAsync(second.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(second.RequestId, rig.Token);
        Assert.Same(window, rig.Owner.OriginalWindow);
        rig.Click("approval-decline");
        var observed = await current.WaitAsync(rig.Token);
        var late = await rig.Owner.OpenAndObserveDecisionOriginalAsync(second.RequestId, rig.Token).WaitAsync(rig.Token);
        Assert.Same(observed, late); // Exact retained actual decision, not a reconstructed approval DTO.
        Assert.Equal(HomePermissionRequestState.Denied, late.State);
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await rig.Permissions.GetAuthorizationAsync(first.RequestId, rig.Token)).State);
    }

    [AvaloniaFact]
    public async Task Closing_and_replacing_the_real_window_cancels_old_wait_without_deciding_the_request()
    {
        await using var rig = await Rig.CreateAsync();
        var request = await rig.RequestAsync();
        var prior = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        var oldWindow = Assert.IsType<Window>(rig.Owner.OriginalWindow);
        var nativeClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldWindow.Closed += (_, _) => nativeClosed.TrySetResult();
        oldWindow.Close();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prior.WaitAsync(rig.Token));
        await nativeClosed.Task.WaitAsync(rig.Token); // Actual native close, not a polling substitute.
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
        Assert.False((await rig.Permissions.BeginExecutionAsync(request.RequestId, rig.Token)).IsAllowed);
        var replacement = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        Assert.NotSame(oldWindow, rig.Owner.OriginalWindow);
        rig.Click("approval-accept");
        Assert.Equal(HomePermissionRequestState.Approved, (await replacement.WaitAsync(rig.Token)).State);
        await rig.CloseAsync();
        Assert.Null(rig.Owner.OriginalWindow);
        Assert.Throws<ObjectDisposedException>(() => { _ = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token); });
    }

    [AvaloniaFact]
    public async Task A_canceled_route_wait_does_not_wait_for_or_cancel_held_actual_global_Home_opening()
    {
        await using var rig = await Rig.CreateAsync();
        var request = await rig.RequestAsync();
        using var route = CancellationTokenSource.CreateLinkedTokenSource(rig.Token);
        rig.Principals.HoldNextActualObservation();
        Task<HomeNativeApprovalDecisionObservation>? observation = null;
        Task? globalOpen = null;
        try
        {
            observation = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, route.Token);
            await rig.Principals.Entered.Task.WaitAsync(rig.Token);
            globalOpen = rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
            Assert.False(globalOpen.IsCompleted);
            route.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observation.WaitAsync(rig.Token));
            Assert.True(observation.IsCanceled); Assert.False(globalOpen.IsCompleted);
        }
        finally { rig.Principals.Release.TrySetResult(); }
        Assert.NotNull(globalOpen); await globalOpen!;
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
        var resumed = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        rig.Click("approval-decline");
        Assert.Equal(HomePermissionRequestState.Denied, (await resumed.WaitAsync(rig.Token)).State);
    }

    [AvaloniaFact]
    public async Task Repeated_late_observations_reuse_one_actual_decision_with_bounded_retained_work()
    {
        await using var rig = await Rig.CreateAsync();
        var request = await rig.RequestAsync();
        var pending = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        rig.Click("approval-decline");
        var observed = await pending.WaitAsync(rig.Token);
        for (var index = 0; index < 140; ++index)
            Assert.Same(observed,
                await rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token).WaitAsync(rig.Token));
        Assert.Equal(HomePermissionRequestState.Denied,
            (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
        Assert.Empty((await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).Grants);
    }

    [AvaloniaFact]
    public async Task Final_original_actor_observation_cannot_join_its_surface_or_parent_even_under_restored_context()
    {
        var beforeOriginal = ExecutionContext.Capture() ?? throw new InvalidOperationException("Capture genuine pre-owner context.");
        await using var rig = await Rig.CreateAsync();
        var request = await rig.RequestAsync();
        var observation = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        var surface = Assert.IsType<HomeApprovalCuiSurface>(rig.Owner.OriginalSurface);
        var model = Assert.IsType<CuiViewModel>(typeof(HomeApprovalCuiSurface)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(surface));
        var callbacks = 0;
        PropertyChangedEventHandler completedStatus = (_, args) =>
        {
            if (args.PropertyName != "Status" || model.Get("Status") is not string text ||
                !text.Contains("approved", StringComparison.OrdinalIgnoreCase)) return;
            // This synchronous genuine model notification is the LAST published status of
            // actual Decide+Refresh. Only the final decision observer's profile read follows.
            rig.Principals.NextCallback = () => ExecutionContext.Run(beforeOriginal.CreateCopy(), _ =>
            {
                ++callbacks;
                Assert.Throws<InvalidOperationException>(surface.DemandExternalOriginalRetirementJoin);
                Assert.Throws<InvalidOperationException>(() => { _ = surface.CloseAndDrainAsync(); });
                Assert.Throws<InvalidOperationException>(rig.Owner.DemandExternalOriginalRetirementJoin);
                Assert.Throws<InvalidOperationException>(rig.Owner.RequestRetirement);
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Owner.CloseAndDrainAsync(); });
                Assert.Null(rig.Owner.OriginalClose);
            }, null);
        };
        model.PropertyChanged += completedStatus;
        try
        {
            rig.Click("approval-accept");
            Assert.Equal(HomePermissionRequestState.Approved, (await observation.WaitAsync(rig.Token)).State);
            Assert.Equal(1, callbacks);
            Assert.False(rig.Owner.OriginalRetirementCapturedAndSettled);
        }
        finally { model.PropertyChanged -= completedStatus; rig.Principals.NextCallback = null; }
    }

    [AvaloniaFact]
    public async Task Cancellation_after_actual_decision_during_final_profile_read_does_not_poison_shared_Home_close()
    {
        await using var rig = await Rig.CreateAsync();
        var request = await rig.RequestAsync();
        using var route = CancellationTokenSource.CreateLinkedTokenSource(rig.Token);
        var observation = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, route.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        var surface = Assert.IsType<HomeApprovalCuiSurface>(rig.Owner.OriginalSurface);
        var model = Assert.IsType<CuiViewModel>(typeof(HomeApprovalCuiSurface)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(surface));
        var callbacks = 0;
        PropertyChangedEventHandler completedStatus = (_, args) =>
        {
            if (args.PropertyName == "Status" && model.Get("Status") is string text &&
                text.Contains("approved", StringComparison.OrdinalIgnoreCase))
                rig.Store.NextReadCallback = () => { ++callbacks; route.Cancel(); };
        };
        model.PropertyChanged += completedStatus;
        try
        {
            rig.Click("approval-accept");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observation.WaitAsync(rig.Token));
            Assert.True(observation.IsCanceled); Assert.Equal(1, callbacks);
            Assert.Equal(HomePermissionRequestState.Approved,
                (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
            var late = await rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token).WaitAsync(rig.Token);
            Assert.Equal(HomePermissionRequestState.Approved, late.State);
            await rig.CloseAsync(); // Genuine shared close must not turn own route cancellation into a fault.
            Assert.True(rig.Owner.OriginalRetirementCapturedAndSettled);
        }
        finally { model.PropertyChanged -= completedStatus; rig.Store.NextReadCallback = null; }
    }

    [AvaloniaFact]
    public async Task Actual_actor_unavailability_before_click_refuses_decision_and_retains_failure()
    {
        await using var rig = await Rig.CreateAsync();
        var request = await rig.RequestAsync();
        var observation = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        rig.Principals.Unavailable = true;
        rig.Click("approval-accept");
        var failure = await Record.ExceptionAsync(() => observation.WaitAsync(rig.Token));
        Assert.NotNull(failure); Assert.True(observation.IsFaulted);
        rig.Principals.Unavailable = false;
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
        await rig.AssertCloseFailureAsync(Leaves(failure!).First());
    }

    [AvaloniaFact]
    public async Task Actual_decision_write_and_post_commit_refresh_failures_keep_original_causes_and_no_fake_success()
    {
        foreach (var afterCommit in new[] { false, true })
        {
            await using var rig = await Rig.CreateAsync();
            var request = await rig.RequestAsync();
            var observation = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
            await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
            var cause = new IOException(afterCommit ? "Actual refresh read failed after durable Home decision." : "Actual decision audit write failed.");
            rig.Store.DecisionFailure = afterCommit ? null : cause;
            rig.Store.RefreshFailureAfterDecision = afterCommit ? cause : null;
            rig.Click("approval-accept");
            var failure = await Record.ExceptionAsync(() => observation.WaitAsync(rig.Token));
            Assert.NotNull(failure); Assert.True(observation.IsFaulted);
            Assert.Contains(Leaves(failure!), item => ReferenceEquals(item, cause));
            Assert.Equal(afterCommit ? HomePermissionRequestState.Approved : HomePermissionRequestState.PendingApproval,
                (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
            await rig.AssertCloseFailureAsync(cause);
        }
    }

    [AvaloniaFact]
    public async Task Faulted_original_decision_OCE_remains_a_fault_not_a_canceled_observation()
    {
        await using var rig = await Rig.CreateAsync();
        var request = await rig.RequestAsync();
        var observation = rig.Owner.OpenAndObserveDecisionOriginalAsync(request.RequestId, rig.Token);
        await rig.Owner.OpenOriginalAsync(request.RequestId, rig.Token);
        var cause = new OperationCanceledException("The actual permission-store task faulted with OCE.");
        rig.Store.DecisionFailure = cause;
        rig.Click("approval-accept");
        var failure = await Record.ExceptionAsync(() => observation.WaitAsync(rig.Token));
        Assert.NotNull(failure); Assert.True(observation.IsFaulted); Assert.False(observation.IsCanceled);
        Assert.Contains(Leaves(failure!), item => ReferenceEquals(item, cause));
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await rig.Permissions.GetAuthorizationAsync(request.RequestId, rig.Token)).State);
        await rig.AssertCloseFailureAsync(cause);
    }

    private static IEnumerable<Exception> Leaves(Exception error)
    {
        if (error is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            foreach (var child in aggregate.InnerExceptions)
                foreach (var leaf in Leaves(child)) yield return leaf;
        else yield return error;
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-home-decision-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private Task? _close;
        private bool _closeAsserted;
        internal CancellationToken Token => _deadline.Token;
        internal ActualPrincipal Principals { get; } = new();
        internal ActualStore Store { get; }
        internal HomeLocalProfileIdentity Profiles { get; }
        internal HomePermissionTrustService Permissions { get; }
        internal HomeCoreRuntime Runtime { get; }
        internal HomeNativeApprovalWindowOwner Owner { get; }
        private AuthenticatedResourceActor _actor = null!;
        private Rig()
        {
            _deadline.CancelAfter(TimeSpan.FromSeconds(30));
            Directory.CreateDirectory(_root);
            Store = new(new FileHomeCoreStateStore(Path.Combine(_root, "home.json")));
            Profiles = new(Store, Principals);
            Permissions = new(Store, (app, action) => app == "write" && action == "write.file.save"
                ? new(HomePermissionRisk.Elevated, true, false, true) : null);
            Runtime = new([new HomeCoreStateService(Store), new HomePermissionsCoreService(Permissions, Profiles)]);
            Owner = new(Runtime, Profiles, Permissions);
        }
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                rig._actor = await rig.Profiles.GetCurrentAsync(rig.Token)
                    ?? throw new InvalidOperationException("The genuine OS profile is unavailable.");
                await rig.Runtime.StartAsync(rig.Token);
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal async Task<HomePermissionAuthorization> RequestAsync()
        {
            var target = new HomeObjectReference("files.item", Guid.NewGuid().ToString("D"));
            var request = await Permissions.AuthorizeAsync(new(null,
                new(_actor.ActorId, "Original decision-control writer", _actor.ProfileId, _actor.AuthenticationRevision, true),
                Guid.NewGuid().ToString("N"), new("write", "write.file.save", [target]),
                new(["files.item"], 1, [target], false, "Save one exact original revision document")), Token);
            Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
            return request;
        }
        internal void Click(string name)
        {
            var surface = Assert.IsType<HomeApprovalCuiSurface>(Owner.OriginalSurface);
            var button = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), item => item.Name == name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        internal async Task CloseAsync()
        { _close ??= Owner.CloseAndDrainAsync(); await _close; }
        internal async Task AssertCloseFailureAsync(Exception cause)
        {
            _close ??= Owner.CloseAndDrainAsync();
            var error = await Record.ExceptionAsync(() => _close);
            Assert.NotNull(error); Assert.Contains(Leaves(error!), item => ReferenceEquals(item, cause));
            _closeAsserted = true;
        }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            if (!_closeAsserted)
                try { await CloseAsync(); } catch (Exception error) { errors.Add(error); }
            try { await Runtime.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { _deadline.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { Directory.Delete(_root, recursive: true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count > 1) throw new AggregateException("Original Home decision fixture and independent cleanup failed.", errors);
        }
    }

    private sealed class ActualPrincipal : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _original = new();
        internal bool Unavailable;
        internal Action? NextCallback;
        private int _hold;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void HoldNextActualObservation() => Interlocked.Exchange(ref _hold, 1);
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            var actual = await _original.GetPrincipalAsync(token);
            Interlocked.Exchange(ref NextCallback, null)?.Invoke();
            if (Interlocked.Exchange(ref _hold, 0) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.ConfigureAwait(false); // Retain the actual original source until released.
            }
            return Unavailable ? null : actual;
        }
    }

    private sealed class ActualStore(IHomeCoreStateStore original) : IHomeCoreStateStore
    {
        internal Exception? DecisionFailure;
        internal Action? NextReadCallback;
        internal Exception? RefreshFailureAfterDecision;
        private Exception? _nextReadFailure;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken token = default)
        {
            if (_nextReadFailure is { } cause) { _nextReadFailure = null; return Task.FromException<HomeStateReadResult>(cause); }
            var actual = original.ReadAsync(token);
            var callback = Interlocked.Exchange(ref NextReadCallback, null);
            return callback is null ? actual : ReadThenCallbackAsync(actual, callback);
        }
        private static async Task<HomeStateReadResult> ReadThenCallbackAsync(Task<HomeStateReadResult> actual, Action callback)
        {
            var result = await actual;
            callback(); // Only after the genuine original read succeeded; no result is fabricated.
            return result;
        }
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken token = default)
        {
            if (record.RecordType == "home.permissions-trust" && record.Payload.GetRawText().Contains("HOME_PERMISSION_ACCEPTED", StringComparison.Ordinal))
            {
                if (DecisionFailure is { } cause) { DecisionFailure = null; return Task.FromException<HomeStateWriteResult>(cause); }
                if (RefreshFailureAfterDecision is { } failure)
                { RefreshFailureAfterDecision = null; return CommitThenFailReadAsync(record, expected, failure, token); }
            }
            return original.WriteAsync(record, expected, token);
        }
        private async Task<HomeStateWriteResult> CommitThenFailReadAsync(HomeCoreStateRecord record, long expected, Exception cause, CancellationToken token)
        {
            var result = await original.WriteAsync(record, expected, token);
            Assert.True(result.IsSuccess);
            _nextReadFailure = cause;
            return result;
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken token = default)
            => original.WriteGuardedAsync(record, expected, actor, guard, token);
    }
}
