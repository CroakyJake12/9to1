using HavenOS.Home.Core;

namespace NineToOne.Web;

internal sealed record BrowserRouteDispatchResult(HomeFeatureNavigationResult Result,
    BrowserCuiSurface? Surface, bool IsUnavailableHome);
internal sealed record BrowserRoutePreparation(HomeFeatureNavigationResult Result,
    Func<BrowserCuiSurface?>? Present, bool IsUnavailableHome);

/// <summary>Selects domain dispatch or the explicitly unavailable Home presentation.</summary>
internal static class BrowserRouteDispatcher
{
    internal static async Task<BrowserRoutePreparation> PrepareAsync(BrowserSurfaceRegistry surfaces,
        HomeFeatureNavigationRequest request, Func<HomeFeatureNavigationRequest, bool> supportsUnavailableHome,
        Func<HomeFeatureNavigationRequest, BrowserCuiSurface?> unavailableHome, CancellationToken cancellationToken)
    {
        if (surfaces.AvailableRoutes.Contains(request.RouteId, StringComparer.Ordinal))
        {
            var owner = await surfaces.PreparePresentationAsync(request, cancellationToken);
            return new(owner.Result, owner.Present, false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (supportsUnavailableHome(request))
            return new(new(true, "HomeServiceUnavailable", "Account services are unavailable.", request),
                () => cancellationToken.IsCancellationRequested ? null : unavailableHome(request), true);
        var missing = await surfaces.PreparePresentationAsync(request, cancellationToken);
        return new(missing.Result, missing.Present, false);
    }

    internal static async Task<BrowserRouteDispatchResult> OpenAsync(BrowserSurfaceRegistry surfaces,
        HomeFeatureNavigationRequest request, Func<HomeFeatureNavigationRequest, BrowserCuiSurface?> unavailableHome,
        CancellationToken cancellationToken)
    {
        // A registered authenticated owner retains authority over every result, including denial.
        if (surfaces.AvailableRoutes.Contains(request.RouteId, StringComparer.Ordinal))
        {
            var (ownerResult, ownerSurface) = await surfaces.OpenAsync(request, cancellationToken);
            return new(ownerResult, ownerSurface, false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (unavailableHome(request) is { } home)
            return new(new(true, "HomeServiceUnavailable", "Account services are unavailable.", request), home, true);
        var (result, surface) = await surfaces.OpenAsync(request, cancellationToken);
        return new(result, surface, false);
    }
}
