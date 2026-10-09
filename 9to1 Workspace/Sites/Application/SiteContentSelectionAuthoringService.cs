using System.Text.Json;
using HavenOS.Apps.Sites.Domain;
namespace HavenOS.Apps.Sites.Application;

public sealed record SiteContentSelectionChoice(Guid ButtonID,string Value,IReadOnlyList<Guid> ContentIDs);

/// <summary>Canonical bounded content selection: local presentation only, no external request or product-data mutation.</summary>
public sealed class SiteContentSelectionAuthoringService(SiteProjectService projects)
{
    public Task<SiteApiResult<SiteProject>> SetGroupAsync(Guid siteID,long expectedRevision,Guid pageID,string groupID,
        IReadOnlyList<SiteContentSelectionChoice> choices,string defaultValue,CancellationToken ct=default)
    {
        if(choices is null)return Task.FromResult(SiteApiResult<SiteProject>.Failure(new("InvalidInput","Content choices are required.","Sites.ContentSelection",false)));
        IReadOnlyList<SiteContentSelectionChoice> choiceSnapshot;
        try
        {
            choiceSnapshot=Array.AsReadOnly(choices.Take(101).Select(choice=>choice is null||choice.ContentIDs is null
                ? throw new ArgumentException("Content choices/targets are required.")
                : new SiteContentSelectionChoice(choice.ButtonID,choice.Value,Array.AsReadOnly(choice.ContentIDs.Take(1001).ToArray()))).ToArray());
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {return Task.FromResult(SiteApiResult<SiteProject>.Failure(new("InvalidInput","Content choices could not be captured consistently.","Sites.ContentSelection",false)));}
        return projects.UpdateProjectAsync(siteID,expectedRevision,project=>
        {
            bool Identifier(string? text)=>text is {Length:>0 and <=128} && text.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
            if(!Identifier(groupID)||choiceSnapshot.Count is <1 or >100||choiceSnapshot.Any(c=>!Identifier(c.Value)||c.ContentIDs.Count is <1 or >1000)||
                choiceSnapshot.Select(c=>c.ButtonID).Distinct().Count()!=choiceSnapshot.Count||choiceSnapshot.Select(c=>c.Value).Distinct(StringComparer.Ordinal).Count()!=choiceSnapshot.Count||!choiceSnapshot.Any(c=>c.Value==defaultValue))throw Invalid("Invalid content selection choices/default.");
            var page=project.Pages.SingleOrDefault(p=>p.PageId==pageID)??throw Invalid("Page not found.");
            var reachable=SiteAuthoringService.Reachable(project,page.RootComponentIds);
            var content=choiceSnapshot.SelectMany(c=>c.ContentIDs).ToArray();
            if(content.Distinct().Count()!=content.Length||content.Any(id=>!reachable.Contains(id))||choiceSnapshot.Any(c=>!reachable.Contains(c.ButtonID)||project.Components.Single(n=>n.ComponentId==c.ButtonID).ComponentType!="button")||content.Intersect(choiceSnapshot.Select(c=>c.ButtonID)).Any())throw Invalid("Selection references must be distinct canonical components on this page.");
            foreach(var id in content)
            {
                var node=project.Components.Single(c=>c.ComponentId==id);
                if(node.Properties.TryGetValue("contentGroup",out var existing)&&(existing.ValueKind!=JsonValueKind.String||existing.GetString()!=groupID))throw Invalid("Content already belongs to another selection group.");
            }
            return project with {Components=project.Components.Select(node=>
            {
                if(!reachable.Contains(node.ComponentId))return node;
                var properties=node.Properties.ToDictionary(p=>p.Key,p=>p.Value);var interactions=node.Interactions.ToDictionary(p=>p.Key,p=>p.Value);
                bool changed=false;
                if(properties.TryGetValue("contentGroup",out var prior)&&prior.ValueKind==JsonValueKind.String&&prior.GetString()==groupID){properties.Remove("contentGroup");properties.Remove("contentValue");changed=true;}
                if(interactions.TryGetValue("select-content-group",out var interaction)&&interaction.ValueKind==JsonValueKind.Object&&interaction.TryGetProperty("groupID",out var priorGroup)&&priorGroup.ValueKind==JsonValueKind.String&&priorGroup.GetString()==groupID){interactions.Remove("select-content-group");properties.Remove("selectedByDefault");changed=true;}
                var button=choiceSnapshot.SingleOrDefault(c=>c.ButtonID==node.ComponentId);
                if(button is not null){interactions["select-content-group"]=JsonSerializer.SerializeToElement(new{groupID,value=button.Value});properties["selectedByDefault"]=JsonSerializer.SerializeToElement(button.Value==defaultValue);changed=true;}
                var target=choiceSnapshot.SingleOrDefault(c=>c.ContentIDs.Contains(node.ComponentId));
                if(target is not null){properties["contentGroup"]=JsonSerializer.SerializeToElement(groupID);properties["contentValue"]=JsonSerializer.SerializeToElement(target.Value);changed=true;}
                return changed?node with{Properties=properties,Interactions=interactions,Revision=node.Revision+1}:node;
            }).ToArray()};
        },ct);
    }
    private static SiteOperationException Invalid(string message)=>new(new("InvalidInput",message,"Sites.ContentSelection",false));
}
