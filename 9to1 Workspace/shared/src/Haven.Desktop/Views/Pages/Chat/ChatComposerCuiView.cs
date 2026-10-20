using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.Prefabs;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>One authored composer chip (capability/Agent/Skill/resource or persisted attachment).</summary>
public sealed record ChatComposerChip(string Id, string Label, bool Invokable);

/// <summary>
/// The single shared canonical composer's data source. Each real consumer (the Haven Chat native
/// scene and the ChatSpace CUI host) supplies a live implementation; no composer state is cloned.
/// Action methods report the actual admitted operation through typed async callbacks so a caller
/// can join the real lifetime instead of swallowing it.
/// </summary>
public interface IChatComposerSource
{
    string Draft { get; set; }
    string Placeholder { get; }
    bool IsSending { get; }
    bool IsSafetyLocked { get; }
    bool HasAdd { get; }
    bool CanSend { get; }
    bool IsEditing { get; }
    IReadOnlyList<ChatComposerChip> Chips { get; }
    ValueTask Send();
    ValueTask Stop();
    ValueTask Add();
    ValueTask OpenSettings();
    ValueTask CloseSettings();
    ValueTask SetActionMode(string mode);
    ValueTask SetResponseMode(string mode);
    ValueTask InvokeChip(string id);
    ValueTask RemoveChip(string id);
    ValueTask FormatSelection(string command);
    ValueTask InsertEmoji(string emoji);
    ValueTask CancelEdit();
}

