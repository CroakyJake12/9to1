using Haven.Application;

namespace Haven.Core.Tests;

public sealed class PolicyAwareModelCatalogueTests
{
    [Fact]
    public async Task Local_only_discovery_never_calls_remote_provider_and_allowlist_precedes_network()
    {
        var local = new Provider("local", true); var remote = new Provider("remote", false);
        IModelProviderRegistry registry = new ModelProviderRegistry([local, remote]);
        var result = await registry.GetModelsAsync(new ModelCataloguePolicy(AllowRemote: false), default);
        Assert.Single(result);
        Assert.Equal(1, local.Calls); Assert.Equal(0, remote.Calls);
        result = await registry.GetModelsAsync(new ModelCataloguePolicy(AllowedProviderIds: new HashSet<string> { "remote" }), default);
        Assert.Single(result);
        Assert.False(result[0].IsLocal); // A remote backend's inaccurate model label cannot invent locality.
        Assert.Equal(1, local.Calls); Assert.Equal(1, remote.Calls);
    }
    private sealed class Provider(string id, bool local) : IModelProvider
    {
        public int Calls;
        public string Id => id;
        public string DisplayName => id;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => local;
        public bool CanManageModels => false;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new(id, true,
                new("model", 0, "", "", "", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow))]);
        }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}
