using System.Collections.Concurrent;
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
/// <see cref="HomeResourceOperationBroker.AuthorizeForActorAsync"/>; the effect boundary then consumes that
/// exact grant once via <see cref="HomeResourceOperationBroker.BeginExecutionCapabilityAsync"/> and
/// <see cref="HomeResourceOperationBroker.ClaimExecutionObservedAsync"/> under the SAME current actor and the
/// SAME argument digest, and <see cref="HomeResourceOperationBroker.CompleteExecutionAsync"/> records the owning
/// terminal outcome. Non-serializable issuer-bound capabilities are held per run and consumed once.
///
/// This is a RUN/effect grant (action <c>automations.run.effect</c>, resource kind <c>automation.run</c>).
/// It never broadens definition-library write actions (<c>automations.recover</c>/<c>automations.disable</c>
/// on <c>automation.definition</c>) into execution, never registers allow-all resolvers, never mints a new
/// actor, and never uses <c>WithPermission(bool)</c> as authority. When Home/permission/provenance is absent
/// every decision fails closed.
/// </summary>
public sealed class HomeAutomationRunAuthority : IAutomationRunAuthority
{
    private const string TargetAppId = "automations";

    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly ConcurrentDictionary<string, Entry> _bindings = new();
    private readonly ConcurrentDictionary<string, HomeResourceExecutionCapability> _active = new();

    private sealed record Entry(AuthenticatedResourceActor Actor, string RequestId);

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
        if (authorization is null || !authorization.IsAllowed || string.IsNullOrWhiteSpace(authorization.RequestId))
            return Deny("HOME_AUTOMATION_RUN_DENIED");

        var key = Key(context);
        _bindings[key] = new(actor, authorization.RequestId);
        return new AutomationRunAuthorityDecision(true, "HOME_AUTOMATION_RUN_ADMITTED", actor.ActorId, authorization.RequestId);
    }

    public async ValueTask<AutomationRunAuthorityDecision> AuthorizeEffectAsync(AutomationRunAuthorityContext context, CancellationToken cancellationToken)
    {
        if (context is null) return Deny("HOME_AUTOMATION_RUN_CONTEXT_INVALID");
        var key = Key(context);
        if (!_bindings.TryGetValue(key, out var entry))
            return Deny("HOME_AUTOMATION_RUN_NOT_ADMITTED");

        // Re-validate the SAME current actor immediately before the protected effect.
        var actor = await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor != entry.Actor ||
            !string.Equals(actor.ActorId, context.ParticipantId, StringComparison.Ordinal))
            return Deny("HOME_AUTOMATION_RUN_ACTOR_CHANGED");

        // Consume the exact granted Home execution capability once, under the same scope/digest/actor.
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
        var key = Key(context);
        if (!_active.TryRemove(key, out var capability) || capability is null) return false;
        var state = succeeded ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed;
        var outcome = new HomeExecutionOutcome(state,
            string.IsNullOrWhiteSpace(code) ? (succeeded ? "HOME_AUTOMATION_RUN_SUCCEEDED" : "HOME_AUTOMATION_RUN_FAILED") : code,
            string.IsNullOrWhiteSpace(message) ? "Automation run effect completed." : message,
            [new HomeObjectReference("automation.run", context.AutomationId.ToString("D"))]);
        try
        {
            var result = await _broker.CompleteExecutionAsync(capability, outcome, cancellationToken).ConfigureAwait(false);
            return result.Succeeded;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string Key(AutomationRunAuthorityContext context) =>
        context.AutomationId.ToString("D") + "|" + context.ExpectedRevision.ToString();

    private static AutomationRunAuthorityDecision Deny(string code) =>
        new(false, code, string.Empty, string.Empty);
}
