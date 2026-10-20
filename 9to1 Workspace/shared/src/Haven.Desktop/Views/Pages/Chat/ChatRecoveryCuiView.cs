using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// The genuine CUI "Manage local models" recovery action bindings for the chat scene. It projects
/// the one observable bool (<c>Recovery.Visible</c>) that drives the authored recovery button and
/// dispatches the canonical <c>chat.recovery.manage</c> command as the scene's
/// <see cref="ManageLocalModelsRequested"/> event. It never fabricates a model or changes the send
/// gate; it only requests the shared Home local-model management route.
/// </summary>
internal sealed class ChatRecoveryBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged
{
    private const string ManageCommand = "chat.recovery.manage";
    private bool _visible;
    private bool _retired;

    /// <summary>Observed visibility of the authored recovery affordance; driven by the owner page's
    /// truthful model-availability gate.</summary>
    internal bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    /// <summary>Requests the shared Home local-model management route. Raised only from the
    /// authored recovery action; it never changes model/send state here.</summary>
    internal event EventHandler? ManageLocalModelsRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        switch (path)
        {
            case "Recovery.Visible":
                value = _visible;
                return true;
            default:
                return false;
        }
    }

    public bool TrySetValue(string path, object? value) => false;

    public bool? IsActionAvailable(string command) =>
        command == ManageCommand ? !_retired : (bool?)false;

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (command != ManageCommand)
            return ValueTask.FromException(new InvalidOperationException(
                $"Chat recovery CUI command '{command}' is not owned by the recovery surface."));
        if (_retired)
            return ValueTask.FromException(new InvalidOperationException("The chat recovery surface has retired."));
        ManageLocalModelsRequested?.Invoke(this, EventArgs.Empty);
        return ValueTask.CompletedTask;
    }

    /// <summary>Stops admitting further recovery commands once the owning scene begins disposal.</summary>
    internal void Retire() => _retired = true;

    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

/// <summary>Loads the authored <c>ChatRecovery.cui</c> document from the packaged desktop assembly.</summary>
internal static class ChatRecoveryCuiDocument
{
    public const string ResourceName = "Haven.Desktop.Chat.Recovery.cui";

    public static CuiDocument Load()
    {
        using var stream = typeof(ChatRecoveryCuiDocument).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The authored recovery CUI resource is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "ChatRecovery.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The authored recovery CUI document is invalid.");
        return document;
    }
}

/// <summary>
/// The live CUI recovery surface mounted into the chat scene under the <c>RecoveryHost</c> native
/// host. It owns the loader/scene host and the typed presentation adapter, rendering
/// <c>ChatRecovery.cui</c>. The owning <see cref="ChatHavenScene"/> controls visibility through the
/// bindings and disposes/unregisters this view during its own disposal.
/// </summary>
public sealed class ChatRecoveryCuiView : ContentControl, IDisposable
{
    private readonly CuiControlLoader _loader = new();
    private readonly ChatRecoveryBindings _bindings;
    private bool _disposed;

    public ChatRecoveryCuiView(ChatRecoveryBindings bindings)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        AutomationProperties.SetAutomationId(this, "ChatRecoveryCuiView");
        AutomationProperties.SetName(this, "Local model recovery");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        var document = ChatRecoveryCuiDocument.Load();
        _loader.SetBindingContext(_bindings);
        _loader.SetActionDispatcher(_bindings);
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
        {
            _loader.Dispose();
            throw new InvalidDataException("The canonical recovery CUI did not produce a usable control.");
        }
        _loader.WireBindings(loaded.Root);
        Content = loaded.Root;
    }

    /// <summary>Joins the real retained recovery action pipeline (including the current admitted
    /// dispatch) so an owner that requested a recovery can observe it settle.</summary>
    public Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();

    /// <summary>Republishes the authored recovery visibility from the owner scene.</summary>
    public void Refresh() => _bindings.Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bindings.Retire();
        Content = null;
        _loader.Dispose();
    }
}
