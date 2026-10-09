using System.Reflection;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Desktop.Tests;

/// <summary>Windows64 owning configuration and retirement controls. No UI/bootstrap,
/// installed peer, protected disk, manual grant, or process execution receipt is inferred.
/// Synthetic encompassing callback scopes are refusal controls only.</summary>
[Trait("Platform", "Windows64")]
public sealed class WindowsNativeDeveloperCompositionTests
{
    [Fact]
    public async Task Typed_setup_aliases_and_lazy_cycles_use_same_Home_and_kernel_without_disk_or_principal_reads()
    {
        await using var control = new Control("0");
        control.Capture();
        Assert.Same(control.ReadPolicy, control.Provider.GetRequiredService<HomeDeveloperProjectReadActionPolicySource>());
        Assert.Same(control.SetupPolicy, control.Provider.GetRequiredService<HomeDeveloperProjectSetupActionPolicySource>());
        Assert.Same(control.ReadResolver, control.Provider.GetRequiredService<HomeDeveloperProjectReadResourceResolver>());
        var reads = control.Provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>();
        Assert.Same(reads, control.Provider.GetRequiredService<IDeveloperProjectOriginalReadAdmissionSource>());
        Assert.Same(reads, control.Provider.GetRequiredService<IDeveloperProjectOriginalReadRetirementSource>());
        Assert.Same(reads, control.Provider.GetRequiredService<IDeveloperProjectOriginalReadAdmissionJoinGuard>());
        Assert.Same(control.Provider.GetRequiredService<HomeDeveloperProjectSetupJournal>(),
            control.Provider.GetRequiredService<IDeveloperProjectOriginalSetupCompletionSource>());
        Assert.Same(control.Provider.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>(),
            control.Provider.GetRequiredService<IDeveloperProjectOriginalSetupPermissionSource>());
        Assert.Same(control.Provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(),
            control.Provider.GetRequiredService<IDeveloperProjectOriginalCaptureAuthority>());
        Assert.Null(control.Provider.GetService<SqliteTaskRunColdRecoveryJournal>());
        Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.Disabled,
            control.Provider.GetRequiredService<NativePersonalTaskColdRecoveryHost>().ConfigurationStatus.Kind);
        control.AssertZeroOperations();
    }

    [Fact]
    public async Task Explicit_requested_source_wraps_same_journal_and_canonical_Task_owner_without_readiness_or_claim()
    {
        await using var control = new Control("1");
        control.Capture();
        var journal = control.Provider.GetRequiredService<SqliteTaskRunColdRecoveryJournal>();
        var projects = control.Provider.GetRequiredService<HomeColdProjectReadReconciliation>();
        var actors = control.Provider.GetRequiredService<HostLocalTaskActorSource>();
        Assert.Same(projects, journal.RequireOriginalProjectResourceSource());
        Assert.True(projects.HasOriginalColdProjectComposition(journal, actors));
        Assert.Same(projects, control.Provider.GetRequiredService<ITaskRunColdProjectResourceSource>());
        Assert.Same(projects, control.Provider.GetRequiredService<IDeveloperOriginalProjectCommandReadSource>());
        Assert.Same(journal, control.Provider.GetRequiredService<ITaskRunColdRecoveryJournal>());
        Assert.Same(journal, control.Provider.GetRequiredService<ITaskRunColdContextAuthority>());
        var authority = control.Provider.GetRequiredService<TaskRunPermissionAuthority>();
        var coordinator = control.Provider.GetRequiredService<TaskExecutionCoordinator>();
        Assert.True(authority.HasOriginalColdRecoveryComposition(journal, journal));
        Assert.True(coordinator.HasOriginalColdRecoveryComposition(journal, journal));
        var bridge = control.Provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>();
        Assert.Same(bridge, control.Provider.GetRequiredService<IDeveloperWorkspaceTrustService>());
        Assert.Same(bridge, control.Provider.GetRequiredService<IDeveloperWorkspaceOriginalExecutionBindingSource>());
        Assert.Same(bridge, control.Provider.GetRequiredService<IDeveloperWorkspaceOriginalExecutionScopedBindingSource>());
        Assert.Same(bridge, control.Provider.GetRequiredService<IDeveloperWorkspaceOriginalExecutionCommitBindingSource>());
        Assert.Same(bridge, control.Provider.GetRequiredService<IDeveloperWorkspaceOriginalExecutionPinCustodySource>());
        Assert.True(bridge.IsBoundToOriginalToolOwner(control.Provider.GetRequiredService<ITaskRunToolActionOwner>()));
        Assert.Same(control.Provider.GetRequiredService<HomeDeveloperWorkspaceExecutionConsentSource>(),
            control.Provider.GetRequiredService<IWorkspaceOriginalProcessStartConsentSource>());
        Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.ConfiguredUnverified,
            control.Provider.GetRequiredService<NativePersonalTaskColdRecoveryHost>().ConfigurationStatus.Kind);
        Assert.False(authority.IsOriginalAdmissionSealed);
        Assert.Null(control.Provider.GetService<IHomeNativeStartupSession>());
        Assert.False(control.Home.InstalledPeerAdmissionConfigured);
        control.AssertZeroOperations();
    }

    [Fact]
    public async Task Duplicate_and_foreign_Home_aliases_refuse_before_replacing_descriptors()
    {
        await using var control = new Control("0");
        var rows = control.Services.ToArray();
        Assert.Throws<InvalidOperationException>(() => control.Services.AddHavenOwnedDeveloperSourceReads(control.Home,
            provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), control.ReadResolver, control.ReadPolicy));
        Assert.Equal(rows, control.Services.ToArray());
        var foreign = new ServiceCollection();
        foreach (var row in control.Services.Where(row => row.ServiceType != typeof(HomeNativeWindowsComposition) &&
            row.ServiceType != typeof(HomeDeveloperProjectReadAdmissionSource) &&
            row.ServiceType != typeof(IDeveloperProjectOriginalReadAdmissionSource) &&
            row.ServiceType != typeof(IDeveloperProjectOriginalReadRetirementSource) &&
            row.ServiceType != typeof(IDeveloperProjectOriginalReadAdmissionJoinGuard) &&
            row.ServiceType != typeof(HomeDeveloperProjectReadResourceResolver) &&
            row.ServiceType != typeof(HomeDeveloperProjectReadActionPolicySource))) foreign.Add(row);
        rows = foreign.ToArray();
        Assert.Throws<InvalidOperationException>(() => foreign.AddHavenOwnedDeveloperSourceReads(control.Home,
            provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), control.ReadResolver, control.ReadPolicy));
        Assert.Equal(rows, foreign.ToArray());
        control.AssertZeroOperations();
    }

    [Fact]
    public async Task Live_or_restored_callback_cannot_partially_retire_developer_dependencies_and_external_close_coalesces()
    {
        await using var control = new Control("1"); control.Capture();
        var reads = control.Provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>();
        var before = ExecutionContext.Capture();
        using (CloudflareOriginalExecutionGuard.EnterOriginal(reads))
            Assert.Throws<InvalidOperationException>(() => control.CloseDeveloper());
        CloudflareOriginalExecutionGuard.InvokeOriginal(reads, () =>
        {
            ExecutionContext.Run(before!, _ => Assert.Throws<InvalidOperationException>(() => control.CloseDeveloper()), null);
            return true;
        });
        Assert.Null(control.Field("_actualWindowsDeveloperDrain"));
        Assert.False(control.Provider.GetRequiredService<TaskRunPermissionAuthority>().IsOriginalAdmissionSealed);
        Assert.Null(control.Home.OriginalCloseTask);
        var actual = control.CloseDeveloper(); Assert.Same(actual, control.CloseDeveloper()); await actual;
        Assert.Null(control.Home.OriginalCloseTask);
        control.AssertZeroOperations();
    }

    [Fact]
    public async Task Held_actual_permission_original_keeps_completion_dependencies_live_and_preserves_fault_siblings()
    {
        await using var control = new Control("0"); control.Capture();
        var actor = await control.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new IOException("Actual held setup source fault.");
        var second = new OperationCanceledException("Faulted finite source sibling; no canceled original.");
        control.ExpectedFailures.Add(first); control.ExpectedFailures.Add(second);
        control.BeforeScopes = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Release the owning finite source control.");
            throw new AggregateException(first, second);
        };
        var permissions = control.Provider.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>();
        // These public metadata values are deliberate negatives. No genuine Files
        // capture/binding or manual permission is issued. The real admission driver
        // enters its actual configured finite source before that refusal can settle.
        var intent = NegativeIntent(actor!);
        Assert.Null(intent.Validate());
        var acquisition = permissions.AcquireOriginalAsync(intent, new NegativeCapture(), CancellationToken.None);
        Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            close = control.CloseDeveloper();
            var permissionClose = permissions.CloseAndDrainOriginalSetupsAsync();
            Assert.False(permissionClose.IsCompleted); Assert.False(close.IsCompleted);
            Assert.False((bool)typeof(HomeDeveloperProjectSetupJournal).GetField("_retiring", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(control.Provider.GetRequiredService<HomeDeveloperProjectSetupJournal>())!);
            Assert.False((bool)typeof(FilesDeveloperOriginalSourceSelection).GetField("_retiring", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(control.Provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>())!);
            Assert.Null(control.Home.OriginalCloseTask);
        }
        finally { release.Set(); }
        var acquisitionError = await Assert.ThrowsAnyAsync<Exception>(() => acquisition);
        Assert.True(acquisition.IsFaulted);
        Assert.Contains(Causes(acquisitionError), value => ReferenceEquals(first, value));
        Assert.Contains(Causes(acquisitionError), value => ReferenceEquals(second, value));
        if (close is not null)
        {
            var closeError = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(close.IsFaulted); Assert.Same(close, control.CloseDeveloper());
            Assert.Contains(Causes(closeError), value => ReferenceEquals(first, value));
            Assert.Contains(Causes(closeError), value => ReferenceEquals(second, value));
        }
        Assert.Null(control.Home.OriginalCloseTask);
        Assert.False(File.Exists(Path.Combine(control.Root, ".task-recovery-auth.v1")));
        Assert.False(File.Exists(Path.Combine(control.Root, "haven.db")));
    }
    private static IEnumerable<Exception> Causes(Exception actual)
    {
        yield return actual;
        if (actual is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var value in Causes(child)) yield return value;
        else if (actual.InnerException is { } child) foreach (var value in Causes(child)) yield return value;
    }
    private static DeveloperProjectSetupIntent NegativeIntent(AuthenticatedResourceActor actor)
    {
        var projectFolder = Guid.NewGuid(); var file = Guid.NewGuid();
        DeveloperProjectSetupStep Step(DeveloperProjectSetupStepKind kind, Guid? fileId = null) => new(Guid.NewGuid(), kind, fileId, projectFolder);
        return new(Guid.NewGuid(), actor, Guid.NewGuid(), new('a', 64), Guid.NewGuid(), Guid.NewGuid().ToString("D"),
            "negative-unissued-capture", new('b', 64), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), projectFolder, "negative-metadata",
            ImmutableArray<DeveloperProjectSetupFolder>.Empty,
            [new(file, projectFolder, Guid.NewGuid(), "source.txt", 0, new('c', 64), "negative-source")],
            [Step(DeveloperProjectSetupStepKind.CreateProjectFolder), Step(DeveloperProjectSetupStepKind.CreateProjectDirectory),
             Step(DeveloperProjectSetupStepKind.RegisterProjectFolder), Step(DeveloperProjectSetupStepKind.PublishImmutableSource, file),
             Step(DeveloperProjectSetupStepKind.PublishMaterializedFile, file), Step(DeveloperProjectSetupStepKind.PublishFileRevision, file),
             Step(DeveloperProjectSetupStepKind.RegisterMaterialization, file), Step(DeveloperProjectSetupStepKind.SaveDevWorkspace)]);
    }
    private sealed class NegativeCapture : IDeveloperProjectOriginalSourceCapture
    {
        public string OriginalCaptureReference => "negative-unissued-capture";
        public string OriginalCaptureDigest => new('b', 64);
        public ImmutableArray<string> OriginalFolderPaths => [];
        public ImmutableArray<DeveloperProjectCapturedSourceFile> OriginalFiles => [];
    }

    private sealed class Control : IAsyncDisposable
    {
        private readonly string _root;
        private readonly App _app = new();
        private readonly CountingPrincipal _principal = new();
        internal readonly ServiceCollection Services = new();
        internal Action? BeforeScopes;
        internal string Root => _root;
        internal readonly HashSet<Exception> ExpectedFailures = new(ReferenceEqualityComparer.Instance);
        internal readonly ServiceProvider Provider;
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly HomeDeveloperProjectReadActionPolicySource ReadPolicy = new();
        internal readonly HomeDeveloperProjectSetupActionPolicySource SetupPolicy = new();
        internal readonly HomeDeveloperProjectReadResourceResolver ReadResolver;
        private readonly Paths _paths;
        internal Control(string selector)
        {
            Assert.True(OperatingSystem.IsWindows() && Environment.Is64BitProcess,
                "This owning control requires actual Windows64.");
            _root = Path.Combine(Path.GetTempPath(), "astra-windows-developer51-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root); _paths = new(_root);
            ServiceProvider? provider = null;
            ReadResolver = new(() => provider!.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>());
            var destination = new FilesDeveloperOriginalSetupDestinationResolver(() => provider!.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>());
            var cold = NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn(selector);
            var requested = cold.OriginalStatus.Kind == NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified;
            var projectResolver = new HomeColdProjectReadResourceResolver(() => provider!.GetRequiredService<HomeColdProjectReadReconciliation>());
            var projectPolicy = new HomeColdProjectReadActionPolicySource();
            var executionResolver = new FilesDeveloperOriginalCurrentProjectExecutionResolver(() => provider!.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
            var executionPolicy = new HomeDeveloperWorkspaceExecutionActionPolicySource();
            NativeFilesWorkspaceService? files = null; NativeFilesWorkspaceAuthority? filesAuthority = null;
            Home = new(new FileHomeCoreStateStore(Path.Combine(_root, "home.json")), _principal, _paths,
                new("9to1.home.windows-developer51." + Guid.NewGuid().ToString("N")),
                originalResourceResolvers: requested ? [ReadResolver, destination, projectResolver, executionResolver] : [ReadResolver, destination],
                originalActionPolicies: requested ? [ReadPolicy, SetupPolicy, projectPolicy, executionPolicy] : [ReadPolicy, SetupPolicy],
                configureOriginalStores: identity =>
                {
                    files = new(identity.StateStore, identity.Profiles);
                    return new([files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files });
                },
                configureOriginalResolvers: ownership =>
                {
                    filesAuthority = new(files!, ownership.Identity.Profiles, ownership.Ownership);
                    var resolver = new FilesArtifactResourceResolver(filesAuthority);
                    return new([resolver], new Dictionary<Type, object>
                    { [typeof(NativeFilesWorkspaceAuthority)] = filesAuthority, [typeof(FilesArtifactResourceResolver)] = resolver });
                });
            Services.AddHavenInfrastructure(); Services.RemoveAll<IAppPaths>(); Services.AddSingleton<IAppPaths>(_paths);
            Services.AddHavenOwnedWindowsHomeDomain(Home, _paths, _principal);
            Services.AddSingleton(files!); Services.AddSingleton(filesAuthority!);
            Services.AddFilesNativeHost(); Services.AddHavenOriginalNativeDevelopment();
            Services.AddHavenOwnedNativeTaskColdRecovery(cold);
            Services.AddHavenOwnedDeveloperSourceReads(Home, p => p.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), ReadResolver, ReadPolicy);
            Services.AddHavenOwnedDeveloperSetups(Home, p => { BeforeScopes?.Invoke(); return p.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>(); },
                p => p.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), p => p.GetRequiredService<FilesDeveloperOriginalFolderSetupProducer>(), SetupPolicy);
            Services.AddFilesOriginalDeveloperSetups(destination, p =>
                ((WorkspaceToolService)p.GetRequiredService<IWorkspaceToolService>()).CreateOriginalDeveloperCaptureSource(
                    p.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>(), () => p.GetRequiredService<FilesDeveloperOriginalSourceSelection>(),
                    () => p.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>(), () => p.GetRequiredService<FileDeveloperWorkspaceStore>(),
                    () => p.GetRequiredService<FilesDeveloperOriginalFolderSetupProducer>()),
                p => p.GetRequiredService<HomeDeveloperProjectSetupJournal>(), p => p.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>(),
                p => p.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>(), p => p.GetRequiredService<FileDeveloperWorkspaceStore>());
            if (requested)
            {
                Services.AddHavenOwnedNativeColdProjectResources(Home);
                Services.AddHavenOwnedCurrentProjectExecution(Home, executionResolver, executionPolicy);
            }
            Services.AddSingleton<CapabilityPreflightService>(); Services.AddSingleton<TerminalCommandActivityHub>();
            Services.AddSingleton<WorkspaceToolRuntime>(); Services.AddSingleton<ComputerToolRuntime>();
            Services.AddSingleton<ChatSessionService>(p => new(p.GetRequiredService<IConversationRepository>(), p.GetRequiredService<IProviderModelClient>(),
                p.GetRequiredService<CapabilityPreflightService>(), p.GetRequiredService<IConversationSafetyService>(),
                p.GetRequiredService<WorkspaceToolRuntime>(), p.GetRequiredService<ComputerToolRuntime>(),
                taskCoordinator: p.GetRequiredService<TaskExecutionCoordinator>(), taskToolOwner: p.GetRequiredService<ITaskRunToolActionOwner>(),
                taskProviderContextCapture: p.GetRequiredService<ITaskRunProviderContextCapture>(),
                taskCloudPermissionRemediation: p.GetRequiredService<TaskRunCloudPermissionRemediationOwner>()));
            Services.AddSingleton<AgentTaskRuntimeService>();
            Provider = provider = Services.BuildServiceProvider();
            SetField("_actualWindowsHome", Home); SetField("_windowsColdConfiguration", cold);
        }
        internal void Capture() => Invoke("CaptureOriginalWindowsDeveloperBorrowers", Provider);
        internal Task CloseDeveloper() => (Task)Invoke("JoinOriginalWindowsDeveloperBorrowersAsync")!;
        internal object? Field(string name) => typeof(App).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_app);
        private void SetField(string name, object actual) => typeof(App).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_app, actual);
        private object? Invoke(string name, params object[] values)
        {
            try { return typeof(App).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_app, values); }
            catch (TargetInvocationException error) when (error.InnerException is { } actual)
            { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
        }
        internal void AssertZeroOperations()
        {
            Assert.Equal(0, _principal.Calls); Assert.Null(Home.OriginalStartTask);
            Assert.False(File.Exists(_paths.DatabasePath)); Assert.False(File.Exists(Path.Combine(_root, ".task-recovery-auth.v1")));
            Assert.False(File.Exists(Path.Combine(_root, "home.json")));
            Assert.False(Directory.Exists(Path.Combine(_root, "Dev")));
        }
        public async ValueTask DisposeAsync()
        {
            // Actual canonical business drains before its current-project/permission dependencies.
            var coordinator = Provider.GetService<TaskExecutionCoordinator>();
            if (coordinator is not null)
            {
                var canonical = Provider.GetRequiredService<TaskRunCanonicalProcessRetirementOwner>();
                canonical.RequestOriginalProcessRetirement(); await canonical.CloseAndSuspendOriginalProducersAsync();
            }
            async Task Settle(Task actual)
            {
                try { await actual; }
                catch (Exception error)
                {
                    var leaves = Causes(error).Where(value => value is not AggregateException && value.InnerException is null).ToArray();
                    if (leaves.Length == 0 || leaves.Any(value => !ExpectedFailures.Contains(value))) throw;
                }
            }
            await Settle(CloseDeveloper()); await Home.CloseAndDrainAsync(); await Settle(Provider.DisposeAsync().AsTask());
            Directory.Delete(_root, true);
        }
    }
    private sealed class CountingPrincipal : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _actual = new();
        internal int Calls;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        { Interlocked.Increment(ref Calls); return _actual.GetPrincipalAsync(token); }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
