using System.Security.Cryptography;
using System.Text;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperProjectTemplateMaterializerTests
{
    [Fact]
    public async Task Prepare_ProducesStableBoundedFilesAndUtf8Digests()
    {
        var provider = new ContentProvider([
            new("Views/Home.cui", "<Cui version=\"1\">👋</Cui>"),
            new("README.md", "Project notes")]);
        var result = await new DeveloperProjectTemplateMaterializer([provider])
            .PrepareAsync(ReadyOption(), "Sample App");

        Assert.True(result.Succeeded);
        var materialization = result.Value!;
        Assert.Equal("Sample App", materialization.ProjectName);
        Assert.Equal(new[] { "README.md", "Views/Home.cui" }, materialization.Files.Select(file => file.RelativePath));
        var emojiFile = materialization.Files.Single(file => file.RelativePath == "Views/Home.cui");
        Assert.Equal(Encoding.UTF8.GetByteCount("<Cui version=\"1\">👋</Cui>"), emojiFile.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(emojiFile.Text))).ToLowerInvariant(), emojiFile.ContentSha256);
        Assert.Equal(materialization.Files.Sum(file => file.SizeBytes), materialization.TotalBytes);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Prepare_DoesNotTrustForgedCanCreateWhenRequiredToolchainIsMissing()
    {
        var provider = new ContentProvider([new("main.cui", "<Cui version=\"1\" />")],
            DeveloperToolchainState.MissingCompilerOrRuntime);
        var option = ReadyOption();

        var result = await new DeveloperProjectTemplateMaterializer([provider]).PrepareAsync(option, "Sample");

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.ToolchainMissing, result.Error!.Code);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData("../outside.cui", "safe")]
    [InlineData("safe\\nested.cui", "safe")]
    [InlineData("a", "a/child.cui")]
    [InlineData("Readme.md", "README.md")]
    public async Task Prepare_RejectsTraversalCollisionsAndCaseInsensitiveAliases(string first, string second)
    {
        var provider = new ContentProvider([new(first, "one"), new(second, "two")]);
        var result = await new DeveloperProjectTemplateMaterializer([provider]).PrepareAsync(ReadyOption(), "Sample");

        Assert.False(result.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, result.Error!.Code);
    }

    [Fact]
    public async Task Prepare_RejectsAmbiguousProviderAndInvalidProjectNameBeforeReadingContent()
    {
        var provider = new ContentProvider([new("main.cui", "<Cui version=\"1\" />")]);
        var materializer = new DeveloperProjectTemplateMaterializer([provider, provider]);
        var ambiguousProvider = await materializer.PrepareAsync(ReadyOption(), "Sample");
        var invalidName = await new DeveloperProjectTemplateMaterializer([provider]).PrepareAsync(ReadyOption(), "../Sample");

        Assert.False(ambiguousProvider.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.CapabilityUnavailable, ambiguousProvider.Error!.Code);
        Assert.False(invalidName.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, invalidName.Error!.Code);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Prepare_EnforcesPerFileAndTotalByteLimits()
    {
        var tooLargeFile = new ContentProvider([new("large.cui", new string('x', DeveloperProjectTemplateMaterializer.MaximumFileBytes + 1))]);
        var oversizedBundle = new ContentProvider(Enumerable.Range(0, 5)
            .Select(index => new DeveloperProjectTemplateFile($"folder{index}/source.cui", new string('x', 4 * 1024 * 1024)))
            .ToArray());
        var materializer = new DeveloperProjectTemplateMaterializer([tooLargeFile]);
        var fileResult = await materializer.PrepareAsync(ReadyOption(), "Sample");
        var totalResult = await new DeveloperProjectTemplateMaterializer([oversizedBundle]).PrepareAsync(ReadyOption(), "Sample");

        Assert.False(fileResult.Succeeded);
        Assert.False(totalResult.Succeeded);
        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, fileResult.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, totalResult.Error!.Code);
    }

    [Fact]
    public async Task FirstPartyCuiProjectTemplate_IsDiscoverableAndPreparesDirectRootSourceFiles()
    {
        var provider = new DeveloperCuiProjectTemplateProvider();
        var catalog = new DeveloperProjectCatalog([provider]);
        var options = await catalog.SearchAsync(new(ProductSurface: "Dev", ToolchainsReady: true));
        var option = Assert.Single(options.Value!);
        var materializer = new DeveloperProjectTemplateMaterializer([provider]);

        var result = await materializer.PrepareAsync(option, "Sample Project");

        Assert.True(result.Succeeded);
        Assert.True(materializer.IsIssued(result.Value!));
        Assert.Equal("cui-page-project", result.Value!.TemplateId);
        Assert.Equal(new[] { "Main.cui", "README.md" }, result.Value.Files.Select(file => file.RelativePath));
        Assert.Contains("<Cui version=\"1\">", result.Value.Files.Single(file => file.RelativePath == "Main.cui").Text);
    }

    private static DeveloperProjectTemplateOption ReadyOption() => new(Template(),
        [new("sdk", DeveloperToolchainState.Ready, "1", "1", null, null)]);

    private static DeveloperProjectTemplate Template() => new("cui-app", "CUI App", "cui", ["CUI"], [],
        ["9to1-OS"], ["Dev"], [new("sdk", "CUI", "CUI runtime", "1", null, true)], "first-party", true);

    private sealed class ContentProvider(IReadOnlyList<DeveloperProjectTemplateFile> files,
        DeveloperToolchainState readinessState = DeveloperToolchainState.Ready)
        : IDeveloperProjectTemplateContentProvider
    {
        public string ProviderId => "first-party";
        public int Calls { get; private set; }
        public Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DeveloperProjectTemplate>>([Template()]);
        }
        public Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(
            DeveloperProjectTemplate template, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DeveloperToolchainReadiness>>([
                new("sdk", readinessState, "1", readinessState == DeveloperToolchainState.Ready ? "1" : null, null, null)]);
        }
        public Task<IReadOnlyList<DeveloperProjectTemplateFile>> GetFilesAsync(
            DeveloperProjectTemplate template, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(files);
        }
    }
}
