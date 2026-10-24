using System.Globalization;
using Haven.Application;

namespace HavenOS.Home.PermissionsTrustNotifications;

/// <summary>Projects the existing permission journal into Home Events. It owns no event store
/// and exposes only requests bound to the same original local profile as this Home session.</summary>
public sealed class HomePermissionActivitySource(HomePermissionTrustService permissions,
    IAuthenticatedResourceActorSource actors, AuthenticatedResourceActor originalActor)
    : IHomeActivitySource, IHomeActivityAuthorization
{
    public string SourceId => "home.permissions";

    private async Task RequireActorAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != originalActor)
            throw new HomeFeatureProviderException("HomeProfileChanged", "Reopen Home to view activity for the current profile.", false);
        token.ThrowIfCancellationRequested();
    }

    public async Task<IReadOnlyList<HomeActivityEvent>> ReadAsync(HomeEventsQuery query, CancellationToken cancellationToken)
    {
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        if (query.SourceApp is not null && query.SourceApp != SourceId) return [];
        var result = new List<HomeActivityEvent>();
        var offset = 0;
        do
        {
            var snapshot = await permissions.GetSnapshotAsync(offset, 500, cancellationToken).ConfigureAwait(false);
            foreach (var entry in snapshot.RecentAuditEvents)
            {
                if (query.From is { } from && entry.Timestamp < from ||
                    query.To is { } to && entry.Timestamp > to) continue;
                var requestId = entry.RequestId ??
                    (entry.Kind == HomePermissionAuditKind.TrustRevoked ? entry.RelatedRequestId : null);
                if (requestId is null) continue;
                var request = await permissions.ReadRequestObservationAsync(requestId, cancellationToken).ConfigureAwait(false);
                if (request is null || request.Impact.ResourceBinding?.OriginalActor != originalActor)
                    continue;
                if (entry.RequestId is null)
                {
                    var grant = snapshot.Grants.SingleOrDefault(item => item.GrantId == entry.RelatedGrantId);
                    if (!IsGrantRevocationBinding(entry, request, grant))
                        continue;
                }
                else if (entry.CallerId != request.Caller.CallerId)
                    continue;
                var severity = entry.RequestState is HomePermissionRequestState.Failed or HomePermissionRequestState.PartiallyCompleted
                    ? "error" : entry.RequestState is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked ? "warning" : "info";
                result.Add(new HomeActivityEvent(entry.AuditId, entry.Timestamp, SourceId, entry.Kind.ToString(),
                    entry.CallerId, request.Caller.DisplayName,
                    [new HomeDeepLink(HomeRoute.Permissions, SourceId, requestId, TargetRouteId: HomeFeatureRouteIds.Permissions)],
                    severity, "permission", $"{request.Caller.DisplayName}: {request.Scope.ActionName} · {entry.Kind}", requestId));
            }
            if (snapshot.ContinuationToken is null) break;
            if (!int.TryParse(snapshot.ContinuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var next) || next <= offset)
                throw new HomeFeatureProviderException("ActivityPaginationInvalid", "Permission activity could not be read completely.");
            offset = next;
        } while (true);
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<HomeActivityEvent?> GetByIdAsync(string eventId, CancellationToken cancellationToken) =>
        (await ReadAsync(new HomeEventsQuery(), cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.EventId == eventId);

    public async Task<bool> CanReadAsync(HomeActivityEvent activity, CancellationToken cancellationToken)
    {
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        if (activity.SourceApp != SourceId || activity.RequestId is not { } requestId) return false;
        var request = await permissions.ReadRequestObservationAsync(requestId, cancellationToken).ConfigureAwait(false);
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        if (request?.Impact.ResourceBinding?.OriginalActor != originalActor ||
            request.Caller.CallerId != activity.ActorIdentity) return false;
        if (activity.EventType != HomePermissionAuditKind.TrustRevoked.ToString()) return true;

        var offset = 0;
        do
        {
            var snapshot = await permissions.GetSnapshotAsync(offset, 500, cancellationToken).ConfigureAwait(false);
            var entry = snapshot.RecentAuditEvents.SingleOrDefault(item => item.AuditId == activity.EventId);
            if (entry is not null)
            {
                var grant = snapshot.Grants.SingleOrDefault(item => item.GrantId == entry.RelatedGrantId);
                await RequireActorAsync(cancellationToken).ConfigureAwait(false);
                return IsGrantRevocationBinding(entry, request, grant) &&
                    entry.RelatedRequestId == requestId && entry.CallerId == activity.ActorIdentity;
            }
            if (snapshot.ContinuationToken is null) return false;
            if (!int.TryParse(snapshot.ContinuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var next) || next <= offset)
                throw new HomeFeatureProviderException("ActivityPaginationInvalid", "Permission activity could not be authorized completely.");
            offset = next;
        } while (true);
    }

    private static bool IsGrantRevocationBinding(HomePermissionAuditEvent entry, HomePermissionRequest request,
        HomePermissionGrant? grant) =>
        entry.Kind == HomePermissionAuditKind.TrustRevoked && entry.RequestId is null &&
        !string.IsNullOrWhiteSpace(entry.RelatedRequestId) && !string.IsNullOrWhiteSpace(entry.RelatedGrantId) &&
        grant is { IsRevoked: true } && grant.GrantId == entry.RelatedGrantId &&
        grant.OriginatingRequestId == entry.RelatedRequestId && request.RequestId == entry.RelatedRequestId &&
        request.AppliedGrantId == grant.GrantId && request.AppliedTrustLevel == grant.TrustLevel &&
        SamePermissionCallerIdentity(request.Caller, grant.Caller) && entry.CallerId == grant.Caller.CallerId &&
        entry.TrustLevel == grant.TrustLevel && entry.Scope is not null && ScopeEquals(entry.Scope, grant.Scope) &&
        HomePermissionTrustService.IsNarrowerOrEqual(request.Scope, grant.Scope);

    private static bool SamePermissionCallerIdentity(HomePermissionCallerIdentity left, HomePermissionCallerIdentity right) =>
        StringComparer.Ordinal.Equals(left.CallerId, right.CallerId) &&
        StringComparer.Ordinal.Equals(left.IdentityVersion, right.IdentityVersion) &&
        StringComparer.Ordinal.Equals(left.Origin, right.Origin) && left.IsVerified == right.IsVerified;

    private static bool ScopeEquals(HomePermissionScope left, HomePermissionScope right) =>
        left.TargetAppId == right.TargetAppId && left.ActionName == right.ActionName &&
        left.IncludesAllObjects == right.IncludesAllObjects && left.Objects.Count == right.Objects.Count &&
        left.Objects.All(right.Objects.Contains);
}
