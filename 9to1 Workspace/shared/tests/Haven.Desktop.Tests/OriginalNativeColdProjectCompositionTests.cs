using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Haven.Desktop.Tests;

public sealed class OriginalNativeColdProjectCompositionTests
{
    [Fact]
    public Task Actual_project_configuration_is_fixed_on_the_same_journal_before_exposure_without_any_read_or_claim()
        => WithGraph("1", async (services, registration, paths, captureSource) =>
        {
            var originalStore = services.Single(row => row.ServiceType == typeof(FileDeveloperWorkspaceStore));
            services.AddHavenOwnedNativeColdProjectResources(registration.OriginalHome);
            Assert.Same(originalStore, services.Single(row => row.ServiceType == typeof(FileDeveloperWorkspaceStore)));
            Assert.Throws<InvalidOperationException>(() => services.AddHavenOwnedNativeColdProjectResources(registration.OriginalHome));
            ServiceProvider? provider = null; HomeColdProjectReadReconciliation? source = null; Task? close = null; Exception? primary = null; var errors = new List<Exception>();
            try
            {
                provider = services.BuildServiceProvider();
                var journal = provider.GetRequiredService<SqliteTaskRunColdRecoveryJournal>();
                source = provider.GetRequiredService<HomeColdProjectReadReconciliation>(); captureSource(source);
                Assert.Same(source, journal.RequireOriginalProjectResourceSource());
                Assert.Same(source, provider.GetRequiredService<ITaskRunColdProjectResourceSource>());
                Assert.Same(source, provider.GetRequiredService<IDeveloperOriginalCurrentProjectReadAdmissionSource>());
                Assert.Same(source, provider.GetRequiredService<IDeveloperOriginalProjectCommandReadSource>());
                Assert.True(source.HasOriginalColdProjectComposition(journal, provider.GetRequiredService<HostLocalTaskActorSource>()));
                Assert.Same(journal, provider.GetRequiredService<ITaskRunColdRecoveryJournal>());
                Assert.False(File.Exists(paths.DatabasePath)); Assert.False(File.Exists(Path.Combine(paths.DataDirectory, ".task-recovery-auth.v1")));
                Assert.False(File.Exists(Path.Combine(paths.DataDirectory, "Home", "state.json")));
            }
            catch (Exception cause) { primary = cause; }
            finally
            {
                try { if (source is not null) { source.RequestOriginalRetirement(); close = source.CloseAndDrainOriginalAsync(); } } catch (Exception cause) { errors.Add(cause); }
                if (close is not null) try { await close; } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
                Task? providerClose = null;
                try { if (provider is not null) providerClose = provider.DisposeAsync().AsTask(); } catch (Exception cause) { errors.Add(cause); }
                if (providerClose is not null) try { await providerClose; } catch (Exception cause) { errors.Add(providerClose.Exception ?? cause); }
            }
            Throw(primary, errors);
        });
    [Fact]
    public Task Disabled_opt_in_keeps_project_resources_unavailable_before_provider_or_journal_operations()
        => WithGraph("0", (services, registration, paths, _) =>
        {
            var refusal = Assert.Throws<NativePersonalTaskColdRecoverySetupRequiredException>(() => services.AddHavenOwnedNativeColdProjectResources(registration.OriginalHome));
            Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.Disabled, refusal.OriginalStatus.Kind);
            Assert.DoesNotContain(services, row => row.ServiceType == typeof(HomeColdProjectReadReconciliation));
            Assert.False(File.Exists(paths.DatabasePath)); return Task.CompletedTask;
        });
    private static async Task WithGraph(string selector, Func<ServiceCollection, CloudflareLocalDomainRegistration, Paths, Action<HomeColdProjectReadReconciliation>, Task> body)
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("original-project-di-").FullName;
        CloudflareLocalDomainRegistration? registration = null; Exception? primary = null; var errors = new List<Exception>();
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var homeRoot = Path.Combine(directory, "Home"); Directory.CreateDirectory(homeRoot);
            File.SetUnixFileMode(homeRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var paths = new Paths(directory); var store = new FileHomeCoreStateStore(Path.Combine(homeRoot, "state.json"));
            NativeFilesWorkspaceService? files = null; NativeFilesWorkspaceAuthority? authority = null;
            HomeColdProjectReadReconciliation? source = null;
            registration = CloudflareLocalDomainRegistration.CreateOriginal(store, new OperatingSystemPrincipalSource(), paths,
                originalResolvers: [new HomeColdProjectReadResourceResolver(() => source ?? throw new InvalidOperationException("Unresolved source is unavailable"))],
                originalPolicies: [new HomeColdProjectReadActionPolicySource()],
                configureOriginalStores: identity =>
                {
                    files = new(identity.StateStore, identity.Profiles);
                    return new([files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files });
                },
                configureOriginalResolvers: components =>
                {
                    authority = new(files!, components.Identity.Profiles, components.Ownership);
                    return new([], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceAuthority)] = authority });
                });
            var services = new ServiceCollection(); services.AddHavenInfrastructure();
            services.AddHavenOwnedNativeTaskColdRecovery(NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn(selector));
            registration.ConfigureOriginalServices(services); services.AddSingleton(files!); services.AddSingleton(authority!);
            services.AddFilesNativeHost(); services.AddHavenOriginalNativeDevelopment();
            await body(services, registration, paths, actual => source = actual);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            Task? homeClose = null;
            try { if (registration is not null) { registration.OriginalHome.RequestOriginalProcessRetirement(); homeClose = registration.OriginalHome.CloseAndDrainAsync(); } } catch (Exception cause) { errors.Add(cause); }
            if (homeClose is not null) try { await homeClose; } catch (Exception cause) { errors.Add(homeClose.Exception ?? cause); }
            try { Directory.Delete(directory, true); } catch (Exception cause) { errors.Add(cause); }
        }
        Throw(primary, errors);
    }
    private static void Throw(Exception? primary, List<Exception> cleanup)
    {
        if (cleanup.Count != 0) throw new AggregateException("Actual project composition and independent cleanup failed.", primary is null ? cleanup : new[] { primary }.Concat(cleanup));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
