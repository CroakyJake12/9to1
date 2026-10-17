using Haven.Application;
using Haven.Core;
using HavenOS.Home.Discover;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeRegisteredModelCatalogueSourceTests
{
    [Fact]
    public async Task Registered_provider_metadata_is_preserved_without_inventing_install_or_voice_support()
    {
        var provider = new Provider("local", true)
        {
            Models = [new("local", true, new("model", 2048, "family", "", "Q4", new HashSet<ToolCapability>
                { ToolCapability.Text, ToolCapability.Vision, ToolCapability.AudioInput }, DateTimeOffset.UtcNow), 8192, "Friendly name")]
        };
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]), "local", () => false);
        var result = await source.GetSnapshotAsync(CancellationToken.None);
        var model = Assert.Single(result.Models);
        Assert.Equal("local", model.Identity.ProviderId);
        Assert.Equal("model", model.Identity.ModelId);
        Assert.Equal("Friendly name", model.DisplayName);
        Assert.Equal("family", model.Architecture);
        Assert.Null(model.ParameterInformation);
        Assert.Equal(8192, model.ContextLimit);
        Assert.Equal(HomeDiscoverLocality.Local, model.Locality);
        Assert.Equal(HomeDiscoverInstallState.Unknown, model.InstallState);
        Assert.Contains(HomeDiscoverCategory.Chat, model.Categories!);
        Assert.Contains(HomeDiscoverCategory.Audio, model.Categories!);
        Assert.DoesNotContain(HomeDiscoverCategory.Image, model.Categories!);
        Assert.DoesNotContain(HomeDiscoverCategory.Voice, model.Categories!);
        Assert.Null(model.Voice);
        Assert.Empty(model.SupportedActions);
        Assert.Equal(result.Revision, (await source.GetSnapshotAsync(CancellationToken.None)).Revision);
    }

    [Fact]
    public async Task Local_only_policy_prevents_remote_catalogue_access()
    {
        var provider = new Provider("cloud", false);
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]), "cloud", () => false);
        var result = await source.GetSnapshotAsync(CancellationToken.None);
        Assert.Equal(0, provider.Reads);
        Assert.Equal(HomeDiscoverSourceState.Unavailable, result.State);
        Assert.Equal("Discover.LocalOnly", result.ErrorCode);
    }

    [Fact]
    public async Task Privacy_revocation_during_provider_read_redacts_the_result()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider("cloud", false) { Read = _ => completion.Task };
        var remoteAllowed = true;
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]), "cloud", () => remoteAllowed);
        var pending = source.GetSnapshotAsync(CancellationToken.None);
        remoteAllowed = false;
        completion.SetResult([Model("cloud", false)]);
        var result = await pending;
        Assert.Empty(result.Models);
        Assert.Equal("Discover.LocalOnly", result.ErrorCode);
    }

    [Fact]
    public async Task Provider_failure_is_unavailable_instead_of_an_empty_successful_catalogue()
    {
        var provider = new Provider("cloud", false) { Read = _ => Task.FromException<IReadOnlyList<ProviderModelDescriptor>>(new IOException("Offline")) };
        var catalog = new HomeDiscoverCatalog([new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]), "cloud", () => true)]);
        var page = await catalog.SearchAsync(new HomeDiscoverQuery());
        Assert.Equal(HomeDiscoverSourceState.Unavailable, page.State);
        Assert.Equal(HomeDiscoverSourceState.Failed, Assert.Single(page.Providers).State);
    }

    [Fact]
    public async Task Cancellation_ignored_by_provider_cannot_publish_catalogue_data()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ProviderModelDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider("local", true) { Read = _ => completion.Task };
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]), "local", () => true);
        using var cancellation = new CancellationTokenSource();
        var pending = source.GetSnapshotAsync(cancellation.Token);
        cancellation.Cancel();
        completion.SetResult([Model("local", true)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static ProviderModelDescriptor Model(string id, bool local) =>
        new(id, local, new("same-name", 0, "", "", "", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow));

    private sealed class Provider(string id, bool local) : IModelProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public ModelProviderKind Kind => local ? ModelProviderKind.Ollama : ModelProviderKind.OpenAICompatible;
        public bool IsLocal => local;
        public bool CanManageModels => true;
        public IReadOnlyList<ProviderModelDescriptor> Models { get; init; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<ProviderModelDescriptor>>>? Read { get; init; }
        public int Reads { get; private set; }
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        { Reads++; return Read?.Invoke(cancellationToken) ?? Task.FromResult(Models); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
