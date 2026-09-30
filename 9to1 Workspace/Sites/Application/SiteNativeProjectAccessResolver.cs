using System.Globalization;
using Haven.Application;
using HavenOS.Apps.Sites.Infrastructure;

namespace HavenOS.Apps.Sites.Application;

/// <summary>Owning native Sites projection over the current explicit Files binding and actual project revision.</summary>
public sealed class SiteNativeProjectAccessResolver(ISiteNativeWorkspaceAuthority workspace) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "sites.project";

    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Denied(string code) => new(false, code, actor.ActorId, scope.Revision, actor.OrganisationId);
        if (scope.Kind != ResourceKind || !Guid.TryParse(scope.Id, out var siteID) || siteID == Guid.Empty ||
            !long.TryParse(scope.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            return Denied("sites_project_scope_invalid");
        if (!((actionId == "sites.project.read" && scope.Access == ResourceAccess.Read) ||
              (actionId == "sites.project.save" && scope.Access == ResourceAccess.Write)))
            return Denied("sites_project_action_unknown");
        // This native port carries a local Home profile only. A CAKE/organisation
        // context needs its separate owning server authority, never an inferred mapping.
        if (actor.AccountId is not null || actor.OrganisationId is not null)
            return Denied("sites_native_account_context_unbound");
        var binding = await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (binding is null || binding.ProfileId != actor.ProfileId || binding.ActorId != actor.ActorId ||
            binding.AuthenticationRevision != actor.AuthenticationRevision || binding.FilesFolderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(binding.FolderRevision) || !Directory.Exists(binding.RootDirectory))
            return Denied("sites_workspace_unavailable");
        var projects = new SiteProjectService(new FileSiteWorkspaceStore(binding.RootDirectory));
        var result = await projects.GetProjectAsync(siteID, cancellationToken).ConfigureAwait(false);
        if (result.Error is not null || result.Value is not { } project || project.SiteId != siteID ||
            project.Revision != revision || project.Source.FilesDirectoryId != binding.FilesFolderId)
            return Denied("sites_project_revision_or_owner_mismatch");
        if (await workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != binding)
            return Denied("sites_workspace_changed");
        return new(true, "sites_project_current", actor.ActorId, scope.Revision, null);
    }
}
