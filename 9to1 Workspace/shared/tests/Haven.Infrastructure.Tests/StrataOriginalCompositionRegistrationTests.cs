using System.Reflection;
using Dulche.Runtime;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Haven.Infrastructure.Tests;

// A reduced maintained descriptor graph, not full Desktop bootstrap/model readiness.
// The genuine authority constructor has an empty provider catalogue; no admission is issued.
public sealed partial class StrataOriginalCompositionRegistrationTests
{
    [Fact]
    public async Task Actual_lazy_source_aliases_reuse_configured_owners_and_refuse_taskless_observation()
    {
        string? directory = null; ProviderConfigurationStore? configurations = null;
        Exception? primary = null; ServiceProvider? provider = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("strata-di-").FullName;
            var paths = new Paths(directory);
            configurations = new ProviderConfigurationStore(paths);
            var authority = new TaskRunPermissionAuthority(new HostLocalTaskActorSource(), new ModelProviderRegistry([]),
                configurations, new PrivacyPreferenceStore(paths),
                new ModelPermissionEvaluator(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(paths))));
            var coordinator = new TaskExecutionCoordinator(null!, null!, null);
            var services = new ServiceCollection(); services.AddHavenInfrastructure();
            // Actual maintained instances replace unrelated host graph requirements for this identity-only control.
            services.AddSingleton(coordinator); services.AddSingleton(authority);
            provider = services.BuildServiceProvider();
            var artifacts = provider.GetRequiredService<StrataNativeArtifactSource>();
            Assert.Same(authority, provider.GetRequiredService<ITaskRunOriginalInferenceAdmissionSource>());
            Assert.Same(coordinator, provider.GetRequiredService<ITaskRunOriginalInferenceAttemptSource>());
            Assert.Same(artifacts, provider.GetRequiredService<IOriginalStrataModelSource>());
            Assert.Same(artifacts, provider.GetRequiredService<IOriginalStrataWorkerSource>());
            Assert.Same(coordinator, Field(artifacts, "_tasks")); Assert.Same(authority, Field(artifacts, "_authority"));
            Assert.Null(Field(artifacts, "_installations"));
            var observations = provider.GetRequiredService<StrataRuntimeObservationSource>();
            Assert.Same(observations, provider.GetRequiredService<IInferenceRuntimeObservationSource>());
            Assert.Same(observations, provider.GetRequiredService<IStrataOriginalRequestObservationSource>());
            var composition = provider.GetRequiredService<ManagedDulcheInferenceComposition>();
            Assert.Same(composition, provider.GetRequiredService<IManagedDulcheInferenceCompositionSource>());
            Assert.Same(artifacts, Field(composition, "_strataModels")); Assert.Same(artifacts, Field(composition, "_strataBinaries"));
            Assert.Same(observations, Field(composition, "_observations"));
            var scope = new Scope();
            var original = observations.ObserveOriginalAsync(new("strata", "model"), scope, default);
            var refusal = await Assert.ThrowsAsync<InferenceEngineException>(() => original);
            Assert.Equal("STRATA_ORIGINAL_REQUEST_MODEL_USE_ADMISSION_REQUIRED", refusal.Error.Message);
            Assert.Empty(scope.Originals); Assert.True(original.IsFaulted);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await CloseActualFixtureAsync(provider, configurations, directory, primary).ConfigureAwait(false);
        }
    }
    [Fact]
    public async Task Actual_response_alias_and_typed_consumer_reuse_the_same_runtime_and_router_singletons()
    {
        string? directory = null; ProviderConfigurationStore? configurations = null;
        Exception? primary = null; ServiceProvider? provider = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("strata-consumer-di-").FullName;
            var paths = new Paths(directory);
            configurations = new ProviderConfigurationStore(paths);
            var authority = new TaskRunPermissionAuthority(new HostLocalTaskActorSource(), new ModelProviderRegistry([]),
                configurations, new PrivacyPreferenceStore(paths),
                new ModelPermissionEvaluator(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(paths))));
            var coordinator = new TaskExecutionCoordinator(null!, null!, null);
            var services = new ServiceCollection(); services.AddHavenInfrastructure();
            // Real maintained descriptors with an isolated path and an empty, unissued authority graph.
            services.AddSingleton<IAppPaths>(paths); services.AddSingleton<IProviderConfigurationStore>(configurations);
            services.AddSingleton(coordinator); services.AddSingleton(authority);
            provider = services.BuildServiceProvider();
            Assert.Same(authority, provider.GetRequiredService<ITaskRunOriginalResponseAdmissionSource>());
            var runtime = provider.GetRequiredService<ManagedDulcheRuntimeService>();
            Assert.Same(runtime, provider.GetRequiredService<IManagedDulcheOriginalModelRequestConsumer>());
            var router = provider.GetRequiredService<ResilientProviderRoutingModelClient>();
            Assert.Same(router, provider.GetRequiredService<IProviderModelClient>());
            Assert.Same(router, provider.GetRequiredService<ITaskRunOriginalToolResponseDispatchWitnessSource>());
            // Inspect the actual retained constructor object, without invoking admission, model startup or inference.
            Assert.Contains(router.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field =>
                typeof(IManagedDulcheOriginalModelRequestConsumer).IsAssignableFrom(field.FieldType) &&
                ReferenceEquals(field.GetValue(router), runtime));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await CloseActualFixtureAsync(provider, configurations, directory, primary).ConfigureAwait(false);
        }
    }
    private static async Task CloseActualFixtureAsync(ServiceProvider? provider,
        ProviderConfigurationStore? configurations, string? directory, Exception? primary)
    {
        var failures = new List<Exception>(); Task? close = null;
        try { if (provider is not null) close = provider.DisposeAsync().AsTask(); }
        catch (Exception error) { Add(error); }
        if (close is not null)
        {
            try { await close.ConfigureAwait(false); }
            catch (Exception error)
            {
                Add(error);
                if (close.Exception is { } group)
                { Add(group); foreach (var cause in group.InnerExceptions) Add(cause); }
            }
        }
        try { configurations?.Dispose(); } catch (Exception error) { Add(error); }
        try { if (directory is not null) Directory.Delete(directory, true); }
        catch (Exception error) { Add(error); }
        if (failures.Count != 0)
        {
            if (primary is not null) failures.Insert(0, primary);
            throw new AggregateException("Actual DI acquisition/body and independently joined cleanup failed.", failures);
        }
        void Add(Exception actual)
        { if (!failures.Any(value => ReferenceEquals(value, actual))) failures.Add(actual); }
    }
    private static object? Field(object actual, string field) => actual.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(actual);
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
    private sealed class Scope : IInferenceEngineOriginalSourceScope
    {
        public readonly List<Task> Originals = [];
        public T InvokeOriginalFactory<T>(Func<T> factory) => factory();
        public T InvokeOriginalCleanup<T>(Func<T> factory) => factory();
        public void RetainOriginalTask(Task actual) => Originals.Add(actual);
    }
}
