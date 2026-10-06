using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

// Actual page/host/factor and the maintained real coordinator Rig. Controlled
// startup and actor observations are protocol negatives, not authenticated,
// installed-service, native-frame, provider, permission or Task-command proof.
public sealed partial class SpaceTasksDashboardOriginalWorkTests
{
    [AvaloniaFact]
    public async Task Missing_original_Home_attachment_is_unavailable_without_actor_or_Task_writes()
    {
        await using var control = await NativeReadinessControl.CreateAsync(null);
        var actual = control.Start();
        Assert.Null(await control.InspectAsync(actual));
        var check = Assert.IsAssignableFrom<Task<CuiSceneAvailability>>(control.Forwarder.ActualCheck);
        var observed = await check.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(CuiSceneAvailabilityState.Unavailable, observed.State);
        Assert.Equal("HomeStartupAttachmentUnavailable", observed.Code);
        Assert.Equal(0, control.Actors.Calls);
        Assert.Equal(0, control.Context.Tasks.Writes);
        Assert.Equal(control.Context.Snapshot.TaskId, control.Context.Tasks.Current!.TaskId);
        Assert.Equal(control.Context.Snapshot.ExecutionId, control.Context.Tasks.Current.ExecutionId);
    }

    [AvaloniaFact]
    public async Task Held_actual_startup_check_remains_in_both_original_drains_without_borrowed_service_close()
    {
        var home = new NativeReadinessStartup();
        await using var control = await NativeReadinessControl.CreateAsync(home);
        var actual = control.Start();
        await home.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        control.Page.RequestRetirement();
        var pageClose = control.Page.CloseAndDrainAsync();
        var childClose = control.Frame.CloseAndDrainAsync();
        Assert.Same(pageClose, control.Page.CloseAndDrainAsync());
        Assert.Same(childClose, control.Frame.CloseAndDrainAsync());
        Assert.False(home.Raw.Task.IsCompleted);
        Assert.False(actual.IsCompleted);
        Assert.False(pageClose.IsCompleted);
        Assert.False(childClose.IsCompleted);
        Assert.Equal(0, home.CloseCalls);
        home.Release();
        Assert.NotNull(await control.InspectAsync(actual));
        Assert.NotNull(await control.InspectAsync(control.Forwarder.ActualCheck!));
        Assert.NotNull(await control.InspectAsync(childClose));
        Assert.NotNull(await control.InspectAsync(pageClose));
        Assert.True(home.Raw.Task.IsCompletedSuccessfully);
        Assert.True(actual.IsCompleted);
        Assert.True(pageClose.IsCompleted);
        Assert.True(childClose.IsCompleted);
        Assert.Null(control.Page.OriginalHost.Content);
        Assert.Same(control.Page.OriginalHost, control.Page.Content); // Failed host drain retained.
        Assert.Equal(0, home.CloseCalls);
        Assert.Equal(0, control.Context.Tasks.Writes);
    }

    [AvaloniaFact]
    public async Task Faulted_startup_OCE_and_direct_sibling_stay_faulted_in_same_readiness_and_page_drains()
    {
        var home = new NativeReadinessStartup();
        await using var control = await NativeReadinessControl.CreateAsync(home);
        var actual = control.Start();
        await home.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var first = new OperationCanceledException("Exact faulted startup cause");
        var second = new IOException("Exact startup direct sibling");
        home.Raw.SetException([first, second]);
        Assert.NotNull(await control.InspectAsync(actual));
        Assert.NotNull(await control.InspectAsync(home.Raw.Task));
        var check = control.Forwarder.ActualCheck!;
        Assert.NotNull(await control.InspectAsync(check));
        Assert.True(home.Raw.Task.IsFaulted);
        Assert.True(check.IsFaulted);
        Assert.False(check.IsCanceled);
        Assert.True(ContainsSameCause(check.Exception!, first));
        Assert.True(ContainsSameCause(check.Exception!, second));
        control.Page.RequestRetirement();
        var childClose = control.Frame.CloseAndDrainAsync();
        var pageClose = control.Page.CloseAndDrainAsync();
        Assert.NotNull(await control.InspectAsync(childClose));
        Assert.NotNull(await control.InspectAsync(pageClose));
        Assert.True(ContainsSameCause(childClose.Exception!, first));
        Assert.True(ContainsSameCause(childClose.Exception!, second));
        Assert.True(ContainsSameCause(pageClose.Exception!, first));
        Assert.True(ContainsSameCause(pageClose.Exception!, second));
        Assert.Equal(0, home.CloseCalls);
        Assert.Equal(0, control.Context.Tasks.Writes);
    }

