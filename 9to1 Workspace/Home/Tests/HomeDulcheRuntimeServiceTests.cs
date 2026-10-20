using System.Security.Cryptography;
using System.Text;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeDulcheRuntimeServiceTests
{
    [Fact]
    public async Task Ready_when_runtime_and_verified_model_evidence_are_present()
    {
        using var fixture = new EvidenceFixture();
        var source = new HomeDulcheRuntimeObservationSource(
            runtimeProbe: _ => true,
            modelEvidence: ct => fixture.VerifiedEvidenceAsync("local-model-q4", ct));
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Ready, observation.Lifecycle);
        Assert.True(observation.RuntimeInstalled);
        Assert.True(observation.RuntimeVerified);
        Assert.True(observation.ModelInstalled);
        Assert.True(observation.ModelVerified);
        Assert.True(observation.TransportSupported);
        Assert.True(observation.CapabilitySupported);
        Assert.Empty(observation.Deficiencies);
    }

    [Fact]
    public async Task Missing_runtime_is_distinguishable_from_missing_model()
    {
        using var fixture = new EvidenceFixture();
        var source = new HomeDulcheRuntimeObservationSource(
            runtimeProbe: _ => false,
            modelEvidence: ct => fixture.VerifiedEvidenceAsync("local-model-q4", ct));
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Degraded, observation.Lifecycle);
        Assert.False(observation.RuntimeInstalled);
        Assert.False(observation.RuntimeVerified);
        Assert.True(observation.ModelInstalled);
        Assert.True(observation.ModelVerified);
        Assert.Contains(observation.Deficiencies, d => d.Contains("runtime package", StringComparison.Ordinal));
        Assert.DoesNotContain(observation.Deficiencies, d => d.Contains("model", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tampered_model_artifact_is_not_admitted_as_verified()
    {
        using var fixture = new EvidenceFixture();
        var source = new HomeDulcheRuntimeObservationSource(
            runtimeProbe: _ => true,
            modelEvidence: ct => fixture.VerifiedEvidenceAsync("local-model-q4", ct));
        var before = await source.ObserveAsync(CancellationToken.None);
        Assert.Equal(HomeServiceLifecycleState.Ready, before.Lifecycle);

        fixture.TamperModel("local-model-q4");
        var after = await source.ObserveAsync(CancellationToken.None);
        Assert.Equal(HomeServiceLifecycleState.Degraded, after.Lifecycle);
        Assert.False(after.ModelInstalled);
        Assert.False(after.ModelVerified);
        Assert.Contains(after.Deficiencies, d => d.Contains("model", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Home_runtime_registers_ready_and_degraded_states_truthfully()
    {
        using var fixture = new EvidenceFixture();

        var readySource = new HomeDulcheRuntimeObservationSource(
            runtimeProbe: _ => true,
            modelEvidence: ct => fixture.VerifiedEvidenceAsync("local-model-q4", ct));
        await using (var runtime = new HomeCoreRuntime([new HomeDulcheRuntimeService(readySource)]))
        {
            var snapshot = await runtime.StartAsync(CancellationToken.None);
            var service = Assert.Single(snapshot.Services, row => row.ServiceId == "dulche.runtime");
            Assert.Equal(HomeServiceLifecycleState.Ready, service.State);
            Assert.True(service.IsAvailable);
        }

        var missingModelSource = new HomeDulcheRuntimeObservationSource(runtimeProbe: _ => true);
        await using (var runtime = new HomeCoreRuntime([new HomeDulcheRuntimeService(missingModelSource)]))
        {
            var snapshot = await runtime.StartAsync(CancellationToken.None);
            var service = Assert.Single(snapshot.Services, row => row.ServiceId == "dulche.runtime");
            Assert.Equal(HomeServiceLifecycleState.Degraded, service.State);
            Assert.False(service.IsAvailable);
            Assert.Contains("HomeDulcheRuntimeUnavailable", service.Diagnostic);
        }
    }

    private sealed class EvidenceFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-dulche-runtime-test-" + Guid.NewGuid().ToString("N"));
        private readonly string _deviceId = "test-device";
        private readonly LocalModelRegistry _registry;
        private readonly Dictionary<string, string> _modelPaths = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private bool _disposed;

        public EvidenceFixture()
        {
            Directory.CreateDirectory(_root);
            _registry = new LocalModelRegistry(_root, _deviceId);
        }

        private string Install(string modelId)
        {
            lock (_gate)
            {
                if (_modelPaths.TryGetValue(modelId, out var existing)) return existing;
                var path = Path.Combine(_root, modelId + ".bin");
                var bytes = Encoding.UTF8.GetBytes("fake-model-artifact-" + modelId + "-" + Guid.NewGuid().ToString("N"));
                File.WriteAllBytes(path, bytes);
                var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                _registry.RegisterAsync(modelId, path, sha).GetAwaiter().GetResult();
                _modelPaths[modelId] = path;
                return path;
            }
        }

        public void TamperModel(string modelId)
        {
            lock (_gate)
            {
                if (!_modelPaths.TryGetValue(modelId, out var path)) throw new InvalidOperationException("Model not installed.");
                File.WriteAllText(path, "tampered-artifact-content");
            }
        }

        public Task<HomeDulcheModelEvidence?> VerifiedEvidenceAsync(string modelId, CancellationToken ct)
        {
            Install(modelId);
            return VerifyAsync(modelId, ct);
        }

        private async Task<HomeDulcheModelEvidence?> VerifyAsync(string modelId, CancellationToken ct)
        {
            var installation = await _registry.GetVerifiedAsync(modelId, ct).ConfigureAwait(false);
            return installation is null
                ? null
                : new HomeDulcheModelEvidence(modelId, "llama-cpp", installation.Sha256, installation.Length, DateTimeOffset.UtcNow);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
