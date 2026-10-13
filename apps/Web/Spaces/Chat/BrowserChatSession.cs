using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using HavenOS.Home.Core;
using NineToOne.Web.Spaces.Storage;

namespace NineToOne.Web.Spaces.Chat;

/// <summary>Presentation of the canonical Chat controller in the shared CUI shell.
/// This owner grants no account, provider, attachment or tool authority.</summary>
public sealed partial class BrowserChatSession : IBrowserSpacesPresentationSession
{
    private readonly Func<IChatSpaceBackend> _backend;
    private readonly Action _demand;
    private readonly Func<Action, Task> _present;
    private readonly Action<HomeFeatureNavigationRequest> _navigate;
    private readonly Func<CancellationToken, Task<string?>>? _selectFile;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _work = [];
    private readonly SemaphoreSlim _serial = new(1, 1);
    private ChatSpaceController? _controller;
    private ChatSpaceViewState _state = ChatSpaceViewState.Empty;
    private CancellationTokenSource? _sending;
    private Task? _close;
    private bool _disposed, _admitted, _available, _busy, _dirty, _compact, _sidebar = true, _desktopSidebar = true;
    private string _status = "Connecting Chat services…", _query = "";
    private bool _catalog, _models, _recovery;
    private Control? _root;
    private Task? _autoSave;
    private long _draftRevision;
    private string? _saveError;

    public BrowserChatSession(Func<IChatSpaceBackend> backend, Action demandCurrent, Func<Action, Task> present,
        Action<HomeFeatureNavigationRequest> navigate, Func<CancellationToken, Task<string?>>? selectFile = null)
    { _backend = backend; _demand = demandCurrent; _present = present; _navigate = navigate; _selectFile = selectFile; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool HasUnsavedChanges => !_disposed && (_dirty || _busy || _state.Composer.IsSending);
    public Task PrepareAsync(CancellationToken cancellationToken) => Track(async () =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _serial.WaitAsync(linked.Token);
        try
        {
            _demand();
            if (_controller is null)
            {
                _controller = new(_backend());
                _controller.StateChanged += OnStateChanged;
            }
            if ((_dirty || _state.Composer.IsSending) && !_controller.RequiresTurnReconciliation) return;
            await _controller.InitializeAsync(_state.Conversation?.Id, linked.Token);
            await PublishAsync(() =>
            {
                _state = _controller.State;
                _available = !_state.IsLoading && _state.Status.Tone != ChatSpaceStatusTone.Error;
                _status = _state.Status.Message;
            }, linked.Token);
        }
        catch (Exception error) when (IsPrivateContextUnavailable(error))
        {
            await PublishAsync(() => { _available = false; _status = "Sign in using the account control to open your private chats."; }, linked.Token);
        }
        finally { _serial.Release(); }
    });

    private static bool IsPrivateContextUnavailable(Exception error) => error switch
    {
        BrowserTaskContextUnavailableException => true,
        AggregateException { InnerExceptions.Count: > 0 } aggregate =>
            aggregate.InnerExceptions.All(IsPrivateContextUnavailable),
        _ => false
    };

