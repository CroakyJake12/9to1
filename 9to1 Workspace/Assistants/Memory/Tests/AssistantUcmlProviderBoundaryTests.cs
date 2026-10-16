using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Theory]
    [InlineData(false, "stream")]
    [InlineData(false, "complete")]
    [InlineData(false, "tools")]
    [InlineData(true, "stream")]
    [InlineData(true, "complete")]
    [InlineData(true, "tools")]
    public async Task Ucml_local_flag_does_not_authorize_an_unverified_provider_or_even_its_catalogue(bool resilient, string action)
    {
        var unverified = new UcmlUnverifiedProvider();
        var registry = new ModelProviderRegistry([unverified]); var privacy = new UcmlDispatchPrivacy();
        var primary = new ProviderRoutingModelClient(new NoModelCalls(), registry, privacy);
        IOllamaClient client = resilient ? new ResilientProviderRoutingModelClient(primary, registry,
            new UcmlBoundaryConfigurations([unverified]), privacy) : primary;
        var options = new GenerationOptions { RequireOriginalPrivateContext = true,
            OriginalPrivateContext = new UcmlBoundaryGuard(), RequestedRoutingConstraints = new(false, true) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => InvokeUcmlBoundaryAsync(client, "unverified:model", action, options));
        Assert.Equal(0, unverified.CatalogueCalls + unverified.DispatchCalls);
        Assert.False(typeof(IOriginalPrivateContextModelProvider).IsAssignableFrom(typeof(OllamaModelProvider)));
        Assert.True(typeof(IOriginalPrivateContextModelProvider).IsAssignableFrom(typeof(LlamaCppModelProvider)));
    }

    [Theory]
    [InlineData("stream")]
    [InlineData("complete")]
    [InlineData("tools")]
    public async Task Ucml_restored_request_with_missing_live_guard_is_refused_before_catalogue(string action)
    {
        var local = new UcmlDispatchProvider("llama-cpp", true);
        var registry = new ModelProviderRegistry([local]); var privacy = new UcmlDispatchPrivacy();
        var router = new ResilientProviderRoutingModelClient(new(new NoModelCalls(), registry, privacy), registry,
            new UcmlBoundaryConfigurations([local]), privacy);
        var options = new GenerationOptions { RequireOriginalPrivateContext = true, RequestedRoutingConstraints = new(false, true) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => InvokeUcmlBoundaryAsync(router, "llama-cpp:model", action, options));
        Assert.Equal(0, local.CatalogueCalls); Assert.Empty(local.Requests);
    }

    [Fact]
    public async Task Ucml_fallback_skips_unverified_local_flags_before_catalogue_and_rechecks_live_guard()
    {
        var first = new UcmlDispatchProvider("llama-cpp", true) { FailStream = true };
        var second = new UcmlDispatchProvider("second", true); var unverified = new UcmlUnverifiedProvider();
        var remote = new UcmlDispatchProvider("remote", false);
        IModelProvider[] providers = [first, second, unverified, remote];
        var registry = new ModelProviderRegistry(providers); var privacy = new UcmlDispatchPrivacy();
        var router = new ResilientProviderRoutingModelClient(new(new NoModelCalls(), registry, privacy), registry,
            new UcmlBoundaryConfigurations(providers), privacy);
        var guard = new UcmlBoundaryGuard();
        await InvokeUcmlBoundaryAsync(router, "llama-cpp:model", "stream", new()
        { RequireOriginalPrivateContext = true, OriginalPrivateContext = guard, RequestedRoutingConstraints = new(false, true) });
        Assert.Single(first.Requests); Assert.Single(second.Requests); Assert.Equal(2, guard.Validations);
        Assert.Equal(0, unverified.CatalogueCalls + unverified.DispatchCalls);
        Assert.Equal(0, remote.CatalogueCalls + remote.Requests.Count);
    }

    private static Task InvokeUcmlBoundaryAsync(IOllamaClient client, string model, string action, GenerationOptions options) =>
        action switch
        {
            "stream" => DrainPrivateRequestAsync(client, new(model, [], EffortLevel.Low, "private fixture", Options: options)),
            "complete" => client.CompleteAsync(new(model, [], EffortLevel.Low, "private fixture", Options: options), Token),
            "tools" => client.ChatWithToolsAsync(new(model, [], [], EffortLevel.Low, "private fixture", Options: options), Token),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

    private sealed class UcmlBoundaryGuard : IOriginalPrivateContextDispatchGuard
    {
        public int Validations;
        public Task ValidateAsync(bool actualProviderIsLocal, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Assert.True(actualProviderIsLocal); Validations++; return Task.CompletedTask; }
    }

    private sealed class UcmlBoundaryConfigurations(IReadOnlyList<IModelProvider> providers) : IProviderConfigurationStore
    {
        private readonly IReadOnlyList<ProviderConfiguration> _values = providers.Select(provider => new ProviderConfiguration(
            provider.Id, provider.Kind, provider.Id, "http://127.0.0.1:11434", true, provider.IsLocal, true,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow)).ToArray();
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult(_values);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult(_values.SingleOrDefault(value => value.Id == id));
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class UcmlUnverifiedProvider : IModelProvider
    {
        public int CatalogueCalls; public int DispatchCalls;
        public string Id => "unverified"; public string DisplayName => "Unverified local flag";
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => true; public bool CanManageModels => false;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { CatalogueCalls++; throw new InvalidOperationException("An unverified recipient must not be catalogued for private context."); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new NotSupportedException();
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { DispatchCalls++; await Task.Yield(); token.ThrowIfCancellationRequested(); yield return "Unverified"; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { DispatchCalls++; return Task.FromResult("Unverified"); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        { DispatchCalls++; return Task.FromResult(new OllamaToolResponse("Unverified", [])); }
    }
}
