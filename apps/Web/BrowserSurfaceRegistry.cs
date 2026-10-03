using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;

namespace NineToOne.Web;

/// <summary>Platform presentation binding for a successful, owner-provided domain route.</summary>
public sealed record BrowserCuiSurface(CuiDocument Document, ICuiBindingContext Bindings,
    ICuiActionDispatcher Actions, IDisposable? Lifetime = null, CuiControlRegistry? ControlRegistry = null);

public enum BrowserSurfaceScope { PrivateContext, DeviceLocal }

public sealed class BrowserSurfaceRegistry
{
    private HomeFeatureNavigationHost _navigation = new();
    private readonly Dictionary<string, Func<HomeFeatureViewState, BrowserCuiSurface>> _renderers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IHomeFeatureRouteHandler Handler, BrowserSurfaceScope Scope)> _handlers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> AvailableRoutes => _navigation.AvailableRoutes;

    /// <summary>Remove prior account/organisation adapters before a new private context is opened.</summary>
    public void Clear()
    {
        RemoveHandlers(privateOnly: false);
    }

    /// <summary>Device-local projects retain their session; no account authority is transferred to them.</summary>
    public void ClearPrivateContext() => RemoveHandlers(privateOnly: true);

    private void RemoveHandlers(bool privateOnly)
    {
        var removed = _handlers.Where(pair => !privateOnly || pair.Value.Scope == BrowserSurfaceScope.PrivateContext).ToArray();
        foreach (var pair in removed) { _handlers.Remove(pair.Key); _renderers.Remove(pair.Key); }
        // Invalidate every in-flight request before calling an owner's teardown.
        _navigation = new();
        foreach (var pair in _handlers.Values) _navigation.Register(pair.Handler);
        List<Exception>? errors = null;
        foreach (var pair in removed)
        {
            if (pair.Value.Handler is not IDisposable lifetime) continue;
            try { lifetime.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is not null) throw new AggregateException("Browser route teardown failed after its context was removed.", errors);
    }

    /// <summary>The caller supplies the existing authenticated domain adapter, not an endpoint guessed by the shell.</summary>
    public HomeCoreOperationResult<bool> Register(IHomeFeatureRouteHandler handler,
        Func<HomeFeatureViewState, BrowserCuiSurface> render, BrowserSurfaceScope scope = BrowserSurfaceScope.PrivateContext)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(render);
        var result = _navigation.Register(handler);
        if (result.Succeeded)
        {
            _renderers.Add(handler.RouteId, render);
            _handlers.Add(handler.RouteId, (handler, scope));
        }
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
