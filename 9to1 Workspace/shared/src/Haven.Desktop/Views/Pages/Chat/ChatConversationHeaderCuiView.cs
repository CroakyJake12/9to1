using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Read-only presentation over the current conversation. Actions carry an issued
/// context token and Temporary returns the original owner's task; this is not another chat store.</summary>
internal sealed class ChatConversationHeaderBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged
{
    private sealed record IssuedTarget(object Context);
    private IssuedTarget _target = new(new object());
    private string _title = "New chat", _model = "No model selected", _notice = "";
    private bool _temporaryAvailable, _temporary, _busy, _retired, _narrow = true, _showLatest;
    private string? _panel;
    private ToolActivity[] _activitySource = [];
    private IReadOnlyList<Dictionary<string, object?>> _activities = [], _summaries = [];
    internal Func<object, bool>? IsCurrentContext { get; set; }
    internal Func<object, ValueTask>? ToggleTemporary { get; set; }
    internal Action? ReturnToLatest { get; set; }
    internal event EventHandler<string>? RestoreFocusRequested;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal bool Temporary => _temporary;

    internal void UpdateConversation(object context, string? title, string? model)
    {
        var changed = !Equals(_target.Context, context);
        if (changed)
        {
            _target = new(context);
            _panel = null;
            _activities = []; _summaries = [];
            _activitySource = [];
            _notice = "";
        }
        var nextTitle = string.IsNullOrWhiteSpace(title) ? "New chat" : title;
        var nextModel = string.IsNullOrWhiteSpace(model) ? "No model selected" : "Model: " + model;
        if (!changed && _title == nextTitle && _model == nextModel) return;
        _title = nextTitle; _model = nextModel;
        Refresh();
    }

    internal void SetTemporary(bool available, bool current)
    {
        if (_temporaryAvailable == available && _temporary == current) return;
        _temporaryAvailable = available; _temporary = current;
        Refresh();
    }

    internal void SetActivities(IEnumerable<ToolActivity> activities)
    {
        var actual = activities.OrderBy(item => item.Timestamp).ToArray();
        if (_activitySource.SequenceEqual(actual)) return;
        _activitySource = actual;
        _activities = actual.Select(item => new Dictionary<string, object?>
        {
            ["Id"] = item.Id, ["Title"] = item.Title, ["Detail"] = item.Detail,
            ["Meta"] = (item.Succeeded ? "Completed" : "Failed") +
                (item.Duration > TimeSpan.Zero ? $" · {item.Duration.TotalSeconds:0.#}s" : "") +
                (item.LinesAdded != 0 || item.LinesRemoved != 0 ? $" · +{item.LinesAdded} −{item.LinesRemoved}" : "")
        }).ToArray();
        Refresh();
    }

    internal void SetSummaries(IEnumerable<ConversationContextEntry> entries)
    {
        _summaries = entries.Where(item => item.Kind == ContextEntryKind.CompactSummary)
            .Select(item => new Dictionary<string, object?>
            { ["Id"] = item.Id, ["Title"] = item.Title, ["Content"] = item.Content }).ToArray();
        Refresh();
    }

    internal void SetReadingEarlier(bool value)
    {
        if (_showLatest == value) return;
        _showLatest = value; Refresh();
    }

    internal void SetWidth(double width)
    {
        var narrow = width < 640;
        if (_narrow == narrow) return;
        _narrow = narrow; Refresh();
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Header.Target" => _target,
            "Header.Title" => _title, "Header.Model" => _model,
            "Header.Columns" => _narrow ? "1fr" : "1fr Auto",
            "Header.Rows" => _narrow ? "Auto Auto" : "Auto",
            "Header.ControlsColumn" => _narrow ? 0 : 1,
            "Header.ControlsRow" => _narrow ? 1 : 0,
            "Header.TemporaryAvailable" => _temporaryAvailable,
            "Header.Temporary" => _temporary,
            "Header.CanToggleTemporary" => !_retired && !_busy && _temporaryAvailable && ToggleTemporary is not null,
            "Header.TemporaryAccessibleName" => _temporary ? "Temporary Chat, on. Select to save locally." : "Temporary Chat, off. Select to turn on for a new chat.",
            "Header.WorkVisible" => _panel == "work", "Header.SummaryVisible" => _panel == "summary",
            "Header.PanelVisible" => _panel is not null,
            "Header.PanelTitle" => _panel == "work" ? "Recorded work" : "Saved context summaries",
            "Header.Activities" => _activities, "Header.NoActivities" => _activities.Count == 0,
            "Header.Summaries" => _summaries, "Header.NoSummaries" => _summaries.Count == 0,
            "Header.HasSummaries" => _summaries.Count > 0,
            "Header.ShowLatest" => _showLatest,
            "Header.Notice" => _notice, "Header.HasNotice" => _notice.Length > 0,
            _ => null
        };
        return value is not null;
    }

    public bool TrySetValue(string path, object? value) => false;
    public bool? IsActionAvailable(string command) => !_retired && (command switch
    {
        "chat.header.temporary" => !_busy && _temporaryAvailable && ToggleTemporary is not null,
        "chat.header.work" or "chat.header.summary" or "chat.header.close-panel" => true,
        "chat.header.latest" => _showLatest && ReturnToLatest is not null,
        _ => false
    });

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (_retired) throw new InvalidOperationException("This conversation header has retired.");
        if (parameter is not IssuedTarget issued || !ReferenceEquals(issued, _target) ||
            IsCurrentContext?.Invoke(issued.Context) == false)
        {
            _notice = "The conversation changed. Use its current controls.";
            Refresh();
            return; // Proven pre-effect refusal; no other conversation can acquire this action.
        }
        switch (command)
        {
            case "chat.header.temporary" when !_busy && _temporaryAvailable && ToggleTemporary is { } toggle:
                _busy = true; _notice = ""; Refresh();
                try { await toggle(issued.Context); }
                finally { _busy = false; Refresh(); }
                break;
            case "chat.header.work": _panel = _panel == "work" ? null : "work"; Refresh(); break;
            case "chat.header.summary": _panel = _panel == "summary" ? null : "summary"; Refresh(); break;
            case "chat.header.close-panel":
                var previous = _panel;
                _panel = null; Refresh();
                if (previous is not null) RestoreFocusRequested?.Invoke(this, previous);
                break;
            case "chat.header.latest" when ReturnToLatest is { } latest: latest(); break;
            default: throw new InvalidOperationException("This conversation control is unavailable.");
        }
    }

    internal void Retire() { _retired = true; Refresh(); }
    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

