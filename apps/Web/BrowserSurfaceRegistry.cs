using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;

namespace NineToOne.Web;

/// <summary>Platform presentation binding for a successful, owner-provided domain route.</summary>
public sealed record BrowserCuiSurface(CuiDocument Document, ICuiBindingContext Bindings,
    ICuiActionDispatcher Actions, IDisposable? Lifetime = null, CuiControlRegistry? ControlRegistry = null,
    IBrowserPresentationAdmission? Admission = null);

/// <summary>Commit a prepared owner view only after the candidate CUI successfully loads.</summary>
public interface IBrowserPresentationAdmission
{
    void Accept();
    void Reject();
}

public enum BrowserSurfaceScope { PrivateContext, DeviceLocal }

/// <summary>A local owner can preserve its draft when preparation fails.</summary>
public interface IBrowserCloseParticipant
{
    bool HasUnsavedChanges { get; }
    Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default);
}

public sealed class BrowserSurfaceRegistry
{
    private HomeFeatureNavigationHost _navigation = new();
    private readonly Dictionary<string, Func<HomeFeatureViewState, BrowserCuiSurface>> _renderers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IHomeFeatureRouteHandler Handler, BrowserSurfaceScope Scope)> _handlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _closeOperations = new(1, 1);
    private bool _closing;

    public IReadOnlyCollection<string> AvailableRoutes => _navigation.AvailableRoutes;
    public bool HasUnsavedChanges => _handlers.Values.Any(entry => entry.Handler is IBrowserCloseParticipant { HasUnsavedChanges: true });

    public async Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        foreach (var participant in _handlers.Values.Select(entry => entry.Handler).OfType<IBrowserCloseParticipant>().ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await participant.PrepareToCloseAsync(cancellationToken);
            if (!result.Succeeded || result.Value != true) return result;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(true, "Succeeded", "Browser owners are ready to close.", true);
    }

    /// <summary>Failed preparation preserves every route and draft; successful preparation precedes detachment.</summary>
    public async Task<HomeCoreOperationResult<bool>> ClearAsync(CancellationToken cancellationToken = default)
    {
        await _closeOperations.WaitAsync(cancellationToken);
        _closing = true;
        try
        {
            var prepared = await PrepareToCloseAsync(cancellationToken);
            if (!prepared.Succeeded || prepared.Value != true) return prepared;
            cancellationToken.ThrowIfCancellationRequested();
            var removed = DetachHandlers(privateOnly: false);
            List<Exception>? errors = null;
            foreach (var handler in removed)
            {
                try
                {
                    if (handler is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
                    else if (handler is IDisposable lifetime) lifetime.Dispose();
                }
                catch (Exception error) { (errors ??= []).Add(error); }
            }
            if (errors is not null) throw new AggregateException("Browser owners were detached with teardown failures.", errors);
            return new(true, "Succeeded", "Browser owners closed.", true);
        }
        finally { _closing = false; _closeOperations.Release(); }
    }

    /// <summary>Remove prior account/organisation adapters before a new private context is opened.</summary>
    public void Clear()
    {
        if (_handlers.Values.Any(entry => entry.Handler is IAsyncDisposable or IBrowserCloseParticipant))
            throw new InvalidOperationException("Asynchronous browser owners require awaited ClearAsync before detachment.");
        RemoveHandlers(privateOnly: false);
    }

    /// <summary>Device-local projects retain their session; no account authority is transferred to them.</summary>
    public void ClearPrivateContext() => RemoveHandlers(privateOnly: true);

    private void RemoveHandlers(bool privateOnly)
    {
        var removed = DetachHandlers(privateOnly);
        List<Exception>? errors = null;
        foreach (var handler in removed)
        {
            if (handler is not IDisposable lifetime) continue;
            try { lifetime.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is not null) throw new AggregateException("Browser route teardown failed after its context was removed.", errors);
    }

    private IHomeFeatureRouteHandler[] DetachHandlers(bool privateOnly)
    {
        var removed = _handlers.Where(pair => !privateOnly || pair.Value.Scope == BrowserSurfaceScope.PrivateContext).ToArray();
        foreach (var pair in removed) { _handlers.Remove(pair.Key); _renderers.Remove(pair.Key); }
        // Invalidate every in-flight request before calling an owner's teardown.
        _navigation = new();
        foreach (var pair in _handlers.Values) _navigation.Register(pair.Handler);
        return removed.Select(pair => pair.Value.Handler).ToArray();
    }

    /// <summary>The caller supplies the existing authenticated domain adapter, not an endpoint guessed by the shell.</summary>
    public HomeCoreOperationResult<bool> Register(IHomeFeatureRouteHandler handler,
        Func<HomeFeatureViewState, BrowserCuiSurface> render, BrowserSurfaceScope scope = BrowserSurfaceScope.PrivateContext)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(render);
        if (scope == BrowserSurfaceScope.PrivateContext && handler is IAsyncDisposable or IBrowserCloseParticipant)
            return new(false, "HomeServiceIncompatible", "This private owner requires an asynchronous context teardown contract.");
        if (_closing) return new(false, "BrowserClosing", "Wait for the browser owners to finish closing.");
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
        var prepared = await PreparePresentationAsync(request, cancellationToken);
        var surface = prepared.Present?.Invoke();
        if (prepared.Result.Succeeded && surface is null)
            return (new(false, "BrowserPresentationExpired", "This presentation is no longer current.", request), null);
        return (prepared.Result, surface);
    }

    internal async Task<(HomeFeatureNavigationResult Result, Func<BrowserCuiSurface?>? Present)> PreparePresentationAsync(
        HomeFeatureNavigationRequest request, CancellationToken cancellationToken)
    {
        if (_closing) return (new(false, "BrowserClosing", "Browser owners are closing.", request), null);
        var navigation = _navigation;
        if (!_renderers.TryGetValue(request.RouteId, out var render))
            return (new(false, "HomeServiceUnavailable", "This destination is unavailable.", request), null);
        var result = await navigation.NavigateAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_closing) return (new(false, "BrowserClosing", "Browser owners are closing.", request), null);
        if (!result.Succeeded) return (result, null);
        if (result.ViewState is null || !string.Equals(result.ViewState.RouteId, request.RouteId, StringComparison.Ordinal))
            return (new(false, "HomeServiceIncompatible", "This destination did not provide a compatible view.", request), null);
        if (!ReferenceEquals(navigation, _navigation))
            return (new(false, "PermissionDenied", "The account context has changed.", request), null);
        return (result, () => cancellationToken.IsCancellationRequested || _closing || !ReferenceEquals(navigation, _navigation)
            ? null : render(result.ViewState));
    }
}