/// <summary>
/// Typed CUI binding/action adapter over the shared composer. It projects the current
/// <see cref="IChatComposerSource"/> into the composer document and forwards authored commands to
/// the source. Presentation-only state (settings/emoji panels, formatting expansion) lives here,
/// not in domain. Dispatching returns the real admitted source operation instead of a swallowed
/// completed task.
/// </summary>
public sealed class ChatComposerBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly IChatComposerSource _source;
    private bool _settingsOpen;
    private bool _emojiOpen;
    private string? _emojiQuery;
    private bool _focused;
    private bool _expanded;

    public ChatComposerBindings(IChatComposerSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Observed focus on the real composer input; drives compact/expanded formatting.</summary>
    internal bool IsFocused
    {
        get => _focused;
        set
        {
            if (_focused == value) return;
            _focused = value;
            if (value) _expanded = true;
            Refresh();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IChatComposerSource Source => _source;

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        switch (path)
        {
            case "Composer.Draft":
                value = _source.Draft;
                return true;
            case "Composer.Placeholder":
                value = _source.Placeholder;
                return true;
            case "Composer.HasChips":
                value = _source.Chips.Count > 0;
                return true;
            case "Composer.Chips":
                value = _source.Chips;
                return true;
            case "Composer.CanSend":
                value = _source.CanSend;
                return true;
            case "Composer.IsEditing":
                value = _source.IsEditing;
                return true;
            case "Composer.SendLabel":
                value = _source.IsEditing ? "Update" : "Send";
                return true;
            case "Composer.ShowFormatting":
                value = _expanded && (_focused || _settingsOpen || _emojiOpen);
                return true;
            case "Composer.ShowSettings":
                value = _settingsOpen;
                return true;
            case "Composer.ShowEmoji":
                value = _emojiOpen;
                return true;
            case "Composer.EmojiQuery":
                value = _emojiQuery ?? string.Empty;
                return true;
            case "Composer.EmojiEntries":
                value = FilteredEmoji();
                return true;
            default:
                return false;
        }
    }

    public bool TrySetValue(string path, object? value)
    {
        if (path == "Composer.Draft")
        {
            _source.Draft = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return true;
        }
        if (path == "Composer.EmojiQuery")
        {
            _emojiQuery = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            return true;
        }
        return false;
    }

    private IReadOnlyList<ComposerEmoji> FilteredEmoji()
    {
        var query = _emojiQuery?.Trim();
        if (string.IsNullOrEmpty(query)) return ComposerEmojiCatalogue.Entries;
        return ComposerEmojiCatalogue.Entries.Where(entry =>
            entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.Text.Contains(query, StringComparison.Ordinal)).ToArray();
    }

    public bool? IsActionAvailable(string command)
    {
        return command switch
        {
            "composer.send" => _source.CanSend,
            "composer.add" => _source.HasAdd,
            "composer.emoji" or "composer.emoji-toggle" or "composer.emoji-search" or "composer.emoji-insert" => true,
            "composer.settings" or "composer.set-action-mode" or "composer.set-response-mode" => true,
            _ => true
        };
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "composer.send":
                return _source.Send();
            case "composer.stop":
                return _source.Stop();
            case "composer.add":
                return _source.Add();
            case "composer.settings":
                _settingsOpen = !_settingsOpen;
                _emojiOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return _settingsOpen ? _source.OpenSettings() : _source.CloseSettings();
            case "composer.set-action-mode" when parameter is string mode:
                _settingsOpen = false;
                _emojiOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ApplyActionModeAsync(mode);
            case "composer.set-response-mode" when parameter is string mode:
                _settingsOpen = false;
                _emojiOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ApplyResponseModeAsync(mode);
            case "composer.invoke-chip" when parameter is string id:
                return _source.InvokeChip(id);
            case "composer.remove-chip" when parameter is string id:
                return _source.RemoveChip(id);
            case "composer.format" when parameter is string formatCommand:
                return _source.FormatSelection(formatCommand);
            case "composer.emoji" or "composer.emoji-toggle":
                _emojiOpen = !_emojiOpen;
                if (_emojiOpen) _expanded = true;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ValueTask.CompletedTask;
            case "composer.emoji-search" when parameter is string query:
                _emojiQuery = query ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ValueTask.CompletedTask;
            case "composer.emoji-insert" when parameter is string emoji:
                _emojiOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return _source.InsertEmoji(emoji);
            case "composer.cancel-edit":
                return _source.CancelEdit();
            default:
                return ValueTask.FromException(new InvalidOperationException($"Chat composer CUI command '{command}' is not owned by the shared composer."));
        }
    }

    private async ValueTask ApplyActionModeAsync(string mode)
    {
        await _source.SetActionMode(mode).ConfigureAwait(false);
        await _source.CloseSettings().ConfigureAwait(false);
    }

    private async ValueTask ApplyResponseModeAsync(string mode)
    {
        await _source.SetResponseMode(mode).ConfigureAwait(false);
        await _source.CloseSettings().ConfigureAwait(false);
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is ChatComposerChip chip)
        {
            value = path switch
            {
                "Id" => chip.Id,
                "Label" => chip.Label,
                "Invokable" => chip.Invokable,
                _ => null
            };
            return value is not null;
        }
        if (item is ComposerEmoji emoji)
        {
            value = path switch
            {
                "Text" => emoji.Text,
                "Name" => emoji.Name,
                _ => null
            };
            return value is not null;
        }
        return false;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

/// <summary>Immediate readiness for the shared composer scene (the host page owns deeper readiness).</summary>
internal sealed class ChatComposerReadiness : ICuiSceneReadiness
{
    public static readonly ChatComposerReadiness Instance = new();
    private ChatComposerReadiness() { }
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
        new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Chat composer is ready."));
}

/// <summary>
/// The single shared canonical Chat composer view. It owns the composer loader/scene host and the
/// typed presentation adapter, rendering <c>ChatComposer.cui</c>. Both real consumers mount this
/// same view: the newer ChatSpace page (through an authored CUI mount slot) and the Haven Chat
/// native scene (through a narrow native-control transport). It also exposes the typed composer
/// input port for the shared engine.
/// </summary>
public sealed class ChatComposerCuiView : ContentControl, IDisposable, IAsyncDisposable
{
    private readonly CuiSceneHost _host;
    private readonly ChatComposerBindings _bindings;
    private readonly CuiComposerInputPort _input;
    private readonly object _initGate = new();
    private Task<CuiSceneAvailability>? _initializeTask;
    /// <summary>The EXACT raw host Show task retained for loader settlement capture. This is a
    /// distinct identity from the public guarded initialization wrapper; only this raw task can
    /// be handed to <see cref="CuiSceneHost.CaptureOriginalActionSettlement"/>.</summary>
    private Task<CuiSceneAvailability>? _rawShowTask;
    private readonly object _closeGate = new();
    private Task? _closeTask;
    private bool _closing;
    private bool _disposed;

    public ChatComposerCuiView(IChatComposerSource source, string? owningAppId = null,
        CuiThemePreferenceContext? themePreference = null)
        : this(source, ChatComposerReadiness.Instance, owningAppId, themePreference)
    {
    }

    /// <summary>Readiness injection seam used by tests to hold/observe the real host show
    /// pipeline; defaults to production readiness. The SAME production host/loader/document/input
    /// are exercised, never an alternate renderer.</summary>
    internal ChatComposerCuiView(IChatComposerSource source, ICuiSceneReadiness readiness,
        string? owningAppId = null, CuiThemePreferenceContext? themePreference = null)
    {
        _bindings = new ChatComposerBindings(source ?? throw new ArgumentNullException(nameof(source)));
        _readiness = readiness ?? ChatComposerReadiness.Instance;
        var registry = new CuiControlRegistry();
        _host = new CuiSceneHost(registry);
        AutomationProperties.SetAutomationId(this, "ChatComposerCuiView");
        AutomationProperties.SetName(this, "Chat composer");
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Content = _host;
        _input = new CuiComposerInputPort(() => FindComposerTextBox(), focused => _bindings.IsFocused = focused);
        _owningAppId = owningAppId;
        _themePreference = themePreference;
        _ = InitializeAsync();
    }

    private readonly string? _owningAppId;
    private readonly CuiThemePreferenceContext? _themePreference;
    private readonly ICuiSceneReadiness _readiness;

    public IChatComposerInputPort Input => _input;
    public ChatComposerBindings Bindings => _bindings;
    public IChatComposerSource Source => _bindings.Source;

    /// <summary>Idempotent guarded initialization: the real scene show plus real input wiring.
    /// Faults/cancellation reach this same task; readiness is never claimed before the input is
    /// actually attached. Parents join it before publication/restoration.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initGate)
        {
            if (_initializeTask is null)
            {
                var scene = new CuiNativeScene(
                    _owningAppId ?? "spaces.chat.composer", "Chat Composer", "Chat",
                    ChatComposerCuiDocument.Load(), _bindings, _bindings, _readiness)
                {
                    ThemePreference = _themePreference,
                    IsPublicationCurrent = () => !_closing
                };
                // Retain the EXACT raw host Show task for loader settlement capture; the public
                // guarded initialization wrapper is a distinct task identity.
                var rawShow = _host.ShowAsync(scene, cancellationToken);
                _rawShowTask = rawShow;
                _initializeTask = ShowGuardedAsync(rawShow, cancellationToken);
            }
            return _initializeTask;
        }
    }

    private async Task<CuiSceneAvailability> ShowGuardedAsync(Task<CuiSceneAvailability> rawShow, CancellationToken cancellationToken)
    {
        var availability = await rawShow.ConfigureAwait(false);
        if (_closing) return availability;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_closing) AttachInputOnce();
        }).GetTask().ConfigureAwait(false);
        return availability;
    }

    /// <summary>
    /// Captures the ACTUAL loader action-settlement pipeline for actions admitted on this
    /// composer, using the exact original raw Show task. External callers join this snapshot
    /// outside any included action (an action must never await a snapshot containing itself).
    /// Requires UI-thread access, a successful guarded initialization/input attachment AND an
    /// exact retained raw host Show; raw Show ready alone does not mean the real input is wired,
    /// so capture is refused before that guarded attachment succeeds, after withdrawal/close, or
    /// when no retained raw Show exists.
    /// </summary>
    public Task CaptureOriginalActionSettlement()
    {
        Dispatcher.UIThread.VerifyAccess();
        Task<CuiSceneAvailability>? rawShow;
        Task<CuiSceneAvailability>? initTask;
        lock (_initGate)
        {
            rawShow = _rawShowTask;
            initTask = _initializeTask;
        }
        if (rawShow is null || _closing || initTask is null || !initTask.IsCompletedSuccessfully)
            throw new InvalidOperationException("The composer has no completed live initialization to capture from.");
        return _host.CaptureOriginalActionSettlement(rawShow);
    }

    /// <summary>Publishes one child close task BEFORE any close callback can reenter, mirroring the
    /// host start-gate pattern. The parent joins this exact task during cleanup; repeated close
    /// requests share the SAME task, and no duplicate work is admitted after it.</summary>
    public Task CloseAsync()
    {
        lock (_closeGate)
        {
            if (_closeTask is not null) return _closeTask;
            _closing = true;
            _input.Withdraw(); // producer retirement begins before any close callback reentry
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = CloseOriginalCoreAsync(start.Task);
            start.SetResult(); // original is published before the close body/callbacks run
            return _closeTask;
        }
    }

    private async Task CloseOriginalCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        // Withdraw producers/action admission first: start the host close independently of the
        // initialization, so a cancellation or fault in init can never skip withdrawal.
        var hostClose = _host.CloseOriginalAsync();
        var faults = new List<Exception>();

        // Join the complete actual initialization independently, retaining its fault.
        Task<CuiSceneAvailability>? initTask;
        lock (_initGate) initTask = _initializeTask;

        try { await hostClose.ConfigureAwait(false); }
        catch (Exception error) { faults.Add(error); }

        if (initTask is not null)
        {
            try { await initTask.ConfigureAwait(false); }
            catch (Exception error) { faults.Add(error); }
        }

        // Physical input watcher detach happens ONLY after both the host close and initialization
        // acknowledged (positive ACK). A faulted close is not approval: references stay owned for
        // the caller and no terminal Detach hides the failure. The release itself is marshaled to
        // the UI thread because these are real TextBox subscriptions/visual-tree operations.
        if (faults.Count == 0)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _input.Detach()).GetTask().ConfigureAwait(false);
        }

        if (faults.Count == 1) throw faults[0];
        if (faults.Count > 1) throw new AggregateException(faults);
    }

    private void AttachInputOnce()
    {
        var box = FindComposerTextBox();
        if (box is null) return;
        _input.Attach(box);
    }

    private TextBox? FindComposerTextBox()
    {
        if (!ReferenceEquals(Content, _host) || _host.Content is not Visual root) return null;
        foreach (var visual in root.GetVisualDescendants())
        {
            if (visual is TextBox box &&
                AutomationProperties.GetAutomationId(box) == "composer-input")
                return box;
        }
        return null;
    }

    /// <summary>Republishes host state into the composer scene (Send/Safety/chips/placeholder).</summary>
    public void RefreshSurface() => _bindings.Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { CloseAsync(); } catch { /* Best-effort synchronous teardown; joins observe faults. */ }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return new ValueTask(CloseAsync());
        _disposed = true;
        return new ValueTask(CloseAsync());
    }
}

