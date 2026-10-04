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

/// <summary>A private owner must synchronously revoke its real authority and hide
/// all owner state before callbacks or cancellation; DisposeAsync then drains every
/// issued factory, owner command and cleanup. Revocation cannot save or be vetoed.</summary>
public interface IBrowserPrivateContextParticipant : IAsyncDisposable
{
    void RevokePrivateContext();
}

public sealed class BrowserSurfaceRegistry
{
    private HomeFeatureNavigationHost _navigation = new();
    private readonly Dictionary<string, Func<HomeFeatureViewState, BrowserCuiSurface>> _renderers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IHomeFeatureRouteHandler Handler, BrowserSurfaceScope Scope)> _handlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _closeOperations = new(1, 1);
    private bool _closing;
    private readonly object _privateResetGate = new();
    private readonly HashSet<PrivateContextReset> _privateResets = [];
    private bool PrivateResetPending { get { lock (_privateResetGate) return _privateResets.Count != 0; } }

    // Capture the owner's declared route once at registration. Rebuilding the
    // presentation registry must not execute owner getters before revocation.
    private sealed class CapturedRoute(string routeId, IHomeFeatureRouteHandler owner) : IHomeFeatureRouteHandler
    {
        public string RouteId => routeId;
        public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,
            CancellationToken cancellationToken = default) => owner.OpenAsync(request, cancellationToken);
    }

    internal bool CanResetPrivateContextSynchronously => !PrivateResetPending
        && !_handlers.Values.Any(entry => entry.Scope == BrowserSurfaceScope.PrivateContext
            && entry.Handler is IAsyncDisposable);

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
            await DrainPrivateContextResetsAsync();
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
        finally
        {
            try { await DrainPrivateContextResetsAsync(); }
            finally { _closing = false; _closeOperations.Release(); }
        }
    }

    /// <summary>Remove prior account/organisation adapters before a new private context is opened.</summary>
    public void Clear()
    {
        if (_handlers.Values.Any(entry => entry.Handler is IAsyncDisposable or IBrowserCloseParticipant))
            throw new InvalidOperationException("Asynchronous browser owners require awaited ClearAsync before detachment.");
        RemoveHandlers(privateOnly: false);
    }

    /// <summary>Device-local projects retain their session; no account authority is transferred to them.</summary>
    public void ClearPrivateContext()
    {
        if (!CanResetPrivateContextSynchronously)
            throw new InvalidOperationException("Private asynchronous owners require the immediate revocation and awaited drain boundary.");
        RemoveHandlers(privateOnly: true);
    }

    /// <summary>Invalidates route admission and fences every old authority before
    /// returning. The application removes old visual/AX references before drain.</summary>
    internal PrivateContextReset BeginPrivateContextReset()
    {
        var removed = DetachHandlers(privateOnly: true);
        var errors = new List<Exception>();
        PrivateContextReset? reset = null;
        reset = new(removed, errors, () => { lock (_privateResetGate) _privateResets.Remove(reset!); });
        lock (_privateResetGate) _privateResets.Add(reset);
        foreach (var owner in removed.OfType<IBrowserPrivateContextParticipant>())
        {
            try { owner.RevokePrivateContext(); }
            catch (Exception error) { errors.Add(error); }
        }
        return reset;
    }

    internal async Task DrainPrivateContextResetsAsync()
    {
        List<Exception>? errors = null;
        while (true)
        {
            PrivateContextReset[] resets;
            lock (_privateResetGate) resets = _privateResets.ToArray();
            if (resets.Length == 0) break;
            var tasks = resets.Select(reset => reset.DrainAsync()).ToArray();
            try { await Task.WhenAll(tasks); }
            catch
            {
                foreach (var task in tasks)
                    if (task.Exception is { } failure) (errors ??= []).AddRange(failure.InnerExceptions);
            }
        }
        if (errors is not null)
            throw new AggregateException("Issued private owner drains failed.", errors);
    }

    internal sealed class PrivateContextReset(IHomeFeatureRouteHandler[] owners,
        List<Exception> errors, Action released)
    {
        private readonly object _gate = new();
        private Task? _drain;
        public Task DrainAsync()
        {
            TaskCompletionSource completion;
            lock (_gate)
            {
                if (_drain is not null) return _drain;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _drain = completion.Task;
            }
            _ = SettleDrainAsync(completion);
            return completion.Task;
        }
        private async Task SettleDrainAsync(TaskCompletionSource completion)
        {
            try { await DrainCoreAsync(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        private async Task DrainCoreAsync()
        {
            try
            {
                foreach (var owner in owners)
                {
                    try
                    {
                        if (owner is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
                        else if (owner is IDisposable synchronous) synchronous.Dispose();
                    }
                    catch (Exception error) { errors.Add(error); }
                }
                if (errors.Count != 0)
                    throw new AggregateException("Private browser owners were revoked with cleanup failures.", errors);
            }
            finally { released(); }
        }
    }

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
        foreach (var pair in _handlers) _navigation.Register(new CapturedRoute(pair.Key, pair.Value.Handler));
        return removed.Select(pair => pair.Value.Handler).ToArray();
    }

    /// <summary>The caller supplies the existing authenticated domain adapter, not an endpoint guessed by the shell.</summary>
    public HomeCoreOperationResult<bool> Register(IHomeFeatureRouteHandler handler,
        Func<HomeFeatureViewState, BrowserCuiSurface> render, BrowserSurfaceScope scope = BrowserSurfaceScope.PrivateContext)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(render);
        if (scope == BrowserSurfaceScope.PrivateContext && (handler is IAsyncDisposable or IBrowserCloseParticipant)
            && handler is not IBrowserPrivateContextParticipant)
            return new(false, "HomeServiceIncompatible", "This private owner requires an asynchronous context teardown contract.");
        if (scope == BrowserSurfaceScope.PrivateContext && PrivateResetPending)
            return new(false, "PrivateContextClosing", "Wait for the revoked private owners to finish cleanup.");
        if (_closing) return new(false, "BrowserClosing", "Wait for the browser owners to finish closing.");
        var routeId = handler.RouteId;
        var result = _navigation.Register(new CapturedRoute(routeId, handler));
        if (result.Succeeded)
        {
            _renderers.Add(routeId, render);
            _handlers.Add(routeId, (handler, scope));
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
