using System.ComponentModel;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>One authored composer chip (capability/Agent/Skill/resource or persisted attachment).</summary>
public sealed record ChatComposerChip(string Id, string Label, bool Invokable);

/// <summary>
/// The single shared canonical composer's data source. Each real consumer (the Haven Chat native
/// scene and the ChatSpace CUI host) supplies a live implementation; no composer state is cloned.
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
    void Send();
    void Stop();
    void Add();
    void OpenSettings();
    void CloseSettings();
    void SetActionMode(string mode);
    void SetResponseMode(string mode);
    void InvokeChip(string id);
    void RemoveChip(string id);
    void FormatSelection(string command);
    void Emoji();
    void CancelEdit();
}

/// <summary>
/// Typed CUI binding/action adapter over the shared composer. It projects the current
/// <see cref="IChatComposerSource"/> into the composer document and forwards authored commands to
/// the source. Presentation-only state (settings panel open/closed) lives here, not in domain.
/// </summary>
public sealed class ChatComposerBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly IChatComposerSource _source;
    private bool _settingsOpen;
    private bool _focused;

    public ChatComposerBindings(IChatComposerSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Observed focus on the real composer input; drives the compact/expanded formatting row.</summary>
    internal bool IsFocused
    {
        get => _focused;
        set
        {
            if (_focused == value) return;
            _focused = value;
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
                value = _focused;
                return true;
            case "Composer.ShowSettings":
                value = _settingsOpen;
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
        return false;
    }

    public bool? IsActionAvailable(string command)
    {
        return command switch
        {
            "composer.send" => _source.CanSend,
            "composer.add" => _source.HasAdd,
            "composer.emoji" => _source.CanSend || _source.IsSending,
            "composer.settings" or "composer.set-action-mode" or "composer.set-response-mode" => true,
            _ => true
        };
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "composer.send":
                _source.Send();
                return ValueTask.CompletedTask;
            case "composer.stop":
                _source.Stop();
                return ValueTask.CompletedTask;
            case "composer.add":
                _source.Add();
                return ValueTask.CompletedTask;
            case "composer.settings":
                _settingsOpen = !_settingsOpen;
                if (_settingsOpen) _source.OpenSettings(); else _source.CloseSettings();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ValueTask.CompletedTask;
            case "composer.set-action-mode" when parameter is string mode:
                _source.SetActionMode(mode);
                _settingsOpen = false;
                _source.CloseSettings();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ValueTask.CompletedTask;
            case "composer.set-response-mode" when parameter is string mode:
                _source.SetResponseMode(mode);
                _settingsOpen = false;
                _source.CloseSettings();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ValueTask.CompletedTask;
            case "composer.invoke-chip" when parameter is string id:
                _source.InvokeChip(id);
                return ValueTask.CompletedTask;
            case "composer.remove-chip" when parameter is string id:
                _source.RemoveChip(id);
                return ValueTask.CompletedTask;
            case "composer.format" when parameter is string command:
                _source.FormatSelection(command);
                return ValueTask.CompletedTask;
            case "composer.emoji":
                _source.Emoji();
                return ValueTask.CompletedTask;
            case "composer.cancel-edit":
                _source.CancelEdit();
                return ValueTask.CompletedTask;
            default:
                return ValueTask.FromException(new InvalidOperationException($"Chat composer CUI command '{command}' is not owned by the shared composer."));
        }
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
public sealed class ChatComposerCuiView : ContentControl, IDisposable
{
    private readonly CuiSceneHost _host;
    private readonly ChatComposerBindings _bindings;
    private readonly CuiComposerInputPort _input;
    private bool _disposed;
    private bool _inputAttached;

    public ChatComposerCuiView(IChatComposerSource source)
    {
        _bindings = new ChatComposerBindings(source ?? throw new ArgumentNullException(nameof(source)));
        var registry = new CuiControlRegistry();
        _host = new CuiSceneHost(registry);
        AutomationProperties.SetAutomationId(this, "ChatComposerCuiView");
        AutomationProperties.SetName(this, "Chat composer");
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Content = _host;
        _input = new CuiComposerInputPort(() => FindComposerTextBox(), box =>
        {
            box.GotFocus += (_, _) => _bindings.IsFocused = true;
            box.LostFocus += (_, _) => _bindings.IsFocused = false;
            box.TextChanged += (_, _) => _bindings.Refresh();
        });
        _ = ShowAsync();
    }

    public IChatComposerInputPort Input => _input;
    public ChatComposerBindings Bindings => _bindings;
    public IChatComposerSource Source => _bindings.Source;

    private Task ShowAsync()
    {
        var scene = new CuiNativeScene(
            "spaces.chat.composer", "Chat Composer", "Chat",
            ChatComposerCuiDocument.Load(), _bindings, _bindings, ChatComposerReadiness.Instance);
        return _host.ShowAsync(scene).ContinueWith(
            _ => Dispatcher.UIThread.Post(() => AttachInputOnce()),
            TaskScheduler.Default);
    }

    private void AttachInputOnce()
    {
        if (_inputAttached) return;
        var box = FindComposerTextBox();
        if (box is null) return;
        _inputAttached = true;
        _input.Attach(box);
    }

    private TextBox? FindComposerTextBox()
    {
        if (!ReferenceEquals(Content, _host) || _host.Content is not { } root) return null;
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
        _host.Dispose();
    }
}

/// <summary>
/// Adapter from the real CUI composer input (the rendered <see cref="TextBox"/>) to the typed
/// composer input port. Reads and writes target the actual canonical input; the input is resolved
/// lazily because the composer scene loads asynchronously.
/// </summary>
internal sealed class CuiComposerInputPort : IChatComposerInputPort
{
    private readonly Func<TextBox?> _resolve;
    private readonly Action<TextBox>? _onAttach;
    private TextBox? _attached;
    private string? _pendingText;

    public CuiComposerInputPort(Func<TextBox?> resolve, Action<TextBox>? onAttach = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _onAttach = onAttach;
    }

    internal void Attach(TextBox box)
    {
        if (ReferenceEquals(box, _attached)) return;
        _attached = box;
        if (_pendingText is not null)
        {
            box.Text = _pendingText;
            _pendingText = null;
        }
        _onAttach?.Invoke(box);
        box.TextChanged += (_, _) =>
        {
            TextChanged?.Invoke(this, EventArgs.Empty);
            Invalidated?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Resolves the real composer input lazily, auto-attaching the first time the actual
    /// CUI TextBox becomes available (render timing varies between the native transport and the
    /// authored mount slot).</summary>
    private TextBox? Current
    {
        get
        {
            if (_attached is not null) return _attached;
            var box = _resolve();
            if (box is not null) Attach(box);
            return box;
        }
    }

    public string Text
    {
        get => Current?.Text ?? _pendingText ?? string.Empty;
        set
        {
            if (Current is { } box) box.Text = value ?? string.Empty;
            else _pendingText = value ?? string.Empty;
        }
    }

    public int CaretIndex => Current?.CaretIndex ?? 0;
    public int SelectionStart => Current?.SelectionStart ?? 0;
    public int SelectionLength => Current?.SelectionLength ?? 0;
    public string SelectedText => Current?.SelectedText ?? string.Empty;
    public bool IsFocused => Current?.IsKeyboardFocusWithin ?? false;

    public event EventHandler? TextChanged;
    public event EventHandler? Invalidated;

    public void PlaceCaretAtEnd(bool extendSelection = false)
    {
        if (Current is not { } box) return;
        box.CaretIndex = box.Text?.Length ?? 0;
        if (!extendSelection) box.SelectionStart = box.CaretIndex;
    }

    public void SetSelection(int anchor, int caret)
    {
        if (Current is not { } box) return;
        box.SelectionStart = anchor;
        box.CaretIndex = caret;
    }

    public void ClearSelection()
    {
        if (Current is not { } box) return;
        box.SelectionStart = box.CaretIndex;
    }

    public void Focus() => Current?.Focus();
}
