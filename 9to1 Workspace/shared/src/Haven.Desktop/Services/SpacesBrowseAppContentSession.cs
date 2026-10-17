using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Home;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>A distinct Spaces view owner per Haven tab, borrowing the shared Home services.</summary>
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
    private volatile Control? _view;
    private volatile bool _published;
    private volatile bool _closing;
    private SpacesHomeController? _controller;
    private SpacesHomeCuiBindings? _bindings;
    private CuiControlLoader? _loader;

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
    public bool IsPublicationCurrent => _published && !_closing && !_token.IsCancellationRequested
        && _view is not null;
    public bool CanRetireOriginalView => _close is { IsCompletedSuccessfully: true };
    public Task? OriginalInitialization => _initialization;
    public Task? OriginalClose => _close;

    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!_published || _closing || _view is null)
                throw new InvalidOperationException("The Spaces view has not completed admission.");
            return _view;
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
            var registry = new SpaceRegistry(_services.GetRequiredService<IVersionedSettingsStore>());
            _controller = new SpacesHomeController(registry);
            _bindings = new SpacesHomeCuiBindings(() => _controller.State, async (command, parameter, cancellationToken) =>
            {
                var id = parameter is Guid g ? g : _controller.State.Selection?.Id ?? Guid.Empty;
                switch (command)
                {
                    case "spaces.create": await _controller.CreateCustomSpaceAsync(cancellationToken); break;
                    case "spaces.open": await _controller.OpenSpaceAsync(id, cancellationToken); break;
                    case "spaces.rename": _controller.StartRename(id); break;
                    case "spaces.move": await _controller.MoveSpaceAsync(id, cancellationToken); break;
                    case "spaces.fork": await _controller.ForkSpaceAsync(id, cancellationToken); break;
                    case "spaces.archive": await _controller.ArchiveSpaceAsync(id, cancellationToken); break;
                    case "spaces.add-context": await _controller.AddContextAsync(id, null, cancellationToken); break;
                    case "spaces.clear-search": _controller.SetQuery(string.Empty); break;
                    case "spaces.set-query": _controller.SetQuery(parameter?.ToString()); break;
                    case "spaces.confirm-rename": await _controller.ConfirmRenameAsync(cancellationToken); break;
                    case "spaces.cancel-rename": _controller.CancelRename(); break;
                    case "spaces.select-move-parent": _controller.SelectMoveParentIndex((int)parameter!); break;
                    case "spaces.edit-extensions": await _controller.EditExtensionsAsync(id, cancellationToken); break;
                    case "spaces.edit-graph": await _controller.EditGraphAsync(id, cancellationToken); break;
                    default: throw new InvalidOperationException($"Unknown Spaces command {command}");
                }
            });
            _controller.StateChanged += _ => _bindings.Refresh();

            _loader = new CuiControlLoader();
            _loader.SetBindingContext(_bindings);
            _loader.SetActionDispatcher(_bindings);
            using var stream = typeof(SpacesHomeCuiDocument).Assembly
                .GetManifestResourceStream(SpacesHomeCuiDocument.ResourceName)
                ?? throw new InvalidOperationException("Embedded Spaces CUI resource was not found.");
            using var reader = new StreamReader(stream);
            var (tree, diagnostics) = _loader.LoadMarkup(reader.ReadToEnd());
            if (diagnostics.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics));
            var root = tree as Control ?? throw new InvalidOperationException("Spaces CUI did not produce a root control.");
            _view = root;
            _loader.WireBindings(root);

            await _controller.LoadAsync(token);
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
            _preparation.ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
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
            return !_closing && _published && _view is not null;
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
            if (_loader is not null)
            {
                _loader.Dispose();
                _loader = null;
            }
            _view = null;
            _controller = null;
            _bindings = null;
        }).ConfigureAwait(false);
        _lifetime.Cancel();
        try { _lifetime.Dispose(); } catch { }
    }

    private void DemandAlive() => ObjectDisposedException.ThrowIf(_closing, this);

    private void DemandExternalJoin()
    {
        // Minimal session does not own the Home original composition; the host validates
        // this session before publication. No cross-owner join is retained here.
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
