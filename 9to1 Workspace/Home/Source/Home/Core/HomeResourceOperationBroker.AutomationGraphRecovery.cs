using System.Text.Json;
using Haven.Application;
using Haven.Application.NodeGraph;
using HavenOS.Home.PermissionsTrustNotifications;
using OriginalHomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    internal async Task<bool> IsOriginalAutomationGraphPublicationAsync(OwnedGraphDefinition graph,
        AuthenticatedResourceActor actor, IReadOnlyList<ResourceScope> expectedScopes, string expectedArgumentsDigest,
        CancellationToken token)
    {
        var request = await ReadOriginalAutomationGraphPublicationRequestAsync(graph, actor, expectedScopes,
            expectedArgumentsDigest, token).ConfigureAwait(false);
        return request is { State: HomePermissionRequestState.Succeeded, ResultCode: "HOME_GRAPH_PUBLISHED" };
    }

    internal async Task<OriginalHomePermissionRequest?> ReadOriginalAutomationGraphPublicationRequestAsync(OwnedGraphDefinition graph,
        AuthenticatedResourceActor actor, IReadOnlyList<ResourceScope> expectedScopes, string expectedArgumentsDigest,
        CancellationToken token)
    {
        var receipt = graph.LastPublication;
        if (receipt?.OriginalRequestId is not { Length: > 0 } requestId ||
            receipt.Owner.AppId != "automations" || receipt.Owner.EntityKind != "automation.definition" ||
            receipt.Kind is not (GraphPublicationKind.SaveDraft or GraphPublicationKind.Activate) ||
            receipt.ArgumentsDigest != expectedArgumentsDigest) return null;
        var request = await permissions.ReadRequestObservationAsync(requestId, token).ConfigureAwait(false);
        return request is not null && IsOriginalAutomationGraphPublicationRequest(request, graph, actor, expectedScopes,
            expectedArgumentsDigest, requestId) ? request : null;
    }

    internal bool IsOriginalAutomationGraphPublicationInState(OwnedGraphDefinition graph,
        AuthenticatedResourceActor actor, IReadOnlyList<ResourceScope> expectedScopes, string expectedArgumentsDigest,
        HomeCoreStoredState state)
    {
        var requestId = graph.LastPublication?.OriginalRequestId;
        if (requestId is not { Length: > 0 }) return false;
        var records = state.Records.Where(record => record.RecordId == "home.permissions-trust" &&
            record.RecordType == "home.permissions-trust" && record.SchemaVersion == 1 &&
            record.Scope == HomeDataScope.DeviceLocal && record.Authority == HomeRecordAuthority.LocalCanonical).ToArray();
        if (records.Length != 1) return false;
        try
        {
            var requests = records[0].Payload.GetProperty("Requests").Deserialize<OriginalHomePermissionRequest[]>();
            var matches = requests?.Where(request => request.RequestId == requestId).ToArray();
            return matches is { Length: 1 } && matches[0].State == HomePermissionRequestState.Succeeded &&
                matches[0].ResultCode == "HOME_GRAPH_PUBLISHED" &&
                IsOriginalAutomationGraphPublicationRequest(matches[0], graph, actor, expectedScopes,
                    expectedArgumentsDigest, requestId);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }

    private bool IsOriginalAutomationGraphPublicationRequest(OriginalHomePermissionRequest request, OwnedGraphDefinition graph,
        AuthenticatedResourceActor actor, IReadOnlyList<ResourceScope> expectedScopes, string expectedArgumentsDigest,
        string requestId)
    {
        var receipt = graph.LastPublication;
        var action = receipt?.Kind == GraphPublicationKind.SaveDraft ? "automations.graph.save-draft" : "automations.activate";
        var binding = request.Impact.ResourceBinding;
        var expectedCaller = new HomePermissionCallerIdentity(actor.ActorId, actor.ActorId, actor.ProfileId,
            actor.AuthenticationRevision, true);
        var expectedPolicy = permissions.ResolveTrustedActionPolicy("automations", action);
        var expectedObjects = expectedScopes.Select(scope => new HomeObjectReference(scope.Kind, scope.Id)).ToArray();
        return receipt is not null && receipt.OriginalRequestId == requestId && request.RequestId == requestId &&
            request.Scope.TargetAppId == "automations" && request.Scope.ActionName == action &&
            !request.Scope.IncludesAllObjects && request.Scope.Objects.SequenceEqual(expectedObjects) &&
            request.Policy.RequiresPerActionApproval && request.Policy == expectedPolicy && request.Caller == expectedCaller &&
            request.Impact.ArgumentsDigest == receipt.ArgumentsDigest && request.Impact.ArgumentsDigest == expectedArgumentsDigest &&
            !request.Impact.IsUnknown && request.Impact.KnownObjects.SequenceEqual(expectedObjects) &&
            request.Impact.AffectedObjectCount == expectedObjects.Length &&
            binding is { } actual && SameAutomationGraphPrincipal(actual.OriginalActor, actor) &&
            actual.Scopes.SequenceEqual(expectedScopes);
    }

    private static bool SameAutomationGraphPrincipal(AuthenticatedResourceActor left, AuthenticatedResourceActor right) =>
        left.ProfileId == right.ProfileId && left.ActorId == right.ActorId && left.AuthenticationRevision == right.AuthenticationRevision &&
        left.AccountId == right.AccountId && left.OrganisationId == right.OrganisationId;
}
