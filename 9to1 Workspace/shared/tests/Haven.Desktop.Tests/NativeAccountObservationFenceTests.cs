using System.Reflection;
using System.Text.Json;
using Haven.Desktop.Accounts;
using HavenOS.Home;
using HavenOS.Home.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NineToOne.Accounts.Native;
using NineToOne.Accounts.Remote;
using NineToOne.Web.Accounts;
using NineToOne.Web.Services;

namespace Haven.Desktop.Tests;

// Presentation counterexamples only. Scripted wire records do not prove live CAKE auth.
public sealed class NativeAccountObservationFenceTests
{
    [Fact]
    public async Task Onboarding_awaits_the_shared_session_and_records_only_its_current_authenticated_result()
    {
        var pending = new TaskCompletionSource<NativeCakeAccountSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new ScriptedSession { IsAvailable = true, OnSignIn = ct => pending.Task.WaitAsync(ct) };
        var adapter = new NativeCakeOnboardingAdapter(session);
        var root = Path.Combine(Path.GetTempPath(), "onboarding-account-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new HomeOnboardingService(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), browserLauncher: adapter);
            var controller = new HomeOnboardingCuiController(service, "synthetic-profile");
            var original = controller.DispatchWithOutcomeAsync("onboarding.signin", null, TestContext.Current.CancellationToken).AsTask();
            Assert.False(original.IsCompleted);
            Assert.False(adapter.IsSignedIn);
            Assert.Equal(1, session.SignInCalls);
            var authenticated = Snapshot();
            session.Current = authenticated;
            pending.SetResult(authenticated);
            Assert.True((await original).Succeeded);
            Assert.True((await controller.DispatchWithOutcomeAsync("onboarding.complete", null, TestContext.Current.CancellationToken)).Succeeded);
            var saved = await service.ReadCompletionAsync("synthetic-profile", TestContext.Current.CancellationToken);
            Assert.True(saved.Completion!.SignedIn);
            Assert.Same(authenticated, session.Current);
            Assert.True(await adapter.LaunchAuthAsync(TestContext.Current.CancellationToken));
            Assert.Equal(1, session.SignInCalls); // Existing shared session, no second OAuth flow.
        }
        finally { pending.TrySetCanceled(TestContext.Current.CancellationToken); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Onboarding_does_not_confuse_browser_return_with_a_current_session()
    {
        var session = new ScriptedSession { IsAvailable = false, OnSignIn = _ => Task.FromResult(Snapshot()) };
        var adapter = new NativeCakeOnboardingAdapter(session);
        Assert.False(await adapter.LaunchAuthAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, session.SignInCalls);
        session.IsAvailable = true;
        Assert.False(await adapter.LaunchAuthAsync(TestContext.Current.CancellationToken));
        Assert.False(adapter.IsSignedIn); // A returned value without current session custody is insufficient.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.LaunchAuthAsync(cancelled.Token));
        Assert.Equal(1, session.SignInCalls);
    }

    [AvaloniaFact]
    public async Task Onboarding_status_is_below_the_actual_step_in_the_mounted_CUI()
    {
        var root = Path.Combine(Path.GetTempPath(), "onboarding-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var service = new HomeOnboardingService(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
        var mounted = FirstRunOnboardingFlow.Mount(service, "synthetic-profile");
        var window = new Window { Width = 900, Height = 650, Content = mounted.Root };
        try
        {
            window.Show(); window.UpdateLayout();
            Control Find(string name) => mounted.Root.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);
            var welcome = Find("onboarding-welcome"); var status = mounted.Root.GetVisualDescendants().OfType<Control>().Single(c => c.Name != null && c.Name.StartsWith("onboarding-status"));
            var stepPosition = welcome.TranslatePoint(default, mounted.Root)!.Value;
            var statusPosition = status.TranslatePoint(default, mounted.Root)!.Value;
            Assert.True(welcome.Bounds.Height > 100);
            Assert.True(statusPosition.Y >= stepPosition.Y + welcome.Bounds.Height,
                "Status text must follow the current step, not overlap its heading or supporting copy.");
        }
        finally { window.Close(); await mounted.RetireAsync(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Failed_onboarding_signin_cannot_persist_completion_before_the_local_only_decision()
    {
        var root = Path.Combine(Path.GetTempPath(), "onboarding-failed-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var session = new ScriptedSession { IsAvailable = true,
                OnSignIn = _ => Task.FromException<NativeCakeAccountSnapshot>(new IOException("synthetic offline")) };
            var service = new HomeOnboardingService(new FileHomeCoreStateStore(Path.Combine(root, "home.json")),
                browserLauncher: new NativeCakeOnboardingAdapter(session));
            var controller = new HomeOnboardingCuiController(service, "synthetic-profile");
            var failure = await controller.DispatchWithOutcomeAsync("onboarding.signin", null, TestContext.Current.CancellationToken);
            Assert.False(failure.Succeeded);
            Assert.Equal("SignInFailed", failure.Code);
            Assert.False((await controller.DispatchWithOutcomeAsync("onboarding.complete", null, TestContext.Current.CancellationToken)).Succeeded);
            Assert.Equal(HomeOnboardingReadState.NotCompleted,
                (await service.ReadCompletionAsync("synthetic-profile", TestContext.Current.CancellationToken)).State);
            Assert.True((await controller.DispatchWithOutcomeAsync("onboarding.notnow", null, TestContext.Current.CancellationToken)).Succeeded);
            Assert.True((await controller.DispatchWithOutcomeAsync("onboarding.complete", null, TestContext.Current.CancellationToken)).Succeeded);
            Assert.False((await service.ReadCompletionAsync("synthetic-profile", TestContext.Current.CancellationToken)).Completion!.SignedIn);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Onboarding_successful_retry_clears_the_rendered_failure_before_model_download_choice()
    {
        var root = Path.Combine(Path.GetTempPath(), "onboarding-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var session = new ScriptedSession { IsAvailable = true,
            OnSignIn = _ => Task.FromException<NativeCakeAccountSnapshot>(new IOException("synthetic offline")) };
        var service = new HomeOnboardingService(new FileHomeCoreStateStore(Path.Combine(root, "home.json")),
            browserLauncher: new NativeCakeOnboardingAdapter(session));
        var mounted = FirstRunOnboardingFlow.Mount(service, "synthetic-profile");
        var window = new Window { Width = 900, Height = 650, Content = mounted.Root };
        T Find<T>(string name) where T : Control => mounted.Root.GetVisualDescendants().OfType<T>()
            .Single(c => c.Name == name && c.IsEffectivelyVisible);
        async Task Click(string name)
        {
            var button = Find<Button>(name);
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await mounted.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        }
        try
        {
            window.Show(); window.UpdateLayout();
            await Click("onboarding-start");
            await Click("onboarding-signin-primary");
            Assert.Contains("Sign-in did not complete", mounted.Root.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name != null && c.Name.StartsWith("onboarding-status")).Text);
            Assert.NotNull(Find<Control>("onboarding-signin"));
            session.OnSignIn = _ =>
            {
                var current = Snapshot(); session.Current = current;
                return Task.FromResult(current);
            };
            await Click("onboarding-signin-primary");
            Assert.Equal(2, session.SignInCalls);
            Assert.NotNull(Find<Control>("onboarding-download"));
            Assert.True(string.IsNullOrEmpty(mounted.Root.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name != null && c.Name.StartsWith("onboarding-status")).Text));
            Assert.False(mounted.CompletionTask.IsCompleted);
            await Click("onboarding-continue");
            Assert.True(mounted.CompletionTask.IsCompletedSuccessfully);
            Assert.True((await service.ReadCompletionAsync("synthetic-profile", TestContext.Current.CancellationToken)).Completion!.SignedIn);
        }
        finally { window.Close(); await mounted.RetireAsync(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Cleared_generation_rejects_original_delayed_read_and_creates_no_followup()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        await using var view = new AccountBrowserBindings(new Transport(action =>
        {
            calls.Add(action); reached.TrySetResult(); return raw.Task;
        }), write => { write(); return Task.CompletedTask; });
        var original = view.DispatchAsync("Refresh", null, TestContext.Current.CancellationToken).AsTask();
        await reached.Task;
        view.ClearAccountObservations("The actual native session expired.");
        raw.SetResult(Current()); // Original backend ignored cancellation/generation retirement.
        await original;
        Assert.Equal(new[] { "GetCurrent" }, calls);
        Assert.True(view.TryGetValue("HasAccount", out var visible)); Assert.Equal(false, visible);
        Assert.True(view.TryGetValue("Sessions", out var rows)); Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<JsonElement>>(rows));
    }

    [Fact]
    public async Task Clearing_during_original_queued_presentation_prevents_old_command_from_using_new_generation()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? originalPublication = null; var first = true; var reads = 0;
        await using var view = new AccountBrowserBindings(new Transport(_ =>
        { reads++; return Task.FromResult(Current()); }), write =>
        {
            if (first) { first = false; originalPublication = write; reached.TrySetResult(); return queued.Task; }
            write(); return Task.CompletedTask;
        });
        var original = view.DispatchAsync("Refresh", null, TestContext.Current.CancellationToken).AsTask();
        await reached.Task;
        view.ClearAccountObservations("The actual native session changed.");
        Assert.NotNull(originalPublication); originalPublication(); queued.SetResult();
        await original; // SAME queued callback and dispatch settle; neither is abandoned.
        Assert.Equal(0, reads);
        Assert.True(view.TryGetValue("HasAccount", out var visible)); Assert.Equal(false, visible);
        Assert.True(view.TryGetValue("CanEdit", out var edit)); Assert.Equal(false, edit);
    }

    [Fact]
    public async Task Clear_removes_profile_draft_session_selection_and_confirmation_but_allows_explicit_fresh_read()
    {
        await using var view = new AccountBrowserBindings(new Transport(action => Task.FromResult(action switch
        {
            "GetCurrent" => Current(),
            "GetProfile" => Ok(new { profile = new { accountId = AccountId, name = "Synthetic", username = "synthetic", icon = "", pronouns = "", job = "", revision = 1 } }),
            "ListSessions" => Ok(new { sessions = new[] { new { accountId = AccountId, sessionId = SessionId, deviceName = "Synthetic" } } }),
            _ => throw new InvalidOperationException("An observation control must not perform an auth mutation.")
        })), write => { write(); return Task.CompletedTask; });
        await view.DispatchAsync("Refresh", null, TestContext.Current.CancellationToken);
        Assert.True(view.TrySetValue("Draft.name", "Unsaved synthetic draft"));
        await view.DispatchAsync("SelectSession", JsonSerializer.SerializeToElement(new { sessionId = SessionId }), TestContext.Current.CancellationToken);
        Assert.True(view.TryGetValue("HasConfirmation", out var confirmation)); Assert.Equal(true, confirmation);
        view.ClearAccountObservations("The genuine session changed.");
        foreach (var path in new[] { "HasAccount", "HasProfile", "HasChanges", "HasConfirmation", "CanSessionActions" })
        { Assert.True(view.TryGetValue(path, out var value)); Assert.Equal(false, value); }
        Assert.True(view.TryGetValue("Draft.name", out var draft)); Assert.Equal("", draft);
        await view.DispatchAsync("Refresh", null, TestContext.Current.CancellationToken); // New explicit command, rather than resuming old work.
        Assert.True(view.TryGetValue("HasAccount", out var current)); Assert.Equal(true, current);
    }

    private const string AccountId = "12345678-1234-4234-8234-123456789abc";
    private const string SessionId = "87654321-4321-4321-8321-cba987654321";
    private static JsonElement Current() => Ok(new { accountId = AccountId, displayName = "Synthetic" });
    private static JsonElement Ok(object body) => JsonSerializer.SerializeToElement(new { ok = true, status = 200, body });
    private sealed class Transport(Func<string, Task<JsonElement>> original) : IAccountBrowserTransport
    {
        public Task<JsonElement> InvokeAsync(string action, JsonElement? arguments, CancellationToken cancellationToken) => original(action);
    }

    // Native-adapter presentation counterexamples. The internal NativeAccountBindingsAdapter is
    // reached through reflection (no InternalsVisibleTo exists). A scripted INativeCakeAccountSession
    // proves presentation boundaries only; it never performs or implies a live CAKE authentication.
    private static readonly Guid AccountGuid = Guid.Parse("12345678-1234-4234-8234-123456789abc");
    private static readonly Guid SessionGuid = Guid.Parse("87654321-4321-4321-8321-cba987654321");
    private static Type AdapterType => typeof(NativeAccountTransport).Assembly
        .GetType("Haven.Desktop.Accounts.NativeAccountBindingsAdapter", throwOnError: true)!;
    private static object CreateAdapter(AccountBrowserBindings view, INativeCakeAccountSession session)
    {
        var ctor = AdapterType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(AccountBrowserBindings), typeof(INativeCakeAccountSession), typeof(CancellationToken) }, null)!;
        return ctor.Invoke(new object?[] { view, session, CancellationToken.None })!;
    }
    private static bool TryGet(object adapter, string path, out object? value)
    {
        var m = AdapterType.GetMethod("TryGetValue", BindingFlags.Public | BindingFlags.Instance)!;
        var args = new object?[] { path, null };
        var ok = (bool)m.Invoke(adapter, args)!;
        value = args[1];
        return ok;
    }
    private static bool? IsActionAvailable(object adapter, string command)
    {
        var m = AdapterType.GetMethod("IsActionAvailable", BindingFlags.Public | BindingFlags.Instance)!;
        return (bool?)m.Invoke(adapter, new object?[] { command });
    }
    private static Task Dispatch(object adapter, string command) =>
        ((ValueTask)AdapterType.GetMethod("DispatchAsync", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(adapter, new object?[] { command, null, TestContext.Current.CancellationToken })!).AsTask();
    private static void Seal(object adapter) =>
        AdapterType.GetMethod("Seal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(adapter, null);
    private static void RefreshPresentationFence(object adapter) =>
        AdapterType.GetMethod("RefreshPresentationFence", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(adapter, null);
    private static NativeCakeAccountSnapshot Snapshot(Guid? sessionId = null) =>
        new(AccountGuid, sessionId ?? SessionGuid, "Synthetic", DateTimeOffset.UtcNow.AddHours(8));
    private static JsonElement AccountRecord(string action) => action switch
    {
        "GetCurrent" => Current(),
        "GetProfile" => Ok(new { profile = new { accountId = AccountId, name = "Synthetic", username = "synthetic", icon = "", pronouns = "", job = "", revision = 1 } }),
        "ListSessions" => Ok(new { sessions = new[] { new { accountId = AccountId, sessionId = SessionId, deviceName = "Synthetic" } } }),
        _ => throw new InvalidOperationException("An observation control must not perform an auth mutation.")
    };
    private static AccountBrowserBindings CreateView(ScriptedSession session, IAccountBrowserTransport transport,
        bool configureSignIn = true) => new(transport, write => { write(); return Task.CompletedTask; },
            configureSignIn ? async ct => { await session.SignInAsync(ct); } : null);
    private static string Text(object adapter, string path)
    {
        Assert.True(TryGet(adapter, path, out var value));
        return Assert.IsType<string>(value);
    }
    private static void AssertReadOnly(object adapter)
    {
        Assert.Contains("read-only", Text(adapter, "ConnectionRecovery"));
        Assert.DoesNotContain("are saved", Text(adapter, "ConnectionRecovery"));
        foreach (var path in new[] { "CanEdit", "CanSave", "HasChanges", "Conflict" })
        { Assert.True(TryGet(adapter, path, out var value)); Assert.Equal(false, value); }
    }
    private sealed class ScriptedSession : INativeCakeAccountSession
    {
        public bool IsAvailable { get; set; }
        public NativeCakeAccountSnapshot? Current { get; set; }
        public int SignInCalls;
        public Func<CancellationToken, Task<NativeCakeAccountSnapshot>>? OnSignIn { get; set; }
        public bool TryGetCurrentSnapshot(out NativeCakeAccountSnapshot? snapshot) { snapshot = Current; return Current is not null; }
        public Task<NativeCakeAccountSnapshot> SignInAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); SignInCalls++; return OnSignIn?.Invoke(ct) ?? Task.FromResult(Current ?? throw new InvalidOperationException("no scripted current")); }
        public Task<ApiResult<RemoteCurrent>> CurrentAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteProfile>> ProfileAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteSession[]>> SessionsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteMutationAcknowledgement>> SignOutAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeSessionAsync(Guid selectedSessionId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeOtherSessionsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task CloseAndDrainAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Unavailable_native_session_presents_primary_unavailable_guidance_and_refuses_sign_in()
    {
        var calls = 0;
        var session = new ScriptedSession { IsAvailable = false, Current = Snapshot() };
        await using var view = CreateView(session, new Transport(_ =>
        { calls++; throw new InvalidOperationException("No transport while the session is unavailable."); }));
        using var adapter = (IDisposable)CreateAdapter(view, session);
        Assert.Equal("Native CAKE sign-in is unavailable.", Text(adapter, "ConnectionSummary"));
        Assert.Contains("Access transport is not configured", Text(adapter, "ConnectionRecovery"));
        Assert.True(TryGet(adapter, "CanSignIn", out var can)); Assert.Equal(false, can);
        Assert.False(IsActionAvailable(adapter, "RequestSignIn") == true);
        await Dispatch(adapter, "RequestSignIn"); // A callback exists; the native availability fence must refuse it.
        Assert.Equal(0, session.SignInCalls);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Available_without_sign_in_callback_does_not_recommend_a_nonexistent_action()
    {
        var session = new ScriptedSession { IsAvailable = true, Current = Snapshot() };
        await using var view = CreateView(session, new Transport(_ =>
            throw new InvalidOperationException("No transport before a genuine sign-in action.")), configureSignIn: false);
        using var adapter = (IDisposable)CreateAdapter(view, session);
        Assert.Equal("This native client is not connected to your 9to1 account.", Text(adapter, "ConnectionSummary"));
        Assert.Contains("Sign-in is not available", Text(adapter, "ConnectionRecovery"));
        Assert.DoesNotContain("Use Sign in", Text(adapter, "ConnectionRecovery"));
        Assert.True(TryGet(adapter, "CanSignIn", out var can)); Assert.Equal(false, can);
        await Dispatch(adapter, "RequestSignIn");
        Assert.Equal(0, session.SignInCalls);
    }

    [Fact]
    public async Task Configured_sign_in_is_offered_and_dispatches_the_same_session_then_observes_the_account()
    {
        var calls = new List<string>();
        var session = new ScriptedSession { IsAvailable = true, Current = Snapshot() };
        await using var view = CreateView(session, new Transport(action =>
        { calls.Add(action); return Task.FromResult(AccountRecord(action)); }));
        using var adapter = (IDisposable)CreateAdapter(view, session);
        Assert.Equal("Sign in to manage your profile and sessions.", Text(adapter, "ConnectionSummary"));
        Assert.Contains("Use Sign in", Text(adapter, "ConnectionRecovery"));
        Assert.True(TryGet(adapter, "CanSignIn", out var can)); Assert.Equal(true, can);
        Assert.True(IsActionAvailable(adapter, "RequestSignIn") == true);
        await Dispatch(adapter, "RequestSignIn");
        Assert.Equal(1, session.SignInCalls);
        Assert.Equal(new[] { "GetCurrent", "GetProfile", "ListSessions" }, calls);
        Assert.Equal("Your 9to1 account is connected (read-only).", Text(adapter, "ConnectionSummary"));
        AssertReadOnly(adapter);
    }

    [Fact]
    public async Task Connected_read_only_presenter_declares_read_only_and_never_promises_saved_profile()
    {
        var session = new ScriptedSession { IsAvailable = true, Current = Snapshot() };
        await using var view = CreateView(session, new Transport(action => Task.FromResult(AccountRecord(action))));
        using var adapter = (IDisposable)CreateAdapter(view, session);
        await Dispatch(adapter, "Refresh"); // Establish both real observation fences through the production command.
        Assert.True(TryGet(adapter, "HasAccount", out var has)); Assert.Equal(true, has);
        Assert.Equal("Your 9to1 account is connected (read-only).", Text(adapter, "ConnectionSummary"));
        AssertReadOnly(adapter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Matching_native_snapshot_without_a_current_account_observation_never_claims_connected(bool failedRead)
    {
        var session = new ScriptedSession { IsAvailable = true, Current = Snapshot() };
        await using var view = CreateView(session, new Transport(action => Task.FromResult(failedRead
            ? JsonSerializer.SerializeToElement(new { ok = false, status = 401, error = new { code = "AuthenticationRequired" } })
            : AccountRecord(action))));
        using var adapter = (IDisposable)CreateAdapter(view, session);
        await Dispatch(adapter, "Refresh");
        if (!failedRead)
        {
            Assert.True(TryGet(adapter, "HasAccount", out var before)); Assert.Equal(true, before);
            view.ClearAccountObservations("The current observation was explicitly cleared.");
        }
        Assert.True(session.TryGetCurrentSnapshot(out _)); // Same actual native snapshot survives the failed/cleared read.
        Assert.True(TryGet(adapter, "HasAccount", out var has)); Assert.Equal(false, has);
        Assert.DoesNotContain("connected (read-only)", Text(adapter, "ConnectionSummary"));
        Assert.Equal("Sign in to manage your profile and sessions.", Text(adapter, "ConnectionSummary"));
    }

    [Theory]
    [InlineData("connected")]
    [InlineData("unavailable")]
    [InlineData("retired")]
    [InlineData("changed")]
    public async Task Original_pending_refresh_keeps_native_read_only_and_retirement_availability_fences(string state)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var session = new ScriptedSession { IsAvailable = true, Current = Snapshot() };
        await using var view = CreateView(session, new Transport(action =>
        {
            if (action == "GetCurrent" && ++reads == 2) { reached.TrySetResult(); return raw.Task; }
            return Task.FromResult(AccountRecord(action));
        }));
        using var adapter = (IDisposable)CreateAdapter(view, session);
        await Dispatch(adapter, "Refresh");
        Assert.Equal("Your 9to1 account is connected (read-only).", Text(adapter, "ConnectionSummary"));
        var original = Dispatch(adapter, "Refresh");
        try
        {
            await reached.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(original.IsCompleted);
            if (state == "unavailable") session.IsAvailable = false;
            else if (state == "retired") Seal(adapter);
            else if (state == "changed") { session.Current = Snapshot(Guid.NewGuid()); RefreshPresentationFence(adapter); }
            if (state == "unavailable")
            {
                Assert.Equal("Native CAKE sign-in is unavailable.", Text(adapter, "ConnectionSummary"));
                Assert.Contains("Access transport is not configured", Text(adapter, "ConnectionRecovery"));
            }
            else if (state == "retired")
            {
                Assert.Contains("retired", Text(adapter, "ConnectionSummary"));
                Assert.Contains("retired", Text(adapter, "ConnectionRecovery"));
            }
            else
            {
                Assert.Equal("Checking your account…", Text(adapter, "ConnectionSummary"));
                AssertReadOnly(adapter);
            }
            Assert.DoesNotContain("are saved", Text(adapter, "ConnectionRecovery"));
            if (state != "connected")
            { Assert.True(TryGet(adapter, "HasAccount", out var has)); Assert.Equal(false, has); }
        }
        finally
        {
            raw.TrySetResult(Current());
            await original; // Settle the SAME issued command; never leave the blocked backend or a replacement task behind.
        }
        if (state == "changed")
        {
            Assert.True(TryGet(adapter, "HasAccount", out var has)); Assert.Equal(false, has);
            Assert.DoesNotContain("connected (read-only)", Text(adapter, "ConnectionSummary"));
        }
    }

    [Fact]
    public async Task Sealed_retired_owner_never_reports_a_stale_connected_summary()
    {
        var session = new ScriptedSession { IsAvailable = true, Current = Snapshot() };
        await using var view = CreateView(session, new Transport(action => Task.FromResult(AccountRecord(action))));
        using var adapter = (IDisposable)CreateAdapter(view, session);
        await Dispatch(adapter, "Refresh");
        Assert.Equal("Your 9to1 account is connected (read-only).", Text(adapter, "ConnectionSummary"));
        Seal(adapter);
        Assert.Contains("retired", Text(adapter, "ConnectionSummary"));
        Assert.True(TryGet(adapter, "HasAccount", out var has)); Assert.Equal(false, has);
        Assert.False(IsActionAvailable(adapter, "RequestSignIn") == true);
    }

    [Fact]
    public async Task Cleared_observation_fence_hides_connected_presentation_after_session_replace()
    {
        var session = new ScriptedSession { IsAvailable = true, Current = Snapshot() };
        await using var view = CreateView(session, new Transport(action => Task.FromResult(AccountRecord(action))));
        using var adapter = (IDisposable)CreateAdapter(view, session);
        await Dispatch(adapter, "Refresh");
        Assert.Equal("Your 9to1 account is connected (read-only).", Text(adapter, "ConnectionSummary"));
        session.Current = Snapshot(Guid.NewGuid());
        RefreshPresentationFence(adapter);
        Assert.Equal("Sign in to manage your profile and sessions.", Text(adapter, "ConnectionSummary"));
        Assert.True(TryGet(adapter, "HasAccount", out var has)); Assert.Equal(false, has);
    }
}
