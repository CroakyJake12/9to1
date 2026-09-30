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
    [Fact]
    public async Task Legacy_catalogue_overload_preserves_real_locality_and_cancellation()
    {
        IModelProviderRegistry registry = new ModelProviderRegistry([new Provider("remote", false)]);
        Assert.False(Assert.Single(await registry.GetModelsAsync(default)).IsLocal);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.GetModelsAsync(cancelled.Token));
        IModelProviderRegistry empty = new ModelProviderRegistry([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => empty.GetModelsAsync(new ModelCataloguePolicy(), cancelled.Token));
    }
    [Fact]
    public void Competing_provider_identities_cannot_silently_replace_the_registered_owner()
    {
        var local = new Provider("same", true);
        Assert.Throws<InvalidOperationException>(() => new ModelProviderRegistry([local, new Provider("SAME", false)]));
        Assert.Single(new ModelProviderRegistry([local, local]).Providers);
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
