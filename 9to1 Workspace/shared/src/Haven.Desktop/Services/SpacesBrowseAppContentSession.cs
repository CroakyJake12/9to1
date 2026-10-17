using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Home.Core;
using HavenOS.Apps.Spaces.Home;
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
    private readonly List<Task> _commands = [];
    private readonly SemaphoreSlim _commandSerial = new(1, 1);
    private readonly Dictionary<Guid, NewChatPage> _chatsBySpace = [];
    private readonly List<NewChatPage> _ownedChats = [];
    private static readonly AsyncLocal<SpacesBrowseAppContentSession?> ExecutingCommand = new();
    private Task? _initialization;
    private Task? _close;
    private Task<bool>? _preparation;
    private readonly ContentControl _root = new();
    private SpacesHomePage? _spacesPage;
    private Action<SpacesHomeViewState>? _stateChangedHandlerSpaces;
    private NewChatPage? _chatPage;
    private volatile Control? _page;
    private volatile AppContentAddress _address;
    private volatile bool _published;
    private volatile bool _closing;

    internal SpacesBrowseAppContentSession(BrowseAppContentRequest request, IServiceProvider services,
        CancellationToken hostLifetime)
    {
        _request = request;
        _services = services;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
        _token = _lifetime.Token;
        _address = request.Address;
    }

    public AppContentAddress Address => _address;
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
            var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
            if (home.OriginalStartTask is { } homeStart)
                await homeStart;
            // This continuation creates and activates the native page. Retain the
            // dispatcher context while genuine Home admission performs async I/O.
            var actor = await home.Profiles.GetCurrentAsync(token)
                ?? throw new UnauthorizedAccessException("The authenticated Home profile is unavailable.");
            if (!string.Equals(actor.ProfileId, _request.ProfileId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The Spaces tab does not belong to the current authenticated profile.");
            var runtime = home.Runtime.Current;
            foreach (var required in new[] { "home.core", "home.state", "permissions.trust" })
            {
                var service = runtime.Services.SingleOrDefault(item => item.ServiceId == required);
                if (service is null || !service.IsAvailable || service.State != HomeServiceLifecycleState.Ready)
                    throw new UnauthorizedAccessException("A required Home service is unavailable for Spaces.");
            }
            var registry = _services.GetRequiredService<SpaceRegistry>();
            var navigationHost = new HavenSpacesActionHost(this);
            var page = new SpacesHomePage(registry, navigationHost, SpacesHomePage.ImmediateReadinessInstance);
            _spacesPage = page;
            try
            {
                await page.ActivateAsync(token);
                token.ThrowIfCancellationRequested();
                if (_closing) throw new OperationCanceledException();
                _stateChangedHandlerSpaces = OnSpacesStateChanged;
                page.Controller.StateChanged += _stateChangedHandlerSpaces;
                if (TryParseSelectedSpace(_request.Address, out var selectedSpaceId))
                    await page.Controller.SelectSpaceAsync(selectedSpaceId, token);
                UpdateAddress(page.Controller.State.Selection?.Id);
                _root.Content = page;
                _page = _root;
                _published = true;
            }
            catch
            {
                if (page is IAsyncDisposable disposable)
                    await disposable.DisposeAsync();
                _spacesPage = null;
                throw;
            }
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
            if (_closing || !_published || _spacesPage is null) return false;
            return await _spacesPage.TryPrepareToCloseAsync(linked.Token);
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
        List<Exception> failures = [];
        try { _lifetime.Cancel(); } catch (Exception error) { failures.Add(error); }
        if (_initialization is { } initialization)
            try { await initialization.ConfigureAwait(false); }
            catch (OperationCanceledException) when (initialization.IsCanceled && _token.IsCancellationRequested) { }
            catch (Exception error) { failures.Add(error); }
        Task[] commands;
        lock (_gate) commands = _commands.ToArray();
        foreach (var command in commands)
        {
            try { await command.ConfigureAwait(false); }
            catch (OperationCanceledException) when (command.IsCanceled && _token.IsCancellationRequested) { }
            catch (Exception error) { failures.Add(error); }
        }
        await OnUiAsync(async () =>
        {
            foreach (var chat in _ownedChats.ToArray())
            {
                try { await chat.DisposeAsync(); _ownedChats.Remove(chat); }
                catch (Exception error) { failures.Add(error); }
            }
            if (_spacesPage is not null && _stateChangedHandlerSpaces is not null)
                _spacesPage.Controller.StateChanged -= _stateChangedHandlerSpaces;
            if (_spacesPage is IAsyncDisposable spacesDisposable)
                try { await spacesDisposable.DisposeAsync(); }
                catch (Exception error) { failures.Add(error); }
            _chatPage = null;
            _chatsBySpace.Clear();
            _spacesPage = null;
            _root.Content = null;
            _page = null;
        }).ConfigureAwait(false);
        _commandSerial.Dispose();
        _lifetime.Dispose();
        if (failures.Count > 0)
            throw new AggregateException("The original Spaces work did not retire cleanly.", failures);
    }

    private void OnSpacesStateChanged(SpacesHomeViewState state)
    {
        if (_closing) return;
        UpdateAddress(state.Selection?.Id);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAddress(Guid? selectedSpaceId)
    {
        if (selectedSpaceId is { } id && id != Guid.Empty)
            _address = AppContentAddress.Parse($"9to1://spaces/selected/{id:D}");
        else
            _address = AppContentAddress.Parse("9to1://spaces");
    }

    private static bool TryParseSelectedSpace(AppContentAddress address, out Guid selectedSpaceId)
    {
        selectedSpaceId = Guid.Empty;
        if (address.ResourceSegments.Count < 2 || !string.Equals(address.ResourceSegments[0], "selected", StringComparison.Ordinal))
            return false;
        return Guid.TryParseExact(address.ResourceSegments[1], "D", out selectedSpaceId) && selectedSpaceId != Guid.Empty;
    }

    private void DemandAlive()
    {
        ObjectDisposedException.ThrowIf(_closing, this);
        _token.ThrowIfCancellationRequested();
    }

    private void DemandExternalJoin()
    {
        if (ReferenceEquals(ExecutingCommand.Value, this))
            throw new InvalidOperationException("An active Spaces command cannot join its own retirement.");
    }

    private Task ExecuteCommandAsync(SpacesAction action, CancellationToken caller)
    {
        lock (_gate)
        {
            DemandAlive();
            caller.ThrowIfCancellationRequested();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = ExecuteCommandDriverAsync(start.Task, action, caller);
            _commands.Add(actual);
            start.SetResult();
            return actual;
        }
    }

    private async Task ExecuteCommandDriverAsync(Task start, SpacesAction action, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        await _commandSerial.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await OnUiAsync(async () =>
            {
                var previous = ExecutingCommand.Value;
                ExecutingCommand.Value = this;
                try { await ExecuteChatOnUiAsync(action, linked.Token); }
                finally { ExecutingCommand.Value = previous; }
            }).ConfigureAwait(false);
        }
        finally { _commandSerial.Release(); }
    }

    private async Task ExecuteChatOnUiAsync(SpacesAction action, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        DemandAlive();
        if (action.Context.Definition.Destination != SpacesDestinationKind.Chat)
            throw new NotSupportedException("Only the Chat destination is available in this Haven tab.");
        var spaceId = action.Context.Scope.RegisteredSpaceId;
        var key = spaceId ?? Guid.Empty;
        if (_chatsBySpace.TryGetValue(key, out var existing))
        {
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                DemandAlive();
                _chatPage = existing;
                _root.Content = existing;
            }
            return;
        }
        var factory = new OverlayChatSessionFactory(_services,
            _services.GetRequiredService<IConversationRepository>(),
            _services.GetRequiredService<CapabilityRegistryService>(),
            _services.GetRequiredService<ICatalogRepository>(),
            _services.GetRequiredService<IModeRegistry>());
        var page = await factory.CreateNewAsync(token);
        _ownedChats.Add(page);
        var published = false;
        try
        {
            token.ThrowIfCancellationRequested();
            DemandAlive();
            await page.AssignSpaceAsync(spaceId);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                DemandAlive();
                page.SetDraft(string.Empty);
                _root.Content = page;
                _chatsBySpace.Add(key, page);
                _chatPage = page;
                published = true;
            }
        }
        finally
        {
            if (!published)
            {
                await page.DisposeAsync();
                _ownedChats.Remove(page);
            }
        }
    }

    private sealed class HavenSpacesActionHost(SpacesBrowseAppContentSession owner) : ISpacesActionHost
    {
        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default) =>
            owner.ExecuteCommandAsync(action, cancellationToken);
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
