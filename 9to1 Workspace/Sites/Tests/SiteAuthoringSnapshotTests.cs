using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Infrastructure;
using Xunit;
namespace HavenOS.Sites.Tests;
public sealed class SiteAuthoringSnapshotTests
{
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
