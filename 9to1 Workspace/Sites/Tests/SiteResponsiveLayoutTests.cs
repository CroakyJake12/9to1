using System.Text.Json;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class SiteResponsiveLayoutTests
{
    [Fact]
    public async Task SavedBreakpointOverridesAndResetShareSourceAndRendererWithoutPublishing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sites-breakpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSiteWorkspaceStore(directory);
            var projects = new SiteProjectService(store);
            var authoring = new SiteAuthoringService(projects);
            var renderer = new SiteDocumentRenderer();
            var project = Good(await projects.CreateProjectAsync(new(Guid.NewGuid(), null, null, "source-1",
                "Responsive", "native", "responsive")));
            project = Good(await authoring.CreatePageAsync(project.SiteId, project.Revision, "Home", "/"));
            var page = Assert.Single(project.Pages);
            project = Good(await authoring.AddComponentAsync(project.SiteId, project.Revision, page.PageId, null,
                "text", Css("text", "Exact canonical page")));
            var component = Assert.Single(project.Components);
            project = Good(await authoring.UpdateComponentAsync(project.SiteId, project.Revision, component.ComponentId,
                node => node with { Layout = node.Layout with { Properties = Css("max-width", "72rem") } }));
            project = Good(await authoring.SetResponsiveOverrideAsync(project.SiteId, project.Revision,
                component.ComponentId, "tablet", Css("max-width", "100%")));
            var beforeMobile = project.Revision;
            project = Good(await authoring.SetResponsiveOverrideAsync(project.SiteId, project.Revision,
                component.ComponentId, "mobile", Css("padding", "1rem")));

            var mobile = SiteResponsiveLayout.Inspect(project, page.PageId, component.ComponentId, "mobile");
            Assert.Equal(project.ProjectId, mobile.ProjectId);
            Assert.Equal(project.Revision, mobile.ProjectRevision);
            Assert.Equal(page.PageId, mobile.PageId);
            Assert.Equal(640, mobile.MaximumWidth);
            Assert.Equal("tablet", mobile.Properties["max-width"].InheritedFromBreakpoint);
            Assert.Equal("100%", mobile.Properties["max-width"].Value.GetString());
            Assert.True(mobile.Properties["padding"].IsExplicitOverride);
            var desktop = SiteResponsiveLayout.Inspect(project, page.PageId, component.ComponentId, null);
            Assert.Equal("72rem", desktop.Properties["max-width"].Value.GetString());
            Assert.False(desktop.Properties.ContainsKey("padding"));

            var preview = renderer.Render(project, page.PageId, SiteRenderContext.PagePreview);
            Assert.Empty(preview.Diagnostics);
            Assert.Contains("Exact canonical page", preview.Document);
            var tabletRule = preview.Document.IndexOf("@media(max-width:1024px)", StringComparison.Ordinal);
            var mobileRule = preview.Document.IndexOf("@media(max-width:640px)", StringComparison.Ordinal);
            Assert.True(tabletRule >= 0 && mobileRule > tabletRule);
            Assert.Contains("padding:1rem", preview.Document);

            var unknown = await authoring.SetResponsiveOverrideAsync(project.SiteId, project.Revision, component.ComponentId,
                "invalid", Css("padding", "0"));
            Assert.Equal("InvalidInput", unknown.Error?.Code);
            var unsafeValue = await authoring.SetResponsiveOverrideAsync(project.SiteId, project.Revision, component.ComponentId,
                "mobile", Css("background-color", "red; border:0"));
            Assert.Equal("InvalidInput", unsafeValue.Error?.Code);
            var stale = await authoring.SetResponsiveOverrideAsync(project.SiteId, beforeMobile, component.ComponentId, "mobile", null);
            Assert.Equal("RevisionConflict", stale.Error?.Code);
            Assert.Equal(project.Revision, Good(await projects.GetProjectAsync(project.SiteId)).Revision);

            project = Good(await authoring.SetResponsiveOverrideAsync(project.SiteId, project.Revision, component.ComponentId, "mobile", null));
            var reset = SiteResponsiveLayout.Inspect(project, page.PageId, component.ComponentId, "mobile");
            Assert.False(reset.Properties.ContainsKey("padding"));
            Assert.Equal("tablet", reset.Properties["max-width"].InheritedFromBreakpoint);
            Assert.DoesNotContain("@media(max-width:640px)", renderer.Render(project, page.PageId, SiteRenderContext.PagePreview).Document);
            Assert.Equal(page.PageId, Assert.Single(project.Pages).PageId);
            Assert.Empty(await store.ReadAsync(state => state.Deployments));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static SiteProject Good(SiteApiResult<SiteProject> result) =>
        result.Value ?? throw new InvalidOperationException(result.Error?.Message);

    private static IReadOnlyDictionary<string, JsonElement> Css(string name, string value) =>
        new Dictionary<string, JsonElement> { [name] = JsonSerializer.SerializeToElement(value) };
}
