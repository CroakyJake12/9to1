using System.Net;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Owning coverage for the routing-level reasoning-target guard: an explicit named-reasoning selection
/// is bound to a genuine provider/model identity and is never silently forwarded to a different fallback
/// provider/model (A→B zero delivery). A direct route whose actual target matches the binding still
/// forwards to a capable provider.
/// </summary>
public sealed class ResilientRoutingReasoningGuardTests
{
    [Fact]
    public async Task Fallback_never_carries_model_A_reasoning_id_to_model_B()
    {
        var a = new ReasoningProvider("a", "m1", failStream: true);
        var b = new ReasoningProvider("b", "m1");
        var registry = new ModelProviderRegistry(new IModelProvider[] { a, b });
        var primary = new ProviderRoutingModelClient(new StubOllamaClient(), registry, new StubPrivacy());
        var configurations = new StubConfigurationStore("a", "b");
        var fallbackOrder = new StubFallbackOrder(["b:m1"]);
        var client = new ResilientProviderRoutingModelClient(primary, registry, configurations, new StubPrivacy(), fallbackOrder);

        var ex = await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in client.StreamChatAsync(
                new OllamaChatRequest("a:m1", [new OllamaMessage("user", "hi")], EffortLevel.Medium)
                {
                    ReasoningId = "high",
                    ReasoningTargetProviderId = "a",
                    ReasoningTargetModelName = "m1"
                }, CancellationToken.None)) { }
        });

        Assert.Equal("high", ex.Target);
        // Fallback coverage requires A to have REALLY run (and failed) before B is ever considered:
        // A's reasoning port was entered with A's bound ID, then A failed.
        Assert.True(a.ReasoningStreamCalls >= 1, "A should have run before failing to count as fallback coverage.");
        Assert.Equal("high", a.LastReasoningId);
        // Provider B must never be invoked with A's reasoning id (zero A-bound delivery to B).
        Assert.Equal(0, b.ReasoningStreamCalls);
        Assert.Null(b.LastChatRequest);
    }

    [Fact]
    public async Task Tool_fallback_never_carries_model_A_reasoning_id_to_model_B()
    {
        var a = new ReasoningProvider("a", "m1", failTools: true);
        var b = new ReasoningProvider("b", "m1");
        var registry = new ModelProviderRegistry(new IModelProvider[] { a, b });
        var primary = new ProviderRoutingModelClient(new StubOllamaClient(), registry, new StubPrivacy());
        var configurations = new StubConfigurationStore("a", "b");
        var fallbackOrder = new StubFallbackOrder(["b:m1"]);
        var client = new ResilientProviderRoutingModelClient(primary, registry, configurations, new StubPrivacy(), fallbackOrder);

        var ex = await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await client.ChatWithToolsAsync(
                new OllamaToolRequest("a:m1",
                    [new OllamaToolTurn("user", "hi")],
                    [new OllamaToolDefinition("x", "x", new Dictionary<string, object>(), [], null)],
                    EffortLevel.Medium)
                {
                    ReasoningId = "medium",
                    ReasoningTargetProviderId = "a",
                    ReasoningTargetModelName = "m1"
                }, CancellationToken.None);
        });

        Assert.Equal("medium", ex.Target);
        // A's tool reasoning port really ran (and failed) before B is ever considered.
        Assert.True(a.ReasoningToolCalls >= 1, "A should have run before failing to count as fallback coverage.");
        Assert.Equal("medium", a.LastReasoningId);
        // B received zero A-bound ID delivery.
        Assert.Equal(0, b.ReasoningToolCalls);
        Assert.Null(b.LastToolRequest);
    }

    [Fact]
    public async Task Direct_routing_with_matching_binding_is_forwarded_to_capable_provider()
    {
        var a = new ReasoningProvider("a", "m1");
        var registry = new ModelProviderRegistry(new IModelProvider[] { a });
        var primary = new ProviderRoutingModelClient(new StubOllamaClient(), registry, new StubPrivacy());
        var configurations = new StubConfigurationStore("a");
        var client = new ResilientProviderRoutingModelClient(primary, registry, configurations, new StubPrivacy());

        var chunks = new List<string>();
        await foreach (var chunk in client.StreamChatAsync(
            new OllamaChatRequest("a:m1", [new OllamaMessage("user", "hi")], EffortLevel.Medium)
            {
                ReasoningId = "low",
                ReasoningTargetProviderId = "a",
                ReasoningTargetModelName = "m1"
            }, CancellationToken.None))
            chunks.Add(chunk);

        Assert.Contains("ok", chunks);
        Assert.Equal(1, a.ReasoningStreamCalls);
        Assert.Equal("low", a.LastReasoningId);
    }

    private sealed class StubFallbackOrder(IReadOnlyList<string> order) : IModelFallbackOrderStore
    {
        public Task<IReadOnlyList<string>> GetOrderAsync(CancellationToken token) => Task.FromResult(order);
        public Task SetOrderAsync(IReadOnlyList<string> orderedModelKeys, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class ReasoningProvider(string id, string modelName, bool failStream = false, bool failTools = false)
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

        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            OrdinaryStreamCalls++;
            if (failStream) throw new HttpRequestException("provider failed");
            LastChatRequest = request;
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            yield return "ok";
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => Task.FromResult("ok");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        {
            OrdinaryToolCalls++;
            if (failTools) throw new HttpRequestException("provider failed");
            LastToolRequest = request;
            return Task.FromResult(new OllamaToolResponse("ok", []));
        }

        public async IAsyncEnumerable<string> StreamChatWithReasoningAsync(OllamaChatRequest request, string reasoningId, [EnumeratorCancellation] CancellationToken token)
        {
            ReasoningStreamCalls++;
            LastChatRequest = request;
            LastReasoningId = reasoningId;
            if (failStream) throw new HttpRequestException("provider failed");
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return "ok";
        }
        public Task<OllamaToolResponse> ChatWithToolsWithReasoningAsync(OllamaToolRequest request, string reasoningId, CancellationToken token)
        {
            ReasoningToolCalls++;
            LastToolRequest = request;
            LastReasoningId = reasoningId;
            if (failTools) throw new HttpRequestException("provider failed");
            return Task.FromResult(new OllamaToolResponse("ok", []));
        }
    }

    private sealed class StubPrivacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token)
        { Current = value; return Task.CompletedTask; }
    }

    private sealed class StubConfigurationStore(params string[] providerIds) : IProviderConfigurationStore
    {
        private readonly List<ProviderConfiguration> _configs = providerIds
            .Select(pid => new ProviderConfiguration(pid, ModelProviderKind.Ollama, pid, "http://127.0.0.1:11434/",
                true, true, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow))
            .ToList();
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ProviderConfiguration>>(_configs);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) =>
            Task.FromResult(_configs.FirstOrDefault(item => item.Id == id));
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
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
