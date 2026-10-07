using Dulche.Runtime;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class StrataOriginalCompositionRegistrationTests
{
    [Fact]
    public async Task Actual_configured_engine_preferences_and_settings_alias_borrow_same_runtime()
    {
        string? directory = null; ProviderConfigurationStore? configurations = null;
        Exception? primary = null; ServiceProvider? provider = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("inference-preference-di-").FullName;
            var paths = new Paths(directory); configurations = new(paths);
            var authority = new TaskRunPermissionAuthority(new HostLocalTaskActorSource(), new ModelProviderRegistry([]),
                configurations, new PrivacyPreferenceStore(paths),
                new ModelPermissionEvaluator(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(paths))));
            var coordinator = new TaskExecutionCoordinator(null!, null!, null);
            var services = new ServiceCollection(); services.AddHavenInfrastructure();
            // SAME maintained reduced identity-only graph: no attempt/model is admitted or initialized.
            services.AddSingleton<IAppPaths>(paths); services.AddSingleton<IProviderConfigurationStore>(configurations);
            services.AddSingleton(coordinator); services.AddSingleton(authority);
            provider = services.BuildServiceProvider();
            var preferences = provider.GetRequiredService<ConfiguredInferenceEnginePreferences>();
            Assert.Same(preferences, provider.GetRequiredService<IInferenceEnginePreferenceSource>());
            var runtime = provider.GetRequiredService<ManagedDulcheRuntimeService>();
            Assert.Same(runtime, provider.GetRequiredService<IManagedDulcheOriginalModelRequestConsumer>());
            Assert.Same(runtime, provider.GetRequiredService<IManagedDulcheOriginalInferenceSettingsSource>());
            Assert.Same(preferences, Field(runtime, "_inferencePreferences"));
            var model = new ModelIdentity("controlled", "selected");
            Assert.Equal(InferenceEngine.Automatic, preferences.GetRequestedInferenceEngine(model));
            preferences.SetRequestedInferenceEngine(model, InferenceEngine.LlamaCpp);
            Assert.Equal(InferenceEngine.LlamaCpp, preferences.GetRequestedInferenceEngine(model));
            Assert.False(authority.IsOriginalAdmissionSealed);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await CloseActualFixtureAsync(provider, configurations, directory, primary); }
    }
}
