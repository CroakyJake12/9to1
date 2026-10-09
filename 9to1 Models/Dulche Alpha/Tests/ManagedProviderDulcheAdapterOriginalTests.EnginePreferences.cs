using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Actual original service/runtime and old coordinator controls with synthetic engine
/// observations. These issue no installation, model residency, native or business acceptance.</summary>
public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
    [Fact]
    public async Task Configured_manual_preference_precedes_model_start_and_never_silently_substitutes()
    {
        await Control(async h =>
        {
            var model = new ModelIdentity(h.Provider.Id, "model");
            var preferences = new RecordingEnginePreference(InferenceEngine.Strata);
            var composition = new PreferenceControlComposition(preferences);
            var service = PreferenceService(h, composition, preferences);
            Task<OperationResult<ManagedDulcheProviderBinding>>? initialization = null;
            Task<OperationResult<InferenceEngineSettings>>? settings = null; Task? close = null;
            var errors = new List<Exception>();
            try
            {
                initialization = service.StartConfiguredModelAsync(h.Provider.Id, model, h.Admission, CancellationToken.None);
                var result = await initialization.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                Assert.False(result.Succeeded); Assert.Equal(DulcheErrorCode.UnsupportedCapability, result.Error!.Code);
                Assert.Equal(1, preferences.Reads); Assert.Same(model, preferences.OriginalModel);
                Assert.Equal(1, composition.Calls); Assert.Equal(1, composition.PreferenceReadsAtFactory);
                Assert.Equal(0, composition.RawFactoryCalls);
                settings = service.GetOriginalInferenceEngineSettingsAsync(h.Admission, model, CancellationToken.None);
                var observed = await settings.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                Assert.True(observed.Succeeded);
                var report = observed.Value!.Read();
                Assert.Equal(InferenceEngine.Strata, report.Requested); Assert.Equal(InferenceEngine.Strata, report.PendingPreference);
                Assert.Null(report.Effective); Assert.Null(report.ActiveSelectionPreference);
                Assert.Same(model, report.Model);
                Assert.Equal(0, h.Provider.CompleteCalls); Assert.Equal(0, h.Provider.StreamCalls); Assert.Equal(0, h.Provider.ToolCalls);
            }
            catch (Exception cause) { AddUnexpected(h, errors, cause); }
            finally
            {
                try { close = service.CloseAndDrainAsync(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
                if (initialization is not null) await JoinUnexpected(h, errors, initialization);
                if (settings is not null) await JoinUnexpected(h, errors, settings);
                if (close is not null) await JoinUnexpected(h, errors, close);
            }
            ThrowInitializedModelControlErrors(errors);
        });
    }

    [Fact]
    public async Task Retired_same_original_lease_cannot_disclose_retained_endpoint_settings()
    {
        await Control(async h =>
        {
            var model = new ModelIdentity(h.Provider.Id, "model");
            var preferences = new RecordingEnginePreference(InferenceEngine.Strata);
            var composition = new PreferenceControlComposition(preferences);
            var service = PreferenceService(h, composition, preferences);
            Task<OperationResult<ManagedDulcheProviderBinding>>? initialization = null;
            Task<OperationResult<InferenceEngineSettings>>? settings = null; Task? close = null;
            var errors = new List<Exception>();
            try
            {
                initialization = service.StartConfiguredModelAsync(h.Provider.Id, model, h.Admission, CancellationToken.None);
                Assert.False((await initialization.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Succeeded);
                await h.Authority.Lease!.DisposeAsync();
                settings = service.GetOriginalInferenceEngineSettingsAsync(h.Admission, model, CancellationToken.None);
                var failure = await Record.ExceptionAsync(() => settings);
                if (failure is not null) h.Expect(failure);
                Assert.IsType<ObjectDisposedException>(failure); Assert.True(settings.IsFaulted);
                Assert.Equal(1, composition.Calls); Assert.Equal(0, composition.RawFactoryCalls);
                Assert.Equal(0, h.Provider.CompleteCalls); Assert.Equal(0, h.Provider.StreamCalls); Assert.Equal(0, h.Provider.ToolCalls);
            }
            catch (Exception cause) { AddUnexpected(h, errors, cause); }
            finally
            {
                try { close = service.CloseAndDrainAsync(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
                if (initialization is not null) await JoinUnexpected(h, errors, initialization);
                if (settings is not null) await JoinUnexpected(h, errors, settings);
                if (close is not null) await JoinUnexpected(h, errors, close);
            }
            ThrowInitializedModelControlErrors(errors);
        });
    }

    [Fact]
    public async Task Settings_lookup_never_creates_a_missing_model_endpoint()
    {
        await Control(async h =>
        {
            var preferences = new RecordingEnginePreference(InferenceEngine.Strata);
            var composition = new PreferenceControlComposition(preferences);
            var service = PreferenceService(h, composition, preferences);
            Task<OperationResult<InferenceEngineSettings>>? settings = null; Task? close = null;
            var errors = new List<Exception>();
            try
            {
                settings = service.GetOriginalInferenceEngineSettingsAsync(h.Admission, new(h.Provider.Id, "model"), CancellationToken.None);
                var result = await settings.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                Assert.False(result.Succeeded); Assert.Equal(DulcheErrorCode.ProviderUnavailable, result.Error!.Code);
                Assert.Equal(0, composition.Calls); Assert.Equal(0, preferences.Reads); Assert.Equal(0, composition.RawFactoryCalls);
                Assert.Equal(0, h.Provider.CompleteCalls); Assert.Equal(0, h.Provider.StreamCalls); Assert.Equal(0, h.Provider.ToolCalls);
            }
            catch (Exception cause) { AddUnexpected(h, errors, cause); }
            finally
            {
                try { close = service.CloseAndDrainAsync(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
                if (settings is not null) await JoinUnexpected(h, errors, settings);
                if (close is not null) await JoinUnexpected(h, errors, close);
            }
            ThrowInitializedModelControlErrors(errors);
        });
    }

    private static ManagedDulcheRuntimeService PreferenceService(Harness h,
        IManagedDulcheInferenceCompositionSource composition, IInferenceEnginePreferenceSource preferences)
        => new(new SyntheticRegistry(h.Provider), new SyntheticConfigurations(h.Provider), h.Coordinator, h.Frames,
            inferenceComposition: composition, inferencePreferences: preferences);

    private sealed class RecordingEnginePreference(InferenceEngine requested) : IInferenceEnginePreferenceSource
    {
        public int Reads; public ModelIdentity? OriginalModel;
        public InferenceEngine GetRequestedInferenceEngine(ModelIdentity sameModel)
        { Reads++; OriginalModel = sameModel; return requested; }
    }

    private sealed class PreferenceControlComposition(RecordingEnginePreference preferences) : IManagedDulcheInferenceCompositionSource
    {
        public int Calls, PreferenceReadsAtFactory, RawFactoryCalls;
        public async Task<OriginalInferenceEngineLease> CreateAfterPublicationAsync(Task originalStart, string providerId,
            ModelIdentity sameModel, TaskRunAttemptAdmission sameAdmission, IModelProviderRegistry sameRegistry,
            IProviderConfigurationStore sameConfigurations, TaskExecutionCoordinator sameCoordinator,
            ITaskRunOriginalFrameOwner sameFrames, IOriginalDulcheProviderToolSource? sameTools,
            IOriginalDulcheProviderContextSource? sameContexts, ITaskRunProviderContextAuthority? sameContextAuthority,
            IDulcheOriginalFactoryCallbackScope originalScope, CancellationToken cancellationToken)
        {
            await originalStart;
            return originalScope.RunOriginalFactoryInvocation<OriginalInferenceEngineLease>(() => {
                cancellationToken.ThrowIfCancellationRequested(); Calls++; PreferenceReadsAtFactory = preferences.Reads;
                var requirements = new InferenceModelRequirements(sameModel, "controlled-artifact", "controlled-architecture",
                    "controlled-family", "GGUF", "Q4", new HashSet<string> { "streaming" }, 0, 0, 2048);
                var hardware = new InferenceHardware("controlled-hardware", "Linux", "x64", 4096, [], new HashSet<string>());
                var support = new InferenceEngineSupport(InferenceEngine.LlamaCpp, "controlled-build", true, null,
                    new HashSet<string> { "controlled-architecture" }, new HashSet<string> { "controlled-family" },
                    new HashSet<string> { "GGUF" }, new HashSet<string> { "Q4" }, new HashSet<string> { "streaming" },
                    new HashSet<string> { "Linux" }, new HashSet<string> { "x64" }, new HashSet<string>());
                var dispatcher = new InferenceEngineDispatcher(providerId, new Uri("http://127.0.0.1:9477"),
                    [new(InferenceEngine.LlamaCpp, new NeverSubstitutedFactory(this))],
                    new PreferenceObservations(new(requirements, hardware, [support])));
                return new PreferenceDispatcherLease(dispatcher);
            });
        }
    }
    private sealed class PreferenceObservations(InferenceRuntimeObservation original) : IInferenceRuntimeObservationSource
    {
        public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity sameModel,
            IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken)
            => Task.FromResult(original);
    }
    private sealed class NeverSubstitutedFactory(PreferenceControlComposition owner) : IInferenceEngineAdapterFactory
    {
        public Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity sameModel,
            IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken)
        { owner.RawFactoryCalls++; throw new InvalidOperationException("Manual Strata must not substitute this controlled Llama engine."); }
    }
    private sealed class PreferenceDispatcherLease(InferenceEngineDispatcher original) : OriginalInferenceEngineLease
    {
        public override IDulcheOriginalProviderAdapter Adapter => original;
        public override void DemandExternalOriginalJoin() => original.DemandExternalOriginalJoin();
        public override Task CloseOriginalAsync() => original.DisposeAsync().AsTask();
    }
}
