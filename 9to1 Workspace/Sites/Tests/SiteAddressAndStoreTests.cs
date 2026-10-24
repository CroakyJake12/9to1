using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class SiteAddressAndStoreTests
{
    [Theory]
    [InlineData("studio", "studio")]
    [InlineData("my-site", "my-site")]
    public void NormalizeSlug_accepts_only_canonical_lowercase_names(string input, string expected) =>
        Assert.Equal(expected, SiteAddressRules.NormalizeSlug(input));

    [Theory]
    [InlineData("MySite")]
    [InlineData("my--site")]
    [InlineData("-site")]
    [InlineData("site-")]
    [InlineData("a1")]
    [InlineData("admin")]
    public void NormalizeSlug_rejects_invalid_or_reserved_names(string input) =>
        Assert.Throws<SiteOperationException>(() => SiteAddressRules.NormalizeSlug(input));

    [Fact]
    public void NormalizeSlug_rejects_names_longer_than_one_dns_label() =>
        Assert.Throws<SiteOperationException>(() => SiteAddressRules.NormalizeSlug(new string('a', 64)));

    [Fact]
    public void NormalizeRoutePath_preserves_segments_and_supports_parameters()
    {
        var segments = SiteAddressRules.NormalizeRoutePath("/blog/:post-id");

        Assert.Equal(new[] { "blog", ":post-id" }, segments);
    }

    [Fact]
    public void NormalizeHostname_canonicalizes_idn_to_ascii()
    {
        var normalized = SiteAddressRules.NormalizeHostname("bücher.example");

        Assert.Equal("xn--bcher-kva.example", normalized.AsciiName);
        Assert.Equal("bücher.example", normalized.DisplayName);
    }

    [Fact]
    public void NormalizeHostname_rejects_single_label_names() =>
        Assert.Throws<SiteOperationException>(() => SiteAddressRules.NormalizeHostname("localhost"));

    [Fact]
    public async Task Concurrent_slug_reservations_cannot_claim_the_same_name()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileSiteWorkspaceStore(directory.Path);
        var first = ReserveAsync(store, Guid.NewGuid());
        var second = ReserveAsync(store, Guid.NewGuid());
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome);
        var saved = await store.ReadAsync(state => state.SlugReservations);
        Assert.Single(saved);
        Assert.Equal("studio", saved[0].Slug);
    }

    [Fact]
    public async Task Unsupported_schema_is_rejected_without_rewriting_the_index()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, ".9to1-sites-index.json");
        var original = Encoding.UTF8.GetBytes("{\"schemaVersion\":999,\"projects\":[],\"deployments\":[],\"domains\":[],\"nameVerifications\":[],\"domainChallenges\":[],\"slugReservations\":[]}");
        await File.WriteAllBytesAsync(path, original);
        var store = new FileSiteWorkspaceStore(directory.Path);

        await Assert.ThrowsAsync<SiteOperationException>(() => store.ReadAsync(state => state));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("deployments")]
    [InlineData("domains")]
    [InlineData("nameVerifications")]
    [InlineData("domainChallenges")]
    [InlineData("slugReservations")]
    public async Task Null_index_collection_is_rejected_without_rewriting_the_index(string collection)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, ".9to1-sites-index.json");
        var index = JsonNode.Parse("{\"schemaVersion\":1,\"projects\":[],\"deployments\":[],\"domains\":[],\"nameVerifications\":[],\"domainChallenges\":[],\"slugReservations\":[]}")!;
        index[collection] = null;
        var original = Encoding.UTF8.GetBytes(index.ToJsonString());
        await File.WriteAllBytesAsync(path, original);
        var store = new FileSiteWorkspaceStore(directory.Path);

        var failure = await Assert.ThrowsAsync<SiteOperationException>(() => store.ReadAsync(state => state));

        Assert.Equal("SitesMetadataInvalid", failure.Error.Code);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Unsupported_project_schema_is_rejected_before_an_unrelated_mutation()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileSiteWorkspaceStore(directory.Path);
        var created = await new SiteProjectService(store).CreateProjectAsync(new(Guid.NewGuid(), null, null,
            "source-1", "Preserved project", "9to1-native", "preserved"));
        Assert.True(created.IsSuccess);
        var path = Path.Combine(directory.Path, ".9to1-sites-index.json");
        var index = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        index["projects"]![0]!["schemaVersion"] = 999;
        var original = Encoding.UTF8.GetBytes(index.ToJsonString());
        await File.WriteAllBytesAsync(path, original);
        var mutationReached = false;

        var failure = await Assert.ThrowsAsync<SiteOperationException>(() => store.MutateAsync(state =>
        {
            mutationReached = true;
            return (state, true);
        }));

        Assert.Equal("SitesSchemaUnsupported", failure.Error.Code);
        Assert.False(mutationReached);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Responsive_overrides_require_a_declared_project_breakpoint_and_reopen_only_the_patch()
    {
        using var directory=new TemporaryDirectory();
        var store=new FileSiteWorkspaceStore(directory.Path);var projects=new SiteProjectService(store);
        var created=await projects.CreateProjectAsync(new(Guid.NewGuid(),null,null,"source-1","Responsive","9to1-native","site"));
        Assert.True(created.IsSuccess);
        var originalProject=created.Value!;
        var authoring=new SiteAuthoringService(projects);
        var page=await authoring.CreatePageAsync(originalProject.SiteId,originalProject.Revision,"Home","/");
        Assert.True(page.IsSuccess);
        var originalPage=page.Value!;
        var component=await authoring.AddComponentAsync(originalProject.SiteId,originalPage.Revision,originalPage.Pages[0].PageId,null,
            "text",new Dictionary<string,JsonElement>());
        Assert.True(component.IsSuccess);
        var addedComponent=component.Value!;
        var withBreakpoint=await projects.UpdateProjectAsync(originalProject.SiteId,addedComponent.Revision,project=>project with
        { DesignSystem=project.DesignSystem with { Breakpoints=new Dictionary<string,int>(StringComparer.Ordinal) { ["tablet"]=768,["invalid"]=0 } } });
        Assert.True(withBreakpoint.IsSuccess);
        var configured=withBreakpoint.Value!;
        var componentId=configured.Components.Single().ComponentId;
        var empty=await authoring.SetResponsiveOverrideAsync(originalProject.SiteId,configured.Revision,componentId,"tablet",
            new Dictionary<string,JsonElement>());
        Assert.Equal("InvalidInput",empty.Error?.Code);
        var unknown=await authoring.SetResponsiveOverrideAsync(originalProject.SiteId,configured.Revision,componentId,"phone",
            new Dictionary<string,JsonElement> { ["width"]=JsonSerializer.SerializeToElement("100%") });
        Assert.Equal("InvalidInput",unknown.Error?.Code);
        var nonpositive=await authoring.SetResponsiveOverrideAsync(originalProject.SiteId,configured.Revision,componentId,"invalid",
            new Dictionary<string,JsonElement> { ["width"]=JsonSerializer.SerializeToElement("100%") });
        Assert.Equal("InvalidInput",nonpositive.Error?.Code);
        var unsafeValue=await authoring.SetResponsiveOverrideAsync(originalProject.SiteId,configured.Revision,componentId,"tablet",
            new Dictionary<string,JsonElement> { ["width"]=JsonSerializer.SerializeToElement("url(https://unsafe.example)") });
        Assert.Equal("InvalidInput",unsafeValue.Error?.Code);
        Assert.Equal(configured.Revision,(await projects.GetProjectAsync(originalProject.SiteId)).Value!.Revision);

        var applied=await authoring.SetResponsiveOverrideAsync(originalProject.SiteId,configured.Revision,componentId,"tablet",
            new Dictionary<string,JsonElement> { ["width"]=JsonSerializer.SerializeToElement("100%") });
        Assert.True(applied.IsSuccess);
        var firstApplied=applied.Value!;
        var patched=await authoring.SetResponsiveOverrideAsync(originalProject.SiteId,firstApplied.Revision,componentId,"tablet",
            new Dictionary<string,JsonElement> { ["padding"]=JsonSerializer.SerializeToElement("8px") });
        Assert.True(patched.IsSuccess);
        var reopened=new FileSiteWorkspaceStore(directory.Path);
        var saved=await reopened.ReadAsync(state=>state.Projects.Single());
        var responsive=saved.Components.Single().Layout.BreakpointOverrides["tablet"].ChangedProperties;
        Assert.Equal(new[] { "padding","width" },responsive.Keys.OrderBy(key=>key,StringComparer.Ordinal));
        Assert.Equal("100%",responsive["width"].GetString());
        Assert.Equal("8px",responsive["padding"].GetString());
        var rendered=new SiteDocumentRenderer().Render(saved,originalPage.Pages[0].PageId,SiteRenderContext.PagePreview);
        Assert.DoesNotContain(rendered.Diagnostics,item=>item.IsError);
        Assert.Contains("@media(max-width:768px)",rendered.Document);
        Assert.Contains("width:100%",rendered.Document);
        Assert.Contains("padding:8px",rendered.Document);
        var reset=await authoring.SetResponsiveOverrideAsync(saved.SiteId,saved.Revision,saved.Components.Single().ComponentId,"tablet",null);
        Assert.True(reset.IsSuccess);
        var afterReset=await reopened.ReadAsync(state=>state.Projects.Single());
        Assert.DoesNotContain("tablet",afterReset.Components.Single().Layout.BreakpointOverrides.Keys);
        Assert.DoesNotContain("width:100%",new SiteDocumentRenderer().Render(afterReset,originalPage.Pages[0].PageId,SiteRenderContext.PagePreview).Document);
    }

    [Fact]
    public async Task Nested_page_text_remains_reachable_for_the_CUI_editor_selection()
    {
        using var directory=new TemporaryDirectory();var projects=new SiteProjectService(new FileSiteWorkspaceStore(directory.Path));
        var created=await projects.CreateProjectAsync(new(Guid.NewGuid(),null,null,"source-1","Nested","9to1-native","site"));
        Assert.True(created.IsSuccess);
        var owner=created.Value!;var authoring=new SiteAuthoringService(projects);
        var page=await authoring.CreatePageAsync(owner.SiteId,owner.Revision,"Home","/");
        Assert.True(page.IsSuccess);
        var source=page.Value!;
        var section=await authoring.AddComponentAsync(owner.SiteId,source.Revision,source.Pages[0].PageId,null,"section",new Dictionary<string,JsonElement>());
        Assert.True(section.IsSuccess);
        var shell=section.Value!;
        var text=await authoring.AddComponentAsync(owner.SiteId,shell.Revision,source.Pages[0].PageId,shell.Components[0].ComponentId,
            "text",new Dictionary<string,JsonElement>());
        Assert.True(text.IsSuccess);
        var nested=text.Value!;
        var reachable=SiteAuthoringService.Reachable(nested,nested.Pages[0].RootComponentIds);
        Assert.Contains(nested.Components.Single(item=>item.ComponentType=="section").ComponentId,reachable);
        Assert.Contains(nested.Components.Single(item=>item.ComponentType=="text").ComponentId,reachable);
    }

    [Fact]
    public async Task Page_rename_preserves_page_route_identity_and_rejects_stale_revision()
    {
        using var directory=new TemporaryDirectory();
        var store=new FileSiteWorkspaceStore(directory.Path);var projects=new SiteProjectService(store);var authoring=new SiteAuthoringService(projects);
        var created=await projects.CreateProjectAsync(new(Guid.NewGuid(),null,null,"source-1","Site","9to1-native","site"));
        Assert.True(created.IsSuccess);
        var project=created.Value!;
        var page=await authoring.CreatePageAsync(project.SiteId,project.Revision," Home "," / ");
        Assert.True(page.IsSuccess);
        var original=page.Value!;var route=original.Routes.Single();
        Assert.Equal("Home",original.Pages[0].Name);Assert.Equal("/",route.Pattern);
        Assert.Null(original.Pages[0].SeoTitle);
        var duplicate=await authoring.CreatePageAsync(original.SiteId,original.Revision,"Another"," /");
        Assert.Equal("RouteConflict",duplicate.Error?.Code);
        var alias=await authoring.CreateRouteAsync(original.SiteId,original.Revision,original.Pages[0].PageId," /welcome ");
        Assert.True(alias.IsSuccess);
        var withAlias=alias.Value!;
        var aliasRoute=withAlias.Routes.Single(item=>item.Pattern=="/welcome");
        Assert.Equal(original.Pages[0].PageId,aliasRoute.PageId);
        Assert.NotEqual(route.RouteId,aliasRoute.RouteId);
        var duplicateAlias=await authoring.CreateRouteAsync(original.SiteId,withAlias.Revision,original.Pages[0].PageId,"/welcome");
        Assert.Equal("RouteConflict",duplicateAlias.Error?.Code);
        var blankRoute=await authoring.CreateRouteAsync(original.SiteId,withAlias.Revision,original.Pages[0].PageId," ");
        Assert.Equal("InvalidInput",blankRoute.Error?.Code);

        var renamed=await authoring.RenamePageAsync(original.SiteId,withAlias.Revision,original.Pages[0].PageId,"Welcome");

        Assert.True(renamed.IsSuccess);
        var updated=renamed.Value!;
        Assert.Equal(original.Pages[0].PageId,updated.Pages[0].PageId);
        Assert.Equal("Welcome",updated.Pages[0].Name);
        Assert.Null(updated.Pages[0].SeoTitle);
        Assert.Equal(original.Pages[0].Revision+1,updated.Pages[0].Revision);
        Assert.Contains(updated.Routes, item=>item.RouteId==route.RouteId&&item.Pattern==route.Pattern);
        Assert.Contains(updated.Routes, item=>item.RouteId==aliasRoute.RouteId&&item.Pattern=="/welcome");
        Assert.Contains("<title>Welcome</title>",new SiteDocumentRenderer().Render(updated,updated.Pages[0].PageId,SiteRenderContext.PublicRoute).Document);
        var stale=await authoring.RenamePageAsync(original.SiteId,original.Revision,original.Pages[0].PageId,"Stale name");
        Assert.Equal("RevisionConflict",stale.Error?.Code);
        var current=(await projects.GetProjectAsync(original.SiteId)).Value!;
        Assert.Equal("Welcome",current.Pages[0].Name);
        var missing=await authoring.RenamePageAsync(original.SiteId,current.Revision,Guid.NewGuid(),"Missing page");
        Assert.Equal("PageNotFound",missing.Error?.Code);
        var blank=await authoring.RenamePageAsync(original.SiteId,current.Revision,original.Pages[0].PageId," ");
        var oversized=await authoring.RenamePageAsync(original.SiteId,current.Revision,original.Pages[0].PageId,new string('x',121));
        Assert.Equal("InvalidInput",blank.Error?.Code);Assert.Equal("InvalidInput",oversized.Error?.Code);
        var seo=await authoring.SetPageSeoAsync(original.SiteId,current.Revision,original.Pages[0].PageId," Welcome & News "," About <this site> ");
        Assert.True(seo.IsSuccess);
        var seoProject=seo.Value!;
        var seoPage=seoProject.Pages.Single();
        Assert.Equal("Welcome & News",seoPage.SeoTitle);Assert.Equal("About <this site>",seoPage.MetaDescription);
        var seoHtml=new SiteDocumentRenderer().Render(seoProject,seoPage.PageId,SiteRenderContext.PublicRoute).Document;
        Assert.Contains("<title>Welcome &amp; News</title>",seoHtml);
        Assert.Contains("content=\"About &lt;this site&gt;\"",seoHtml);
        Assert.Equal(current.Revision+1,seoProject.Revision);
        var oversizedSeo=await authoring.SetPageSeoAsync(seoProject.SiteId,seoProject.Revision,seoPage.PageId,new string('s',65537),null);
        Assert.Equal("InvalidInput",oversizedSeo.Error?.Code);
        Assert.Equal(seoProject.Revision,(await projects.GetProjectAsync(seoProject.SiteId)).Value!.Revision);
        var clearedSeo=await authoring.SetPageSeoAsync(seoProject.SiteId,seoProject.Revision,seoPage.PageId,null,null);
        Assert.True(clearedSeo.IsSuccess);
        var clearedProject=clearedSeo.Value!;
        var clearedPage=clearedProject.Pages.Single();
        var clearedHtml=new SiteDocumentRenderer().Render(clearedProject,clearedPage.PageId,SiteRenderContext.PublicRoute).Document;
        Assert.Contains("<title>Welcome</title>",clearedHtml);
        Assert.DoesNotContain("name=\"description\"",clearedHtml);
    }

    [Fact]
    public void Responsive_width_write_is_a_revision_scoped_typed_Build_action()
    {
        var siteId=Guid.NewGuid();var projectId=Guid.NewGuid();var domainId=Guid.NewGuid();var sourceBindingProjectId=Guid.NewGuid();var componentId=Guid.NewGuid();
        var binding=new SiteNativeWorkspaceBinding("profile","actor","auth-revision",Guid.NewGuid(),"files-revision","/files/sites");
        Assert.Throws<ArgumentException>(()=>SiteNativeWriteIntent.SetResponsiveOverride(binding,siteId,12,componentId,"tablet",
            new Dictionary<string,JsonElement> { ["width"]=JsonSerializer.SerializeToElement("100%") },projectId,null!));
        Assert.Throws<ArgumentException>(()=>SiteNativeWriteIntent.SetResponsiveOverride(binding,siteId,12,componentId,"tablet",
            new Dictionary<string,JsonElement> { ["width"]=JsonSerializer.SerializeToElement("100%") },projectId,"",domainId));
        Assert.Throws<ArgumentException>(()=>SiteNativeWriteIntent.SetResponsiveOverride(binding,siteId,12,componentId,"tablet",
            new Dictionary<string,JsonElement> { ["width"]=JsonSerializer.SerializeToElement("100%") },projectId,"build-revision",Guid.Empty));
        var intent=SiteNativeWriteIntent.SetResponsiveOverride(binding,siteId,12,componentId,"tablet",
            new Dictionary<string,JsonElement> { ["width"]=JsonSerializer.SerializeToElement("100%") },projectId,"build-revision",domainId,sourceBindingProjectId);
        Assert.Equal("sites.project.save",intent.ActionId);
        Assert.NotNull(new SiteNativeActionPolicies().TryGet(SiteNativeWriteIntent.TargetAppId,intent.ActionId));
        Assert.Contains(intent.Scopes,scope=>scope.Kind=="sites.project"&&scope.Id==siteId.ToString());
        using var arguments=JsonDocument.Parse(intent.Arguments.GetRawText());
        Assert.Equal("component.responsive",arguments.RootElement.GetProperty("operation").GetString());
        Assert.Equal(siteId,arguments.RootElement.GetProperty("siteID").GetGuid());
        Assert.Equal(projectId,arguments.RootElement.GetProperty("projectID").GetGuid());
        Assert.Equal(domainId,arguments.RootElement.GetProperty("domainID").GetGuid());
        Assert.Equal(sourceBindingProjectId,arguments.RootElement.GetProperty("sourceBindingProjectID").GetGuid());
        Assert.Equal("build-revision",arguments.RootElement.GetProperty("sourceRevision").GetString());
        Assert.Equal(12L,arguments.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("tablet",arguments.RootElement.GetProperty("payload").GetProperty("breakpoint").GetString());
        var pageId=Guid.NewGuid();var pageIntent=SiteNativeWriteIntent.UpdatePage(binding,siteId,12,pageId,"Welcome",projectId,"build-revision",domainId,sourceBindingProjectId);
        Assert.Equal(intent.ActionId,pageIntent.ActionId);
        using var pageArguments=JsonDocument.Parse(pageIntent.Arguments.GetRawText());
        Assert.Equal("page.update",pageArguments.RootElement.GetProperty("operation").GetString());
        Assert.Equal(projectId,pageArguments.RootElement.GetProperty("projectID").GetGuid());
        Assert.Equal(domainId,pageArguments.RootElement.GetProperty("domainID").GetGuid());
        Assert.Equal("build-revision",pageArguments.RootElement.GetProperty("sourceRevision").GetString());
        Assert.Equal(pageId,pageArguments.RootElement.GetProperty("payload").GetProperty("pageID").GetGuid());
        Assert.Equal("Welcome",pageArguments.RootElement.GetProperty("payload").GetProperty("name").GetString());
        var routeIntent=SiteNativeWriteIntent.CreateRoute(binding,siteId,12,pageId,"/welcome",projectId,"build-revision",domainId,sourceBindingProjectId);
        Assert.Equal(intent.ActionId,routeIntent.ActionId);
        using var routeArguments=JsonDocument.Parse(routeIntent.Arguments.GetRawText());
        Assert.Equal("route.create",routeArguments.RootElement.GetProperty("operation").GetString());
        Assert.Equal(pageId,routeArguments.RootElement.GetProperty("payload").GetProperty("pageID").GetGuid());
        var seoIntent=SiteNativeWriteIntent.UpdatePageSeo(binding,siteId,12,pageId,"Welcome","A public description",projectId,"build-revision",domainId,sourceBindingProjectId);
        Assert.Equal(intent.ActionId,seoIntent.ActionId);
        using var seoArguments=JsonDocument.Parse(seoIntent.Arguments.GetRawText());
        Assert.Equal("page.seo",seoArguments.RootElement.GetProperty("operation").GetString());
        Assert.Equal("A public description",seoArguments.RootElement.GetProperty("payload").GetProperty("metaDescription").GetString());
        var clearSeoIntent=SiteNativeWriteIntent.UpdatePageSeo(binding,siteId,12,pageId,null,null,projectId,"build-revision",domainId,sourceBindingProjectId);
        using var clearSeoArguments=JsonDocument.Parse(clearSeoIntent.Arguments.GetRawText());
        Assert.Equal(JsonValueKind.Null,clearSeoArguments.RootElement.GetProperty("payload").GetProperty("seoTitle").ValueKind);
        var paddingIntent=SiteNativeWriteIntent.SetResponsiveOverride(binding,siteId,12,componentId,"tablet",
            new Dictionary<string,JsonElement> { ["padding"]=JsonSerializer.SerializeToElement("8px") },projectId,"build-revision",domainId,sourceBindingProjectId);
        using var paddingArguments=JsonDocument.Parse(paddingIntent.Arguments.GetRawText());
        Assert.Equal("8px",paddingArguments.RootElement.GetProperty("payload").GetProperty("properties").GetProperty("padding").GetString());
        var reset=SiteNativeWriteIntent.SetResponsiveOverride(binding,siteId,12,componentId,"tablet",null,projectId,"build-revision",domainId,sourceBindingProjectId);
        using var resetArguments=JsonDocument.Parse(reset.Arguments.GetRawText());
        Assert.Equal(JsonValueKind.Null,resetArguments.RootElement.GetProperty("payload").GetProperty("properties").ValueKind);
    }

    [Fact]
    public void Failed_preview_render_keeps_the_last_valid_saved_render()
    {
        var buffer=new SitePreviewBuffer();
        var valid=new SiteRenderResult("<html>last valid</html>",[]);
        Assert.True(buffer.TryReplace(valid));
        var failed=new SiteRenderResult("<html>partial</html>",[new("BuildFailed","Preview failed.",null,null,true)]);
        Assert.False(buffer.TryReplace(failed));
        Assert.Same(valid,buffer.Current);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("domain")]
    [InlineData("path")]
    [InlineData("revision")]
    [InlineData("framework")]
    public async Task Generic_project_updates_cannot_detach_Build_source_identity(string field)
    {
        using var directory=new TemporaryDirectory();var store=new FileSiteWorkspaceStore(directory.Path);var service=new SiteProjectService(store);
        var created=await service.CreateProjectAsync(new(Guid.NewGuid(),null,null,"source-1","Bound","9to1-native","site"));
        Assert.True(created.IsSuccess);
        var original=created.Value!;
        var result=await service.UpdateProjectAsync(original.SiteId,original.Revision,project=>project with
        { Source=project.Source with
            {
                StackProjectId=field=="project"?Guid.NewGuid():project.Source.StackProjectId,
                StackDomainId=field=="domain"?Guid.NewGuid():project.Source.StackDomainId,
                SourceRevision=field=="revision"?"different-revision":project.Source.SourceRevision,
                FrameworkId=field=="framework"?"another-framework":project.Source.FrameworkId,
                RelativeProjectPath=field=="path"?"elsewhere":project.Source.RelativeProjectPath
            } });
        Assert.Equal("IdentityMutationBlocked",result.Error?.Code);
        var reopened=await new FileSiteWorkspaceStore(directory.Path).ReadAsync(state=>state.Projects.Single());
        Assert.Null(reopened.Source.StackProjectId);Assert.Null(reopened.Source.StackDomainId);
        Assert.Equal("source-1",reopened.Source.SourceRevision);Assert.Equal("9to1-native",reopened.Source.FrameworkId);
        Assert.Equal("site",reopened.Source.RelativeProjectPath);
    }

    [Fact]
    public async Task Source_bound_projects_refuse_Sites_only_mutations_until_Build_writer_is_available()
    {
        using var directory=new TemporaryDirectory();var store=new FileSiteWorkspaceStore(directory.Path);var service=new SiteProjectService(store);
        var buildProjectId=Guid.NewGuid();var domainId=Guid.NewGuid();
        var created=await service.CreateProjectAsync(new(Guid.NewGuid(),buildProjectId,domainId,"build-revision","Linked","9to1-native","site"));
        Assert.True(created.IsSuccess);
        var project=created.Value!;
        var blocked=await service.UpdateProjectAsync(project.SiteId,project.Revision,current=>current with { Name="Divergent edit" });
        Assert.Equal("CapabilityUnavailable",blocked.Error?.Code);
        var reopened=await new FileSiteWorkspaceStore(directory.Path).ReadAsync(state=>state.Projects.Single());
        Assert.Equal("Linked",reopened.Name);Assert.Equal<Guid?>(buildProjectId,reopened.Source.StackProjectId);
        Assert.Equal<Guid?>(domainId,reopened.Source.StackDomainId);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("domain")]
    public async Task Project_creation_rejects_empty_Build_source_ids(string emptyIdentity)
    {
        using var directory=new TemporaryDirectory();var service=new SiteProjectService(new FileSiteWorkspaceStore(directory.Path));
        var request=new CreateSiteProjectRequest(Guid.NewGuid(),emptyIdentity=="project"?Guid.Empty:Guid.NewGuid(),
            emptyIdentity=="domain"?Guid.Empty:Guid.NewGuid(),"source-1","Invalid binding","9to1-native","site");
        var result=await service.CreateProjectAsync(request);
        Assert.Equal("InvalidInput",result.Error?.Code);
        Assert.Empty(await new FileSiteWorkspaceStore(directory.Path).ReadAsync(state=>state.Projects));
    }

    [Fact]
    public async Task Store_preserves_a_source_project_binding_when_no_domain_is_bound()
    {
        using var directory=new TemporaryDirectory();var store=new FileSiteWorkspaceStore(directory.Path);
        var created=await new SiteProjectService(store).CreateProjectAsync(new(Guid.NewGuid(),null,null,"source-1","Local","9to1-native","site"));
        Assert.True(created.IsSuccess);
        var original=created.Value!;var buildProjectId=Guid.NewGuid();
        await store.MutateAsync(state=>(state with { Projects=[original with { Source=original.Source with { StackProjectId=buildProjectId } }] },true));
        var reopened=await new FileSiteWorkspaceStore(directory.Path).ReadAsync(state=>state.Projects.Single());
        Assert.Equal<Guid?>(buildProjectId,reopened.Source.StackProjectId);
        Assert.Null(reopened.Source.StackDomainId);
    }

    private static async Task<bool> ReserveAsync(FileSiteWorkspaceStore store, Guid siteId)
    {
        try
        {
            await store.MutateAsync(state =>
            {
                var reservation = new SiteSlugReservation("studio", siteId, SiteNameVerificationState.Passed, DateTimeOffset.UtcNow, null);
                return (state with { SlugReservations = [.. state.SlugReservations, reservation] }, true);
            });
            return true;
        }
        catch (SiteOperationException)
        {
            return false;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sites-tests-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
