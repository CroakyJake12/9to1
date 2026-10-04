using CakeOS.Settings;
using CakeOS.Platform;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CakeOS.Settings.Tests;

public sealed class SettingsServiceTests
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "cakeos-settings-tests", Guid.NewGuid().ToString("N"), "haven");

    [Fact]
    public async Task GetAndSetPlatformSetting_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();
        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        await settingsService.SetPlatformSettingAsync("test.setting", new TestConfig("value1"));
        var result = await settingsService.GetPlatformSettingAsync<TestConfig>("test.setting");

        Assert.NotNull(result);
        Assert.Equal("value1", result.Value);
    }

    [Fact]
    public async Task GetAndSetUserSetting_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();
        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        await settingsService.SetUserSettingAsync("preference.theme", new ThemeConfig("dark"));
        var result = await settingsService.GetUserSettingAsync<ThemeConfig>("preference.theme");

        Assert.NotNull(result);
        Assert.Equal("dark", result.Theme);
    }

    [Fact]
    public async Task RemoveSetting_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();
        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        await settingsService.SetPlatformSettingAsync("test.remove", "value");
        await settingsService.RemoveSettingAsync($"{SettingsService.PlatformSettingsPrefix}test.remove");
        var result = await settingsService.GetPlatformSettingAsync<string>("test.remove");

        Assert.Null(result);
    }

    [Fact]
    public async Task PermissionPolicy_GetAndSet_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();
        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        var initialPolicy = await settingsService.GetPermissionPolicyAsync();
        Assert.Equal(PermissionPolicy.AlwaysAsk, initialPolicy);

        await settingsService.SetPermissionPolicyAsync(PermissionPolicy.AskForConsequentialRisk);
        var updatedPolicy = await settingsService.GetPermissionPolicyAsync();
        Assert.Equal(PermissionPolicy.AskForConsequentialRisk, updatedPolicy);
    }

    [Fact]
    public async Task PermissionGrants_GetGrantRevoke_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();
        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        var grants = await settingsService.GetPermissionGrantsAsync();
        Assert.Empty(grants);

        await settingsService.GrantPermissionAsync("test.subject", "test.resource", "test.action", GrantSource.User);
        grants = await settingsService.GetPermissionGrantsAsync();
        Assert.Single(grants);
        Assert.Equal("test.subject", grants[0].SubjectId);
        Assert.Equal("test.resource", grants[0].Resource);
        Assert.Equal("test.action", grants[0].Action);

        await settingsService.RevokePermissionAsync("test.subject", "test.resource", "test.action");
        grants = await settingsService.GetPermissionGrantsAsync();
        Assert.Empty(grants);
    }

    [Fact]
    public async Task ProviderRegistry_RegisterUnregister_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();
        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        var descriptor = await settingsService.RegisterProviderAsync(GetProviderPath("llamacpp"));
        Assert.Equal("llamacpp", descriptor.ProviderId);

        var providers = settingsService.GetProviders();
        Assert.Contains(providers, p => p.ProviderId == "llamacpp");

        var unregistered = await settingsService.UnregisterProviderAsync("llamacpp");
        Assert.True(unregistered);

        providers = settingsService.GetProviders();
        Assert.DoesNotContain(providers, p => p.ProviderId == "llamacpp");
    }

    [Fact]
    public async Task ModelGovernance_FallbackOrder_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();

        // Register providers
        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));
        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("ollama"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        await settingsService.SetModelFallbackOrderAsync(["ollama", "llamacpp"]);
        var order = await settingsService.GetModelFallbackOrderAsync();
        
        Assert.Equal("ollama", order[0]);
        Assert.Equal("llamacpp", order[1]);
    }

    [Fact]
    public async Task ModelGovernance_ResolveModelKey_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        await settingsService.SetModelFallbackOrderAsync(["llamacpp"]);
        var resolved = await settingsService.ResolveModelKeyAsync("test-model");

        Assert.NotEmpty(resolved);
        Assert.Contains(resolved, k => k.Contains("test-model"));
    }

    [Fact]
    public async Task ModelGovernance_EvaluateModelUse_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        await settingsService.SetModelFallbackOrderAsync(["llamacpp"]);

        var request = new ModelUseRequest("llamacpp", "llamacpp:test-model", "chat", PermissionRisk.Consequential);
        
        // Before grant - should Ask
        var decision = await settingsService.EvaluateModelUseAsync(request);
        Assert.Equal(PermissionDecisionKind.Ask, decision.Kind);

        // Grant permission
        await settingsService.GrantPermissionAsync("provider.llamacpp", "model.llamacpp:test-model", "chat");

        // After grant - should Allow
        decision = await settingsService.EvaluateModelUseAsync(request);
        Assert.Equal(PermissionDecisionKind.Allowed, decision.Kind);
    }

    [Fact]
    public async Task BackupAndRestore_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settingsStore = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settingsStore);
        var providerRegistry = new ProviderRegistry();
        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settingsStore);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SettingsService>();
        var settingsService = new SettingsService(settingsStore, permissions, providerRegistry, modelGovernance, logger);

        await settingsService.SetPlatformSettingAsync("backup.test", "value1");
        await settingsService.SetUserSettingAsync("backup.pref", "value2");

        var backupResult = await settingsService.CreateBackupAsync("test-backup");
        Assert.True(backupResult.Success);
        Assert.NotNull(backupResult.BackupPath);

        // Clear settings
        await settingsStore.RemoveAsync($"{SettingsService.PlatformSettingsPrefix}backup.test");
        await settingsStore.RemoveAsync($"{SettingsService.UserSettingsPrefix}backup.pref");

        var restoreResult = await settingsService.RestoreBackupAsync(backupResult.BackupPath!);
        Assert.True(restoreResult.Success);

        var platformSetting = await settingsService.GetPlatformSettingAsync<string>("backup.test");
        var userSetting = await settingsService.GetUserSettingAsync<string>("backup.pref");

        Assert.Equal("value1", platformSetting);
        Assert.Equal("value2", userSetting);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "havenos.lock")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate the CakeOS repository root.");
    }

    private static string GetProviderPath(string providerName) =>
        Path.Combine(RepositoryRoot(), "HUI", $"{providerName}-provider.json");

    private sealed record TestConfig(string Value);
    private sealed record ThemeConfig(string Theme);
}