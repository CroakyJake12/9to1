using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Infrastructure;
using Xunit;
namespace HavenOS.Sites.Tests;
public sealed class SiteAuthoringSnapshotTests
{
    [Fact]
    public async Task Move_detaches_slot_membership_and_preserves_unrelated_revisions()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-slots-"+Guid.NewGuid());
        try
        {
            var projects=new SiteProjectService(new FileSiteWorkspaceStore(root));
            var edits=new SiteAuthoringService(projects);
            var project=(await projects.CreateProjectAsync(new(Guid.NewGuid(),null,null,"fixture","Hierarchy","9to1-native","hierarchy"))).Value!;
            project=(await edits.CreatePageAsync(project.SiteId,project.Revision,"Home","/")).Value!;
            var page=project.Pages.Single().PageId;
            foreach(var type in new[]{"section","section","p","p"})
                project=(await edits.AddComponentAsync(project.SiteId,project.Revision,page,null,type,new Dictionary<string,JsonElement>())).Value!;
            var first=project.Components[0].ComponentId;var destination=project.Components[1].ComponentId;
            var child=project.Components[2].ComponentId;var unrelated=project.Components[3].ComponentId;
            project=(await projects.UpdateProjectAsync(project.SiteId,project.Revision,current=>current with
            {
                Pages=current.Pages.Select(p=>p with{RootComponentIds=p.RootComponentIds.Where(id=>id!=child).ToArray()}).ToArray(),
                Components=current.Components.Select(c=>c.ComponentId==first?c with
                {Slots=new Dictionary<string,IReadOnlyList<Guid>>{{"content",new[]{child}}},Revision=c.Revision+1}:c).ToArray()
            })).Value!;
            var original=project;
            var denied=await edits.UpdateComponentAsync(project.SiteId,project.Revision,first,c=>c with{Slots=new Dictionary<string,IReadOnlyList<Guid>>()});
            Assert.NotNull(denied.Error);
            Assert.Equal(original.Revision,(await projects.GetProjectAsync(project.SiteId)).Value!.Revision);
            var moved=await edits.MoveComponentAsync(project.SiteId,project.Revision,child,page,destination,0);
            Assert.Null(moved.Error);project=moved.Value!;
            Assert.Empty(project.Components.Single(c=>c.ComponentId==first).Slots["content"]);
            Assert.Equal(new[]{child},project.Components.Single(c=>c.ComponentId==destination).ChildIds);
            Assert.Equal(original.Components.Single(c=>c.ComponentId==unrelated).Revision,project.Components.Single(c=>c.ComponentId==unrelated).Revision);
            Assert.Equal(original.Components.Single(c=>c.ComponentId==child).Revision,project.Components.Single(c=>c.ComponentId==child).Revision);
            Assert.Equal(original.Pages.Single().Revision,project.Pages.Single().Revision);
            Assert.NotNull((await edits.MoveComponentAsync(project.SiteId,original.Revision,child,page,null,0)).Error);
            Assert.NotNull((await edits.MoveComponentAsync(project.SiteId,project.Revision,destination,page,child,0)).Error);
            var reopened=(await new SiteProjectService(new FileSiteWorkspaceStore(root)).GetProjectAsync(project.SiteId)).Value!;
            Assert.Empty(reopened.Components.Single(c=>c.ComponentId==first).Slots["content"]);
            Assert.Equal(new[]{child},reopened.Components.Single(c=>c.ComponentId==destination).ChildIds);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Properties_and_responsive_values_are_owned_before_real_persistence_wait()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-properties-"+Guid.NewGuid());
        try
        {
            var projects=new SiteProjectService(new FileSiteWorkspaceStore(root));var edits=new SiteAuthoringService(projects);
            var project=(await projects.CreateProjectAsync(new(Guid.NewGuid(),null,null,"fixture","Snapshot","9to1-native","Snapshot"))).Value!;
            project=(await edits.CreatePageAsync(project.SiteId,project.Revision,"Home","/")).Value!;
            var document=JsonDocument.Parse("{\"text\":\"Captured property\"}");
            var properties=new Dictionary<string,JsonElement>{["text"]=document.RootElement.GetProperty("text")};
            Task<HavenOS.Apps.Sites.Domain.SiteApiResult<HavenOS.Apps.Sites.Domain.SiteProject>> pending;
            using(var persistenceLease=new FileStream(Path.Combine(root,".9to1-sites-index.json.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                pending=edits.AddComponentAsync(project.SiteId,project.Revision,project.Pages.Single().PageId,null,"p",properties);
                Assert.False(pending.IsCompleted);properties.Clear();document.Dispose();
            }
            var saved=await pending;Assert.Null(saved.Error);project=saved.Value!;
            var component=project.Components.Single();Assert.Equal("Captured property",component.Properties["text"].GetString());
            var patch=new Dictionary<string,JsonElement>{["width"]=JsonSerializer.SerializeToElement("100%")};
            using(var persistenceLease=new FileStream(Path.Combine(root,".9to1-sites-index.json.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                pending=edits.SetResponsiveOverrideAsync(project.SiteId,project.Revision,component.ComponentId,"mobile",patch);
                Assert.False(pending.IsCompleted);patch["width"]=JsonSerializer.SerializeToElement("1px");patch.Clear();
            }
            saved=await pending;Assert.Null(saved.Error);project=saved.Value!;
            Assert.Equal("100%",project.Components.Single().Layout.BreakpointOverrides["mobile"].ChangedProperties["width"].GetString());
            Assert.Equal(project.Revision,(await projects.GetProjectAsync(project.SiteId)).Value!.Revision);
            Assert.NotNull((await edits.AddComponentAsync(project.SiteId,project.Revision,project.Pages.Single().PageId,null,"p",null!)).Error);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
