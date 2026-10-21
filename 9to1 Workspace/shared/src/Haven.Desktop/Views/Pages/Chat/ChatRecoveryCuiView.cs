using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
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
    private string? _status;
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

    /// <summary>Read-only projection of the ACTUAL existing chat scene status. It reflects the real
    /// model-availability sentence from the owner page/scene; the authored recovery row renders it
    /// alongside the Manage action, and it never fabricates a status or readiness.</summary>
    internal string? Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
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
            case "Recovery.Status":
                value = _status;
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

/// <summary>Immediate readiness for the shared recovery scene (the host page owns deeper readiness).</summary>
internal sealed class ChatRecoveryReadiness : ICuiSceneReadiness
{
    public static readonly ChatRecoveryReadiness Instance = new();
    private ChatRecoveryReadiness() { }
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
        new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Chat recovery is ready."));
}

/// <summary>
/// The live CUI recovery surface mounted into the chat scene under the <c>RecoveryHost</c> native
/// host. It mirrors the shared composer <see cref="CuiSceneHost"/> pattern: it owns the scene host,
/// the typed presentation adapter and a guarded initialization, and it exposes the actual loader
/// action-settlement capture (<see cref="CaptureOriginalActionSettlement"/>). The owning
/// <see cref="ChatHavenScene"/> controls visibility through the bindings and joins the child close
/// before releasing the physical native mapping.
/// </summary>
public sealed class ChatRecoveryCuiView : ContentControl, IDisposable, IAsyncDisposable
{
    private readonly CuiSceneHost _host;
    private readonly ChatRecoveryBindings _bindings;
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

    internal ChatRecoveryCuiView(ChatRecoveryBindings bindings)
        : this(bindings, ChatRecoveryReadiness.Instance)
    {
    }

    /// <summary>Readiness injection seam used by tests to hold/observe the real host show
    /// pipeline; defaults to production readiness. The SAME production host/loader/document are
    /// exercised, never an alternate renderer.</summary>
    internal ChatRecoveryCuiView(ChatRecoveryBindings bindings, ICuiSceneReadiness readiness)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _readiness = readiness ?? ChatRecoveryReadiness.Instance;
        _host = new CuiSceneHost();
        AutomationProperties.SetAutomationId(this, "ChatRecoveryCuiView");
        AutomationProperties.SetName(this, "Local model recovery");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = _host;
        _ = InitializeAsync();
    }

    private readonly ICuiSceneReadiness _readiness;

    /// <summary>Idempotent guarded initialization; parents join it before publication/restoration.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initGate)
        {
            if (_initializeTask is null)
            {
                var scene = new CuiNativeScene(
                    "spaces.chat.recovery", "Chat Recovery", "Chat",
                    ChatRecoveryCuiDocument.Load(), _bindings, _bindings, _readiness)
                {
                    IsPublicationCurrent = () => !_closing
                };
                var rawShow = _host.ShowAsync(scene, cancellationToken);
                _rawShowTask = rawShow;
                _initializeTask = rawShow;
            }
            return _initializeTask;
        }
    }

    /// <summary>
    /// Captures the ACTUAL loader action-settlement pipeline for actions admitted on this recovery
    /// surface, using the exact original raw Show task. External callers join this snapshot outside
    /// any included action (an action must never await a snapshot containing itself). Requires
    /// UI-thread access and a successful guarded initialization.
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
            throw new InvalidOperationException("The recovery surface has no completed live initialization to capture from.");
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
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = CloseOriginalCoreAsync(start.Task);
            start.SetResult(); // original is published before the close body/callbacks run
            return _closeTask;
        }
    }

    private async Task CloseOriginalCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        // Retire command admission first: start the host close independently of initialization, so a
        // cancellation or fault in init can never skip retirement.
        _bindings.Retire();
        var hostClose = _host.CloseOriginalAsync();
        var faults = new List<Exception>();
        Task<CuiSceneAvailability>? initTask;
        lock (_initGate) initTask = _initializeTask;
        try { await hostClose.ConfigureAwait(false); }
        catch (Exception error) { faults.Add(error); }
        if (initTask is not null)
        {
            try { await initTask.ConfigureAwait(false); }
            catch (Exception error) { faults.Add(error); }
        }
        if (faults.Count == 1) throw faults[0];
        if (faults.Count > 1) throw new AggregateException(faults);
    }

    /// <summary>Republishes the authored recovery visibility from the owner scene.</summary>
    public void Refresh() => _bindings.Refresh();

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