/// <summary>
/// Adapter from the real CUI composer input (the rendered <see cref="TextBox"/>) to the typed
/// composer input port. Reads and writes target the actual canonical input; the input is resolved
/// lazily because the composer scene loads asynchronously. Named watchers are detached on
/// retirement and retained textbox references are cleared so no delayed attachment can publish
/// after close.
/// </summary>
internal sealed class CuiComposerInputPort : IChatComposerInputPort
{
    private readonly Func<TextBox?> _resolve;
    private readonly Action<bool>? _onFocusChanged;
    private TextBox? _attached;
    private string? _pendingText;
    private bool _released;
    private volatile bool _withdrawn;
    private EventHandler<TextChangingEventArgs>? _textChangingHandler;
    private EventHandler<RoutedEventArgs>? _gotFocusHandler;
    private EventHandler<RoutedEventArgs>? _lostFocusHandler;

    public CuiComposerInputPort(Func<TextBox?> resolve, Action<bool>? onFocusChanged = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _onFocusChanged = onFocusChanged;
    }

    /// <summary>Attaches the real input. The first attach keeps the input live; a later resolved
    /// input replaces watchers without releasing the port. After terminal <see cref="Detach"/>
    /// retirement or retirement onset (<see cref="Withdraw"/>) any late attach is refused.</summary>
    internal void Attach(TextBox box)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_released || _withdrawn) return;
        if (ReferenceEquals(box, _attached)) return;
        ReplaceWatchers(box);
    }

    /// <summary>
    /// Generic producer-retirement gate, separate from terminal physical <see cref="Detach"/>.
    /// Called when close begins: it stops any late attach, text/selection/caret/focus mutation and
    /// watcher/focus producer callbacks from reaching the real TextBox, while RETAINING the retained
    /// input/watcher/mapping custody so failed/pending close can be recovered by the owning caller.
    /// Terminal unsubscribe and reference clearing only ever happen later on a successful ACK via
    /// <see cref="Detach"/>.
    /// </summary>
    internal void Withdraw()
    {
        // This gate may be set by an external close owner on another thread. Keep all physical
        // watcher/input custody until the acknowledged UI-thread Detach below. Removing a handler
        // here would neither stop its already-running invocation nor preserve UI-thread ownership.
        _withdrawn = true;
    }

    private void ReplaceWatchers(TextBox box)
    {
        if (_attached is { } previous && !ReferenceEquals(previous, box))
        {
            if (_textChangingHandler is not null) previous.TextChanging -= _textChangingHandler;
            if (_gotFocusHandler is not null) previous.GotFocus -= _gotFocusHandler;
            if (_lostFocusHandler is not null) previous.LostFocus -= _lostFocusHandler;
        }
        _attached = box;
        if (_pendingText is not null)
        {
            box.Text = _pendingText;
            _pendingText = null;
        }
        // The fork's TextBox raises TextChanging SYNCHRONOUSLY after the Text property is set
        // (rendering not required), while TextChanged is posted to the dispatcher and only runs
        // later. An edit followed immediately by the queue/send clear must be observed now, so
        // the real value is captured before the clear: this is the actual edit signal.
        _textChangingHandler = (_, _) =>
        {
            NotifyLiveInputSubscribers(TextChanged);
            NotifyLiveInputSubscribers(Invalidated);
        };
        _gotFocusHandler = (_, _) => { if (!_released && !_withdrawn) _onFocusChanged?.Invoke(true); };
        _lostFocusHandler = (_, _) => { if (!_released && !_withdrawn) _onFocusChanged?.Invoke(false); };
        box.TextChanging += _textChangingHandler;
        box.GotFocus += _gotFocusHandler;
        box.LostFocus += _lostFocusHandler;
    }

    private void NotifyLiveInputSubscribers(EventHandler? subscribers)
    {
        if (subscribers is null) return;
        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            // A subscriber may synchronously close the owner. Recheck before EACH subsequent
            // callback, including the transition from TextChanged to Invalidated.
            if (_released || _withdrawn) return;
            subscriber(this, EventArgs.Empty);
        }
    }

    /// <summary>Terminal release: detaches every named watcher, clears retained textbox
    /// references and refuses any later attach. Used only on retirement/close.</summary>
    internal void Detach()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_attached is { } box)
        {
            if (_textChangingHandler is not null) box.TextChanging -= _textChangingHandler;
            if (_gotFocusHandler is not null) box.GotFocus -= _gotFocusHandler;
            if (_lostFocusHandler is not null) box.LostFocus -= _lostFocusHandler;
        }
        _textChangingHandler = null;
        _gotFocusHandler = null;
        _lostFocusHandler = null;
        _attached = null;
        _pendingText = null;
        _released = true;
    }

    private TextBox? Current
    {
        get
        {
            if (_released) return null;
            if (_attached is not null) return _attached; // retained for reads/recovery after withdrawal
            if (_withdrawn) return null;                  // no late attach after retirement onset
            var box = _resolve();
            if (box is not null) Attach(box);
            return box;
        }
    }

    public string Text
    {
        get
        {
            if (_released) return string.Empty;
            return Current?.Text ?? _pendingText ?? string.Empty;
        }
        set
        {
            // After retirement onset or terminal release no delayed write may reach the real box.
            if (_released || _withdrawn) return;
            if (Current is { } box) box.Text = value ?? string.Empty;
            else _pendingText = value ?? string.Empty;
        }
    }

    public int CaretIndex => Current?.CaretIndex ?? 0;
    public int SelectionStart => Current?.SelectionStart ?? 0;
    public int SelectionLength
    {
        get
        {
            if (Current is not { } box) return 0;
            return Math.Max(0, box.SelectionEnd - box.SelectionStart);
        }
    }
    public string SelectedText => Current?.SelectedText ?? string.Empty;
    public bool IsFocused => Current?.IsKeyboardFocusWithin ?? false;

    public event EventHandler? TextChanged;
    public event EventHandler? Invalidated;

    public void PlaceCaretAtEnd(bool extendSelection = false)
    {
        if (_released || _withdrawn) return;
        if (Current is not { } box) return;
        var length = box.Text?.Length ?? 0;
        if (extendSelection)
        {
            box.SelectionEnd = length;
        }
        else
        {
            box.CaretIndex = length;
            box.SelectionStart = length;
            box.SelectionEnd = length;
        }
    }

    public void SetSelection(int anchor, int caret)
    {
        if (_released || _withdrawn) return;
        if (Current is not { } box) return;
        var length = box.Text?.Length ?? 0;
        var start = Math.Clamp(anchor, 0, length);
        var end = Math.Clamp(caret, 0, length);
        // Normalize to the bounded selection region so SelectionLength stays non-negative and
        // deterministic regardless of the control's own clamping/clamping ordering.
        box.SelectionStart = Math.Min(start, end);
        box.SelectionEnd = Math.Max(start, end);
    }

    public void ClearSelection()
    {
        if (_released || _withdrawn) return;
        if (Current is not { } box) return;
        var caret = Math.Clamp(box.CaretIndex, 0, box.Text?.Length ?? 0);
        box.SelectionStart = caret;
        box.SelectionEnd = caret;
    }

    public void Focus()
    {
        if (_released || _withdrawn) return;
        Current?.Focus();
    }
}
