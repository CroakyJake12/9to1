using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HomeDiscoverQuery = HavenOS.Home.Discover.HomeDiscoverQuery;
using HavenOS.Home.Discover;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verified_installed_Dulche_model_remains_visible_when_live_catalogue_is_empty_or_fails(bool fail)
    {
        using var store = new ModelStore();
        var (root, sha) = store.WriteVerifiedModel();
        var provider = DulcheProvider();
        if (fail)
            provider.Read = _ => Task.FromException<IReadOnlyList<ProviderModelDescriptor>>(new IOException("worker socket unavailable"));
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]),
            "llama-cpp", () => false, new HomeDulchePackagedModelEvidenceSource(root));

        var result = await source.GetSnapshotAsync(CancellationToken.None);

        var model = Assert.Single(result.Models);
        Assert.Equal("llama-cpp", model.Identity.ProviderId);
        Assert.Equal("local-model-q4", model.Identity.ModelId);
        Assert.Equal(sha, model.Identity.ArtifactRevision);
        Assert.Equal(HomeDiscoverInstallState.Installed, model.InstallState);
        Assert.Equal(HomeDiscoverConnectionState.Unavailable, model.ConnectionState);
        Assert.Empty(model.Categories!);
        Assert.Empty(model.Capabilities!);
        Assert.Empty(model.SupportedActions);
        Assert.Equal(HomeDiscoverSourceState.Partial, result.State);
        Assert.Equal(fail ? "Discover.ProviderUnavailable" : "Discover.ModelNotObserved", result.ErrorCode);
        if (fail)
        {
            Assert.NotEmpty(result.ErrorMessage!);
            Assert.DoesNotContain("worker socket unavailable", result.ErrorMessage);
        }
    }

    [Fact]
    public async Task Matching_live_Dulche_model_keeps_only_observed_capabilities_and_is_marked_installed()
    {
        using var store = new ModelStore();
        var (root, sha) = store.WriteVerifiedModel();
        var provider = DulcheProvider();
        provider.Models = [new("llama-cpp", true, new("local-model-q4", 32, "gguf", "", "Q4",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming }, DateTimeOffset.UtcNow))];
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]),
            "llama-cpp", () => false, new HomeDulchePackagedModelEvidenceSource(root));

        var result = await source.GetSnapshotAsync(CancellationToken.None);

        var model = Assert.Single(result.Models);
        Assert.Equal(sha, model.Identity.ArtifactRevision);
        Assert.Equal(HomeDiscoverInstallState.Installed, model.InstallState);
        Assert.Equal(HomeDiscoverConnectionState.Connected, model.ConnectionState);
        Assert.Contains(HomeDiscoverCategory.Chat, model.Categories!);
        Assert.Contains(nameof(ToolCapability.Streaming), model.Capabilities!);
        Assert.Equal(HomeDiscoverSourceState.Available, result.State);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData(ModelEvidenceState.Missing)]
    [InlineData(ModelEvidenceState.CorruptManifest)]
    [InlineData(ModelEvidenceState.HashMismatch)]
    public async Task Missing_or_unverified_Dulche_evidence_does_not_create_an_installed_model(ModelEvidenceState state)
    {
        using var store = new ModelStore();
        var root = store.Root;
        if (state != ModelEvidenceState.Missing)
        {
            store.WriteVerifiedModel();
            if (state == ModelEvidenceState.CorruptManifest) store.CorruptManifest();
            if (state == ModelEvidenceState.HashMismatch) store.TamperBlob();
        }
        var provider = DulcheProvider();
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]),
            "llama-cpp", () => false, new HomeDulchePackagedModelEvidenceSource(root));

        var result = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Empty(result.Models);
        Assert.Equal(HomeDiscoverSourceState.Empty, result.State);
    }

    [Fact]
    public async Task Packaged_evidence_is_not_projected_for_other_or_remote_providers()
    {
        using var store = new ModelStore();
        var (root, _) = store.WriteVerifiedModel();
        var evidence = new CountingEvidence(new HomeDulchePackagedModelEvidenceSource(root));
        var provider = new Provider("ollama", true);
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]),
            "ollama", () => false, evidence);

        var result = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Empty(result.Models);
        Assert.Equal(0, evidence.Reads);

        var misregisteredDulche = new Provider("llama-cpp", false) { Kind = ModelProviderKind.OpenAICompatible };
        var remoteSource = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([misregisteredDulche]),
            "llama-cpp", () => true, evidence);
        var remoteResult = await remoteSource.GetSnapshotAsync(CancellationToken.None);
        Assert.Empty(remoteResult.Models);
        Assert.Equal(0, evidence.Reads);
    }

    [Fact]
    public async Task Dulche_evidence_cancellation_is_observed_before_provider_query()
    {
        var pendingEvidence = new TaskCompletionSource<HomeDulcheModelEvidence?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var evidence = new CountingEvidence(_ => new ValueTask<HomeDulcheModelEvidence?>(pendingEvidence.Task));
        var provider = DulcheProvider();
        var source = new HomeRegisteredModelCatalogueSource(new ModelProviderRegistry([provider]),
            "llama-cpp", () => false, evidence);
        using var cancellation = new CancellationTokenSource();

        var pending = source.GetSnapshotAsync(cancellation.Token);
        cancellation.Cancel();
        pendingEvidence.SetCanceled(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, provider.Reads);
    }

    [Fact]
    public async Task Registry_provider_identity_change_during_Dulche_read_is_rejected()
    {
        using var store = new ModelStore();
        var (root, _) = store.WriteVerifiedModel();
        var provider = DulcheProvider();
        var registry = new SwappingRegistry(provider);
        var source = new HomeRegisteredModelCatalogueSource(registry, "llama-cpp", () => false,
            new HomeDulchePackagedModelEvidenceSource(root));

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetSnapshotAsync(CancellationToken.None));
    }

    private static ProviderModelDescriptor Model(string id, bool local) =>
        new(id, local, new("same-name", 0, "", "", "", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow));

    private sealed class Provider(string id, bool local) : IModelProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public ModelProviderKind Kind { get; init; } = local ? ModelProviderKind.Ollama : ModelProviderKind.OpenAICompatible;
        public bool IsLocal => local;
        public bool CanManageModels => true;
        public IReadOnlyList<ProviderModelDescriptor> Models { get; set; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<ProviderModelDescriptor>>>? Read { get; set; }
        public int Reads { get; private set; }
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        { Reads++; return Read?.Invoke(cancellationToken) ?? Task.FromResult(Models); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static Provider DulcheProvider() => new("llama-cpp", true)
    {
        Kind = ModelProviderKind.OpenAICompatible
    };

    public enum ModelEvidenceState { Missing, CorruptManifest, HashMismatch }

    private sealed class CountingEvidence : IHomeDulcheModelEvidenceSource
    {
        private readonly IHomeDulcheModelEvidenceSource? _inner;
        private readonly Func<CancellationToken, ValueTask<HomeDulcheModelEvidence?>>? _read;
        public CountingEvidence(IHomeDulcheModelEvidenceSource inner) => _inner = inner;
        public CountingEvidence(Func<CancellationToken, ValueTask<HomeDulcheModelEvidence?>> read) => _read = read;
        public int Reads { get; private set; }
        public ValueTask<HomeDulcheModelEvidence?> FindEligibleAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return _read?.Invoke(cancellationToken) ?? _inner!.FindEligibleAsync(cancellationToken);
        }
    }

    private sealed class SwappingRegistry(IModelProvider provider) : IModelProviderRegistry
    {
        private readonly IModelProvider _replacement = new Provider("llama-cpp", true) { Kind = ModelProviderKind.OpenAICompatible };
        private int _lookups;
        public IReadOnlyList<IModelProvider> Providers => [provider];
        public IModelProvider? Find(string providerId) => Interlocked.Increment(ref _lookups) == 1 ? provider : _replacement;
        public IModelProvider GetRequired(string providerId) => Find(providerId) ?? throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]);
    }

    private sealed class ModelStore : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-discover-dulche-" + Guid.NewGuid().ToString("N"));
        private string? _manifestPath;
        private string? _blobPath;
        public string Root => _root;
        public (string Root, string Sha256) WriteVerifiedModel()
        {
            var content = Encoding.UTF8.GetBytes("fake-dulche-model-" + Guid.NewGuid().ToString("N"));
            var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var blob = Path.Combine(sha[..2], sha + ".gguf").Replace('\\', '/');
            _blobPath = Path.Combine(_root, "blobs", "sha256", blob);
            Directory.CreateDirectory(Path.GetDirectoryName(_blobPath)!);
            File.WriteAllBytes(_blobPath, content);
            _manifestPath = Path.Combine(_root, "manifests", "local-model-q4.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(new
            {
                id = "local-model-q4", displayName = "Local Q4", sha256 = sha, blob,
                size = content.LongLength, license = "test", source = "test"
            }));
            return (_root, sha);
        }
        public void CorruptManifest() => File.WriteAllText(_manifestPath!, "{");
        public void TamperBlob() => File.WriteAllText(_blobPath!, "changed-model-bytes");
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
