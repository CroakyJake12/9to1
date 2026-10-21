using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Automations;

/// <summary>
/// Real Home caller/session/profile/entity and action authority for automation Run/effect execution. It is
/// the typed seam the durable adapter consumes at admission and immediately before every protected effect.
///
/// Authority is derived from the ACTUAL current Home actor (<see cref="HomeLocalProfileIdentity.GetCurrentAsync"/>,
/// backed by the operating-system principal), never from a caller-supplied participant string/actor record,
/// and never from a graph's own nodes. Each admission obtains a real Home permission submission through
/// <see cref="HomeResourceOperationBroker.AuthorizeForActorAsync"/>; the effect boundary then observes that
/// EXACT original submission's real decision once (never minting a new broker Guid on retry) via
/// <see cref="HomeResourceOperationBroker.BeginExecutionCapabilityAsync"/> and
/// <see cref="HomeResourceOperationBroker.ClaimExecutionObservedAsync"/> under the SAME current actor and the
/// SAME argument digest, and <see cref="HomeResourceOperationBroker.CompleteExecutionAsync"/> records the owning
/// terminal outcome.
///
/// Custody is PER-OPERATION, not per-AutomationId+revision: two concurrent Runs of the same automation revision
/// with different sessions/arguments hold independent Home grants and never steal each other's capability. A
/// failed/incomplete Home audit RETAINS the claimed capability handle so a real retry/drain can finish the audit
/// without rerunning graph effects, and the original execution cause plus the audit failure are both exposed.
///
/// This is a RUN/effect grant (action <c>automations.run.effect</c>, resource kind <c>automation.run</c>).
/// It never broadens definition-library write actions (<c>automations.recover</c>/<c>automations.disable</c>
/// on <c>automation.definition</c>) into execution, never registers allow-all resolvers, never mints a new
/// actor, and never uses <c>WithPermission(bool)</c> as authority. When Home/permission/provenance is absent
/// every decision fails closed.
/// </summary>
public sealed class HomeAutomationRunAuthority : IAutomationRunAuthority, IAutomationRunAuditDrain
{
    private const string TargetAppId = "automations";

    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly ConcurrentDictionary<string, Entry> _bindings = new();
    private readonly ConcurrentDictionary<string, HomeResourceExecutionCapability> _active = new();
    private readonly ConcurrentDictionary<string, PendingAudit> _pendingAudits = new();
    // Serializes overlapping/identical admits so concurrent identical contexts coalesce into ONE original Home
    // review/admission (never two broker submissions racing to overwrite each other).
    private readonly System.Threading.SemaphoreSlim _authorizeGate = new(1, 1);

    private sealed record Entry(AuthenticatedResourceActor Actor, string RequestId);
    // Retains the EXACT detached terminal outcome with the same capability before cancellable completion, so a
    // retry/drain can settle that exact outcome without rerunning graph effects.
    private sealed record PendingAudit(HomeResourceExecutionCapability Capability, HomeExecutionOutcome Outcome, string? AuditFailure);

    public HomeAutomationRunAuthority(HomeLocalProfileIdentity profiles, HomeResourceOperationBroker broker)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    public ValueTask<AutomationRunAuthorityDecision> AuthorizeAsync(AutomationRunAuthorityContext context, CancellationToken cancellationToken)
        => AuthorizeCoreAsync(context, cancellationToken);

