using System.Globalization;
using Haven.Core;

namespace Haven.Application;

public interface IPlannerCalendarResourceBinding
{
    ValueTask<bool> AllowsAsync(AuthenticatedResourceActor actor, PlannerCalendar calendar, ResourceAccess access, CancellationToken cancellationToken);
}

/// <summary>Explicit trusted profile-to-store binding for personal calendar storage; organisation access requires its own owner ACL binding.</summary>
public sealed class ProfilePlannerCalendarResourceBinding(string canonicalStoreProfileId) : IPlannerCalendarResourceBinding
{
    public ValueTask<bool> AllowsAsync(AuthenticatedResourceActor actor, PlannerCalendar calendar, ResourceAccess access, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(!string.IsNullOrWhiteSpace(canonicalStoreProfileId) && actor.ProfileId == canonicalStoreProfileId &&
            actor.OrganisationId is null && Enum.IsDefined(calendar.Permission) &&
            (access == ResourceAccess.Read || (access == ResourceAccess.Write && calendar.Permission is CalendarPermission.Owner or CalendarPermission.Writer)));
    }
}

/// <summary>Resource owner validates actual Event/Calendar records and their current permissions on each shared broker dispatch.</summary>
public sealed class PlannerEventResourceResolver(IPlannerRepository repository, IPlannerCalendarResourceBinding binding) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "planner.event";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny(string code) => new(false, code, actor.ActorId, scope.Revision, actor.OrganisationId);
        if (scope.Kind != ResourceKind || !Guid.TryParse(scope.Id, out var id) || scope.Access == ResourceAccess.Execute ||
            actionId is not ("maps.planner.read" or "maps.planner.attach" or "maps.planner.start")) return Deny("PlannerScopeInvalid");
        if ((actionId == "maps.planner.attach") != (scope.Access == ResourceAccess.Write)) return Deny("PlannerScopeAccessInvalid");
        var plannerEvent = await repository.GetEventAsync(id, cancellationToken).ConfigureAwait(false);
        if (plannerEvent is null || plannerEvent.DeletedAt is not null) return Deny("PlannerEventUnavailable");
        var revision = plannerEvent.UpdatedAt.ToString("O", CultureInfo.InvariantCulture);
        if (revision != scope.Revision) return Deny("PlannerEventRevisionConflict");
        if (scope.Access == ResourceAccess.Write && plannerEvent.IsReadOnly) return Deny("PlannerEventReadOnly");
        var calendar = (await repository.GetCalendarsAsync(false, cancellationToken).ConfigureAwait(false)).SingleOrDefault(item => item.Id == plannerEvent.CalendarId);
        if (calendar is null || !await binding.AllowsAsync(actor, calendar, scope.Access, cancellationToken).ConfigureAwait(false)) return Deny("PlannerCalendarPermissionDenied");
        return new(true, "Allowed", actor.ActorId, revision, actor.OrganisationId);
    }
}

public sealed class ResourceAuthorisedPlannerEventJourneyPolicy(ResourceAuthorizationService authorization) : IPlannerEventJourneyPolicy
{
    public async ValueTask<bool> MayReadAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) =>
        await authorization.AuthorizeAsync("maps.planner.read", [Scope(plannerEvent, ResourceAccess.Read)], cancellationToken).ConfigureAwait(false) is not null;
    public async ValueTask<bool> MayAttachJourneyAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) =>
        await authorization.AuthorizeAsync("maps.planner.attach", [Scope(plannerEvent, ResourceAccess.Write)], cancellationToken).ConfigureAwait(false) is not null;
    private static ResourceScope Scope(PlannerEvent plannerEvent, ResourceAccess access) =>
        new("planner.event", plannerEvent.Id.ToString("N"), plannerEvent.UpdatedAt.ToString("O", CultureInfo.InvariantCulture), access);
}
