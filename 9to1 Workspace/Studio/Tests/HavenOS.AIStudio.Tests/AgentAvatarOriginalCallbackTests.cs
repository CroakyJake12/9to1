using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

[Collection("StudioNative")]
public sealed class AgentAvatarOriginalCallbackTests
{
    private static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");

    [Fact]
    public Task Actual_native_tick_close_waits_the_same_held_den_read_before_bitmap_and_store_retirement() =>
        Native(async () =>
        {
            await WithFixture(async fixture =>
            {
                var controls = new List<AgentAvatarPreviewControl>();
                var control = AgentAvatarPreviewControl.CreateOwned(fixture.Preview, controls);
                var window = new Window { Content = control };
                Task? original = null; Task? close = null;
                Exception? primary = null; List<Exception> cleanup = []; bool cancellationAsserted = false;
                try
                {
                    window.Show(); fixture.Policy.Arm();
                    await fixture.Policy.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    original = control.WhenAnimationIdleAsync();
                    Assert.Same(original, control.WhenAnimationIdleAsync());
                    Assert.False(original.IsCompleted); Assert.NotNull(control.Source);
                    close = control.CloseAndDrainAsync();
                    Assert.Same(close, control.CloseAndDrainAsync());
                    await fixture.Policy.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.False(original.IsCompleted); Assert.False(close.IsCompleted);
                    Assert.NotNull(control.Source);
                    fixture.Policy.Release.TrySetResult();
                    var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
                    Assert.Equal(fixture.Policy.OriginalToken, cancellation.CancellationToken);
                    await close;
                    Assert.Null(control.Source); Assert.Null(fixture.Preview.Frame);
                    Assert.Single(controls); Assert.Same(control, controls[0]);
                    Assert.False(fixture.Policy.StoreDisposed);
                    Assert.Equal("animated", (await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", "animated"))!.Id);
                    cancellationAsserted = true;
                }
                catch (Exception error) { primary = error; }
                finally
                {
                    fixture.Policy.Release.TrySetResult();
                    if (original is not null)
                        try { await original; }
                        catch (OperationCanceledException error) when (cancellationAsserted && primary is null && fixture.Policy.Cancelled.Task.IsCompletedSuccessfully &&
                            error.CancellationToken == fixture.Policy.OriginalToken) { }
                        catch (Exception error) { Add(cleanup, error); }
                    try { await control.CloseAndDrainAsync(); } catch (Exception error) { Add(cleanup, error); }
                    try { window.Close(); } catch (Exception error) { Add(cleanup, error); }
                }
                Throw(primary, cleanup);
            });
        });

    [Fact]
    public Task Actual_tick_failure_and_reentrant_native_detach_failure_are_both_retained() =>
        Native(async () =>
        {
            await WithFixture(async fixture =>
            {
                var control = new AgentAvatarPreviewControl(fixture.Preview);
                var window = new Window { Content = control };
                var callbackFailure = new InvalidOperationException("actual held owning ACL failure");
                var detachFailure = new InvalidOperationException("actual native Source notification failure");
                Task? original = null; Task? close = null; Task? reentered = null;
                Exception? primary = null; List<Exception> cleanup = []; bool failureAsserted = false;
                void ThrowOnDetach(object? sender, AvaloniaPropertyChangedEventArgs change)
                {
                    if (change.Property == Image.SourceProperty && control.Source is null)
                    { reentered = control.CloseAndDrainAsync(); throw detachFailure; }
                }
                try
                {
                    window.Show(); fixture.Policy.Arm(callbackFailure);
                    await fixture.Policy.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    original = control.WhenAnimationIdleAsync();
                    control.PropertyChanged += ThrowOnDetach;
                    close = control.CloseAndDrainAsync();
                    await fixture.Policy.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.False(close.IsCompleted);
                    fixture.Policy.Release.TrySetResult();
                    Assert.Same(callbackFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => original));
                    var failure = await Assert.ThrowsAsync<AggregateException>(() => close);
                    Assert.Equal(2, failure.InnerExceptions.Count);
                    Assert.Same(callbackFailure, failure.InnerExceptions[0]); Assert.Same(detachFailure, failure.InnerExceptions[1]);
                    Assert.Same(close, reentered); Assert.Same(close, control.CloseAndDrainAsync());
                    Assert.Null(control.Source); Assert.Null(fixture.Preview.Frame); failureAsserted = true;
                }
                catch (Exception error) { primary = error; }
                finally
                {
                    fixture.Policy.Release.TrySetResult();
                    control.PropertyChanged -= ThrowOnDetach;
                    if (original is not null) try { await original; }
                        catch (Exception error) { if (!(failureAsserted && primary is null && ReferenceEquals(error, callbackFailure))) Add(cleanup, error); }
                    try { await control.CloseAndDrainAsync(); }
                    catch (AggregateException error) when (error.InnerExceptions.Count == 2 &&
                        ReferenceEquals(error.InnerExceptions[0], callbackFailure) &&
                        ReferenceEquals(error.InnerExceptions[1], detachFailure) && primary is null && failureAsserted) { }
                    catch (Exception error) { Add(cleanup, error); }
                    try { window.Close(); } catch (Exception error) { Add(cleanup, error); }
                }
                Throw(primary, cleanup);
            });
        });

    [Fact]
    public Task Foreign_thread_retirement_refuses_before_poisoning_the_native_control() =>
        Native(async () =>
        {
            await WithFixture(async fixture =>
            {
                var control = new AgentAvatarPreviewControl(fixture.Preview);
                Exception? primary = null; List<Exception> cleanup = [];
                try
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => control.CloseAndDrainAsync()));
                    Assert.NotNull(control.Source);
                    await control.CloseAndDrainAsync(); Assert.Null(control.Source);
                }
                catch (Exception error) { primary = error; }
                finally { try { await control.CloseAndDrainAsync(); } catch (Exception error) { Add(cleanup, error); } }
                Throw(primary, cleanup);
            });
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Actual_native_refresh_notification_close_prevents_later_frame_publication(bool closeOnName) =>
        Native(async () =>
        {
            await WithFixture(async fixture =>
            {
                var control = new AgentAvatarPreviewControl(fixture.Preview);
                var window = new Window { Content = control };
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task? original = null; Task? close = null;
                bool actualFrameAtClose = false; int framesAfterClose = 0;
                Exception? primary = null; List<Exception> cleanup = [];
                if (closeOnName) Avalonia.Automation.AutomationProperties.SetName(control, "Pending real refresh");
                void Observe(object? sender, AvaloniaPropertyChangedEventArgs change)
                {
                    if (change.Property == Image.SourceProperty && control.Source is not null && close is not null)
                        framesAfterClose++;
                    var boundary = closeOnName
                        ? change.Property == Avalonia.Automation.AutomationProperties.NameProperty
                        : change.Property == Image.SourceProperty && control.Source is null;
                    if (boundary && close is null)
                    {
                        actualFrameAtClose = fixture.Preview.Frame is not null;
                        original = control.WhenAnimationIdleAsync();
                        close = control.CloseAndDrainAsync();
                        entered.TrySetResult();
                    }
                }
                control.PropertyChanged += Observe;
                try
                {
                    window.Show();
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.True(actualFrameAtClose); Assert.NotNull(original); Assert.NotNull(close);
                    Assert.Same(close, control.CloseAndDrainAsync());
                    await original!;
                    await close!;
                    Assert.Equal(0, framesAfterClose); Assert.Null(control.Source); Assert.Null(fixture.Preview.Frame);
                    Assert.Equal("animated", (await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", "animated"))!.Id);
                }
                catch (Exception error) { primary = error; }
                finally
                {
                    control.PropertyChanged -= Observe;
                    if (original is not null) try { await original; } catch (Exception error) { Add(cleanup, error); }
                    try { await control.CloseAndDrainAsync(); } catch (Exception error) { Add(cleanup, error); }
                    try { window.Close(); } catch (Exception error) { Add(cleanup, error); }
                }
                Throw(primary, cleanup);
            });
        });

    private static async Task Native(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(StudioAvatarFrameTestApp));
        Task? originalCallback = null; Task<bool>? originalDispatch = null;
        Exception? primary = null; List<Exception> cleanup = [];
        try
        {
            originalDispatch = session.Dispatch<bool>(async () =>
            { originalCallback = body(); await originalCallback; return true; }, CancellationToken.None);
            await originalDispatch;
        }
        catch (Exception error) { primary = error; }
        if (originalCallback is not null) try { await originalCallback; } catch (Exception error) { Add(cleanup, error); }
        if (originalDispatch is not null) try { await originalDispatch; } catch (Exception error) { Add(cleanup, error); }
        try { await session.DisposeAsync(); } catch (Exception error) { Add(cleanup, error); }
        Throw(primary, cleanup);
    }

    private static async Task WithFixture(Func<Fixture, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-actual-animation-original-" + Guid.NewGuid().ToString("N"));
        DenStore? store = null; AgentAvatarPreview? preview = null; HeldPolicy? policy = null;
        Exception? primary = null; List<Exception> cleanup = []; bool storeClosed = false;
        try
        {
            store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            policy = new HeldPolicy();
            var den = new DulcheDen(store, policy, "owner");
            var agent = await den.SaveAsync(new AgentDefinitionRecord { Id = "animated", NamespaceId = "personal", DisplayName = "Animated", Version = "1" }, 0, "create");
            var attachment = await den.AddAttachmentAsync("personal", agent.Id, DenAgentPresentationAssets.AgentOwnerKind, "image/gif", Gif, "attach");
            var assets = new DenAgentPresentationAssets(den);
            preview = new AgentAvatarPreview(assets);
            await preview.LoadAsync("personal", new(agent.Id, agent.Revision, attachment.Id, "Animated avatar", "Idle", true, "idle"), default);
            Assert.True(preview.IsAnimating); Assert.NotNull(preview.Frame);
            await body(new(preview, policy, den));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            policy?.Release.TrySetResult();
            if (preview is not null) try { await preview.DisposeAsync(); } catch (Exception error) { Add(cleanup, error); }
            if (store is not null)
                try { await store.DisposeAsync(); storeClosed = true; if (policy is not null) policy.StoreDisposed = true; }
                catch (Exception error) { Add(cleanup, error); }
            if (store is null || storeClosed)
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { Add(cleanup, error); }
        }
        Throw(primary, cleanup);
    }

    private sealed record Fixture(AgentAvatarPreview Preview, HeldPolicy Policy, DulcheDen Den);
    private sealed class HeldPolicy : IDenAccessPolicy
    {
        private bool _armed; private Exception? _failure;
        public CancellationToken OriginalToken { get; private set; }
        public bool StoreDisposed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm(Exception? failure = null) { _failure = failure; _armed = true; }
        public async ValueTask<bool> IsAllowedAsync(string principal, string ns, string id, DenPermission permission, CancellationToken cancellationToken = default)
        {
            if (_armed)
            {
                _armed = false; OriginalToken = cancellationToken; Entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Cancelled.TrySetResult(); if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw(); throw; }
                finally { await Release.Task; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return principal == "owner" && ns == "personal";
        }
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(previous => ReferenceEquals(previous, error))) errors.Add(error); }
    private static void Throw(Exception? primary, List<Exception> cleanup)
    {
        List<Exception> errors = []; if (primary is not null) Add(errors, primary);
        foreach (var error in cleanup) Add(errors, error);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }
}

public sealed class StudioAvatarFrameTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<StudioAvatarFrameTestApp>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