    [AvaloniaFact]
    public async Task Restored_pre_page_startup_callback_can_request_but_cannot_join_encompassing_page_retirement()
    {
        var beforePage = ExecutionContext.Capture()!;
        var home = new NativeReadinessStartup();
        await using var control = await NativeReadinessControl.CreateAsync(home);
        Exception? refused = null;
        home.BeforeCheck = () => ExecutionContext.Run(beforePage, _ =>
        {
            control.Page.RequestRetirement();
            refused = Record.Exception(() => control.Page.CloseAndDrainAsync().GetAwaiter().GetResult());
        }, null);
        var actual = control.Start();
        await home.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.IsType<InvalidOperationException>(refused);
        Assert.Contains("readiness callback", refused!.Message);
        var pageClose = control.Page.CloseAndDrainAsync();
        Assert.False(pageClose.IsCompleted);
        Assert.False(home.Raw.Task.IsCompleted);
        Assert.Equal(0, home.CloseCalls);
        home.Release();
        Assert.NotNull(await control.InspectAsync(actual));
        Assert.NotNull(await control.InspectAsync(control.Forwarder.ActualCheck!));
        Assert.NotNull(await control.InspectAsync(pageClose));
        var childClose = control.Frame.CloseAndDrainAsync();
        Assert.NotNull(await control.InspectAsync(childClose));
        Assert.True(childClose.IsCompleted);
        Assert.Null(control.Page.OriginalHost.Content);
        Assert.Equal(0, control.Context.Tasks.Writes);
    }

    [AvaloniaFact]
    public async Task Actual_actor_revision_change_during_Home_check_refuses_same_view_without_replacing_Task_Run()
    {
        var home = new NativeReadinessStartup();
        await using var control = await NativeReadinessControl.CreateAsync(home);
        var original = control.Actors.Current;
        var actual = control.Start();
        await home.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        control.Actors.Current = original with { AuthenticationRevision = "controlled-new-authentication" };
        home.Release();
        Assert.NotNull(await control.InspectAsync(actual));
        var check = control.Forwarder.ActualCheck!;
        Assert.NotNull(await control.InspectAsync(check));
        Assert.True(check.IsFaulted);
        Assert.NotNull(FindCause<UnauthorizedAccessException>(check.Exception!, x => x.Message.Contains("actor changed", StringComparison.Ordinal)));
        Assert.Null(control.Page.OriginalHost.Content);
        Assert.Equal(control.Context.Snapshot.TaskId, control.Context.Tasks.Current!.TaskId);
        Assert.Equal(control.Context.Snapshot.ExecutionId, control.Context.Tasks.Current.ExecutionId);
        Assert.Equal(0, control.Context.Tasks.Writes);
        control.Page.RequestRetirement();
        Assert.NotNull(await control.InspectAsync(control.Frame.CloseAndDrainAsync()));
        Assert.NotNull(await control.InspectAsync(control.Page.CloseAndDrainAsync()));
        Assert.Equal(0, home.CloseCalls);
    }

