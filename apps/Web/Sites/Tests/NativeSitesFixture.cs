using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Web.Sites;

/// <summary>Local test composition of the actual OS-profile/Home/Files/Sites owners.
/// Only this test driver explicitly accepts each reviewed native write; production supplies its own approval UI.</summary>
sealed class NativeSitesFixture
{
    public NativeFilesWorkspaceAuthority Files { get; private set; } = null!;
    public SitesNativeWorkspaceAuthority Sites { get; private set; } = null!;
    public HomeResourceOperationBroker Broker { get; private set; } = null!;
    public HomePermissionTrustService Permissions { get; private set; } = null!;
    public SiteNativeWriteCoordinator Writer { get; private set; } = null!;
    public ResourceAuthorizationService Resources { get; private set; } = null!;
    public string Root { get; private set; } = "";
    public bool LoseNextMutationReply { get; set; }
    public bool ForeignNextMutationReceipt { get; set; }
    public SitesBrowserOperations Operations => new(ListAsync, OpenAsync,
        async (name, ct) => await ExecuteAsync(binding => SiteNativeWriteIntent.Create(binding, name, "9to1-native", "browser-site"), null, ct),
        async (id, revision, name, path, ct) => await ExecuteAsync(binding => SiteNativeWriteIntent.CreatePage(binding, id, revision, name, path), (id, revision), ct),
        async (id, revision, page, text, ct) => await ExecuteAsync(binding => SiteNativeWriteIntent.AddComponent(binding, id, revision, page, null, "paragraph",
            new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement(text) }), (id, revision), ct),
        async (id, revision, component, text, ct) =>
        {
            var found = await OpenAsync(id, ct);
            if (!found.IsSuccess) return found;
            var node = found.Value!.Components.Single(candidate => candidate.ComponentId == component);
            var properties = node.Properties.ToDictionary(property => property.Key, property => property.Value.Clone());
            properties["text"] = JsonSerializer.SerializeToElement(text);
            return await ExecuteAsync(binding => SiteNativeWriteIntent.UpdateProperties(binding, id, revision, component, properties), (id, revision), ct);
        });

    public static async Task<NativeSitesFixture> CreateAsync(string root, bool initialize = true)
    {
        Directory.CreateDirectory(root);
        var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        _ = await profiles.GetCurrentAsync(default) ?? throw new UnauthorizedAccessException("Actual OS profile unavailable.");
        var service = new NativeFilesWorkspaceService(home, profiles);
        var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([service]), new HomePermissionTrustService(home, (_, _) => null));
        if (initialize)
        {
            var selected = Path.Combine(root, "explicitly-selected-empty-files-folder"); Directory.CreateDirectory(selected);
            await service.ConfigureNewAsync(selected, ownership, default);
        }
        var files = new NativeFilesWorkspaceAuthority(service, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
        var sites = new SitesNativeWorkspaceAuthority(files);
        var resources = new ResourceAuthorizationService(profiles, [new FilesArtifactResourceResolver(files), new SiteNativeProjectAccessResolver(sites)]);
        var permissions = new HomePermissionTrustService(home, new SiteNativeActionPolicies().TryGet);
        var broker = new HomeResourceOperationBroker(resources, permissions);
        return new() { Root = root, Files = files, Sites = sites, Resources = resources, Permissions = permissions, Broker = broker, Writer = new(sites, broker) };
    }

    public async Task<SiteNativeWorkspaceBinding> BindingAsync(CancellationToken ct = default)
        => await Sites.GetCurrentAsync(ct) ?? throw new UnauthorizedAccessException("Actual current Sites Files binding unavailable.");
    public async Task<SiteProjectService> ProjectsAsync(CancellationToken ct = default)
    {
        var captured = await BindingAsync(ct);
        return new(new FileSiteWorkspaceStore(captured.RootDirectory, async token =>
        {
            if (await Sites.GetCurrentAsync(token) != captured) throw new UnauthorizedAccessException("Sites Files binding changed before actual commit.");
        }));
    }

    private async Task<SiteApiResult<IReadOnlyList<SiteProject>>> ListAsync(CancellationToken ct)
    {
        var captured = await BindingAsync(ct);
        var projects = await new FileSiteWorkspaceStore(captured.RootDirectory).ReadAsync(state => state.Projects, ct);
        var allowed = new List<SiteProject>();
        foreach (var project in projects)
            if (await Resources.AuthorizeAsync("sites.project.read", [Scope(project)], ct) is not null) allowed.Add(project);
        if (await Sites.GetCurrentAsync(ct) != captured) throw new UnauthorizedAccessException();
        return SiteApiResult<IReadOnlyList<SiteProject>>.Success(allowed);
    }
    private async Task<SiteApiResult<SiteProject>> OpenAsync(Guid id, CancellationToken ct)
    {
        var captured = await BindingAsync(ct);
        var found = await new SiteProjectService(new FileSiteWorkspaceStore(captured.RootDirectory)).GetProjectAsync(id, ct);
        if (!found.IsSuccess) return found;
        if (await Resources.AuthorizeAsync("sites.project.read", [Scope(found.Value!)], ct) is null || await Sites.GetCurrentAsync(ct) != captured)
            return SiteApiResult<SiteProject>.Failure(new("PermissionDenied", "Actual current owner access denied.", "siteID", false));
        return found;
    }
    private static ResourceScope Scope(SiteProject project) => new("sites.project", project.SiteId.ToString(), project.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read);

    public async Task<SiteApiResult<SiteProject>> ExecuteAsync(Func<SiteNativeWorkspaceBinding, SiteNativeWriteIntent> makeIntent,
        (Guid SiteID, long Revision)? expected, CancellationToken ct = default)
    {
        var binding = await BindingAsync(ct);
        if (expected is { } expectation)
        {
            var current = await OpenAsync(expectation.SiteID, ct);
            if (!current.IsSuccess) return current;
            if (current.Value!.Revision != expectation.Revision)
                // The actual domain supplies a negative CAS result; this callback must never execute.
                return await (await ProjectsAsync(ct)).UpdateProjectAsync(expectation.SiteID, expectation.Revision,
                    _ => throw new InvalidOperationException("Stale CAS unexpectedly executed a mutation callback."), ct);
        }
        var intent = makeIntent(binding);
        var pending = await Broker.AuthorizeAsync("sites", intent.ActionId, intent.Scopes, intent.Arguments,
            "Explicit local test approval for the captured canonical Sites edit", null, binding.AuthenticationRevision, ct);
        if (pending.State != HomePermissionRequestState.PendingApproval) throw new UnauthorizedAccessException("Actual Home did not offer approval.");
        if (!(await Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded) throw new UnauthorizedAccessException();
        var capability = await Broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, ct) ?? throw new UnauthorizedAccessException();
        var result = await Writer.ExecuteAsync(intent, capability, ct);
        if (result.IsSuccess && LoseNextMutationReply) { LoseNextMutationReply = false; throw new IOException("Induced lost reply after real owner commit and Home audit."); }
        if (result.IsSuccess && ForeignNextMutationReceipt)
        { ForeignNextMutationReceipt = false; return SiteApiResult<SiteProject>.Success(result.Value! with { SiteId = Guid.NewGuid() }); }
        return result;
    }

    public async Task DeleteActualSitesFolderAsync()
    {
        var workspace = await Files.GetCurrentAsync(default) ?? throw new UnauthorizedAccessException();
        var folder = (await workspace.Provider.GetAsync(workspace.Configuration.AppFolders["sites"], default)).Value!;
        var now = DateTimeOffset.UtcNow;
        var result = await workspace.Provider.MutateAsync(new(new(Guid.NewGuid()), workspace.Actor.ActorId, folder.Id, folder.ParentId, null,
            "Delete", folder.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null), null, default);
        if (!result.IsSuccess) throw new Exception("Actual Files revocation failed: " + result.Error!.Code);
    }
}
