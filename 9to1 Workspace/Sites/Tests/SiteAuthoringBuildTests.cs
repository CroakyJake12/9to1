using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Hosting;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using Xunit;

namespace HavenOS.Sites.Tests;

public sealed class SiteAuthoringBuildTests
{
    [Fact]
    public async Task Canonical_edit_preview_and_independent_artifact_share_rendering_and_identity()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-build-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var projects=new SiteProjectService(new FileSiteWorkspaceStore(root));var edits=new SiteAuthoringService(projects);
            var created=await projects.CreateProjectAsync(new(Guid.NewGuid(),null,null,"revision-a","Website","9to1-native","Website"));
            Assert.Null(created.Error);var project=created.Value!;
            project=(await edits.CreatePageAsync(project.SiteId,project.Revision,"Home","/")).Value!;
            var page=Assert.Single(project.Pages);
            project=(await edits.AddComponentAsync(project.SiteId,project.Revision,page.PageId,null,"heading",
                new Dictionary<string,JsonElement>{["text"]=JsonSerializer.SerializeToElement("Hello <world>"),["level"]=JsonSerializer.SerializeToElement(1)})).Value!;
            var component=Assert.Single(project.Components);
            var invalid=await edits.MoveComponentAsync(project.SiteId,project.Revision,component.ComponentId,page.PageId,component.ComponentId,0);
            Assert.NotNull(invalid.Error);
            var preview=new SiteDocumentRenderer().Render(project,page.PageId,SiteRenderContext.PagePreview);
            Assert.Contains("Hello &lt;world&gt;",preview.Document);Assert.Empty(preview.Diagnostics);
            Assert.Throws<InvalidOperationException>(()=>new SiteDocumentRenderer().Render(project,page.PageId,SiteRenderContext.EditorShell));
            await File.WriteAllTextAsync(Path.Combine(root,"site.project.json"),JsonSerializer.Serialize(project));
            var pipeline=new NativeSiteBuildPipeline(Path.Combine(root,"artifacts"));
            var artifact=await pipeline.BuildAndPackageAsync(new(project.SiteId,Guid.NewGuid(),new("revision-a","config-a","9to1-native",root,true,true),Guid.NewGuid()),(_,_)=>ValueTask.CompletedTask,CancellationToken.None);
            Assert.False(artifact.SecretScanPassed); // no scanner cannot assert a security gate passed
            Assert.Equal(preview.Document,await File.ReadAllTextAsync(Path.Combine(artifact.ArtifactReference,"index.html")));
            Assert.True((await pipeline.ValidateArtifactAsync(artifact,CancellationToken.None)).IsValid);
            var unlisted=Path.Combine(artifact.ArtifactReference,"private.json");await File.WriteAllTextAsync(unlisted,"unlisted");
            Assert.False((await pipeline.ValidateArtifactAsync(artifact,CancellationToken.None)).IsValid);File.Delete(unlisted);
            await File.AppendAllTextAsync(Path.Combine(artifact.ArtifactReference,"index.html"),"tampered");
            Assert.False((await pipeline.ValidateArtifactAsync(artifact,CancellationToken.None)).IsValid);
            var directArtifact=await new SiteArtifactAuthoringService(projects,Path.Combine(root,"authoring-builds")).BuildAsync(project.SiteId,project.Revision,"config-b");
            Assert.Equal(preview.Document,await File.ReadAllTextAsync(Path.Combine(directArtifact.ArtifactReference,"index.html")));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>new SiteArtifactAuthoringService(projects,Path.Combine(root,"authoring-builds")).BuildAsync(project.SiteId,project.Revision-1,"config-b"));
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root,"authoring-builds","inputs")));
            var reopened=(await new SiteProjectService(new FileSiteWorkspaceStore(root)).GetProjectAsync(project.SiteId)).Value!;
            Assert.Equal(component.ComponentId,Assert.Single(reopened.Components).ComponentId);
        }
        finally{Directory.Delete(root,true);}
    }
}
