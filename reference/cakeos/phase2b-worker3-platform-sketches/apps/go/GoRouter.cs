using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace CakeOS.Go;

/// <summary>
/// Registry-backed routing service consuming the canonical ProductRegistry and Router.
/// Provides navigation and deep-linking capabilities.
/// </summary>
public sealed class GoRouter(
    IProductRegistry registry,
    IProductRouter router,
    IPermissionService permissions,
    ILogger<GoRouter> logger)
{
    private readonly Dictionary<string, CachedRoute> _cache = new(StringComparer.Ordinal);
    private readonly object _cacheLock = new();

    /// <summary>
    /// Navigates to a product by ID, resolving its route and creating the root element.
    /// </summary>
    public async ValueTask<NavigationResult> NavigateAsync(string productId, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(services);

        logger.LogDebug("Navigating to product {ProductId}", productId);

        var request = new ProductRouteRequest(registry.Identity, productId, services);
        var resolution = await router.ResolveAsync(request, permissions, cancellationToken);

        return resolution.Kind switch
        {
            RouteResolutionKind.Resolved => HandleResolved(resolution, productId),
            RouteResolutionKind.UnknownProduct => new NavigationResult(false, productId, null, null, "Product not found."),
            RouteResolutionKind.IncompatibleRegistry => new NavigationResult(false, productId, null, null, "Registry identity mismatch."),
            RouteResolutionKind.CapabilityDenied => new NavigationResult(false, productId, null, null, resolution.Reason),
            RouteResolutionKind.RootCreationFailed => new NavigationResult(false, productId, null, null, resolution.Reason),
            _ => new NavigationResult(false, productId, null, null, $"Unknown resolution kind: {resolution.Kind}"),
        };
    }

    /// <summary>
    /// Navigates to a product by its registered route path.
    /// </summary>
    public async ValueTask<NavigationResult> NavigateByRouteAsync(string route, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(services);

        if (!route.StartsWith("/", StringComparison.Ordinal))
        {
            return new NavigationResult(false, null, null, route, "Route must be absolute.");
        }

        var registration = registry.GetAll().FirstOrDefault(p => string.Equals(p.Route, route, StringComparison.Ordinal));
        if (registration is null)
        {
            return new NavigationResult(false, null, null, route, $"No product registered for route '{route}'.");
        }

        return await NavigateAsync(registration.ProductId, services, cancellationToken);
    }

    /// <summary>
    /// Gets all registered routes for navigation UI.
    /// </summary>
    public IReadOnlyList<RouteEntry> GetRoutes()
    {
        return registry.GetAll()
            .Where(p => p.LaunchAvailability != LaunchAvailability.Disabled)
            .Select(p => new RouteEntry(p.ProductId, p.DisplayName, p.Route, p.Capabilities.HasWindow))
            .ToImmutableList();
    }

    /// <summary>
    /// Gets a route entry by product ID.
    /// </summary>
    public RouteEntry? GetRoute(string productId)
    {
        var registration = registry.GetAll().FirstOrDefault(p => string.Equals(p.ProductId, productId, StringComparison.Ordinal));
        if (registration is null)
            return null;

        return new RouteEntry(registration.ProductId, registration.DisplayName, registration.Route, registration.Capabilities.HasWindow);
    }

    /// <summary>
    /// Invalidates the route cache for a specific product or all products.
    /// </summary>
    public void InvalidateCache(string? productId = null)
    {
        lock (_cacheLock)
        {
            if (productId is null)
            {
                _cache.Clear();
            }
            else
            {
                _cache.Remove(productId);
            }
        }
    }

    private NavigationResult HandleResolved(RouteResolution resolution, string productId)
    {
        var cached = new CachedRoute(productId, resolution.Registration!.Route, resolution.Root!);
        
        lock (_cacheLock)
        {
            _cache[productId] = cached;
        }

        return new NavigationResult(true, productId, resolution.Root, resolution.Registration.Route, resolution.Reason);
    }

    /// <summary>
    /// Gets a cached route if available.
    /// </summary>
    public bool TryGetCachedRoute(string productId, out CachedRoute? cachedRoute)
    {
        lock (_cacheLock)
        {
            return _cache.TryGetValue(productId, out cachedRoute);
        }
    }
}

/// <summary>
/// Result of a navigation operation.
/// </summary>
public sealed record NavigationResult(
    bool Success,
    string? ProductId,
    IRootElement? Root,
    string? Route,
    string? Reason)
{
    public static NavigationResult CreateSuccess(string productId, IRootElement root, string route) => new(true, productId, root, route, "Navigated successfully.");
    public static NavigationResult CreateFailure(string? productId, string? route, string reason) => new(false, productId, null, route, reason);
}

/// <summary>
/// Entry representing a registered product route for navigation UI.
/// </summary>
public sealed record RouteEntry(
    string ProductId,
    string DisplayName,
    string Route,
    bool HasWindow);

/// <summary>
/// Cached route with resolved root element.
/// </summary>
public sealed record CachedRoute(
    string ProductId,
    string Route,
    IRootElement Root);

/// <summary>
/// Extension methods for dependency injection registration.
/// </summary>
public static class GoRouterExtensions
{
    public static IServiceCollection AddGoRouter(this IServiceCollection services)
    {
        return services.AddScoped<GoRouter>();
    }
}