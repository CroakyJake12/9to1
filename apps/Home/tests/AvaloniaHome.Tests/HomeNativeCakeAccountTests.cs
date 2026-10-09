#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NineToOne.Accounts.Native;
using NineToOne.Accounts.Remote;
using Xunit;

namespace AvaloniaHome.Tests;

// Scripted typed session ports verify Home custody/presentation only. They issue no token,
// live-session evidence, Access credential, OS actor, Home permission or auth acceptance.
public sealed class HomeNativeCakeAccountTests
{
    [Fact]
    public void Uses_the_maintained_native_public_client_and_exact_loopback_contract()
    {
        var options = HomeNativeCakeAccountOwner.ExactOptions().Capture();
        Assert.Equal("https://cake-id-release-validation.jcbailey008.workers.dev/api/auth", options.Issuer);
        Assert.Equal("xwKChQGPLRpdokMkLAMvzimLZxnwmnJt", options.PublicClientId);
        Assert.Equal("http://127.0.0.1:43821/cake-id/callback/", options.RedirectUri);
        Assert.Equal("native", options.ApplicationType); Assert.Equal("none", options.TokenEndpointAuthMethod);
        Assert.Equal(HomeNativeCakeAccountOwner.Origin, options.ApiResource);
        Assert.Equal(options.Issuer + "/oauth2/token", options.TokenUri.AbsoluteUri);
    }

