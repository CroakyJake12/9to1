using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Cui.AI;

namespace HavenOS.Apps.Terminal;

/// <summary>Home approval and model routing for registered live-session typed signals only.
/// The model supplies an inert signal choice; the owner supplies identity, revision, impact and the actual operation.</summary>
public sealed class HomeTerminalActionBroker(TerminalOwnedSessionRegistry sessions, TerminalOwnedSignalExecutor executor,
    HomeResourceOperationBroker home, HomePermissionTrustService permissions, IDulcheAppClient client) : ITerminalActionBroker, IDisposable
{
    private sealed class Pending(TerminalResolvedAction action, TerminalSignalIntent intent)
    {
        public TerminalResolvedAction Action { get; } = action;
        public TerminalSignalIntent Intent { get; } = intent;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? RequestId;
    }
    private enum AuditKind { Completion, RejectedClaim, UnclaimedAbort, RejectedBegin }
    private sealed record AuditRecovery(HomeResourceExecutionCapability? Capability, string RequestId, AuditKind Kind);
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
    private readonly ConcurrentDictionary<Guid, AuditRecovery> _auditRecovery = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public async Task<TerminalResolvedAction> ResolveAsync(Guid sessionId, TerminalEnvironmentId environmentId, string request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(request) || request.Length > 16384) throw new ArgumentException("A bounded process-control request is required.", nameof(request));
        if (_pending.Count + _auditRecovery.Count >= 1024) throw new InvalidOperationException("Too many unconsumed Terminal previews; close this Terminal action session before creating more.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var snapshot = await sessions.GetAsync(sessionId, linked.Token).ConfigureAwait(false);
        if (snapshot.EnvironmentID != environmentId) throw new UnauthorizedAccessException("The requested environment is not the live session environment.");
        var context = new AppAiContextSnapshot("terminal", "terminal.signal-resolution", sessionId.ToString("D"), "Live Terminal process control", null,
            new Dictionary<string, JsonElement> { ["session"] = JsonSerializer.SerializeToElement(snapshot) }, AppAiDataSensitivity.Restricted, DateTimeOffset.UtcNow,
            snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var prompt = new AppAiPrompt("Classify only an explicit request to interrupt, terminate or kill THIS live Terminal process. " +
            "Return exactly JSON {\"signal\":\"Interrupt\"}, {\"signal\":\"Terminate\"}, {\"signal\":\"Kill\"}, or {\"signal\":null} when the request is not clearly one of these. " +
            "Do not emit commands, execute actions, reinterpret unrelated requests as process control, or infer a different target. User request:\n" + request,
            context, Guid.NewGuid().ToString("N"), AppAiAccessMode.ReadOnly, []);
        var text = new StringBuilder();
        await foreach (var chunk in client.StreamAsync(prompt, linked.Token).ConfigureAwait(false))
        {
            if (chunk.RequestedAction is not null || chunk.Text.Length > 4096 - text.Length)
                throw new InvalidOperationException("Home returned an unsupported process-control resolution; nothing was executed.");
            text.Append(chunk.Text);
        }
        TerminalProcessSignal signal;
        try
        {
            using var json = JsonDocument.Parse(text.ToString());
            if (json.RootElement.ValueKind != JsonValueKind.Object || json.RootElement.EnumerateObject().Count() != 1 ||
                !json.RootElement.TryGetProperty("signal", out var value) || value.ValueKind != JsonValueKind.String ||
                value.GetString() is not ("Interrupt" or "Terminate" or "Kill") || !Enum.TryParse(value.GetString(), out signal))
                throw new InvalidOperationException("This Terminal request has no supported typed process-control operation. No shell fallback is available.");
        }
        catch (JsonException) { throw new InvalidOperationException("Home could not resolve a typed process-control operation. Nothing was executed."); }
        var intent = await TerminalSignalIntent.CaptureAsync(sessions, sessionId, signal, linked.Token).ConfigureAwait(false);
        if (intent.Session != snapshot) throw new InvalidOperationException("The Terminal session changed while the request was being resolved. Request a new preview.");
        var action = new TerminalResolvedAction(Guid.NewGuid(), sessionId, environmentId, TerminalActionKind.TypedApi,
            TerminalSignalIntent.AppID, TerminalSignalIntent.ActionID,
            $"Send {signal} to process {snapshot.ProcessID} in {snapshot.EnvironmentID.Value} (session revision {snapshot.Revision}).",
            Array.AsReadOnly(new[] { sessionId.ToString("D") }), signal == TerminalProcessSignal.Interrupt ? TerminalActionRisk.Mutating : TerminalActionRisk.Destructive,
            false, false, null, "Process effects and unsaved state are unknown; a sent signal cannot be rolled back.");
        if (!_pending.TryAdd(action.Id, new(action, intent))) throw new InvalidOperationException("The Terminal preview could not be registered.");
        return action;
    }

    public async Task<TerminalActionExecutionResult> ExecuteAsync(TerminalResolvedAction action, string? verificationToken = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(action);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (!_pending.TryGetValue(action.Id, out var pending) || JsonSerializer.Serialize(action) != JsonSerializer.Serialize(pending.Action))
            return Denied(action, "TerminalPreviewMismatch", "The action is not this broker's unchanged registered preview.");
        await pending.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!_pending.TryGetValue(action.Id, out var current) || !ReferenceEquals(current, pending))
                return Denied(action, "TerminalActionConsumed", "The resolved Terminal action has already been consumed.");
            if (verificationToken is not null && verificationToken != pending.RequestId)
                return Denied(action, "TerminalVerificationMismatch", "The verification reference is not bound to this action.");
            if (await sessions.GetAsync(action.SessionId, linked.Token).ConfigureAwait(false) != pending.Intent.Session)
            { _pending.TryRemove(action.Id, out _); return Denied(action, "TerminalSessionChanged", "The session changed. Request a new preview."); }
            if (pending.RequestId is null)
            {
                var authorization = await home.AuthorizeAsync(TerminalSignalIntent.AppID, TerminalSignalIntent.ActionID,
                    pending.Intent.Scopes, pending.Intent.Arguments, action.Summary + " " + action.UnknownImpact, null,
                    action.SessionId.ToString("D"), linked.Token).ConfigureAwait(false);
                pending.RequestId = authorization.RequestId;
            }
            var approved = await permissions.GetAuthorizationAsync(pending.RequestId, linked.Token).ConfigureAwait(false);
            if (approved.State == HomePermissionRequestState.PendingApproval)
                return new(false, "PermissionRequired", "Review this exact process signal in Home, then retry the unchanged preview.", pending.RequestId);
            if (!approved.IsAllowed)
            { _pending.TryRemove(action.Id, out _); return Denied(action, approved.Code, approved.Message); }
            HomeResourceExecutionCapability? capability;
            try { capability = await home.BeginExecutionCapabilityAsync(pending.RequestId, pending.Intent.Arguments, linked.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                _pending.TryRemove(action.Id, out _);
                _auditRecovery[action.Id] = new(null, pending.RequestId, AuditKind.RejectedBegin);
                _ = await RetryAuditAsync(action.Id).ConfigureAwait(false);
                return new(false, "NeedsRecovery", "Dispatch admission failed. No signal was issued; only its audit may be retried.");
            }
            _pending.TryRemove(action.Id, out _);
            if (capability is null) return Denied(action, "TerminalApprovalUnavailable", "Home approval expired, changed or was consumed. No signal was sent by this call.");
            HomeExecutionOutcome outcome;
            var sent = false;
            try
            {
                await executor.ExecuteAsync(pending.Intent, capability, linked.Token).ConfigureAwait(false);
                sent = true;
                outcome = new(HomePermissionRequestState.Succeeded, "TerminalSignalSent",
                    "The owning session sent the approved signal; process exit is not implied.",
                    [new(pending.Intent.Scopes[0].Kind, pending.Intent.Scopes[0].Id)]);
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                outcome = new(HomePermissionRequestState.PartiallyCompleted, "TerminalSignalNeedsRecovery",
                    "Signal completion could not be confirmed. Inspect the current process before requesting another action.", []);
            }
            var recorded = await RecordAsync(action.Id, capability, outcome).ConfigureAwait(false);
            if (sent && recorded)
                return new(true, "Executed", "The approved signal was sent. Process exit is not implied.");
            return new(false, "NeedsRecovery", sent
                ? "The signal was sent, but Home could not record its outcome. Retry only the audit; do not replay this action."
                : "Signal completion could not be confirmed. Inspect the process before requesting another action.",
                Failure: new("TerminalSignalNeedsRecovery", "The signal outcome requires inspection.", action.SessionId.ToString("D"), true, false));
        }
        finally { pending.Gate.Release(); }
    }

    private async Task<bool> RecordAsync(Guid actionId, HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        _auditRecovery[actionId] = new(capability, capability.RequestId, AuditKind.Completion);
        try
        {
            var result = await home.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false);
            if (result.Code == "HOME_EXECUTION_COMPLETION_NOT_OWNED")
            {
                _auditRecovery[actionId] = new(capability, capability.RequestId, AuditKind.RejectedClaim);
                result = await home.RetryRejectedClaimAuditAsync(capability, CancellationToken.None).ConfigureAwait(false);
                if (result.Code == "HOME_CLAIM_REJECTION_NOT_OWNED")
                {
                    _auditRecovery[actionId] = new(capability, capability.RequestId, AuditKind.UnclaimedAbort);
                    result = await home.AbortUnclaimedExecutionAsync(capability, CancellationToken.None).ConfigureAwait(false);
                }
            }
            if (result.Succeeded) _auditRecovery.TryRemove(actionId, out _);
            return result.Succeeded;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Host-lifetime recovery of an already-consumed action's exact audit only. Never resolves or signals a process.</summary>
    public async Task<HomePermissionOperationResult> RetryAuditAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_auditRecovery.TryGetValue(actionId, out var recovery))
            return new(false, "TerminalAuditNotOwned", "This host retains no pending audit for this consumed action.");
        try
        {
            var result = recovery.Kind switch
            {
                AuditKind.Completion => await home.RetryCompletionAuditAsync(recovery.Capability!, cancellationToken).ConfigureAwait(false),
                AuditKind.RejectedClaim => await home.RetryRejectedClaimAuditAsync(recovery.Capability!, cancellationToken).ConfigureAwait(false),
                AuditKind.UnclaimedAbort => await home.AbortUnclaimedExecutionAsync(recovery.Capability!, cancellationToken).ConfigureAwait(false),
                _ => await home.RetryRejectedBeginAuditAsync(recovery.RequestId, cancellationToken).ConfigureAwait(false)
            };
            if (!result.Succeeded && recovery.Kind != AuditKind.Completion && result.Code is
                "HOME_BEGIN_AUDIT_NOT_OWNED" or "HOME_CLAIM_REJECTION_NOT_OWNED" or "HOME_EXECUTION_ABORT_NOT_OWNED")
            {
                // Automatic negative audit may already have completed and released its local handle.
                // Observe Home's actual terminal decision rather than rewriting it or issuing another signal.
                var observed = recovery.Capability is { } handle
                    ? await home.GetExecutionDecisionAsync(handle, cancellationToken).ConfigureAwait(false)
                    : await permissions.GetAuthorizationAsync(recovery.RequestId, cancellationToken).ConfigureAwait(false);
                if (observed?.State is HomePermissionRequestState.Failed or HomePermissionRequestState.Cancelled or
                    HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked or
                    HomePermissionRequestState.Succeeded or HomePermissionRequestState.PartiallyCompleted)
                    result = new(true, "TerminalAuditObserved", "Home's existing terminal decision was observed; no process operation was retried.");
                else if (observed?.State is HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved)
                {
                    _auditRecovery.TryRemove(actionId, out _);
                    return new(false, "TerminalAuditNotOwned", "No dispatch was admitted and this host retains no failed-dispatch audit right. No signal was retried.");
                }
            }
            if (result.Succeeded) _auditRecovery.TryRemove(actionId, out _);
            return result;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
        { return new(false, "TerminalAuditUnavailable", "Audit storage remains unavailable; no process signal was retried."); }
    }
    async Task<TerminalActionExecutionResult> ITerminalActionBroker.RetryAuditAsync(Guid actionId, CancellationToken cancellationToken)
    {
        var result = await RetryAuditAsync(actionId, cancellationToken).ConfigureAwait(false);
        return new(false, result.Succeeded ? "AuditRecorded" : result.Code == "TerminalAuditNotOwned" ? "Unavailable" : "NeedsRecovery",
            result.Succeeded ? result.Code == "TerminalAuditObserved" ? result.Message : "The original consumed action outcome was recorded; no process operation was retried." : result.Message);
    }
    private static TerminalActionExecutionResult Denied(TerminalResolvedAction action, string code, string message) =>
        new(false, "Denied", message, Failure: new(code, message, action.SessionId.ToString("D"), true));
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _pending.Clear(); _auditRecovery.Clear(); _lifetime.Dispose(); }
}
