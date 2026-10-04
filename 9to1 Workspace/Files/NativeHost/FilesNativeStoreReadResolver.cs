using Haven.Application;
using HavenOS.Home.Core;
namespace HavenOS.Files.NativeHost;

/// <summary>Actual current configured store observation, intersected with the canonical Home actor and binding.</summary>
public sealed class FilesNativeStoreReadResolver(NativeFilesWorkspaceAuthority workspaces,
    HomeLocalProfileIdentity profiles) : ICanonicalResourceAccessResolver
{
    public const string Kind = "files.native.store";
    public string ResourceKind => Kind;
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, CancellationToken token)
    {
        ResourceAccessDecision Denied() => new(false, "FilesStoreUnavailable", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (scope.Kind != Kind || scope.Access != ResourceAccess.Read ||
            actionId != HomeNativeFilesActionPolicies.ReadAction || actor.AccountId is not null ||
            actor.OrganisationId is not null || !Guid.TryParseExact(scope.Id, "D", out var store) ||
            store == Guid.Empty || await profiles.GetCurrentAsync(token).ConfigureAwait(false) != actor)
            return Denied();
        var workspace = await workspaces.GetCurrentAsync(store, token).ConfigureAwait(false);
        if (workspace?.Actor != actor) return Denied();
        var originalCheck = await workspaces.CaptureOriginalReadCheckAsync(workspace, () => !token.IsCancellationRequested, token).ConfigureAwait(false);
        var evidence = await workspace.Provider.GetStoreEvidenceAsync(store, token).ConfigureAwait(false);
        if (evidence.StoreId != store || evidence.Revision != scope.Revision ||
            !await originalCheck(token).ConfigureAwait(false)) return Denied();
        return new(true, "Allowed", actor.ActorId, scope.Revision, null);
    }
}
