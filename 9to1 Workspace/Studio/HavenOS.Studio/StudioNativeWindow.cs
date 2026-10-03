using System.Globalization;
using Dulche.Runtime.Agents;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
    private readonly StudioDenLifetime _den;
    private readonly CuiViewModel _model = new();
    private readonly CuiSceneHost _shell;
    private readonly StudioAuthoredTextObservation _authoredInputs;
    private static readonly IReadOnlyDictionary<string, string> AuthoredTextBindings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["studio-AgentName"] = "AgentName",
        ["studio-AgentDescription"] = "AgentDescription",
        ["studio-AgentInstructions"] = "AgentInstructions",
        ["studio-ModelID"] = "ModelID",
        ["studio-ProviderID"] = "ProviderID",
        ["studio-RequiredCapabilities"] = "RequiredCapabilities",
        ["studio-MaxTokens"] = "MaxTokens",
        ["studio-MaxTimeSeconds"] = "MaxTimeSeconds",
        ["studio-MaxSteps"] = "MaxSteps",
        ["studio-MaxToolCalls"] = "MaxToolCalls",
        ["studio-MaxCost"] = "MaxCost",
        ["studio-ToolIDs"] = "ToolIDs",
        ["studio-SkillIDs"] = "SkillIDs",
        ["studio-PluginIDs"] = "PluginIDs",
        ["studio-McpCapabilityIDs"] = "McpCapabilityIDs",
        ["studio-AllowedPermissions"] = "AllowedPermissions",
    };
    private bool _settingBuilderFields;
    private static readonly IReadOnlySet<string> AuthoredBuilderFields = AuthoredTextBindings.Values
        .Concat(["InheritModel", "AllowCloud", "AllowFallback"]).ToHashSet(StringComparer.Ordinal);
    private readonly ContentControl _editorContent = new();
    private readonly HomeApprovalCuiSurface _approvals;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private AgentAvatarEditor? _editor;
    private CancellationTokenSource? _avatarLifetime;
    private readonly List<Task> _retiredAvatarTasks = [];
    private StudioOriginalValidationWork _validations;
    private StudioOriginalTaskDrain? _selectedContextRetirement;
    private StudioOriginalTaskDrain? _shutdown;
    private StudioOriginalTaskDrain? _standaloneClose;
    private Func<Task>? _applicationShutdown;
    private bool _allowPreparedClose, _initializationPendingAtShutdown;
    private StudioAuthoredTextObservation? _avatarAuthoredInputs;
    private List<AgentAvatarPreviewControl> _avatarPreviewControls = [];
    private static readonly IReadOnlyDictionary<string, string> AvatarAuthoredTextBindings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["studio-avatar-NamespaceID"] = "NamespaceID",
        ["studio-avatar-AgentID"] = "AgentID",
        ["studio-avatar-StaticAsset"] = "StaticAsset",
        ["studio-avatar-AccessibleName"] = "AccessibleName",
        ["studio-avatar-InitialState"] = "InitialState",
        ["studio-avatar-StateID"] = "StateID",
        ["studio-avatar-StateLabel"] = "StateLabel",
        ["studio-avatar-StateAsset"] = "StateAsset",
        ["studio-avatar-FromState"] = "FromState",
        ["studio-avatar-ToState"] = "ToState",
        ["studio-avatar-TransitionEvent"] = "TransitionEvent",
        ["studio-avatar-ReactionEvent"] = "ReactionEvent",
        ["studio-avatar-ReactionState"] = "ReactionState",
        ["studio-avatar-PreviewEvent"] = "PreviewEvent",
        ["studio-avatar-AvatarActivity"] = "AvatarActivity",
    };
    private ICanonicalAgentBuilderAdapter? _agentBuilder;
    private CanonicalAgentDefinition? _openedAgentDefinition;
    private AgentCommitRecovery? _committedRecovery;
    private sealed record AgentCommitRecovery(CanonicalAgentCommitReceipt Receipt, HomePersonalDenSession OriginalSession);
    private CuiSceneHost? _editorHost;
    private HomePersonalDenSession? _session;
    private AuthenticatedResourceActor? _workspaceActor;
    private string? _selectedDen, _pendingImport, _pendingImportAudit;
    private AgentDefinitionRecord[] _agents = [];
    public Task Initialization { get; private set; } = Task.CompletedTask;

    public StudioNativeWindow(IServiceProvider services)
    {
        _services = services; _den = Get<StudioDenLifetime>();
        _validations = new(_lifetime.Token);
        Title = "AI Studio"; Width = 1100; Height = 850;
        _approvals = new(Get<HomeCoreRuntime>(), Get<HomeLocalProfileIdentity>(), Get<HomePermissionTrustService>());
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("studio.host-approvals", _ => _approvals);
        registry.RegisterObjectRenderer("studio.host-editor", _ => _editorContent);
        _shell = new(registry); Content = _shell;
        _authoredInputs = new(_model, () => !_lifetime.IsCancellationRequested);
        _model.Set("AgentName", ""); _model.Set("AgentNames", Array.Empty<string>()); _model.Set("SelectedAgentIndex", -1);
        SetBuilderFields(null);
        _model.Set("MoreOptionsExpanded", false); _model.Set("MoreOptionsCaption", "More Options ▸");
        _model.PropertyChanged += (_, change) =>
        {
            if (!_settingBuilderFields && change.PropertyName is { } property && AuthoredBuilderFields.Contains(property))
                _model.Set("BuilderSaveState", _openedAgentDefinition is null ? "New draft" : "Unsaved changes");
        };
        _model.Set("Status", "Choose the Den to author. Existing storage requires Home ownership review.");
        Opened += async (_, _) => { try { Initialization = InitializeAsync(_lifetime.Token); await Initialization; } catch (Exception e) { Status(e.Message); } };
        Activated += (_, _) =>
        {
            if (_workspaceActor is null || IsClosing) return;
            // The owning validation scope retains the original operation; no async-void task is discarded.
            _ = _validations.RunAsync(async ct =>
            {
                try { await ReadWorkspaceSessionAsync(ct); }
                catch (Exception primary)
                {
                    List<Exception> failures = [primary];
                    try { await RetireWorkspaceAsync(default); } catch (Exception cleanup) { StudioOriginalTaskDrain.Add(failures, cleanup); }
                    try { Status(primary.Message); } catch (Exception display) { StudioOriginalTaskDrain.Add(failures, display); }
                    StudioOriginalTaskDrain.Throw(failures);
                }
            });
        };
        Closing += (_, args) =>
        {
            if (_allowPreparedClose) return;
            args.Cancel = true;
            _ = _applicationShutdown is null ? CloseStandaloneAsync() : _applicationShutdown();
        };
        Closed += (_, _) =>
        {
            if (_shutdown?.OriginalTasksCapturedAndSettled != true)
                throw new InvalidOperationException("Native Studio closed before its original work was captured and settled.");
        };
    }
    /// <summary>Capture the SAME original shell action pipeline immediately after its native button click. Settlement does not attest success.</summary>
    public Task WhenActionsIdleAsync() => _shell.WhenActionsIdleAsync();
    /// <summary>Capture the SAME original avatar action pipeline immediately after its native button click. Settlement does not attest success.</summary>
    public Task WhenAvatarActionsIdleAsync() => (_editorHost ?? throw new InvalidOperationException("Open an Agent presentation first.")).WhenActionsIdleAsync();
    private IStorageProvider Picker => _services.GetService<IStorageProvider>() ?? StorageProvider;
    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();
    private string Text(string key) => _model.Get(key)?.ToString() ?? "";
    private void Status(string text) => _model.Set("Status", text);
    private async Task InitializeAsync(CancellationToken ct)
    {
        await _approvals.InitializeAsync(ct);
        using var stream = typeof(StudioNativeWindow).Assembly.GetManifestResourceStream("HavenOS.AIStudio.UI.StudioHost.cui")
            ?? throw new InvalidDataException("Studio host CUI source is missing.");
        using var reader = new StreamReader(stream);
        await _shell.ShowAsync(new CuiNativeScene("9to1.Studio", "AI Studio", "Studio", new CuiRichParser().Parse(reader.ReadToEnd()),
            _model, this, new ConfigurationReadiness()), ct);
        _authoredInputs.Bind(_shell, AuthoredTextBindings);
    }
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsClosing) throw new ObjectDisposedException(nameof(StudioNativeWindow));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var ct = linked.Token;
        await _operations.WaitAsync(ct);
        try
        {
            switch (command)
            {
                case "CreateDen": case "OpenDen":
                    var folders = await Picker.OpenFolderPickerAsync(new() { AllowMultiple = false, Title = command == "CreateDen" ? "Choose an empty folder for the new Den" : "Choose an existing Den" });
                    if (folders.Count != 1) return;
                    using (var folder = folders[0])
                    {
                        var path = folder.TryGetLocalPath() ?? throw new NotSupportedException("This Den provider requires an actual local folder.");
                        _selectedDen = await _den.SelectAsync(path, command == "CreateDen", Get<HomeLocalStoreOwnership>(), ct);
                        _validations = new(_lifetime.Token);
                        _selectedContextRetirement = null;
                    }
                    _committedRecovery = null;
                    _pendingImport = null; _pendingImportAudit = null; _agents = []; _model.Set("AgentNames", Array.Empty<string>()); _model.Set("SelectedAgentIndex", -1);
                    if (command == "CreateDen") await RefreshAgentsAsync(null, ct);
                    Status(command == "CreateDen" ? "New personal Den bound to the current Home profile. Create or open an Agent." : "Existing Den selected. Review ownership in Home before opening it.");
                    break;
                case "RequestImport":
                    var request = await _den.RequestExistingImportAsync(Get<HomeLocalStoreOwnership>(), ct);
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
                    _pendingImport = null; await RefreshAgentsAsync(null, ct); Status("Den ownership verified. Create or open an Agent.");
                    break;
                case "RetryImportAudit":
                    var audit = _pendingImportAudit ?? throw new InvalidOperationException("No Den import audit recovery is pending in this session.");
                    await _den.RetryExistingImportAuditAsync(audit, Get<HomeLocalStoreOwnership>(), ct);
                    _pendingImportAudit = null; await RefreshAgentsAsync(null, ct);
                    Status("Den ownership audit recovered. Create or open an Agent.");
                    break;
                case "CreateAgent":
                    var name = Text("AgentName").Trim();
                    if (name.Length is < 1 or > 256) throw new InvalidOperationException("Enter an Agent name of at most 256 characters.");
                    if (_committedRecovery is not null) throw new InvalidOperationException("Recover the saved Agent before creating another draft in this context.");
                    var creationSession = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
                    var creator = Get<ICanonicalAgentBuilderAdapter>();
                    StudioResult<CanonicalAgentReference> created;
                    try { created = await creator.CreateAsync(DraftFromFields(Guid.Empty, 0), ct); }
                    catch (CanonicalAgentCommittedObservationException known)
                    { _committedRecovery = new(known.Receipt, creationSession); throw; }
                    if (!created.IsSuccess) { RefuseBuilder(created.Error!); break; }
                    var createdReceipt = new CanonicalAgentCommitReceipt(created.Value!.AgentId, created.Value.DefinitionRevision, CanonicalAgentCommitKind.CreatedDraft);
                    try
                    {
                        var createdId = created.Value.AgentId.ToString("D");
                        await RefreshAgentsAsync(createdId, ct); await OpenAgentAsync(createdId, ct);
                    }
                    catch (Exception cause)
                    { _committedRecovery = new(createdReceipt, creationSession); throw new CanonicalAgentCommittedObservationException(createdReceipt, cause); }
                    break;
                case "RefreshAgents": await RefreshAgentsAsync(null, ct); break;
                case "OpenAgent":
                    var index = _model.Get("SelectedAgentIndex") is int selected ? selected : -1;
                    if (index < 0 || index >= _agents.Length) throw new InvalidOperationException("Choose an Agent first.");
                    var selectedAgent = _agents[index];
                    await OpenAgentAsync(selectedAgent.Id, ct); break;
                case "RecoverSavedAgent": await RecoverSavedAgentAsync(ct); break;
                case "SaveAgentDraft": await SaveAgentDraftAsync(ct); break;
                case "ValidateAgentDraft": await ValidateAgentDraftAsync(ct); break;
                case "ToggleBuilderMoreOptions":
                    var expanded = !Flag("MoreOptionsExpanded");
                    _model.Set("MoreOptionsExpanded", expanded);
                    _model.Set("MoreOptionsCaption", expanded ? "More Options ▾" : "More Options ▸");
                    break;
                case "ImportAvatar": await ImportAvatarAsync(ct); break;
                default: throw new InvalidOperationException("Unknown Studio workspace action.");
            }
        }
        catch (CanonicalAgentCommittedObservationException known)
        {
            try { await RetireWorkspaceAsync(default); }
            catch (Exception cleanup) { throw new AggregateException(known, cleanup); }
            _model.Set("BuilderSaveState", $"Saved revision {known.Receipt.DefinitionRevision} · recovery required");
            _model.Set("BuilderStatus", known.Message); Status(known.Message);
            throw;
        }
        catch (Exception e)
        {
            if (_workspaceActor is not null && (e is UnauthorizedAccessException || e is DenException { Code: DenErrorCode.Forbidden }))
            {
                try { await RetireWorkspaceAsync(default); }
                catch (Exception cleanup) { throw new AggregateException(e, cleanup); }
            }
            Status(e.Message); throw;
        }
        finally { _operations.Release(); }
    }
    private async Task RefreshAgentsAsync(string? preferredId, CancellationToken ct)
    {
        var observed = await StudioDenObservationBoundary.ReadAgentsAsync(
            token => _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), token), _selectedDen, ct);
        var session = observed.Session; var agents = observed.Agents;
        if (_selectedDen != session.DenId || (_workspaceActor is not null && _workspaceActor != session.Actor) ||
            (_session is not null && !StudioDenObservationBoundary.SameSession(_session, session)))
            throw new UnauthorizedAccessException("The displayed Den or Agent editor belongs to a retired Home session.");
        ct.ThrowIfCancellationRequested();
        if (preferredId is null && _model.Get("SelectedAgentIndex") is int previous && previous >= 0 && previous < _agents.Length)
            preferredId = _agents[previous].Id;
        _workspaceActor = session.Actor;
        _agents = agents;
        _model.Set("AgentNames", agents.Select(item => item.DisplayName).ToArray());
        var index = preferredId is null ? -1 : Array.FindIndex(agents, item => item.Id == preferredId);
        _model.Set("SelectedAgentIndex", index >= 0 ? index : agents.Length == 0 ? -1 : 0);
    }
    private async Task OpenAgentAsync(string id, CancellationToken ct)
    {
        await RetireEditorAsync(ct);
        var session = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
        var assets = new DenAgentPresentationAssets(session.Den);
        _avatarLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var editor = new AgentAvatarEditor(new AgentPresentationService(session.Den, assets), assets,
            RequestWorkspaceInvalidationAsync, ("personal", id), _avatarLifetime.Token);
        await editor.OpenAsync("personal", id, ct);
        _session = session; _workspaceActor = session.Actor; _editor = editor;
        var scene = StudioNativeScene.Create(editor.Bindings, editor, new SessionReadiness(this), editor.Preview, _avatarPreviewControls);
        var host = new CuiSceneHost(scene.ControlRegistry); _editorHost = host; _editorContent.Content = host;
        await host.ShowAsync(scene, ct);
        if (!ReferenceEquals(_editor, editor) || !ReferenceEquals(_editorHost, host))
            throw new UnauthorizedAccessException("The original Agent presentation was retired.");
        var authoredInputs = new StudioAuthoredTextObservation(editor.Bindings, () => !_lifetime.IsCancellationRequested &&
            ReferenceEquals(_editor, editor) && ReferenceEquals(_editorHost, host));
        _avatarAuthoredInputs = authoredInputs;
        authoredInputs.Bind(host, AvatarAuthoredTextBindings);
        SetBuilderFields(null);
        if (Guid.TryParseExact(id, "D", out var agentId) && agentId.ToString("D") == id)
        {
            var builder = Get<ICanonicalAgentBuilderAdapter>();
            var opened = await builder.OpenAsync(agentId, ct);
            await RequireSessionAsync(ct);
            if (!opened.IsSuccess) RefuseBuilder(opened.Error!);
            else { _agentBuilder = builder; _openedAgentDefinition = opened.Value!; SetBuilderFields(opened.Value!); }
        }
        else _model.Set("BuilderStatus", "This existing Agent keeps its original string ID; the shared UUID builder cannot edit it.");
        Status("Opened the canonical Agent. Presentation changes use its saved revision.");
    }
    public Task ValidateWorkspaceAsync(CancellationToken ct) =>
        _validations.RunAsync(async token => { await ReadWorkspaceSessionAsync(token); }, ct);
    private async Task<HomePersonalDenSession> ReadWorkspaceSessionAsync(CancellationToken ct)
    {
        try
        {
            var current = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
            if (_workspaceActor is null || current.Actor != _workspaceActor || current.DenId != _selectedDen)
                throw new UnauthorizedAccessException("The selected Den or Home actor changed.");
            if (_session is not null) StudioDenObservationBoundary.RequireSameSession(_session, current);
            ct.ThrowIfCancellationRequested();
            return current;
        }
        catch (Exception primary)
        {
            try { await RetireWorkspaceAsync(default); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }
    private async Task RequireSessionAsync(CancellationToken ct)
    {
        var current = await ReadWorkspaceSessionAsync(ct);
        if (_session is null || _session.Actor != _workspaceActor || _session.DenId != _selectedDen)
            throw new UnauthorizedAccessException("The Agent editor belongs to a different Den or Home actor.");
        StudioDenObservationBoundary.RequireSameSession(_session, current);
    }
    private async Task ImportAvatarAsync(CancellationToken ct)
    {
        await RequireSessionAsync(ct);
        var editor = _editor ?? throw new InvalidOperationException("Open an Agent first.");
        var identity = editor.CurrentAgentIdentity ?? throw new InvalidOperationException("Open an Agent first.");
        var session = _session!;
        var files = await Picker.OpenFilePickerAsync(new() { AllowMultiple = false, Title = "Import an avatar image into this Agent", FileTypeFilter = [FilePickerFileTypes.ImageAll] });
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
            var mime = await Task.Run(() => { using var decoder = new PictureGlycinSharedRasterDecoder().OpenFrames(bytes, false, ct); return decoder.MimeType; }, ct);
            await RequireSessionAsync(ct);
            if (!ReferenceEquals(_editor, editor) || editor.CurrentAgentIdentity != identity)
                throw new InvalidOperationException("The Agent selection changed while choosing its avatar image.");
            var attachment = await session.Den.AddAttachmentAsync(identity.NamespaceID, identity.AgentID, DenAgentPresentationAssets.AgentOwnerKind, mime, bytes, Guid.NewGuid().ToString("N"), ct);
            if (!ReferenceEquals(_editor, editor) || editor.CurrentAgentIdentity != identity)
            {
                Status("The image was imported for the previously selected Agent. The current selection was preserved.");
                return;
            }
            editor.Bindings.Set("StaticAsset", attachment.Id);
            editor.Bindings.Set("StateAsset", attachment.Id);
            Status("Image copied into this Agent's Den attachments. Apply the avatar identity or state, then save the presentation.");
        }
        finally { if (bytes is not null) Array.Clear(bytes); }
    }
    public bool IsClosing => _shutdown?.IsClosing == true;
    public bool OriginalShutdownTasksCapturedAndSettled => _shutdown?.OriginalTasksCapturedAndSettled == true;
    public void ConfigureShutdownRequest(Func<Task> request) =>
        _applicationShutdown = request ?? throw new ArgumentNullException(nameof(request));
    public void AllowPreparedClose()
    {
        if (!OriginalShutdownTasksCapturedAndSettled)
            throw new InvalidOperationException("Original Studio task capture and settlement are required before native close.");
        _allowPreparedClose = true;
    }
    public Task PrepareShutdownAsync()
    {
        _shutdown ??= new StudioOriginalTaskDrain(() => { },
            [
                () => { _initializationPendingAtShutdown = !Initialization.IsCompleted; return Initialization; },
                () => _shell.WhenActionsIdleAsync(),
                () => _editorHost?.WhenActionsIdleAsync(),
                () => _validations.CloseAndDrainAsync()
            ],
            () => _lifetime.Cancel(),
            [
                () => RetireWorkspaceAsync(default),
                DrainRetiredAvatarsAsync,
                () => { StudioOriginalTaskDrain.RunSynchronous([_authoredInputs.Dispose, _approvals.Dispose, _shell.Dispose, _lifetime.Dispose]); return Task.CompletedTask; }
            ],
            error => _initializationPendingAtShutdown && _lifetime.IsCancellationRequested &&
                error.CancellationToken == _lifetime.Token);
        return _shutdown.CloseAndDrainAsync();
    }
    private Task CloseStandaloneAsync()
    {
        _standaloneClose ??= new StudioOriginalTaskDrain(() => { }, [PrepareShutdownAsync], () => { },
            [() => { AllowPreparedClose(); Close(); return Task.CompletedTask; }]);
        return _standaloneClose.CloseAndDrainAsync();
    }
    /// <summary>Replacement does not wait for the SelectDen shell action which is itself awaiting this callback.
    /// It cancels and settles the original current-context validation and embedded avatar actions.</summary>
    public Task RetireSelectedContextAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _selectedContextRetirement ??= new StudioOriginalTaskDrain(() => { },
            [() => _validations.CloseAndDrainAsync(), () => RetireWorkspaceAsync(default)],
            () => { }, [DrainRetiredAvatarsAsync]);
        return _selectedContextRetirement.CloseAndDrainAsync();
    }
    // Called from inside the original child action. Its retirement task is owned and awaited at
    // replacement/shutdown; the child must not await a drain containing itself.
    private Task RequestWorkspaceInvalidationAsync(CancellationToken ct)
    {
        var original = RetireWorkspaceAsync(ct);
        _retiredAvatarTasks.Add(original);
        return Task.CompletedTask;
    }
    public Task RetireWorkspaceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _selectedDen = null; _pendingImport = null; _pendingImportAudit = null; _workspaceActor = null; _agents = [];
        var originalEditor = BeginRetireEditor();
        return StudioOriginalTaskDrain.RunIndependentAsync([
            () => StudioOwnedCleanup.RunAsync([
                () => _model.Set("AgentNames", Array.Empty<string>()),
                () => _model.Set("SelectedAgentIndex", -1), () => _model.Set("AgentName", "")], null, null),
            () => originalEditor]);
    }
    public Task RetireEditorAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return BeginRetireEditor();
    }
    private Task BeginRetireEditor()
    {
        var host = _editorHost; var preview = _editor?.Preview; var authoredInputs = _avatarAuthoredInputs;
        var lifetime = _avatarLifetime;
        var previewControls = _avatarPreviewControls.ToArray(); _avatarPreviewControls = [];
        _editor = null; _editorHost = null; _session = null; _avatarAuthoredInputs = null; _avatarLifetime = null;
        _agentBuilder = null; _openedAgentDefinition = null;
        var originals = new List<Func<Task?>> { () => host?.WhenActionsIdleAsync() };
        originals.AddRange(previewControls.Select(control => (Func<Task?>)(() => control.WhenAnimationIdleAsync())));
        var drain = new StudioOriginalTaskDrain(() => { }, originals,
            () => StudioOriginalTaskDrain.RunSynchronous([
                () => authoredInputs?.Dispose(), () => _editorContent.Content = null, () => SetBuilderFields(null),
                () => StudioOriginalTaskDrain.RunSynchronous(previewControls.Select(control => (Action)control.Dispose).ToArray()),
                () => lifetime?.Cancel(), () => host?.Dispose()]),
            [
                () => StudioOriginalTaskDrain.RunIndependentAsync(previewControls.Select(control =>
                    (Func<Task>)(() => control.CloseAndDrainAsync())).ToArray()),
                () => preview is null ? Task.CompletedTask : preview.DisposeAsync().AsTask(),
                () => { lifetime?.Dispose(); return Task.CompletedTask; }
            ], error => previewControls.Any(control => control.IsObservedOwnedCancellation(error)));
        var original = drain.CloseAndDrainAsync();
        _retiredAvatarTasks.RemoveAll(task => task.IsCompletedSuccessfully);
        _retiredAvatarTasks.Add(original);
        return original;
    }
    private Task DrainRetiredAvatarsAsync() => StudioOriginalTaskDrain.RunIndependentAsync(
        _retiredAvatarTasks.Select(original => (Func<Task>)(() => original)).ToArray());
    private void RefuseBuilder(StudioError error)
    {
        _model.Set("BuilderStatus", error.Message);
        if (error.Code == "Forbidden") throw new UnauthorizedAccessException(error.Message);
        Status(error.Message);
    }
    private async Task SaveAgentDraftAsync(CancellationToken ct)
    {
        await RequireSessionAsync(ct);
        var opened = _openedAgentDefinition ?? throw new InvalidOperationException("Open a canonical UUID Agent first.");
        var builder = _agentBuilder ?? throw new InvalidOperationException("The canonical authoring session is unavailable.");
        var originalSession = _session!;
        StudioResult<CanonicalAgentDefinition> result;
        try { result = await builder.UpdateDraftAsync(opened.AgentId, opened.DefinitionRevision,
            DraftFromFields(opened.AgentId, opened.DefinitionRevision), ct); }
        catch (CanonicalAgentCommittedObservationException known)
        { _committedRecovery = new(known.Receipt, originalSession); throw; }
        if (!result.IsSuccess) { await RequireSessionAsync(ct); RefuseBuilder(result.Error!); return; }
        var receipt = new CanonicalAgentCommitReceipt(result.Value!.AgentId, result.Value.DefinitionRevision, CanonicalAgentCommitKind.UpdatedDraft);
        try
        {
            await RequireSessionAsync(ct); _openedAgentDefinition = result.Value; SetBuilderFields(result.Value);
            await RefreshAgentsAsync(opened.AgentId.ToString("D"), ct);
            Status("Agent draft saved in the selected Den. Reopen the Agent before saving avatar edits at its new revision.");
        }
        catch (Exception cause)
        { _committedRecovery = new(receipt, originalSession); throw new CanonicalAgentCommittedObservationException(receipt, cause); }
    }
    private async Task RecoverSavedAgentAsync(CancellationToken ct)
    {
        var recovery = _committedRecovery ?? throw new InvalidOperationException("No known saved Agent needs recovery.");
        var current = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
        if (current.Actor != recovery.OriginalSession.Actor || current.DenId != recovery.OriginalSession.DenId ||
            !ReferenceEquals(current.Den.Store, recovery.OriginalSession.Den.Store))
            throw new UnauthorizedAccessException("Recover this saved Agent only in its original authorised Den and Home session.");
        _selectedDen = current.DenId;
        await RefreshAgentsAsync(recovery.Receipt.AgentId.ToString("D"), ct);
        await OpenAgentAsync(recovery.Receipt.AgentId.ToString("D"), ct);
        await RequireSessionAsync(ct);
        if (_openedAgentDefinition is null || _openedAgentDefinition.AgentId != recovery.Receipt.AgentId ||
            _openedAgentDefinition.DefinitionRevision < recovery.Receipt.DefinitionRevision)
            throw new InvalidOperationException("The known committed Agent revision could not be observed. Its receipt remains available; do not repeat creation.");
        _committedRecovery = null;
        Status("Reopened the saved canonical Agent. Creation and saving were not repeated.");
    }
    private async Task ValidateAgentDraftAsync(CancellationToken ct)
    {
        await RequireSessionAsync(ct);
        var opened = _openedAgentDefinition ?? throw new InvalidOperationException("Open a canonical UUID Agent first.");
        var result = await (_agentBuilder ?? throw new InvalidOperationException("The canonical authoring session is unavailable."))
            .ValidateAsync(opened.AgentId, opened.DefinitionRevision, ct);
        await RequireSessionAsync(ct);
        if (!result.IsSuccess) { RefuseBuilder(result.Error!); return; }
        _model.Set("BuilderStatus", result.Value!.IsValid ? "Current canonical validation passed." :
            string.Join(" ", result.Value.Issues.Select(issue => issue.Message)));
    }
    private bool Flag(string key) => _model.Get(key) is true;
    private void SetBuilderFields(CanonicalAgentDefinition? definition)
    {
        _settingBuilderFields = true;
        try { SetBuilderFieldsCore(definition); }
        finally { _settingBuilderFields = false; }
    }
    private void SetBuilderFieldsCore(CanonicalAgentDefinition? definition)
    {
        var config = definition is null ? new DenAgentBuilderConfiguration() :
            DenCanonicalAgentBuilderAdapter.DecodeConfiguration(definition.ConfigurationJson);
        _model.Set("AgentName", definition?.Name ?? "");
        _model.Set("AgentDescription", definition?.Description ?? "");
        _model.Set("AgentInstructions", definition?.Instructions ?? "");
        _model.Set("AgentIdentity", definition is null ? "No Agent open" : $"{definition.AgentId:D} · revision {definition.DefinitionRevision}");
        _model.Set("BuilderStatus", "Draft authoring is separate from current registry validation and execution permission.");
        _model.Set("InheritModel", config.ModelPolicy.Inherit); _model.Set("ModelID", config.ModelPolicy.ModelId ?? "");
        _model.Set("ProviderID", config.ModelPolicy.ProviderId ?? ""); _model.Set("AllowCloud", config.ModelPolicy.AllowCloud);
        _model.Set("AllowFallback", config.ModelPolicy.AllowFallback);
        _model.Set("RequiredCapabilities", string.Join("\n", config.ModelPolicy.RequiredCapabilities ?? new HashSet<string>()));
        _model.Set("MaxTokens", config.Budget.MaxTokens?.ToString(CultureInfo.InvariantCulture) ?? "");
        _model.Set("MaxTimeSeconds", config.Budget.MaxTime is { } time ? ((decimal)time.Ticks / TimeSpan.TicksPerSecond).ToString(CultureInfo.InvariantCulture) : "");
        _model.Set("MaxSteps", config.Budget.MaxSteps?.ToString(CultureInfo.InvariantCulture) ?? "");
        _model.Set("MaxToolCalls", config.Budget.MaxToolCalls?.ToString(CultureInfo.InvariantCulture) ?? "");
        _model.Set("MaxCost", config.Budget.MaxCost?.ToString(CultureInfo.InvariantCulture) ?? "");
        _model.Set("ToolIDs", string.Join("\n", config.ToolIds)); _model.Set("SkillIDs", string.Join("\n", config.SkillIds));
        _model.Set("PluginIDs", string.Join("\n", config.PluginIds)); _model.Set("McpCapabilityIDs", string.Join("\n", config.McpCapabilityIds));
        _model.Set("AllowedPermissions", string.Join("\n", config.AllowedPermissions));
        _model.Set("BuilderSaveState", definition is null ? "New draft" : $"Saved revision {definition.DefinitionRevision}");
        _model.Set("AgentAvailabilitySummary", config.AvailabilityBindings is null ? "Availability has not been set" :
            config.AvailabilityBindings.Count == 0 ? "No surfaces selected" :
            string.Join(" · ", config.AvailabilityBindings.Select(binding => binding.Scope == AgentAvailabilityScope.Global ? "Global" : $"{binding.Scope}: {binding.TargetId}")));
        _model.Set("AgentMemorySummary", config.MemoryPolicy is null ? "Memory has not been configured" :
            $"{config.MemoryPolicy.NamespaceId} · {config.MemoryPolicy.ScopeKind} · {config.MemoryPolicy.ScopeId}");
        _model.Set("AgentKnowledgeSummary", config.KnowledgeReferences.Count == 0 ? "No linked knowledge sources" :
            string.Join("\n", config.KnowledgeReferences.Select(reference => $"{reference.DisplayName ?? reference.CanonicalId} · {reference.OwningApp} · revision {reference.Revision} · {reference.AccessScope}")));
        _model.Set("AgentQuickActionSummary", config.QuickActions.Count == 0 ? "No manual quick actions" :
            string.Join("\n", config.QuickActions.OrderBy(action => action.Order).ThenBy(action => action.QuickActionId)
                .Select(action => $"{action.Label} · {action.QuickActionId:D} · {action.ConfirmationPolicy}")));
    }
    private CanonicalAgentDefinition DraftFromFields(Guid id, long revision)
    {
        static long? Count(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)) return count;
            throw new InvalidOperationException("Budget counts must be nonnegative whole numbers or empty.");
        }
        static decimal? Amount(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount >= 0) return amount;
            throw new InvalidOperationException("Budget amounts must be nonnegative decimals or empty.");
        }
        static TimeSpan? Time(string value)
        {
            var seconds = Amount(value); if (seconds is null) return null;
            if (seconds > (decimal)long.MaxValue / TimeSpan.TicksPerSecond)
                throw new InvalidOperationException("The time budget is too large.");
            var ticks = seconds.Value * TimeSpan.TicksPerSecond;
            if (decimal.Truncate(ticks) != ticks) throw new InvalidOperationException("Time budget precision must be at most seven decimal places.");
            return TimeSpan.FromTicks((long)ticks);
        }
        static string[] IDs(string value) => value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var capabilities = IDs(Text("RequiredCapabilities"));
        // Retain untouched canonical declarations while these existing native fields are edited.
        // A missing/mismatched opened basis must refuse rather than drop newer metadata.
        var basis = id == Guid.Empty ? new DenAgentBuilderConfiguration() :
            _openedAgentDefinition is { } opened && opened.AgentId == id && opened.DefinitionRevision == revision ?
                DenCanonicalAgentBuilderAdapter.DecodeConfiguration(opened.ConfigurationJson) :
                throw new InvalidOperationException("The original opened Agent revision is required to edit its declarations.");
        var config = basis with
        {
            ModelPolicy = new AgentModelPolicy(Flag("InheritModel"), Text("ModelID").Length == 0 ? null : Text("ModelID"),
                Text("ProviderID").Length == 0 ? null : Text("ProviderID"), Flag("AllowCloud"), Flag("AllowFallback"),
                capabilities.Length == 0 ? null : capabilities.ToHashSet(StringComparer.Ordinal)),
            Budget = new AgentBudgetLimits(Count(Text("MaxTokens")), Time(Text("MaxTimeSeconds")),
                Count(Text("MaxSteps")), Count(Text("MaxToolCalls")), Amount(Text("MaxCost"))),
            ToolIds = IDs(Text("ToolIDs")), SkillIds = IDs(Text("SkillIDs")), PluginIds = IDs(Text("PluginIDs")),
            McpCapabilityIds = IDs(Text("McpCapabilityIDs")), AllowedPermissions = IDs(Text("AllowedPermissions"))
        };
        return new(id, Text("AgentName"), Text("AgentDescription"), Text("AgentInstructions"),
            DenCanonicalAgentBuilderAdapter.EncodeConfiguration(config), revision, true);
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
