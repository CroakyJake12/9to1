using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using Xunit;
namespace HavenOS.Sites.Tests;
public sealed class SiteHtmlContentImporterTests
{
    [Fact]
    public async Task Content_preserves_inline_order_links_lists_and_preview_build_identity()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-import-"+Guid.NewGuid());
        try
        {
            var service=new SiteProjectService(new FileSiteWorkspaceStore(root));
            var initial=(await service.CreateProjectAsync(new(Guid.NewGuid(),null,null,"fixture","Import","9to1-native","Import"))).Value!;
            var imported=await new SiteHtmlContentImporter(service).ImportPageAsync(initial.SiteId,initial.Revision,"Home","/",
                "<h1>CAKE &amp; community</h1><p>Before <strong>bold &lt;safe&gt;</strong> after <a href='/rules'>rules</a>.</p><ol><li>First</li><li>Second<br>next</li></ol><figure><img src='https://example.test/banner.png' alt='Banner'><figcaption>Caption</figcaption></figure>");
            Assert.NotNull(imported.Project);Assert.Empty(imported.Issues);
            var project=imported.Project!;var page=Assert.Single(project.Pages);
            var rendered=new SiteDocumentRenderer().Render(project,page.PageId,SiteRenderContext.PagePreview);
            Assert.Empty(rendered.Diagnostics);Assert.Contains("href=\"/rules\"",rendered.Document);Assert.Contains("<ol ",rendered.Document);
            Assert.True(rendered.Document.IndexOf("Before",StringComparison.Ordinal)<rendered.Document.IndexOf("bold &lt;safe&gt;",StringComparison.Ordinal));
            Assert.True(rendered.Document.IndexOf("bold &lt;safe&gt;",StringComparison.Ordinal)<rendered.Document.IndexOf(" after ",StringComparison.Ordinal));
            Assert.DoesNotContain("display:block",rendered.Document); // semantic inline/table defaults remain intact
            var artifact=await new SiteArtifactAuthoringService(service,Path.Combine(root,"builds")).BuildAsync(project.SiteId,project.Revision,"fixture-config");
            Assert.Equal(rendered.Document,await File.ReadAllTextAsync(Path.Combine(artifact.ArtifactReference,"index.html")));
            Assert.Equal(project.Revision,(await service.GetProjectAsync(project.SiteId)).Value!.Revision);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<p onclick='alert(1)'>Hello</p>")]
    [InlineData("<a href='javascript:alert(1)'>Click</a>")]
    [InlineData("<iframe src='https://example.test'></iframe>")]
    [InlineData("<form><input name='secret'></form>")]
    [InlineData("<img src='/missing-alt.png'>")]
    public async Task Unsupported_or_unsafe_content_does_not_mutate_project(string html)
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-reject-"+Guid.NewGuid());
        try
        {
            var service=new SiteProjectService(new FileSiteWorkspaceStore(root));
            var initial=(await service.CreateProjectAsync(new(Guid.NewGuid(),null,null,"fixture","Import","9to1-native","Import"))).Value!;
            var result=await new SiteHtmlContentImporter(service).ImportPageAsync(initial.SiteId,initial.Revision,"Home","/",html);
            Assert.Null(result.Project);Assert.Contains(result.Issues,i=>i.BlocksImport);
            var unchanged=(await service.GetProjectAsync(initial.SiteId)).Value!;
            Assert.Equal(initial.Revision,unchanged.Revision);Assert.Empty(unchanged.Pages);Assert.Empty(unchanged.Components);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
