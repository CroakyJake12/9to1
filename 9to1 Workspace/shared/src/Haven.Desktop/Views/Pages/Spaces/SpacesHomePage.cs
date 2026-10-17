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
public sealed class SpacesHomePage : UserControl, IActivatablePage,
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

    public SpacesHomePage(
        SpaceRegistry registry,
        ISpacesActionHost navigationHost,
        ICuiSceneReadiness readiness,
        ISpacesExtensionEditorHost? extensionEditorHost = null,
        Func<string, CancellationToken, Task<string>>? pickFileAsync = null,
        Func<SpacesAction, Task, bool>? queueNavigation = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (navigationHost is null) throw new ArgumentNullException(nameof(navigationHost));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));

        Document = SpacesHomeCuiDocument.Load();
        _host = new CuiSceneHost();
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
        lock (_gate) { _work.DemandAdmission(); _active = true; generation = ++_generation; }
        return _work.RunAsync(async original =>
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

    public void Deactivate() { lock (_gate) { _active = false; _generation++; } }

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
        }
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                _bindings.Refresh();
            }).GetTask()).ConfigureAwait(false);
        });
    }

    private void OnBindingsPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
        PropertyChanged?.Invoke(this, args);

    private bool IsCurrent(long generation)
    { lock (_gate) return !_work.IsRetiring && _active && _generation == generation; }

    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) => _bindings.TrySetValue(path, value);
    public bool TryGetItemValue(object item, string path, out object? value) => _bindings.TryGetItemValue(item, path, out value);
    public bool TrySetItemValue(object item, string path, object? value) => _bindings.TrySetItemValue(item, path, value);

    public bool? IsActionAvailable(string command) => _bindings.IsActionAvailable(command);

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (_bindings.IsActionAvailable(command) != true)
            throw new InvalidOperationException($"Spaces Home command '{command}' is unavailable.");

        long generation;
        lock (_gate) generation = _generation;
        await _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            original.DemandPublication();
            await DispatchOriginalAsync(original, generation, command, parameter, scope.Token).ConfigureAwait(false);
        });
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
            case "spaces.rename":
                InvokeOriginalSource(() => _controller.StartRename(id));
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

    public void RequestRetirement() => _work.RequestRetirement();

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return _work.CloseAndDrainAsync();
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
    private sealed class QueuedSpacesActionHost : ISpacesActionHost, ISpacesActionAdmissionHost
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

        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken)
        {
            if (_queueNavigation is null)
                return _inner.ExecuteAsync(action, cancellationToken);

            var originating = _work.Executing;
            if (originating is null)
                return _inner.ExecuteAsync(action, cancellationToken);

            originating.DemandPublication();

            var exactShow = _page._exactOriginalShow;
            if (exactShow is null)
                throw new InvalidOperationException("No exact original Spaces Home Show is available for action settlement.");

            return ExecuteQueuedAsync(action, originating, exactShow, cancellationToken);
        }

        private async Task ExecuteQueuedAsync(SpacesAction action, DesktopOriginalWorkLifetime.Original originating,
            Task<CuiSceneAvailability> exactShow, CancellationToken cancellationToken)
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
