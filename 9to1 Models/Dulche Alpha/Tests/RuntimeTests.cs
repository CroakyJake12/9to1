using System.Runtime.CompilerServices;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Dulche.Runtime.Translate;
using Xunit;

namespace Dulche.Runtime.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public void CanonicalLanguageResolutionDoesNotGuessUnknownOrAutoLanguage()
    {
        var resolver = new CanonicalLanguageResolver();
        Assert.Equal(LanguageResolutionState.Resolved, resolver.Resolve("en").State);
        Assert.Equal("en", resolver.Resolve("en").Language!.Code);
        Assert.Equal(LanguageResolutionState.Unsupported, resolver.Resolve("%%%").State);
        Assert.Equal(LanguageResolutionState.Invalid, resolver.Resolve("auto").State);
        Assert.Equal(LanguageResolutionState.Resolved, resolver.Resolve("auto", allowAutoDetect: true).State);
    }

    [Fact]
    public void MissingTranslationVoicePairEvidenceRemainsUnknown()
    {
        var catalog = new UnknownTranslationCapabilityCatalog();
        Assert.Equal(TranslationCapabilityState.Unknown, catalog.GetState("provider", "en", "fr", TranslationMediaKind.Audio));
    }

    [Fact]
    public async Task EndpointSessionStreamingAndMetricsAreScoped()
    {
        var adapter = new FakeAdapter();
        var runtime = new DulcheRuntime([adapter]);
        var endpoint = await runtime.StartLocalAsync(adapter.ProviderId, 9477, new("fake", "model-a"));
        Assert.True(endpoint.Succeeded);
        Assert.Equal(EndpointState.Ready, endpoint.Value!.State);
        var firstSession = runtime.CreateSession(endpoint.Value.EndpointId).Value!;
        var secondSession = runtime.CreateSession(endpoint.Value.EndpointId).Value!;
        var request = runtime.PromptStream(new("hello", SessionId: firstSession.SessionId), endpoint.Value.EndpointId);
        Assert.True(request.Succeeded);
        var response = await request.Value!.AwaitResult(CancellationToken.None);
        Assert.Equal(RequestState.Completed, response.Status);
        Assert.Equal("reply:hello", response.Text);
        Assert.Equal(firstSession.SessionId, response.SessionId);
        Assert.True(response.Tokens.Input.Availability == Availability.Value);
        Assert.Equal(1L, response.Tokens.Input.Value);
        Assert.Equal(2L, response.Tokens.Output.Value);
        Assert.Equal(firstSession.SessionId, runtime.GetSession(firstSession.SessionId).Value!.SessionId);
        Assert.Empty(runtime.GetSession(secondSession.SessionId).Value!.Messages);
        Assert.Contains(response.FullLog, item => item.Type == "TextDelta");
    }

    [Fact]
    public async Task RequestsOnOneEndpointAreSerializedAndStopIsIdempotent()
    {
        var adapter = new FakeAdapter(delayMilliseconds: 60);
        var runtime = new DulcheRuntime([adapter]);
        var endpoint = (await runtime.StartLocalAsync(adapter.ProviderId, 9478)).Value!;
        var one = runtime.Submit(new("first"), endpoint.EndpointId).Value!;
        var two = runtime.Submit(new("second"), endpoint.EndpointId).Value!;
        var result = await Task.WhenAll(one.AwaitResult(CancellationToken.None), two.AwaitResult(CancellationToken.None));
        Assert.All(result, item => Assert.Equal(RequestState.Completed, item.Status));
        Assert.Equal(1, adapter.MaximumConcurrent);
        Assert.True((await runtime.StopResponseAsync(one.RequestId)).Succeeded);
        Assert.True((await runtime.StopResponseAsync(one.RequestId)).Succeeded);
    }

    [Fact]
    public async Task ReplaceKeepsLogicalRequestIdAndCreatesNewAttempt()
    {
        var adapter = new FakeAdapter(delayMilliseconds: 90);
        var runtime = new DulcheRuntime([adapter]);
        var endpoint = (await runtime.StartLocalAsync(adapter.ProviderId, 9479)).Value!;
        var request = runtime.Submit(new("old"), endpoint.EndpointId).Value!;
        await adapter.FirstGenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var replacement = await runtime.ReplacePromptAsync(request.RequestId, "new");
        Assert.True(replacement.Succeeded);
        Assert.Equal(request.RequestId, replacement.Value!.RequestId);
        var result = await replacement.Value.AwaitResult(CancellationToken.None);
        Assert.Equal(2, result.Revision);
        Assert.Equal("reply:new", result.Text);
        Assert.Contains(result.FullLog, item => item.Type == "Replaced");
    }

    [Fact]
    public async Task SettingsAreValidatedAndSnapshottedForQueuedWork()
    {
        var adapter = new FakeAdapter(delayMilliseconds: 70);
        var runtime = new DulcheRuntime([adapter]);
        var endpoint = (await runtime.StartLocalAsync(adapter.ProviderId, 9480)).Value!;
        Assert.Equal(DulcheErrorCode.InvalidArgument, runtime.SetFutureSettings(endpoint.EndpointId, new(Temperature: 3)).Error!.Code);
        var stops = new List<string> { "END" };
        Assert.True(runtime.SetFutureSettings(endpoint.EndpointId, new(Temperature: .4, StopSequences: stops)).Succeeded);
        var handle = runtime.Submit(new("queued"), endpoint.EndpointId).Value!;
        stops.Add("LATER");
        Assert.Equal("END", runtime.InspectRequestSettings(handle.RequestId).Value!.StopSequences!.Single());
        await handle.AwaitResult(CancellationToken.None);
    }

    [Fact]
    public void ReasoningMappingAndMetricJsonPreserveNaNullAndZero()
    {
        var levels = new[] { "low", "medium", "high", "max", "ultra" };
        Assert.Equal("low", ReasoningLevelMapper.FromPercentage(0, levels).Value);
        Assert.Equal("medium", ReasoningLevelMapper.FromPercentage(20, levels).Value);
        Assert.Equal("ultra", ReasoningLevelMapper.FromPercentage(100, levels).Value);
        Assert.Equal(DulcheErrorCode.InvalidArgument, ReasoningLevelMapper.FromPercentage(101, levels).Error!.Code);
        var options = new JsonSerializerOptions(); options.Converters.Add(new MetricJsonConverterFactory());
        Assert.Equal("0", JsonSerializer.Serialize(Metric<long>.Measured(0), options));
        Assert.Equal("null", JsonSerializer.Serialize(Metric<long>.Empty, options));
        Assert.Equal("\"Na\"", JsonSerializer.Serialize(Metric<long>.Na, options));
    }

    [Fact]
    public async Task RouteFiltersCapabilitiesAndCloudPolicyBeforePriority()
    {
        var local = Descriptor("local", true, new HashSet<ToolCapability>());
        var cloud = Descriptor("cloud", false, new HashSet<ToolCapability> { ToolCapability.WebSearch });
        var registry = new StubRegistry([local, cloud]);
        var resolver = new ModelRouteResolver(registry);
        var route = new ModelRoute("active", 1, [new("cloud", "cloud"), new("local", "local")], new(AllowCloud: true, RequiredCapabilities: new HashSet<string> { "WebSearch" }));
        var result = await resolver.ResolveAsync(route, null);
        Assert.True(result.Succeeded);
        Assert.Equal("cloud:cloud:current", result.Value!.Model.StableKey);
        var denied = await resolver.ResolveAsync(route with { Policy = route.Policy with { AllowPrivateContextToCloud = false } }, null, containsPrivateContext: true);
        Assert.Equal(DulcheErrorCode.PermissionDenied, denied.Error!.Code);
    }

    [Fact]
    public async Task ScopedRoutesUseRevisionChecksAndPreviewTrace()
    {
        var registryProvider = new StubRegistry([Descriptor("local", true, new HashSet<ToolCapability>()), Descriptor("cloud", false, new HashSet<ToolCapability> { ToolCapability.WebSearch })]);
        var repository = new InMemoryModelRouteRepository();
        var registry = new ModelRouteRegistry(registryProvider, repository, new ModelRouteResolver(registryProvider));
        var route = new ConfiguredModelRoute("agent-chat", 0, ModelRouteScope.Agent, "agent:writer", ModelCapabilityCategory.Chat,
            [new(new("cloud", "cloud"), false, 0), new(new("local", "local"), true, 1)], new(AllowCloud: false));
        var saved = await registry.SaveRouteAsync(route, expectedRevision: 0);
        Assert.NotNull(saved.Route);
        Assert.Equal(1, saved.Route!.Revision);
        var stale = await registry.SaveRouteAsync(route, expectedRevision: 0);
        Assert.Equal(DulcheErrorCode.Conflict, stale.Error!.Code);
        var preview = await registry.PreviewAsync(saved.Route);
        Assert.Equal("local:local:current", preview.Selection!.Model.StableKey);
        Assert.Contains(preview.Trace, item => item.Contains("disabled by user", StringComparison.Ordinal));
        Assert.Equal(2, (await registry.GetCatalogueAsync()).Count);
    }

    private static ProviderModelDescriptor Descriptor(string name, bool local, IReadOnlySet<ToolCapability> capabilities) =>
        new(name, local, new(name, 1, "test", "1B", "q4", capabilities, DateTimeOffset.UtcNow));

    private sealed class StubRegistry(IReadOnlyList<ProviderModelDescriptor> models) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => [];
        public IModelProvider? Find(string providerId) => null;
        public IModelProvider GetRequired(string providerId) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult(models);
    }

    private sealed class FakeAdapter(int delayMilliseconds = 0) : IDulcheAdapter
    {
        private int _active;
        public string ProviderId => "fake";
        public string RuntimeVersion => "test-1";
        public bool IsLocal => true;
        public IReadOnlySet<string> Capabilities => new HashSet<string> { "chat", "sampling.temperature", "reasoning.levels" };
        public int MaximumConcurrent { get; private set; }
        public TaskCompletionSource FirstGenerationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> LoadModelAsync(DulcheEndpoint endpoint, ModelIdentity model, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public async IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint, DulcheRequest request, string requestId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active); MaximumConcurrent = Math.Max(MaximumConcurrent, active); FirstGenerationStarted.TrySetResult();
            try
            {
                if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds, cancellationToken);
                yield return new(Text: $"reply:{request.EffectivePrompt}", InputTokens: 1, OutputTokens: 2);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public async IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint, DulcheRequest request, string requestId, ToolInvocationResult result, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new(Text: "continued");
        }
        public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Failure(new(DulcheErrorCode.UnsupportedCapability, "pause unsupported", requestId, false)));
        public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Failure(new(DulcheErrorCode.UnsupportedCapability, "resume unsupported", requestId, false)));
        public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken) => ValueTask.FromResult(new RuntimeHealth(endpoint.EndpointId, endpoint.State, DateTimeOffset.UtcNow, null, Metric<double>.Na, Metric<double>.Na, Metric<double>.Na));
    }
}