    private async ValueTask<AutomationRunAuthorityDecision> AuthorizeCoreAsync(AutomationRunAuthorityContext context, CancellationToken cancellationToken)
    {
        if (context is null) return Deny("HOME_AUTOMATION_RUN_CONTEXT_INVALID");
        var actor = await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        // The current Home actor must exist and equal the request participant. A foreign/absent actor fails closed.
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision) || actor.AccountId == Guid.Empty ||
            !string.Equals(actor.ActorId, context.ParticipantId, StringComparison.Ordinal))
            return Deny("HOME_AUTOMATION_RUN_ACTOR_MISMATCH");

        var key = OperationKey(context);

        // Serialize overlapping/identical admits so two simultaneous identical contexts coalesce into ONE
        // original Home review/admission — never two concurrent broker submissions racing to overwrite each
        // other. Independent operations (different key) also serialize here, which is acceptable for the
        // short, synchronous review-admission step and preserves exact original custody.
        await _authorizeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Retain the EXACT original Home review/operation. A later retry of the same operation observes this
            // same request — never a freshly minted broker Guid — so a manual review can admit the original run.
            if (_bindings.TryGetValue(key, out var retained))
            {
                if (!string.Equals(retained.Actor.ActorId, actor.ActorId, StringComparison.Ordinal))
                    return Deny("HOME_AUTOMATION_RUN_ACTOR_CHANGED");
                return new AutomationRunAuthorityDecision(true, "HOME_AUTOMATION_RUN_ADMITTED", actor.ActorId, retained.RequestId);
            }

            HomePermissionAuthorization authorization;
            try
            {
                authorization = await _broker.AuthorizeForActorAsync(actor, TargetAppId, context.ActionId,
                    context.Scopes, context.Arguments, "Run an automation graph effect", null, context.SessionId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
            {
                return Deny("HOME_AUTOMATION_RUN_DENIED");
            }
            if (authorization is null || string.IsNullOrWhiteSpace(authorization.RequestId))
                return Deny("HOME_AUTOMATION_RUN_DENIED");

            // A definitively declined/blocked review refuses admission. A PendingApproval or Approved request is
            // retained and admitted; its real decision is observed once at the effect boundary so a manual review
            // can later admit the original operation without re-submitting a new broker request.
            if (authorization.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked)
                return Deny("HOME_AUTOMATION_RUN_DENIED");

            // Publish under the gate: the first admit wins, and every identical concurrent caller observes the
            // SAME original review/admission (never a duplicate broker submission that would strand a pending review).
            _bindings[key] = new(actor, authorization.RequestId);
            return new AutomationRunAuthorityDecision(true, "HOME_AUTOMATION_RUN_ADMITTED", actor.ActorId, authorization.RequestId);
        }
        finally
        {
            _authorizeGate.Release();
        }
    }

    public async ValueTask<AutomationRunAuthorityDecision> AuthorizeEffectAsync(AutomationRunAuthorityContext context, CancellationToken cancellationToken)
    {
        if (context is null) return Deny("HOME_AUTOMATION_RUN_CONTEXT_INVALID");
        var key = OperationKey(context);
        if (!_bindings.TryGetValue(key, out var entry))
            return Deny("HOME_AUTOMATION_RUN_NOT_ADMITTED");

        // Re-validate the SAME current actor immediately before the protected effect.
        var actor = await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor != entry.Actor ||
            !string.Equals(actor.ActorId, context.ParticipantId, StringComparison.Ordinal))
            return Deny("HOME_AUTOMATION_RUN_ACTOR_CHANGED");

        // Observe the EXACT original Home decision once, under the same scope/digest/actor. A PendingApproval /
        // denied / revoked / profile-changed request yields no capability and refuses before any effect.
        HomeResourceExecutionCapability? capability;
        try
        {
            capability = await _broker.BeginExecutionCapabilityAsync(entry.RequestId, context.Arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return Deny("HOME_AUTOMATION_RUN_EXECUTION_UNAVAILABLE");
        }
        if (capability is null)
            return Deny("HOME_AUTOMATION_RUN_EXECUTION_UNAVAILABLE");

        HomeResourceClaimResult claim;
        try
        {
            claim = await _broker.ClaimExecutionObservedAsync(capability, TargetAppId, context.ActionId,
                context.Scopes, context.Arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return Deny("HOME_AUTOMATION_RUN_CLAIM_REJECTED");
        }
        if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != entry.Actor)
            return Deny("HOME_AUTOMATION_RUN_CLAIM_REJECTED");

        _active[key] = capability;
        return new AutomationRunAuthorityDecision(true, "HOME_AUTOMATION_RUN_EFFECT_AUTHORIZED", actor.ActorId, entry.RequestId);
    }

    public async Task<bool> CompleteEffectAsync(AutomationRunAuthorityContext context, AutomationRunAuthorityDecision authorization,
        bool succeeded, string code, string message, CancellationToken cancellationToken)
    {
        if (context is null || !authorization.Allowed) return false;
        var key = OperationKey(context);
        if (!_active.TryRemove(key, out var capability) || capability is null) return false;
        var state = succeeded ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed;
        var outcome = new HomeExecutionOutcome(state,
            string.IsNullOrWhiteSpace(code) ? (succeeded ? "HOME_AUTOMATION_RUN_SUCCEEDED" : "HOME_AUTOMATION_RUN_FAILED") : code,
            string.IsNullOrWhiteSpace(message) ? "Automation run effect completed." : message,
            [new HomeObjectReference("automation.run", context.AutomationId.ToString("D"))]);
        try
        {
            var result = await _broker.CompleteExecutionAsync(capability, outcome, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                _pendingAudits.TryRemove(key, out _);
                return true;
            }
            // Audit not acknowledged: RETAIN the exact capability handle AND the exact detached outcome so a
            // real retry/drain can finish the audit without rerunning graph effects. Expose the original
            // execution cause + the audit failure.
            _pendingAudits[key] = new PendingAudit(capability, outcome, result.Message);
            return false;
        }
        catch (Exception ex)
        {
            _pendingAudits[key] = new PendingAudit(capability, outcome, "HOME_AUTOMATION_RUN_AUDIT_FAULT: " + ex.Message);
            return false;
        }
    }

    /// <summary>Snapshots the retained, un-acknowledged Home audit handles (exact detached outcome cause + audit
    /// failure) so a real retry/drain can finish audit recovery without rerunning graph effects.</summary>
    public IReadOnlyList<AutomationRunPendingAudit> PendingAuditFailures => _pendingAudits.Select(kv =>
        new AutomationRunPendingAudit(kv.Key, kv.Value.Outcome.Code, kv.Value.AuditFailure)).ToArray();

    /// <summary>Retries the retained Home completion audit for one operation handle. This replays ONLY the exact
    /// detached outcome — never a graph effect — via the real <see cref="HomeResourceOperationBroker.CompleteExecutionAsync"/>
    /// settlement, so a cancellation that happened before the broker recorded the outcome remains retryable.</summary>
    public async Task<bool> RetryPendingAuditAsync(string operationKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationKey) || !_pendingAudits.TryGetValue(operationKey, out var pending))
            return false;
        try
        {
            var result = await _broker.CompleteExecutionAsync(pending.Capability, pending.Outcome, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                _pendingAudits.TryRemove(operationKey, out _);
                return true;
            }
            _pendingAudits[operationKey] = pending with { AuditFailure = result.Message };
            return false;
        }
        catch (Exception ex)
        {
            _pendingAudits[operationKey] = pending with { AuditFailure = ex.Message };
            return false;
        }
    }

    /// <summary>Drains every retained Home audit by replaying each exact detached outcome through the real broker
    /// settlement. Returns the operations that are still unsettled after the best-effort drain.</summary>
    public async Task<int> DrainPendingAuditsAsync(CancellationToken cancellationToken)
    {
        var keys = _pendingAudits.Keys.ToArray();
        var unsettled = 0;
        foreach (var key in keys)
        {
            if (!await RetryPendingAuditAsync(key, cancellationToken).ConfigureAwait(false))
                unsettled++;
        }
        return unsettled;
    }

    /// <summary>Per-operation custody: the actor, the canonical automation/revision, the session and the exact
    /// argument digest. Two concurrent Runs of one automation revision with different sessions/arguments are
    /// independent operations and never steal each other's capability.</summary>
    private static string OperationKey(AutomationRunAuthorityContext context)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context.Arguments.GetRawText())));
        return context.ParticipantId + "|" + context.AutomationId.ToString("D") + "|" + context.ExpectedRevision.ToString()
            + "|" + context.SessionId + "|" + digest;
    }

    private static AutomationRunAuthorityDecision Deny(string code) =>
        new(false, code, string.Empty, string.Empty);
}

/// <summary>Descriptive summary of a retained, un-acknowledged Home completion audit for one operation. It
/// exposes BOTH the original execution/persistence cause and the independent audit failure so a real retry/drain
/// can finish audit recovery without rerunning graph effects.</summary>
public sealed record AutomationRunPendingAudit(string OperationKey, string OriginalCause, string? AuditFailure);
