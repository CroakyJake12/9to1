using CakeOS.Cui;
using HavenOS.Home.Core;

namespace NineToOne.Web;

/// <summary>Platform presentation binding for a successful, owner-provided domain route.</summary>
public sealed record BrowserCuiSurface(CuiDocument Document, ICuiBindingContext Bindings,
    ICuiActionDispatcher Actions, IDisposable? Lifetime = null);

public sealed class BrowserSurfaceRegistry
{
    private HomeFeatureNavigationHost _navigation = new();
    private readonly Dictionary<string, Func<HomeFeatureViewState, BrowserCuiSurface>> _renderers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> AvailableRoutes => _navigation.AvailableRoutes;

    /// <summary>Remove prior account/organisation adapters before a new private context is opened.</summary>
    public void Clear()
    {
        _navigation = new();
        _renderers.Clear();
    }

    /// <summary>The caller supplies the existing authenticated domain adapter, not an endpoint guessed by the shell.</summary>
    public HomeCoreOperationResult<bool> Register(IHomeFeatureRouteHandler handler,
        Func<HomeFeatureViewState, BrowserCuiSurface> render)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(render);
        var result = _navigation.Register(handler);
        if (result.Succeeded) _renderers.Add(handler.RouteId, render);
        return result;
    }

    internal async Task<(HomeFeatureNavigationResult Result, BrowserCuiSurface? Surface)> OpenAsync(
        HomeFeatureNavigationRequest request, CancellationToken cancellationToken)
    {
        var navigation = _navigation;
        if (!_renderers.TryGetValue(request.RouteId, out var render))
            return (new(false, "HomeServiceUnavailable", "This destination is unavailable.", request), null);
        var result = await navigation.NavigateAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Succeeded) return (result, null);
        if (result.ViewState is null || !string.Equals(result.ViewState.RouteId, request.RouteId, StringComparison.Ordinal))
            return (new(false, "HomeServiceIncompatible", "This destination did not provide a compatible view.", request), null);
        if (!ReferenceEquals(navigation, _navigation))
            return (new(false, "PermissionDenied", "The account context has changed.", request), null);
        return (result, render(result.ViewState));
    }
}
