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
    private readonly StudioDenLifetime _den;
    private readonly CuiViewModel _model = new();
    private readonly CuiSceneHost _shell;
    private readonly ContentControl _editorContent = new();
    private readonly HomeApprovalCuiSurface _approvals;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private AgentAvatarEditor? _editor;
    private CuiSceneHost? _editorHost;
    private HomePersonalDenSession? _session;
    private AuthenticatedResourceActor? _workspaceActor;
    private string? _selectedDen, _pendingImport, _pendingImportAudit;
    private AgentDefinitionRecord[] _agents = [];
    public Task Initialization { get; private set; } = Task.CompletedTask;

    public StudioNativeWindow(IServiceProvider services)
    {
        _services = services; _den = Get<StudioDenLifetime>();
        Title = "AI Studio"; Width = 1100; Height = 850;
        _approvals = new(Get<HomeCoreRuntime>(), Get<HomeLocalProfileIdentity>(), Get<HomePermissionTrustService>());
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("studio.host-approvals", _ => _approvals);
        registry.RegisterObjectRenderer("studio.host-editor", _ => _editorContent);
        _shell = new(registry); Content = _shell;
        _model.Set("AgentName", ""); _model.Set("AgentNames", Array.Empty<string>()); _model.Set("SelectedAgentIndex", -1);
        _model.Set("Status", "Choose the Den to author. Existing storage requires Home ownership review.");
        Opened += async (_, _) => { try { Initialization = InitializeAsync(_lifetime.Token); await Initialization; } catch (Exception e) { Status(e.Message); } };
        Activated += async (_, _) =>
        {
            if (_workspaceActor is null) return;
            try { await ValidateWorkspaceAsync(_lifetime.Token); }
            catch (Exception e) { await RetireWorkspaceAsync(default); Status(e.Message); }
        };
        Closed += (_, _) => { _lifetime.Cancel(); _approvals.Dispose(); _shell.Dispose(); };
    }
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
    }
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
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
                    }
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
                    var newSession = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
                    var created = await newSession.Den.SaveAsync(new AgentDefinitionRecord { Id = Guid.NewGuid().ToString("D"), NamespaceId = "personal", DisplayName = name, Version = "1" }, 0, Guid.NewGuid().ToString("N"), ct);
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
        catch (Exception e)
        {
            if (_workspaceActor is not null && (e is UnauthorizedAccessException || e is DenException { Code: DenErrorCode.Forbidden }))
                await RetireWorkspaceAsync(default);
            Status(e.Message); throw;
        }
        finally { _operations.Release(); }
    }
    private async Task RefreshAgentsAsync(string? preferredId, CancellationToken ct)
    {
        var session = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
        var agents = (await session.Den.ListAsync<AgentDefinitionRecord>("personal", ct)).OrderBy(item => item.DisplayName, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
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
        var editor = new AgentAvatarEditor(new AgentPresentationService(session.Den, assets), assets, RetireWorkspaceAsync);
        await editor.OpenAsync("personal", id, ct);
        _session = session; _workspaceActor = session.Actor; _editor = editor;
        var scene = StudioNativeScene.Create(editor.Bindings, editor, new SessionReadiness(this), editor.Preview);
        _editorHost = new(scene.ControlRegistry); _editorContent.Content = _editorHost;
        await _editorHost.ShowAsync(scene, ct);
        Status("Opened the canonical Agent. Presentation changes use its saved revision.");
    }
    public async Task ValidateWorkspaceAsync(CancellationToken ct)
    {
        try
        {
            var current = await _den.OpenBoundSessionAsync(Get<IResourceStoreOwnershipReceiptAuthority>(), ct);
            if (_workspaceActor is null || current.Actor != _workspaceActor || current.DenId != _selectedDen)
                throw new UnauthorizedAccessException("The selected Den or Home actor changed.");
        }
        catch { await RetireWorkspaceAsync(default); throw; }
    }
    private async Task RequireSessionAsync(CancellationToken ct)
    {
        await ValidateWorkspaceAsync(ct);
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
    public async Task RetireWorkspaceAsync(CancellationToken ct)
    {
        await RetireEditorAsync(ct);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _selectedDen = null; _pendingImport = null; _pendingImportAudit = null; _workspaceActor = null; _agents = [];
            _model.Set("AgentNames", Array.Empty<string>()); _model.Set("SelectedAgentIndex", -1); _model.Set("AgentName", "");
        });
    }
    public async Task RetireEditorAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var editor = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var retired = _editor; _editor = null; _session = null;
            _editorContent.Content = null; _editorHost?.Dispose(); _editorHost = null;
            return retired;
        });
        if (editor?.Preview is not null) await editor.Preview.DisposeAsync();
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
