using System.Text.Json;
using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Application;

/// <summary>Revision-checked semantic editing over the canonical Sites project; UI and automation share this service.</summary>
public sealed class SiteAuthoringService(SiteProjectService projects)
{
    public Task<SiteApiResult<SiteProject>> CreatePageAsync(Guid siteID, long revision, string name, string path, CancellationToken ct = default)
        => projects.UpdateProjectAsync(siteID, revision, project =>
        {
            if (string.IsNullOrWhiteSpace(name)) throw Invalid("Page name is required.");
            ValidateRoute(path);
            if (project.Routes.Any(r => r.Pattern == path)) throw Invalid("Route already exists.");
            var page = new SitePage(Guid.NewGuid(), name, null, [], name, null, 1);
            var route = new SiteRoute(Guid.NewGuid(), page.PageId, path, SiteRouteKind.Static);
            return project with { Pages = project.Pages.Append(page).ToArray(), Routes = project.Routes.Append(route).ToArray() };
        }, ct);
    public Task<SiteApiResult<SiteProject>> AddComponentAsync(Guid siteID, long revision, Guid pageID, Guid? parentID,
        string type, IReadOnlyDictionary<string, JsonElement> properties, CancellationToken ct = default)
    {
        IReadOnlyDictionary<string,JsonElement> snapshot;
        try{snapshot=SnapshotProperties(properties);}
        catch(SiteOperationException error){return Task.FromResult(SiteApiResult<SiteProject>.Failure(error.Error));}
        return projects.UpdateProjectAsync(siteID, revision, project =>
        {
            var page = project.Pages.SingleOrDefault(p => p.PageId == pageID) ?? throw Invalid("Page not found.");
            var component = new SiteComponent(Guid.NewGuid(), type, snapshot, [], new Dictionary<string,IReadOnlyList<Guid>>(),
                new(SiteLayoutMode.Flow,new Dictionary<string,JsonElement>(),new Dictionary<string,SiteLayoutOverride>()),
                new Dictionary<string,string>(), new Dictionary<string,JsonElement>(),new Dictionary<string,JsonElement>(),
                new Dictionary<string,JsonElement>(),null,null,new Dictionary<string,JsonElement>(),1);
            var components = project.Components.Append(component).ToArray();
            if (parentID is { } parent)
            {
                if (!Reachable(project, page.RootComponentIds).Contains(parent)) throw Invalid("Parent does not belong to page.");
                components = components.Select(c => c.ComponentId == parent
                    ? c with { ChildIds = c.ChildIds.Append(component.ComponentId).ToArray(), Revision = c.Revision + 1 } : c).ToArray();
            }
            else page = page with { RootComponentIds = page.RootComponentIds.Append(component.ComponentId).ToArray(), Revision = page.Revision + 1 };
            return project with { Components = components, Pages = project.Pages.Select(p => p.PageId == pageID ? page : p).ToArray() };
        }, ct);
    }
    public Task<SiteApiResult<SiteProject>> UpdateComponentAsync(Guid siteID, long revision, Guid componentID,
        Func<SiteComponent,SiteComponent> update, CancellationToken ct = default)
        => projects.UpdateProjectAsync(siteID,revision, project =>
        {
            if (!project.Components.Any(c => c.ComponentId == componentID)) throw Invalid("Component not found.");
            return project with { Components = project.Components.Select(c =>
            {
                if (c.ComponentId != componentID) return c;
                var next = update(c);
                if (next.ComponentId != c.ComponentId || !next.ChildIds.SequenceEqual(c.ChildIds) || !SameSlots(next.Slots, c.Slots))
                    throw Invalid("Use structural actions to change hierarchy.");
                return next with { Revision = c.Revision + 1 };
            }).ToArray() };
        },ct);
    public Task<SiteApiResult<SiteProject>> MoveComponentAsync(Guid siteID,long revision,Guid componentID,Guid pageID,Guid? parentID,int position,CancellationToken ct=default)
        => projects.UpdateProjectAsync(siteID,revision, project =>
        {
            if (!project.Components.Any(c=>c.ComponentId==componentID) || !project.Pages.Any(p=>p.PageId==pageID)) throw Invalid("Target not found.");
            if (parentID is { } parent && (parent==componentID || Reachable(project,[componentID]).Contains(parent))) throw Invalid("Component cycle blocked.");
            if (parentID is { } destination && !Reachable(project,project.Pages.Single(p=>p.PageId==pageID).RootComponentIds).Contains(destination)) throw Invalid("Destination is not in page.");
            IReadOnlyList<Guid> Insert(IReadOnlyList<Guid> ids) { var list=ids.Where(id=>id!=componentID).ToList(); if(position<0||position>list.Count)throw Invalid("Invalid insertion position."); list.Insert(position,componentID);return list; }
            return project with {
                Pages=project.Pages.Select(p=>
                {
                    var roots = p.PageId==pageID && parentID is null ? Insert(p.RootComponentIds) : p.RootComponentIds.Where(id=>id!=componentID).ToArray();
                    return roots.SequenceEqual(p.RootComponentIds) ? p : p with { RootComponentIds=roots, Revision=p.Revision+1 };
                }).ToArray(),
                Components=project.Components.Select(c=>
                {
                    var children=c.ComponentId==parentID ? Insert(c.ChildIds) : c.ChildIds.Where(id=>id!=componentID).ToArray();
                    var slots=c.Slots.ToDictionary(slot=>slot.Key,slot=>(IReadOnlyList<Guid>)slot.Value.Where(id=>id!=componentID).ToArray(),StringComparer.Ordinal);
                    return children.SequenceEqual(c.ChildIds) && SameSlots(slots,c.Slots) ? c
                        : c with { ChildIds=children, Slots=slots, Revision=c.Revision+1 };
                }).ToArray() };
        },ct);
    public Task<SiteApiResult<SiteProject>> SetResponsiveOverrideAsync(Guid siteID,long revision,Guid componentID,string breakpoint,
        IReadOnlyDictionary<string,JsonElement>? patch,CancellationToken ct=default)
    {
        IReadOnlyDictionary<string,JsonElement>? snapshot;
        try{snapshot=patch is null?null:SnapshotProperties(patch);}
        catch(SiteOperationException error){return Task.FromResult(SiteApiResult<SiteProject>.Failure(error.Error));}
        return UpdateComponentAsync(siteID,revision,componentID, component=>
        {
            var overrides=component.Layout.BreakpointOverrides.ToDictionary(p=>p.Key,p=>p.Value);
            if(snapshot is null) overrides.Remove(breakpoint); else overrides[breakpoint]=new(snapshot);
            return component with { Layout=component.Layout with { BreakpointOverrides=overrides } };
        },ct);
    }
    internal static HashSet<Guid> Reachable(SiteProject project,IEnumerable<Guid> roots)
    {
        var found=new HashSet<Guid>();var queue=new Queue<Guid>(roots);var nodes=project.Components.ToDictionary(c=>c.ComponentId);
        while(queue.TryDequeue(out var id))
        {
            if(!found.Add(id)) continue;
            var component=nodes.GetValueOrDefault(id)??throw Invalid("Missing component reference.");
            foreach(var child in component.ChildIds.Concat(component.Slots.Values.SelectMany(v=>v)))queue.Enqueue(child);
        }
        return found;
    }
    private static bool SameSlots(IReadOnlyDictionary<string,IReadOnlyList<Guid>>? left, IReadOnlyDictionary<string,IReadOnlyList<Guid>> right)
        => left is not null && left.Count==right.Count && right.All(slot=>left.TryGetValue(slot.Key,out var values) && values is not null && values.SequenceEqual(slot.Value));
    private static IReadOnlyDictionary<string,JsonElement> SnapshotProperties(IReadOnlyDictionary<string,JsonElement>? input)
    {
        if(input is null)throw Invalid("Component properties are required.");
        try
        {
            var snapshot=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
            foreach(var property in input)
            {
                if(string.IsNullOrWhiteSpace(property.Key)||property.Value.ValueKind==JsonValueKind.Undefined)throw Invalid("Component properties must have defined values and names.");
                snapshot.Add(property.Key,property.Value.Clone());
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string,JsonElement>(snapshot);
        }
        catch(ObjectDisposedException){throw Invalid("Component property data is no longer available.");}
    }
    private static void ValidateRoute(string route) => SiteAddressRules.NormalizeRoutePath(route);
    private static SiteOperationException Invalid(string message)=>new(new("InvalidInput",message,"Sites.Authoring",false));
}
