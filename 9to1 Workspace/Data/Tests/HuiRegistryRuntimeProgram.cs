using CakeOS.Platform;
using Haven.UI.Components;
using HavenOS.Apps.Data.Hui;

var dataRoot = Path.Combine(Path.GetTempPath(), "haven-data-hui-registry", Guid.NewGuid().ToString("N"));

try
{
    var registry = new ProductRegistry();
    DataHuiProduct.Register(registry);

    if (!registry.TryGet(DataHuiProduct.ProductId, out var registration) || registration is null)
        throw new InvalidOperationException("Data product was not registered in the shared registry.");
    if (registration.Route != "/apps/data" || registration.Entrypoint != DataHuiProduct.Entrypoint)
        throw new InvalidOperationException("Data product metadata does not match its registered route.");

    var permissions = new PermissionService(new VersionedSettingsStore(new XdgPlatformStorageLayout(dataRoot)));
    await permissions.GrantAsync("product.haven.data", "launch", "execute", GrantSource.System);
    var resolved = await new RegistryBackedRouter(registry).ResolveAsync(
        new ProductRouteRequest(registry.Identity, DataHuiProduct.ProductId, EmptyServiceProvider.Instance),
        permissions);

    if (resolved.Kind != RouteResolutionKind.Resolved || resolved.Root is not DataHuiRootElement { NativeRoot: Page })
        throw new InvalidOperationException("Data product did not resolve to a mountable HUI Page.");

    Console.WriteLine("Data HUI shared registry and mountable root runtime checks passed.");
}
finally
{
    if (Directory.Exists(dataRoot))
        Directory.Delete(dataRoot, recursive: true);
}

file sealed class EmptyServiceProvider : IServiceProvider
{
    public static EmptyServiceProvider Instance { get; } = new();

    public object? GetService(Type serviceType) => null;
}