/// <summary>Authored CUI child of the retained native transcript. Close joins its actual
/// initialization and admitted actions before its parent releases the native mapping.</summary>
internal sealed class ChatConversationHeaderCuiView : ContentControl, IDisposable, IAsyncDisposable
{
    private sealed class HeaderReadiness : ICuiSceneReadiness
    {
        internal static readonly HeaderReadiness Instance = new();
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Conversation controls are ready."));
    }
    private readonly ChatConversationHeaderBindings _bindings;
    private readonly CuiSceneHost _host;
    private readonly object _gate = new();
    private Task<CuiSceneAvailability>? _rawShow;
    private Task? _initialization, _close;
    private bool _closing, _correctingToggle;
    private ToggleSwitch? _temporarySwitch;

    internal ChatConversationHeaderCuiView(ChatConversationHeaderBindings bindings)
    {
        _bindings = bindings;
        var registry = new CuiControlRegistry();
        // The typed native seam retains this authored switch even while the
        // availability binding removes its cell from the visible layout.
        registry.BindAuthoredControl("chat-conversation-temporary", _ => _temporarySwitch = new ToggleSwitch());
        _host = new CuiSceneHost(registry);
        Content = _host;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "ChatConversationHeaderCuiView");
        AutomationProperties.SetName(this, "Conversation controls");
        SizeChanged += OnSizeChanged;
        _bindings.RestoreFocusRequested += OnRestoreFocus;
    }

    internal Task InitializeAsync()
    {
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            using var stream = typeof(ChatConversationHeaderCuiView).Assembly.GetManifestResourceStream("Haven.Desktop.Chat.ConversationHeader.cui")
                ?? throw new InvalidDataException("The authored conversation header is missing.");
            using var reader = new StreamReader(stream);
            var parser = new CuiRichParser();
            var document = parser.Parse(reader.ReadToEnd(), "ChatConversationHeader.cui");
            if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The authored conversation header is invalid.");
            var scene = new CuiNativeScene("spaces.chat.conversation-header", "Conversation", "Chat",
                document, _bindings, _bindings, HeaderReadiness.Instance) { IsPublicationCurrent = () => !_closing };
            _rawShow = _host.ShowAsync(scene);
            return _initialization = FinishInitializationAsync(_rawShow);
        }
    }

    private async Task FinishInitializationAsync(Task<CuiSceneAvailability> actualShow)
    {
        await actualShow.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_closing) return;
            if (_temporarySwitch is null)
                throw new InvalidDataException("The authored Temporary switch was not created.");
            // Native toggles normally flip before Click. Temporary must display the observed
            // accepted state, including while local-save confirmation is pending or cancelled.
            _temporarySwitch.IsCheckedChanged += OnTemporaryNativeChange;
            ApplyWidth();
        }).GetTask().ConfigureAwait(false);
    }

    private void OnTemporaryNativeChange(object? sender, EventArgs args)
    {
        if (_correctingToggle || _temporarySwitch is null || _temporarySwitch.IsChecked == _bindings.Temporary) return;
        _correctingToggle = true;
        try { _temporarySwitch.IsChecked = _bindings.Temporary; }
        finally { _correctingToggle = false; }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args) => ApplyWidth();
    private void ApplyWidth() => _bindings.SetWidth(CuiInterfaceScale.LogicalWidth(Bounds.Width, CuiInterfaceScale.For(_host)));
    private void OnRestoreFocus(object? sender, string panel) => this.GetVisualDescendants().OfType<Button>()
        .FirstOrDefault(item => item.Name == "chat-conversation-" + panel)?.Focus();

    internal Task CaptureOriginalActionSettlement()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_rawShow is null || _initialization?.IsCompletedSuccessfully != true || _closing)
            throw new InvalidOperationException("The current conversation header has no initialized action owner.");
        return _host.CaptureOriginalActionSettlement(_rawShow);
    }

    internal Task CloseAsync()
    {
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseCoreAsync(start.Task);
            start.SetResult();
            return _close;
        }
    }

    private async Task CloseCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _bindings.Retire();
                SizeChanged -= OnSizeChanged;
                _bindings.RestoreFocusRequested -= OnRestoreFocus;
                if (_temporarySwitch is not null) _temporarySwitch.IsCheckedChanged -= OnTemporaryNativeChange;
            }).GetTask().ConfigureAwait(false);
        }
        catch (Exception failure) { failures.Add(failure); }
        // Start close even when initialization failed; neither failure may skip child retirement.
        try { await _host.CloseOriginalAsync().ConfigureAwait(false); }
        catch (Exception failure) { failures.Add(failure); }
        if (_initialization is { } initialization)
            try { await initialization.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    public void Dispose() => _ = CloseAsync();
    public ValueTask DisposeAsync() => new(CloseAsync());
}
