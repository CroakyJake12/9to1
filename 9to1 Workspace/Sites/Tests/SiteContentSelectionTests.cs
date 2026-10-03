using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using Xunit;
namespace HavenOS.Sites.Tests;
public sealed class SiteContentSelectionTests
{
    [Fact]
    public async Task Canonical_content_selection_binds_page_targets_and_explicit_default()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-select-"+Guid.NewGuid());
        try
        {
            var projects=new SiteProjectService(new FileSiteWorkspaceStore(root));var edits=new SiteAuthoringService(projects);
            var p=(await projects.CreateProjectAsync(new(Guid.NewGuid(),null,null,"fixture","Select","9to1-native","Select"))).Value!;
            p=(await edits.CreatePageAsync(p.SiteId,p.Revision,"Home","/")).Value!;var page=p.Pages.Single();
            foreach(var item in new[]{("button","Subreddits"),("button","Discords"),("section","Subreddit content"),("section","Discord content")})
                p=(await edits.AddComponentAsync(p.SiteId,p.Revision,page.PageId,null,item.Item1,new Dictionary<string,JsonElement>{["text"]=JsonSerializer.SerializeToElement(item.Item2)})).Value!;
            var nodes=p.Components.ToArray();var choices=new[]{new SiteContentSelectionChoice(nodes[0].ComponentId,"subreddit",[nodes[2].ComponentId]),new SiteContentSelectionChoice(nodes[1].ComponentId,"discord",[nodes[3].ComponentId])};
            p=(await new SiteContentSelectionAuthoringService(projects).SetGroupAsync(p.SiteId,p.Revision,page.PageId,"communities",choices,"subreddit")).Value!;
            var mutableTargets=new List<Guid>{nodes[2].ComponentId};
            var mutableChoices=new List<SiteContentSelectionChoice>{new(nodes[0].ComponentId,"subreddit",mutableTargets),new(nodes[1].ComponentId,"discord",new List<Guid>{nodes[3].ComponentId})};
            Task<HavenOS.Apps.Sites.Domain.SiteApiResult<HavenOS.Apps.Sites.Domain.SiteProject>> pending;
            using(var persistenceLease=new FileStream(Path.Combine(root,".9to1-sites-index.json.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                pending=new SiteContentSelectionAuthoringService(projects).SetGroupAsync(p.SiteId,p.Revision,page.PageId,"communities",mutableChoices,"subreddit");
                Assert.False(pending.IsCompleted); // real persistence await is held, not an artificial service mock
                mutableTargets.Clear();mutableTargets.Add(Guid.NewGuid());mutableChoices.Clear();
            }
            var snapshotResult=await pending;Assert.Null(snapshotResult.Error);p=snapshotResult.Value!;
            var rendered=new SiteDocumentRenderer().Render(p,page.PageId,SiteRenderContext.PagePreview);Assert.Empty(rendered.Diagnostics);
            Assert.Contains("data-site-select-value=\"subreddit\" aria-pressed=\"true\"",rendered.Document);
            Assert.Contains("data-site-select-value=\"discord\" aria-pressed=\"false\"",rendered.Document);
            Assert.Contains("data-site-content-value=\"discord\" hidden",rendered.Document);
            Assert.Contains("document.addEventListener('click'",rendered.Document);
            Assert.Contains("[hidden]{display:none!important}",rendered.Document);
            var bad=await new SiteContentSelectionAuthoringService(projects).SetGroupAsync(p.SiteId,p.Revision,page.PageId,"communities",[new(nodes[0].ComponentId,"wrong",[Guid.NewGuid()])],"wrong");Assert.NotNull(bad.Error);
            Assert.Equal(p.Revision,(await projects.GetProjectAsync(p.SiteId)).Value!.Revision);
            var artifact=await new SiteArtifactAuthoringService(projects,Path.Combine(root,"builds")).BuildAsync(p.SiteId,p.Revision,"fixture-config");
            Assert.Equal(rendered.Document,await File.ReadAllTextAsync(Path.Combine(artifact.ArtifactReference,"index.html")));
            if(Environment.GetEnvironmentVariable("ASTRA_SELECTION_ARTIFACT") is {} reviewPath)await File.WriteAllTextAsync(reviewPath,rendered.Document);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
