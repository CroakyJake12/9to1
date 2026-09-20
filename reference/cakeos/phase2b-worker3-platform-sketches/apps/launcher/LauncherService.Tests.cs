using CakeOS.Launcher;
using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CakeOS.Launcher.Tests;

public sealed class LauncherServiceTests
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "cakeos-launcher-tests", Guid.NewGuid().ToString("N"), "haven");

    [Fact]
    public async Task LaunchRegisteredProduct_Succeeds()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var registry = new ProductRegistry();
        var permissions = new PermissionService(settings, () => DateTimeOffset.UtcNow);
        var router = new RegistryBackedRouter(registry);

        var registration = new ProductRegistration(
            ProductId: "test.app",
            DisplayName: "Test App",
            ProductType: ProductType.App,
            Route: "/test",
            RootFactory: _ => new TestRootElement(),
            Entrypoint: "TestEntryPoint",
            Capabilities: new ProductCapabilities(true, false, false, false, []),
            Dependencies: new ProductDependencies([], [], []),
            Persistence: new ProductPersistence(false, false, false, false),
            Permissions: new ProductPermissions([], []),
            OptionalProviders: [],
            LifecycleOperations: [AppLifecycleOperation.Create, AppLifecycleOperation.Activate],
            LaunchAvailability: LaunchAvailability.Always);
        registry.Register(registration);

        await permissions.GrantAsync("product.test.app", "launch", "execute", GrantSource.System);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<LauncherService>();
        var launcher = new LauncherService(registry, router, permissions, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var result = await launcher.LaunchAsync("test.app", services);

        Assert.True(result.Success);
        Assert.Equal("test.app", result.ProductId);
        Assert.NotNull(result.Root);
    }

    [Fact]
    public async Task LaunchUnknownProduct_ReturnsFailure()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var registry = new ProductRegistry();
        var permissions = new PermissionService(settings);
        var router = new RegistryBackedRouter(registry);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<LauncherService>();
        var launcher = new LauncherService(registry, router, permissions, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var result = await launcher.LaunchAsync("unknown.app", services);

        Assert.False(result.Success);
        Assert.Equal("unknown.app", result.ProductId);
        Assert.Contains("not found", result.Reason);
    }

    [Fact]
    public async Task LaunchWithoutPermission_ReturnsCapabilityDenied()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var registry = new ProductRegistry();
        var permissions = new PermissionService(settings);
        var router = new RegistryBackedRouter(registry);

        var registration = new ProductRegistration(
            ProductId: "test.app",
            DisplayName: "Test App",
            ProductType: ProductType.App,
            Route: "/test",
            RootFactory: _ => new TestRootElement(),
            Entrypoint: "TestEntryPoint",
            Capabilities: new ProductCapabilities(true, false, false, false, []),
            Dependencies: new ProductDependencies([], [], []),
            Persistence: new ProductPersistence(false, false, false, false),
            Permissions: new ProductPermissions([], []),
            OptionalProviders: [],
            LifecycleOperations: [AppLifecycleOperation.Create, AppLifecycleOperation.Activate],
            LaunchAvailability: LaunchAvailability.Always);
        registry.Register(registration);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<LauncherService>();
        var launcher = new LauncherService(registry, router, permissions, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var result = await launcher.LaunchAsync("test.app", services);

        Assert.False(result.Success);
        Assert.Contains("grant is required", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetLaunchableProducts_ReturnsOnlyWindowedProducts()
    {
        var registry = new ProductRegistry();

        var windowedApp = new ProductRegistration(
            ProductId: "windowed.app",
            DisplayName: "Windowed App",
            ProductType: ProductType.App,
            Route: "/windowed",
            RootFactory: _ => new TestRootElement(),
            Entrypoint: "WindowedEntryPoint",
            Capabilities: new ProductCapabilities(true, false, false, false, []),
            Dependencies: new ProductDependencies([], [], []),
            Persistence: new ProductPersistence(false, false, false, false),
            Permissions: new ProductPermissions([], []),
            OptionalProviders: [],
            LifecycleOperations: [AppLifecycleOperation.Create],
            LaunchAvailability: LaunchAvailability.Always);
        registry.Register(windowedApp);

        var backgroundService = new ProductRegistration(
            ProductId: "background.service",
            DisplayName: "Background Service",
            ProductType: ProductType.BackgroundComponent,
            Route: "/background",
            RootFactory: _ => new TestRootElement(),
            Entrypoint: "BackgroundEntryPoint",
            Capabilities: new ProductCapabilities(false, true, false, false, []),
            Dependencies: new ProductDependencies([], [], []),
            Persistence: new ProductPersistence(false, false, false, false),
            Permissions: new ProductPermissions([], []),
            OptionalProviders: [],
            LifecycleOperations: [AppLifecycleOperation.Create],
            LaunchAvailability: LaunchAvailability.OnDemand);
        registry.Register(backgroundService);

        var disabledApp = new ProductRegistration(
            ProductId: "disabled.app",
            DisplayName: "Disabled App",
            ProductType: ProductType.App,
            Route: "/disabled",
            RootFactory: _ => new TestRootElement(),
            Entrypoint: "DisabledEntryPoint",
            Capabilities: new ProductCapabilities(true, false, false, false, []),
            Dependencies: new ProductDependencies([], [], []),
            Persistence: new ProductPersistence(false, false, false, false),
            Permissions: new ProductPermissions([], []),
            OptionalProviders: [],
            LifecycleOperations: [AppLifecycleOperation.Create],
            LaunchAvailability: LaunchAvailability.Disabled);
        registry.Register(disabledApp);

        var settings = new VersionedSettingsStore(new XdgPlatformStorageLayout(_dataRoot));
        var permissions = new PermissionService(settings);
        var router = new RegistryBackedRouter(registry);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<LauncherService>();
        var launcher = new LauncherService(registry, router, permissions, logger);

        var launchable = launcher.GetLaunchableProducts();

        Assert.Single(launchable);
        Assert.Equal("windowed.app", launchable[0].ProductId);
    }

    private sealed class TestRootElement : IRootElement { }
}