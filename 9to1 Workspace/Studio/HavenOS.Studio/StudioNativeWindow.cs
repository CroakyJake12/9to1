using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using HavenOS.Images;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Dulche.Den;

namespace HavenOS.AIStudio;

public sealed class StudioNativeWindow : Window, ICuiActionDispatcher
{
    private readonly IServiceProvider _services;
    private readonly IPictureSharedRasterDecoder _rasterDecoder;
    private readonly StudioDenLifetime _den;
    private readonly CuiViewModel _model = new();
    private readonly CuiSceneHost _shell;
    private readonly ContentControl _editorContent = new();
    private readonly HomeApprovalCuiSurface _approvals;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StudioOriginalCallbackLifetime _callbacks = new();
    private readonly StudioOriginalCallbackLifetime _retirements = new();
    private readonly List<Exception> _nativeCallbackFailures = [];
    private StudioOriginalEditorLifetime? _originalEditor;
    private Task? _close, _originalInitialization, _nativeOwnerTask, _nativeCloseObservation;
    private Func<Task>? _nativeOwnerClose;
    private bool _retiring, _nativeCloseAuthorized, _nativeClosedWithoutDrain;
    private AgentAvatarEditor? _editor;
    private CuiSceneHost? _editorHost;
    private HomePersonalDenSession? _session;
    private AuthenticatedResourceActor? _workspaceActor;
    private string? _selectedDen, _pendingImport, _pendingImportAudit;
    private AgentDefinitionRecord[] _agents = [];
    public Task Initialization { get; private set; } = Task.CompletedTask;

    public StudioNativeWindow(IServiceProvider services)
        : this(services, new PictureGlycinSharedRasterProvider()) { }

