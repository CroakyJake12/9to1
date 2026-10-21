using Haven.Application;
using Haven.Application.NodeGraph;
using Haven.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Automations;

/// <summary>
/// Bounded, fail-closed canonical resource resolver for the automation RUN/effect action
/// (<c>automations.run.effect</c> on resource kind <c>automation.run</c>). This is a Run grant, distinct
/// from the definition library's write actions (<c>automations.recover</c>/<c>automations.disable</c> on
/// <c>automation.definition</c>).
///
/// Unlike a string/Execute-only grant, this resolver resolves the ACTUAL maintained canonical Automation from
/// the owning repository, verifies it is a genuine, enabled object owned by the CURRENT Home profile/actor,
/// and verifies the requested revision is the current Active revision produced by the canonical graph
/// repository. It never grants an arbitrary id/revision, never registers an allow-all resolver, never accepts
/// a scripted bool or supplied participant string as authority, and never broadens a definition write into
/// execution. When the canonical repository/graph source is absent (unwired), every decision fails closed.
///
/// The repository/graph sources are deferred <c>Func</c> factories (the same deferred-composition pattern as
/// <c>HomeCanonicalAutomationDefinitionResourceResolver</c>) because the Home composition is constructed before
/// the lazily-created repository singletons are resolved; evaluation happens at runtime, after DI is built.
/// </summary>
public sealed class HomeAutomationRunResourceResolver : ICanonicalResourceAccessResolver
{
    public const string ResourceKindValue = "automation.run";
    public const string RunEffectAction = "automations.run.effect";

    private readonly Func<IAutomationRepository?> _repository;
    private readonly Func<IVersionedNodeGraphRepository?> _graphs;

    public HomeAutomationRunResourceResolver(Func<IAutomationRepository?>? repository = null,
        Func<IVersionedNodeGraphRepository?>? graphs = null)
    {
        _repository = repository ?? static () => null;
        _graphs = graphs ?? static () => null;
    }

    public string ResourceKind => ResourceKindValue;

    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A genuine current Home actor is mandatory.
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
            return Deny("HOME_AUTOMATION_RUN_ACTOR_INVALID", actor, scope);

        // Only the single registered run action on Execute access is ever admissible.
        if (!string.Equals(actionId, RunEffectAction, StringComparison.Ordinal) ||
            scope is null || !string.Equals(scope.Kind, ResourceKindValue, StringComparison.Ordinal) ||
            scope.Access != ResourceAccess.Execute ||
            !Guid.TryParse(scope.Id, out var objectId) || objectId == Guid.Empty ||
            !long.TryParse(scope.Revision, out var expectedRevision) || expectedRevision < 1)
            return Deny("HOME_AUTOMATION_RUN_SCOPE_DENIED", actor, scope);

        // Canonical resolution from the owning repository. No repository -> no canonical object -> fail closed.
        var repository = _repository();
        if (repository is null)
            return Deny("HOME_AUTOMATION_RUN_CATALOGUE_UNAVAILABLE", actor, scope);

        IReadOnlyList<AutomationDefinition> all;
        try
        {
            all = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return Deny("HOME_AUTOMATION_RUN_REPOSITORY_FAULT", actor, scope);
        }

        // The scope id is either the maintained Automation definition id (resume) or the canonical graph id the
        // definition is bound to (fresh start). Resolve the SAME maintained object either way.
        var definition = all.FirstOrDefault(item => item is not null &&
            (item.Id == objectId || item.GraphBinding?.GraphId == objectId));
        if (definition is null)
            return Deny("HOME_AUTOMATION_RUN_OBJECT_UNKNOWN", actor, scope);
        if (!definition.IsEnabled)
            return Deny("HOME_AUTOMATION_RUN_OBJECT_DISABLED", actor, scope);

        // Owner/current-profile check against the SAME Home actor. Action-scoped approval never establishes
        // underlying object authority: the object must actually belong to this profile/actor.
        if (definition.OwnerBinding is not { } owner ||
            !string.Equals(owner.ProfileId, actor.ProfileId, StringComparison.Ordinal) ||
            !string.Equals(owner.ActorId, actor.ActorId, StringComparison.Ordinal))
            return Deny("HOME_AUTOMATION_RUN_OBJECT_NOT_OWNED", actor, scope);

        // The current pinned revision producer must confirm the requested revision is the current Active rev.
        var graphs = _graphs();
        if (graphs is null)
            return Deny("HOME_AUTOMATION_RUN_GRAPH_REVISION_UNAVAILABLE", actor, scope);
        OwnedGraphDefinition? owned;
        try
        {
            owned = await graphs.GetAsync(definition.GraphBinding?.GraphId ?? definition.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return Deny("HOME_AUTOMATION_RUN_GRAPH_REVISION_FAULT", actor, scope);
        }

        // A fresh start must target the current Active revision. A resume, however, legitimately binds the
        // exact historical pin of a genuine persisted resumable Run (e.g. a V4 Run resuming after V5 is
        // Active). Only that persisted-run pin resumes; a fresh stale start stays refused.
        var granted = false;
        if (owned?.Active is { } active && active.State == GraphRevisionState.Active)
        {
            if (active.Revision == expectedRevision)
            {
                granted = true;
            }
            else
            {
                try
                {
                    var runs = await repository.GetRunsAsync(definition.Id, 200, cancellationToken).ConfigureAwait(false);
                    granted = runs.Any(run => run is { Status: AutomationRunStatus.Waiting or AutomationRunStatus.ApprovalRequired } &&
                        (run.PinnedGraph?.Revision ?? run.DefinitionRevision) == expectedRevision);
                }
                catch (Exception)
                {
                    granted = false; // repository fault fails closed
                }
            }
        }
        if (!granted)
            return Deny("HOME_AUTOMATION_RUN_REVISION_NOT_CURRENT", actor, scope);

        return new ResourceAccessDecision(true, "HOME_AUTOMATION_RUN_GRANTED",
            actor.ActorId, scope.Revision, actor.OrganisationId);
    }

    private static ResourceAccessDecision Deny(string code, AuthenticatedResourceActor? actor, ResourceScope? scope) =>
        new(false, code, actor?.ActorId ?? string.Empty, scope?.Revision ?? string.Empty, actor?.OrganisationId);
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
