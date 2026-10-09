using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    public Task<HomePreparedReviewObservation> AuthorizePreparedReviewWithinOriginalSourceAsync(
        HomeResourcePreparedReview prepared, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => RunColdProjectBrokerAsync(scope, retain, async sources =>
        {
            RequirePrepared(prepared); var first = prepared.Reserve();
            await WaitColdProjectBrokerGateAsync(sources, prepared.Gate, token).ConfigureAwait(false);
            try
            {
                RequirePrepared(prepared);
                if (!first) return await ObserveColdProjectPreparedAsync(sources, prepared, token).ConfigureAwait(false);
                var actor = await sources.ReadAsync(() => resources.AuthorizeForActorWithinOriginalSourceAsync(
                    prepared.Actor, prepared.Submission.Scope.ActionName, prepared.Scopes, sources.Run, sources.Retain, token)).ConfigureAwait(false);
                if (actor != prepared.Actor)
                {
                    prepared.KnownDenied = true;
                    return new(HomePreparedReviewState.AdmissionDenied, null, false, "HOME_PREPARED_RESOURCE_DENIED");
                }
                prepared.CanonicalAdmission = true;
                var result = await sources.ReadAsync(() => permissions.AuthorizeAsync(prepared.Submission, token)).ConfigureAwait(false);
                prepared.Conflict = result.Code == "HOME_REQUEST_ID_CONFLICT";
                return await ObserveColdProjectPreparedAsync(sources, prepared, token).ConfigureAwait(false);
            }
            finally { prepared.Gate.Release(); }
        });
    public Task<HomePreparedReviewObservation> ObservePreparedReviewWithinOriginalSourceAsync(
        HomeResourcePreparedReview prepared, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => RunColdProjectBrokerAsync(scope, retain, async sources =>
        {
            RequirePrepared(prepared);
            await WaitColdProjectBrokerGateAsync(sources, prepared.Gate, token).ConfigureAwait(false);
            try { RequirePrepared(prepared); return await ObserveColdProjectPreparedAsync(sources, prepared, token).ConfigureAwait(false); }
            finally { prepared.Gate.Release(); }
        });
    private async Task<HomePreparedReviewObservation> ObserveColdProjectPreparedAsync(
        HomeOwnershipOriginalSourceCallbacks sources, HomeResourcePreparedReview prepared, CancellationToken token)
    {
        if (!prepared.AttemptReserved) return new(HomePreparedReviewState.NotAttempted, null, false, "HOME_PREPARED_NOT_ATTEMPTED");
        if (prepared.KnownDenied || prepared.Conflict) return new(HomePreparedReviewState.AdmissionDenied, null, false, "HOME_PREPARED_ADMISSION_DENIED");
        var request = await sources.ReadAsync(() => permissions.ReadRequestObservationAsync(prepared.RequestId, token)).ConfigureAwait(false);
        if (request is null || !prepared.CanonicalAdmission || !MatchesPrepared(prepared.Submission, request))
            return new(HomePreparedReviewState.OutcomeUnconfirmed, null, false, "HOME_PREPARED_REQUEST_UNCONFIRMED");
        if (!prepared.BoundOnce && request.State is HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved)
        {
            // Direct configured policy lookup, under the actual parent boundary: no swallowed
            // synchronous policy cause and no caller-supplied policy can issue this binding.
            var policy = sources.Invoke(() => permissions.ResolveOriginalTrustedActionPolicy(
                prepared.Submission.Scope.TargetAppId, prepared.Submission.Scope.ActionName));
            if (policy is null) throw new UnauthorizedAccessException("The actual original trusted action policy is unavailable.");
            var binding = new Binding(prepared.Actor, prepared.Submission.Scope.TargetAppId,
                prepared.Submission.Scope.ActionName, prepared.Scopes.ToArray(), prepared.Submission.Impact.ArgumentsDigest!, prepared.Submission, policy);
            if (!_bindings.TryAdd(prepared.RequestId, binding))
                return new(HomePreparedReviewState.OutcomeUnconfirmed, request, false, "HOME_PREPARED_BINDING_CONFLICT");
            prepared.BoundOnce = true;
        }
        return new(HomePreparedReviewState.RequestObserved, request, prepared.BoundOnce, "HOME_PREPARED_REQUEST_OBSERVED");
    }
    public Task<HomeResourceExecutionCapability?> BeginExecutionCapabilityWithinOriginalSourceAsync(
        string requestId, JsonElement arguments, Action<Action> scope, Action<Task> retain,
        Action<Action> cleanupScope, CancellationToken token)
        => RunColdProjectBrokerAsync(scope, retain, async sources =>
        {
            if (_executions.Count >= 1024 || !_bindings.TryGetValue(requestId, out var binding) || binding.Digest != Digest(arguments)) return null;
            var current = await sources.ReadAsync(() => resources.AuthorizeForActorWithinOriginalSourceAsync(
                binding.Actor, binding.ActionId, binding.Scopes, sources.Run, sources.Retain, token)).ConfigureAwait(false);
            if (current != binding.Actor) return null;
            var approval = await sources.ReadAsync(() => permissions.GetAuthorizationAsync(requestId, token)).ConfigureAwait(false);
            if (!approval.IsAllowed || !_bindings.TryRemove(requestId, out var consumed) || consumed != binding) return null;
            var capability = new HomeResourceExecutionCapability(this, requestId, binding.TargetAppId,
                binding.ActionId, Array.AsReadOnly(binding.Scopes.ToArray()));
            try
            {
                if (!(await sources.ReadAsync(() => permissions.BeginExecutionAsync(requestId, token)).ConfigureAwait(false)).IsAllowed) return null;
                return _executions.TryAdd(capability, binding) ? capability : null;
            }
            catch (Exception primary)
            {
                capability.MarkRejected(this, HomeResourceRejectionKind.Begin); _rejectedBegins[requestId] = capability;
                try
                {
                    var audit = await RunColdProjectBrokerAsync(cleanupScope, retain,
                        cleanup => cleanup.ReadAsync(() => RetryRejectedBeginAuditAsync(requestId, CancellationToken.None))).ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("The actual rejected begin audit is unfinished: " + audit.Code);
                }
                catch (Exception cleanup) { throw new AggregateException("Original begin and actual rejection audit failed.", primary, cleanup); }
                ExceptionDispatchInfo.Capture(primary).Throw(); throw;
            }
        });
    public Task<HomeResourceClaimResult> ClaimExecutionWithinOriginalSourceAsync(
        HomeResourceExecutionCapability capability, string targetAppId, string actionId,
        IReadOnlyList<ResourceScope> scopes, JsonElement arguments, Action<Action> scope, Action<Task> retain,
        Action<Action> cleanupScope, CancellationToken token)
        => RunColdProjectBrokerAsync<HomeResourceClaimResult>(scope, retain, async sources =>
        {
            ArgumentNullException.ThrowIfNull(capability); ArgumentNullException.ThrowIfNull(scopes);
            var captured = sources.Invoke(() => scopes.Take(1001).ToArray());
            if (captured.Length > 1000 || captured.Any(value => value is null))
                return new(HomeResourceClaimDisposition.InputNotConsumed, null);
            if (!capability.IssuedBy(this) || capability.TargetAppId != targetAppId || capability.ActionId != actionId ||
                !_executions.TryGetValue(capability, out var binding) || binding.Digest != Digest(arguments) ||
                !binding.Scopes.SequenceEqual(captured)) return new(HomeResourceClaimDisposition.InputNotConsumed, null);
            if (!_executions.TryRemove(capability, out var consumed) || consumed != binding)
                return new(HomeResourceClaimDisposition.Unavailable, null);
            try
            {
                var current = await sources.ReadAsync(() => resources.AuthorizeForActorWithinOriginalSourceAsync(
                    binding.Actor, binding.ActionId, binding.Scopes, sources.Run, sources.Retain, token)).ConfigureAwait(false);
                if (current == binding.Actor && await sources.ReadAsync(() => permissions.IsExecutionCurrentAsync(capability.RequestId, token)).ConfigureAwait(false))
                {
                    var observed = binding.OriginalSubmission is null || binding.OriginalPolicy is null ? null
                        : await sources.ReadAsync(() => permissions.ReadRequestObservationAsync(capability.RequestId, token)).ConfigureAwait(false);
                    if ((binding.OriginalSubmission is null || binding.OriginalPolicy is null || MatchesClaimedOriginal(binding, observed)) && capability.MarkClaimed(this))
                    {
                        if (observed is not null) RetainClaimedAttestation(capability, binding, observed);
                        return new(HomeResourceClaimDisposition.Claimed, current);
                    }
                }
            }
            catch (Exception primary)
            {
                capability.MarkRejected(this);
                try { await AuditColdProjectRejectedClaimAsync(capability, cleanupScope, retain).ConfigureAwait(false); }
                catch (Exception cleanup) { throw new AggregateException("Original claim and actual rejection audit failed.", primary, cleanup); }
                ExceptionDispatchInfo.Capture(primary).Throw(); throw;
            }
            capability.MarkRejected(this);
            await AuditColdProjectRejectedClaimAsync(capability, cleanupScope, retain).ConfigureAwait(false);
            return new(HomeResourceClaimDisposition.ConsumedRejected, null);
        });
    private async Task AuditColdProjectRejectedClaimAsync(HomeResourceExecutionCapability capability,
        Action<Action> cleanupScope, Action<Task> retain)
    {
        var audit = await RunColdProjectBrokerAsync(cleanupScope, retain,
            sources => sources.ReadAsync(() => RetryRejectedClaimAuditAsync(capability, CancellationToken.None))).ConfigureAwait(false);
        if (!audit.Succeeded) throw new InvalidOperationException("Actual original rejected claim audit is unfinished: " + audit.Code);
    }
    private static async Task WaitColdProjectBrokerGateAsync(HomeOwnershipOriginalSourceCallbacks sources,
        SemaphoreSlim gate, CancellationToken token)
    {
        Task? raw = null; Exception? invocation = null, observed = null; var held = false;
        try { _ = sources.Invoke(() => { raw = gate.WaitAsync(token); sources.Retain(raw); return raw; }); }
        catch (Exception cause) { invocation = cause; }
        if (raw is not null)
            try { await raw.ConfigureAwait(false); held = true; }
            catch (Exception cause) { observed = raw.Exception ?? cause; }
        if (invocation is not null || observed is not null)
        {
            if (held) gate.Release();
            if (invocation is null && raw?.IsCanceled == true && observed is OperationCanceledException)
                ExceptionDispatchInfo.Capture(observed).Throw();
            throw new AggregateException("Actual prepared review gate and finite source failed.",
                new[] { invocation, observed }.Where(value => value is not null).Cast<Exception>());
        }
        if (!held) throw new InvalidOperationException("No actual prepared review gate was acquired.");
    }

    private static async Task<T> RunColdProjectBrokerAsync<T>(Action<Action> scope, Action<Task> retain,
        Func<HomeOwnershipOriginalSourceCallbacks, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var originals = new CloudflareOriginalTaskLedger(); HomeOwnershipOriginalSourceCallbacks sources = null!;
        sources = new(scope, raw => { _ = originals.Track(raw); sources.Run(() => retain(raw)); });
        T result = default!; Exception? primary = null;
        try { result = await body(sources).ConfigureAwait(false); }
        catch (Exception cause) { primary = cause; originals.Retain(cause); }
        await originals.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in sources.Errors) originals.Retain(cause);
        if (primary is OperationCanceledException && originals.OriginalTasks.Any(raw => raw.IsCanceled) &&
            originals.OriginalTasks.All(raw => !raw.IsFaulted) && originals.OriginalErrors.All(cause => cause is OperationCanceledException))
            ExceptionDispatchInfo.Capture(primary).Throw();
        if (originals.OriginalErrors.Count != 0) throw new AggregateException("Actual scoped Home broker source failed.", originals.OriginalErrors);
        return result;
    }
}
