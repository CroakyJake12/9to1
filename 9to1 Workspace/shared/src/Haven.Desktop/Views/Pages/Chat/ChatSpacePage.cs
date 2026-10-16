using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
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
    ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard,
    INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private readonly ChatSpaceController _controller;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiSceneHost _host;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly ChatSpaceCuiBindings _bindings;
    private readonly object _gate = new();
    [ThreadStatic] private static List<ChatSpacePage>? _sourceCallbacks;
    private bool _active;
    private long _generation;
    private Task? _hostClose;

    public ChatSpacePage(IChatSpaceBackend backend, ICuiSceneReadiness readiness, Guid? conversationId = null)
    {
        _controller = new ChatSpaceController(backend ?? throw new ArgumentNullException(nameof(backend)));
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
        _host = new CuiSceneHost();
        AutomationProperties.SetAutomationId(this, "ChatSpacePage");
        AutomationProperties.SetName(this, "Chat Space (CUI)");
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
        var init = InvokeOriginalSource(() => _controller.InitializeAsync(InitialConversationId, token));
        await original.AwaitAsync(init).ConfigureAwait(false);
        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
        {
            original.DemandPublication();
            _bindings.Refresh();
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
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                _bindings.Refresh();
            }).GetTask()).ConfigureAwait(false);
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
            case "chat.attach" when parameter is string path:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.AddAttachmentAsync(path, token))).ConfigureAwait(false);
                break;
            case "chat.remove-attachment" when parameter is Guid attachmentId:
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.RemoveAttachmentAsync(attachmentId, token))).ConfigureAwait(false);
                break;
            case "chat.send":
                await original.AwaitAsync(InvokeOriginalSource(() => _controller.SendAsync(token))).ConfigureAwait(false);
                break;
            case "chat.clear-search":
                // The recent-search input currently owns its own value through TrySetValue.
                // Clearing it here would require a two-way binding, which Select/Text do not
                // support for the search query. Kept as a no-op marker for the delta document.
                break;
            default:
                throw new InvalidOperationException($"Chat Space command '{command}' is not owned by this host.");
        }
    }

    private string? ResolveModelName(object? parameter)
    {
        if (parameter is ChatSpaceModelChoice choice) return choice.Name;
        var state = _controller.State;
        if (parameter is int index && index >= 0 && index < state.Models.Count)
            return state.Models[index].Name;
        if (parameter is string name && state.Models.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return name;
        return null;
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
