using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using Xunit;

namespace HavenOS.Sites.Tests;

public sealed class SiteResponsiveCascadeTests
{
    [Fact]
    public async Task Persisted_phone_override_wins_after_tablet_regardless_of_edit_order_and_reset_inherits()
    {
        var root=Path.Combine(Path.GetTempPath(),"sites-responsive-"+Guid.NewGuid());
        try
        {
            var projects=new SiteProjectService(new FileSiteWorkspaceStore(root));
            var edits=new SiteAuthoringService(projects);
            var project=(await projects.CreateProjectAsync(new(Guid.NewGuid(),null,null,"fixture","Responsive","9to1-native","responsive"))).Value!;
            project=(await edits.CreatePageAsync(project.SiteId,project.Revision,"Home","/")).Value!;
            var page=project.Pages.Single().PageId;
            project=(await edits.AddComponentAsync(project.SiteId,project.Revision,page,null,"section",new Dictionary<string,JsonElement>())).Value!;
            var component=project.Components.Single().ComponentId;
            project=(await projects.UpdateProjectAsync(project.SiteId,project.Revision,current=>current with
            {
                DesignSystem=current.DesignSystem with {Breakpoints=new Dictionary<string,int>{{"phone",480},{"tablet",900}}},
                Components=current.Components.Select(c=>c with {Layout=c.Layout with {Properties=new Dictionary<string,JsonElement>{{"width",JsonSerializer.SerializeToElement("1000px")}}}}).ToArray()
            })).Value!;
            // Persist the narrower override first: map insertion order is not cascade order.
            project=(await edits.SetResponsiveOverrideAsync(project.SiteId,project.Revision,component,"phone",new Dictionary<string,JsonElement>{{"width",JsonSerializer.SerializeToElement("300px")}})).Value!;
            project=(await edits.SetResponsiveOverrideAsync(project.SiteId,project.Revision,component,"tablet",new Dictionary<string,JsonElement>{{"width",JsonSerializer.SerializeToElement("700px")},{"padding",JsonSerializer.SerializeToElement("12px")}})).Value!;
            var reopened=(await new SiteProjectService(new FileSiteWorkspaceStore(root)).GetProjectAsync(project.SiteId)).Value!;
            var renderer=new SiteDocumentRenderer();
            var rendered=renderer.Render(reopened,page,SiteRenderContext.PagePreview);
            Assert.Empty(rendered.Diagnostics);
            Assert.True(rendered.Document.IndexOf("@media(max-width:900px)",StringComparison.Ordinal)<rendered.Document.IndexOf("@media(max-width:480px)",StringComparison.Ordinal));
            Assert.Single(reopened.Components);
            Assert.Single(reopened.Components.Single().Layout.BreakpointOverrides["phone"].ChangedProperties);
            Assert.Equal(rendered.Document,renderer.Render(reopened,page,SiteRenderContext.PublicRoute).Document);
            project=(await edits.SetResponsiveOverrideAsync(project.SiteId,project.Revision,component,"phone",null)).Value!;
            var reset=renderer.Render(project,page,SiteRenderContext.PagePreview);
            Assert.DoesNotContain("@media(max-width:480px)",reset.Document);
            Assert.Contains("width:700px;padding:12px",reset.Document);
            Assert.Equal(component,project.Components.Single().ComponentId);
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