    [Fact]
    public async Task Missing_actual_client_configuration_disables_sign_in()
    {
        var session = new Session { IsAvailable = false };
        var view = new HomeNativeCakeAccountBindings(session, () => { });
        try { Assert.False(view.IsActionAvailable("SignInCakeAccount")); await view.Start("SignInCakeAccount"); Assert.Equal(0, session.SignIns); }
        finally { await view.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Original_sign_in_reads_current_profile_sessions_and_confirmed_sign_out_once()
    {
        var session = new Session(); var view = new HomeNativeCakeAccountBindings(session, () => { });
        try
        {
            await view.Start("SignInCakeAccount"); Assert.Equal(1, session.SignIns);
            Assert.Equal(1, session.Currents); Assert.Equal(1, session.Profiles); Assert.Equal(1, session.Sessions);
            Assert.True(view.TryGetValue("CakeAccountId", out var account)); Assert.Equal(session.Snapshot!.AccountId.ToString("D"), account);
            await view.Start("RequestCakeSignOut"); Assert.Equal(0, session.SignOuts);
            await view.Start("ConfirmCakeSignOut"); Assert.Equal(1, session.SignOuts);
            Assert.True(view.TryGetValue("CakeAccountId", out var cleared)); Assert.Equal("", cleared);
            await view.Start("ConfirmCakeSignOut"); Assert.Equal(1, session.SignOuts); // No replay from stale confirmation.
        }
        finally { await view.CloseAndDrainAsync(); }
        Assert.Equal(0, session.Closes); // The view only borrowed the original session.
    }

    [Fact]
    public async Task Genuine_typed_server_refusal_clears_the_previous_profile_projection()
    {
        var session = new Session(); var view = new HomeNativeCakeAccountBindings(session, () => { });
        try
        {
            await view.Start("RefreshCakeAccount");
            Assert.True(view.TryGetValue("CakeAccountId", out var before)); Assert.NotEqual("", before);
            session.ProfileFailure = ApiFailure.PermissionDenied;
            await view.Start("RefreshCakeAccount");
            Assert.True(view.TryGetValue("CakeAccountId", out var after)); Assert.Equal("", after);
            Assert.True(view.TryGetValue("CakeAccountStatus", out var status)); Assert.Contains("PermissionDenied", Assert.IsType<string>(status));
        }
        finally { await view.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Replacement_session_refuses_old_confirmation_without_recapturing_it()
    {
        var session = new Session(); var view = new HomeNativeCakeAccountBindings(session, () => { });
        try
        {
            await view.Start("RefreshCakeAccount"); await view.Start("RequestCakeSignOut");
            session.Snapshot = session.Snapshot! with { SessionId = Guid.NewGuid() };
            Assert.False(view.IsActionAvailable("ConfirmCakeSignOut"));
            await view.Start("ConfirmCakeSignOut"); Assert.Equal(0, session.SignOuts);
            Assert.True(view.TryGetValue("CakeAccountId", out var hidden)); Assert.Equal("", hidden);
        }
        finally { await view.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Held_original_read_is_released_in_finally_and_view_and_close_are_independently_joined()
    {
        var session = new Session(); var raw = new TaskCompletionSource<ApiResult<RemoteCurrent>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Read = _ => { entered.SetResult(); return raw.Task; };
        var view = new HomeNativeCakeAccountBindings(session, () => { });
        Task? actual = null, close = null; var errors = new List<Exception>();
        try
        {
            actual = view.Start("RefreshCakeAccount"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = view.CloseAndDrainAsync();
            Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted);
            Assert.True(view.TryGetValue("CakeAccountId", out var hidden)); Assert.Equal("", hidden);
        }
        catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        finally
        {
            raw.TrySetResult(new(new(session.Snapshot!.AccountId, "original"), ApiFailure.None));
            if (actual is not null) await HomeNativeCakeCauses.JoinAsync(actual, errors);
            close ??= view.CloseAndDrainAsync(); await HomeNativeCakeCauses.JoinAsync(close, errors);
        }
        Assert.True(raw.Task.IsCompletedSuccessfully);
        Assert.True(actual!.IsFaulted); // Faulted cancellation is kept as a faulted public task.
        Assert.Contains(errors, error => error is OperationCanceledException);
        HomeNativeCakeCauses.Throw(errors.Where(error => error is not OperationCanceledException).ToList());
    }

    [Fact]
    public async Task Faulted_OCE_raw_sibling_and_stop_callback_fault_all_survive_close()
    {
        var oce = new OperationCanceledException("faulted source OCE"); var sibling = new InvalidOperationException("raw sibling");
        var stop = new InvalidOperationException("stop callback");
        var session = new Session(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<ApiResult<RemoteCurrent>>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Read = token => { token.Register(() => throw stop); entered.SetResult(); return raw.Task; };
        var view = new HomeNativeCakeAccountBindings(session, () => { }); Task? actual = null, close = null;
        var errors = new List<Exception>();
        try
        {
            actual = view.Start("RefreshCakeAccount"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = view.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
        }
        catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        finally
        {
            raw.TrySetException(new AggregateException(oce, sibling));
            if (actual is not null) await HomeNativeCakeCauses.JoinAsync(actual, errors);
            close ??= view.CloseAndDrainAsync(); await HomeNativeCakeCauses.JoinAsync(close, errors);
        }
        Assert.Contains(errors, error => ReferenceEquals(error, oce));
        Assert.Contains(errors, error => ReferenceEquals(error, sibling));
        Assert.Contains(errors, error => ReferenceEquals(error, stop));
        Assert.True(actual!.IsFaulted);
        HomeNativeCakeCauses.Throw(errors.Where(error => !ReferenceEquals(error, oce) && !ReferenceEquals(error, sibling) && !ReferenceEquals(error, stop)).ToList());
    }

    [Fact]
    public async Task Physical_source_callback_cannot_join_its_own_view_or_close_admission()
    {
        var session = new Session(); HomeNativeCakeAccountBindings? view = null;
        session.Read = _ =>
        {
            Assert.Throws<InvalidOperationException>(() => view!.CloseAndDrainAsync());
            Assert.False(view!.IsClosing);
            return Task.FromResult(new ApiResult<RemoteCurrent>(new(session.Snapshot!.AccountId, "original"), ApiFailure.None));
        };
        view = new(session, () => { });
        try { await view.Start("RefreshCakeAccount"); Assert.True(view.TryGetValue("CakeAccountId", out var account)); Assert.NotEqual("", account); }
        finally { await view.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Logical_source_after_await_cannot_join_its_live_original_ancestor()
    {
        var session = new Session(); HomeNativeCakeAccountBindings? view = null;
        session.Read = async _ =>
        {
            await Task.Yield(); Assert.Throws<InvalidOperationException>(() => view!.CloseAndDrainAsync());
            Assert.False(view!.IsClosing);
            return new(new(session.Snapshot!.AccountId, "original"), ApiFailure.None);
        };
        view = new(session, () => { });
        try { await view.Start("RefreshCakeAccount"); }
        finally { await view.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Borrowed_owner_close_does_not_close_the_actual_shared_session()
    {
        var session = new Session(); var owner = new HomeNativeCakeAccountOwner(session);
        var first = owner.CloseAndDrainAsync(); Assert.Same(first, owner.CloseAndDrainAsync()); await first;
        Assert.Equal(0, session.Closes);
    }

    [Fact]
    public async Task Standalone_owner_starts_session_and_helper_close_independently_and_preserves_both_faults()
    {
        var sessionFault = new InvalidOperationException("session close"); var helperFault = new InvalidOperationException("helper close");
        var session = new Session { CloseSource = () => Task.FromException(sessionFault) };
        var helper = new Access { CloseSource = () => Task.FromException(helperFault) };
        var owner = new HomeNativeCakeAccountOwner(session, helper); var errors = new List<Exception>();
        await HomeNativeCakeCauses.JoinAsync(owner.CloseAndDrainAsync(), errors);
        Assert.Equal(1, session.Closes); Assert.Equal(1, helper.Closes);
        Assert.Contains(errors, error => ReferenceEquals(error, sessionFault)); Assert.Contains(errors, error => ReferenceEquals(error, helperFault));
        HomeNativeCakeCauses.Throw(errors.Where(error => !ReferenceEquals(error, sessionFault) && !ReferenceEquals(error, helperFault)).ToList());
    }

    [Fact]
    public async Task Unknown_sign_out_is_not_replayed_and_clears_account_observations()
    {
        var session = new Session { SignOutFailure = ApiFailure.CompletionUnknown }; var view = new HomeNativeCakeAccountBindings(session, () => { });
        try
        {
            await view.Start("RefreshCakeAccount"); await view.Start("RequestCakeSignOut"); await view.Start("ConfirmCakeSignOut");
            await view.Start("ConfirmCakeSignOut"); Assert.Equal(1, session.SignOuts);
            Assert.True(view.TryGetValue("CakeAccountId", out var account)); Assert.Equal("", account);
            Assert.True(view.TryGetValue("CakeAccountStatus", out var status)); Assert.Contains("CompletionUnknown", Assert.IsType<string>(status));
        }
        finally { await view.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Borrowed_view_keeps_sign_out_with_the_original_shared_account_owner()
    {
        var session = new Session(); var view = new HomeNativeCakeAccountBindings(session, () => { }, allowSignOut: false);
        try
        {
            await view.Start("RefreshCakeAccount");
            Assert.False(view.IsActionAvailable("RequestCakeSignOut"));
            await view.Start("RequestCakeSignOut"); await view.Start("ConfirmCakeSignOut");
            Assert.Equal(0, session.SignOuts);
            Assert.True(view.TryGetValue("CakeUnsupportedServices", out var message));
            Assert.Contains("original account owner", Assert.IsType<string>(message));
        }
        finally { await view.CloseAndDrainAsync(); }
    }

    private sealed class Session : INativeCakeAccountSession
    {
        internal NativeCakeAccountSnapshot? Snapshot = new(Guid.NewGuid(), Guid.NewGuid(), "Scripted account", DateTimeOffset.UtcNow.AddHours(1));
        public bool IsAvailable { get; set; } = true;
        internal int SignIns, Currents, Profiles, Sessions, SignOuts, Closes;
        internal ApiFailure ProfileFailure, SignOutFailure;
        internal Func<CancellationToken, Task<ApiResult<RemoteCurrent>>>? Read;
        internal Func<Task>? CloseSource;
        public Task<NativeCakeAccountSnapshot> SignInAsync(CancellationToken token) { SignIns++; return Task.FromResult(Snapshot!); }
        public Task<ApiResult<RemoteCurrent>> CurrentAsync(CancellationToken token)
        { Currents++; return Read?.Invoke(token) ?? Task.FromResult(new ApiResult<RemoteCurrent>(new(Snapshot!.AccountId, "Scripted account"), ApiFailure.None)); }
        public Task<ApiResult<RemoteProfile>> ProfileAsync(CancellationToken token)
        { Profiles++; return Task.FromResult(new ApiResult<RemoteProfile>(new(Snapshot!.AccountId, "Scripted profile", "fixture", null, null, null, 3), ProfileFailure)); }
        public Task<ApiResult<RemoteSession[]>> SessionsAsync(CancellationToken token)
        { Sessions++; return Task.FromResult(new ApiResult<RemoteSession[]>([new(Snapshot!.SessionId, Snapshot.AccountId, "fixture", DateTimeOffset.UtcNow.AddMinutes(-1), Snapshot.ExpiresAt, null, HomeNativeCakeAccountOwner.PublicClientId)], ApiFailure.None)); }
        public Task<ApiResult<RemoteMutationAcknowledgement>> SignOutAsync(CancellationToken token)
        { SignOuts++; Snapshot = null; return Task.FromResult(new ApiResult<RemoteMutationAcknowledgement>(SignOutFailure == ApiFailure.None ? new(true) : null, SignOutFailure)); }
        public bool TryGetCurrentSnapshot(out NativeCakeAccountSnapshot? value) { value = Snapshot; return value is not null; }
        public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeSessionAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeOtherSessionsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task CloseAndDrainAsync() { Closes++; return CloseSource?.Invoke() ?? Task.CompletedTask; }
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    }
    private sealed class Access : INativeCakeAccessCredentialSource
    {
        internal int Closes; internal Func<Task>? CloseSource;
        public ValueTask<string?> AcquireForRequestAsync(Uri origin, CancellationToken token) => throw new NotSupportedException();
        public Task CloseAndDrainAsync() { Closes++; return CloseSource?.Invoke() ?? Task.CompletedTask; }
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    }
}
