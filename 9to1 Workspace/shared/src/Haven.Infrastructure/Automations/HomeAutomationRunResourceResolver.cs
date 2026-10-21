using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Automations;

/// <summary>
/// Bounded, fail-closed canonical resource resolver for the automation RUN/effect action
/// (<c>automations.run.effect</c> on resource kind <c>automation.run</c>). This is a Run grant, distinct
/// from the definition library's write actions (<c>automations.recover</c>/<c>automations.disable</c> on
/// <c>automation.definition</c>).
///
/// It is deliberately narrow: it allows ONLY the single registered run action, ONLY Execute access on the
/// <c>automation.run</c> kind, and ONLY a genuine current Home local-profile actor (well-formed profile/account
/// and a live authentication revision). Every other action/kind/access/actor is denied. This is NOT an
/// allow-all resolver, never mints an actor, and never uses a boolean permission as authority. Real ACLs
/// are enforced at evaluation time against the current actor supplied by the trusted resource service.
/// </summary>
public sealed class HomeAutomationRunResourceResolver : ICanonicalResourceAccessResolver
{
    public const string ResourceKindValue = "automation.run";
    public const string RunEffectAction = "automations.run.effect";

    public string ResourceKind => ResourceKindValue;

    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A genuine current Home actor is mandatory. Home local-profile actors carry null account/org
        // identifiers (like the resource service's own actor gate), so only malformed identity is a deny.
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
            return ValueTask.FromResult(new ResourceAccessDecision(false, "HOME_AUTOMATION_RUN_ACTOR_INVALID",
                actor?.ActorId ?? string.Empty, scope?.Revision ?? string.Empty, actor?.OrganisationId));

        // Only the single registered run action on Execute access is ever admissible.
        if (!string.Equals(actionId, RunEffectAction, StringComparison.Ordinal) ||
            scope is null || !string.Equals(scope.Kind, ResourceKindValue, StringComparison.Ordinal) ||
            scope.Access != ResourceAccess.Execute ||
            string.IsNullOrWhiteSpace(scope.Id) || string.IsNullOrWhiteSpace(scope.Revision))
            return ValueTask.FromResult(new ResourceAccessDecision(false, "HOME_AUTOMATION_RUN_SCOPE_DENIED",
                actor.ActorId, scope?.Revision ?? string.Empty, actor.OrganisationId));

        return ValueTask.FromResult(new ResourceAccessDecision(true, "HOME_AUTOMATION_RUN_GRANTED",
            actor.ActorId, scope.Revision, actor.OrganisationId));
    }
}

/// <summary>
/// Trusted action catalogue for the bounded automation run/effect action. Declares the action risk to the
/// real Home permission service so approvals are required per action and the effect is treated as
/// non-reversible with external side effects. Any other action for any app returns null, which the Home
/// trust service treats as <c>HOME_ACTION_NOT_REGISTERED</c> (unknown action denied).
/// </summary>
public sealed class HomeAutomationRunActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId)
    {
        if (string.Equals(appId, "automations", StringComparison.Ordinal) &&
            string.Equals(actionId, HomeAutomationRunResourceResolver.RunEffectAction, StringComparison.Ordinal))
            return new HomePermissionActionPolicy(HomePermissionRisk.Elevated, IsReversible: false,
                HasExternalSideEffects: true, RequiresPerActionApproval: true);
        return null;
    }
}
