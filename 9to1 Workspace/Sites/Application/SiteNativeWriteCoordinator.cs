using System.Globalization;
using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Sites.Application;

/// <summary>Captured typed edit; no caller field supplies actor identity or grants authority.</summary>
public sealed class SiteNativeWriteIntent
{
    public const string TargetAppId = "sites";
    private readonly JsonElement _arguments;
    private readonly JsonElement _payload;
    private SiteNativeWriteIntent(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, string operation, JsonElement payload)
    {
        if (binding.FilesFolderId == Guid.Empty || string.IsNullOrWhiteSpace(binding.FolderRevision)) throw new ArgumentException("A current Files folder is required.");
        if (operation != "create" && (siteID == Guid.Empty || revision < 1)) throw new ArgumentException("The current Sites identity and revision are required.");
        Binding = binding; SiteID = siteID; Revision = revision; Operation = operation; _payload = payload.Clone();
        ActionId = operation == "create" ? "sites.project.create" : "sites.project.save";
        var scopes = new List<ResourceScope> { new("files.item", binding.FilesFolderId.ToString(), binding.FolderRevision, ResourceAccess.Write) };
        if (operation != "create") scopes.Add(new("sites.project", siteID.ToString(), revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write));
        Scopes = scopes.AsReadOnly();
        _arguments = JsonSerializer.SerializeToElement(new { operation, siteID, revision, filesFolderID = binding.FilesFolderId, folderRevision = binding.FolderRevision, payload = _payload });
    }
    internal SiteNativeWorkspaceBinding Binding { get; }
    internal JsonElement Payload => _payload;
    public Guid SiteID { get; }
    public long Revision { get; }
    public string Operation { get; }
    public string ActionId { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    public static SiteNativeWriteIntent Create(SiteNativeWorkspaceBinding binding, string name, string frameworkID, string relativePath)
        => new(binding, Guid.Empty, 0, "create", JsonSerializer.SerializeToElement(new { name, frameworkID, relativePath }));
    public static SiteNativeWriteIntent Rename(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, string name)
        => new(binding, siteID, revision, "rename", JsonSerializer.SerializeToElement(new { name }));
    public static SiteNativeWriteIntent CreatePage(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, string name, string path)
        => new(binding, siteID, revision, "page.create", JsonSerializer.SerializeToElement(new { name, path }));
    public static SiteNativeWriteIntent AddComponent(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, Guid pageID, Guid? parentID, string type, IReadOnlyDictionary<string, JsonElement> properties)
        => new(binding, siteID, revision, "component.add", JsonSerializer.SerializeToElement(new { pageID, parentID, type, properties }));
    public static SiteNativeWriteIntent UpdateProperties(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, Guid componentID, IReadOnlyDictionary<string, JsonElement> properties)
        => new(binding, siteID, revision, "component.properties", JsonSerializer.SerializeToElement(new { componentID, properties }));
    public static SiteNativeWriteIntent MoveComponent(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, Guid componentID, Guid pageID, Guid? parentID, int position)
        => new(binding, siteID, revision, "component.move", JsonSerializer.SerializeToElement(new { componentID, pageID, parentID, position }));
    public static SiteNativeWriteIntent SetResponsiveOverride(SiteNativeWorkspaceBinding binding, Guid siteID, long revision, Guid componentID, string breakpoint, IReadOnlyDictionary<string, JsonElement>? properties)
        => new(binding, siteID, revision, "component.responsive", JsonSerializer.SerializeToElement(new { componentID, breakpoint, properties }));
}

/// <summary>Home approval is consumed by the owning Sites write, followed by its atomic revision transaction.
/// The canonical Files/profile binding is rechecked inside the store lock before the commit.</summary>
public sealed class SiteNativeWriteCoordinator(ISiteNativeWorkspaceAuthority workspace, HomeResourceOperationBroker home)
{
    public async Task<SiteApiResult<SiteProject>> ExecuteAsync(SiteNativeWriteIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        async Task CheckBinding(CancellationToken ct)
        {
            if (await workspace.GetCurrentAsync(ct).ConfigureAwait(false) != intent.Binding)
                throw new UnauthorizedAccessException("The canonical Sites Files/profile binding changed.");
        }
        await CheckBinding(cancellationToken).ConfigureAwait(false);
        var actor = await home.ClaimExecutionAsync(capability, SiteNativeWriteIntent.TargetAppId, intent.ActionId, intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null || actor.ActorId != intent.Binding.ActorId ||
            actor.ProfileId != intent.Binding.ProfileId || actor.AuthenticationRevision != intent.Binding.AuthenticationRevision)
            throw new UnauthorizedAccessException("Home did not authorise this exact current Sites write.");
        var projects = new SiteProjectService(new FileSiteWorkspaceStore(intent.Binding.RootDirectory, CheckBinding));
        var authoring = new SiteAuthoringService(projects);
        var payload = intent.Payload;
        string Text(string name) => payload.GetProperty(name).GetString() ?? "";
        return intent.Operation switch
        {
            "create" => await projects.CreateProjectAsync(new(intent.Binding.FilesFolderId, null, null, intent.Binding.FolderRevision,
                Text("name"), Text("frameworkID"), Text("relativePath")), cancellationToken).ConfigureAwait(false),
            "rename" => await projects.UpdateProjectAsync(intent.SiteID, intent.Revision, project =>
                string.IsNullOrWhiteSpace(Text("name")) || Text("name").Trim().Length > 120
                    ? throw new SiteOperationException(new("InvalidInput", "Project name must have 1–120 characters.", "name", false))
                    : project with { Name = Text("name").Trim() }, cancellationToken).ConfigureAwait(false),
            "page.create" => await authoring.CreatePageAsync(intent.SiteID, intent.Revision, Text("name"), Text("path"), cancellationToken).ConfigureAwait(false),
            "component.add" => await authoring.AddComponentAsync(intent.SiteID, intent.Revision, payload.GetProperty("pageID").GetGuid(),
                payload.GetProperty("parentID").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("parentID").GetGuid(), Text("type"),
                payload.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()), cancellationToken).ConfigureAwait(false),
            "component.properties" => await authoring.UpdateComponentAsync(intent.SiteID, intent.Revision, payload.GetProperty("componentID").GetGuid(),
                component => component with { Properties = payload.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) }, cancellationToken).ConfigureAwait(false),
            "component.move" => await authoring.MoveComponentAsync(intent.SiteID, intent.Revision, payload.GetProperty("componentID").GetGuid(), payload.GetProperty("pageID").GetGuid(),
                payload.GetProperty("parentID").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("parentID").GetGuid(), payload.GetProperty("position").GetInt32(), cancellationToken).ConfigureAwait(false),
            "component.responsive" => await authoring.SetResponsiveOverrideAsync(intent.SiteID, intent.Revision, payload.GetProperty("componentID").GetGuid(), Text("breakpoint"),
                payload.GetProperty("properties").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()), cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Unknown captured Sites operation.")
        };
    }
}
