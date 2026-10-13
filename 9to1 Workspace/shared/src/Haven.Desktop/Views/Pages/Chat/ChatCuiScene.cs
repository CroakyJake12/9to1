using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using HavenOS.Apps.Spaces.Chat.Rendering;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>The canonical CUI presentation of the SAME NewChatPage conversation.
/// Owns only presentation originals, never a provider, persisted chat, account or permission.</summary>
internal sealed partial class ChatCuiScene : ICuiWritableBindingContext, ICuiRepeatItemBindingContext,
    ICuiActionDispatcher, INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private readonly ChatCuiActions _actions;
    private readonly CuiControlLoader _loader;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private readonly IDisposable _composerKeys;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, ChatSceneMessage> _messages = [];
    private readonly List<Guid> _messageOrder = [];
    private readonly Dictionary<Guid, IReadOnlyList<HavenElement>> _generatedContent = [];
    private readonly HashSet<ChatCuiGeneratedChildren> _generatedPresentations = [];
    private readonly Dictionary<string, Func<Task>> _dialogChoices = [];
    private Func<string, Task>? _dialogConfirm;
    private long _dialogRevision;
    private bool _compactHeader;
    private bool _sending, _modelAvailable, _safetyLocked, _dialogBusy, _disposed;
    private Control? _dialogReturnFocus;
    private bool _isContextExpanded, _contextDismissed;
    private IReadOnlyList<ChatSceneContextEntry> _contextEntries = [];
    private IReadOnlyList<ChatAttachmentChip> _attachmentChips = [];

    public ChatCuiScene(ChatCuiActions actions, IHavenAvaloniaNativeControlResolver originalGeneratedResolver,
        CuiThemePreferenceContext? actualHomeTheme = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        Host = new ChatCuiSceneHost();
        _originalWork = new(StopAsync, CleanupAsync);
        InitializeValues();
        var registry = ChatSpaceCuiRuntime.CreateRegistry(DispatchCodeAction);
        NineToOne.Web.BrowserCanonicalIcons.Register(registry);
        registry.RegisterObjectRenderer("OriginalChatGeneratedChildren", _ => new ChatCuiGeneratedChildren(this, originalGeneratedResolver));
        _loader = new CuiControlLoader(registry);
        var appearance = CuiThemeScopeApplier.DetectAppearance();
        _loader.SetSurface("Spaces", "app.spaces"); _loader.SetAppearance(appearance);
        if (actualHomeTheme is not null) _loader.SetThemePreferenceContext(actualHomeTheme);
        _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
        var loaded = _loader.TryLoad(HavenOS.Apps.Spaces.ChatWorkspaceCuiDocument.Load());
        Root = loaded.Root ?? throw new InvalidDataException("The canonical Chat workspace did not produce a root.");
        if (loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error)) throw new InvalidDataException(string.Join(Environment.NewLine, loaded.Diagnostics));
        _loader.WireBindings(Root);
        Root.Resources.MergedDictionaries.Add(CuiSceneVisualResources.Create("Spaces", appearance));
        Root.SetValue(ThemeVariantScope.RequestedThemeVariantProperty, CuiSceneVisualResources.Variant(appearance));
        Host.Content = Root;
        Instruction = new CuiChatComposer(Find<TextBox>("composer-input"));
        Instruction.Invalidated += OnComposerInvalidated;
        Messages = new CuiChatTranscript(Root);
        _composerKeys = ChatSpaceCuiRuntime.AttachComposer(Root, Instruction.Control, Find<Button>("send"));
        Root.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, OnDialogKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Root.SizeChanged += OnRootSizeChanged;
    }

    public Control Root { get; }
    public ChatCuiSceneHost Host { get; }
    public CuiChatComposer Instruction { get; }
    public CuiChatTranscript Messages { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Task? OriginalClose => _originalWork.OriginalClose;
    public bool ContextDismissed => _contextDismissed;
    private void InitializeValues()
    {
        foreach (var key in new[] { "chat.title", "chat.status", "context.summary", "dialog.title", "dialog.description", "dialog.text", "dialog.error", "catalog.query", "catalog.status" }) _values[key] = "";
        _values["chat.hasIssues"] = false;
        _values["chat.standaloneHeader"] = true;
        _values["chat.headerColumns"] = "1fr Auto"; _values["chat.headerRows"] = "Auto";
        _values["chat.headerActionsColumn"] = 1; _values["chat.headerActionsRow"] = 0;
        _values["chat.typeLabel"] = "Normal"; _values["chat.locationLabel"] = "Local";
        _values["chat.modelLabel"] = "No model selected";
        _values["chat.approvalLabel"] = "Ask for Approval";
        _values["chat.emptyDescription"] = "Choose an available model and write a message.";
        _values["composer.placeholder"] = "Message";
        _values["context.visible"] = false; _values["context.expanded"] = false;
        _values["dialog.visible"] = false; _values["dialog.isText"] = false; _values["dialog.confirmLabel"] = "Save";
        _values["dialog.choices"] = Array.Empty<object>();
        _values["catalog.visible"] = false; _values["catalog.items"] = Array.Empty<object>();
        _values["dual.available"] = false; _values["dual.active"] = false;
        _values["dual.label"] = "Dual: Off"; _values["dual.modelLabel"] = "Model B: choose…";
    }
    public void SetConversationHeader(string title, string typeLabel, string locationLabel, string? modelLabel)
    {
        Publish(() => { _values["chat.title"] = title; _values["chat.typeLabel"] = typeLabel; _values["chat.locationLabel"] = locationLabel;
            _values["chat.modelLabel"] = string.IsNullOrWhiteSpace(modelLabel) ? "No model selected" : modelLabel; }, "chat.title", "chat.typeLabel", "chat.locationLabel", "chat.modelLabel");
    }
    internal void SetShellHosted(bool hosted) => Publish(() => _values["chat.standaloneHeader"] = !hosted, "chat.standaloneHeader");
    public void SetComposerPlaceholder(string value) => Publish(() => _values["composer.placeholder"] = value, "composer.placeholder");
    public void SetApprovalLabel(string value) => Publish(() => _values["chat.approvalLabel"] = value, "chat.approvalLabel");
    public void SetSending(bool sending, bool modelAvailable) => Publish(() => { _sending = sending; _modelAvailable = modelAvailable; });
    public void SetSafetyLocked(bool locked) => Publish(() => _safetyLocked = locked);
    internal void SetHasIssues(bool available) => Publish(() => _values["chat.hasIssues"] = available, "chat.hasIssues");
    public void SetStatus(string? value) => Publish(() => _values["chat.status"] = value ?? "", "chat.status");
    public void SetAttachmentChips(IReadOnlyList<ChatAttachmentChip> chips) => Publish(() => _attachmentChips = chips.ToArray(), "composer.attachments", "composer.hasAttachments");
    public void SetContextEntries(string summary, IReadOnlyList<ChatSceneContextEntry> entries) => Publish(() =>
    {
        _contextEntries = entries.ToArray(); _contextDismissed = false;
        _values["context.summary"] = summary; _values["context.visible"] = entries.Count > 0;
        _values["context.expanded"] = _isContextExpanded;
    });
    public void SyncMessages(IReadOnlyList<ChatSceneMessage> messages)
    {
        if (messages.Select(message => message.Id).Distinct().Count() != messages.Count) throw new ArgumentException("Message identities must be unique.");
        Publish(() =>
        {
            var ids = messages.Select(message => message.Id).ToHashSet();
            foreach (var stale in _messages.Keys.Where(id => !ids.Contains(id)).ToArray()) { _messages.Remove(stale); _generatedContent.Remove(stale); }
            _messageOrder.Clear();
            foreach (var message in messages) { _messages[message.Id] = message; _messageOrder.Add(message.Id); }
            foreach (var presentation in _generatedPresentations) presentation.Refresh();
        }, "chat.messages", "chat.hasMessages", "chat.empty");
    }
    public void UpdateMessage(ChatSceneMessage message) => Publish(() =>
    {
        if (!_messages.ContainsKey(message.Id)) _messageOrder.Add(message.Id);
        _messages[message.Id] = message;
    }, "chat.messages", "chat.hasMessages", "chat.empty");
    public void SetGeneratedContent(Guid messageId, IReadOnlyList<HavenElement> children) => Publish(() =>
    {
        if (!_messages.ContainsKey(messageId)) throw new InvalidOperationException("Generated children require their current canonical message.");
        _generatedContent[messageId] = children.ToArray();
        foreach (var presentation in _generatedPresentations.Where(item => item.MessageId == messageId)) presentation.Refresh();
    });
    public void ClearGeneratedContent(Guid messageId) => Publish(() =>
    {
        _generatedContent.Remove(messageId);
        foreach (var presentation in _generatedPresentations.Where(item => item.MessageId == messageId)) presentation.Refresh();
    });
    internal void RegisterGeneratedPresentation(ChatCuiGeneratedChildren presentation) => _generatedPresentations.Add(presentation);
    internal IReadOnlyList<HavenElement> GetOriginalGeneratedChildren(Guid id) => !_disposed && _messages.ContainsKey(id) ? _generatedContent.GetValueOrDefault(id) ?? [] : [];
    public void ScrollToEnd() => Messages.ScrollY = Messages.MaxScrollY;
    public void EnsureDualModelBar() => Publish(() => _values["dual.available"] = true);
    public void SetDualActive(bool active) => Publish(() => { _values["dual.active"] = active; _values["dual.label"] = active ? "Dual: On" : "Dual: Off"; });
    public void SetDualSecondModel(string? key) => Publish(() => _values["dual.modelLabel"] = string.IsNullOrWhiteSpace(key) ? "Model B: choose…" : "Model B: " + key);
    public void ShowDualModelChoices(IReadOnlyList<string> modelKeys) => ShowChoices("Choose the second model", "",
        modelKeys.Select(key => ("Model B — " + key, (Func<Task>)(() => _actions.SelectDualModel(key)))).ToArray());

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "chat.messages" => _messageOrder.Select(id => ProjectMessage(_messages[id])).ToArray(),
            "chat.hasMessages" => _messages.Count > 0, "chat.empty" => _messages.Count == 0,
            "chat.mainInteractive" => _values["dialog.visible"] is not true && !_disposed,
            "chat.canChooseType" => _actions.ChooseTypeAndLocation is not null && !_disposed,
            "chat.canChooseModel" => _actions.ChooseModelAndReasoning is not null && !_disposed,
            "chat.canChooseApproval" => _actions.ChooseToolApproval is not null && !_disposed,
            "chat.workspacePadding" => _compactHeader ? "16,20,16,24" : "24,20,24,53",
            "chat.transcriptMargin" => _compactHeader ? "0" : "31,0,13,0",
            "composer.controlsRows" => _compactHeader ? "Auto Auto" : "Auto",
            "composer.approvalRow" => _compactHeader ? 1 : 0,
            "composer.approvalColumn" => _compactHeader ? 0 : 2,
            "composer.approvalSpan" => _compactHeader ? 5 : 1,
            "composer.text" => Instruction?.Text ?? "", "composer.canEdit" => !_safetyLocked && !_disposed,
            "composer.canSend" => !_safetyLocked && !_disposed && (_sending || _modelAvailable),
            "composer.sending" => _sending && !_disposed,
            "composer.sendLabel" => _sending ? (string.IsNullOrWhiteSpace(Instruction?.Text) ? "Stop response" : "Response in progress") : "Send message",
            "composer.sendGlyph" => _sending && string.IsNullOrWhiteSpace(Instruction?.Text) ? "×" : "↑",
            "composer.sendIcon" => _sending && string.IsNullOrWhiteSpace(Instruction?.Text) ? "icon:close" : "icon:arrow-up",
            "composer.hasAttachments" => _attachmentChips.Count > 0,
            "composer.attachments" => _attachmentChips.Select(chip => (object)new Dictionary<string, object?> { ["id"] = chip.Id, ["label"] = chip.Label, ["removeLabel"] = "Remove " + chip.Label }).ToArray(),
            "context.entries" => _contextEntries.Select(entry => (object)new Dictionary<string, object?> { ["id"] = entry.EntryId.ToString("D"), ["title"] = entry.CategoryLabel + ": " + entry.Title, ["preview"] = entry.Preview, ["canRemove"] = entry.IsRemovable }).ToArray(),
            "dialog.canInteract" => !_dialogBusy && !_disposed,
            "dialog.canConfirm" => !_dialogBusy && !_disposed && _dialogConfirm is not null && !string.IsNullOrWhiteSpace(_values["dialog.text"] as string),
            _ => _values.GetValueOrDefault(path)
        };
        return value is not null;
    }
    private Dictionary<string, object?> ProjectMessage(ChatSceneMessage message)
    {
        var user = message.Role == MessageRole.User;
        Root.TryFindResource("CuiBackgroundBrush", out var userBrush);
        return new()
        {
            ["id"] = message.Id.ToString("D"), ["content"] = message.Content,
            ["author"] = user ? "You" : message.AgentName,
            ["alignment"] = user ? "Right" : "Left", ["padding"] = user ? "16,12" : "0",
            ["background"] = user ? userBrush as IBrush ?? Brushes.Transparent : Brushes.Transparent,
            ["accessibleName"] = (user ? "Your message" : "Response from " + message.AgentName),
            ["workStatus"] = message.Thinking, ["hasWorkStatus"] = !string.IsNullOrWhiteSpace(message.Thinking),
            ["hasGenerated"] = _generatedContent.TryGetValue(message.Id, out var children) && children.Count > 0,
            ["isAssistant"] = message.Role == MessageRole.Assistant, ["canModify"] = !_safetyLocked && !_sending,
            ["tools"] = (message.ToolActivities ?? []).Select(tool => (object)new Dictionary<string, object?>
            {
                ["id"] = tool.Id.ToString("D"), ["title"] = tool.Title + (tool.Succeeded ? " · Completed" : " · Failed"),
                ["detail"] = tool.Detail + (tool.Duration > TimeSpan.Zero ? $" · {tool.Duration.TotalSeconds:0.#}s" : "") + (tool.LinesAdded != 0 || tool.LinesRemoved != 0 ? $" · +{tool.LinesAdded} −{tool.LinesRemoved}" : "")
            }).ToArray()
        };
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    { value = null; return item is IReadOnlyDictionary<string, object?> row && row.TryGetValue(path, out value); }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public bool TrySetValue(string path, object? value)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || value is not string text) return false;
        if (path == "composer.text" && !_safetyLocked) { if (Instruction is not null && Instruction.Text != text) Instruction.Text = text; return true; }
        if (path == "catalog.query") { _values[path] = text; RebuildCatalog(); Changed(); return true; }
        if (path == "dialog.text" && _dialogConfirm is not null && !_dialogBusy) { _values[path] = text; Changed(); return true; }
        return false;
    }
    private void OnRootSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (_disposed || args.NewSize.Width <= 0) return;
        var compact = args.NewSize.Width < 640;
        if (_compactHeader == compact) return;
        _compactHeader = compact;
        Publish(() =>
        {
            _values["chat.headerColumns"] = compact ? "1fr" : "1fr Auto";
            _values["chat.headerRows"] = compact ? "Auto Auto" : "Auto";
            _values["chat.headerActionsColumn"] = compact ? 0 : 1;
            _values["chat.headerActionsRow"] = compact ? 1 : 0;
        }, "chat.headerColumns", "chat.headerRows", "chat.headerActionsColumn", "chat.headerActionsRow",
            "chat.workspacePadding", "chat.transcriptMargin", "composer.controlsRows", "composer.approvalRow", "composer.approvalColumn", "composer.approvalSpan");
    }
    private void OnComposerInvalidated(object? sender, EventArgs args) { if (!_disposed) Changed("composer.text"); }
    private void Publish(Action action, params string[] paths)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => { if (!_disposed) { action(); Notify(); } }); return; }
        if (_disposed) return;
        action(); Notify();
        void Notify() { if (paths.Length == 0) Changed(); else foreach (var path in paths) Changed(path); }
    }
    private void Changed(string? path = null) => PropertyChanged?.Invoke(this, new(path));
    private T Find<T>(string name) where T : Control => _loader.FindAuthoredControl<T>(Root, name);

    public Task CloseAsync() => _originalWork.CloseAndDrainAsync();
    private Task StopAsync()
    {
        _disposed = true;
        var failures = new List<Exception>();
        Stop(() => Instruction.Invalidated -= OnComposerInvalidated);
        Stop(_composerKeys.Dispose);
        Stop(() => Root.SizeChanged -= OnRootSizeChanged);
        Stop(Instruction.Dispose);
        Stop(() => Root.RemoveHandler(Avalonia.Input.InputElement.KeyDownEvent, OnDialogKey));
        if (failures.Count > 0) throw new AggregateException("Chat input shutdown failed.", failures);
        return Task.CompletedTask;
        void Stop(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
    }
    private async Task CleanupAsync()
    {
        var failures = new List<Exception>();
        try { _loader.Dispose(); } catch (Exception error) { failures.Add(error); }
        Task? actions = null;
        try { actions = _loader.WhenActionsIdleAsync(); await actions; }
        catch (Exception error) { failures.Add(actions?.Exception ?? error); }
        // A failed dispose does not mean the original action queue disappeared.
        // Keep the original presentation attached if any owning join failed.
        if (failures.Count > 0) throw new AggregateException("Chat presentation cleanup failed.", failures);
        foreach (var generated in _generatedPresentations) generated.DetachAfterOriginalChildrenJoined();
        _generatedPresentations.Clear(); _generatedContent.Clear();
        Host.Content = null;
    }
    public void Dispose() => _ = CloseAsync();
    public ValueTask DisposeAsync() => new(CloseAsync());
}

internal sealed class CuiChatTranscript(Control root)
{
    private ScrollViewer? View => root.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(view =>
        view.GetVisualAncestors().OfType<Control>().Any(parent => parent.Name == "chat-transcript"));
    public double MaxScrollY => View is { } view ? Math.Max(0, view.Extent.Height - view.Viewport.Height) : 0;
    public double ScrollY { get => View?.Offset.Y ?? 0; set { if (View is { } view) view.Offset = new(view.Offset.X, Math.Clamp(value, 0, MaxScrollY)); } }
}
