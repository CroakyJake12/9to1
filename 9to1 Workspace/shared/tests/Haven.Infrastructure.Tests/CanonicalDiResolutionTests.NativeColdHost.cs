using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalDiResolutionTests08
{
    [Theory]
    [InlineData(null, NativePersonalTaskColdRecoveryConfigurationKind.Disabled)]
    [InlineData("0", NativePersonalTaskColdRecoveryConfigurationKind.Disabled)]
    [InlineData("true", NativePersonalTaskColdRecoveryConfigurationKind.MalformedConfiguration)]
    [InlineData("unexpected", NativePersonalTaskColdRecoveryConfigurationKind.MalformedConfiguration)]
    public void Native_cold_host_missing_or_malformed_opt_in_refuses_before_original_journal_invocation(
        string? selector, NativePersonalTaskColdRecoveryConfigurationKind expected)
    {
        var configuration = NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn(selector);
        var host = new NativePersonalTaskColdRecoveryHost(configuration);
        Assert.Equal(expected, host.ConfigurationStatus.Kind);
        var failure = Assert.Throws<NativePersonalTaskColdRecoverySetupRequiredException>((Action)(() =>
        { _ = host.ObserveOriginalInputAsync(Guid.NewGuid(), Guid.NewGuid(), default); }));
        Assert.Equal(expected, failure.OriginalStatus.Kind);
        Assert.Throws<NativePersonalTaskColdRecoverySetupRequiredException>((Action)(() =>
        { _ = host.StartObservedOriginalResumeAsync(Guid.NewGuid(), Guid.NewGuid(), default); }));
        Assert.False(host.HasOriginalComposition(null!, null!, null!));
    }
    [Fact]
    public void Native_cold_registration_requires_normal_canonical_prefix_and_denies_second_or_foreign_source()
    {
        var configuration = NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn("0");
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddHavenOwnedNativeTaskColdRecovery(configuration));
        var services = new ServiceCollection(); services.AddHavenInfrastructure();
        services.AddHavenOwnedNativeTaskColdRecovery(configuration);
        Assert.Throws<InvalidOperationException>(() => services.AddHavenOwnedNativeTaskColdRecovery(configuration));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ITaskRunColdRecoveryJournal));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ITaskRunColdContextAuthority));
    }
    [Fact]
    public Task Default_infrastructure_has_no_cold_producer_and_preserves_actual_normal_authority()
        => InspectAsync(provider =>
        {
            Assert.Null(provider.GetService<NativePersonalTaskColdRecoveryHost>());
            Assert.Null(provider.GetService<ITaskRunColdRecoveryJournal>());
            var authority = provider.GetRequiredService<TaskRunPermissionAuthority>();
            Assert.False(authority.HasOriginalColdRecoveryComposition(null!, null!));
            Assert.False(authority.IsOriginalAdmissionSealed);
            return Task.CompletedTask;
        });
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Native_opt_in_configures_same_sources_before_exposure_without_claiming_protected_store_or_fresh_activation()
    {
        string? root = null; ServiceProvider? provider = null;
        var failures = new List<Exception>(); TaskRunCanonicalProcessRetirementOwner? cohort = null;
        try
        {
            root = Path.Combine(Path.GetTempPath(), "haven-native-cold-di", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var paths = new Paths(root); var services = new ServiceCollection(); services.AddHavenInfrastructure();
            services.AddSingleton<IAppPaths>(paths);
            services.AddHavenOwnedNativeTaskColdRecovery(NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn("1"));
            // Same maintained required host prefix as the existing identity control; nullable
            // Browser/Automation ports stay absent. This is not a complete native host or restart test.
            services.AddSingleton<CapabilityPreflightService>(); services.AddSingleton<TerminalCommandActivityHub>();
            services.AddSingleton<WorkspaceToolRuntime>(); services.AddSingleton<ComputerToolRuntime>();
            services.AddSingleton<ChatSessionService>(provider => new ChatSessionService(
                provider.GetRequiredService<IConversationRepository>(), provider.GetRequiredService<IProviderModelClient>(),
                provider.GetRequiredService<CapabilityPreflightService>(), provider.GetRequiredService<IConversationSafetyService>(),
                provider.GetRequiredService<WorkspaceToolRuntime>(), provider.GetRequiredService<ComputerToolRuntime>(),
                taskCoordinator: provider.GetRequiredService<TaskExecutionCoordinator>(),
                taskToolOwner: provider.GetRequiredService<ITaskRunToolActionOwner>(),
                taskProviderContextCapture: provider.GetRequiredService<ITaskRunProviderContextCapture>(),
                taskCloudPermissionRemediation: provider.GetRequiredService<TaskRunCloudPermissionRemediationOwner>()));
            services.AddSingleton<AgentTaskRuntimeService>();
            provider = services.BuildServiceProvider();
            var authority = provider.GetRequiredService<TaskRunPermissionAuthority>();
            var journal = provider.GetRequiredService<SqliteTaskRunColdRecoveryJournal>();
            var host = provider.GetRequiredService<NativePersonalTaskColdRecoveryHost>();
            Assert.True(authority.HasOriginalColdRecoveryComposition(journal, journal));
            Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified, host.ConfigurationStatus.Kind);
            var coordinator = provider.GetRequiredService<TaskExecutionCoordinator>();
            Assert.True(coordinator.HasOriginalColdRecoveryComposition(journal, journal));
            Assert.True(host.HasOriginalComposition(journal, authority, coordinator));
            Assert.True(journal.HasOriginalComposition(provider.GetRequiredService<SqliteDatabase>(), paths,
                provider.GetRequiredService<HostLocalTaskActorSource>()));
            Assert.Same(journal, provider.GetRequiredService<ITaskRunColdRecoveryJournal>());
            Assert.Same(journal, provider.GetRequiredService<ITaskRunColdContextAuthority>());
            Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.ConfiguredUnverified, host.ConfigurationStatus.Kind);
            Assert.False(File.Exists(paths.DatabasePath)); Assert.False(File.Exists(Path.Combine(root, ".task-recovery-auth.v1")));
            cohort = provider.GetRequiredService<TaskRunCanonicalProcessRetirementOwner>();
            Assert.True(cohort.HasOriginalComposition(coordinator, provider.GetRequiredService<AgentTaskRuntimeService>(), authority,
                provider.GetRequiredService<TaskRunOriginalFrameOwner>()));
            cohort.RequestOriginalProcessRetirement();
            Assert.True(authority.IsOriginalAdmissionSealed);
            Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = host.ObserveOriginalInputAsync(Guid.NewGuid(), Guid.NewGuid(), default); }));
            Assert.False(File.Exists(paths.DatabasePath));
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            Task? actualClose = null;
            if (cohort is not null)
            {
                try { cohort.RequestOriginalProcessRetirement(); actualClose = cohort.CloseAndSuspendOriginalProducersAsync(); }
                catch (Exception cause) { Add(failures, cause); }
                if (actualClose is not null) try { await actualClose; } catch (Exception cause) { AddTask(failures, cause, actualClose); }
            }
            Task? actualProviderClose = null;
            try { if (provider is not null) actualProviderClose = provider.DisposeAsync().AsTask(); } catch (Exception cause) { Add(failures, cause); }
            if (actualProviderClose is not null) try { await actualProviderClose; } catch (Exception cause) { AddTask(failures, cause, actualProviderClose); }
            try { if (root is not null && Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception cause) { Add(failures, cause); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
    [Fact]
    public Task Normal_cold_router_alias_is_the_same_router_and_default_journal_remains_absent() => InspectAsync(provider =>
    {
        var router = provider.GetRequiredService<ResilientProviderRoutingModelClient>();
        Assert.Same(router, provider.GetRequiredService<ITaskRunColdToolCheckpointSelectionSource>());
        Assert.Null(provider.GetService<ITaskRunColdRecoveryJournal>());
        Assert.Same(provider.GetRequiredService<TaskRunPermissionAuthority>(), provider.GetRequiredService<ITaskRunOriginalSelectedRouteCaptureSource>());
        return Task.CompletedTask;
    });

}
