using System.ComponentModel;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Home;

namespace Haven.Desktop.Views.Pages.Spaces;

/// <summary>
/// Reusable native CUI content page for Spaces Home. It borrows the canonical
/// <see cref="SpaceRegistry"/> and hosts the authored <c>SpacesHome.cui</c> surface through
/// <see cref="SpacesHomeController"/> and <see cref="SpacesHomeCuiBindings"/>.
///
/// The page is a content surface, not a window or full shell. Root owns tab/routing lifetime.
/// Navigation actions are admitted under the page's original-work lifetime. When Root supplies a
/// queue callback, the page hands it the immutable Spaces action plus the combined settlement of
/// the originating page original and the actual CUI loader pipeline; Root owns downstream
/// navigation independently. Without the callback, the supplied host is invoked directly.
/// </summary>
public sealed partial class SpacesHomePage : UserControl, IActivatablePage,
    ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, ICuiRepeatItemBindingContext,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard,
    INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private readonly SpaceRegistry _registry;
    private readonly SpacesHomeController _controller;
    private readonly SpacesHomeCuiBindings _bindings;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiSceneHost _host;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    [ThreadStatic] private static List<SpacesHomePage>? _sourceCallbacks;
    private bool _active;
    private long _generation;
    private Task? _hostClose;
    private Task<CuiSceneAvailability>? _exactOriginalShow;
    private int _editorOperationCount;
    // The authored `space-app-content` region inside the Spaces rail layout. Root owns whatever
    // workspace is mounted here; this page only hosts the control and reports its presence.
    private ContentControl? _spaceWorkspace;
    // The contextual sidebar's recent conversations for the selected Space. The owning surface
    // supplies them from the real conversation store; this page caches the latest projection.
    private readonly Func<Guid, CancellationToken, Task<IReadOnlyList<SpacesHomeConversationRow>>>? _conversationsProvider;
    private readonly Func<string, object?, CancellationToken, Task>? _workspaceCommandHost;
    private IReadOnlyList<SpacesHomeConversationRow> _conversations = [];

    public SpacesHomePage(
        SpaceRegistry registry,
        ISpacesActionHost navigationHost,
        ICuiSceneReadiness readiness,
        ISpacesExtensionEditorHost? extensionEditorHost = null,
        Func<string, CancellationToken, Task<string>>? pickFileAsync = null,
        Func<SpacesAction, Task, bool>? queueNavigation = null,
        Func<Guid, CancellationToken, Task<IReadOnlyList<SpacesHomeConversationRow>>>? conversationsProvider = null,
        Func<string, object?, CancellationToken, Task>? workspaceCommandHost = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (navigationHost is null) throw new ArgumentNullException(nameof(navigationHost));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _conversationsProvider = conversationsProvider;
        _workspaceCommandHost = workspaceCommandHost;

        Document = SpacesHomeCuiDocument.Load();
        var controlRegistry = SpacesHomeCuiDocument.CreateControlRegistry();
        // The authored workspace region is a stable host-owned mount, so a re-published scene
        // keeps the same control and the mounted workspace is never silently rebuilt empty.
        // A fresh publication builds a new tree, so the previous parent must release the mount
        // first; Avalonia controls admit only one parent.
        controlRegistry.BindAuthoredControl("space-app-content", _ =>
        {
            if (_spaceWorkspace is { } existing) DetachFromParent(existing);
            return _spaceWorkspace ??= new ContentControl();
        });
        _host = new CuiSceneHost(controlRegistry);
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);

        var queuedNavigationHost = new QueuedSpacesActionHost(this, navigationHost, _work, queueNavigation);
        _controller = new SpacesHomeController(
            registry,
            queuedNavigationHost,
            extensionEditorHost,
            WrapPicker(pickFileAsync));
        _bindings = new SpacesHomeCuiBindings(
            () => _controller.State,
            DispatchAsync,
            (expected, actual) => ReferenceEquals(expected, actual));
        _controller.StateChanged += OnControllerStateChanged;
        _bindings.PropertyChanged += OnBindingsPropertyChanged;

        AutomationProperties.SetAutomationId(this, "SpacesHomePage");
        AutomationProperties.SetName(this, "Spaces Home");
        Content = _host;
    }

    public CuiDocument Document { get; }
    public SpacesHomeController Controller => _controller;
    internal CuiSceneHost OriginalHost => _host;
    public Task? OriginalClose => _work.OriginalClose;
    public new event PropertyChangedEventHandler? PropertyChanged;

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        long generation;
        lock (_gate)
        {
            _work.DemandAdmission();
            if (_homeCloseLease is not null) throw new InvalidOperationException("Abort the Home preparation before activation.");
            _active = true; generation = ++_generation;
            return RunHomeOriginal(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
                original.DemandPublication();
                await RefreshOriginalAsync(original, generation, scope.Token).ConfigureAwait(false);

                Task? showJoin = null;
                var dispatcher = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    original.DemandPublication();
                    var show = InvokeOriginalSource(() => _host.ShowAsync(new("spaces.home", "Spaces", "Spaces",
                        Document, this, this, _readiness) { IsPublicationCurrent = () => IsCurrent(generation) }, scope.Token));
                    _exactOriginalShow = show;
                    showJoin = original.AwaitAsync(show);
                }).GetTask();
                try { await original.AwaitAsync(dispatcher).ConfigureAwait(false); }
                catch (Exception error) { original.Capture(dispatcher, error); }
                if (showJoin is not null)
                    try { await original.AwaitAsync(showJoin).ConfigureAwait(false); }
                    catch (Exception error) { original.Capture(showJoin, error); }
                else original.Retain(new InvalidOperationException("No Spaces Home Show was acquired."));
                original.ThrowRetained();
            });
        }
    }

    public void Deactivate()
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_gate)
        {
            if (_active && _exactOriginalShow is { } shown)
                _homeLastPipeline = _host.CaptureOriginalActionSettlement(shown);
            _active = false; _generation++;
        }
    }

    private async Task RefreshOriginalAsync(DesktopOriginalWorkLifetime.Original original, long generation, CancellationToken token)
    {
        original.DemandPublication();
        var init = InvokeOriginalSource(() => _controller.LoadAsync(token));
        await original.AwaitAsync(init).ConfigureAwait(false);
        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
        {
            original.DemandPublication();
            _bindings.Refresh();
        }).GetTask()).ConfigureAwait(false);
    }

    private void OnControllerStateChanged(SpacesHomeViewState state)
    {
        long generation;
        lock (_gate)
        {
            if (!_active || _work.IsRetiring) return;
            generation = _generation;
            _ = RunHomeOriginal(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                    original.RunAcceptedPublicationCallback(_bindings.Refresh)).GetTask()).ConfigureAwait(false);
            });
        }
    }

    private void OnBindingsPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
        PropertyChanged?.Invoke(this, args);

    private bool IsCurrent(long generation)
    { lock (_gate) return !_work.IsRetiring && _active && _generation == generation; }

    public bool TryGetValue(string path, out object? value)
    {
        if (path == "spaces.canMutate") { lock (_gate) value = CanAdmitHomeMutation(); return true; }
        if (path == "spaces.hasAppContent") { lock (_gate) value = _spaceWorkspace?.Content is not null; return true; }
        if (path == "spaces.hasSpaceSummary") { lock (_gate) value = _spaceWorkspace?.Content is null; return true; }
        if (path == "spaces.conversations") { lock (_gate) value = _conversations; return true; }
        if (path == "spaces.hasConversations") { lock (_gate) value = _conversations.Count > 0; return true; }
        return _bindings.TryGetValue(path, out value);
    }
    public bool TrySetValue(string path, object? value)
    { lock (_gate) return CanAdmitHomeMutation() && _bindings.TrySetValue(path, value); }
    public bool TryGetItemValue(object item, string path, out object? value) => _bindings.TryGetItemValue(item, path, out value);
    public bool TrySetItemValue(object item, string path, object? value)
    { lock (_gate) return CanAdmitHomeMutation() && _bindings.TrySetItemValue(item, path, value); }

    public bool? IsActionAvailable(string command)
    { lock (_gate) return CanAdmitHomeMutation() && _bindings.IsActionAvailable(command) == true; }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // A loader action queued before the reversible boundary may arrive later.
            // It acquires no mutation original while paused, and does not poison that loader.
            if (_homeCloseLease is not null) return ValueTask.CompletedTask;
            if (!CanAdmitHomeMutation() || _bindings.IsActionAvailable(command) != true)
                throw new InvalidOperationException($"Spaces Home command '{command}' is unavailable.");
            var generation = _generation;
            var actual = RunHomeOriginal(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
                original.DemandPublication();
                await DispatchOriginalAsync(original, generation, command, parameter, scope.Token).ConfigureAwait(false);
            });
            _latestOriginalMutation = actual;
            return new(actual);
        }
    }

    private async Task DispatchOriginalAsync(DesktopOriginalWorkLifetime.Original original, long generation,
        string command, object? parameter, CancellationToken token)
    {
        original.DemandPublication();
        var id = parameter is Guid g ? g : _controller.State.Selection?.Id ?? Guid.Empty;
        switch (command)
        {
            case "spaces.create":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.CreateCustomSpaceAsync(token))).ConfigureAwait(false);
                break;
            case "spaces.open":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.OpenSpaceAsync(id, token))).ConfigureAwait(false);
                break;
            case "spaces.edit-details":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.EditDetailsAsync(id, token))).ConfigureAwait(false);
                break;
            case "spaces.rename":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.StartRenameAsync(id, token))).ConfigureAwait(false);
                break;
            case "spaces.move":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.MoveSpaceAsync(id, token))).ConfigureAwait(false);
                break;
            case "spaces.fork":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.ForkSpaceAsync(id, token))).ConfigureAwait(false);
                break;
            case "spaces.archive":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.ArchiveSpaceAsync(id, token))).ConfigureAwait(false);
                break;
            case "spaces.add-context":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.AddContextAsync(id, null, token))).ConfigureAwait(false);
                break;
            case "spaces.clear-search":
                InvokeOriginalSource(() => _controller.SetQuery(string.Empty));
                break;
            case "spaces.select":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.SelectSpaceAsync(id, token))).ConfigureAwait(false);
                // The owner's Spaces entry journey is direct: selecting a Space in the icon-only
                // rail opens that Space's conversation in the same gesture. The Space summary is
                // a management view, never a mandatory interstitial in front of a chat.
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.OpenSpaceAsync(id, token))).ConfigureAwait(false);
                break;
            case "spaces.new-chat":
                if (_workspaceCommandHost is not { } newChatHost)
                    throw new InvalidOperationException("No Spaces workspace host is available to start a conversation.");
                await original.AwaitAsync(InvokeOriginalSource(() => newChatHost("spaces.new-chat", id, token))).ConfigureAwait(false);
                break;
            case "spaces.open-conversation":
                if (_workspaceCommandHost is not { } conversationHost)
                    throw new InvalidOperationException("No Spaces workspace host is available to open a conversation.");
                await original.AwaitAsync(InvokeOriginalSource(() => conversationHost("spaces.open-conversation", parameter, token))).ConfigureAwait(false);
                break;
            case "spaces.toggle-sidebar":
                InvokeOriginalSource(_controller.ToggleSidebar);
                break;
            case "spaces.toggle-space-switcher":
                InvokeOriginalSource(_controller.ToggleSpaceSwitcher);
                break;
            case "spaces.toggle-space-overflow":
                InvokeOriginalSource(_controller.ToggleSpaceOverflow);
                break;
            case "spaces.set-rename-draft":
                InvokeOriginalSource(() => _controller.SetRenameDraft(parameter?.ToString()));
                break;
            case "spaces.set-query":
                InvokeOriginalSource(() => _controller.SetQuery(parameter?.ToString()));
                break;
            case "spaces.confirm-rename":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.ConfirmRenameAsync(token))).ConfigureAwait(false);
                break;
            case "spaces.cancel-rename":
                InvokeOriginalSource(_controller.CancelRename);
                break;
            case "spaces.select-move-parent":
                InvokeOriginalSource(() => _controller.SelectMoveParentIndex((int)parameter!));
                break;
            case "spaces.edit-extensions":
                Interlocked.Increment(ref _editorOperationCount);
                try { await original.AwaitAsync(InvokeOriginalSource(() => _controller.EditExtensionsAsync(id, token))).ConfigureAwait(false); }
                finally { Interlocked.Decrement(ref _editorOperationCount); }
                break;
            case "spaces.edit-graph":
                Interlocked.Increment(ref _editorOperationCount);
                try { await original.AwaitAsync(InvokeOriginalSource(() => _controller.EditGraphAsync(id, token))).ConfigureAwait(false); }
                finally { Interlocked.Decrement(ref _editorOperationCount); }
                break;
            default:
                throw new InvalidOperationException($"Spaces Home command '{command}' is not owned by this host.");
        }
    }

    /// <summary>The authored <c>space-app-content</c> region inside the retained Spaces rail
    /// layout, or null before the scene has published it. Root owns whatever is mounted there.</summary>
    internal Control? SpaceWorkspaceMount
    {
        get { lock (_gate) return _spaceWorkspace; }
    }

    /// <summary>The workspace Root currently mounts in that region, or null. Reading this never
    /// transfers ownership of the mounted control.</summary>
    internal Control? SpaceWorkspaceContent
    {
        get { lock (_gate) return _spaceWorkspace?.Content as Control; }
    }

    /// <summary>
    /// Mounts Root's workspace (a Space chat, builder or details surface) into the authored
    /// workspace region so the icon-only Spaces rail and the workspace, including its own
    /// chat/history sidebar, stay visible together. This page never owns the mounted control:
    /// Root retains its lifetime, close preparation and actual retirement.
    /// </summary>
    internal bool TryMountSpaceWorkspace(Control workspace)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(workspace);
        ContentControl? mount;
        lock (_gate)
        {
            if (_work.IsRetiring || !_active) return false;
            mount = _spaceWorkspace;
        }
        if (mount is null) return false;
        return InvokeOriginalSource(() =>
        {
            mount.Content = workspace;
            _bindings.Refresh();
            return true;
        });
    }

    /// <summary>Detaches the workspace Root has retired from the authored region and restores the
    /// selected Space summary. A non-matching <paramref name="expected"/> leaves the live
    /// workspace untouched rather than clearing a newer navigation.</summary>
    internal bool ClearSpaceWorkspace(Control? expected = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_gate)
        {
            if (_work.IsRetiring || _spaceWorkspace is not { } mount) return false;
            if (expected is not null && !ReferenceEquals(mount.Content, expected)) return false;
            if (mount.Content is null) return false;
            mount.Content = null;
        }
        InvokeOriginalSource(() => { _bindings.Refresh(); return true; });
        return true;
    }

    /// <summary>Re-reads the selected Space's recent conversations from the owning store and
    /// republishes the contextual sidebar. A stale or retiring presentation discards the result
    /// instead of overwriting a newer selection.</summary>
    public async Task RefreshConversationsAsync(CancellationToken cancellationToken = default)
    {
        long generation;
        Guid? selected;
        Func<Guid, CancellationToken, Task<IReadOnlyList<SpacesHomeConversationRow>>>? provider;
        lock (_gate)
        {
            if (_work.IsRetiring || !_active) return;
            provider = _conversationsProvider;
            if (provider is null) return;
            generation = _generation;
            selected = _controller.State.Selection?.Id;
        }
        if (selected is not { } spaceId)
        {
            lock (_gate) if (!_work.IsRetiring && _generation == generation) _conversations = [];
            return;
        }
        var rows = await provider(spaceId, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_work.IsRetiring || !_active || _generation != generation) return;
            _conversations = rows;
        }
        await Dispatcher.UIThread.InvokeAsync(() => _bindings.Refresh()).GetTask().ConfigureAwait(false);
    }

    /// <summary>Releases a control from its current native parent. A fresh CUI publication
    /// builds a new tree, and Avalonia admits only one parent per control.</summary>
    private static void DetachFromParent(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case ContentControl host when ReferenceEquals(host.Content, control):
                host.Content = null;
                break;
            // A CUI Container cell wraps each child in its own decorator border.
            case Decorator decorator when ReferenceEquals(decorator.Child, control):
                decorator.Child = null;
                break;
        }
    }

    /// <summary>
    /// Pre-flight close safety. Returns <c>false</c> while a rename draft or editor operation is
    /// pending so the live UI is preserved; the caller should keep the page open.
    /// </summary>
    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_controller.State.PendingRenameSpaceId.HasValue || Interlocked.CompareExchange(ref _editorOperationCount, 0, 0) > 0)
                return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        if (_sourceCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual Spaces Home source callback cannot join its encompassing close.");
        _work.DemandExternalClose();
    }

    public void RequestRetirement() { lock (_gate) _work.RequestRetirement(); }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate) return _work.CloseAndDrainAsync();
    }

    private Task StopOriginalPresentationAsync()
    {
        lock (_gate) { _active = false; _generation++; }
        _controller.StateChanged -= OnControllerStateChanged;
        _bindings.PropertyChanged -= OnBindingsPropertyChanged;
        try { return _hostClose ??= _host.CloseOriginalAsync(); }
        catch (Exception cause) { return _hostClose ??= Task.FromException(cause); }
    }

    private async Task DetachOriginalPresentationAsync()
    {
        Task? hostClose;
        lock (_gate) hostClose = _hostClose;
        if (hostClose is null)
            throw new InvalidOperationException("No actual Spaces Home host close was acquired before cleanup.");
        await hostClose.ConfigureAwait(false);
        if (!hostClose.IsCompletedSuccessfully)
        {
            // Preserve the actual close cause instead of replacing it with a generic message;
            // the live view stays inspectable because we do not detach Content on failure.
            hostClose.GetAwaiter().GetResult();
        }
        var actual = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            OwnNestedOriginalSource(() => { if (ReferenceEquals(Content, _host)) Content = null; })).GetTask());
        await actual.ConfigureAwait(false);
    }

    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private void OwnNestedOriginalSource(Action callback) =>
        InvokeOriginalSource(() => { callback(); return true; });

    private void InvokeOriginalSource(Action callback)
    {
        var stack = _sourceCallbacks ??= []; stack.Add(this);
        try { callback(); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }

    private T InvokeOriginalSource<T>(Func<T> callback)
    {
        var stack = _sourceCallbacks ??= []; stack.Add(this);
        try { return CloudflareOriginalExecutionGuard.InvokeOriginal(this, callback); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }

    private Func<string, CancellationToken, Task<string>>? WrapPicker(Func<string, CancellationToken, Task<string>>? picker) =>
        picker is null ? null : async (title, token) =>
        {
            return await CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                Dispatcher.UIThread.InvokeAsync<Task<string>>(() => picker(title, token)).GetTask().Unwrap());
        };

    private sealed class ImmediateReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Spaces Home is ready."));
    }

    /// <summary>Shared readiness instance for callers that do not supply their own.</summary>
    public static ICuiSceneReadiness ImmediateReadinessInstance { get; } = new ImmediateReadiness();

    /// <summary>
    /// Wraps the real navigation host. When Root supplies a <c>queueNavigation</c>
    /// callback, open actions are admitted synchronously under the SAME originating page original
    /// and the actual CUI loader settlement is handed to Root's queue. The page never awaits
    /// downstream navigation. Without the callback, the inner host is called directly so direct
    /// standalone/domain callers keep their normal completion semantics.
    /// </summary>
    private sealed class QueuedSpacesActionHost : ISpacesActionHost, ISpacesActionAdmissionHost, ISpacesActionSupport
    {
        private readonly SpacesHomePage _page;
        private readonly ISpacesActionHost _inner;
        private readonly DesktopOriginalWorkLifetime _work;
        private readonly Func<SpacesAction, Task, bool>? _queueNavigation;

        public QueuedSpacesActionHost(SpacesHomePage page, ISpacesActionHost inner, DesktopOriginalWorkLifetime work, Func<SpacesAction, Task, bool>? queueNavigation)
        {
            _page = page;
            _inner = inner;
            _work = work;
            _queueNavigation = queueNavigation;
        }

        public bool IsQueueAdmission => _queueNavigation is not null;

        public bool SupportsAction(SpacesActionKind action) =>
            _inner is ISpacesActionSupport support && support.SupportsAction(action);

        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken)
        {
            if (action.Kind == SpacesActionKind.EditDetails && !SupportsAction(action.Kind))
                throw new NotSupportedException("The actual Spaces host does not support editing details.");

            if (_queueNavigation is null)
                return _inner.ExecuteAsync(action, cancellationToken);

            var originating = _work.Executing;
            if (originating is null)
                return _inner.ExecuteAsync(action, cancellationToken);

            originating.DemandPublication();

            var exactShow = _page._exactOriginalShow;
            if (exactShow is null)
                throw new InvalidOperationException("No exact original Spaces Home Show is available for action settlement.");

            return ExecuteQueuedAsync(action, originating, exactShow, _page._controller.State, cancellationToken);
        }

        private async Task ExecuteQueuedAsync(SpacesAction action, DesktopOriginalWorkLifetime.Original originating,
            Task<CuiSceneAvailability> exactShow, SpacesHomeViewState selection, CancellationToken cancellationToken)
        {
            // Capture the actual loader action settlement on the UI dispatcher, then combine it
            // with the SAME originating page original so the queue cannot navigate until both the
            // CUI pipeline and the page action have settled.
            var pipeline = await _page.InvokeOriginalSource(() =>
                Dispatcher.UIThread.InvokeAsync<Task>(() => _page._host.CaptureOriginalActionSettlement(exactShow)).GetTask()).ConfigureAwait(false);
            var combined = Task.WhenAll(pipeline, originating.Task);

            var admitted = false;
            try
            {
                originating.RunAcceptedPublicationCallback(() =>
                {
                    if (action.Kind == SpacesActionKind.EditDetails)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (action.Kind == SpacesActionKind.EditDetails &&
                        (!ReferenceEquals(_page._controller.State, selection) ||
                         selection.Selection is not { CanEditDetails: true, IsBuiltIn: false } current ||
                         current.Id != action.Context.Scope.RegisteredSpaceId || !SupportsAction(action.Kind)))
                        throw new InvalidOperationException("The Space selection changed before details queue admission.");
                    admitted = _queueNavigation!(action, combined);
                });
            }
            catch (Exception cause)
            {
                throw new InvalidOperationException("The Spaces action could not be admitted for navigation.", cause);
            }

            if (!admitted)
                throw new InvalidOperationException("The navigation queue refused the Spaces action.");
        }
    }
}
