using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using NineToOne.Accounts.Native;
using NineToOne.Accounts.Remote;

namespace AvaloniaHome;

/// <summary>Home presentation over the maintained native client. A CAKE session is never a
/// Home operating-system actor, installed peer, permission or provider-availability grant.</summary>
internal sealed class HomeNativeCakeAccountBindings : ICuiBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged
{
    private sealed class Work
    {
        internal Task Task = null!;
        internal Task Driver = null!;
        internal readonly List<Task> Sources = new();
        internal readonly List<Exception> Errors = new();
    }
    private readonly object _gate = new();
    private readonly INativeCakeAccountSession _session;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Work> _issued = new();
    private readonly List<Exception> _ownErrors = new();
    private readonly Action _requestWindowClose;
    private readonly bool _allowSignOut;
    private NativeCakeAccountSnapshot? _visible;
    private RemoteProfile? _profile;
    private RemoteSession[] _sessions = Array.Empty<RemoteSession>();
    private NativeCakeAccountSnapshot? _signOutIntent;
    private bool _busy, _closing;
    private Task? _close;
    private string _status = "Sign in to connect CAKE ID. Your local Home dashboard uses your operating-system profile.";
    internal HomeNativeCakeAccountBindings(INativeCakeAccountSession session, Action requestWindowClose, bool allowSignOut = true)
    { _session = session ?? throw new ArgumentNullException(nameof(session)); _requestWindowClose = requestWindowClose; _allowSignOut = allowSignOut; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal bool IsClosing { get { lock (_gate) return _closing; } }
    internal void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);

