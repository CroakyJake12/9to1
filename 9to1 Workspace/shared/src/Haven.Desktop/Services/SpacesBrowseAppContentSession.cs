using Avalonia.Controls;
using Avalonia.Threading;
using System.Text;
#if !ANDROID
using Haven.Desktop.Views.Pages.Assistants;
using HavenOS.Apps.Assistants.Contracts;
#endif
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Home.Core;
using HavenOS.Apps.Spaces.Home;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed class SpacesHistoryAuthorityRefusal : Exception
{
    internal SpacesHistoryAuthorityRefusal(string message) : base(message) { }
}


/// <summary>A Space UI action that was refused before it could change anything. The refusal is
/// reported in the surface that owns the action instead of being retained as a close obligation,
/// so one failed click can never make the tab permanently unclosable.</summary>
internal sealed class SpacesWorkspaceActionRefusal : InvalidOperationException
{
    public SpacesWorkspaceActionRefusal(string message) : base(message) { }
}

/// <summary>A distinct Spaces view owner per Haven tab, reusing the canonical
/// Spaces Home page and its services.</summary>
internal sealed partial class SpacesBrowseAppContentSession : IBrowseNativeAppContentSession, IBrowseAppContentClosePreparationParticipant
{
    private readonly BrowseAppContentRequest _request;
    private readonly IServiceProvider _services;
    // The host's existing typed navigation contract, reused so an extension-contributed sidebar
    // element opens its target through the same tab-ownership and origin checks the Home launcher uses.
    private readonly Func<BrowseNavigationIntent, Task, bool>? _hostQueueNavigation;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly CancellationToken _appLifetime;
    private readonly CancellationToken _windowLifetime;
#if !ANDROID
    private readonly App? _actualApp;
    private readonly Window? _actualWindow;
#endif
    private readonly object _gate = new();
    private readonly List<Task> _commands = [];
    private readonly SemaphoreSlim _commandSerial = new(1, 1);
    private AuthenticatedResourceActor? _historyOriginalActor;
    private readonly Dictionary<Guid, NewChatPage> _chatsBySpace = [];
    private readonly List<NewChatPage> _ownedChats = [];
#if !ANDROID
    private readonly Dictionary<NewChatPage, (EventHandler Catalogue, EventHandler<AddMenuSelection> Selection)> _chatAssistantHandlers = [];
    private NativeAssistantsDesktopPage? _assistantPage;
    private sealed record PublishedAssistantRoute(NativeAssistantsDesktopPage Page, AppContentAddress Address,
        NewChatPage? PriorChat, AssistantIdentity? Identity);
    private PublishedAssistantRoute? _publishedAssistantRoute;
#endif
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
        CancellationToken hostLifetime, Func<BrowseNavigationIntent, Task, bool>? queueNavigation = null,
        Func<Haven.Desktop.Views.Pages.Tasks.SpaceTaskWidgetPage, NativeCanonicalTaskSceneReadiness>? bindTaskFrame = null,
        CancellationToken appLifetime = default, App? actualApp = null, Window? actualWindow = null)
    {
        _request = request;
        _services = services;
        _hostQueueNavigation = queueNavigation;
        _bindTaskFrame = bindTaskFrame;
        _appLifetime = appLifetime;
        _windowLifetime = hostLifetime;
#if !ANDROID
        if ((actualApp is null) != (actualWindow is null))
            throw new ArgumentException("Retain the actual App and native Window together.");
        _actualApp = actualApp;
        _actualWindow = actualWindow;
#endif
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime, appLifetime);
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
            _historyOriginalActor = actor;
            var registry = _services.GetRequiredService<SpaceRegistry>();
            var navigationHost = new HavenSpacesActionHost(this);
            var page = new SpacesHomePage(registry, navigationHost, SpacesHomePage.ImmediateReadinessInstance,
                queueNavigation: QueueSpaceAction,
                conversationsProvider: ReadSpaceConversationsAsync,
                sidebarItemsProvider: ReadSpaceSidebarItemsAsync,
                workspaceCommandHost: ExecuteWorkspaceCommandAsync,
                conversationSearchProvider: _services.GetRequiredService<IConversationRepository>() is ISpaceConversationSearch
                    ? FindSpaceConversationTitlesAsync : null,
                publishConversationRows: PublishOriginalConversationRowsAsync);
            _spacesPage = page;
            BindTaskSidebar(page);
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
                if (!await TryRestoreRequestedAssistantAsync(token) && !await TryRestoreRequestedBuilderAsync(token) &&
                    !await TryRestoreRequestedDetailsAsync(token) && !await TryRestoreRequestedTaskAsync(token) &&
                    !await TryRestoreRequestedConversationAsync(token))
                {
                    UpdateAddress(page.Controller.State.Selection?.Id);
                    SetRootContent(page);
                    _routeEpoch++;
                }
                _page = _root;
                _published = true;
                // The bootstrap publication guard is now set, so the selected Space's real recent
                // history can be read for the FINAL restored selection through the canonical provider
                // (ReadSpaceConversationsAsync). This is the genuine initial read: it runs at the
                // admitted startup boundary, it is part of this initialization task (original task /
                // cancellation / failure custody), and it never waits for a later presentation toggle,
                // a detached fire-and-forget read, a forced manager open or a second repository. It
                // reads the current controller selection, which after restore is the restored Space.
                await RefreshSpacesConversationsAsync(token);
            }
            catch (Exception initializationFailure)
            {
#if !ANDROID
                if (_assistantPage is not null)
                {
                    try { await RetireAssistantPresentationOnUiAsync(restorePriorChat: false); }
                    catch (Exception assistantCleanupFailure)
                    {
                        if (page is IAsyncDisposable failedSpaces) await failedSpaces.DisposeAsync();
                        _spacesPage = null;
                        throw new AggregateException("The Spaces or Assistant route failed to initialize and the partial Assistant page did not retire cleanly.",
                            initializationFailure, assistantCleanupFailure);
                    }
                }
#endif
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
        BeginTaskViewRetirement(failures);
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
        BeginTaskViewRetirement(failures);
        await JoinDetailsRetirementAsync(failures).ConfigureAwait(false);
        await JoinBuilderRetirementAsync(failures).ConfigureAwait(false);
        await JoinTaskViewRetirementAsync(failures).ConfigureAwait(false);
        await OnUiAsync(async () =>
        {
#if !ANDROID
            try { await RetireAssistantPresentationOnUiAsync(restorePriorChat: false); }
            catch (Exception error) { failures.Add(error); }
#endif
            foreach (var chat in _ownedChats.ToArray())
            {
                #if !ANDROID
                UnwireChatAssistants(chat);
                #endif
                UnobserveConversationRoute(chat);
                try { await chat.DisposeAsync(); _ownedChats.Remove(chat); }
                catch (Exception error) { failures.Add(error); }
            }
            if (_spacesPage is not null && _stateChangedHandlerSpaces is not null)
                _spacesPage.Controller.StateChanged -= _stateChangedHandlerSpaces;
            foreach (var builder in _ownedBuilders.ToArray())
                try { await builder.DisposeAsync(); if (failures.Count == 0) lock (_gate) { _ownedBuilders.Remove(builder); _builderCloses.Remove(builder); } }
                catch (Exception error) { failures.Add(error); }
            if (failures.Count == 0) lock (_gate) { _buildersBySpace.Clear(); }
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
        // The contextual sidebar follows the selected Space, so its recent conversations are
        // re-read from the real store whenever the selection publishes.
        ObserveSpacesSelectionChange();
    }

    private void UpdateAddress(Guid? selectedSpaceId)
    {
        lock (_gate)
        {
            if (_publishedAssistantRoute is { } assistant)
                _address = assistant.Address;
            else if (_publishedDetailsRoute is { } details)
                _address = details.Address;
            // A published editor owns the tab address. Without this the address fell back to the
            // selected-Space form whenever a later state change re-projected it, so the address bar
            // no longer identified the Space surface actually presented.
            else if (_publishedBuilderRoute is { } builder)
                _address = builder.Address;
            else if (_publishedChatRoute is { } chat)
                _address = chat.Address;
            else if (_publishedTaskRoute is { } task && ReferenceEquals(_spacesPage?.SpaceWorkspaceContent, task.Page))
                _address = task.Address;
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
        // The conversation is mounted inside the authored Spaces workspace region so the
        // icon-only Spaces rail and the conversation's own chat/history sidebar remain visible
        // together. Only when no Spaces Home region exists does Chat take the whole tab.
        var home = _spacesPage;
        if (home is not null && home.TryMountSpaceWorkspace(chat))
            SetRootContent(home);
        else
        {
            // The authored workspace may still hold this same page from an earlier in-place
            // mount. Detach it before taking the whole tab, otherwise one control would have
            // two native parents and the actual publication would fail.
            home?.ClearSpaceWorkspace(chat);
            SetRootContent(chat);
        }
        _publishedDetailsRoute = null;
        _publishedAssistantRoute = null;
        _publishedTaskRoute = null;
        _routeEpoch++;
        _chatPage = chat;
        _publishedChatRoute = new(chat, ConversationAddress(chat.CurrentConversation));
        _address = _publishedChatRoute.Address;
        // Close the presentation-only manager only after the successful setter AND
        // current chat/route projection. StateChanged observers must see that same tuple.
        // This synchronous callback has no await between the current mount and notification.
        if (home is not null && ReferenceEquals(_root.Content, home) &&
            ReferenceEquals(home.SpaceWorkspaceContent, chat))
            home.NotifyNavigationPublished();
    }

    /// <summary>Detaches a Space workspace that failed or was retired without replacement and
    /// restores the selected Space summary with its own address. Root still owns the detached
    /// page's actual close; this only removes it from the live presentation.</summary>
    private void ClearMountedWorkspaceLocked()
    {
        Dispatcher.UIThread.VerifyAccess();
        var home = _spacesPage;
        if (home is not null) home.ClearSpaceWorkspace();
        _publishedChatRoute = null;
        _publishedTaskRoute = null;
        _routeEpoch++;
        UpdateAddress(home?.Controller.State.Selection?.Id);
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
        Haven.Desktop.Views.Pages.Tasks.SpaceTaskWidgetPage[] taskViews;
        lock (_gate) taskViews = _ownedTaskViews.ToArray();
        foreach (var page in taskViews) page.DemandExternalOriginalRetirementJoin();
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
                try
                {
#if !ANDROID
                    await RetireAssistantPresentationOnUiAsync();
#endif
                    await ExecuteActionOnUiAsync(action, linked.Token);
                }
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
        // Every canonical Space type is a conversation Space: Study, Tasks, Translate, Research,
        // Shopping and Assistants are Space types, not separate apps. The Haven tab therefore
        // opens a conversation scoped to the selected Space for any registered destination.
        var spaceId = action.Context.Scope.RegisteredSpaceId;
        if (spaceId is not { } registeredSpaceId || registeredSpaceId == Guid.Empty)
            throw new InvalidOperationException("The Space action has no registered Space to scope its conversation.");
        var key = registeredSpaceId;
        // Opening/selecting a Space is an explicit new-chat action. Prior chats stay owned
        // with their identities and drafts; only explicit history/restore or management return
        // may reopen them. Reusing the last page here would silently undo that navigation intent.
        var actualSpace = await _services.GetRequiredService<SpaceRegistry>().ReadExistingAsync(registeredSpaceId, token)
            ?? throw new SpacesWorkspaceActionRefusal("The selected Space is unavailable.");
        if (actualSpace.IsArchived) throw new SpacesWorkspaceActionRefusal("The selected Space is archived.");
        var factory = NewChatSessionFactory();
        var page = await factory.CreateNewAsync(token);
        _ownedChats.Add(page);
        var published = false;
        try
        {
            token.ThrowIfCancellationRequested();
            DemandAlive();
            if (actualSpace.Kind == SpaceKind.Tasks) page.ConfigureTaskMode();
            await page.AssignSpaceAsync(spaceId);
#if !ANDROID
            await RetireAssistantPresentationOnUiAsync();
#endif
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
            await RefreshSpacesConversationsAsync(token);
        }
        finally
        {
            if (!published)
            {
#if !ANDROID
                UnwireChatAssistants(page);
#endif
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

    private sealed record HistoryHomeAuthority(AuthenticatedResourceActor Actor, long ServicesRevision);
    private sealed class OriginalHistoryRows(SpacesBrowseAppContentSession owner, HistoryHomeAuthority authority,
        SpacesHomeSelection selection, long routeEpoch, IReadOnlyList<SpacesHomeConversationRow> rows)
        : IReadOnlyList<SpacesHomeConversationRow>
    {
        internal SpacesBrowseAppContentSession Owner => owner;
        internal HistoryHomeAuthority Authority => authority;
        internal SpacesHomeSelection Selection => selection;
        internal long RouteEpoch => routeEpoch;
        public int Count => rows.Count;
        public SpacesHomeConversationRow this[int index] => rows[index];
        public IEnumerator<SpacesHomeConversationRow> GetEnumerator() => rows.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private void DemandHistoryServicesCurrent(HomeNativeWindowsComposition home)
    {
        if (_request.Privacy != BrowserTabPrivacy.Standard)
            throw new SpacesHistoryAuthorityRefusal("Space history requires the authenticated standard profile.");
        foreach (var required in new[] { "home.core", "home.state", "permissions.trust" })
        {
            var service = home.Runtime.Current.Services.SingleOrDefault(item => item.ServiceId == required);
            if (service is null || !service.IsAvailable || service.State != HomeServiceLifecycleState.Ready)
                throw new SpacesHistoryAuthorityRefusal("A required Home service is unavailable for Space history.");
        }
    }

    private async Task<HistoryHomeAuthority> ReadHistoryHomeAuthorityAsync(CancellationToken token)
    {
        var actor = _historyOriginalActor
            ?? throw new SpacesHistoryAuthorityRefusal("The original admitted history actor is unavailable.");
        if (!string.Equals(actor.ProfileId, _request.ProfileId, StringComparison.Ordinal))
            throw new SpacesHistoryAuthorityRefusal("Space history belongs to a different authenticated profile.");
        var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
        if (home.OriginalStartTask is { } started) await started;
        DemandHistoryServicesCurrent(home);
        // Read SAME canonical Home state and use the existing explicit actor predicate.
        // Do not catch Home/principal/store OS-access exceptions: only an actual FALSE
        // observation is an expected authority refusal. No recursive store lease is held.
        var read = await home.StateStore.ReadAsync(token);
        var state = read.State;
        if (!read.IsSuccess || state is null)
            throw new InvalidDataException("The canonical Home profile state is unavailable; preserve it for recovery.");
        if (!await home.Profiles.CheckAsync(state, actor, HomeStateCommitPhase.Publication, token))
            throw new SpacesHistoryAuthorityRefusal("The authenticated Home actor changed during history access.");
        DemandHistoryServicesCurrent(home);
        token.ThrowIfCancellationRequested(); DemandAlive();
        return new(actor, home.Runtime.Current.Revision);
    }

    private async Task PublishOriginalConversationRowsAsync(IReadOnlyList<SpacesHomeConversationRow> rows,
        Action<bool> commit, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (rows is not OriginalHistoryRows original || !ReferenceEquals(original.Owner, this))
            throw new InvalidOperationException("The SAME original history batch is required for publication.");
        HistoryHomeAuthority current;
        try { current = await ReadHistoryHomeAuthorityAsync(token); }
        catch (SpacesHistoryAuthorityRefusal) { commit(false); return; }
        Dispatcher.UIThread.VerifyAccess();
        // Comparison and the supplied original UI commit are synchronous in this continuation.
        // No dispatcher hop/await may be inserted between this decision and the cache publication.
        var allowed = current == original.Authority && IsPublicationCurrent &&
            ReferenceEquals(_spacesPage?.Controller.State.Selection, original.Selection) &&
            _routeEpoch == original.RouteEpoch;
        var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
        try { DemandHistoryServicesCurrent(home); }
        catch (SpacesHistoryAuthorityRefusal) { allowed = false; }
        commit(allowed && home.Runtime.Current.Revision == original.Authority.ServicesRevision);
    }

    /// <summary>Reads the selected Space's real recent conversations for the contextual sidebar.
    /// A store without Space membership queries truthfully contributes no rows; any other failure
    /// is reported to the caller rather than being presented as an empty history.</summary>
    private async Task<IReadOnlyList<SpacesHomeConversationRow>> ReadSpaceConversationsAsync(Guid spaceId, CancellationToken token)
    {
        var selection = _spacesPage?.Controller.State.Selection;
        var epoch = _routeEpoch;
        var authority = await ReadHistoryHomeAuthorityAsync(token).ConfigureAwait(false);
        if (!IsPublicationCurrent || selection?.Id != spaceId ||
            !ReferenceEquals(_spacesPage?.Controller.State.Selection, selection) || _routeEpoch != epoch)
            throw new SpacesHistoryAuthorityRefusal("The selected history context changed before reading.");
        IReadOnlyList<Conversation> rows;
        try
        {
            rows = await _services.GetRequiredService<IConversationRepository>()
                .GetBySpaceAsync(spaceId, 20, token).ConfigureAwait(false);
        }
        catch (NotSupportedException) { rows = []; }
        Guid? current;
        lock (_gate) current = _publishedChatRoute?.Page.CurrentConversation.Id;
        var projection = rows
            .Where(row => row.SpaceId == spaceId && !row.IsArchived && !row.IsTemporary)
            .Select(row => new SpacesHomeConversationRow(row.Id,
                string.IsNullOrWhiteSpace(row.Title) ? "Untitled conversation" : row.Title,
                row.UpdatedAt.LocalDateTime.ToString("d MMM yyyy HH:mm"),
                row.Id == current))
            .ToArray();
        return new OriginalHistoryRows(this, authority, selection!, epoch, projection);
    }

    /// <summary>Searches the SAME current Space's canonical saved titles. No recent-list filtering,
    /// cross-Space search, conversation transition or provider invocation is performed.</summary>
    private async Task<IReadOnlyList<SpacesHomeConversationRow>> FindSpaceConversationTitlesAsync(
        Guid spaceId, string query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DemandAlive();
        var selection = _spacesPage?.Controller.State.Selection;
        if (!IsPublicationCurrent || selection?.Id != spaceId)
            throw new SpacesHistoryAuthorityRefusal("The selected Space changed before chat history search.");
        var epoch = _routeEpoch;
        var authority = await ReadHistoryHomeAuthorityAsync(token).ConfigureAwait(false);
        if (!ReferenceEquals(_spacesPage?.Controller.State.Selection, selection) || _routeEpoch != epoch)
            throw new SpacesHistoryAuthorityRefusal("The selected history context changed before search.");
        var registry = _services.GetRequiredService<SpaceRegistry>();
        var originalSpace = await registry.ReadExistingAsync(spaceId, token).ConfigureAwait(false);
        if (originalSpace is null || originalSpace.IsArchived)
            throw new SpacesHistoryAuthorityRefusal("The selected Space is unavailable.");
        var repository = _services.GetRequiredService<IConversationRepository>();
        if (repository is not ISpaceConversationSearch search)
            throw new NotSupportedException("This conversation store does not support saved-title search.");
        if (await ReadHistoryHomeAuthorityAsync(token).ConfigureAwait(false) != authority ||
            !ReferenceEquals(_spacesPage?.Controller.State.Selection, selection) || _routeEpoch != epoch)
            throw new SpacesHistoryAuthorityRefusal("Home or Space authority changed before the title query.");
        var matches = await search.FindSavedTitlesBySpaceAsync(spaceId, query, 50, token).ConfigureAwait(false);
        if (await ReadHistoryHomeAuthorityAsync(token).ConfigureAwait(false) != authority)
            throw new SpacesHistoryAuthorityRefusal("Home authority changed during chat history search.");
        var currentRows = new List<Conversation>();
        foreach (var match in matches.Take(50).DistinctBy(row => row.Id))
        {
            // Membership/title may have changed during the query. Read the maintained row again
            // before exposing its metadata; opening still uses the existing typed route validation.
            var current = await repository.GetAsync(match.Id, token).ConfigureAwait(false);
            if (current is { IsArchived: false, IsTemporary: false } && current.SpaceId == spaceId &&
                current.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                currentRows.Add(current);
        }
        var finalSpace = await registry.ReadExistingAsync(spaceId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        DemandAlive();
        if (!IsPublicationCurrent || !ReferenceEquals(_spacesPage?.Controller.State.Selection, selection) ||
            finalSpace is null || finalSpace.IsArchived || finalSpace.Revision != originalSpace.Revision)
            throw new SpacesHistoryAuthorityRefusal("The selected Space changed during chat history search.");
        Guid? activeConversation;
        lock (_gate) activeConversation = _publishedChatRoute?.Page.CurrentConversation.Id;
        var projection = currentRows.Select(row => new SpacesHomeConversationRow(row.Id,
            string.IsNullOrWhiteSpace(row.Title) ? "Untitled conversation" : row.Title,
            row.UpdatedAt.LocalDateTime.ToString("d MMM yyyy HH:mm"), row.Id == activeConversation)).ToArray();
        return new OriginalHistoryRows(this, authority, selection!, epoch, projection);
    }

    /// <summary>Reads the custom sidebar elements declared by the extensions actually attached to the
    /// selected Space. It projects the selection the controller already resolved, so refreshing this
    /// surface never issues an extra Space-registry read and cannot disturb when a selection or route
    /// read settles. Attachment plus the Space's own enable/disable override is the per-Space
    /// visibility decision, so nothing is shown for a Space that has not attached the extension and
    /// disabling it does not drop the attachment. An element is only offered as actionable when the
    /// host can actually open its target.</summary>
    private async Task<IReadOnlyList<SpacesHomeSidebarRow>> ReadSpaceSidebarItemsAsync(SpacesHomeSelection selection,
        CancellationToken token)
    {
        var attached = selection.Capabilities
            .Where(capability => string.Equals(capability.Kind, nameof(SpaceCapabilityKind.Plugin), StringComparison.Ordinal)
                                 && !capability.IsDisabled
                                 && !string.IsNullOrWhiteSpace(capability.Id))
            .Select(capability => capability.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (attached.Length == 0) return [];
        var extensions = _services.GetRequiredService<ExtensionManager>();
        var canOpen = _hostQueueNavigation is not null;
        var rows = new List<SpacesHomeSidebarRow>();
        foreach (var packageId in attached)
        {
            var package = await extensions.GetPackageAsync(packageId, token).ConfigureAwait(false);
            if (package is null || !package.IsEnabled) continue;
            if (package.Manifest.SidebarContributions is not { Count: > 0 } contributions) continue;
            foreach (var contribution in contributions)
            {
                if (string.IsNullOrWhiteSpace(contribution.SidebarItemId) || string.IsNullOrWhiteSpace(contribution.TargetId))
                    continue;
                var unavailableReason = "";
                var actionable = canOpen && TryResolveSidebarTarget(contribution.TargetKind.ToString(),
                    contribution.TargetId, out _, out unavailableReason);
                rows.Add(new SpacesHomeSidebarRow(contribution.SidebarItemId, contribution.Label, contribution.IconKey,
                    contribution.TargetKind.ToString(), contribution.TargetId, actionable,
                    actionable ? "" : (canOpen ? unavailableReason : "This tab cannot open that target yet."))
                {
                    OwnerPackageId = package.Manifest.PackageId,
                    OwnerPackageVersion = package.Manifest.Version
                });
            }
        }
        return rows;
    }

    /// <summary>Republishes the contextual sidebar for the current selection. The sidebar is a
    /// projection, so a failed read never takes a live navigation down with it.</summary>
    private Task RefreshSpacesConversationsAsync(CancellationToken token)
    {
        try { return _spacesPage is { } home ? home.Controller.State.Selection?.Id == SpaceRegistry.TasksSpaceId
            ? home.RefreshTaskSidebarAsync(token) : home.RefreshConversationsAsync(token) : Task.CompletedTask; }
        catch (Exception failure)
        {
            System.Diagnostics.Trace.TraceError("Spaces sidebar refresh failed: {0}", failure);
            return Task.CompletedTask;
        }
    }

    private void ObserveSpacesSelectionChange()
    {
        _ = Task.Run(async () =>
        {
            try { await RefreshSpacesConversationsAsync(_token).ConfigureAwait(false); }
            catch (Exception failure) when (failure is not OperationCanceledException)
            { System.Diagnostics.Trace.TraceError("Spaces sidebar conversation refresh failed: {0}", failure); }
            // The extension-contributed projection reads only the selection the controller already
            // resolved, so it issues no extra Space-registry read and cannot change when a selection or
            // held route read settles. That is what makes it safe to refresh here.
            try { await (_spacesPage?.RefreshSidebarItemsAsync(_token) ?? Task.CompletedTask).ConfigureAwait(false); }
            catch (Exception failure) when (failure is not OperationCanceledException)
            { System.Diagnostics.Trace.TraceError("Spaces sidebar item refresh failed: {0}", failure); }
        });
    }

    private OverlayChatSessionFactory NewChatSessionFactory()
    {
        var factory = new OverlayChatSessionFactory(_services,
            _services.GetRequiredService<IConversationRepository>(),
            _services.GetRequiredService<CapabilityRegistryService>(),
            _services.GetRequiredService<ICatalogRepository>(),
            _services.GetRequiredService<IModeRegistry>());
        WireChatRecovery(factory);
        return factory;
    }

    /// <summary>Routes the genuine local-model recovery request through the host's typed
    /// navigation seam. It opens the canonical Home Models surface in a separate tab so the current
    /// conversation, draft, selection and focus are preserved; when this host cannot navigate, or
    /// when this session is closing or no longer publishes its view, the request stays unrouted (no
    /// fabricated navigation or state change) and surfaces a structured refusal instead of being
    /// silently traced. The navigation settlement is the recovery surface's genuinely admitted
    /// original action settlement, captured on the real loader/UI thread and handed to the external
    /// navigation driver without awaiting its included action.</summary>
    private void WireChatRecovery(OverlayChatSessionFactory factory)
    {
        factory.WireHost = page =>
        {
            page.ManageLocalModelsRequested += (_, _) =>
            {
                if (_hostQueueNavigation is null)
                    throw new SpacesWorkspaceActionRefusal("No host navigation seam is available; local-model recovery cannot route.");
                // Retained history pages keep their drafts and close ownership, but only the exact
                // currently published Chat page may request host navigation. Session-wide readiness
                // alone would let an old page route recovery after a fresh chat or editor is shown.
                lock (_gate)
                    if (!IsPublicationCurrent || !ReferenceEquals(_publishedChatRoute?.Page, page))
                        throw new SpacesWorkspaceActionRefusal("The Spaces session no longer publishes this Chat page; local-model recovery is unrouted.");

                var address = AppContentAddress.Parse("browse://home/models");
                Task settlement;
                try
                {
                    settlement = page.RecoveryActionSettlement;
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    throw new SpacesWorkspaceActionRefusal("The recovery action settlement could not be captured: " + failure.Message);
                }

                var admitted = _hostQueueNavigation(
                    new BrowseNavigationIntent(_request.TabId, address, OpenInNewTab: true), settlement);
                if (!admitted)
                    throw new SpacesWorkspaceActionRefusal("The host navigation queue refused the local-model recovery route.");
            };
#if !ANDROID
            WireChatAssistants(page);
#endif
        };
    }

#if !ANDROID
    private void WireChatAssistants(NewChatPage page)
    {
        EventHandler catalogue = (_, _) => QueueAssistantMenuCommand(page,
            (token, epoch) => ReadAssistantCatalogueForChatAsync(page, epoch, token));
        EventHandler<AddMenuSelection> selection = (_, selected) =>
        {
            if (!TryGetAssistantMenuSelection(selected, out var identity)) return;
            QueueAssistantMenuCommand(page, (token, epoch) => OpenAssistantOnUiAsync(page, identity, epoch, token));
        };
        page.AssistantCatalogueRequested += catalogue;
        page.AddCatalogItemSelected += selection;
        _chatAssistantHandlers.Add(page, (catalogue, selection));
    }

    private void QueueAssistantMenuCommand(NewChatPage page, Func<CancellationToken, long, Task> command)
    {
        lock (_gate)
        {
            DemandAlive();
            if (!IsPublicationCurrent || !ReferenceEquals(_publishedChatRoute?.Page, page))
                throw new SpacesWorkspaceActionRefusal("The Spaces session no longer publishes this Chat page; Assistants is unrouted.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var execution = new CommandExecution(this);
            var actual = ExecuteAssistantMenuCommandAsync(start.Task, execution, page, command, _routeEpoch);
            execution.Original = actual;
            _commands.RemoveAll(task => task.IsCompletedSuccessfully);
            _commands.Add(actual);
            start.SetResult();
        }
    }

    private async Task ExecuteAssistantMenuCommandAsync(Task start, CommandExecution execution, NewChatPage page,
        Func<CancellationToken, long, Task> command, long routeEpoch)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_token, _appLifetime, _windowLifetime);
        await _commandSerial.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await OnUiAsync(async () =>
            {
                var previous = ExecutingCommand.Value;
                ExecutingCommand.Value = execution;
                Volatile.Write(ref execution.Active, true);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    DemandAlive();
                    if (!ReferenceEquals(_publishedChatRoute?.Page, page) || _routeEpoch != routeEpoch)
                        throw new OperationCanceledException("The source Chat route changed before the Assistant action ran.", linked.Token);
                    try { await command(linked.Token, routeEpoch); }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                    catch (Exception failure)
                    {
                        ReportWorkspaceActionRefusal("Could not open configured Assistants: " + failure.Message);
                        throw;
                    }
                }
                finally { ExecutingCommand.Value = previous; }
            }).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref execution.Active, false); _commandSerial.Release(); }
    }

    private bool CanPublishAssistantCatalogue(NewChatPage page, long routeEpoch, long generation, Guid conversationId) =>
        IsPublicationCurrent && ReferenceEquals(_publishedChatRoute?.Page, page) && _routeEpoch == routeEpoch &&
        page.ConversationId == conversationId && page.IsAssistantCatalogueOpen &&
        page.AssistantCatalogueRequestGeneration == generation;

    private async Task ReadAssistantCatalogueForChatAsync(NewChatPage page, long routeEpoch, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var actualApp = _actualApp;
        if (actualApp is null || _actualWindow is null) return;
        var generation = page.AssistantCatalogueRequestGeneration;
        var conversationId = page.ConversationId;
        if (!CanPublishAssistantCatalogue(page, routeEpoch, generation, conversationId)) return;
        var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
        try
        {
            var expectedActor = _historyOriginalActor
                ?? throw new SpacesHistoryAuthorityRefusal("The original Home profile actor is unavailable.");
            if (await home.Profiles.GetCurrentAsync(token) != expectedActor)
                throw new SpacesHistoryAuthorityRefusal("The authenticated Home profile changed before Assistants could be read.");
            var acquisition = await actualApp.CreateOriginalAssistantsControllerAsync(_services, home, token);
            token.ThrowIfCancellationRequested();
            if (!CanPublishAssistantCatalogue(page, routeEpoch, generation, conversationId)) return;
            if (acquisition is null)
            {
                page.SetAssistantCatalogue(null, "Assistants is not ready in this Home profile. Retry after setup is complete.");
                return;
            }
            var snapshot = await acquisition.OriginalController.InitializeAsync(token);
            token.ThrowIfCancellationRequested();
            var currentActor = await home.Profiles.GetCurrentAsync(token);
            if (currentActor != expectedActor)
                throw new SpacesHistoryAuthorityRefusal("The authenticated Home profile changed while Assistants were loading.");
            if (CanPublishAssistantCatalogue(page, routeEpoch, generation, conversationId))
                page.SetAssistantCatalogue(snapshot.Assistants, snapshot.Assistants is null
                    ? "Assistants is not ready in this Home profile."
                    : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception failure)
        {
            if (CanPublishAssistantCatalogue(page, routeEpoch, generation, conversationId))
                page.SetAssistantCatalogue(null, "Could not load the configured Assistants: " + failure.Message);
        }
    }

    private async Task OpenAssistantOnUiAsync(NewChatPage? chat, AssistantIdentity? identity,
        long routeEpoch, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var priorChat = chat ?? _chatPage;
        NativeAssistantsDesktopPage? page = _assistantPage;
        var pageWasAlreadyPublished = page is not null && ReferenceEquals(_publishedAssistantRoute?.Page, page);
        var publishedThisOperation = false;
        try
        {
            var actualApp = _actualApp;
            var actualWindow = _actualWindow;
            if (actualApp is null || actualWindow is null)
            {
                chat?.SetAssistantCatalogue(null, "Assistants is unavailable in this host.");
                ReportWorkspaceActionRefusal("Assistants is unavailable in this host.");
                if (chat is null) RestoreSpacesAfterAssistantRefusal();
                return;
            }
            if (chat is not null && (!IsPublicationCurrent || !ReferenceEquals(_publishedChatRoute?.Page, chat) || _routeEpoch != routeEpoch))
                throw new OperationCanceledException("The originating Chat route changed before Assistants could open.", token);
            var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
            var expectedActor = _historyOriginalActor
                ?? throw new SpacesHistoryAuthorityRefusal("The original Home profile actor is unavailable.");
            if (await home.Profiles.GetCurrentAsync(token) != expectedActor)
                throw new SpacesHistoryAuthorityRefusal("The authenticated Home profile changed before Assistants could open.");
            var acquisition = await actualApp.CreateOriginalAssistantsControllerAsync(_services, home, token);
            token.ThrowIfCancellationRequested();
            if (acquisition is null)
            {
                chat?.SetAssistantCatalogue(null, "Assistants is not ready in this Home profile. Complete setup and retry.");
                ReportWorkspaceActionRefusal("Assistants is not ready in this Home profile. Complete setup and retry.");
                if (chat is null) RestoreSpacesAfterAssistantRefusal();
                return;
            }
            var newPage = page is null || page.OriginalClose is not null;
            if (newPage)
            {
                NativeAssistantsDesktopPage? captured = null;
                void Capture(NativeAssistantsDesktopPage actual)
                {
                    if (captured is not null && !ReferenceEquals(captured, actual))
                        throw new InvalidOperationException("The actual Assistant page factory supplied multiple pages.");
                    captured = actual;
                    page = actual;
                    _assistantPage = actual;
                    actualApp.RetainOriginalAssistantsPresentation(acquisition.OriginalController, actual);
                }
                page = NativeAssistantsDesktopPage.BindOriginal(_services, home, acquisition.OriginalDenHost,
                    acquisition.OriginalOpenedDen, acquisition.OriginalController, _appLifetime, _windowLifetime,
                    actualWindow, Capture, migration: acquisition.OriginalMigration,
                    migrationUnavailableReason: acquisition.OriginalMigration is null ? "The original saved Agent migration source is not configured." : null,
                    memoryManagement: acquisition.OriginalMemoryManagement,
                    memoryUnavailableReason: acquisition.OriginalMemoryManagement is null ? "The original local Assistant memory source is not configured." : null,
                    miniComputerManagement: acquisition.OriginalMiniComputerManagement,
                    miniComputerUnavailableReason: acquisition.OriginalMiniComputerManagement is null ? "The original protected Mini Computer catalogue and operation source is not configured." : null,
                    generatedUiHost: acquisition.OriginalGeneratedUiHost);
                if (!ReferenceEquals(page, captured)) throw new InvalidOperationException("The actual Assistant page was not captured before native publication.");
                _assistantPage = page;
                await page.InitializeAsync(token);
            }
            else if (!ReferenceEquals(page!.OriginalController, acquisition.OriginalController) ||
                     !page.IsOriginalComposition(_services, home) || !page.IsOriginalWindow(actualWindow, _appLifetime, _windowLifetime))
            {
                throw new UnauthorizedAccessException("Retain the SAME actual Assistants page, acquisition, Home and native Window.");
            }
            if (identity is { } selected)
                await acquisition.OriginalController.OpenAssistantAsync(selected, token);
            token.ThrowIfCancellationRequested();
            if (await home.Profiles.GetCurrentAsync(token) != expectedActor)
                throw new SpacesHistoryAuthorityRefusal("The authenticated Home profile changed before the Assistant page could publish.");
            if (chat is not null && (!IsPublicationCurrent || !ReferenceEquals(_publishedChatRoute?.Page, chat) || _routeEpoch != routeEpoch))
                throw new OperationCanceledException("The originating Chat route changed before the Assistant page could publish.", token);
            var address = CreateAssistantRouteAddress(identity);
            var actualPage = page ?? throw new InvalidOperationException("The actual Assistant page was not acquired.");
            actualPage.PublishOriginalTab(() =>
            {
                if (_spacesPage is null || !_spacesPage.TryMountSpaceWorkspace(actualPage))
                    throw new InvalidOperationException("The retained Spaces workspace region is unavailable for Assistants.");
                SetRootContent(_spacesPage);
                lock (_gate)
                {
                    _publishedAssistantRoute = new(actualPage, address, chat ?? _chatPage, identity);
                    _publishedChatRoute = null;
                    _chatPage = null;
                    _address = address;
                    _routeEpoch++;
                }
            });
            publishedThisOperation = true;
            PublishStateChanged();
        }
        catch (Exception openingFailure)
        {
            // A newly captured native page has acquired real App ownership, even if initialization,
            // cancellation, actor revalidation, selection, or publication later fails. Retire that
            // exact partial page before permitting a later menu action to construct a replacement.
            // A page that was already successfully published remains mounted and owned if changing
            // its selected Assistant fails.
            var partial = page ?? _assistantPage;
            if (partial is not null && !pageWasAlreadyPublished && !publishedThisOperation &&
                ReferenceEquals(_assistantPage, partial))
            {
                try { await RetireFailedAssistantOpenOnUiAsync(partial, priorChat); }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Opening the Assistant failed and its partial native page did not retire cleanly.",
                        openingFailure, cleanupFailure);
                }
            }
            throw;
        }
    }

    private async Task RetireFailedAssistantOpenOnUiAsync(NativeAssistantsDesktopPage page, NewChatPage? priorChat)
    {
        Dispatcher.UIThread.VerifyAccess();
        await page.CloseAndDrainAsync();
        if (ReferenceEquals(_spacesPage?.SpaceWorkspaceContent, page))
            _spacesPage.ClearSpaceWorkspace(page);
        if (ReferenceEquals(_assistantPage, page)) _assistantPage = null;
        lock (_gate)
        {
            if (_publishedAssistantRoute is { } route && ReferenceEquals(route.Page, page))
                _publishedAssistantRoute = null;
            _publishedChatRoute = null;
            _chatPage = null;
        }
        if (!_closing && priorChat is not null && _ownedChats.Contains(priorChat) &&
            _spacesPage is { } home && home.TryMountSpaceWorkspace(priorChat))
        {
            SetRootContent(home);
            lock (_gate) PublishChatPage(priorChat);
            return;
        }
        if (_spacesPage is { } spaces)
        {
            SetRootContent(spaces);
            lock (_gate) UpdateAddress(spaces.Controller.State.Selection?.Id);
        }
    }

    private void RestoreSpacesAfterAssistantRefusal()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_spacesPage is null) return;
        SetRootContent(_spacesPage);
        lock (_gate)
        {
            _publishedAssistantRoute = null;
            UpdateAddress(_spacesPage.Controller.State.Selection?.Id);
        }
    }

    private async Task<bool> TryRestoreRequestedAssistantAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!TryReadAssistantRoute(_request.Address, out var identity)) return false;
        await OpenAssistantOnUiAsync(null, identity, _routeEpoch, token);
        return true;
    }

    internal static bool TryGetAssistantMenuSelection(AddMenuSelection selection, out AssistantIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(selection);
        identity = null;
        if (selection.Kind != AddMenu.AddMenuAction.Assistant) return false;
        if (selection.Item is AssistantDefinitionSnapshot assistant && assistant.Kind == ConfiguredIdentityKind.Assistant)
        {
            identity = assistant.Identity;
            return true;
        }
        return selection.Item is ChatAddMenuSurface.OpenAssistantsRequest;
    }

    internal static AppContentAddress CreateAssistantRouteAddress(AssistantIdentity? identity) => identity is { } assistant
        ? AppContentAddress.Parse($"9to1://spaces/assistant/{EncodeAssistantRoutePart(assistant.DenId)}/{EncodeAssistantRoutePart(assistant.NamespaceId)}/{EncodeAssistantRoutePart(assistant.DefinitionId)}")
        : AppContentAddress.Parse("9to1://spaces/assistant");

    internal static bool TryReadAssistantRoute(AppContentAddress address, out AssistantIdentity? identity)
    {
        identity = null;
        var segments = address.ResourceSegments;
        if (segments.Count == 1 && string.Equals(segments[0], "assistant", StringComparison.Ordinal)) return true;
        if (segments.Count == 0 || !string.Equals(segments[0], "assistant", StringComparison.Ordinal)) return false;
        if (segments.Count != 4)
            throw new FormatException("The saved Assistant route identity is incomplete.");
        var den = DecodeAssistantRoutePart(segments[1]);
        var ns = DecodeAssistantRoutePart(segments[2]);
        var definition = DecodeAssistantRoutePart(segments[3]);
        if (string.IsNullOrWhiteSpace(den) || string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(definition))
            throw new FormatException("The saved Assistant route identity is incomplete.");
        identity = new AssistantIdentity(den, ns, definition);
        return true;
    }

    private static string EncodeAssistantRoutePart(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DecodeAssistantRoutePart(string value)
    {
        if (value.Length == 0 || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new FormatException("The saved Assistant route identity is invalid.");
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
        catch (FormatException error) { throw new FormatException("The saved Assistant route identity is invalid.", error); }
    }

    private void UnwireChatAssistants(NewChatPage page)
    {
        if (!_chatAssistantHandlers.Remove(page, out var handlers)) return;
        page.AssistantCatalogueRequested -= handlers.Catalogue;
        page.AddCatalogItemSelected -= handlers.Selection;
    }

    private async Task RetireAssistantPresentationOnUiAsync(bool restorePriorChat = true)
    {
        Dispatcher.UIThread.VerifyAccess();
        var page = _assistantPage;
        if (page is null) return;
        var priorChat = _publishedAssistantRoute is { } route && ReferenceEquals(route.Page, page)
            ? route.PriorChat : null;
        _spacesPage?.ClearSpaceWorkspace(page);
        lock (_gate)
        {
            if (_publishedAssistantRoute is { } current && ReferenceEquals(current.Page, page))
                _publishedAssistantRoute = null;
        }
        await page.CloseAndDrainAsync();
        if (ReferenceEquals(_assistantPage, page)) _assistantPage = null;
        if (restorePriorChat && priorChat is not null && _ownedChats.Contains(priorChat))
        {
            lock (_gate)
            {
                if (!_closing && _spacesPage is { } home && home.TryMountSpaceWorkspace(priorChat))
                {
                    SetRootContent(home);
                    PublishChatPage(priorChat);
                    return;
                }
            }
        }
        lock (_gate)
        {
            _publishedChatRoute = null;
            _chatPage = null;
            UpdateAddress(_spacesPage?.Controller.State.Selection?.Id);
        }
    }
#endif

    /// <summary>Starts a new conversation inside the selected Space from the contextual sidebar.
    /// It never creates another Space and never reuses an existing conversation.</summary>
    private async Task NewConversationOnUiAsync(Guid spaceId, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        DemandAlive();
        var space = await _services.GetRequiredService<SpaceRegistry>().ReadExistingAsync(spaceId, token)
            ?? throw new SpacesWorkspaceActionRefusal("The selected Space is unavailable.");
        if (space.IsArchived) throw new SpacesWorkspaceActionRefusal("The selected Space is archived.");
        var page = await NewChatSessionFactory().CreateNewAsync(token);
        _ownedChats.Add(page);
        var published = false;
        try
        {
            token.ThrowIfCancellationRequested();
            DemandAlive();
            if (space.Kind == SpaceKind.Tasks) page.ConfigureTaskMode();
            await page.AssignSpaceAsync(spaceId);
#if !ANDROID
            await RetireAssistantPresentationOnUiAsync();
#endif
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                DemandAlive();
                page.SetDraft(string.Empty);
                PublishChatPage(page);
                _chatsBySpace[spaceId] = page;
                ObserveConversationRoute(page);
                published = true;
            }
            PublishStateChanged();
            await RefreshSpacesConversationsAsync(token);
        }
        finally
        {
            if (!published)
            {
#if !ANDROID
                UnwireChatAssistants(page);
#endif
                await page.DisposeAsync(); _ownedChats.Remove(page);
            }
        }
    }

    /// <summary>Opens an existing conversation of the selected Space from the contextual sidebar.
    /// The real stored identity is restored; nothing is replaced or recreated on failure.</summary>
    private sealed record HistoryOpenTicket(SpacesHomePage Owner,
        SpacesHomePage.ConversationOpenIntent? Issued, long RouteEpoch);

    private void DemandCurrentHistoryOpen(HistoryOpenTicket ticket)
    {
        if (ticket.Issued is not { } issued || !IsPublicationCurrent ||
            !ReferenceEquals(_spacesPage, ticket.Owner) || _routeEpoch != ticket.RouteEpoch ||
            !ticket.Owner.IsCurrentConversationOpenIntent(issued))
            throw new SpacesWorkspaceActionRefusal("The selected conversation is no longer available in this history view.");
    }

    private async Task OpenConversationOnUiAsync(HistoryOpenTicket ticket, CancellationToken token)
    {
        try { await OpenOriginalHistoryConversationOnUiAsync(ticket, token); }
        catch (SpacesHistoryAuthorityRefusal denial)
        {
            _spacesPage?.RefuseConversationHistoryAccess();
            throw new SpacesWorkspaceActionRefusal(denial.Message);
        }
    }

    private async Task OpenOriginalHistoryConversationOnUiAsync(HistoryOpenTicket ticket, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested(); DemandAlive(); DemandCurrentHistoryOpen(ticket);
        var originalAuthority = await ReadHistoryHomeAuthorityAsync(token);
        DemandCurrentHistoryOpen(ticket);
        var conversationId = ticket.Issued!.ConversationId;
        var spaceId = ticket.Issued.SpaceId;
        var conversations = _services.GetRequiredService<IConversationRepository>();
        var row = await conversations.GetAsync(conversationId, token)
            ?? throw new SpacesWorkspaceActionRefusal("The selected conversation is no longer available.");
        DemandCurrentHistoryOpen(ticket);
        if (await ReadHistoryHomeAuthorityAsync(token) != originalAuthority)
            throw new SpacesHistoryAuthorityRefusal("Home authority changed before opening that conversation.");
        if (row.IsArchived || row.IsTemporary || row.SpaceId != spaceId)
            throw new SpacesWorkspaceActionRefusal("That conversation is not available in this Space.");
        var registry = _services.GetRequiredService<SpaceRegistry>();
        var originalSpace = await registry.ReadExistingAsync(spaceId, token);
        DemandCurrentHistoryOpen(ticket);
        if (originalSpace is null || originalSpace.IsArchived)
            throw new SpacesWorkspaceActionRefusal("The selected Space is unavailable.");
        if (await ReadHistoryHomeAuthorityAsync(token) != originalAuthority)
            throw new SpacesHistoryAuthorityRefusal("Home authority changed before restoring that conversation.");
        DemandCurrentHistoryOpen(ticket);
        var chat = await NewChatSessionFactory().RestoreAsync(conversationId, token)
            ?? throw new SpacesWorkspaceActionRefusal("The selected conversation is no longer available.");
        _ownedChats.Add(chat);
        var published = false;
        try
        {
            token.ThrowIfCancellationRequested(); DemandAlive(); DemandCurrentHistoryOpen(ticket);
            var finalRow = await conversations.GetAsync(conversationId, token);
            var finalSpace = await registry.ReadExistingAsync(spaceId, token);
            var finalAuthority = await ReadHistoryHomeAuthorityAsync(token);
            // This continuation is on the UI dispatcher. The full actor/services comparison,
            // issued row/selection/projection/route checks and mount have no intervening await.
            Dispatcher.UIThread.VerifyAccess();
            if (finalAuthority != originalAuthority)
                throw new SpacesHistoryAuthorityRefusal("Home authority changed before opening that conversation.");
            if (finalRow is null || finalRow.IsArchived || finalRow.IsTemporary ||
                finalRow.SpaceId != spaceId || finalSpace is null || finalSpace.IsArchived ||
                finalSpace.Revision != originalSpace.Revision || chat.CurrentConversation.Id != conversationId ||
                chat.CurrentConversation.SpaceId != spaceId)
                throw new SpacesWorkspaceActionRefusal("The selected conversation or its Home authority changed before opening.");
#if !ANDROID
            await RetireAssistantPresentationOnUiAsync();
#endif
            lock (_gate)
            {
                token.ThrowIfCancellationRequested(); DemandAlive(); DemandCurrentHistoryOpen(ticket);
                var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
                DemandHistoryServicesCurrent(home);
                if (home.Runtime.Current.Revision != originalAuthority.ServicesRevision)
                    throw new SpacesHistoryAuthorityRefusal("Home services changed before opening that conversation.");
                PublishChatPage(chat);
                _chatsBySpace[spaceId] = chat;
                ObserveConversationRoute(chat);
                published = true;
            }
            PublishStateChanged();
            await RefreshSpacesConversationsAsync(token);
        }
        finally
        {
            if (!published)
            {
#if !ANDROID
                UnwireChatAssistants(chat);
#endif
                await chat.DisposeAsync(); _ownedChats.Remove(chat);
            }
        }
    }

    /// <summary>Runs one contextual-sidebar workspace command under the same serial and UI owner as
    /// the queued Space navigation, so a sidebar click cannot race an in-flight route change.</summary>
    private Task ExecuteWorkspaceCommandAsync(string command, object? parameter, CancellationToken caller,
        Task? originatingSettlement = null)
    {
        lock (_gate)
        {
            DemandAlive();
            caller.ThrowIfCancellationRequested();
            if (_activeDetailsPreparation is not null)
                throw new InvalidOperationException("Abort the exact Spaces close preparation before navigating.");
            if (command == "spaces.open-sidebar-item")
            {
                if (parameter is not string { Length: > 0 } sidebarItemId)
                    throw new InvalidOperationException("The Spaces sidebar command has no item identity.");
                return AdmitWorkspaceDriver((start, execution) => ExecuteSidebarItemDriverAsync(start, execution, sidebarItemId, caller, originatingSettlement));
            }
            if (command == "spaces.tasks.open")
            {
                var owner = _spacesPage ?? throw new InvalidOperationException("The actual Tasks sidebar owner is unavailable.");
                if (parameter is not SpacesHomePage.TaskOpenIntent intent || !owner.IsCurrentTaskOpenIntent(intent))
                    throw new SpacesWorkspaceActionRefusal("The task tile changed before navigation.");
                var ticket = new TaskOpenTicket(owner, intent, _routeEpoch);
                return AdmitWorkspaceDriver((start, execution) => ExecuteWorkspaceCommandDriverAsync(start, execution, command, ticket, caller));
            }
            if (command == "spaces.open-conversation")
            {
                var owner = _spacesPage ?? throw new InvalidOperationException("The original Spaces history owner is unavailable.");
                var ticket = new HistoryOpenTicket(owner, parameter as SpacesHomePage.ConversationOpenIntent, _routeEpoch);
                return AdmitWorkspaceDriver((start, execution) => ExecuteWorkspaceCommandDriverAsync(start, execution, command, ticket, caller));
            }
            if (parameter is not Guid target || target == Guid.Empty)
                throw new InvalidOperationException("The Spaces workspace command has no valid target identity.");
            return AdmitWorkspaceDriver((start, execution) => ExecuteWorkspaceCommandDriverAsync(start, execution, command, target, caller));
        }
    }

    /// <summary>Admits one workspace action as original work with the same lease, retirement and
    /// failure bookkeeping regardless of which workspace command it is.</summary>
    private Task AdmitWorkspaceDriver(Func<Task, CommandExecution, Task> build)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new CommandExecution(this);
        var actual = build(start.Task, execution);
        execution.Original = actual;
        _commands.RemoveAll(task => task.IsCompletedSuccessfully);
        _commands.Add(actual);
        _lastQueuedNavigation = actual;
        start.SetResult();
        ObserveQueuedSpaceAction(actual);
        return actual;
    }

    private async Task ExecuteSidebarItemDriverAsync(Task start, CommandExecution execution, string sidebarItemId, CancellationToken caller,
        Task? originatingSettlement = null)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        await _commandSerial.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await OnUiAsync(async () =>
            {
                var previous = ExecutingCommand.Value;
                ExecutingCommand.Value = execution;
                Volatile.Write(ref execution.Active, true);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    DemandAlive();
                    try { await OpenSidebarItemOnUiAsync(sidebarItemId, linked.Token, originatingSettlement); }
                    catch (SpacesWorkspaceActionRefusal refusal) { ReportWorkspaceActionRefusal(refusal.Message); }
                }
                finally
                {
                    ExecutingCommand.Value = previous;
                    Volatile.Write(ref execution.Active, false);
                }
            }).ConfigureAwait(false);
        }
        finally { _commandSerial.Release(); }
    }

    private async Task<AuthenticatedResourceActor> ReadSidebarHomeActorAsync(CancellationToken token)
    {
        try
        {
            await DemandDetailsHomeAdmissionAsync(token);
            return await _services.GetRequiredService<HomeNativeWindowsComposition>().Profiles.GetCurrentAsync(token)
                ?? throw new UnauthorizedAccessException("The authenticated Home profile is unavailable.");
        }
        catch (UnauthorizedAccessException denial)
        {
            // Admission has made no effect. Keep expected denials out of retained close faults;
            // I/O, corrupt-state and other genuine failures retain their original custody.
            throw new SpacesWorkspaceActionRefusal(denial.Message);
        }
    }

    /// <summary>Opens the target an extension-contributed sidebar element declares. It reuses the
    /// host's existing typed navigation intent, so tab ownership, origin checks and retirement stay
    /// host-owned and the current conversation and its draft are not disturbed by an unresolvable
    /// target: an element whose target cannot be resolved refuses before anything changes.</summary>
    private async Task OpenSidebarItemOnUiAsync(string sidebarItemId, CancellationToken token,
        Task? originatingSettlement = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        DemandAlive();
        var initialActor = await ReadSidebarHomeActorAsync(token);
        if (_spacesPage?.Controller.State.Selection is not { } selection)
            throw new SpacesWorkspaceActionRefusal("Select a Space before opening one of its sidebar items.");
        var initialSelection = selection;
        var initialSpace = await _services.GetRequiredService<SpaceRegistry>().ReadExistingAsync(selection.Id, token);
        if (initialSpace is null || initialSpace.IsArchived)
            throw new SpacesWorkspaceActionRefusal("The selected Space is unavailable.");
        long initialRouteEpoch;
        lock (_gate) initialRouteEpoch = _routeEpoch;
        var rows = await ReadSpaceSidebarItemsAsync(selection, token).ConfigureAwait(true);
        var matchingRows = rows.Where(item => string.Equals(item.SidebarItemId, sidebarItemId, StringComparison.Ordinal)).ToArray();
        if (matchingRows.Length == 0)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer attached to this Space.");
        if (matchingRows.Length > 1)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is ambiguous.");
        var row = matchingRows[0];
        if (_hostQueueNavigation is null)
            throw new SpacesWorkspaceActionRefusal("This tab cannot open that sidebar target.");

        // Revalidate against the live controller projection and the authoritative Space/package
        // state. A delayed extension read must not admit a stale selection or attachment.
        token.ThrowIfCancellationRequested();
        DemandAlive();
        if (_spacesPage?.Controller.State.Selection is not { } currentSelection ||
            !ReferenceEquals(currentSelection, initialSelection) ||
            currentSelection.Id != selection.Id)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer attached to this Space.");
        var actualSpace = await _services.GetRequiredService<SpaceRegistry>().ReadExistingAsync(selection.Id, token);
        if (actualSpace is null || actualSpace.IsArchived)
            throw new SpacesWorkspaceActionRefusal("The selected Space is unavailable.");
        if (actualSpace.Revision != initialSpace.Revision)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer attached to this Space.");
        if (!SpaceCapabilityBindingsEqual(actualSpace.CapabilityBindings, initialSpace.CapabilityBindings))
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer attached to this Space.");
        var attachedPackageIds = (actualSpace.CapabilityBindings ?? Array.Empty<SpaceCapabilityBinding>())
            .Where(capability => capability.Kind == SpaceCapabilityKind.Plugin
                                 && capability.Override != SpaceCapabilityOverride.Disabled
                                 && !string.IsNullOrWhiteSpace(capability.CapabilityId))
            .Select(capability => capability.CapabilityId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var extensions = _services.GetRequiredService<ExtensionManager>();
        var stillAttached = false;
        var attachedCount = 0;
        foreach (var packageId in attachedPackageIds)
        {
            var package = await extensions.GetPackageAsync(packageId, token);
            if (package is null || !package.IsEnabled) continue;
            if (package.Manifest.SidebarContributions is { Count: > 0 } contributions)
            {
                foreach (var contribution in contributions)
                {
                    if (!string.Equals(contribution.SidebarItemId, sidebarItemId, StringComparison.Ordinal)) continue;
                    if (contribution.TargetKind.ToString() != row.TargetKind) continue;
                    if (!string.Equals(contribution.TargetId, row.TargetId, StringComparison.Ordinal)) continue;
                    if (row.OwnerPackageId is not null &&
                        !string.Equals(package.Manifest.PackageId, row.OwnerPackageId, StringComparison.Ordinal))
                        continue;
                    if (row.OwnerPackageVersion is not null &&
                        !string.Equals(package.Manifest.Version, row.OwnerPackageVersion, StringComparison.Ordinal))
                        continue;
                    stillAttached = true;
                    attachedCount++;
                }
            }
        }
        if (attachedCount > 1)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is ambiguous.");
        if (!stillAttached)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer attached to this Space.");

        // Final checks after ALL delayed reads.
        token.ThrowIfCancellationRequested();
        DemandAlive();
        if (_spacesPage?.Controller.State.Selection is not { } postSelection ||
            !ReferenceEquals(postSelection, initialSelection) ||
            postSelection.Id != initialSelection.Id)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer attached to this Space.");
        var postSpace = await _services.GetRequiredService<SpaceRegistry>().ReadExistingAsync(selection.Id, token);
        if (postSpace is null || postSpace.IsArchived)
            throw new SpacesWorkspaceActionRefusal("The selected Space is unavailable.");
        if (postSpace.Revision != initialSpace.Revision)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer attached to this Space.");
        if (!SpaceCapabilityBindingsEqual(postSpace.CapabilityBindings, initialSpace.CapabilityBindings))
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer attached to this Space.");
        var currentActor = await ReadSidebarHomeActorAsync(token);
        if (initialActor != currentActor)
            throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer available for this tab.");
        token.ThrowIfCancellationRequested();
        DemandAlive();
        lock (_gate)
        {
            if (_closing || !_published || _routeEpoch != initialRouteEpoch ||
                _activeDetailsPreparation is not null ||
                !ReferenceEquals(_spacesPage?.Controller.State.Selection, initialSelection))
                throw new SpacesWorkspaceActionRefusal("That sidebar item is no longer available for this tab.");
        }
        string unavailableReason = "";
        if (!TryResolveSidebarTarget(row.TargetKind, row.TargetId, out var address, out unavailableReason))
            throw new SpacesWorkspaceActionRefusal(string.IsNullOrWhiteSpace(unavailableReason)
                ? row.UnavailableReason : unavailableReason);

        // The host owns downstream navigation. The genuine originating settlement is
        // captured by the Spaces page action and handed through the typed callback.
        // Never synthesize a completed settlement here.
        if (originatingSettlement is null)
            throw new SpacesWorkspaceActionRefusal("This tab cannot open that sidebar target.");
        var execution = ExecutingCommand.Value;
        if (execution is null || !ReferenceEquals(execution.Owner, this) ||
            !Volatile.Read(ref execution.Active) || execution.Original is null)
            throw new SpacesWorkspaceActionRefusal("This tab cannot open that sidebar target.");
        Task settlement = Task.WhenAll(originatingSettlement, execution.Original);

        if (!_hostQueueNavigation(new BrowseNavigationIntent(_request.TabId, address, OpenInNewTab: false), settlement))
            throw new SpacesWorkspaceActionRefusal("The tab could not accept that sidebar destination.");
    }

    private async Task ExecuteWorkspaceCommandDriverAsync(Task start, CommandExecution execution, string command, object target,
        CancellationToken caller)
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
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    DemandAlive();
                    try
                    {
                        if (command == "spaces.new-chat") await NewConversationOnUiAsync((Guid)target, linked.Token);
                        else if (command == "spaces.tasks.open") await OpenSidebarTaskOnUiAsync((TaskOpenTicket)target, linked.Token);
                        else if (command == "spaces.open-conversation") await OpenConversationOnUiAsync((HistoryOpenTicket)target, linked.Token);
                        else throw new InvalidOperationException($"The Spaces workspace command '{command}' is not owned by this host.");
                    }
                    catch (SpacesWorkspaceActionRefusal refusal)
                    {
                        // A refusal before any effect is reported in the surface that raised it. It is
                        // not retained as a close obligation, so a failed click cannot make the tab
                        // permanently unclosable.
                        ReportWorkspaceActionRefusal(refusal.Message);
                    }
                }
                finally
                {
                    ExecutingCommand.Value = previous;
                    Volatile.Write(ref execution.Active, false);
                }
            }).ConfigureAwait(false);
        }
        finally { _commandSerial.Release(); }
    }

    private void ReportWorkspaceActionRefusal(string message)
    {
        Dispatcher.UIThread.VerifyAccess();
        _spacesPage?.Controller.ReportWorkspaceActionRefusal(message);
    }

    private static bool SpaceCapabilityBindingsEqual(IReadOnlyList<SpaceCapabilityBinding>? left,
        IReadOnlyList<SpaceCapabilityBinding>? right)
    {
        var a = left ?? Array.Empty<SpaceCapabilityBinding>();
        var b = right ?? Array.Empty<SpaceCapabilityBinding>();
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].CapabilityId, b[i].CapabilityId, StringComparison.Ordinal)) return false;
            if (a[i].Kind != b[i].Kind) return false;
            if (a[i].Override != b[i].Override) return false;
            if (!string.Equals(a[i].VersionPin, b[i].VersionPin, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool TryResolveSidebarTarget(string targetKind, string targetId, out AppContentAddress address,
        out string unavailableReason)
    {
        address = default!;
        unavailableReason = "";
        if (!Enum.TryParse<ExtensionSidebarTargetKind>(targetKind, out var kind) ||
            kind is not (ExtensionSidebarTargetKind.Page or ExtensionSidebarTargetKind.AppSurface))
        {
            unavailableReason = "That sidebar target kind is not supported yet.";
            return false;
        }
        AppContentAddress parsed;
        try { parsed = AppContentAddress.Parse(targetId); }
        catch (Exception failure) when (failure is FormatException or UriFormatException or ArgumentException)
        {
            unavailableReason = $"That sidebar item's target is not an addressable destination: {targetKind}";
            return false;
        }
        if (kind == ExtensionSidebarTargetKind.Page && !IsWebAddress(parsed))
        {
            unavailableReason = "That Page sidebar target must use a permitted web address.";
            return false;
        }
        if (kind == ExtensionSidebarTargetKind.AppSurface && parsed.Namespace != AppContentNamespace.NativeApp)
        {
            unavailableReason = "That AppSurface sidebar target must use a registered app address.";
            return false;
        }
        address = parsed;
        return true;
    }

    private static bool IsWebAddress(AppContentAddress address) => address.IsWeb;

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
