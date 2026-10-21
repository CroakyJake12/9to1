using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Owning coverage for the shipping request bridge: a transport request carrying an exact advertised
/// named-reasoning id reaches BOTH the plain (stream) and typed tool provider paths through
/// <see cref="INamedReasoningProvider"/>, verbatim, when the resolved provider implements the port.
/// An incapable backend refuses with typed <see cref="UnsupportedCapabilityException"/> BEFORE any
/// provider invocation; ordinary (no-explicit) requests never require the port.
/// </summary>
public sealed class NamedReasoningBridgeTests
{
    // ---- Primary ProviderRoutingModelClient (the fundamental dispatcher) ----

    [Fact]
    public async Task Primary_stream_carries_exact_reasoning_id_to_capable_provider()
    {
        var provider = new ReasoningCapturingProvider("reasoning", "m1");
        var client = new ProviderRoutingModelClient(new StubOllamaClient(), new ModelProviderRegistry(new IModelProvider[] { provider }), new StubPrivacy());

        var chunks = new List<string>();
        await foreach (var chunk in client.StreamChatAsync(
            new OllamaChatRequest("reasoning:m1", [new OllamaMessage("user", "hi")], EffortLevel.Medium)
            { ReasoningId = "high" }, CancellationToken.None))
            chunks.Add(chunk);

        Assert.Equal(["ok"], chunks);
        Assert.Equal(0, provider.OrdinaryStreamCalls);
        Assert.Equal(1, provider.ReasoningStreamCalls);
        Assert.Equal("high", provider.LastReasoningId);
        Assert.Equal("high", provider.LastChatRequest?.ReasoningId);
    }

    [Fact]
    public async Task Primary_tools_carries_exact_reasoning_id_to_capable_provider()
    {
        var provider = new ReasoningCapturingProvider("reasoning", "m1");
        var client = new ProviderRoutingModelClient(new StubOllamaClient(), new ModelProviderRegistry(new IModelProvider[] { provider }), new StubPrivacy());

        var response = await client.ChatWithToolsAsync(
            new OllamaToolRequest("reasoning:m1",
                [new OllamaToolTurn("user", "hi")],
                [new OllamaToolDefinition("a", "db", new Dictionary<string, object>(), [], null)],
                EffortLevel.Medium)
            { ReasoningId = "high" }, CancellationToken.None);

        Assert.Equal("ok", response.Content);
        Assert.Equal(0, provider.OrdinaryToolCalls);
        Assert.Equal(1, provider.ReasoningToolCalls);
        Assert.Equal("high", provider.LastReasoningId);
        Assert.Equal("high", provider.LastToolRequest?.ReasoningId);
    }

    [Fact]
    public async Task Primary_ordinary_request_never_requires_reasoning_port()
    {
        var provider = new ReasoningCapturingProvider("reasoning", "m1");
        var client = new ProviderRoutingModelClient(new StubOllamaClient(), new ModelProviderRegistry(new IModelProvider[] { provider }), new StubPrivacy());

        var chunks = new List<string>();
        await foreach (var chunk in client.StreamChatAsync(
            new OllamaChatRequest("reasoning:m1", [new OllamaMessage("user", "hi")], EffortLevel.Medium),
            CancellationToken.None))
            chunks.Add(chunk);

        Assert.Equal(["ok"], chunks);
        Assert.Equal(1, provider.OrdinaryStreamCalls);
        Assert.Equal(0, provider.ReasoningStreamCalls);
        Assert.Null(provider.LastReasoningId);
    }

