using System.Reflection;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class ResilientProviderRoutingRecoveryTests
{
    /// <summary>Real coordinator/frame-issued SAME attempt and actual dispatcher compatibility.
    /// The existing fixture authority and installation/hardware observations are synthetic:
    /// this does not grant model use, execute native code or certify a CUDA capability.</summary>
    [Theory]
    [InlineData(false, "Tools")]
    [InlineData(true, "Tools")]
    [InlineData(false, "Vision")]
    [InlineData(true, "Vision")]
    public async Task Admitted_request_feature_is_hard_Strata_fit_before_native_factory(bool manual, string feature)
    {
        var provider = new Provider("strata-control", isLocal: true, capabilities:
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools, ToolCapability.Vision });
        var fixture = await CanonicalFixture.CreateAsync([provider], initialRequiredCapabilities:
            [ToolCapability.Text, Enum.Parse<ToolCapability>(feature)]);
        var failures = new List<Exception>();
        InferenceEngineDispatcher? dispatcher = null;
        Task? startup = null, close = null;
        try
        {
            var before = await fixture.CurrentAsync();
            var same = await fixture.Coordinator.GetIssuedAttemptAsync(before.TaskId, before.ExecutionId,
                fixture.Admission.AttemptId, CancellationToken.None);
            Assert.Same(fixture.Admission, same);
            if (same is null) throw new InvalidOperationException("No SAME privately issued attempt exists.");
            var model = new ModelIdentity(same.Lease.Candidate.ProviderId, same.Lease.Candidate.ModelId,
                same.Lease.Candidate.ArtifactIdentity);
            var installed = StrataCompatibilityInstalled(model);
            var requirements = StrataCompatibilityRequirements(installed, same);
            var (hardware, inventory) = StrataCompatibilityBuild();
            var support = StrataCompatibilitySupport(hardware, inventory);
            var factory = new UnexpectedStrataCompatibilityFactory();
            var observation = new InferenceRuntimeObservation(requirements, hardware.Hardware, [support]);
            var target = new Uri("http://127.0.0.1:12345");
            dispatcher = new(model.ProviderId, target, [new(InferenceEngine.Strata, factory)],
                new StrataCompatibilityObservation(observation));
            if (manual) Assert.True(dispatcher.SetInferenceEngine(InferenceEngine.Strata).Succeeded);
            var endpoint = new DulcheEndpoint("same-request-feature-control", model.ProviderId, target.AbsoluteUri,
                12345, EndpointState.Starting, model, new HashSet<string>(), false, DateTimeOffset.UnixEpoch);
            var original = dispatcher.StartAsync(endpoint, CancellationToken.None).AsTask();
            startup = original;
            var result = await original;
            Assert.False(result.Succeeded);
            Assert.Equal(DulcheErrorCode.UnsupportedCapability, result.Error!.Code);
            Assert.Equal(0, factory.Calls);
            var diagnostic = dispatcher.GetInferenceEngine(endpoint.EndpointId);
            Assert.Null(diagnostic.Effective);
            Assert.Same(model, diagnostic.Model);
            Assert.Equal(manual ? InferenceEngine.Strata : InferenceEngine.Automatic, diagnostic.Requested);
            Assert.Contains(Assert.Single(diagnostic.Compatibility).Unmet,
                gap => gap.Requirement == "runtime.feature" && gap.Expected == feature);
            Assert.Contains(feature, requirements.RequiredFeatures);
            Assert.DoesNotContain(feature, installed.RequiredFeatures);
            Assert.DoesNotContain(feature, support.Features);
            Assert.Equal(0, provider.ToolCalls);
            Assert.Equal(0, provider.CompletionCalls);
            Assert.Equal(0, provider.StreamCalls);
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.CurrentAsync()));
            Assert.Same(same.Lease, Assert.Single(fixture.Authority.Leases));
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            // Acquire and join independent actual closes even when the oracle/startup fails.
            if (dispatcher is not null)
                try { close = dispatcher.DisposeAsync().AsTask(); }
                catch (Exception error) { failures.Add(error); }
            foreach (var actual in new Task?[] { startup, close })
                if (actual is not null) await CollectStrataCompatibilityOriginalAsync(actual, failures);
            await CollectStrataCompatibilityCloseAsync(fixture.Frames.CloseAndDrainAsync, failures);
        }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Text_request_preserves_installation_conditions_and_never_adds_build_features()
    {
        var provider = new Provider("strata-control", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([provider], initialRequiredCapabilities: [ToolCapability.Text]);
        var failures = new List<Exception>();
        try
        {
            var model = new ModelIdentity(provider.Id, provider.Descriptor.Name);
            var installed = StrataCompatibilityInstalled(model);
            var requirements = StrataCompatibilityRequirements(installed, fixture.Admission);
            var (hardware, inventory) = StrataCompatibilityBuild();
            var support = StrataCompatibilitySupport(hardware, inventory);
            Assert.True(InferenceCompatibilityRegistry.Inspect(requirements, hardware.Hardware, support).Compatible);
            Assert.Same(installed.Model, requirements.Model);
            Assert.Same(installed.PreferredEngines, requirements.PreferredEngines);
            Assert.Equal(installed with { RequiredFeatures = requirements.RequiredFeatures }, requirements);
            Assert.Equal(inventory with { Features = support.Features }, support);
            Assert.Contains("Tools", inventory.Features);
            Assert.DoesNotContain("Tools", support.Features);
            Assert.Contains("Text", support.Features);
            Assert.Contains("Streaming", support.Features);
            Assert.Equal(new[] { "Text" }, installed.RequiredFeatures.OrderBy(value => value).ToArray());
            var streamingOnly = inventory with { Features = new HashSet<string> { "Streaming", "Tools" } };
            var observed = StrataCompatibilitySupport(hardware, streamingOnly);
            Assert.DoesNotContain("Text", observed.Features); // The ceiling never invents support.
            Assert.Contains("Streaming", observed.Features);
            Assert.False(InferenceCompatibilityRegistry.Inspect(requirements, hardware.Hardware, observed).Compatible);
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectStrataCompatibilityCloseAsync(fixture.Frames.CloseAndDrainAsync, failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    private static async Task CollectStrataCompatibilityOriginalAsync(Task same, List<Exception> failures)
    {
        try { await same.ConfigureAwait(false); }
        catch (Exception caught)
        {
            if (!failures.Any(prior => ReferenceEquals(prior, caught))) failures.Add(caught);
            // Retain the complete original group, not only the exception chosen by await.
            if (same.Exception is { } group && !failures.Any(prior => ReferenceEquals(prior, group))) failures.Add(group);
        }
    }
    private static async Task CollectStrataCompatibilityCloseAsync(Func<Task> source, List<Exception> failures)
    {
        Task? actual = null;
        try { actual = source(); }
        catch (Exception caught) { failures.Add(caught); }
        if (actual is not null) await CollectStrataCompatibilityOriginalAsync(actual, failures).ConfigureAwait(false);
    }

    private static InferenceModelRequirements StrataCompatibilityInstalled(ModelIdentity model) => new(model,
        "synthetic-installation-artifact", "control-architecture", "control-family", "Safetensors", "control-Q4",
        new HashSet<string> { "Text" }, 1, 1, 2048, [InferenceEngine.Strata], "control-registration");
    private static (StrataOriginalHardwareObservation, InferenceEngineSupport) StrataCompatibilityBuild()
    {
        var hardware = new StrataOriginalHardwareObservation(new("synthetic-hardware", "Linux", "X64", 8192,
            [new("synthetic-device", 8, 6, 4096)], new HashSet<string>()), "synthetic-native-build", 12080, 12080,
            true, false, ["control-registration"], [0]);
        var support = new InferenceEngineSupport(InferenceEngine.Strata, hardware.RuntimeBuild, true, null,
            new HashSet<string> { "control-architecture" }, new HashSet<string> { "control-family" },
            new HashSet<string> { "Safetensors" }, new HashSet<string> { "control-Q4" },
            new HashSet<string> { "Text", "Streaming", "Tools", "Vision" }, new HashSet<string> { "Linux" },
            new HashSet<string> { "X64" }, new HashSet<string>(), RequiresCuda: true,
            NativeRegistrations: new HashSet<string> { "control-registration" });
        return (hardware, support);
    }
    private static InferenceModelRequirements StrataCompatibilityRequirements(InferenceModelRequirements original,
        TaskRunAttemptAdmission same) => (InferenceModelRequirements)typeof(StrataRuntimeObservationSource)
        .GetMethod("ObserveRequestRequirements", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [original, same])!;
    private static InferenceEngineSupport StrataCompatibilitySupport(StrataOriginalHardwareObservation hardware,
        InferenceEngineSupport inventory) => (InferenceEngineSupport)typeof(StrataRuntimeObservationSource)
        .GetMethod("ObserveBuildSupport", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [hardware, inventory])!;
    private sealed class StrataCompatibilityObservation(InferenceRuntimeObservation same) : IInferenceRuntimeObservationSource
    {
        public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,
            IInferenceEngineOriginalSourceScope scope, CancellationToken token) => Task.FromResult(same);
    }
    private sealed class UnexpectedStrataCompatibilityFactory : IInferenceEngineAdapterFactory
    {
        public int Calls;
        public Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity model,
            IInferenceEngineOriginalSourceScope scope, CancellationToken token)
        { Calls++; return Task.FromException<OriginalInferenceEngineLease>(new InvalidOperationException("No native factory may run for an incompatible request.")); }
    }
}