    private T Source<T>(Func<T> body)
    {
        try { return CloudflareOriginalExecutionGuard.InvokeOriginal(this, body); }
        catch (Exception error)
        {
            lock (_gate) { Clear(); HomeNativeCakeCauses.Add(_ownErrors, error); }
            throw; // Even a getter/registration failure outside an admitted command stays owned.
        }
    }
    private NativeCakeAccountSnapshot? Current()
    {
        var current = Source(() => _session.TryGetCurrentSnapshot(out var value) ? value : null);
        return current is { AccountId: var account, SessionId: var session } && account != Guid.Empty &&
            session != Guid.Empty && current.ExpiresAt > DateTimeOffset.UtcNow ? current : null;
    }
    private static bool Same(NativeCakeAccountSnapshot? left, NativeCakeAccountSnapshot? right) =>
        left is not null && right is not null && left.AccountId == right.AccountId &&
        left.SessionId == right.SessionId && left.ExpiresAt == right.ExpiresAt;
    private bool Visible()
    {
        if (_closing || _visible is null) return false;
        if (Same(_visible, Current())) return true;
        Clear(); _status = "The CAKE ID session changed or expired. Sign in or refresh."; return false;
    }
    private void Clear() { _visible = null; _profile = null; _sessions = Array.Empty<RemoteSession>(); _signOutIntent = null; }
    internal void RefreshPrivacyFence()
    {
        using var scope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try { lock (_gate) { if (_closing) return; Visible(); Notify(); } }
        catch (Exception error) { lock (_gate) { Clear(); HomeNativeCakeCauses.Add(_ownErrors, error); } throw; }
    }
    public bool TryGetValue(string path, out object? value)
    {
        using var scope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        lock (_gate)
        {
            var visible = Visible();
            value = path switch
            {
                "IsNativeHomeShell" => false,
                "IsNativeCakeAccountWindow" => true,
                "CakeAccountHeading" => visible ? _visible!.DisplayName : "CAKE ID",
                "CakeAccountStatus" => _status,
                "CakeAccountId" => visible ? _visible!.AccountId.ToString("D") : "",
                "CakeProfileSummary" => visible && _profile is not null
                    ? $"{_profile.Name} · @{_profile.Username} · profile revision {_profile.Revision}" : "",
                "CakeSessionsSummary" => visible ? string.Join(Environment.NewLine, _sessions.Select(session =>
                    $"{session.DeviceName} · {session.SessionID:D} · {(session.RevokedAt is null ? "expires " + session.ExpiresAt.ToString("u") : "revoked")}")) : "",
                "CakeHasSignOutConfirmation" => visible && Same(_signOutIntent, _visible),
                "CakeUnsupportedServices" => "Profile editing, plan and billing are not available to this native client. CAKE ID does not grant Home permissions." +
                    (_allowSignOut ? "" : " This Home window borrows the shared session; sign-out is managed by its original account owner."),
                _ => null
            };
            return value is not null;
        }
    }
    public bool? IsActionAvailable(string command)
    {
        lock (_gate)
        {
            if (_closing) return false;
            if (command == "CloseCakeAccount") return true;
            if (_busy) return false;
            return command switch
            {
                "SignInCakeAccount" => Source(() => _session.IsAvailable),
                "RefreshCakeAccount" => Source(() => _session.IsAvailable),
                "RequestCakeSignOut" => _allowSignOut && Visible(),
                "ConfirmCakeSignOut" or "CancelCakeSignOut" => _allowSignOut && Visible() && Same(_signOutIntent, _visible),
                _ => false
            };
        }
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (command == "CloseCakeAccount")
        {
            lock (_gate) { if (_closing) return ValueTask.CompletedTask; }
            // A finite request to the owning dispatcher, never a join inside the issuing action.
            _requestWindowClose(); return ValueTask.CompletedTask;
        }
        return new(Start(command, cancellationToken));
    }
    internal Task Start(string command, CancellationToken caller = default)
    {
        Work work; TaskCompletionSource output; TaskCompletionSource start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (IsActionAvailable(command) != true) return Task.CompletedTask;
            _issued.RemoveAll(item => item.Task.IsCompletedSuccessfully && item.Driver.IsCompletedSuccessfully &&
                item.Sources.All(source => source.IsCompletedSuccessfully));
            if (_issued.Count >= 32) throw new InvalidOperationException("Close this account view before admitting more retained failures.");
            _busy = true;
            output = new(TaskCreationOptions.RunContinuationsAsynchronously);
            start = new(); work = new() { Task = output.Task };
            work.Driver = RunAsync(start.Task, work, output, command, caller);
            _issued.Add(work); // SAME public task/driver before source getters, events or cancellation.
        }
        start.SetResult(); return work.Task;
    }
    private async Task RunAsync(Task start, Work work, TaskCompletionSource output, string command, CancellationToken caller)
    {
        await start.ConfigureAwait(true);
        using var scope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        CancellationTokenSource? linked = null;
        try
        {
            linked = Source(() => CancellationTokenSource.CreateLinkedTokenSource(caller, _stop.Token));
            linked.Token.ThrowIfCancellationRequested(); Notify();
            switch (command)
            {
                case "SignInCakeAccount":
                    lock (_gate) { Clear(); _status = "Complete the genuine CAKE ID sign-in in your browser."; Notify(); }
                    var signed = await AwaitSource(work, () => _session.SignInAsync(linked.Token));
                    linked.Token.ThrowIfCancellationRequested();
                    if (!Same(signed, Current())) throw new UnauthorizedAccessException("The original native sign-in session is no longer current.");
                    await RefreshAsync(work, linked.Token); break;
                case "RefreshCakeAccount": await RefreshAsync(work, linked.Token); break;
                case "RequestCakeSignOut":
                    lock (_gate) { if (Visible()) { _signOutIntent = _visible; _status = "Confirm signing out of this exact CAKE ID session. Local Home stays available."; } }
                    break;
                case "CancelCakeSignOut": lock (_gate) { _signOutIntent = null; _status = "Sign-out cancelled."; } break;
                case "ConfirmCakeSignOut":
                    NativeCakeAccountSnapshot? expected;
                    lock (_gate) { expected = _signOutIntent; if (!Visible() || !Same(expected, _visible)) throw new UnauthorizedAccessException("The confirmed session changed."); Clear(); }
                    if (!Same(expected, Current())) throw new UnauthorizedAccessException("The confirmed CAKE ID session changed before sign-out.");
                    var acknowledgement = await AwaitSource(work, () => _session.SignOutAsync(linked.Token));
                    lock (_gate) _status = acknowledgement.Failure == ApiFailure.None && acknowledgement.Value?.Acknowledged == true
                        ? "Signed out of CAKE ID. Your local Home dashboard is unchanged."
                        : $"Local account observations cleared. Server sign-out: {acknowledgement.Failure}; acknowledgement {acknowledgement.Value?.Acknowledged}. No automatic retry.";
                    break;
            }
        }
        catch (Exception error)
        {
            HomeNativeCakeCauses.Add(work.Errors, error);
            lock (_gate) { Clear(); _status = $"CAKE ID operation failed: {error.Message}"; }
        }
        finally
        {
            try { if (linked is not null) Source(() => { linked.Dispose(); return true; }); }
            catch (Exception error) { HomeNativeCakeCauses.Add(work.Errors, error); }
            lock (_gate)
            {
                _busy = false;
                try { if (!_closing) Notify(); } catch (Exception error) { HomeNativeCakeCauses.Add(work.Errors, error); }
                // Preserve faulted OCE as a fault, after actual source/cleanup, under SAME fence.
                if (work.Errors.Count > 0) output.TrySetException(work.Errors);
                else if (_closing) output.TrySetException(new ObjectDisposedException(nameof(HomeNativeCakeAccountBindings)));
                else output.TrySetResult();
            }
        }
    }
    private async Task<T> AwaitSource<T>(Work work, Func<Task<T>> invoke)
    {
        var actual = Source(invoke) ?? throw new InvalidOperationException("The native account source returned no task.");
        lock (_gate) work.Sources.Add(actual); // Original, no reconstructed or cancellation-wrapped task.
        try { return await actual.ConfigureAwait(true); }
        catch (Exception error) { HomeNativeCakeCauses.AddTask(work.Errors, actual, error); throw; }
    }
    private async Task RefreshAsync(Work work, CancellationToken token)
    {
        lock (_gate) Clear();
        var expected = Current();
        if (expected is null) { lock (_gate) _status = "No current CAKE ID session. Sign in to connect your account."; return; }
        var current = await AwaitSource(work, () => _session.CurrentAsync(token));
        token.ThrowIfCancellationRequested();
        if (!Same(expected, Current())) throw new UnauthorizedAccessException("The account session changed during Current.");
        var profile = await AwaitSource(work, () => _session.ProfileAsync(token));
        token.ThrowIfCancellationRequested();
        if (!Same(expected, Current())) throw new UnauthorizedAccessException("The account session changed during Profile.");
        var sessions = await AwaitSource(work, () => _session.SessionsAsync(token));
        token.ThrowIfCancellationRequested();
        if (!Same(expected, Current())) throw new UnauthorizedAccessException("The account session changed during Sessions.");
        if (current.Failure != ApiFailure.None || profile.Failure != ApiFailure.None || sessions.Failure != ApiFailure.None)
        {
            lock (_gate) _status = $"CAKE ID server checks: account {current.Failure}, profile {profile.Failure}, sessions {sessions.Failure}. Account observations remain private.";
            return;
        }
        if (current.Value?.AccountID != expected.AccountId || profile.Value?.AccountID != expected.AccountId ||
            sessions.Value is not { } actualSessions || actualSessions.Any(item => item.AccountID != expected.AccountId || item.SessionID == Guid.Empty) ||
            actualSessions.Select(item => item.SessionID).Distinct().Count() != actualSessions.Length ||
            !actualSessions.Any(item => item.SessionID == expected.SessionId && item.RevokedAt is null && item.ExpiresAt > DateTimeOffset.UtcNow))
            throw new InvalidDataException("The genuine account/profile/session observations disagree with the original current session.");
        lock (_gate)
        {
            if (_closing || !Same(expected, Current())) throw new ObjectDisposedException(nameof(HomeNativeCakeAccountBindings));
            _visible = expected; _profile = profile.Value; _sessions = actualSessions.ToArray();
            _status = "CAKE ID connected. Account and session checks came from the current server. Local Home permissions remain separate.";
        }
    }
    private void Notify() => Source(() => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); return true; });
    internal Task CloseAndDrainAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true; Clear(); _status = "Account view closed."; // Immediate result/admission/privacy fence.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseCoreAsync(start.Task, _issued.ToArray()); start.SetResult(); return _close;
        }
    }
    private async Task CloseCoreAsync(Task start, Work[] originals)
    {
        await start.ConfigureAwait(false);
        using var scope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>();
        try { Source(() => { _stop.Cancel(); return true; }); } catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        foreach (var work in originals)
        {
            await HomeNativeCakeCauses.JoinAsync(work.Task, errors);
            await HomeNativeCakeCauses.JoinAsync(work.Driver, errors);
            foreach (var raw in work.Sources) await HomeNativeCakeCauses.JoinAsync(raw, errors);
            foreach (var error in work.Errors) HomeNativeCakeCauses.Add(errors, error);
        }
        foreach (var error in _ownErrors) HomeNativeCakeCauses.Add(errors, error);
        try { Source(() => { _stop.Dispose(); return true; }); } catch (Exception error) { HomeNativeCakeCauses.Add(errors, error); }
        PropertyChanged = null; HomeNativeCakeCauses.Throw(errors);
    }
}

internal static class HomeNativeCakeCauses
{
    internal static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException group) { foreach (var inner in group.InnerExceptions) Add(errors, inner); return; }
        if (!errors.Any(prior => ReferenceEquals(prior, error))) errors.Add(error);
    }
    internal static void AddTask(List<Exception> errors, Task actual, Exception caught)
    { if (actual.IsFaulted && actual.Exception is { } group) Add(errors, group); else Add(errors, caught); }
    internal static async Task JoinAsync(Task actual, List<Exception> errors)
    { try { await actual.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, actual, error); } }
    internal static void Throw(List<Exception> errors)
    { if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw(); if (errors.Count > 1) throw new AggregateException("Original native Home account work and cleanup failed.", errors); }
}
