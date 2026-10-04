using CakeOS.ModelPicker;
using CakeOS.Platform;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CakeOS.ModelPicker.Tests;

public sealed class ModelPickerServiceTests
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "cakeos-modelpicker-tests", Guid.NewGuid().ToString("N"), "haven");

    [Fact]
    public async Task GetAvailableProviders_ReturnsRegisteredProviders()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        var providers = modelPicker.GetAvailableProviders();

        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.ProviderId == "llamacpp");
    }

    [Fact]
    public async Task ResolveModelKey_ReturnsQualifiedKeys()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);
        await modelGovernance.SetFallbackOrderAsync(["llamacpp"]);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        var resolved = await modelPicker.ResolveModelKeyAsync("test-model");

        Assert.NotEmpty(resolved);
        Assert.All(resolved, r => Assert.Contains("test-model", r.QualifiedKey));
        // llamacpp provider declares legacyUnqualifiedProvider=ollama, so both formats are valid resolutions
        Assert.Contains(resolved, r => r.QualifiedKey == "llamacpp:test-model" || r.QualifiedKey == "ollama:test-model");
    }

    [Fact]
    public async Task CheckModelUse_BeforeGrant_ReturnsDenied()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        var result = await modelPicker.CheckModelUseAsync("llamacpp", "llamacpp:test-model", "chat");

        Assert.False(result.Allowed);
        Assert.Contains("grant is required", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckModelUse_AfterGrant_ReturnsAllowed()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        // Grant permission
        await modelPicker.GrantModelUseAsync("llamacpp", "llamacpp:test-model", "chat");

        var result = await modelPicker.CheckModelUseAsync("llamacpp", "llamacpp:test-model", "chat");

        Assert.True(result.Allowed);
        Assert.Equal("Allowed by capability scope.", result.Reason);
    }

    [Fact]
    public async Task RevokeModelUse_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        await modelPicker.GrantModelUseAsync("llamacpp", "llamacpp:test-model", "chat");
        
        var allowed = await modelPicker.CheckModelUseAsync("llamacpp", "llamacpp:test-model", "chat");
        Assert.True(allowed.Allowed);

        await modelPicker.RevokeModelUseAsync("llamacpp", "llamacpp:test-model", "chat");
        
        var denied = await modelPicker.CheckModelUseAsync("llamacpp", "llamacpp:test-model", "chat");
        Assert.False(denied.Allowed);
    }

    [Fact]
    public async Task GetAvailableModelKeys_ReturnsFormats()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));
        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("ollama"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        var modelKeys = modelPicker.GetAvailableModelKeys();

        Assert.NotEmpty(modelKeys);
        Assert.Contains(modelKeys, k => k.ProviderId == "llamacpp" && k.FormatKey == "qualified");
        Assert.Contains(modelKeys, k => k.ProviderId == "ollama" && k.FormatKey == "qualified");
    }

    [Fact]
    public async Task GetProviderModelFormat_ReturnsFormat()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        await providerRegistry.RegisterDescriptorAsync(GetProviderPath("llamacpp"));

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        var format = modelPicker.GetProviderModelFormat("llamacpp", "qualified");

        Assert.NotNull(format);
        Assert.Equal("llamacpp", format!.ProviderId);
        Assert.Equal("qualified", format.FormatKey);
        Assert.Equal("llamacpp:<model-id>", format.Format);
    }

    [Fact]
    public void GetProviderModelFormat_UnknownProvider_ReturnsNull()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        var format = modelPicker.GetProviderModelFormat("unknown");

        Assert.Null(format);
    }

    [Fact]
    public async Task FallbackOrder_GetAndSet_Works()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var permissions = new PermissionService(settings);
        var providerRegistry = new ProviderRegistry();

        var fullPath1 = GetProviderPath("llamacpp");
        var fullPath2 = GetProviderPath("ollama");
        
        await providerRegistry.RegisterDescriptorAsync(fullPath1);
        await providerRegistry.RegisterDescriptorAsync(fullPath2);

        var modelGovernance = new ModelGovernance(providerRegistry, permissions, settings);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ModelPickerService>();
        var modelPicker = new ModelPickerService(providerRegistry, modelGovernance, permissions, logger);

        await modelPicker.SetFallbackOrderAsync(["ollama", "llamacpp"]);
        var order = await modelPicker.GetFallbackOrderAsync();

        Assert.Equal("ollama", order[0]);
        Assert.Equal("llamacpp", order[1]);
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
}