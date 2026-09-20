using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace CakeOS.Launcher;

/// <summary>
/// Registry-backed application launcher consuming the canonical ProductRegistry and Router.
/// </summary>
public sealed class LauncherService(
    IProductRegistry registry,
    IProductRouter router,
    IPermissionService permissions,
    ILogger<LauncherService> logger)
{
    public async ValueTask<LaunchResult> LaunchAsync(string productId, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(services);

        logger.LogInformation("Launching product {ProductId}", productId);

        var request = new ProductRouteRequest(registry.Identity, productId, services);
        var resolution = await router.ResolveAsync(request, permissions, cancellationToken);

        return resolution.Kind switch
        {
            RouteResolutionKind.Resolved => new LaunchResult(true, resolution.Registration?.ProductId, resolution.Root, resolution.Reason),
            RouteResolutionKind.UnknownProduct => new LaunchResult(false, productId, null, $"Product '{productId}' not found in registry."),
            RouteResolutionKind.IncompatibleRegistry => new LaunchResult(false, productId, null, "Registry identity mismatch."),
            RouteResolutionKind.CapabilityDenied => new LaunchResult(false, productId, null, resolution.Reason),
            RouteResolutionKind.RootCreationFailed => new LaunchResult(false, productId, null, resolution.Reason),
            _ => new LaunchResult(false, productId, null, $"Unknown resolution kind: {resolution.Kind}"),
        };
    }

    public IReadOnlyList<ProductRegistration> GetLaunchableProducts()
    {
        return registry.GetAll()
            .Where(p => p.Capabilities.HasWindow && p.LaunchAvailability != LaunchAvailability.Disabled)
            .ToImmutableList();
    }
}

/// <summary>
/// Result of a product launch operation.
/// </summary>
public sealed record LaunchResult(
    bool Success,
    string? ProductId,
    IRootElement? Root,
    string? Reason)
{
    public static LaunchResult CreateSuccess(string productId, IRootElement root) => new(true, productId, root, "Launched successfully.");
    public static LaunchResult CreateFailure(string productId, string reason) => new(false, productId, null, reason);
}

/// <summary>
/// Extension methods for dependency injection registration.
/// </summary>
public static class LauncherServiceExtensions
{
    public static IServiceCollection AddLauncher(this IServiceCollection services)
    {
        return services.AddScoped<LauncherService>();
    }
}