using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Native CUI host for the Chat Space. The live Chat surface remains <see cref="NewChatPage"/>;
/// this page exists to validate the CUI Chat surface against real domain state before any swap.
///
/// The page owns one <see cref="ChatSpaceController"/>, one <see cref="ChatSpaceCuiBindings"/>
/// and one <see cref="CuiSceneHost"/>. It follows the same lifetime patterns as
/// <see cref="SpaceTaskWidgetPage"/>: an <see cref="IActivatablePage"/> activation, an original-work
/// lifetime, and explicit external retirement.
/// </summary>
public sealed partial class ChatSpacePage : UserControl, IActivatablePage,
    ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, ICuiRepeatItemBindingContext,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard,
    INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private readonly ChatSpaceController _controller;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiSceneHost _host;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly ChatSpaceCuiBindings _bindings;
    private readonly ChatComposerCuiView _composerView;
    private readonly object _gate = new();
    [ThreadStatic] private static List<ChatSpacePage>? _sourceCallbacks;
    private bool _active;
    private bool _initialized;
    private long _generation;
    private Task? _hostClose;
    private Task? _composerClose;

    public ChatSpacePage(IChatSpaceBackend backend, ICuiSceneReadiness readiness, Guid? conversationId = null, IPlatformShellService? clipboard = null,
        Func<MessageRole, (bool Visible, string? Source)>? avatarResolver = null)
    {
        _controller = new ChatSpaceController(backend ?? throw new ArgumentNullException(nameof(backend)), clipboard, avatarResolver);
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        _bindings = new ChatSpaceCuiBindings(
            () => _controller.State,
            DispatchAsync,
            (expected, actual) => ReferenceEquals(expected, actual));
        _controller.StateChanged += OnControllerStateChanged;
        // This page is the scene's binding context, so the CUI reconciler subscribes to THIS
        // event. Forwarding the projection's own notification is what makes a republished
        // transcript, composer or recent-chat row actually re-render; without it the scene
        // loads once and never updates.
        _bindings.PropertyChanged += OnBindingsPropertyChanged;
        Document = ChatSpaceCuiDocument.Load();
        // The shared canonical composer is mounted into the authored `composer-mount` slot below.
        ChatComposerCuiView? composerView = null;
        composerView = new ChatComposerCuiView(new ChatSpaceComposerSource(
            () => _controller.State, DispatchAsync, () => composerView!.Input));
        _composerView = composerView;
        var controlRegistry = new CuiControlRegistry();
        controlRegistry.BindAuthoredControl("composer-mount", _ => _composerView);
        _host = new CuiSceneHost(controlRegistry);
        AutomationProperties.SetAutomationId(this, "ChatSpacePage");
        AutomationProperties.SetName(this, "Chat Space (CUI)");
        SizeChanged += (_, _) => _bindings.UpdateViewportWidth(Bounds.Width);
        Content = _host;
        InitialConversationId = conversationId;
    }

    public Guid? InitialConversationId { get; }
    public CuiDocument Document { get; }
    public ChatSpaceController Controller => _controller;
    internal CuiSceneHost OriginalHost => _host;
    public new event PropertyChangedEventHandler? PropertyChanged;

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        long generation;
        lock (_gate) { _work.DemandAdmission(); _active = true; generation = ++_generation; }
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            original.DemandPublication();
            await RefreshOriginalAsync(original, generation, scope.Token).ConfigureAwait(false);
            Task? showJoin = null;
            var dispatcher = Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                var show = InvokeOriginalSource(() => _host.ShowAsync(new("spaces.chat", "Chat Space", "Chat",
                    Document, this, this, _readiness) { IsPublicationCurrent = () => IsCurrent(generation) }, scope.Token));
                showJoin = original.AwaitAsync(show);
            }).GetTask();
            try { await original.AwaitAsync(dispatcher).ConfigureAwait(false); }
            catch (Exception error) { original.Capture(dispatcher, error); }
            if (showJoin is not null)
                try { await original.AwaitAsync(showJoin).ConfigureAwait(false); }
                catch (Exception error) { original.Capture(showJoin, error); }
            else original.Retain(new InvalidOperationException("No Chat Space Show was acquired."));
            original.ThrowRetained();
        });
    }

    public void Deactivate() { lock (_gate) { _active = false; _generation++; } }

    private async Task RefreshOriginalAsync(DesktopOriginalWorkLifetime.Original original, long generation, CancellationToken token)
    {
        original.DemandPublication();
        bool initialize;
        lock (_gate) initialize = !_initialized;
        if (initialize)
        {
            var init = InvokeOriginalSource(() => _controller.InitializeAsync(InitialConversationId, token));
            await original.AwaitAsync(init).ConfigureAwait(false);
            lock (_gate) _initialized = !_controller.State.IsLoading && _controller.State.Status.Tone != ChatSpaceStatusTone.Error;
        }
        // The shared composer mounts its own scene; join its guarded initialization before the
        // host publishes/restores the conversation so no empty mount or late input race is
        // presented as a ready composer.
        await original.AwaitAsync(_composerView.InitializeAsync(token)).ConfigureAwait(false);
        // A tab reactivation republishes this same controller, including navigation, draft and
        // any response completed while hidden. It does not reopen the construction-time chat.
        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
        {
            original.DemandPublication();
            _bindings.Refresh();
            _composerView.RefreshSurface();
        }).GetTask()).ConfigureAwait(false);
    }

    private void OnControllerStateChanged(ChatSpaceViewState state)
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
            try
            {
                await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsCurrent(generation)) return;
                    _bindings.Refresh();
                    _composerView.RefreshSurface();
                }).GetTask()).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The presentation was replaced or retired between the state change and
                // the dispatcher callback. The next state change republishes; nothing to do.
            }
        });
    }

    private bool IsCurrent(long generation)
    { lock (_gate) return !_work.IsRetiring && _active && _generation == generation; }

    public bool TryGetValue(string path, out object? value)
    {
        // The binding projection is owned by ChatSpaceCuiBindings; this surface only exposes
        // values the authored document reads. Unknown paths render absent rather than silently
        // matching a different property.
        return _bindings.TryGetValue(path, out value);
    }

    public bool TrySetValue(string path, object? value)
    {
        return _bindings.TrySetValue(path, value);
    }

    public bool TryGetItemValue(object item, string path, out object? value) => _bindings.TryGetItemValue(item, path, out value);
    public bool TrySetItemValue(object item, string path, object? value) => _bindings.TrySetItemValue(item, path, value);

    public bool? IsActionAvailable(string command)
    {
        return _bindings.IsActionAvailable(command);
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (_bindings.IsActionAvailable(command) != true)
            throw new InvalidOperationException($"Chat Space command '{command}' is unavailable.");

        long generation;
        lock (_gate) generation = _generation;
        await _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            original.DemandPublication();
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            await DispatchOriginalAsync(original, generation, command, parameter, scope.Token).ConfigureAwait(false);
        });
    }

    private async Task DispatchOriginalAsync(DesktopOriginalWorkLifetime.Original original, long generation,
        string command, object? parameter, CancellationToken token)
    {
        original.DemandPublication();
        switch (command)
        {
            case "chat.new":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.NewChatAsync(token))).ConfigureAwait(false);
                break;
            case "chat.open" when parameter is Guid conversationId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.OpenChatAsync(conversationId, token))).ConfigureAwait(false);
                break;
            case "chat.update-draft" when parameter is string draft:
                InvokeOriginalSource(() => _controller.UpdateDraft(draft));
                break;
            case "chat.select-model":
                var modelName = ResolveModelName(parameter);
                if (!string.IsNullOrWhiteSpace(modelName))
                    InvokeOriginalSource(() => _controller.SelectModel(modelName));
                break;
            case "chat.switch-branch" when parameter is Guid branchId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.SwitchBranchAsync(branchId, token))).ConfigureAwait(false);
                break;
            case "chat.branch" when parameter is Guid messageId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.BranchAsync(messageId, null, token))).ConfigureAwait(false);
                break;
            case "chat.regenerate" when parameter is Guid messageId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.RegenerateAsync(messageId, token))).ConfigureAwait(false);
                break;
            case "chat.attach":
                var paths = parameter is string supplied && !string.IsNullOrWhiteSpace(supplied)
                    ? new[] { supplied }
                    : await PickOriginalAttachmentPathsAsync(original, token).ConfigureAwait(false);
                foreach (var candidate in paths)
                    await original.AwaitAsync(InvokeOriginalSource(() => _controller.AddAttachmentAsync(candidate, token))).ConfigureAwait(false);
                break;
            case "chat.remove-attachment" when parameter is Guid attachmentId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.RemoveAttachmentAsync(attachmentId, token))).ConfigureAwait(false);
                break;
            case "chat.send":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.SendAsync(token))).ConfigureAwait(false);
                break;
            case "chat.stop":
                // The Chat Space domain does not expose an in-flight response cancellation. Fail
                // honestly rather than reporting a successful no-op stop.
                throw new InvalidOperationException("Chat Space does not support stopping an in-flight response.");
            case "chat.invoke-chip":
                // Chat Space chips are persisted attachments; they are not invokable capabilities.
                throw new InvalidOperationException("Chat Space attachments are not invokable chips.");
            case "chat.clear-search":
                InvokeOriginalSource(_bindings.ClearRecentSearch);
                break;
            case "chat.copy" when parameter is Guid messageId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.CopyMessageAsync(messageId, token))).ConfigureAwait(false);
                break;
            case "chat.delete" when parameter is Guid messageId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.DeleteMessageAsync(messageId, token))).ConfigureAwait(false);
                break;
            case "chat.forget" when parameter is Guid messageId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.ForgetMessageAsync(messageId, token))).ConfigureAwait(false);
                break;
            case "chat.edit" when parameter is Guid messageId:
                InvokeOriginalSource(() => _controller.StartEditMessage(messageId));
                break;
            case "chat.cancel-edit":
                InvokeOriginalSource(_controller.CancelEdit);
                break;
            case "chat.set-action-mode" when parameter is string actionMode && Enum.TryParse<ChatActionMode>(actionMode, true, out var parsedAction):
                InvokeOriginalSource(() => _controller.SetThreadActionMode(parsedAction));
                break;
            case "chat.set-response-mode" when parameter is string responseMode && Enum.TryParse<GenerativeUiResponseMode>(responseMode, true, out var parsedResponse):
                InvokeOriginalSource(() => _controller.SetThreadResponseMode(parsedResponse));
                break;
            case "chat.toggle-dual":
                InvokeOriginalSource(_controller.ToggleDualModel);
                break;
            case "chat.select-second-model" when parameter is string secondModel:
                InvokeOriginalSource(() => _controller.SelectDualSecondModel(secondModel));
                break;
            case "chat.select-dual-mode" when parameter is string dualMode && Enum.TryParse<DuoMode>(dualMode, true, out var parsedDuo):
                InvokeOriginalSource(() => _controller.SelectDualMode(parsedDuo));
                break;
            default:
                throw new InvalidOperationException($"Chat Space command '{command}' is not owned by this host.");
        }
    }

    private string? ResolveModelName(object? parameter)
    {
        if (parameter is ChatSpaceModelChoice choice) return choice.Name;
        var state = _bindings.Snapshot;
        if (parameter is int index && index >= 0 && index < state.Models.Count)
            return state.Models[index].Name;
        if (parameter is string name && state.Models.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return name;
        return null;
    }

    /// <summary>
    /// Resolves attachment paths through the platform file picker.
    ///
    /// An unavailable picker is raised as an action failure rather than silently ignored, so the
    /// surface shows the real reason the Attach control did nothing instead of appearing broken.
    /// </summary>
    private async Task<string[]> PickOriginalAttachmentPathsAsync(
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var storage = await original.AwaitAsync(InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(
            () => TopLevel.GetTopLevel(this)?.StorageProvider).GetTask())).ConfigureAwait(false);
        if (storage is null)
            throw new InvalidOperationException("The platform file picker is unavailable in this Chat Space window.");
        original.DemandPublication();
        var files = await original.AwaitAsync(InvokeOriginalSource(() => storage.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = "Add files to this chat", AllowMultiple = true })))
            .ConfigureAwait(false);
        return files.Select(file => file.TryGetLocalPath()).OfType<string>()
            .Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
    }

    private void OnBindingsPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
        PropertyChanged?.Invoke(this, args);

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
        try { return callback(); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_sourceCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual Chat Space source callback cannot join its encompassing close.");
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
        // Withdraw the publications BEFORE the host close so no callback can republish into a
        // scene that is retiring, and a failed host close still leaves no live producer.
        _controller.StateChanged -= OnControllerStateChanged;
        _bindings.PropertyChanged -= OnBindingsPropertyChanged;
        // Start the shared composer's child close now (withdraw its producers and detach its
        // watchers); the parent joins this exact task during detach.
        try { _composerClose = _composerView.CloseAsync(); } catch { /* Retained by work */ }
        Task? hostClose = null;
        try { hostClose = _hostClose ??= _host.CloseOriginalAsync(); } catch { /* Retained by work */ }
        return hostClose ?? Task.CompletedTask;
    }

    private async Task DetachOriginalPresentationAsync()
    {
        Task? hostClose;
        lock (_gate) hostClose = _hostClose;
        if (hostClose is not null) await hostClose.ConfigureAwait(false);
        if (_hostClose is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual Chat Space host close did not acknowledge retirement.");
        var actual = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            OwnNestedOriginalSource(() => { if (ReferenceEquals(Content, _host)) Content = null; })).GetTask());
        await actual.ConfigureAwait(false);
        // Join the exact shared-composer child close published during Stop. Only after the child
        // acknowledged is the physical view released; a failed close preserves physical ownership
        // and remains observable as an independent cleanup failure.
        var composerClose = _composerClose ?? _composerView.CloseAsync();
        await composerClose.ConfigureAwait(false);
        _composerView.Dispose();
    }

    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private sealed class ImmediateReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Chat Space is ready."));
    }

    /// <summary>Shared readiness instance for callers that do not supply their own.</summary>
    public static ICuiSceneReadiness ImmediateReadinessInstance { get; } = new ImmediateReadiness();
}
