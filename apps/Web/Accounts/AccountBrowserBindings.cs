using System.ComponentModel;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Web.Services;

namespace NineToOne.Web.Accounts;

/// <summary>CUI presentation of server-owned account/profile/session wire records. Drafts and confirmation state are view-local.</summary>
public sealed class AccountBrowserBindings : ICuiWritableBindingContext, ICuiRepeatItemBindingContext,
    ICuiActionDispatcher, ICuiLifetimeAwareActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private static readonly string[] Fields = ["name", "username", "icon", "pronouns", "job"];
    private IAccountBrowserTransport? _transport;
    private Func<Action, Task>? _present;
    private Func<CancellationToken, Task>? _openSignIn;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _ownership = new();
    private readonly HashSet<Task> _ownedBoundaries = [];
    private readonly HashSet<Task> _brokerContinuations = [];
    private long _generation;
    private Task? _cleanup;

    // A boundary settles only when all view-owned work ends or transfers to the
    // EXISTING broker before its reset callback. It is not a token/actor/job model.
    private sealed class CommandBoundary
    {
        internal readonly TaskCompletionSource Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Transferred;
        internal CancellationToken ViewCancellation;
        internal CancellationTokenRegistration ViewRegistration;
    }
    private readonly Dictionary<string, string?> _draft = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);
    private JsonElement? _current, _profile;
    private JsonElement[] _sessions = [];
    private string? _selectedSession, _confirmation;
    private long _revision;
    private long? _conflictRevision;
    private bool _busy, _conflict, _disposed, _sessionsChecked;
    private string _status = "Account services have not been checked.";
    public event PropertyChangedEventHandler? PropertyChanged;

    // A browser composition root must marshal presentation changes onto its CUI UI thread.
    public AccountBrowserBindings(IAccountBrowserTransport transport, Func<Action, Task> present,
        Func<CancellationToken, Task>? openSignIn = null)
    { _transport = transport; _present = present; _openSignIn = openSignIn; }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Status" => _status, "Busy" => _busy, "CanRefresh" => !_disposed && !_busy,
            "CanSignIn" => !_disposed && !_busy && _openSignIn is not null,
            "CanEdit" => !_disposed && !_busy && _profile is not null,
            "CanSave" => !_disposed && !_busy && !_conflict && _profile is not null && _changed.Count > 0,
            "HasChanges" => _changed.Count > 0, "Conflict" => _conflict,
            "ConflictRevision" => _conflictRevision?.ToString() ?? "",
            "HasProfile" => _profile is not null, "HasAccount" => _current is not null,
            "AccountId" => Text(_current, "accountId"), "DisplayName" => Text(_current, "displayName"),
            "ProfileRevision" => _profile is null ? "" : _revision.ToString(),
            "Sessions" => Array.AsReadOnly(_sessions), "Confirmation" => _confirmation ?? "",
            "SessionSummary" => !_sessionsChecked ? "Sessions have not been checked." : _sessions.Length == 0
                ? "No sessions were returned for this account." : "Sessions returned by the account service.",
            "HasConfirmation" => _confirmation is not null,
            "CanConfirm" => !_disposed && !_busy && _confirmation is not null,
            "CanSessionActions" => !_disposed && !_busy && _current is not null,
            "UnsupportedServices" => "Plan, billing, username availability and icon upload are unavailable until their account services are connected.",
            _ when path.StartsWith("Draft.", StringComparison.Ordinal) && Fields.Contains(path[6..], StringComparer.Ordinal)
                => _draft.GetValueOrDefault(path[6..]) ?? "",
            _ => null,
        };
        return value is not null;
    }

    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || _busy || _profile is null || !path.StartsWith("Draft.", StringComparison.Ordinal) ||
            !Fields.Contains(path[6..], StringComparer.Ordinal) || value is not string text) return false;
        var field = path[6..];
        _draft[field] = text;
        if (text == Text(_profile, field)) _changed.Remove(field); else _changed.Add(field);
        Changed();
        return true;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = !_disposed && _current is not null && item is JsonElement record && record.ValueKind == JsonValueKind.Object &&
            Text(record, "accountId") == Text(_current, "accountId") &&
            _sessions.Any(row => Text(row, "sessionId") == Text(record, "sessionId")) &&
            record.TryGetProperty(path, out var property) ? Scalar(property) : null;
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;

    public bool? IsActionAvailable(string command) => !_disposed && (command switch
    {
        "Refresh" or "DiscardAndReload" => !_busy,
        "RequestSignIn" => !_busy && _openSignIn is not null,
        "SaveProfile" => !_busy && !_conflict && _profile is not null && _changed.Count > 0,
        "ClearIcon" or "ClearPronouns" or "ClearJob" => !_busy && _profile is not null,
        "SelectSession" or "RequestSignOut" or "RequestRevokeOthers" => !_busy && _current is not null,
        "ConfirmSessionMutation" => !_busy && _confirmation is not null,
        "CancelSessionMutation" => !_busy && _confirmation is not null,
        _ => false,
    });

    // Existing public callers keep their actual explicit cancellation, unchanged.
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        => DispatchTrackedAsync(command, parameter, cancellationToken, default);

    // SOURCE ONLY: blocked until A1 owns/delivers the canonical optional CUI port.
    public ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
        => DispatchTrackedAsync(command, parameter, lifetime.CallerCancellation, lifetime.ViewCancellation);

    private ValueTask DispatchTrackedAsync(string command, object? parameter,
        CancellationToken cancellationToken, CancellationToken viewCancellation)
    {
        CommandBoundary boundary;
        long generation;
        lock (_ownership)
        {
            if (_disposed) return ValueTask.CompletedTask;
            generation = _generation;
            boundary = new() { ViewCancellation = viewCancellation };
            _ownedBoundaries.Add(boundary.Settled.Task);
        }
        return new(RunIssuedCommandAsync(command, parameter, cancellationToken, generation, boundary));
    }

    /// <summary>Diagnostic ownership receipts only, never authentication success.
    /// These ORIGINAL broker tasks remain caller-cancellable and may outlive view drain.</summary>
    public IReadOnlyList<Task> BrokerOwnedContinuations
    { get { lock (_ownership) return _brokerContinuations.ToArray(); } }
    public bool HasOutstandingBrokerWork => BrokerOwnedContinuations.Any(task => !task.IsCompleted);
    private void TransferToExistingBroker(CommandBoundary boundary)
    {
        // Unregister ONLY CUI view retirement, at the existing validated handoff.
        // Already observed cancellation stays set; true caller remains linked.
        boundary.ViewRegistration.Dispose();
        lock (_ownership) { boundary.Transferred = true; _brokerContinuations.Add(boundary.Completion.Task); }
        boundary.Settled.TrySetResult();
    }

    private async Task RunIssuedCommandAsync(string command, object? parameter, CancellationToken token,
        long generation, CommandBoundary boundary)
    {
        try { await DispatchCoreAsync(command, parameter, token, generation, boundary); boundary.Completion.TrySetResult(); }
        catch (Exception error) { if (boundary.Transferred) boundary.Completion.TrySetException(error); else boundary.Completion.TrySetResult(); throw; }
        finally
        {
            boundary.Settled.TrySetResult();
            lock (_ownership) _ownedBoundaries.Remove(boundary.Settled.Task);
        }
    }

    private async Task DispatchCoreAsync(string command, object? parameter, CancellationToken cancellationToken,
        long generation, CommandBoundary boundary)
    {
        if (_disposed || generation != _generation) return;
        // Confirmed session mutations and trusted sign-in clear/dispose this private view during their lifecycle.
        // They retain explicit caller cancellation; the reviewed mutation client pins the original session token.
        // View disposal still cancels reads and profile writes and never permits another command on this view.
        using var request = command is "ConfirmSessionMutation" or "RequestSignIn"
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        // The same request remains view-cancellable through selection/admission.
        // LIFO disposal releases this registration before its request CTS.
        using var viewRegistration = boundary.ViewCancellation.Register(
            static state => ((CancellationTokenSource)state!).Cancel(), request);
        boundary.ViewRegistration = viewRegistration;
        if (!await _gate.WaitAsync(0, request.Token)) return; // Never queue an edit or revocation behind a different operation.
        try
        {
            if (IsActionAvailable(command) != true) return;
            if (command == "SelectSession")
            {
                var id = parameter is JsonElement selected ? Text(selected, "sessionId") : null;
                if (id is null || !_sessions.Any(row => Text(row, "sessionId") == id)) return;
                await Present(() => { _selectedSession = id; _pendingMutation = null; _confirmation = $"Revoke session {id}? Private views will close before the request is sent."; });
                return;
            }
            if (command is "RequestSignOut" or "RequestRevokeOthers")
            {
                await Present(() => { _selectedSession = null; _confirmation = command == "RequestSignOut"
                    ? "Sign out of this session? Private views will close before the request is sent."
                    : "Revoke all other sessions? Private views will close before the request is sent.";
                    _pendingMutation = command == "RequestSignOut" ? "SignOut" : "RevokeAllOtherSessions"; });
                return;
            }
            if (command == "CancelSessionMutation")
            { await Present(() => { _confirmation = null; _selectedSession = null; _pendingMutation = null; }); return; }
            if (command.StartsWith("Clear", StringComparison.Ordinal))
            {
                var field = command switch { "ClearIcon" => "icon", "ClearPronouns" => "pronouns", _ => "job" };
                await Present(() => { _draft[field] = null; _changed.Add(field); }); return;
            }
            await Present(() => { _busy = true; _status = "Checking account services…"; });
            if (command == "SaveProfile") await SaveAsync(request.Token);
            else if (command == "ConfirmSessionMutation") await MutateSessionAsync(request.Token, boundary);
            else if (command == "RequestSignIn")
            {
                var signIn = _openSignIn ?? throw new OperationCanceledException(request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (_disposed || generation != _generation) return;
                // Exact existing owner: configured-accounts.requestSignIn + BrowserPublicClient.
                // Its awaited private reset cannot await this enclosing dispatch. Caller cancellation
                // remains attached to the broker; no token or verified identity is supplied by this view.
                TransferToExistingBroker(boundary);
                await signIn(request.Token);
                // Fresh registration reads actual API self/profile/sessions, not this retired view.
                await Present(() => _status = "Sign-in owner completed. Reopen account settings to check the actual account service.");
            }
            else
            {
                if (command == "DiscardAndReload") await Present(() => { _changed.Clear(); _conflict = false; });
                await RefreshAsync(request.Token, generation);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        { await Present(() => _status = "Account operation cancelled. No success has been confirmed."); }
        catch (Exception)
        { await Present(() => { ClearPrivate(); _status = "Account services are unavailable or returned an incompatible record. No success has been confirmed."; }); }
        finally
        {
            try { await Present(() => _busy = false); }
            finally { _gate.Release(); }
        }
    }
    private string? _pendingMutation;

    private async Task RefreshAsync(CancellationToken ct, long originalGeneration)
    {
        Task Publish(Action write) => Present(write, originalGeneration);
        var current = await InvokeOwnedAsync("GetCurrent", null, ct, originalGeneration);
        if (!Succeeded(current)) { await Publish(() => { ClearPrivate(); Fail(current); }); return; }
        var body = current.GetProperty("body");
        var id = Text(body, "accountId");
        if (id is null) { await Publish(() => { ClearPrivate(); _status = "Account services returned an incompatible record."; }); return; }
        if (_current is not null && Text(_current, "accountId") != id)
            await Publish(ClearPrivate);
        await Publish(() => _current = body.Clone());
        var profile = await InvokeOwnedAsync("GetProfile", null, ct, originalGeneration);
        if (Succeeded(profile))
        {
            var record = profile.GetProperty("body").GetProperty("profile");
            if (Text(record, "accountId") != id) { await Publish(() => { ClearPrivate(); _status = "The account context changed. Reopen account settings."; }); return; }
            await Publish(() =>
            {
                if (_changed.Count == 0) ApplyProfile(record);
                else if (_revision != record.GetProperty("revision").GetInt64())
                { _conflict = true; _conflictRevision = record.GetProperty("revision").GetInt64(); }
            });
        }
        else { await Publish(() => Fail(profile)); if (PrivateFailure(profile)) return; }
        var sessions = await InvokeOwnedAsync("ListSessions", null, ct, originalGeneration);
        if (Succeeded(sessions))
        {
            var rows = sessions.GetProperty("body").GetProperty("sessions").EnumerateArray().Select(row => row.Clone()).ToArray();
            if (rows.Any(row => Text(row, "accountId") != id))
            { await Publish(() => { ClearPrivate(); _status = "The account context changed. Reopen account settings."; }); return; }
            await Publish(() => { _sessions = rows; _sessionsChecked = true; _selectedSession = null; _confirmation = null; _pendingMutation = null;
                if (Succeeded(profile)) _status = _conflict ? "Your profile changed elsewhere. Your draft is preserved. Discard and reload before saving."
                    : _changed.Count > 0 ? "Account checked. Your unsaved profile draft is preserved." : "Account, profile and sessions checked."; });
        }
        else await Publish(() => Fail(sessions));
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_draft.GetValueOrDefault("name")) || string.IsNullOrWhiteSpace(_draft.GetValueOrDefault("username")))
        { await Present(() => _status = "Name and Username are required. Enter a nonblank value for both fields."); return; }
        // Capture only changed fields and the original edit revision before transport can yield.
        var patch = _changed.ToDictionary(field => field, field => _draft.GetValueOrDefault(field), StringComparer.Ordinal);
        var result = await InvokeOwnedAsync("UpdateProfile", JsonSerializer.SerializeToElement(new { expectedRevision = _revision, fields = patch }), ct);
        await Present(() =>
        {
            if (!Succeeded(result)) { Fail(result); return; }
            var record = result.GetProperty("body").GetProperty("profile");
            if (Text(record, "accountId") != Text(_current, "accountId")) { ClearPrivate(); _status = "The account context changed. Reopen account settings."; return; }
            ApplyProfile(record); _status = "Profile saved and acknowledged by the account service.";
        });
    }

    private async Task MutateSessionAsync(CancellationToken ct, CommandBoundary boundary)
    {
        var action = _selectedSession is null ? _pendingMutation : "RevokeSession";
        var selected = _selectedSession;
        var transport = _transport;
        if (action is null || transport is null) return;
        // Private CUI data disappears before the authenticated client's own cleanup and server mutation.
        await Present(() => { ClearPrivate(); _status = "Private views cleared. Waiting for the account service to acknowledge the session change…"; });
        ct.ThrowIfCancellationRequested();
        if (_disposed) return;
        // AccountApiClient pins the genuine original token, then awaits owner cleanup
        // before network mutation. That existing broker owns the transferred task.
        TransferToExistingBroker(boundary);
        var result = await transport.InvokeAsync(action, selected is null ? null : JsonSerializer.SerializeToElement(new { sessionId = selected }), ct);
        await Present(() => { if (Succeeded(result)) _status = "Session change acknowledged. Revalidate your account before reopening private content."; else Fail(result); });
    }

    private void ApplyProfile(JsonElement profile)
    {
        _profile = profile.Clone(); _revision = profile.GetProperty("revision").GetInt64();
        _draft.Clear(); foreach (var field in Fields) _draft[field] = Text(profile, field);
        _changed.Clear(); _conflict = false; _conflictRevision = null;
    }
    private void Fail(JsonElement result)
    {
        var code = Error(result);
        if (PrivateFailure(result)) ClearPrivate();
        if (code == "profile_conflict")
        {
            _conflict = true;
            if (result.GetProperty("error").TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object &&
                body.TryGetProperty("revision", out var revision) && revision.TryGetInt64(out var next)) _conflictRevision = next;
        }
        _status = HttpStatus(result) == 403 ? "The account service denied this action. Private account data has been cleared." : code switch
        {
            "profile_conflict" => "Your profile changed elsewhere. Your draft is preserved. Discard and reload before saving.",
            "username_unavailable" => "That username is unavailable. Your draft is preserved; choose another username.",
            "invalid_profile" or "invalid_request" or "InvalidArgument" => "Check the profile fields. The account service has not saved this edit.",
            "unauthorized" or "session_revoked_or_expired" or "AuthenticationRequired" => "Your session is unavailable. Sign in and revalidate before reopening private content.",
            "PermissionDenied" or "forbidden" or "insufficient_scope" or "origin_not_allowed" => "The account service denied this action. Private account data has been cleared.",
            "SessionContextChanged" => "Your account context changed. Reopen account settings.",
            "Cancelled" => "Account operation cancelled. No success has been confirmed.",
            "PrivateContextCleanupFailed" => "Private account cleanup failed. Keep account views closed and reload.",
            _ => "Account services are unavailable or returned an incompatible response. No success has been confirmed.",
        };
    }
    private static bool Succeeded(JsonElement result) => result.ValueKind == JsonValueKind.Object && result.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
    private static string Error(JsonElement result) => result.ValueKind == JsonValueKind.Object && result.TryGetProperty("error", out var error) ? Text(error, "code") ?? "ServiceUnavailable" : "ServiceUnavailable";
    private static int? HttpStatus(JsonElement result) => result.ValueKind == JsonValueKind.Object && result.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var code) ? code : null;
    private static bool PrivateFailure(JsonElement result) => HttpStatus(result) is 401 or 403 || Error(result) is "unauthorized" or "session_revoked_or_expired" or "AuthenticationRequired"
        or "SessionContextChanged" or "PrivateContextCleanupFailed" or "PermissionDenied" or "forbidden" or "insufficient_scope" or "origin_not_allowed";
    private static string? Text(JsonElement? value, string field) => value is { ValueKind: JsonValueKind.Object } record && record.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static object? Scalar(JsonElement value) => value.ValueKind switch
    { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
    private Task Present(Action action, long? expectedGeneration = null)
    {
        var generation = expectedGeneration ?? _generation;
        var present = _present;
        return _disposed || generation != _generation || present is null ? Task.CompletedTask : present(() =>
        { if (!_disposed && generation == _generation) { action(); Changed(); } });
    }
    private async Task<JsonElement> InvokeOwnedAsync(string action, JsonElement? args, CancellationToken ct, long? expectedGeneration = null)
    {
        ct.ThrowIfCancellationRequested();
        var generation = expectedGeneration ?? _generation;
        var transport = _transport;
        if (_disposed || generation != _generation || transport is null) throw new OperationCanceledException(ct);
        var result = await transport.InvokeAsync(action, args, ct);
        // A backend ignoring cancellation must not repopulate or start a follow-up request.
        ct.ThrowIfCancellationRequested();
        if (_disposed || generation != _generation) throw new OperationCanceledException(ct);
        return result;
    }
    /// <summary>Nonterminal native privacy fence. The owning UI invokes this on its
    /// presentation thread when the genuine session changes. No account or Home
    /// authority is conferred; existing reads/presentations cannot restore old data.</summary>
    public void ClearAccountObservations(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_ownership)
        {
            if (_disposed) return;
            ++_generation;
            ClearPrivate();
            _status = status;
        }
        Changed(); // Private fields and generation are fenced before observer callbacks.
    }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private void ClearPrivate() { _current = null; _profile = null; _sessions = []; _sessionsChecked = false; _draft.Clear(); _changed.Clear(); _conflict = false; _conflictRevision = null; _revision = 0; _selectedSession = null; _confirmation = null; _pendingMutation = null; }
    /// <summary>Callback-free, terminal, synchronous privacy fence. Cancellation and
    /// observer disposal occur only after every sensitive field and callback is detached.</summary>
    public void RevokePrivateContext()
    {
        lock (_ownership)
        {
            if (_disposed) return;
            _disposed = true;
            ++_generation;
            ClearPrivate();
            _busy = false;
            _status = "Account view closed.";
            PropertyChanged = null;
            _transport = null;
            _present = null;
            _openSignIn = null;
        }
    }

    // IDisposable is an immediate view fence and starts cleanup; root owner uses
    // awaited DisposeAsync, whose exact same task preserves cancellation faults.
    public void Dispose() { RevokePrivateContext(); _ = BeginCleanup(); }
    public ValueTask DisposeAsync() { RevokePrivateContext(); return new(BeginCleanup()); }
    private Task BeginCleanup()
    {
        TaskCompletionSource completion;
        Task[] owned;
        lock (_ownership)
        {
            if (_cleanup is not null) return _cleanup;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _cleanup = completion.Task;
            owned = _ownedBoundaries.ToArray();
        }
        _ = DrainAsync(owned, completion);
        return completion.Task;
    }
    private async Task DrainAsync(Task[] owned, TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { failures.Add(error); }
        try { await Task.WhenAll(owned); } catch (Exception error) { failures.Add(error); }
        try { _lifetime.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) completion.TrySetException(new AggregateException("Revoked account view cleanup failed.", failures));
        else completion.TrySetResult();
    }
}
