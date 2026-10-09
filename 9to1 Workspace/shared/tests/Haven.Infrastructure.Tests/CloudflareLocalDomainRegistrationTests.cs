using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Actual local Home/profile/store and maintained canonical DI owners; controlled principal
// callback failures only. No account/OAuth/remote effect or full Desktop readiness is asserted.
public sealed class CloudflareLocalDomainRegistrationTests
{
    [Fact]
    public async Task Actual_local_aliases_share_domain_and_require_original_start_before_setup()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var domainLifetime = new CancellationTokenSource();
        string? root = null; CloudflareLocalDomainRegistration? registration = null; ServiceProvider? provider = null;
        Exception? primary = null;
        try
        {
            root = NewRoot(); var paths = new Paths(root);
            registration = CloudflareLocalDomainRegistration.CreateOriginal(new(Path.Combine(root, "home.json")),
                new OperatingSystemPrincipalSource(), paths);
            var services = Graph(); registration.ConfigureOriginalServices(services); provider = services.BuildServiceProvider();
            var owner = registration.CaptureOriginalOwner(provider);
            Assert.Same(registration.OriginalHome, provider.GetRequiredService<HomeLocalDomainComposition>());
            Assert.Same(registration.OriginalHome.StateStore, provider.GetRequiredService<IHomeCoreStateStore>());
            Assert.Same(registration.OriginalHome.Profiles, provider.GetRequiredService<IAuthenticatedResourceActorSource>());
            Assert.Same(registration.OriginalHome.Permissions, provider.GetRequiredService<HomePermissionTrustService>());
            Assert.Same(registration.OriginalHome.Broker, provider.GetRequiredService<HomeResourceOperationBroker>());
            Assert.Same(provider.GetRequiredService<LinuxProviderSecretStore>(), provider.GetRequiredService<IProviderSecretStore>());
            Assert.Null(provider.GetService<HomeNativeWindowsComposition>());
            Assert.Throws<CloudflareSetupRequiredException>((Action)(() => { _ = owner.GetSetupAsync(domainLifetime.Token); }));
            var start = registration.StartOriginalHomeAsync(); Assert.Same(start, registration.OriginalStartTask); await start;
            Assert.Same(owner, registration.CaptureOriginalOwner(provider));
            var setup = await owner.GetSetupAsync(domainLifetime.Token);
            Assert.False(setup.Configured); Assert.Equal("CF_SETUP_REQUIRED", setup.Code);
        }
        catch (Exception cause) { primary = cause; }
        finally { await Cleanup(registration, provider, root, primary); }
    }

    [Fact]
    public async Task Actual_existing_home_or_foreign_secret_descriptor_is_not_replaced()
    {
        if (!OperatingSystem.IsLinux()) return;
        string? root = null; CloudflareLocalDomainRegistration? registration = null; Exception? primary = null;
        try
        {
            root = NewRoot(); var paths = new Paths(root); var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            registration = CloudflareLocalDomainRegistration.CreateOriginal(store, new OperatingSystemPrincipalSource(), paths);
            var existing = Graph(); existing.AddSingleton<IHomeCoreStateStore>(store);
            Assert.Throws<InvalidOperationException>(() => registration.ConfigureOriginalServices(existing));
            Assert.Same(store, existing.Last(row => row.ServiceType == typeof(IHomeCoreStateStore)).ImplementationInstance);
            var foreign = Graph(); foreign.AddSingleton<IProviderSecretStore>(new WindowsProviderSecretStore());
            Assert.Throws<InvalidOperationException>(() => registration.ConfigureOriginalServices(foreign));
            Assert.DoesNotContain(foreign, row => row.ServiceType == typeof(LinuxProviderSecretStore));
        }
        catch (Exception cause) { primary = cause; }
        finally { await Cleanup(registration, null, root, primary); }
    }

    [Fact]
    public async Task Actual_request_precedes_held_start_join_and_raw_principal_fault_is_conserved()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var domainLifetime = new CancellationTokenSource();
        string? root = null; CloudflareLocalDomainRegistration? registration = null; ServiceProvider? provider = null;
        Task? actualStart = null, actualClose = null; Exception? primary = null;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("controlled exact held principal failure");
        try
        {
            root = NewRoot(); var paths = new Paths(root);
            registration = CloudflareLocalDomainRegistration.CreateOriginal(new(Path.Combine(root, "home.json")),
                new Principal(token => { reached.TrySetResult(); return new(raw.Task); }), paths);
            var services = Graph(); registration.ConfigureOriginalServices(services); provider = services.BuildServiceProvider();
            var owner = registration.CaptureOriginalOwner(provider); actualStart = registration.StartOriginalHomeAsync();
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5), domainLifetime.Token);
            registration.RequestOriginalCloudflareRetirement();
            Assert.NotNull(registration.OriginalHome.OriginalProcessRetirementRequestTask);
            actualClose = registration.CloseAndDrainOriginalCloudflareAsync();
            Assert.False(actualStart.IsCompleted); Assert.False(actualClose.IsCompleted);
            Assert.Throws<ObjectDisposedException>((Action)(() => { _ = owner.GetSetupAsync(domainLifetime.Token); }));
            raw.TrySetException(failure);
            var startError = await Assert.ThrowsAsync<AggregateException>(() => actualStart);
            Assert.Contains(Leaves(startError), cause => ReferenceEquals(cause, failure));
            var closeError = await Assert.ThrowsAsync<AggregateException>(() => actualClose);
            Assert.Contains(Leaves(closeError), cause => ReferenceEquals(cause, failure));
            Assert.All(Leaves(closeError), cause => Assert.Same(failure, cause));
            Assert.Same(actualClose, registration.CloseAndDrainOriginalCloudflareAsync());
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            raw.TrySetException(failure);
            await Cleanup(registration, provider, root, primary, failure, actualStart, actualClose);
        }
    }

    [Fact]
    public async Task Real_profile_factory_restored_context_cannot_join_own_CF_parent()
    {
        if (!OperatingSystem.IsLinux()) return;
        string? root = null; CloudflareLocalDomainRegistration? registration = null; ServiceProvider? provider = null;
        Exception? primary = null; var earlier = ExecutionContext.Capture(); Assert.NotNull(earlier);
        var actual = new OperatingSystemPrincipalSource(); int callbacks = 0;
        try
        {
            root = NewRoot(); var paths = new Paths(root);
            registration = CloudflareLocalDomainRegistration.CreateOriginal(new(Path.Combine(root, "home.json")), new Principal(token =>
            {
                ExecutionContext.Run(earlier, _ =>
                {
                    callbacks++;
                    Assert.Throws<InvalidOperationException>((Action)(() => { _ = registration!.CloseAndDrainOriginalCloudflareAsync(); }));
                }, null);
                return actual.GetPrincipalAsync(token);
            }), paths);
            var services = Graph(); registration.ConfigureOriginalServices(services); provider = services.BuildServiceProvider();
            registration.CaptureOriginalOwner(provider); await registration.StartOriginalHomeAsync();
            Assert.True(callbacks > 1); Assert.True(registration.OriginalStartTask!.IsCompletedSuccessfully);
        }
        catch (Exception cause) { primary = cause; }
        finally { await Cleanup(registration, provider, root, primary); }
    }

    private static string NewRoot()
    {
        var root = Directory.CreateTempSubdirectory("cloudflare-domain-").FullName;
        try { if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original local Cloudflare fixture requires Linux."); File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); return root; }
        catch (Exception primary)
        {
            try { Directory.Delete(root, true); }
            catch (Exception cleanup) { throw new AggregateException("Actual new controlled root and cleanup failed.", primary, cleanup); }
            throw;
        }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
    private sealed class Principal(Func<CancellationToken, ValueTask<string?>> actual) : ITrustedHostPrincipalSource
    { public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => actual(token); }

    private static ServiceCollection Graph()
    {
        var services = new ServiceCollection(); services.AddHavenInfrastructure();
        services.AddHavenPlannerInfrastructure();
        // Maintained required Chat descriptors, supported nullable Browser/Automation absent.
        // This constructs the original cohort; it does not issue an admission or initialize a model.
        services.AddSingleton<CapabilityPreflightService>(); services.AddSingleton<TerminalCommandActivityHub>();
        services.AddSingleton<WorkspaceToolRuntime>(); services.AddSingleton<ComputerToolRuntime>();
        services.AddSingleton<ChatSessionService>(provider => new ChatSessionService(
            provider.GetRequiredService<IConversationRepository>(), provider.GetRequiredService<IProviderModelClient>(),
            provider.GetRequiredService<CapabilityPreflightService>(), provider.GetRequiredService<IConversationSafetyService>(),
            provider.GetRequiredService<WorkspaceToolRuntime>(), provider.GetRequiredService<ComputerToolRuntime>(),
            mcpTools: provider.GetRequiredService<McpToolRuntime>(), calendarTools: provider.GetRequiredService<CalendarConnectionToolRuntime>(),
            pluginTools: provider.GetRequiredService<PluginToolRuntime>(), executionEvents: provider.GetRequiredService<IExecutionEventSink>(),
            recovery: provider.GetRequiredService<AutonomousRecoveryService>(), remediations: provider.GetRequiredService<RemediationCoordinator>(),
            personalities: provider.GetRequiredService<ModelPersonalityService>(), modelPermissions: provider.GetRequiredService<ModelPermissionEvaluator>(),
            defaultProviders: provider.GetRequiredService<IDefaultProviderStore>(), checkpoints: provider.GetRequiredService<CheckpointService>(),
            projectInstructionFiles: provider.GetRequiredService<IProjectInstructionSource>(), memorySource: provider.GetRequiredService<IMemoryQuerySource>(),
            taskCoordinator: provider.GetRequiredService<TaskExecutionCoordinator>(), taskToolOwner: provider.GetRequiredService<ITaskRunToolActionOwner>(),
            taskProviderContextCapture: provider.GetRequiredService<ITaskRunProviderContextCapture>(),
            taskCloudPermissionRemediation: provider.GetRequiredService<TaskRunCloudPermissionRemediationOwner>()));
        services.AddSingleton<AgentTaskRuntimeService>(); return services;
    }

    private static IEnumerable<Exception> Leaves(Exception cause) => cause is AggregateException group
        ? group.InnerExceptions.SelectMany(Leaves) : [cause];
    private static async Task Cleanup(CloudflareLocalDomainRegistration? registration, ServiceProvider? provider,
        string? root, Exception? primary, Exception? expected = null, params Task?[] prior)
    {
        var errors = new List<Exception>(); if (primary is not null) errors.Add(primary);
        var originals = prior.OfType<Task>().ToList();
        Task? cf = null, home = null, disposal = null;
        // Acquire actual CF close even if a body/assertion failed; this also drains SAME canonical owners.
        try { if (registration is not null) cf = registration.CloseAndDrainOriginalCloudflareAsync(); } catch (Exception cause) { Add(cause); }
        if (cf is not null) originals.Add(cf);
        foreach (var original in originals.Distinct<Task>(ReferenceEqualityComparer.Instance)) await Join(original);
        // These controls create no Dev/Files borrower. Actual runner independently joins them first.
        try { if (registration is not null) home = registration.CloseOriginalHomeAfterBorrowersAsync(); } catch (Exception cause) { Add(cause); }
        if (home is not null) await Join(home);
        try { if (provider is not null) disposal = provider.DisposeAsync().AsTask(); } catch (Exception cause) { Add(cause); }
        if (disposal is not null) await Join(disposal);
        try { if (root is not null) Directory.Delete(root, true); } catch (Exception cause) { Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual domain acquisition/body and independently joined originals failed.", errors);
        void Add(Exception cause)
        {
            foreach (var leaf in Leaves(cause))
                if (!ReferenceEquals(leaf, expected) && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
        async Task Join(Task original)
        { try { await original.ConfigureAwait(false); } catch (Exception cause) { Add(original.Exception ?? cause); } }
    }
}