    [AvaloniaFact]
    public async Task Late_actual_readiness_factory_product_is_captured_and_joined_after_page_seal()
    {
        await using var context = await Rig.CreateAsync();
        using var connection = new CancellationTokenSource();
        using var windowLifetime = new CancellationTokenSource();
        var home = new NativeReadinessStartup();
        var actors = new NativeReadinessActors();
        SpaceTaskWidgetPage? page = null;
        NativeCanonicalTaskSceneReadiness? child = null;
        Task? acquisition = null, inspectedClose = null;
        try
        {
            page = new(context.Service, context.Canonical, context.Space.Id, context.Conversation.Id,
                new NativeReadinessForwarder(), expectedOriginalTaskId: context.Snapshot.TaskId,
                expectedOriginalExecutionId: context.Snapshot.ExecutionId);
            acquisition = page.AcquireOriginalNativeReadinessAsync(actualPage =>
            {
                child = NativeCanonicalTaskSceneReadiness.BindOriginal(actualPage, home, actors, connection.Token, windowLifetime.Token);
                actualPage.RequestRetirement(); // Real page seal before SAME child returns.
                return child;
            });
            Assert.NotNull(await InspectNativeOriginalAsync(acquisition));
            Assert.True(acquisition.IsFaulted);
            Assert.NotNull(child);
            var childClose = child!.CloseAndDrainAsync();
            await childClose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Same(childClose, child.CloseAndDrainAsync());
            Assert.True(childClose.IsCompletedSuccessfully);
            inspectedClose = page.CloseAndDrainAsync();
            Assert.NotNull(await InspectNativeOriginalAsync(inspectedClose));
            Assert.True(inspectedClose.IsFaulted);
            Assert.Null(page.Content); // Host never acquired a Show; its actual close succeeded.
            Assert.Equal(0, home.Calls);
            Assert.Equal(0, home.CloseCalls);
            Assert.Equal(0, actors.Calls);
            Assert.Equal(0, context.Tasks.Writes);
        }
        finally
        {
            home.Release();
            var failures = new List<Exception>();
            if (acquisition is not null) _ = await JoinNativeOriginalTerminalAsync(acquisition);
            if (child is not null)
            {
                try { child.RequestRetirement(); var cause = await JoinNativeOriginalTerminalAsync(child.CloseAndDrainAsync()); if (cause is not null) failures.Add(cause); }
                catch (Exception error) { failures.Add(error); }
            }
            if (page is not null)
            {
                try { page.RequestRetirement(); var actualClose = page.CloseAndDrainAsync(); var cause = await JoinNativeOriginalTerminalAsync(actualClose);
                    if (cause is not null && !ReferenceEquals(actualClose, inspectedClose)) failures.Add(cause); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0) throw new AggregateException(failures);
        }
    }

    [AvaloniaFact]
    public async Task Connection_cancellation_does_not_complete_or_replace_held_original_startup_check()
    {
        var home = new NativeReadinessStartup();
        await using var control = await NativeReadinessControl.CreateAsync(home);
        var actual = control.Start();
        await home.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        control.Connection.Cancel();
        control.Page.RequestRetirement();
        var close = control.Page.CloseAndDrainAsync();
        Assert.False(home.Raw.Task.IsCompleted);
        Assert.False(actual.IsCompleted);
        Assert.False(close.IsCompleted);
        home.Release();
        Assert.NotNull(await control.InspectAsync(actual));
        Assert.NotNull(await control.InspectAsync(close));
        var check = control.Forwarder.ActualCheck!;
        Assert.NotNull(await control.InspectAsync(check));
        Assert.True(home.Raw.Task.IsCompletedSuccessfully);
        Assert.False(home.Raw.Task.IsCanceled);
        Assert.True(check.IsFaulted);
        Assert.False(check.IsCanceled);
        Assert.NotNull(FindCause<OperationCanceledException>(check.Exception!, _ => true));
        Assert.NotNull(await control.InspectAsync(control.Frame.CloseAndDrainAsync()));
        Assert.Equal(0, home.CloseCalls);
        Assert.Equal(0, control.Context.Tasks.Writes);
    }

    [AvaloniaFact]
    public async Task Held_first_native_factory_reserves_one_actual_child_and_refuses_second_factory_before_any_callback()
    {
        await using var context = await Rig.CreateAsync();
        using var connection = new CancellationTokenSource();
        using var windowLifetime = new CancellationTokenSource();
        var home = new NativeReadinessStartup();
        var actors = new NativeReadinessActors();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspected = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        SpaceTaskWidgetPage? page = null;
        NativeCanonicalTaskSceneReadiness? firstChild = null, secondChild = null;
        Task<NativeCanonicalTaskSceneReadiness>? firstDriver = null, firstAcquisition = null, secondAcquisition = null;
        Task? pageClose = null;
        var secondFactoryCalls = 0;
        Exception? primary = null;
        try
        {
            page = new(context.Service, context.Canonical, context.Space.Id, context.Conversation.Id,
                new NativeReadinessForwarder(), expectedOriginalTaskId: context.Snapshot.TaskId,
                expectedOriginalExecutionId: context.Snapshot.ExecutionId);
            var actualPage = page;
            firstDriver = Task.Run(() =>
            {
                firstAcquisition = actualPage.AcquireOriginalNativeReadinessAsync(owner =>
                {
                    firstChild = NativeCanonicalTaskSceneReadiness.BindOriginal(owner, home, actors, connection.Token, windowLifetime.Token);
                    entered.TrySetResult();
                    release.Task.GetAwaiter().GetResult(); // Actual synchronous source factory held before product return.
                    return firstChild;
                });
                return firstAcquisition;
            }, TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            secondAcquisition = page.AcquireOriginalNativeReadinessAsync(owner =>
            {
                secondFactoryCalls++;
                secondChild = NativeCanonicalTaskSceneReadiness.BindOriginal(owner, home, actors, connection.Token, windowLifetime.Token);
                return secondChild;
            });
            Assert.NotNull(await InspectNativeOriginalAsync(secondAcquisition)); inspected.Add(secondAcquisition);
            Assert.True(secondAcquisition.IsFaulted);
            Assert.False(secondAcquisition.IsCanceled);
            Assert.Equal(0, secondFactoryCalls);
            Assert.Null(secondChild);
            Assert.NotNull(firstChild);
            Assert.False(firstDriver.IsCompleted);
            page.RequestRetirement();
            pageClose = page.CloseAndDrainAsync();
            Assert.Same(pageClose, page.CloseAndDrainAsync());
            Assert.False(pageClose.IsCompleted); // SAME first factory original is still pending.
            release.TrySetResult();
            Assert.NotNull(await InspectNativeOriginalAsync(firstDriver)); inspected.Add(firstDriver);
            Assert.NotNull(firstAcquisition);
            Assert.True(firstAcquisition!.IsFaulted);
            Assert.True(firstDriver.IsFaulted);
            inspected.Add(firstAcquisition);
            var childClose = firstChild!.CloseAndDrainAsync();
            Assert.Null(await InspectNativeOriginalAsync(childClose)); inspected.Add(childClose);
            Assert.Same(childClose, firstChild.CloseAndDrainAsync());
            Assert.True(childClose.IsCompletedSuccessfully);
            Assert.NotNull(await InspectNativeOriginalAsync(pageClose)); inspected.Add(pageClose);
            Assert.True(pageClose.IsFaulted);
            Assert.False(pageClose.IsCanceled);
            Assert.Null(page.Content); // Never shown; real host cleanup remains independently joined.
            Assert.Equal(0, home.Calls);
            Assert.Equal(0, home.CloseCalls);
            Assert.Equal(0, actors.Calls);
            Assert.Equal(0, context.Tasks.Writes);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            release.TrySetResult(); home.Release();
            var failures = new List<Exception>();
            foreach (var actual in new Task?[] { firstDriver, firstAcquisition, secondAcquisition }.OfType<Task>().Distinct<Task>(ReferenceEqualityComparer.Instance))
            {
                var cause = await JoinNativeOriginalTerminalAsync(actual);
                if (cause is not null && !inspected.Contains(actual)) failures.Add(cause);
            }
            foreach (var child in new[] { firstChild, secondChild }.OfType<NativeCanonicalTaskSceneReadiness>().Distinct<NativeCanonicalTaskSceneReadiness>(ReferenceEqualityComparer.Instance))
            {
                try
                {
                    child.RequestRetirement(); var actualClose = child.CloseAndDrainAsync();
                    var cause = await JoinNativeOriginalTerminalAsync(actualClose);
                    if (cause is not null && !inspected.Contains(actualClose)) failures.Add(cause);
                }
                catch (Exception error) { failures.Add(error); }
            }
            if (page is not null)
            {
                try
                {
                    page.RequestRetirement(); var actualClose = page.CloseAndDrainAsync();
                    var cause = await JoinNativeOriginalTerminalAsync(actualClose);
                    if (cause is not null && !inspected.Contains(actualClose)) failures.Add(cause);
                }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0) throw new AggregateException(primary is null ? failures : new[] { primary }.Concat(failures));
        }
    }

    private static async Task<Exception?> InspectNativeOriginalAsync(Task actual)
    {
        try { await actual.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); return null; }
        catch (Exception error) { return actual.Exception ?? error; }
    }
    private static async Task<Exception?> JoinNativeOriginalTerminalAsync(Task actual)
    {
        // Cleanup joins the actual terminal, independently of test/caller cancellation.
        try { await actual; return null; } catch (Exception error) { return actual.Exception ?? error; }
    }
    private sealed class NativeReadinessForwarder : ICuiSceneReadiness
    {
        internal NativeCanonicalTaskSceneReadiness? Owner;
        internal Task<CuiSceneAvailability>? ActualCheck;
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            var actual = (Owner ?? throw new InvalidOperationException("No actual captured route.")).CheckAsync(token).AsTask();
            ActualCheck = actual; // SAME ValueTask materialized exactly once.
            return new(actual);
        }
    }
    private sealed class NativeReadinessActors : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = new("controlled-task-actor", "controlled-task-profile",
            Guid.NewGuid(), null, "controlled-original-authentication");
        internal int Calls;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
    }
    private sealed class NativeReadinessStartup : IHomeNativeStartupSession
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<HomeNativeStartupObservation> Raw = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? BeforeCheck;
        internal int Calls, CloseCalls;
        public Task<HomeNativeStartupObservation> CheckAsync(CancellationToken token)
        { Calls++; BeforeCheck?.Invoke(); Entered.TrySetResult(); return Raw.Task; }
        public Task CloseAndDrainAsync() { CloseCalls++; return Task.CompletedTask; }
        internal void Release() => Raw.TrySetResult(new(HomeNativeStartupState.Unready,
            "CONTROLLED_UNAVAILABLE_HOME", "A managed negative observation, not an installed-service or native-frame receipt."));
    }
    private sealed class NativeReadinessControl : IAsyncDisposable
    {
        internal Rig Context = null!;
        internal Window? Window;
        internal SpaceTaskWidgetPage Page = null!;
        internal NativeCanonicalTaskSceneReadiness Frame = null!;
        internal readonly NativeReadinessForwarder Forwarder = new();
        internal readonly NativeReadinessActors Actors = new();
        internal readonly CancellationTokenSource Connection = new(), WindowLifetime = new();
        private NativeReadinessStartup? _home;
        private Task? _activation;
        private readonly HashSet<Task> _inspected = new(ReferenceEqualityComparer.Instance);
        internal static async Task<NativeReadinessControl> CreateAsync(NativeReadinessStartup? home)
        {
            var control = new NativeReadinessControl { _home = home };
            try
            {
                control.Context = await Rig.CreateAsync();
                control.Window = new Window(); // Retain before actual setters or further acquisitions.
                control.Window.Width = 960; control.Window.Height = 720;
                control.Page = new(control.Context.Service, control.Context.Canonical, control.Context.Space.Id,
                    control.Context.Conversation.Id, control.Forwarder, expectedOriginalTaskId: control.Context.Snapshot.TaskId,
                    expectedOriginalExecutionId: control.Context.Snapshot.ExecutionId);
                control.Frame = await control.Page.AcquireOriginalNativeReadinessAsync(page =>
                    NativeCanonicalTaskSceneReadiness.BindOriginal(page, home, control.Actors,
                        control.Connection.Token, control.WindowLifetime.Token));
                control.Forwarder.Owner = control.Frame;
                control.Window.Content = control.Page; control.Window.Show();
                return control;
            }
            catch (Exception acquired)
            {
                try { await control.DisposeAsync(); }
                catch (Exception cleanup) { throw new AggregateException(acquired, cleanup); }
                throw;
            }
        }
        internal Task Start() => _activation = Page.ActivateAsync(CancellationToken.None);
        internal async Task<Exception?> InspectAsync(Task actual)
        { _inspected.Add(actual); return await InspectNativeOriginalAsync(actual); }
        public async ValueTask DisposeAsync()
        {
            _home?.Release();
            var failures = new List<Exception>();
            var actuals = new List<Task>();
            if (_activation is not null) actuals.Add(_activation);
            if (Forwarder.ActualCheck is { } check) actuals.Add(check);
            if (Page is not null)
            {
                try { Page.RequestRetirement(); } catch (Exception error) { failures.Add(error); }
                try { actuals.Add(Page.CloseAndDrainAsync()); } catch (Exception error) { failures.Add(error); }
            }
            if (Frame is not null)
            {
                try { Frame.RequestRetirement(); } catch (Exception error) { failures.Add(error); }
                try { actuals.Add(Frame.CloseAndDrainAsync()); } catch (Exception error) { failures.Add(error); }
            }
            foreach (var actual in actuals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            {
                var cause = await JoinNativeOriginalTerminalAsync(actual);
                if (cause is not null && !_inspected.Contains(actual)) failures.Add(cause);
            }
            if (Window is not null)
            {
                try { Window.Content = null; } catch (Exception error) { failures.Add(error); }
                try { Window.Close(); } catch (Exception error) { failures.Add(error); }
            }
            try { if (Context is not null) await Context.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            WindowLifetime.Dispose(); Connection.Dispose();
            if (failures.Count > 0) throw new AggregateException(failures);
        }
    }
}
