using System.Text.Json;
using HavenOS.Files;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Infrastructure;

namespace NineToOne.Web;

public sealed class SitesWebDomain(FilesWorkspaceDirectoryResolver files) : IWorkspaceWebDomain
{
    public string Name=>"Sites";
    public IReadOnlySet<string> Actions {get;}=new HashSet<string>{"ListProjects","OpenProject","CreateProject","CreatePage","AddComponent","BuildArtifact"};
    public async Task<JsonElement> InvokeAsync(string action,JsonElement arguments,Guid accountID,CancellationToken ct)
    {
        var binding=await files.ResolveAsync(accountID,"sites",ct);
        if(!binding.IsSuccess)throw new KeyNotFoundException("FilesWorkspaceUnavailable");
        var store=new FileSiteWorkspaceStore(binding.Value!.DirectoryPath);var projects=new SiteProjectService(store);var authoring=new SiteAuthoringService(projects);
        object result;
        switch(action)
        {
            case "ListProjects": result=await store.ReadAsync(state=>state.Projects,ct);break;
            case "OpenProject":result=await projects.GetProjectAsync(arguments.GetProperty("siteID").GetGuid(),ct);break;
            case "CreateProject":
                var request=arguments.Deserialize<CreateSiteProjectRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new ArgumentException("invalid_project");
                if(request.FilesDirectoryId!=binding.Value.FolderId.Value)throw new UnauthorizedAccessException("unbound_source_folder");
                // Remote Stack domains require their own authorisation adapter; they cannot be self-asserted here.
                if(request.StackProjectId is not null||request.StackDomainId is not null)throw new KeyNotFoundException("StackBindingUnavailable");
                result=await projects.CreateProjectAsync(request,ct);break;
            case "CreatePage":result=await authoring.CreatePageAsync(arguments.GetProperty("siteID").GetGuid(),arguments.GetProperty("expectedRevision").GetInt64(),arguments.GetProperty("name").GetString()!,arguments.GetProperty("path").GetString()!,ct);break;
            case "BuildArtifact":result=await new SiteArtifactAuthoringService(projects,Path.Combine(binding.Value.DirectoryPath,".9to1-site-builds")).BuildAsync(arguments.GetProperty("siteID").GetGuid(),arguments.GetProperty("expectedRevision").GetInt64(),arguments.GetProperty("configurationRevision").GetString()!,ct);break;
            case "AddComponent":
                var properties=arguments.GetProperty("properties").Deserialize<Dictionary<string,JsonElement>>()??throw new ArgumentException("invalid_properties");
                result=await authoring.AddComponentAsync(arguments.GetProperty("siteID").GetGuid(),arguments.GetProperty("expectedRevision").GetInt64(),arguments.GetProperty("pageID").GetGuid(),arguments.TryGetProperty("parentID",out var parent)&&parent.ValueKind!=JsonValueKind.Null?parent.GetGuid():null,arguments.GetProperty("type").GetString()!,properties,ct);break;
            default:throw new KeyNotFoundException("capability_unavailable");
        }
        return JsonSerializer.SerializeToElement(result);
    }
}
public sealed class FilesWebDomain(Func<Guid,IFilesProvider?> providers) : IWorkspaceWebDomain
{
    public string Name=>"Files";
    public IReadOnlySet<string> Actions {get;}=new HashSet<string>{"List","Get"};
    public async Task<JsonElement> InvokeAsync(string action,JsonElement arguments,Guid accountID,CancellationToken ct)
    {
        var provider=providers(accountID)??throw new UnauthorizedAccessException("Files account location is not configured.");
        object result=action switch {
            "Get"=>await provider.GetAsync(new(arguments.GetProperty("itemID").GetGuid()),ct),
            "List"=>await provider.ListAsync(arguments.TryGetProperty("parentID",out var parent)&&parent.ValueKind!=JsonValueKind.Null?new HostedItemId(parent.GetGuid()):null,null,arguments.TryGetProperty("cursor",out var cursor)?cursor.GetString():null,ct),
            _=>throw new KeyNotFoundException("capability_unavailable")};
        return JsonSerializer.SerializeToElement(result);
    }
}
public sealed record FilesAccountLocation(Guid AccountID,Guid LocationID,string StatePath);
