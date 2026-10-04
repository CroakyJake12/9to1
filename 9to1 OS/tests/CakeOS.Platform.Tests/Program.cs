using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var suite = new PlatformFoundationSuite();
await suite.RunAsync();

internal sealed class PlatformFoundationSuite
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "cakeos-platform-tests", Guid.NewGuid().ToString("N"), "haven");

    public async Task RunAsync()
    {
        try
        {
            await VerifyRegistryAndRoutingAsync();
            VerifyHuiHostAbi();
            await VerifyPersistenceAndPermissionsAsync();
            await VerifyProviderRegistryAndGovernanceAsync();
            await VerifyNotificationEventsAsync();
            await VerifyLocalCakeUpdateFlowAsync();
            Console.WriteLine("CakeOS shared platform foundation tests passed.");
        }
        finally
        {
            if (Directory.Exists(_dataRoot))
                Directory.Delete(Path.GetDirectoryName(_dataRoot)!, recursive: true);
        }
    }

    private async Task VerifyRegistryAndRoutingAsync()
    {
        var registry = new ProductRegistry();
        Equal("cakeos.shared-app-registry", registry.Identity.RegistryId, "registry id");
        Equal(1, registry.Identity.RegistrySchemaVersion, "registry schema version");

        var registration = new ProductRegistration(
            ProductId: "test.application",
            DisplayName: "Test Application",
            ProductType: ProductType.App,
            Route: "/test",
            RootFactory: _ => new TestRootElement(),
            Entrypoint: "TestEntryPoint",
            Capabilities: new ProductCapabilities(true, false, false, false, []),
            Dependencies: new ProductDependencies([], [], []),
            Persistence: new ProductPersistence(false, false, false, false),
            Permissions: new ProductPermissions([], []),
            OptionalProviders: [],
            LifecycleOperations: [AppLifecycleOperation.Create, AppLifecycleOperation.Activate, AppLifecycleOperation.RequestClose],
            LaunchAvailability: LaunchAvailability.Always);
        registry.Register(registration);
        
        var router = new RegistryBackedRouter(registry);
        var services = new ServiceCollection().BuildServiceProvider();
        var permissions = new PermissionService(new VersionedSettingsStore(new XdgPlatformStorageLayout(_dataRoot)), () => DateTimeOffset.UnixEpoch);
        
        // Need to grant permission first
        await permissions.GrantAsync("product.test.application", "launch", "execute", GrantSource.System);
        
        var resolved = await router.ResolveAsync(new ProductRouteRequest(registry.Identity, registration.ProductId, services), permissions);
        Equal(RouteResolutionKind.Resolved, resolved.Kind, "registered route resolved");
        NotNull(resolved.Root, "root created");
        Equal(registration.ProductId, resolved.Registration?.ProductId, "registration returned");
        
        Equal(RouteResolutionKind.UnknownProduct, (await router.ResolveAsync(new ProductRouteRequest(registry.Identity, "missing.application", services), permissions)).Kind, "unknown route");
        Equal(
            RouteResolutionKind.IncompatibleRegistry,
            (await router.ResolveAsync(new ProductRouteRequest(new RegistryIdentity("other.registry", 1), registration.ProductId, services), permissions)).Kind,
            "mismatched registry");
        
        // Test capability denial
        var noGrantPermissions = new PermissionService(new VersionedSettingsStore(new XdgPlatformStorageLayout(Path.Combine(Path.GetTempPath(), "cakeos-platform-tests", Guid.NewGuid().ToString("N"), "haven"))), () => DateTimeOffset.UnixEpoch);
        var denied = await router.ResolveAsync(new ProductRouteRequest(registry.Identity, registration.ProductId, services), noGrantPermissions);
        Equal(RouteResolutionKind.CapabilityDenied, denied.Kind, "capability denied without grant");
    }

    private static void VerifyHuiHostAbi()
    {
        HuiLinuxHostAbi.RequireCompatible(HuiLinuxHostAbi.Current);
        var root = new TestHuiRootElement();
        NotNull(root.NativeRoot, "HUI root exposes native element");
        Throws<NotSupportedException>(
            () => HuiLinuxHostAbi.RequireCompatible(new HuiRootProviderAbi(HuiLinuxHostAbi.ContractId, HuiLinuxHostAbi.CurrentVersion + 1)),
            "future HUI ABI must be rejected");
    }

    private async Task VerifyPersistenceAndPermissionsAsync()
    {
        var xdgDataHome = Path.Combine(_dataRoot, "xdg-data-home");
        var xdgLayout = XdgPlatformStorageLayout.FromEnvironment(name => name == "XDG_DATA_HOME" ? xdgDataHome : null);
        Equal(Path.Combine(xdgDataHome, "haven"), xdgLayout.DataRoot, "XDG data root");
        Equal(Path.Combine(xdgLayout.DataRoot, "platform", "settings.v1.json"), xdgLayout.SettingsFile, "shared settings path");
        Equal(Path.Combine(xdgLayout.DataRoot, "config"), xdgLayout.ConfigDirectory, "config directory");
        Equal(Path.Combine(xdgLayout.DataRoot, "cache"), xdgLayout.CacheDirectory, "cache directory");
        Equal(Path.Combine(xdgLayout.DataRoot, "state"), xdgLayout.StateDirectory, "state directory");
        Equal(Path.Combine(xdgLayout.DataRoot, "models"), xdgLayout.ModelsDirectory, "models directory");

        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        await settings.SetAsync("test.setting", new TestSetting("persisted"));
        var reopened = new VersionedSettingsStore(layout);
        Equal("persisted", (await reopened.GetAsync<TestSetting>("test.setting"))?.Value, "versioned settings persistence");

        // Test migrations
        var migrationResult = await settings.MigrateAsync(1, async (entries, fromVersion) =>
        {
            entries["migrated"] = JsonSerializer.SerializeToElement(true);
            return entries;
        });
        True(migrationResult.Success, "migration success");
        
        // Test backup/restore
        var backupResult = await settings.CreateBackupAsync("test-backup");
        True(backupResult.Success, "backup success");
        NotNull(backupResult.BackupPath, "backup path");
        
        var restoreResult = await settings.RestoreBackupAsync(backupResult.BackupPath!);
        True(restoreResult.Success, "restore success");

        var permissions = new PermissionService(reopened, () => DateTimeOffset.UnixEpoch);
        var events = new List<PermissionAuditEvent>();
        permissions.Audited += (_, e) => events.Add(e);
        
        var request = new PermissionRequest("test.application", "files", "read", PermissionRisk.Consequential);
        Equal(PermissionDecisionKind.Ask, (await permissions.EvaluateAsync(request)).Kind, "ungranted consequential permission");
        
        await permissions.GrantAsync(request.SubjectId, request.Resource, request.Action, GrantSource.User);
        var allowedDecision = await permissions.EvaluateAsync(request);
        Equal(PermissionDecisionKind.Allowed, allowedDecision.Kind, "scoped grant");
        
        // Verify audit event was emitted
        True(events.Count >= 2, "audit events recorded");
        Equal(PermissionDecisionKind.Allowed, events[^1].Decision, "audit decision");
        
        await permissions.RevokeAsync(request.SubjectId, request.Resource, request.Action);
        Equal(PermissionDecisionKind.Ask, (await permissions.EvaluateAsync(request)).Kind, "revoked grant");
        
        // Test grant with expiration
        var future = DateTimeOffset.UtcNow.AddHours(1);
        await permissions.GrantAsync(request.SubjectId, request.Resource, request.Action, GrantSource.User, future);
        Equal(PermissionDecisionKind.Allowed, (await permissions.EvaluateAsync(request)).Kind, "grant with future expiration");
    }

    private async Task VerifyProviderRegistryAndGovernanceAsync()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var providers = new ProviderRegistry();
        
        var llamacppDescriptor = await providers.RegisterDescriptorAsync(Path.Combine(RepositoryRoot(), "HUI", "llamacpp-provider.json"));
        Equal("llamacpp", llamacppDescriptor.ProviderId, "descriptor provider id");
        Equal("http-over-unix", llamacppDescriptor.TransportType, "descriptor transport");
        Equal("ollama", llamacppDescriptor.LegacyUnqualifiedProvider, "descriptor legacy provider");
        Equal(ProviderKind.Local, llamacppDescriptor.Kind, "descriptor kind");
        True(llamacppDescriptor.TransportDetails.Count > 0, "transport details populated");
        True(llamacppDescriptor.Discovery.Count > 0, "discovery populated");
        
        var ollamaDescriptor = await providers.RegisterDescriptorAsync(Path.Combine(RepositoryRoot(), "HUI", "ollama-provider.json"));
        Equal("ollama", ollamaDescriptor.ProviderId, "ollama provider id");
        Equal("http", ollamaDescriptor.TransportType, "ollama transport");
        Equal(ProviderKind.Local, ollamaDescriptor.Kind, "ollama kind");
        
        var permissions = new PermissionService(settings);
        var governance = new ModelGovernance(providers, permissions, settings);
        await governance.SetFallbackOrderAsync(["llamacpp", "ollama"]);
        Equal("llamacpp", (await governance.GetFallbackOrderAsync())[0], "model fallback order first");
        Equal("ollama", (await governance.GetFallbackOrderAsync())[1], "model fallback order second");
        
        var request = new ModelUseRequest("llamacpp", "llamacpp:test-model", "chat", PermissionRisk.Consequential);
        Equal(PermissionDecisionKind.Ask, (await governance.EvaluateModelUseAsync(request)).Kind, "model use before grant");
        await permissions.GrantAsync("provider.llamacpp", "model.llamacpp:test-model", "chat");
        Equal(PermissionDecisionKind.Allowed, (await governance.EvaluateModelUseAsync(request)).Kind, "model use after central grant");
        
        // Test model key resolution (unqualified -> qualified)
        var resolvedKeys = await governance.ResolveModelKeyAsync("test-model");
        True(resolvedKeys.Count > 0, "unqualified model key resolved");
        True(resolvedKeys.Any(k => k.Contains("test-model")), "resolved key contains model name");
        
        // Test unregister
        True(await providers.UnregisterAsync("ollama"), "unregister ollama");
        False(providers.TryGet("ollama", out _), "ollama unregistered");
    }

    private async Task VerifyNotificationEventsAsync()
    {
        var settings = new VersionedSettingsStore(new XdgPlatformStorageLayout(_dataRoot));
        var notifications = new NotificationService(settings, () => DateTimeOffset.UnixEpoch);
        var events = new List<NotificationEvent>();
        notifications.Changed += (_, notificationEvent) => events.Add(notificationEvent);

        var actions = new[] { new NotificationAction("dismiss", "Dismiss"), new NotificationAction("open", "Open", true) };
        var notification = await notifications.PublishAsync(new NotificationDraft("test.application", NotificationSeverity.Information, "Title", "Body", actions, NotificationPersistencePolicy.Persistent));
        Equal(1, (await notifications.GetActiveAsync()).Count, "active notification count");
        Equal(NotificationEventKind.Published, events[0].Kind, "published notification event");
        Equal(2, events[0].Notification.Actions.Count, "notification actions preserved");
        
        True(await notifications.MarkReadAsync(notification.Id), "notification mark read");
        Equal(NotificationEventKind.Read, events[1].Kind, "read notification event");
        NotNullStruct(events[1].Notification.ReadAtUtc, "read timestamp set");
        
        True(await notifications.DismissAsync(notification.Id), "notification dismissal");
        Equal(0, (await notifications.GetActiveAsync()).Count, "dismissed notification count");
        Equal(NotificationEventKind.Dismissed, events[2].Kind, "dismissed notification event");
        NotNullStruct(events[2].Notification.DismissedAtUtc, "dismissed timestamp set");
        
        // Test history includes dismissed
        var history = await notifications.GetHistoryAsync(10);
        Equal(1, history.Count, "history includes dismissed notification");
        
        // Test clear dismissed
        await notifications.PublishAsync(new NotificationDraft("test.application", NotificationSeverity.Information, "Title2", "Body2"));
        await notifications.DismissAsync((await notifications.GetHistoryAsync(1))[0].Id);
        var cleared = await notifications.ClearDismissedAsync();
        Equal(1, cleared, "cleared dismissed count");
    }

    private async Task VerifyLocalCakeUpdateFlowAsync()
    {
        var bundleDirectory = Path.Combine(_dataRoot, "bundles");
        var trustedBundlePath = Path.Combine(bundleDirectory, "trusted-local-test.cakeupdate");
        await CreateTestBundleAsync(trustedBundlePath);

        var validator = new CakeUpdateBundleValidator(new StaticCakeOsSystemInfoProvider(new CakeOsSystemInfo("1.0.0", "amd64")));
        var history = new CakeUpdateHistoryStore(Path.Combine(_dataRoot, "system-updates"));
        var successfulInstaller = new TestUpdateInstaller(true);
        var updates = new SettingsUpdatesModel(validator, successfulInstaller, history, () => DateTimeOffset.UnixEpoch);

        Equal("Install Update From File", updates.InstallUpdateFromFile.Title, "updates file picker title");
        Equal(CakeUpdateBundleValidator.BundleExtension, updates.InstallUpdateFromFile.AllowedExtensions.Single(), "updates file picker extension");
        var selection = await updates.SelectUpdateFileAsync(trustedBundlePath);
        True(selection.RequiresConfirmation, "validated bundle requires explicit confirmation");
        Equal(0, successfulInstaller.Requests.Count, "selection does not invoke privileged installer");
        NotNull(selection.Confirmation, "validated bundle confirmation");
        Equal("trusted-local-test", selection.Confirmation!.BundleId, "validated bundle id");
        Equal(2, selection.Confirmation.PackageCount, "validated package count");

        var installed = await updates.ConfirmInstallAsync(selection.Confirmation.Id);
        Equal(UpdateInstallStatus.Succeeded, installed.Status, "confirmed update status");
        True(installed.HistoryRecorded, "successful update history recorded");
        Equal(1, successfulInstaller.Requests.Count, "confirmation invokes privileged installer once");
        Equal(Path.GetFullPath(trustedBundlePath), successfulInstaller.Requests[0].BundlePath, "privileged installer receives selected bundle path");
        Equal(1, (await updates.GetHistoryAsync()).Count, "successful update history count");
        Equal(UpdateInstallStatus.Succeeded, (await updates.GetHistoryAsync())[0].Status, "successful update history status");
        Equal(UpdateInstallStatus.Failed, (await updates.ConfirmInstallAsync(selection.Confirmation.Id)).Status, "confirmation cannot be reused");

        var failureHistory = new CakeUpdateHistoryStore(Path.Combine(_dataRoot, "failed-system-updates"));
        var failedUpdates = new SettingsUpdatesModel(validator, new TestUpdateInstaller(false), failureHistory, () => DateTimeOffset.UnixEpoch);
        var failedSelection = await failedUpdates.SelectUpdateFileAsync(trustedBundlePath);
        NotNull(failedSelection.Confirmation, "failure-path confirmation");
        var failedInstall = await failedUpdates.ConfirmInstallAsync(failedSelection.Confirmation!.Id);
        Equal(UpdateInstallStatus.Failed, failedInstall.Status, "failed installer status");
        True(failedInstall.HistoryRecorded, "failed update history recorded");
        Equal(UpdateInstallStatus.Failed, (await failureHistory.GetAsync()).Single().Status, "failed update history status");

        var tamperedBundlePath = Path.Combine(bundleDirectory, "tampered-local-test.cakeupdate");
        await CreateTestBundleAsync(tamperedBundlePath, mismatchedPackageHash: true);
        False((await updates.SelectUpdateFileAsync(tamperedBundlePath)).RequiresConfirmation, "tampered package hash is rejected");

        var scriptBundlePath = Path.Combine(bundleDirectory, "script-local-test.cakeupdate");
        await CreateTestBundleAsync(scriptBundlePath, addScript: true);
        False((await updates.SelectUpdateFileAsync(scriptBundlePath)).RequiresConfirmation, "embedded scripts are rejected");

        var missingDependencyBundlePath = Path.Combine(bundleDirectory, "missing-dependency-local-test.cakeupdate");
        await CreateTestBundleAsync(missingDependencyBundlePath, missingDependency: true);
        False((await updates.SelectUpdateFileAsync(missingDependencyBundlePath)).RequiresConfirmation, "missing package dependency is rejected");

        var traversalBundlePath = Path.Combine(bundleDirectory, "traversal-local-test.cakeupdate");
        await CreateTestBundleAsync(traversalBundlePath, addTraversalEntry: true);
        False((await updates.SelectUpdateFileAsync(traversalBundlePath)).RequiresConfirmation, "archive traversal entry is rejected");

        Console.WriteLine($"Validated harmless .cakeupdate package integrity: {trustedBundlePath}");
    }

    private static async Task CreateTestBundleAsync(string bundlePath, bool mismatchedPackageHash = false, bool addScript = false, bool missingDependency = false, bool addTraversalEntry = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
        var runtimePackage = Encoding.UTF8.GetBytes("harmless CakeOS runtime test package\n");
        var shellPackage = Encoding.UTF8.GetBytes("harmless CakeOS shell test package\n");
        var declaredRuntimeHash = Sha256(runtimePackage);
        var runtimePayload = mismatchedPackageHash ? Encoding.UTF8.GetBytes("harmless CakeOS runtime test package!\n") : runtimePackage;
        var packages = new[]
        {
            new TestBundlePackage("cakeos-fixture-runtime", "1.0.1", "all", "packages/cakeos-fixture-runtime_1.0.1_all.deb", declaredRuntimeHash, runtimePayload.LongLength, []),
            new TestBundlePackage("cakeos-fixture-shell", "1.0.1", "amd64", "packages/cakeos-fixture-shell_1.0.1_amd64.deb", Sha256(shellPackage), shellPackage.LongLength, [missingDependency ? "cakeos-fixture-missing" : "cakeos-fixture-runtime"])
        };
        var manifest = new TestBundleManifest(
            1,
            "trusted-local-test",
            "1.0.1",
            new TestBundleCompatibility("1.0.0", "1.0.0", ["amd64"]),
            packages);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        await using var stream = new FileStream(bundlePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        await WriteEntryAsync(archive, "manifest.json", manifestBytes);
        await WriteEntryAsync(archive, packages[0].Path, runtimePayload);
        await WriteEntryAsync(archive, packages[1].Path, shellPackage);
        if (addScript)
            await WriteEntryAsync(archive, "scripts/install.sh", Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"));
        if (addTraversalEntry)
            await WriteEntryAsync(archive, "packages/../outside.deb", Encoding.UTF8.GetBytes("not a package\n"));
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        await using var stream = entry.Open();
        await stream.WriteAsync(content);
    }

    private static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string RepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "havenos.lock")))
                    return directory.FullName;
                var osRoot = Path.Combine(directory.FullName, "9to1 OS");
                if (File.Exists(Path.Combine(osRoot, "havenos.lock")))
                    return osRoot;
            }
        }
        throw new InvalidOperationException("Could not locate the CakeOS repository root.");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
    }

    private static void NotNull<T>(T? value, string name) where T : class
    {
        if (value is null)
            throw new InvalidOperationException($"{name}: expected non-null.");
    }

    private static void NotNullStruct<T>(T? value, string name) where T : struct
    {
        if (!value.HasValue)
            throw new InvalidOperationException($"{name}: expected non-null.");
    }

    private static void True(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"{name}: expected true.");
    }

    private static void False(bool condition, string name)
    {
        if (condition)
            throw new InvalidOperationException($"{name}: expected false.");
    }

    private static void Throws<TException>(Action action, string name) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}.");
    }

    private sealed record TestSetting(string Value);
    private sealed record TestBundleManifest(int SchemaVersion, string BundleId, string Version, TestBundleCompatibility Compatibility, TestBundlePackage[] Packages);
    private sealed record TestBundleCompatibility(string MinimumInstalledVersion, string MaximumInstalledVersion, string[] Architectures);
    private sealed record TestBundlePackage(string Id, string Version, string Architecture, string Path, string Sha256, long SizeBytes, string[] DependsOn);
    private sealed class TestUpdateInstaller(bool succeeds) : IPrivilegedCakeUpdateInstaller
    {
        public List<CakeUpdateInstallRequest> Requests { get; } = [];

        public Task<PrivilegedInstallerResult> InstallAsync(CakeUpdateInstallRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(succeeds
                ? new PrivilegedInstallerResult(true, "Test privileged boundary accepted the validated bundle.")
                : new PrivilegedInstallerResult(false, "Test privileged boundary rejected the validated bundle."));
        }
    }
    private sealed class TestRootElement : IRootElement { }
    private sealed class TestHuiRootElement : IHuiRootElement
    {
        public object NativeRoot { get; } = new();
    }
}
