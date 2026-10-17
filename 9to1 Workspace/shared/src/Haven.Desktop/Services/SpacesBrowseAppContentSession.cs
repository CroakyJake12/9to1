using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>A distinct Spaces view owner per Haven tab, reusing the canonical
/// Spaces Home page and its services.</summary>
internal sealed class SpacesBrowseAppContentSession : IBrowseNativeAppContentSession
{
    private readonly BrowseAppContentRequest _request;
    private readonly IServiceProvider _services;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly object _gate = new();
    private Task? _initialization;
    private Task? _close;
    private Task<bool>? _preparation;
    private volatile SpacesHomePage? _page;
    private volatile bool _published;
    private volatile bool _closing;

    internal SpacesBrowseAppContentSession(BrowseAppContentRequest request, IServiceProvider services,
        CancellationToken hostLifetime)
    {
        _request = request;
        _services = services;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
        _token = _lifetime.Token;
    }

    public AppContentAddress Address => _request.Address;
    public string Title => "Spaces";
    public string IconKey => "spaces";
    public event EventHandler? StateChanged;
    public bool IsPublicationCurrent => _published && !_closing && !_token.IsCancellationRequested && _page is not null;
    public bool CanRetireOriginalView => _close is { IsCompletedSuccessfully: true };
    public Task? OriginalInitialization => _initialization;
    public Task? OriginalClose => _close;

    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!_published || _closing || _page is null)
                throw new InvalidOperationException("The Spaces view has not completed admission.");
            return _page;
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            DemandAlive();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = InitializeDriverAsync(gate.Task, cancellationToken);
            gate.SetResult();
            return _initialization;
        }
    }

    private async Task InitializeDriverAsync(Task gate, CancellationToken caller)
    {
        await gate.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var token = linked.Token;
        await OnUiAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            DemandAlive();
            var registry = _services.GetRequiredService<SpaceRegistry>();
            var page = new SpacesHomePage(registry, new UnavailableSpacesActionHost(), SpacesHomePage.ImmediateReadinessInstance);
            await page.ActivateAsync(token);
            _page = page;
            _published = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }).ConfigureAwait(false);
    }

    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return _preparation ??= Task.FromResult(false);
            if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _preparation = PrepareDriverAsync(gate.Task, cancellationToken);
            gate.SetResult();
            return _preparation;
        }
    }

    private async Task<bool> PrepareDriverAsync(Task gate, CancellationToken caller)
    {
        await gate.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        return await OnUiAsync(async () =>
        {
            linked.Token.ThrowIfCancellationRequested();
            if (_closing || !_published || _page is null) return false;
            return await _page.TryPrepareToCloseAsync(linked.Token);
        }).ConfigureAwait(false);
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true;
            _published = false;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseDriverAsync(gate.Task);
            gate.SetResult();
            return _close;
        }
    }

    private async Task CloseDriverAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        await OnUiAsync(async () =>
        {
            if (_page is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
            _page = null;
        }).ConfigureAwait(false);
        _lifetime.Cancel();
        try { _lifetime.Dispose(); } catch { }
    }

    private void DemandAlive() => ObjectDisposedException.ThrowIf(_closing, this);

    private void DemandExternalJoin()
    {
        // The Spaces page owns its original work lifetime and close preflight.
    }

    private sealed class UnavailableSpacesActionHost : ISpacesActionHost
    {
        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default) =>
            Task.FromException(new NotSupportedException("Space destination launch is not available in this Haven tab."));
    }

    private static async Task OnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) { await action(); return; }
        var actual = await Dispatcher.UIThread.InvokeAsync<Task>(action).GetTask();
        await actual.ConfigureAwait(false);
    }

    private static async Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return await action();
        var actual = await Dispatcher.UIThread.InvokeAsync<Task<T>>(action).GetTask();
        return await actual.ConfigureAwait(false);
    }
}