    public void Admit() { _demand(); if (_disposed) throw new ObjectDisposedException(nameof(BrowserChatSession)); _admitted = true; Changed(); }
    public void Deactivate() { _admitted = false; _root = null; }
    public void AttachRoot(Control root) => _root = root;
    public void SetViewportWidth(double width)
    {
        if (_disposed || width <= 0 || !double.IsFinite(width)) return;
        var compact = width < 860;
        if (_compact == compact) return;
        _compact = compact; _sidebar = compact ? false : _desktopSidebar; Changed();
    }
    private void OnStateChanged(ChatSpaceViewState state)
    {
        // UI notifications themselves are original tracked work, so account retirement
        // joins them and synchronous revocation hides all private values first.
        _ = Track(() => PublishAsync(() => { if (state.Revision >= _state.Revision) { _state = state; _status = state.Status.Message; } }, _lifetime.Token));
    }
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
    private Task PublishAsync(Action action, CancellationToken token) => _present(() =>
    {
        if (_disposed || token.IsCancellationRequested) return;
        _demand(); action(); Changed();
    });

    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || !_admitted) return false;
        _demand();
        if (path == "composer.text" && value is string draft && CanEdit)
        {
            _dirty = true; _draftRevision++; _controller!.UpdateDraft(draft); _state = _controller.State;
            Changed(); StartDraftSave(); return true;
        }
        if (path == "sidebar.query" && value is string query) { _query = query; Changed(); return true; }
        return false;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    private bool CanAct => !_disposed && _admitted;
    private bool CanEdit => CanAct && _available && !_busy && !_state.Composer.IsSending;
    private bool HasIssues => _controller is not null && (_saveError is not null || _controller.RequiresTurnReconciliation ||
        _state.Status.Tone == ChatSpaceStatusTone.Error || (_available && _state.Models.Count == 0));

    private void StartDraftSave()
    {
        if (_autoSave is { IsCompleted: false }) return;
        _autoSave = Track(async () =>
        {
            Task? originalOperation = null;
            try
            {
                while (!_disposed && _dirty)
                {
                    var revision = _draftRevision;
                    originalOperation = Task.Delay(250, _lifetime.Token);
                    await originalOperation;
                    if (revision != _draftRevision) continue;
                    originalOperation = _serial.WaitAsync(_lifetime.Token);
                    await originalOperation;
                    try
                    {
                        if (_disposed) return;
                        _demand(); _lifetime.Token.ThrowIfCancellationRequested();
                        if (!_dirty || _controller is null || _controller.RequiresTurnReconciliation) return;
                        revision = _draftRevision;
                        originalOperation = _controller.SaveDraftAsync(_lifetime.Token);
                        await originalOperation;
                        originalOperation = PublishAsync(() =>
                        {
                            if (revision == _draftRevision) { _dirty = false; _saveError = null; }
                        }, _lifetime.Token);
                        await originalOperation;
                    }
                    finally { _serial.Release(); }
                }
            }
            catch (Exception error)
            {
                if (error is OperationCanceledException cancelled && _lifetime.IsCancellationRequested &&
                    cancelled.CancellationToken == _lifetime.Token && originalOperation?.IsCanceled == true) throw;
                // A foreign OCE is an original failure, not proof that this owner
                // requested cancellation. Keep it faulted across this async boundary.
                Exception failure = originalOperation?.Exception is { } original
                    ? original.InnerExceptions.Count == 1 ? original.InnerExceptions[0] : original : error;
                if (failure is OperationCanceledException)
                    failure = new AggregateException("The original draft operation failed with an unrelated cancellation.", failure);
                try { await PublishAsync(() => _saveError = "The draft could not be saved: " + error.Message, _lifetime.Token); }
                catch (Exception presentationFailure)
                { throw new AggregateException("The original draft save and its error presentation both failed.", failure, presentationFailure); }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        });
    }
    public bool? IsActionAvailable(string command) => CanAct && (command switch
    {
        "nav.manage-spaces" or "shell.account" or "shell.home" or "nav.toggle-sidebar" => true,
        "chat.refresh" => !_busy && !_state.Composer.IsSending,
        "nav.open-space" => true, // exact destination is checked by dispatch
        "nav.new-chat" or "nav.open-entry" => CanEdit && _controller?.RequiresTurnReconciliation != true,
        "chat.send" => CanEdit && _state.Composer.CanSend && _controller?.RequiresTurnReconciliation != true,
        "chat.stop" => _sending is not null,
        "chat.choose-model" or "shell.choose-model" => CanEdit && _state.Models.Count > 0,
        "chat.add" => CanEdit,
        "chat.attach-files" => CanEdit && _selectFile is not null,
        "chat.close-catalog" or "chat.cancel-dialog" => true,
        "chat.choose-dialog" => (CanEdit && _models) || (_recovery && !_busy && !_state.Composer.IsSending),
        "chat.message-copy" => _state.Conversation is not null && _root is not null,
        "chat.message-branch" => CanEdit && _controller?.RequiresTurnReconciliation != true,
        "chat.remove-attachment" => CanEdit,
        "chat.resolve" => HasIssues && !_busy && !_state.Composer.IsSending,
        _ => false
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        new(DispatchOwnedAsync(command, parameter, cancellationToken, default));
    public ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime) =>
        new(DispatchOwnedAsync(command, parameter, lifetime.CallerCancellation, lifetime.ViewCancellation));

    private Task DispatchOwnedAsync(string command, object? parameter, CancellationToken caller, CancellationToken view) => Track(async () =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, view, _lifetime.Token);
        var token = linked.Token;
        _demand(); token.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Chat action is unavailable in the current state.");
        if (command == "chat.stop") { _sending?.Cancel(); return; }
        if (command == "nav.toggle-sidebar") { _sidebar = !_sidebar; if (!_compact) _desktopSidebar = _sidebar; Changed(); return; }
        if (command == "nav.open-space")
        {
            if (ReadId(parameter) != SpaceRegistry.ChatSpaceId) throw new InvalidOperationException("This Space is not yet connected in this browser.");
            return;
        }
        if (command == "chat.add") { _catalog = !_catalog; Changed(); return; }
        if (command == "chat.close-catalog") { _catalog = false; Changed(); return; }
        if (command is "chat.choose-model" or "shell.choose-model") { _models = true; _recovery = false; Changed(); return; }
        if (command == "chat.cancel-dialog") { _models = _recovery = false; Changed(); return; }
        if (command == "chat.resolve") { _recovery = true; _models = false; Changed(); return; }
        if (command == "chat.choose-dialog" && _models)
        { _controller!.SelectModel(parameter?.ToString() ?? ""); _models = false; Changed(); return; }
        if (command == "chat.choose-dialog" && _recovery)
        {
            if (parameter?.ToString() != "refresh") throw new InvalidOperationException("Choose an available recovery action.");
            command = "chat.refresh";
        }
        if (command == "chat.message-copy")
        {
            var message = RequireMessage(parameter);
            var clipboard = _root is { } root ? TopLevel.GetTopLevel(root)?.Clipboard : null;
            if (clipboard is null) throw new InvalidOperationException("Clipboard is unavailable in this browser.");
            await clipboard.SetTextAsync(message.Content); return;
        }
        await _serial.WaitAsync(token);
        Exception? commandFailure = null;
        try
        {
            _demand(); token.ThrowIfCancellationRequested();
            if (!CanEdit && command is not ("shell.account" or "shell.home" or "nav.manage-spaces" or "chat.refresh"))
                throw new InvalidOperationException("The Chat changed before this action could start.");
            if (_busy || _state.Composer.IsSending) throw new InvalidOperationException("Wait for the current Chat operation to finish.");
            _busy = true; Changed();
            switch (command)
            {
                case "chat.refresh":
                    if (_controller is null)
                    {
                        _controller = new(_backend());
                        _controller.StateChanged += OnStateChanged;
                    }
                    // Reconcile the original turn before touching its saved draft. This
                    // operation retries reads/acknowledgements, never provider execution.
                    if (_dirty && !_controller.RequiresTurnReconciliation) await SaveDraftAsync(token);
                    await _controller.InitializeAsync(_state.Conversation?.Id, token);
                    await PublishAsync(() =>
                    {
                        _state = _controller.State;
                        _available = !_state.IsLoading && _state.Status.Tone != ChatSpaceStatusTone.Error;
                        _status = _state.Status.Message;
                        _dirty = false;
                        _saveError = null;
                        _recovery = false;
                    }, token);
                    break;
                case "shell.account":
                case "shell.home":
                case "nav.manage-spaces":
                    if (_dirty) await SaveDraftAsync(token);
                    await PublishAsync(() => _navigate(command == "shell.account" ? new(HomeFeatureRouteIds.Settings)
                        : command == "shell.home" ? new(HomeFeatureRouteIds.Dashboard)
                        : new(HomeFeatureRouteIds.Spaces, Action: "manage")), token);
                    break;
                case "nav.new-chat":
                    if (_dirty) await SaveDraftAsync(token);
                    await _controller!.NewChatAsync(token); break;
                case "nav.open-entry":
                    if (_dirty) await SaveDraftAsync(token);
                    var id = ReadId(parameter);
                    if (!_state.RecentChats.Any(chat => chat.Id == id)) throw new InvalidOperationException("That chat is no longer in the current list.");
                    await _controller!.OpenChatAsync(id, token); break;
                case "chat.attach-files":
                    var selected = await _selectFile!(token);
                    if (selected is not null) { await _controller!.AddAttachmentAsync(selected, token); _dirty = false; _saveError = null; }
                    _catalog = false; break;
                case "chat.remove-attachment":
                    await _controller!.RemoveAttachmentAsync(ReadId(parameter), token); _dirty = false; _saveError = null; break;
                case "chat.message-branch": await _controller!.BranchAsync(RequireMessage(parameter).Id, cancellationToken: token); break;
                case "chat.message-regenerate": await SendAsync(() => _controller!.RegenerateAsync(RequireMessage(parameter).Id, _sending!.Token), token); break;
                case "chat.send": await SendAsync(() => _controller!.SendAsync(_sending!.Token), token); break;
            }
        }
        catch (Exception error) { commandFailure = error; throw; }
        finally
        {
            _busy = false;
            try { Changed(); }
            catch (Exception presentationFailure) when (commandFailure is not null)
            { throw new AggregateException("The original Chat action and its presentation both failed.", commandFailure, presentationFailure); }
            finally { _serial.Release(); }
        }
    });
    private async Task SaveDraftAsync(CancellationToken token)
    {
        var draft = _controller!.State.Composer;
        await _controller.SaveDraftAsync(token);
        _demand(); token.ThrowIfCancellationRequested();
        if (_controller.State.Composer.Draft != draft.Draft ||
            !_controller.State.Composer.Attachments.Select(item => item.Id).SequenceEqual(draft.Attachments.Select(item => item.Id)))
            throw new InvalidOperationException("The draft changed while it was being saved. Save again before leaving.");
        _dirty = false;
        _saveError = null;
    }
    private async Task SendAsync(Func<Task> send, CancellationToken token)
    {
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(token);
        _sending = sending;
        try
        {
            await send();
            _demand(); token.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(BrowserChatSession));
            _state = _controller!.State;
            _dirty = _state.Composer.Draft.Length > 0 || _state.Composer.Attachments.Count > 0;
        }
        finally { _sending = null; }
    }
    private ChatSpaceMessage RequireMessage(object? value)
    {
        var id = ReadId(value);
        return _state.Conversation?.Messages.SingleOrDefault(message => message.Id == id)
            ?? throw new InvalidOperationException("That message is no longer in the current chat.");
    }
    private static Guid ReadId(object? value) => Guid.TryParse(value?.ToString(), out var id) && id != Guid.Empty
        ? id : throw new ArgumentException("Choose an item from the current view.");
    private Task Track(Func<Task> source)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) { if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(BrowserChatSession))); _work.Add(completion.Task); }
        _ = SettleAsync(source, completion); return completion.Task;
    }
    private async Task SettleAsync(Func<Task> source, TaskCompletionSource completion)
    {
        Task? actual = null;
        try { actual = source(); await actual; completion.TrySetResult(); }
        catch (OperationCanceledException error) when (actual?.IsCanceled == true) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { if (actual?.Exception is { } failure) completion.TrySetException(failure.InnerExceptions); else completion.TrySetException(error); }
        finally { lock (_gate) if (completion.Task.IsCompletedSuccessfully) _work.Remove(completion.Task); }
    }
    public void RevokePrivateContext()
    {
        lock (_gate)
        {
            _disposed = true; _admitted = _available = false;
            if (_controller is not null) _controller.StateChanged -= OnStateChanged;
            _state = ChatSpaceViewState.Empty; _root = null; _query = ""; _catalog = _models = _recovery = false;
            _saveError = null;
            _status = "Chat is closed.";
        }
    }
    public void Dispose() { RevokePrivateContext(); _ = DisposeAsync(); }
    public ValueTask DisposeAsync()
    {
        RevokePrivateContext();
        TaskCompletionSource completion; Task[] work;
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = completion.Task; work = _work.ToArray();
        }
        _ = DrainAsync(work, completion); return new(completion.Task);
    }
    private async Task DrainAsync(Task[] work, TaskCompletionSource completion)
    {
        try
        {
            await BrowserSpacesDrain.CompleteAsync(new Func<Task>[] { () => { _lifetime.Cancel(); return Task.CompletedTask; } }
                .Concat(work.Select(task => (Func<Task>)(() => task))));
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
        finally { _controller = null; _state = ChatSpaceViewState.Empty; _lifetime.Dispose(); _serial.Dispose(); }
    }
}