    public StudioNativeWindow(IServiceProvider services, IPictureSharedRasterDecoder rasterDecoder)
    {
        ArgumentNullException.ThrowIfNull(rasterDecoder);
        _rasterDecoder = rasterDecoder;
        _services = services; _den = Get<StudioDenLifetime>();
        // Retain this exact acquired native owner before constructor/native callbacks.
        _den.BindOriginalNativePresentation(this);
        Title = "AI Studio"; Width = 1100; Height = 850;
        _approvals = new(Get<HomeCoreRuntime>(), Get<HomeLocalProfileIdentity>(), Get<HomePermissionTrustService>());
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("studio.host-approvals", _ => _approvals);
        registry.RegisterObjectRenderer("studio.host-editor", _ => _editorContent);
        _shell = new(registry); Content = _shell;
        _model.Set("AgentName", ""); _model.Set("AgentNames", Array.Empty<string>()); _model.Set("SelectedAgentIndex", -1);
        _model.Set("Status", "Choose the Den to author. Existing storage requires Home ownership review.");
        Opened += OnOriginalOpened;
        Activated += OnOriginalActivated;
        Closing += OnOriginalClosing;
        Closed += (_, _) => { if (!_nativeCloseAuthorized) _nativeClosedWithoutDrain = true; };
    }
    private IStorageProvider Picker => _services.GetService<IStorageProvider>() ?? StorageProvider;
    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();
    private string Text(string key) => _model.Get(key)?.ToString() ?? "";
    private void Status(string text)
    {
        if (_retiring) return;
        _model.Set("Status", text);
        if (_retiring) throw new InvalidOperationException("Native Studio publication retired during notification.");
    }
    private void RequireOriginalPublication(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_retiring, this);
    }
    private async Task InitializeAsync(CancellationToken ct)
    {
        RequireOriginalPublication(ct);
        await _approvals.InitializeAsync(ct);
        RequireOriginalPublication(ct);
        using var stream = typeof(StudioNativeWindow).Assembly.GetManifestResourceStream("HavenOS.AIStudio.UI.StudioHost.cui")
            ?? throw new InvalidDataException("Studio host CUI source is missing.");
        using var reader = new StreamReader(stream);
        await _shell.ShowAsync(new CuiNativeScene("9to1.Studio", "AI Studio", "Studio", new CuiRichParser().Parse(reader.ReadToEnd()),
            _model, this, new ConfigurationReadiness()), ct);
        RequireOriginalPublication(ct);
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        var capturedCommand = command; var capturedParameter = parameter;
        return new(_callbacks.Run(token => DispatchOriginalAsync(capturedCommand, capturedParameter, token), cancellationToken));
    }
    private async Task DispatchOriginalAsync(string command, object? parameter, CancellationToken cancellationToken)
    {
        RequireOriginalPublication(cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var ct = linked.Token;
        await _operations.WaitAsync(ct);
        try
        {
            switch (command)
            {
                case "CreateDen": case "OpenDen":
                    var folders = await Picker.OpenFolderPickerAsync(new() { AllowMultiple = false, Title = command == "CreateDen" ? "Choose an empty folder for the new Den" : "Choose an existing Den" });
                    RequireOriginalPublication(ct);
                    if (folders.Count != 1) return;
                    using (var folder = folders[0])
                    {
                        var path = folder.TryGetLocalPath() ?? throw new NotSupportedException("This Den provider requires an actual local folder.");
                        var actualSelectedDen = await _den.SelectAsync(path, command == "CreateDen", Get<HomeLocalStoreOwnership>(), ct);
                        RequireOriginalPublication(ct);
                        _selectedDen = actualSelectedDen;
                    }
                    _pendingImport = null; _pendingImportAudit = null; _agents = []; _model.Set("AgentNames", Array.Empty<string>()); _model.Set("SelectedAgentIndex", -1);
                    if (command == "CreateDen") await RefreshAgentsAsync(null, ct);
                    Status(command == "CreateDen" ? "New personal Den bound to the current Home profile. Create or open an Agent." : "Existing Den selected. Review ownership in Home before opening it.");
                    break;
                case "RequestImport":
                    var request = await _den.RequestExistingImportAsync(Get<HomeLocalStoreOwnership>(), ct);
                    RequireOriginalPublication(ct);
                    _pendingImport = request.RequestId;
                    await _approvals.FocusRequestAsync(request.RequestId, ct);
                    Status("Review Den ownership in Home, then choose Complete approved Den import.");
                    break;
                case "CompleteImport":
                    var displayed = _pendingImport ?? throw new InvalidOperationException("Request ownership review first.");
                    try { await _den.CompleteExistingImportAsync(displayed, Get<HomeLocalStoreOwnership>(), ct); }
                    catch (HomeStoreImportAuditPendingException pending)
                    {
                        _pendingImport = null; _pendingImportAudit = pending.RequestId;
                        Status("Den ownership was imported. Retry its Home audit to finish recording the result; importing again is unnecessary.");
                        break;
                    }
                    RequireOriginalPublication(ct);
                    _pendingImport = null; await RefreshAgentsAsync(null, ct); Status("Den ownership verified. Create or open an Agent.");
                    break;
                case "RetryImportAudit":
                    var audit = _pendingImportAudit ?? throw new InvalidOperationException("No Den import audit recovery is pending in this session.");
                    await _den.RetryExistingImportAuditAsync(audit, Get<HomeLocalStoreOwnership>(), ct);
                    RequireOriginalPublication(ct);
                    _pendingImportAudit = null; await RefreshAgentsAsync(null, ct);
                    Status("Den ownership audit recovered. Create or open an Agent.");
                    break;
                case "CreateAgent":
                    var name = Text("AgentName").Trim();
                    if (name.Length is < 1 or > 256) throw new InvalidOperationException("Enter an Agent name of at most 256 characters.");
                    var newSession = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
                    var created = await newSession.Den.SaveAsync(new AgentDefinitionRecord { Id = Guid.NewGuid().ToString("D"), NamespaceId = "personal", DisplayName = name, Version = "1" }, 0, Guid.NewGuid().ToString("N"), ct);
                    RequireOriginalPublication(ct);
                    await RefreshAgentsAsync(created.Id, ct);
                    await OpenAgentAsync(created.Id, ct);
                    break;
                case "RefreshAgents": await RefreshAgentsAsync(null, ct); break;
                case "OpenAgent":
                    var index = _model.Get("SelectedAgentIndex") is int selected ? selected : -1;
                    if (index < 0 || index >= _agents.Length) throw new InvalidOperationException("Choose an Agent first.");
                    var selectedAgent = _agents[index];
                    await OpenAgentAsync(selectedAgent.Id, ct); break;
                case "ImportAvatar": await ImportAvatarAsync(ct); break;
                default: throw new InvalidOperationException("Unknown Studio workspace action.");
            }
        }
        catch (Exception error)
        {
            List<Exception> errors = [error];
            if (_workspaceActor is not null && (error is UnauthorizedAccessException || error is DenException { Code: DenErrorCode.Forbidden }))
                try { await RetireWorkspaceAsync(default); }
                catch (Exception retirement) { StudioOriginalCallbackLifetime.Add(errors, retirement); }
            try { Status(error.Message); }
            catch (Exception publication) { StudioOriginalCallbackLifetime.Add(errors, publication); }
            StudioOriginalCallbackLifetime.Throw(errors);
        }
        finally { _operations.Release(); }
    }
    private async Task RefreshAgentsAsync(string? preferredId, CancellationToken ct)
    {
        var session = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
        var agents = (await session.Den.ListAsync<AgentDefinitionRecord>("personal", ct)).OrderBy(item => item.DisplayName, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (preferredId is null && _model.Get("SelectedAgentIndex") is int previous && previous >= 0 && previous < _agents.Length)
            preferredId = _agents[previous].Id;
        RequireOriginalPublication(ct);
        _workspaceActor = session.Actor;
        _agents = agents;
        _model.Set("AgentNames", agents.Select(item => item.DisplayName).ToArray());
        var index = preferredId is null ? -1 : Array.FindIndex(agents, item => item.Id == preferredId);
        _model.Set("SelectedAgentIndex", index >= 0 ? index : agents.Length == 0 ? -1 : 0);
    }
    private async Task OpenAgentAsync(string id, CancellationToken ct)
    {
        await RetireEditorAsync(ct);
        RequireOriginalPublication(ct);
        var session = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
        RequireOriginalPublication(ct);
        var assets = new DenAgentPresentationAssets(session.Den);
        // The host owns Forbidden retirement; an editor callback must not await its own drain.
        var editor = new AgentAvatarEditor(new AgentPresentationService(session.Den, assets), assets, null, _rasterDecoder);
        StudioOriginalEditorLifetime? original = null;
        original = new(editor, () => !_retiring && ReferenceEquals(_originalEditor, original),
            _ => RequestOriginalAuthorityRetirement());
        _originalEditor = original; _editor = editor; _session = session; _workspaceActor = session.Actor;
        await original.OpenAsync("personal", id, ct);
        original.RequireCurrent(ct);
        var scene = StudioNativeScene.Create(editor.Bindings, original.Actions,
            new SessionReadiness(this), editor.Preview, original.AcquirePreviewControl);
        var acquiredHost = new CuiSceneHost(scene.ControlRegistry);
        original.Host = acquiredHost; _editorHost = acquiredHost;
        original.RequireCurrent(ct);
        _editorContent.Content = acquiredHost;
        original.RequireCurrent(ct);
        await acquiredHost.ShowAsync(scene, ct);
        original.RequireCurrent(ct);
        Status("Opened the canonical Agent. Presentation changes use its saved revision.");
    }

    public Task ValidateWorkspaceAsync(CancellationToken ct) =>
        _callbacks.Run(ValidateWorkspaceOriginalAsync, ct);

    private async Task ValidateWorkspaceOriginalAsync(CancellationToken ct)
    {
        RequireOriginalPublication(ct);
        try
        {
            var current = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
            RequireOriginalPublication(ct);
            if (_workspaceActor is null || current.Actor != _workspaceActor || current.DenId != _selectedDen)
                throw new UnauthorizedAccessException("The selected Den or Home actor changed.");
        }
        catch (Exception error)
        {
            List<Exception> errors = [error];
            try { await RetireWorkspaceAsync(default); }
            catch (Exception retirement) { StudioOriginalCallbackLifetime.Add(errors, retirement); }
            StudioOriginalCallbackLifetime.Throw(errors);
        }
    }
    private async Task RequireSessionAsync(CancellationToken ct)
    {
        await ValidateWorkspaceOriginalAsync(ct);
        if (_session is null || _session.Actor != _workspaceActor || _session.DenId != _selectedDen)
            throw new UnauthorizedAccessException("The Agent editor belongs to a different Den or Home actor.");
    }
    private async Task ImportAvatarAsync(CancellationToken ct)
    {
        await RequireSessionAsync(ct);
        var editor = _editor ?? throw new InvalidOperationException("Open an Agent first.");
        var identity = editor.CurrentAgentIdentity ?? throw new InvalidOperationException("Open an Agent first.");
        var session = _session!;
        var files = await Picker.OpenFilePickerAsync(new() { AllowMultiple = false, Title = "Import an avatar image into this Agent", FileTypeFilter = [FilePickerFileTypes.ImageAll] });
        RequireOriginalPublication(ct);
        if (files.Count != 1) return;
        byte[]? bytes = null;
        try
        {
            using var file = files[0]; await using var input = await file.OpenReadAsync();
            using var output = new MemoryStream(); var buffer = new byte[65536];
            try
            {
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0)
                {
                    if (output.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("Avatar images must be at most 32 MiB.");
                    output.Write(buffer, 0, count);
                }
                bytes = output.ToArray();
            }
            finally { Array.Clear(buffer); if (output.TryGetBuffer(out var owned)) Array.Clear(owned.Array!, owned.Offset, owned.Count); }
            var mime = await Task.Run(() => { using var decoder = _rasterDecoder.OpenFrames(bytes, false, ct); return decoder.MimeType; }, ct);
            await RequireSessionAsync(ct);
            if (!ReferenceEquals(_editor, editor) || editor.CurrentAgentIdentity != identity)
                throw new InvalidOperationException("The Agent selection changed while choosing its avatar image.");
            var attachment = await session.Den.AddAttachmentAsync(identity.NamespaceID, identity.AgentID, DenAgentPresentationAssets.AgentOwnerKind, mime, bytes, Guid.NewGuid().ToString("N"), ct);
            if (_retiring || !ReferenceEquals(_editor, editor) || editor.CurrentAgentIdentity != identity)
            {
                // The canonical attachment acknowledgement is retained by the original action.
                // Retirement withdraws its presentation; it never repeats the acknowledged write.
                Status("The image was imported for the previously selected Agent. The current selection was preserved.");
                return;
            }
            editor.Bindings.Set("StaticAsset", attachment.Id);
            RequireOriginalPublication(ct);
            if (!ReferenceEquals(_editor, editor) || editor.CurrentAgentIdentity != identity)
                throw new InvalidOperationException("The original Agent changed during avatar notification.");
            editor.Bindings.Set("StateAsset", attachment.Id);
            RequireOriginalPublication(ct);
            Status("Image copied into this Agent's Den attachments. Apply the avatar identity or state, then save the presentation.");
        }
        finally { if (bytes is not null) Array.Clear(bytes); }
    }
    public async Task RetireWorkspaceAsync(CancellationToken ct)
    {
        if (_close is { IsCompletedSuccessfully: true }) { await _close; return; }
        // Explicit generic overload retains the SAME nested UI-created retirement Task.
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(() =>
            _retirements.Run(RetireWorkspaceOriginalAsync, ct));
        var actual = await dispatch;
        if (actual is null) throw new InvalidOperationException("The original workspace retirement supplied no task.");
        await actual;
    }

    private async Task RetireWorkspaceOriginalAsync(CancellationToken ct)
    {
        await RetireEditorAsync(ct);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_retiring) return;
            _selectedDen = null; _pendingImport = null; _pendingImportAudit = null; _workspaceActor = null; _agents = [];
            _model.Set("AgentNames", Array.Empty<string>());
            RequireOriginalPublication(ct);
            _model.Set("SelectedAgentIndex", -1);
            RequireOriginalPublication(ct);
            _model.Set("AgentName", "");
            RequireOriginalPublication(ct);
        });
    }

    private void RequestOriginalAuthorityRetirement()
    {
        Dispatcher.UIThread.VerifyAccess();
        // Stop-request only from inside the editor action. The SAME actual retirement stays in its owner.
        if (!_retirements.IsClosing) _retirements.Run(RetireWorkspaceOriginalAsync);
    }

    public async Task RetireEditorAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(() =>
        {
            var original = _originalEditor;
            return original is null ? RetireNoEditorOriginalAsync() : RetireAcquiredEditorOriginalAsync(original);
        });
        var actual = await dispatch;
        if (actual is null) throw new InvalidOperationException("The original editor retirement supplied no task.");
        await actual;
    }

    private Task RetireNoEditorOriginalAsync()
    {
        if (_editor is not null || _editorHost is not null)
            throw new InvalidOperationException("Acquired editor custody is missing; native teardown is refused.");
        return Task.CompletedTask; // Explicitly no acquired editor; this is not provider/native-exit proof.
    }

    private async Task RetireAcquiredEditorOriginalAsync(StudioOriginalEditorLifetime original)
    {
        var actual = original.CloseAndDrainAsync();
        await actual;
        if (!actual.IsCompletedSuccessfully || !ReferenceEquals(_originalEditor, original))
            throw new InvalidOperationException("The original editor close or current reference is not established.");
        _editorContent.Content = null;
        if (!ReferenceEquals(_originalEditor, original))
            throw new InvalidOperationException("The original editor changed during native withdrawal.");
        _editorHost = null; _editor = null; _session = null; _originalEditor = null;
    }

    public Task StartOriginalInitializationAsync(CancellationToken caller = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_originalInitialization is not null) return _originalInitialization;
        RequireOriginalPublication(caller);
        return _callbacks.Run(InitializeAsync, caller, actual =>
        {
            _originalInitialization = actual; Initialization = actual;
        });
    }

    public Task? OriginalNativeCloseObservationTask => _nativeCloseObservation;

    public void AuthorizeOriginalNativeClose(Task actualNativeOwnerClose)
    {
        Dispatcher.UIThread.VerifyAccess();
        var produced = _nativeOwnerTask ?? _nativeOwnerClose?.Invoke()
            ?? throw new InvalidOperationException("The original native owner close is absent.");
        if (!ReferenceEquals(produced, actualNativeOwnerClose) || !produced.IsCompletedSuccessfully ||
            _close is not { IsCompletedSuccessfully: true } || _nativeCallbackFailures.Count != 0 ||
            _nativeCloseObservation is { IsCompletedSuccessfully: false })
            throw new InvalidOperationException("The original native-owner/window observer proof is incomplete.");
        _nativeOwnerTask = produced;
        _nativeCloseAuthorized = true;
    }

    public Task? OriginalInitializationTask => _originalInitialization;
    public Task? OriginalCloseTask => _close;
    public Task? OriginalNativeOwnerCloseTask => _nativeOwnerTask;
    public bool NativeClosedWithoutOriginalDrain => _nativeClosedWithoutDrain;

    public void BindOriginalNativeOwnerClose(Func<Task> close)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(close);
        if (_nativeOwnerClose is not null || _retiring)
            throw new InvalidOperationException("The original native owner is already bound or retired.");
        _nativeOwnerClose = close;
    }

    private void OnOriginalOpened(object? sender, EventArgs args)
    {
        if (_retiring) return;
        try
        {
            _ = StartOriginalInitializationAsync();
        }
        catch (Exception error) { RetainNativeCallbackFailure(error); }
    }

    private void OnOriginalActivated(object? sender, EventArgs args)
    {
        if (_retiring || _workspaceActor is null) return;
        try { _callbacks.Run(ValidateWorkspaceOriginalAsync); }
        catch (Exception error) { RetainNativeCallbackFailure(error); }
    }

    private void RetainNativeCallbackFailure(Exception error)
    {
        StudioOriginalCallbackLifetime.Add(_nativeCallbackFailures, error);
        // A refused synchronous admission is retained; no resource teardown is authorized.
        _retiring = true;
    }

    private void OnOriginalClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_nativeCloseAuthorized) return;
        args.Cancel = true;
        if (_nativeCloseObservation is not null) return;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Publish before calling a native owner that can synchronously reenter Close.
        _nativeCloseObservation = ObserveOriginalNativeCloseAsync(start.Task);
        start.SetResult();
    }

    private async Task ObserveOriginalNativeCloseAsync(Task start)
    {
        await start;
        try
        {
            var actual = _nativeOwnerClose?.Invoke()
                ?? throw new InvalidOperationException("The original native owner close is unavailable.");
            _nativeOwnerTask = actual;
            await actual;
            if (!ReferenceEquals(_nativeOwnerTask, actual) || !actual.IsCompletedSuccessfully ||
                _close is not { IsCompletedSuccessfully: true } || _nativeCallbackFailures.Count != 0)
                throw new InvalidOperationException("Actual native-owner/window close settlement is missing.");
        }
        catch (Exception error) { RetainNativeCallbackFailure(error); throw; }
        // Native Close is a separate captured final UI action after this observer settles.
    }

    public Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_close is not null) return _close;
        _retiring = true;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = CloseOriginalAsync(start.Task);
        start.SetResult();
        return _close;
    }

    private async Task CloseOriginalAsync(Task start)
    {
        await start;
        List<Exception> errors = [];
        List<Task> actualCloses = [];
        var capturedEditor = _originalEditor;
        var expectedActualCloses = capturedEditor is null ? 3 : 4;
        Capture(() => _callbacks.CloseAndDrainAsync());
        Capture(() => _retirements.CloseAndDrainAsync());
        if (capturedEditor is not null) Capture(capturedEditor.CloseAndDrainAsync);
        // This is the actual Home-owned producer port, not IDisposable as a settlement proxy.
        Capture(_approvals.CloseAndDrainAsync);
        try { _lifetime.Cancel(); } catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        foreach (var actual in actualCloses)
            try { await actual; }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        foreach (var error in _nativeCallbackFailures) StudioOriginalCallbackLifetime.Add(errors, error);
        if (actualCloses.Count != expectedActualCloses ||
            !_callbacks.OriginalsCapturedAndSettled || !_retirements.OriginalsCapturedAndSettled)
            StudioOriginalCallbackLifetime.Add(errors, new InvalidOperationException("Actual window producer settlement is missing."));
        // Original save/currentness failures leave their draft and acquired resources owned.
        StudioOriginalCallbackLifetime.Throw(errors);
        if (_originalEditor is { } captured)
            await RetireAcquiredEditorOriginalAsync(captured);
        _shell.Dispose();
        _operations.Dispose();
        _lifetime.Dispose();

        void Capture(Func<Task> close)
        {
            try
            {
                var actual = close();
                if (actual is null) throw new InvalidOperationException("An original native producer supplied no close task.");
                actualCloses.Add(actual);
            }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        }
    }

    private sealed class ConfigurationReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "studio-configuration", "Select canonical storage and review its ownership.")); }
    }
    private sealed class SessionReadiness(StudioNativeWindow window) : ICuiSceneReadiness
    {
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) { await window.RequireSessionAsync(ct); return new(CuiSceneAvailabilityState.Ready, "studio-authorised-den", "Current Home actor and Den ownership verified."); }
    }
}
