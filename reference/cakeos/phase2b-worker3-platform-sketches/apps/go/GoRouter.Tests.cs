using CakeOS.Go;
using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CakeOS.Go.Tests;

public sealed class GoRouterTests
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "cakeos-go-tests", Guid.NewGuid().ToString("N"), "haven");

    [Fact]
    public async Task NavigateToRegisteredProduct_Succeeds()
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
        var logger = loggerFactory.CreateLogger<GoRouter>();
        var goRouter = new GoRouter(registry, router, permissions, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var result = await goRouter.NavigateAsync("test.app", services);

        Assert.True(result.Success);
        Assert.Equal("test.app", result.ProductId);
        Assert.NotNull(result.Root);
        Assert.Equal("/test", result.Route);
    }

    [Fact]
    public async Task NavigateByRoute_Succeeds()
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
        var logger = loggerFactory.CreateLogger<GoRouter>();
        var goRouter = new GoRouter(registry, router, permissions, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var result = await goRouter.NavigateByRouteAsync("/test", services);

        Assert.True(result.Success);
        Assert.Equal("test.app", result.ProductId);
        Assert.Equal("/test", result.Route);
    }

    [Fact]
    public async Task NavigateToUnknownProduct_ReturnsFailure()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var registry = new ProductRegistry();
        var permissions = new PermissionService(settings);
        var router = new RegistryBackedRouter(registry);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<GoRouter>();
        var goRouter = new GoRouter(registry, router, permissions, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var result = await goRouter.NavigateAsync("unknown.app", services);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Reason);
    }

    [Fact]
    public async Task NavigateByUnknownRoute_ReturnsFailure()
    {
        var layout = new XdgPlatformStorageLayout(_dataRoot);
        var settings = new VersionedSettingsStore(layout);
        var registry = new ProductRegistry();
        var permissions = new PermissionService(settings);
        var router = new RegistryBackedRouter(registry);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<GoRouter>();
        var goRouter = new GoRouter(registry, router, permissions, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var result = await goRouter.NavigateByRouteAsync("/unknown", services);

        Assert.False(result.Success);
        Assert.Contains("No product registered", result.Reason);
    }

    [Fact]
    public void GetRoutes_ReturnsOnlyEnabledProducts()
    {
        var registry = new ProductRegistry();

        var enabledApp = new ProductRegistration(
            ProductId: "enabled.app",
            DisplayName: "Enabled App",
            ProductType: ProductType.App,
            Route: "/enabled",
            RootFactory: _ => new TestRootElement(),
            Entrypoint: "EnabledEntryPoint",
            Capabilities: new ProductCapabilities(true, false, false, false, []),
            Dependencies: new ProductDependencies([], [], []),
            Persistence: new ProductPersistence(false, false, false, false),
            Permissions: new ProductPermissions([], []),
            OptionalProviders: [],
            LifecycleOperations: [AppLifecycleOperation.Create],
            LaunchAvailability: LaunchAvailability.Always);
        registry.Register(enabledApp);

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
        var logger = loggerFactory.CreateLogger<GoRouter>();
        var goRouter = new GoRouter(registry, router, permissions, logger);

        var routes = goRouter.GetRoutes();

        Assert.Single(routes);
        Assert.Equal("enabled.app", routes[0].ProductId);
        Assert.Equal("/enabled", routes[0].Route);
        Assert.True(routes[0].HasWindow);
    }

    [Fact]
    public void GetRoute_ReturnsRouteEntry()
    {
        var registry = new ProductRegistry();

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
            LifecycleOperations: [AppLifecycleOperation.Create],
            LaunchAvailability: LaunchAvailability.Always);
        registry.Register(registration);

        var settings = new VersionedSettingsStore(new XdgPlatformStorageLayout(_dataRoot));
        var permissions = new PermissionService(settings);
        var router = new RegistryBackedRouter(registry);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<GoRouter>();
        var goRouter = new GoRouter(registry, router, permissions, logger);

        var route = goRouter.GetRoute("test.app");

        Assert.NotNull(route);
        Assert.Equal("test.app", route!.ProductId);
        Assert.Equal("Test App", route.DisplayName);
        Assert.Equal("/test", route.Route);
        Assert.True(route.HasWindow);
    }

    [Fact]
    public void GetRoute_UnknownProduct_ReturnsNull()
    {
        var registry = new ProductRegistry();
        var settings = new VersionedSettingsStore(new XdgPlatformStorageLayout(_dataRoot));
        var permissions = new PermissionService(settings);
        var router = new RegistryBackedRouter(registry);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<GoRouter>();
        var goRouter = new GoRouter(registry, router, permissions, logger);

        var route = goRouter.GetRoute("unknown.app");

        Assert.Null(route);
    }

    [Fact]
    public async Task Cache_Invalidation_Works()
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
        var logger = loggerFactory.CreateLogger<GoRouter>();
        var goRouter = new GoRouter(registry, router, permissions, logger);

        var services = new ServiceCollection().BuildServiceProvider();
        var result = await goRouter.NavigateAsync("test.app", services);

        Assert.True(result.Success);
        Assert.True(goRouter.TryGetCachedRoute("test.app", out var cached));
        Assert.NotNull(cached);
        Assert.Equal("test.app", cached!.ProductId);

        goRouter.InvalidateCache("test.app");
        Assert.False(goRouter.TryGetCachedRoute("test.app", out _));

        goRouter.InvalidateCache();
        Assert.False(goRouter.TryGetCachedRoute("test.app", out _));
    }

    private sealed class TestRootElement : IRootElement { }
}