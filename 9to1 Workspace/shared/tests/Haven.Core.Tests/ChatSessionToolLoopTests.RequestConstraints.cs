using System.Net;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Core.Tests;

public sealed partial class ChatSessionToolLoopTests
{
    [Fact]
    public async Task Constrained_local_Send_never_discovers_or_dispatches_remote_provider()
    {
        var local = new RequestedCounterProvider("ollama", "local", true, [ToolCapability.Text, ToolCapability.Streaming]);
        var remote = new RequestedCounterProvider("remote", "remote", false, [ToolCapability.Text, ToolCapability.Streaming]);
        var selected = local.Descriptor;
        var events = await SendRequestedAsync([local, remote], selected, new(false, false));
        Assert.Single(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(1, local.DiscoveryCalls);
        Assert.Equal(1, local.StreamCalls);
        Assert.Equal(0, remote.DiscoveryCalls + remote.StreamCalls + remote.CompletionCalls + remote.ToolCalls);
        Assert.Equal(0, local.HealthCalls + remote.HealthCalls);
    }

    [Fact]
    public async Task Selected_only_Send_does_not_replace_missing_vision_with_inventory_model()
    {
        var selectedProvider = new RequestedCounterProvider("ollama", "text", true, [ToolCapability.Text, ToolCapability.Streaming]);
        var other = new RequestedCounterProvider("other", "vision", true, [ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Vision]);
        var selected = selectedProvider.Descriptor;
        var events = await SendRequestedAsync([selectedProvider, other], selected, new(null, false), ["image-test"]);
        Assert.Single(events, item => item.Kind == ChatStreamEventKind.PreflightFailed);
        Assert.DoesNotContain(events, item => item.Kind == ChatStreamEventKind.AssistantStarted);
        Assert.Equal(0, selectedProvider.DiscoveryCalls + selectedProvider.StreamCalls);
        Assert.Equal(0, other.DiscoveryCalls + other.StreamCalls);
    }

    [Fact]
    public async Task Constrained_fallback_stays_in_actual_router_and_never_discovers_remote()
    {
        var first = new RequestedCounterProvider("ollama", "first", true, [ToolCapability.Text, ToolCapability.Streaming], fail: true);
        var alternate = new RequestedCounterProvider("alternate", "second", true, [ToolCapability.Text, ToolCapability.Streaming]);
        var remote = new RequestedCounterProvider("remote", "cloud", false, [ToolCapability.Text, ToolCapability.Streaming]);
        var selected = first.Descriptor;
        var events = await SendRequestedAsync([first, alternate, remote], selected, new(false, true));
        Assert.Single(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(1, first.StreamCalls);
        Assert.Equal(1, alternate.StreamCalls);
        Assert.Equal(1, first.DiscoveryCalls);
        Assert.Equal(1, alternate.DiscoveryCalls);
        Assert.Equal(0, remote.DiscoveryCalls + remote.StreamCalls + remote.CompletionCalls + remote.ToolCalls);
        Assert.Equal(0, first.HealthCalls + alternate.HealthCalls + remote.HealthCalls);
    }

    [Fact]
    public async Task Unconstrained_Send_preserves_existing_compatibility_model_selection()
    {
        var first = new RequestedCounterProvider("ollama", "text", true, [ToolCapability.Text, ToolCapability.Streaming]);
        var alternate = new RequestedCounterProvider("alternate", "vision", true, [ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Vision]);
        var selected = first.Descriptor;
        var events = await SendRequestedAsync([first, alternate], selected, null, ["image-test"]);
        Assert.Single(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(0, first.StreamCalls);
        Assert.Equal(1, alternate.StreamCalls);
        Assert.True(first.DiscoveryCalls >= 1);
        Assert.True(alternate.DiscoveryCalls >= 1);
    }

    private async Task<List<ChatStreamEvent>> SendRequestedAsync(RequestedCounterProvider[] providers,
        ProviderModelDescriptor actualSelected, ModelRequestRoutingConstraints? constraints, IReadOnlyList<string>? images = null)
    {
        var registry = new ModelProviderRegistry(providers);
        var privacy = new RequestedPrivacy();
        var configurations = new RequestedConfigurations(providers);
        var primary = new ProviderRoutingModelClient(new FakeOllama(actualSelected.Model), registry, privacy);
        var selected = primary.ToCompatibilityDescriptor(actualSelected);
        var router = new ResilientProviderRoutingModelClient(primary, registry, configurations, privacy);
        var service = new ChatSessionService(new FakeConversations(), router, new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "policy", null, null, false, true, now, now);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(conversation, "hello", selected, EffortLevel.Medium,
            [], "Assistant", "", DuoMode.Solo, null, null, null, images, CancellationToken.None,
            generationOptions: constraints is null ? null : new GenerationOptions { RequestedRoutingConstraints = constraints }))
            events.Add(item);
        return events;
    }

    private sealed class RequestedPrivacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken token)
        { Current = preferences; return Task.CompletedTask; }
    }
    private sealed class RequestedConfigurations(RequestedCounterProvider[] providers) : IProviderConfigurationStore
    {
        private readonly IReadOnlyDictionary<string, ProviderConfiguration> _values = providers.ToDictionary(provider => provider.Id,
            provider => new ProviderConfiguration(provider.Id, provider.Kind, provider.Id, "https://example.test/",
                true, provider.IsLocal, true, new Dictionary<string, string>(), DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(_values.Values.ToArray());
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult(_values.GetValueOrDefault(id));
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class RequestedCounterProvider : IModelProvider
    {
        private readonly bool _fail;
        public RequestedCounterProvider(string id, string model, bool local, ToolCapability[] capabilities, bool fail = false)
        {
            Id = id; IsLocal = local; _fail = fail;
            Descriptor = new ProviderModelDescriptor(id, local,
                new ModelDescriptor(model, 1, "test", "", "", capabilities.ToHashSet(), DateTimeOffset.UtcNow), 32768);
        }
        public ProviderModelDescriptor Descriptor { get; }
        public string Id { get; }
        public string DisplayName => Id;
        public ModelProviderKind Kind => Id == "ollama" ? ModelProviderKind.Ollama : ModelProviderKind.OpenAICompatible;
        public bool IsLocal { get; }
        public bool CanManageModels => false;
        public int DiscoveryCalls { get; private set; }
        public int StreamCalls { get; private set; }
        public int CompletionCalls { get; private set; }
        public int ToolCalls { get; private set; }
        public int HealthCalls { get; private set; }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token)
        { HealthCalls++; return Task.FromResult(new ProviderHealthStatus(Id, true, "actual test host", TimeSpan.Zero, DateTimeOffset.UtcNow)); }
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { DiscoveryCalls++; return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Descriptor]); }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            StreamCalls++; await Task.Yield(); token.ThrowIfCancellationRequested();
            if (_fail) throw new HttpRequestException("actual selected route failure", null, HttpStatusCode.ServiceUnavailable);
            yield return Id + " response";
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { CompletionCalls++; return Task.FromResult(Id + " response"); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        { ToolCalls++; return Task.FromResult(new OllamaToolResponse(Id + " response", [])); }
    }
}
