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
        CancellationToken hostLifetime, Func<BrowseNavigationIntent, Task, bool>? queueNavigation = null)
    {
        _request = request;
        _services = services;
        _hostQueueNavigation = queueNavigation;
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
                queueNavigation: QueueSpaceAction,
                conversationsProvider: ReadSpaceConversationsAsync,
                sidebarItemsProvider: ReadSpaceSidebarItemsAsync,
                workspaceCommandHost: ExecuteWorkspaceCommandAsync);
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
            if (_publishedDetailsRoute is { } details)
                _address = details.Address;
            // A published editor owns the tab address. Without this the address fell back to the
            // selected-Space form whenever a later state change re-projected it, so the address bar
            // no longer identified the Space surface actually presented.
            else if (_publishedBuilderRoute is { } builder)
                _address = builder.Address;
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
        _routeEpoch++;
        _chatPage = chat;
        _publishedChatRoute = new(chat, ConversationAddress(chat.CurrentConversation));
        _address = _publishedChatRoute.Address;
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
        // Every canonical Space type is a conversation Space: Study, Tasks, Translate, Research,
        // Shopping and Assistants are Space types, not separate apps. The Haven tab therefore
        // opens a conversation scoped to the selected Space for any registered destination.
        var spaceId = action.Context.Scope.RegisteredSpaceId;
        if (spaceId is not { } registeredSpaceId || registeredSpaceId == Guid.Empty)
            throw new InvalidOperationException("The Space action has no registered Space to scope its conversation.");
        var key = registeredSpaceId;
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
        WireChatRecovery(factory);
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
            await RefreshSpacesConversationsAsync(token);
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

    /// <summary>Reads the selected Space's real recent conversations for the contextual sidebar.
    /// A store without Space membership queries truthfully contributes no rows; any other failure
    /// is reported to the caller rather than being presented as an empty history.</summary>
    private async Task<IReadOnlyList<SpacesHomeConversationRow>> ReadSpaceConversationsAsync(Guid spaceId, CancellationToken token)
    {
        IReadOnlyList<Conversation> rows;
        try
        {
            rows = await _services.GetRequiredService<IConversationRepository>()
                .GetBySpaceAsync(spaceId, 20, token).ConfigureAwait(false);
        }
        catch (NotSupportedException) { return []; }
        Guid? current;
        lock (_gate) current = _publishedChatRoute?.Page.CurrentConversation.Id;
        return rows
            .Where(row => !row.IsArchived && !row.IsTemporary)
            .Select(row => new SpacesHomeConversationRow(row.Id,
                string.IsNullOrWhiteSpace(row.Title) ? "Untitled conversation" : row.Title,
                row.UpdatedAt.LocalDateTime.ToString("d MMM yyyy HH:mm"),
                row.Id == current))
            .ToArray();
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
        try { return _spacesPage?.RefreshConversationsAsync(token) ?? Task.CompletedTask; }
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
    /// fabricated navigation or state change). The navigation settlement is the page's genuinely
    /// admitted original work, never a completed-task stand-in.</summary>
    private void WireChatRecovery(OverlayChatSessionFactory factory)
    {
        factory.WireHost = page => page.ManageLocalModelsRequested += (_, _) =>
        {
            if (_hostQueueNavigation is null || _closing || !_published) return;
            try
            {
                var address = AppContentAddress.Parse("9to1://home/models");
                var settlement = page.JoinAdmittedOriginalWorkAsync();
                _hostQueueNavigation(new BrowseNavigationIntent(_request.TabId, address, OpenInNewTab: true),
                    settlement);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                System.Diagnostics.Trace.TraceError("Spaces local-model recovery navigation failed: {0}", failure);
            }
        };
    }

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
            await page.AssignSpaceAsync(spaceId);
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
            if (!published) { await page.DisposeAsync(); _ownedChats.Remove(page); }
        }
    }

    /// <summary>Opens an existing conversation of the selected Space from the contextual sidebar.
    /// The real stored identity is restored; nothing is replaced or recreated on failure.</summary>
    private async Task OpenConversationOnUiAsync(Guid conversationId, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        DemandAlive();
        var conversations = _services.GetRequiredService<IConversationRepository>();
        var row = await conversations.GetAsync(conversationId, token)
            ?? throw new SpacesWorkspaceActionRefusal("The selected conversation is no longer available.");
        if (row.IsArchived || row.IsTemporary)
            throw new SpacesWorkspaceActionRefusal("That conversation is not available in this Space.");
        var chat = await NewChatSessionFactory().RestoreAsync(conversationId, token)
            ?? throw new SpacesWorkspaceActionRefusal("The selected conversation is no longer available.");
        _ownedChats.Add(chat);
        var published = false;
        try
        {
            token.ThrowIfCancellationRequested();
            DemandAlive();
            if (chat.CurrentConversation.Id != conversationId || chat.CurrentConversation.SpaceId != row.SpaceId)
                throw new InvalidOperationException("The restored conversation is not the selected Space conversation.");
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                DemandAlive();
                PublishChatPage(chat);
                if (chat.CurrentConversation.SpaceId is { } spaceId) _chatsBySpace[spaceId] = chat;
                ObserveConversationRoute(chat);
                published = true;
            }
            PublishStateChanged();
            await RefreshSpacesConversationsAsync(token);
        }
        finally
        {
            if (!published) { await chat.DisposeAsync(); _ownedChats.Remove(chat); }
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

    private async Task ExecuteWorkspaceCommandDriverAsync(Task start, CommandExecution execution, string command, Guid target,
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
                        if (command == "spaces.new-chat") await NewConversationOnUiAsync(target, linked.Token);
                        else if (command == "spaces.open-conversation") await OpenConversationOnUiAsync(target, linked.Token);
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
