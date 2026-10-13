using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Spaces.Chat;
using Xunit;

namespace NineToOne.Web.Spaces.Tests;

public sealed class BrowserPrivacySessionTests
{
    private const string Key = "privacy.preferences.v1";
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"LocalOnlyMode\":false}")]
    [InlineData("{\"LocalOnlyMode\":false,\"LocalOnlyMode\":true,\"BackgroundLearningEnabled\":false,\"ModelImprovementSharingEnabled\":false,\"UpdatedAt\":\"2026-10-13T00:00:00Z\"}")]
    public async Task Present_invalid_policy_refuses_cloud_and_preserves_original(string json)
    {
        var store = new Settings { Json = json }; var policy = new BrowserPrivacyPreferenceStore(store); var provider = new Provider();
        var hosted = new BrowserHostedOllamaClient(provider, () => { }, policy);
        await Assert.ThrowsAsync<InvalidDataException>(() => hosted.CompleteAsync(Request(), default));
        Assert.Equal(json, store.Json); Assert.Equal(0, provider.Dispatches);
        Assert.Throws<InvalidOperationException>(() => policy.Current);
    }
    [Fact]
    public async Task Only_confirmed_missing_policy_uses_shared_default()
    {
        var store = new Settings(); var policy = new BrowserPrivacyPreferenceStore(store);
        Assert.Throws<InvalidOperationException>(() => policy.Current);
        Assert.Same(PrivacyPreferences.Default, await policy.RefreshAsync(default)); Assert.Equal(1, store.Reads);
        store.Failure = new IOException("original policy read failure");
        Assert.Same(store.Failure, await Assert.ThrowsAsync<IOException>(() => policy.RefreshAsync(default)));
        Assert.Throws<InvalidOperationException>(() => policy.Current);
    }
    [Fact]
    public async Task Local_only_blocks_catalogue_and_dispatch_before_provider()
    {
        var store = new Settings { Json = JsonSerializer.Serialize(PrivacyPreferences.Default with { LocalOnlyMode = true }) };
        var policy = new BrowserPrivacyPreferenceStore(store); var provider = new Provider();
        var hosted = new BrowserHostedOllamaClient(provider, () => { }, policy);
        Assert.Empty(await hosted.GetModelsAsync(default)); Assert.False(await hosted.IsAvailableAsync(default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => hosted.CompleteAsync(Request(), default)); Assert.Equal(0, provider.Dispatches);
    }
    [Fact]
    public async Task Actual_catalogue_is_qualified_remote_and_original_name_is_used_on_wire()
    {
        var policy = new BrowserPrivacyPreferenceStore(new Settings()); var provider = new Provider();
        IOllamaClient hosted = new BrowserHostedOllamaClient(provider, () => { }, policy);
        Assert.Equal("hosted-ollama:actual-model", Assert.Single(await hosted.GetModelsAsync(default)).Name);
        Assert.False(hosted.IsDeviceLocalTransportVerified); Assert.Null(hosted.TransportEndpoint);
        Assert.Equal("actual", await hosted.CompleteAsync(Request(), default)); Assert.Equal("actual-model", provider.LastModel);
    }
    [Fact]
    public async Task Streaming_reads_policy_once_and_observes_acknowledged_live_update_without_network_per_chunk()
    {
        var store = new Settings(); var policy = new BrowserPrivacyPreferenceStore(store); var provider = new Provider();
        var hosted = new BrowserHostedOllamaClient(provider, () => { }, policy);
        await using var stream = hosted.StreamChatAsync(Request(), default).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync()); Assert.Equal("one", stream.Current);
        Assert.True(await stream.MoveNextAsync()); Assert.Equal("two", stream.Current); Assert.Equal(1, store.Reads);
        await policy.UpdateAsync(PrivacyPreferences.Default with { LocalOnlyMode = true }, default);
        Assert.Equal(2, store.Reads); Assert.True(policy.Current.LocalOnlyMode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => stream.MoveNextAsync().AsTask()); Assert.Equal(2, store.Reads);
    }
    [Fact]
    public async Task Unknown_write_ack_clears_cached_cloud_policy()
    {
        var store = new Settings(); var policy = new BrowserPrivacyPreferenceStore(store); await policy.RefreshAsync(default);
        store.WriteFailure = new IOException("original write acknowledgement lost");
        Assert.Same(store.WriteFailure, await Assert.ThrowsAsync<IOException>(() => policy.UpdateAsync(PrivacyPreferences.Default with { LocalOnlyMode = true }, default)));
        Assert.Throws<InvalidOperationException>(() => policy.Current);
        Assert.True(JsonSerializer.Deserialize<PrivacyPreferences>(store.Json!)!.LocalOnlyMode);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Route_restrictions_cannot_silently_enable_cloud_or_fallback(bool cloud, bool fallback)
    {
        var provider = new Provider(); var hosted = new BrowserHostedOllamaClient(provider, () => { }, new(new Settings()));
        var request = Request() with { Options = new() { RequestedRoutingConstraints = new(cloud, fallback) } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => hosted.CompleteAsync(request, default)); Assert.Equal(0, provider.Dispatches);
    }
    private static OllamaChatRequest Request() => new("hosted-ollama:actual-model", [new("user", "test")], EffortLevel.Low,
        Options: new() { RequestedRoutingConstraints = new(true, false) });
    private sealed class Settings : IVersionedSettingsStore
    {
        public string? Json; public int Reads; public IOException? Failure, WriteFailure;
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        { Reads++; if (Failure is not null) return Task.FromException<SettingsExportManifest>(Failure); return Task.FromResult(new SettingsExportManifest { Settings = Json is null ? [] : new() { [Key] = Json } }); }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => throw new InvalidOperationException("Nullable Get loses presence and must not be used for policy.");
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        { Assert.Equal(Key, key); Json = JsonSerializer.Serialize(value); return WriteFailure is null ? Task.CompletedTask : Task.FromException(WriteFailure); }
        public Task RemoveAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Provider : IOllamaClient
    {
        public int Dispatches; public string? LastModel;
        public Task<bool> IsAvailableAsync(CancellationToken token) { Dispatches++; return Task.FromResult(true); }
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) { Dispatches++; return Task.FromResult<IReadOnlyList<ModelDescriptor>>([new("actual-model", 1, "family", "parameter", "format", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch)]); }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        { Dispatches++; LastModel = request.Model; foreach (var chunk in new[] { "one", "two", "three" }) { await Task.Yield(); token.ThrowIfCancellationRequested(); yield return chunk; } }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) { Dispatches++; LastModel = request.Model; return Task.FromResult("actual"); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
