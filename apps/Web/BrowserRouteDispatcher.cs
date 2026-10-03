using HavenOS.Home.Core;

namespace NineToOne.Web;

internal sealed record BrowserRouteDispatchResult(HomeFeatureNavigationResult Result,
    BrowserCuiSurface? Surface, bool IsUnavailableHome);

/// <summary>Selects domain dispatch or the explicitly unavailable Home presentation.</summary>
internal static class BrowserRouteDispatcher
{
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
