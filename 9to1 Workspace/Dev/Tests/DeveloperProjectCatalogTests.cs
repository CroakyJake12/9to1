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
}