    [Fact]
    public async Task Primary_incapable_backend_throws_unsupported_before_invocation()
    {
        var provider = new PlainProvider("plain", "m1");
        var client = new ProviderRoutingModelClient(new StubOllamaClient(), new ModelProviderRegistry(new IModelProvider[] { provider }), new StubPrivacy());

        var ex = await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in client.StreamChatAsync(
                new OllamaChatRequest("plain:m1", [new OllamaMessage("user", "hi")], EffortLevel.Medium)
                { ReasoningId = "high" }, CancellationToken.None)) { }
        });

        Assert.Equal("high", ex.Target);
        Assert.Equal(0, provider.InvokedCount);
    }

    // ---- ResilientProviderRoutingModelClient (the shipping consumer) ----

    [Fact]
    public async Task Resilient_stream_carries_exact_reasoning_id_to_capable_provider()
    {
        var provider = new ReasoningCapturingProvider("reasoning", "m1");
        var client = CreateResilient(provider);

        var chunks = new List<string>();
        await foreach (var chunk in client.StreamChatAsync(
            new OllamaChatRequest("reasoning:m1", [new OllamaMessage("user", "hi")], EffortLevel.Medium)
            { ReasoningId = "low" }, CancellationToken.None))
            chunks.Add(chunk);

        Assert.Contains("ok", chunks);
        Assert.Equal(0, provider.OrdinaryStreamCalls);
        Assert.Equal(1, provider.ReasoningStreamCalls);
        Assert.Equal("low", provider.LastReasoningId);
        Assert.Equal("low", provider.LastChatRequest?.ReasoningId);
    }

    [Fact]
    public async Task Resilient_tools_carries_exact_reasoning_id_to_capable_provider()
    {
        var provider = new ReasoningCapturingProvider("reasoning", "m1");
        var client = CreateResilient(provider);

        var response = await client.ChatWithToolsAsync(
            new OllamaToolRequest("reasoning:m1",
                [new OllamaToolTurn("user", "hi")],
                [new OllamaToolDefinition("a", "db", new Dictionary<string, object>(), [], null)],
                EffortLevel.Medium)
            { ReasoningId = "medium" }, CancellationToken.None);

        Assert.Equal("ok", response.Content);
        Assert.Equal(0, provider.OrdinaryToolCalls);
        Assert.Equal(1, provider.ReasoningToolCalls);
        Assert.Equal("medium", provider.LastReasoningId);
        Assert.Equal("medium", provider.LastToolRequest?.ReasoningId);
    }

    [Fact]
    public async Task Resilient_incapable_backend_throws_unsupported_before_invocation()
    {
        var provider = new PlainProvider("plain", "m1");
        var client = CreateResilient(provider);

        var ex = await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in client.StreamChatAsync(
                new OllamaChatRequest("plain:m1", [new OllamaMessage("user", "hi")], EffortLevel.Medium)
                { ReasoningId = "high" }, CancellationToken.None)) { }
        });

        Assert.Equal("high", ex.Target);
        Assert.Equal(0, provider.InvokedCount);
    }

    private static ResilientProviderRoutingModelClient CreateResilient(IModelProvider provider)
    {
        var registry = new ModelProviderRegistry(new IModelProvider[] { provider });
        var primary = new ProviderRoutingModelClient(new StubOllamaClient(), registry, new StubPrivacy());
        var configurations = new StubConfigurationStore(provider.Id);
        return new ResilientProviderRoutingModelClient(primary, registry, configurations, new StubPrivacy());
    }

    private sealed class ReasoningCapturingProvider(string id, string modelName)
        : IModelProvider, INamedReasoningProvider
    {
        public int OrdinaryStreamCalls { get; private set; }
        public int ReasoningStreamCalls { get; private set; }
        public int OrdinaryToolCalls { get; private set; }
        public int ReasoningToolCalls { get; private set; }
        public OllamaChatRequest? LastChatRequest { get; private set; }
        public OllamaToolRequest? LastToolRequest { get; private set; }
        public string? LastReasoningId { get; private set; }

        public string Id => id;
        public string DisplayName => id;
        public bool IsLocal => true;
        public bool CanManageModels => false;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public ProviderModelDescriptor Model { get; } = new(id, true, new ModelDescriptor(
            modelName, 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools },
            DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));

        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { OrdinaryStreamCalls++; LastChatRequest = request; await Task.Yield(); yield return "ok"; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => Task.FromResult("ok");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        { OrdinaryToolCalls++; LastToolRequest = request; return Task.FromResult(new OllamaToolResponse("ok", [])); }

        public async IAsyncEnumerable<string> StreamChatWithReasoningAsync(OllamaChatRequest request, string reasoningId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            ReasoningStreamCalls++; LastChatRequest = request; LastReasoningId = reasoningId;
            token.ThrowIfCancellationRequested(); await Task.Yield(); yield return "ok";
        }
        public Task<OllamaToolResponse> ChatWithToolsWithReasoningAsync(OllamaToolRequest request, string reasoningId, CancellationToken token)
        { ReasoningToolCalls++; LastToolRequest = request; LastReasoningId = reasoningId; return Task.FromResult(new OllamaToolResponse("ok", [])); }
    }

    /// <summary>An incapable backend: does NOT implement the named-reasoning port; counts any
    /// generation invocation so a test can prove none occurred before the typed refusal.</summary>
    private sealed class PlainProvider(string id, string modelName) : IModelProvider
    {
        public int InvokedCount { get; private set; }
        public string Id => id;
        public string DisplayName => id;
        public bool IsLocal => true;
        public bool CanManageModels => false;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public ProviderModelDescriptor Model { get; } = new(id, true, new ModelDescriptor(
            modelName, 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools },
            DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));

        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) { InvokedCount++; throw new NotSupportedException(); }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) { InvokedCount++; throw new NotSupportedException(); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) { InvokedCount++; throw new NotSupportedException(); }
    }

    private sealed class StubPrivacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken)
        { Current = preferences; return Task.CompletedTask; }
    }

    private sealed class StubConfigurationStore(string providerId) : IProviderConfigurationStore
    {
        private readonly ProviderConfiguration _configuration = new(
            providerId, ModelProviderKind.Ollama, providerId, "http://127.0.0.1:11434/",
            true, true, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ProviderConfiguration>>([_configuration]);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) =>
            Task.FromResult<ProviderConfiguration?>(id.Equals(providerId, StringComparison.OrdinalIgnoreCase) ? _configuration : null);
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class StubOllamaClient : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(false);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteModelAsync(string model, CancellationToken token) => throw new NotSupportedException();
    }
}
