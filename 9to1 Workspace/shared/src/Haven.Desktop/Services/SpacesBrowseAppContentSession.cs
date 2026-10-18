using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
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
internal sealed partial class SpacesBrowseAppContentSession : IBrowseNativeAppContentSession, IBrowseAppContentClosePreparationParticipant
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
    private sealed class CommandExecution(SpacesBrowseAppContentSession owner)
    {
        internal readonly SpacesBrowseAppContentSession Owner = owner;
        internal Task? Original;
        internal bool Active;
    }
    private static readonly AsyncLocal<CommandExecution?> ExecutingCommand = new();
    private Task? _initialization;
    private Task? _close;
    private Task<bool>? _preparation;
    private readonly ContentControl _root = new();
    private SpacesHomePage? _spacesPage;
    private Action<SpacesHomeViewState>? _stateChangedHandlerSpaces;
    private NewChatPage? _chatPage;
    // Published only after the UI owner actually installs this exact Chat page.
    // Controller notifications may arrive on a pool thread; they consume this
    // immutable projection, never Avalonia properties or mutable Chat state.
    private sealed record PublishedChatRoute(NewChatPage Page, AppContentAddress Address);
    private PublishedChatRoute? _publishedChatRoute; // Guarded by _gate.
    private CuiSceneHost? _retiredDiagnosticHost;
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

    /// <summary>The canonical CUI failure presentation retained after a failed retirement.
    /// It is owned by this session and stays live in the same tab; the disposed Chat/Spaces
    /// views are never republished in its place. Null until a retirement actually fails.</summary>
    internal CuiSceneHost? RetiredDiagnosticHost => _retiredDiagnosticHost;

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
            var page = new SpacesHomePage(registry, navigationHost, SpacesHomePage.ImmediateReadinessInstance,
                queueNavigation: QueueSpaceAction);
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
                // Saved Chat routes carry a canonical ConversationId, including the
                // existing legacy workspace migration. Restore it before publishing
                // a generic Spaces location over the saved target.
                if (!await TryRestoreRequestedBuilderAsync(token) && !await TryRestoreRequestedDetailsAsync(token) && !await TryRestoreRequestedConversationAsync(token))
                {
                    UpdateAddress(page.Controller.State.Selection?.Id);
                    SetRootContent(page);
                    _routeEpoch++;
                }
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
            PublishStateChanged();
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
        var preparation = await PrepareCloseAsync(linked.Token).ConfigureAwait(false);
        if (preparation is null) return false;
        await preparation.AbortAsync().ConfigureAwait(false);
        return true; // Compatibility observation never leaves a hidden mutation freeze.
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
        // Cancel/retire actual editor owners BEFORE joining a pending decision/save preparation.
        // A preparation may be waiting for the same page cancellation or real save source.
        BeginDetailsRetirement(failures);
        BeginBuilderRetirement(failures);
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
        Task[] preparations;
        lock (_gate) preparations = _detailsPreparations.ToArray();
        foreach (var preparation in preparations)
            try { await preparation.ConfigureAwait(false); }
            catch (OperationCanceledException) when (preparation.IsCanceled && _token.IsCancellationRequested) { }
            catch (Exception error) { AddActualCauses(failures, preparation, error); }
        // A command acquired before retirement may have produced an owner late.
        BeginDetailsRetirement(failures);
        BeginBuilderRetirement(failures);
        await JoinDetailsRetirementAsync(failures).ConfigureAwait(false);
        await JoinBuilderRetirementAsync(failures).ConfigureAwait(false);
        await OnUiAsync(async () =>
        {
            foreach (var chat in _ownedChats.ToArray())
            {
                UnobserveConversationRoute(chat);
                try { await chat.DisposeAsync(); _ownedChats.Remove(chat); }
                catch (Exception error) { failures.Add(error); }
            }
            if (_spacesPage is not null && _stateChangedHandlerSpaces is not null)
                _spacesPage.Controller.StateChanged -= _stateChangedHandlerSpaces;
            foreach (var builder in _ownedBuilders.ToArray())
                try { await builder.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            lock (_gate) { _ownedBuilders.Clear(); _buildersBySpace.Clear(); _builderCloses.Clear(); }
            if (_spacesPage is IAsyncDisposable spacesDisposable)
                try { await spacesDisposable.DisposeAsync(); }
                catch (Exception error) { failures.Add(error); }
            lock (_gate) { _chatPage = null; _publishedChatRoute = null; _publishedDetailsRoute = null; _publishedBuilderRoute = null; }
            _chatsBySpace.Clear();
            _spacesPage = null;
            // Only a clean retirement may drop the root to an empty tab. A failed retirement
            // presents the SAME canonical CUI failure surface the rest of the shell uses: the
            // disposed Chat/Spaces controls are never republished as live, the retirement never
            // becomes clean, and every retained cause stays in the thrown aggregate.
            if (RequiresFailedRetirementPresentation(failures))
                await PresentFailedRetirementAsync(failures);
            else
                SetRootContent(null);
            _page = null;
        }).ConfigureAwait(false);
        _commandSerial.Dispose();
        _lifetime.Dispose();
        if (failures.Count > 0)
            throw new AggregateException("The original Spaces work did not retire cleanly.", failures);
    }

    /// <summary>A clean retirement may drop to an empty tab; a failed retirement must keep a
    /// canonical, owned CUI failure presentation so the tab never silently becomes clean.
    /// Exposed for the bounded retirement-decision test.</summary>
    internal static bool RequiresFailedRetirementPresentation(IReadOnlyList<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return failures.Count > 0;
    }

    /// <summary>Presents the canonical CUI unavailable scene for a failed Spaces retirement.
    /// The host owns the scene loader/action/cleanup lifetime and the canonical presentation
    /// binds the accessible user-facing text. The exact <see cref="Exception"/> objects stay in
    /// the caller's aggregate; only their message text is surfaced here. If even that canonical
    /// presentation cannot settle, the exact cause is retained and the tab drops to empty —
    /// it never republishes the disposed views and never becomes a clean retirement.</summary>
    private async Task PresentFailedRetirementAsync(List<Exception> failures)
    {
        Dispatcher.UIThread.VerifyAccess();
        var host = new CuiSceneHost();
        _retiredDiagnosticHost = host;
        try
        {
            await host.ShowAsync(BuildFailedRetirementScene(failures)).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            failures.Add(error);
            SetRootContent(null);
            return;
        }
        SetRootContent(host);
    }

    /// <summary>Builds the canonical CUI failure scene for a failed Spaces retirement. Exposed
    /// so the bounded test can assert the exact accessible text and the non-ready readiness
    /// handshake without constructing the heavy native page.</summary>
    internal static CuiNativeScene BuildFailedRetirementScene(IReadOnlyList<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0)
            throw new ArgumentException("A failed retirement presentation requires at least one retained cause.", nameof(failures));
        var detail = string.Join(
            Environment.NewLine + Environment.NewLine,
            failures.Select(error => error.Message));
        var message = "This Spaces tab did not retire cleanly. The original view is no longer live; "
            + "the retained failure is kept here for diagnosis."
            + Environment.NewLine + Environment.NewLine + detail;
        return new CuiNativeScene("spaces.retirement", "Spaces", "Spaces",
            FailedRetirementDocument, UnavailableSceneBindings.Instance, UnavailableSceneBindings.Instance,
            new FailedRetirementReadiness(message));
    }

    private static readonly CuiDocument FailedRetirementDocument = new CuiRichParser().Parse(
        "<Cui><Container Type=\"Vertical Stack\"><Text id=\"spaces-retirement\" text=\"Spaces retirement\" /></Container></Cui>");

    /// <summary>The canonical host substitutes its own accessible availability text for any
    /// non-ready scene, so this supplies only the typed readiness handshake.</summary>
    private sealed class FailedRetirementReadiness(string message) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable, "spaces-retirement-failed", message));
    }

    /// <summary>Bindings/dispatch for an unavailable canonical scene. The canonical host never
    /// consults them for a non-ready scene, and this diagnostic admits no actions.</summary>
    private sealed class UnavailableSceneBindings : ICuiBindingContext, ICuiActionDispatcher
    {
        internal static UnavailableSceneBindings Instance { get; } = new();
        public bool TryGetValue(string path, out object? value) { value = null; return false; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("A failed-retirement diagnostic admits no actions."));
    }

    private void OnSpacesStateChanged(SpacesHomeViewState state)
    {
        lock (_gate)
        {
            if (_closing || !_published) return;
            UpdateAddress(state.Selection?.Id);
        }
        PublishStateChanged();
    }

    private void UpdateAddress(Guid? selectedSpaceId)
    {
        lock (_gate)
        {
            if (_publishedDetailsRoute is { } details)
                _address = details.Address;
            else if (_publishedChatRoute is { } chat)
                _address = chat.Address;
            else if (selectedSpaceId is { } id && id != Guid.Empty)
                _address = AppContentAddress.Parse($"9to1://spaces/selected/{id:D}");
            else
                _address = AppContentAddress.Parse("9to1://spaces");
        }
    }

    // All callers hold _gate and retain the actual UI dispatcher. The native
    // setter must succeed before its current page/address projection is published.
    private void PublishChatPage(NewChatPage chat)
    {
        Dispatcher.UIThread.VerifyAccess();
        SetRootContent(chat);
        _publishedDetailsRoute = null;
        _routeEpoch++;
        _chatPage = chat;
        _publishedChatRoute = new(chat, ConversationAddress(chat.CurrentConversation));
        _address = _publishedChatRoute.Address;
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

    private void PublishStateChanged() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    });

    private void SetRootContent(Control? content)
    {
        Dispatcher.UIThread.VerifyAccess();
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            _root.Content = content;
            return true;
        });
    }

    private void DemandExternalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        if (ExecutingCommand.Value is { } execution && ReferenceEquals(execution.Owner, this) &&
            Volatile.Read(ref execution.Active) && execution.Original is { IsCompleted: false })
            throw new InvalidOperationException("An active Spaces command cannot join its own retirement.");
    }

    private Task ExecuteCommandAsync(SpacesAction action, CancellationToken caller)
    {
        lock (_gate)
        {
            DemandAlive();
            caller.ThrowIfCancellationRequested();
            if (_activeDetailsPreparation is not null)
                throw new InvalidOperationException("Abort the exact Spaces close preparation before navigating.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var execution = new CommandExecution(this);
            var actual = ExecuteCommandDriverAsync(start.Task, action, caller, execution);
            execution.Original = actual;
            _commands.Add(actual);
            start.SetResult();
            return actual;
        }
    }

    private async Task ExecuteCommandDriverAsync(Task start, SpacesAction action, CancellationToken caller, CommandExecution execution)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        await _commandSerial.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await OnUiAsync(async () =>
            {
                var previous = ExecutingCommand.Value;
                ExecutingCommand.Value = execution; Volatile.Write(ref execution.Active, true);
                try { await ExecuteActionOnUiAsync(action, linked.Token); }
                finally { ExecutingCommand.Value = previous; }
            }).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref execution.Active, false); _commandSerial.Release(); }
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
        if (_chatsBySpace.TryGetValue(key, out var existing) && existing.CurrentConversation.SpaceId == spaceId)
        {
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                DemandAlive();
                PublishChatPage(existing);
            }
            PublishStateChanged();
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
                PublishChatPage(page);
                _chatsBySpace[key] = page;
                ObserveConversationRoute(page);
                published = true;
            }
            PublishStateChanged();
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

    private sealed class HavenSpacesActionHost(SpacesBrowseAppContentSession owner) : ISpacesActionHost, ISpacesActionSupport
    {
        public bool SupportsAction(SpacesActionKind action) => action is SpacesActionKind.Open or SpacesActionKind.EditDetails;
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
