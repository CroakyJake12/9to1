using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Stacks.NativeUI;

/// <summary>Trusted Files/Home owner mapping. A binding is neither an action grant nor a caller-selected filesystem path.</summary>
public sealed record StackNativeWorkspaceBinding(string ProfileId, string ActorId, string AuthenticationRevision,
    Guid FilesFolderId, string FolderRevision, Guid ProjectId, string ProjectDirectory);
public interface IStackNativeWorkspaceAuthority
{
    Task<StackNativeWorkspaceBinding?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken = default);
}
public sealed class StackNativeActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "stacks" && actionId is "stacks.domain.create" or "stacks.domain.set-active" or "stacks.source.commit"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
}

/// <summary>Exact canonical project/domain revision checks over a revalidated host-owned Files mapping.</summary>
public sealed class StackNativeResourceAccessResolver(IStackNativeWorkspaceAuthority workspace, bool domainScope) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => domainScope ? "stacks.domain" : "stacks.project";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken ct)
    {
        ResourceAccessDecision Deny(string code) => new(false, code, actor.ActorId, scope.Revision, actor.OrganisationId);
        if (scope.Kind != ResourceKind || scope.Access != ResourceAccess.Write ||
            actionId is not ("stacks.domain.create" or "stacks.domain.set-active" or "stacks.source.commit") ||
            !Guid.TryParseExact(scope.Id, "D", out var resourceId) || resourceId == Guid.Empty ||
            actor.AccountId is not null || actor.OrganisationId is not null)
            return Deny("StackScopeInvalid");
        Guid projectId = resourceId, expectedHead = Guid.Empty;
        string token = scope.Revision;
        if (domainScope)
        {
            var parts = token.Split(':');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out projectId) ||
                !Guid.TryParseExact(parts[1], "D", out expectedHead)) return Deny("StackDomainRevisionInvalid");
            token = parts[2];
        }
        if (token.Length != 64 || token.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return Deny("StackRevisionInvalid");
        var binding = await workspace.GetCurrentAsync(projectId, ct).ConfigureAwait(false);
        if (binding is null || binding.ProjectId != projectId || binding.ActorId != actor.ActorId ||
            binding.ProfileId != actor.ProfileId || binding.AuthenticationRevision != actor.AuthenticationRevision ||
            binding.FilesFolderId == Guid.Empty || string.IsNullOrWhiteSpace(binding.FolderRevision))
            return Deny("StackWorkspaceUnavailable");
        var store = new JsonFileStackProjectStore(binding.ProjectDirectory);
        var manifest = await store.LoadAsync(ct).ConfigureAwait(false);
        if (manifest.ProjectId != projectId || store.LoadedRevisionToken != token) return Deny("StackProjectRevisionChanged");
        if (domainScope && !manifest.Domains.Any(domain => domain.Id == resourceId && !domain.IsDeleted &&
            (domain.HeadRevisionId ?? domain.BaseRevisionId) == expectedHead)) return Deny("StackDomainRevisionChanged");
        if (await workspace.GetCurrentAsync(projectId, ct).ConfigureAwait(false) != binding) return Deny("StackWorkspaceChanged");
        return new(true, "StackCurrentCanonicalRevision", actor.ActorId, scope.Revision, null);
    }
}
