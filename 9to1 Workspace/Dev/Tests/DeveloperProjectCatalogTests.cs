using Xunit;
using HavenOS.Apps.Dev;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperProjectCatalogTests
{
    [Fact]
    public async Task Search_FiltersProviderTemplatesAndReportsMissingToolchainBeforeCreation()
    {
        var provider = new TemplateProvider();
        var catalog = new DeveloperProjectCatalog([provider]);

        var result = await catalog.SearchAsync(new(Language: "C#", ToolchainsReady: false));

        Assert.True(result.Succeeded);
        var option = Assert.Single(result.Value!);
        Assert.Equal("console-csharp", option.Template.TemplateId);
        Assert.False(option.CanCreate);
        Assert.Equal(DeveloperToolchainState.MissingCompilerOrRuntime, Assert.Single(option.Readiness).State);
    }

    [Fact]
    public async Task Search_RejectsProviderThatOmitsDeclaredToolchainRequirement()
    {
        var result = await new DeveloperProjectCatalog([new TemplateProvider(omitReadiness: true)])
            .SearchAsync(new());

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.CapabilityUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task Search_AppliesTypedProjectRuntimeProviderAndOrganisationFiltersToActualProviderMetadata()
    {
        var selected = new DeveloperProjectTemplate("shared-template", "CUI Desktop", "desktop", ["CUI"], [], ["Linux"],
            ["Build"], [], "provider.desktop", true, ["net10.0"], "org.template.desktop");
        var otherProvider = new CatalogProvider("provider.web", [new DeveloperProjectTemplate("shared-template", "CUI Web", "web",
            ["CUI"], [], ["Browser"], ["Build"], [], "provider.web", true, ["browser"], "org.template.web")]);
        var catalog = new DeveloperProjectCatalog([
            new CatalogProvider("provider.desktop", [selected]),
            otherProvider
        ]);

        var result = await catalog.SearchAsync(new(ProjectType: "desktop", TargetRuntime: "NET10.0", Platform: "linux",
            ProductSurface: "build", ProviderId: "provider.desktop", OrganisationTemplateId: "org.template.desktop",
            ToolchainsReady: true));

        Assert.True(result.Succeeded);
        Assert.Equal(selected, Assert.Single(result.Value!).Template);
        var wrongProvider = await catalog.SearchAsync(new(ProviderId: "provider.web", ProjectType: "desktop"));
        Assert.True(wrongProvider.Succeeded);
        Assert.Empty(wrongProvider.Value!);

        var partialProvider = await catalog.SearchAsync(new(ProviderId: "provider"));
        Assert.True(partialProvider.Succeeded);
        Assert.Empty(partialProvider.Value!);
        var partialRuntime = await catalog.SearchAsync(new(TargetRuntime: "net10"));
        Assert.True(partialRuntime.Succeeded);
        Assert.Empty(partialRuntime.Value!);
        var searchableMetadata = await catalog.SearchAsync(new(SearchText: "NET10.0"));
        Assert.True(searchableMetadata.Succeeded);
        Assert.Equal(selected, Assert.Single(searchableMetadata.Value!).Template);
    }

    [Fact]
    public void Catalog_RejectsDuplicateProviderIdentityInsteadOfSelectingAnArbitraryProvider()
    {
        var first = new CatalogProvider("duplicate", []);
        var second = new CatalogProvider("duplicate", []);
        Assert.Throws<ArgumentException>(() => new DeveloperProjectCatalog([first, second]));
    }

    private sealed class TemplateProvider(bool omitReadiness = false) : IDeveloperProjectTemplateProvider
    {
        private readonly DeveloperProjectTemplate _template = new(
            "console-csharp", ".NET Console", "console", ["C#"], [".NET"], ["Windows", "9to1-OS"],
            ["Dev"], [new("dotnet-sdk-10", "C#", ".NET SDK", "10.0", null, true)], "first-party", true);
        public string ProviderId => "first-party";
        public Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeveloperProjectTemplate>>([_template]);
        public Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(
            DeveloperProjectTemplate template, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeveloperToolchainReadiness>>(omitReadiness ? [] :
                [new("dotnet-sdk-10", DeveloperToolchainState.MissingCompilerOrRuntime, ">= 10.0", null, "install-dotnet", "Install .NET SDK 10 or later.")]);
    }

    private sealed class CatalogProvider(string providerId, IReadOnlyList<DeveloperProjectTemplate> templates)
        : IDeveloperProjectTemplateProvider
    {
        public string ProviderId => providerId;
        public Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(templates);
        }
        public Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(
            DeveloperProjectTemplate template, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DeveloperToolchainReadiness>>([]);
        }
    }
}
