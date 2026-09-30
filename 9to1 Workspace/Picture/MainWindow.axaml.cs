using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core.Media;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;

namespace HavenOS.Images;

public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CuiViewModel _model = new();
    private readonly CuiSceneHost _shell;
    private readonly ContentControl _content = new();
    private readonly HomeApprovalCuiSurface _approvals;
    private NativeFilesWorkspace? _workspace;
    private NativeFilesWorkspaceConfiguration? _configuration;
    private string? _pendingOwnership;
    private bool _requestUncertain;
    private HostedItemMetadata[] _documents = [];
    private int _selected;
    private string? _nextPage;
    private PictureFilesArtifactBridge? _files;
    private PictureFilesSourceRenderer? _renderer;
    private PictureHomeImportOperation? _import;
    private PictureHomePngExportOperation? _export;
    private PictureHomeEditOperation? _edits;
    private PictureFilesOpenResult? _opened;
    private IDisposable? _view;
    private string? _pendingRequest;
    private JsonElement _pendingArguments;
    private Func<HomeResourceExecutionCapability, CancellationToken, Task<PictureFilesOpenResult?>>? _pendingExecute;
    private IDisposable? _pendingDisposable;
    private bool _busy, _ready, _closed;
    public Task Initialization { get; private set; } = Task.CompletedTask;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        Title = "Picture"; Width = 1200; Height = 800; MinWidth = 800; MinHeight = 560;
        _approvals = new(Get<HomeCoreRuntime>(), Get<HomeLocalProfileIdentity>(), Get<HomePermissionTrustService>());
        var controls = new CuiControlRegistry();
        controls.RegisterControlType("PictureHostContent", _ => _content);
        controls.RegisterControlType("PictureHostApprovals", _ => _approvals);
        _shell = new(controls);
        _model.Set("CropX", "0"); _model.Set("CropY", "0"); _model.Set("Width", "1"); _model.Set("Height", "1");
        Content = _shell;
        Opened += async (_, _) =>
        {
            try { Initialization = InitializeAsync(_lifetime.Token); await Initialization; }
            catch (Exception error) { SetStatus(error.Message); }
        };
        Activated += async (_, _) =>
        {
            if (_view is not PictureNativeCuiSurface surface) return;
            try { await surface.ValidateAccessAsync(_lifetime.Token); }
            catch (Exception error) { _ready = false; SetStatus(error.Message); RefreshBindings(); }
        };
        Closed += (_, _) =>
        {
            _closed = true; _lifetime.Cancel(); _view?.Dispose(); _pendingDisposable?.Dispose();
            _approvals.Dispose(); _shell.Dispose(); _lifetime.Dispose();
        };
    }
    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();
    // A compiled native host may supply its platform picker; app actions never supply paths or picker results.
    private IStorageProvider NativePicker => _services.GetService<IStorageProvider>() ?? StorageProvider;
    private bool WriteAvailable() => !_closed && _ready && _workspace is not null;
    private async Task InitializeAsync(CancellationToken ct)
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("HavenOS.Images.UI.PictureHost.cui")
            ?? throw new InvalidDataException("The Picture host CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(await reader.ReadToEndAsync(ct));
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error)) throw new InvalidDataException("Picture host CUI is invalid.");
        await _shell.ShowAsync(new("picture", "Picture", "Picture", document, _model, new Actions(this), new HostReadiness(this)), ct);
        await _approvals.InitializeAsync(ct);
        await RefreshAsync(null, ct);
    }
    private async ValueTask<CuiSceneAvailability> CheckHostAsync(CancellationToken ct)
    {
        var profiles = Get<HomeLocalProfileIdentity>();
        var actor = await profiles.GetCurrentAsync(ct);
        var snapshot = await Get<HomeCoreRuntime>().StartAsync(ct);
        _ready = actor is not null && actor == await profiles.GetCurrentAsync(ct) &&
            new[] { "home.core", "home.state", "permissions.trust" }.All(id => snapshot.Services.Any(service => service.ServiceId == id &&
                service.IsAvailable && service.State == HomeServiceLifecycleState.Ready &&
                service.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major &&
                service.ContractVersion.Minor >= HomeCoreServiceCatalog.CurrentContractVersion.Minor));
        return new(_ready ? CuiSceneAvailabilityState.Ready : CuiSceneAvailabilityState.Unavailable,
            _ready ? "PictureHomeReady" : "PictureHomeUnavailable", _ready ? "Picture is ready." : "Open Home to recover this profile and its services.");
    }
    private async Task RefreshAsync(string? page, CancellationToken ct)
    {
        await CheckHostAsync(ct);
        var authority = Get<NativeFilesWorkspaceAuthority>();
        _workspace = await authority.GetCurrentAsync(ct);
        _configuration = await Get<NativeFilesWorkspaceService>().GetConfigurationAsync(ct);
        _documents = []; _selected = 0; _nextPage = null;
        if (_workspace is not { } workspace) { SetStatus("Configure Pictures in Files or review its ownership in Home."); RefreshBindings(); return; }
        var actors = Get<IAuthenticatedResourceActorSource>(); var resources = Get<ResourceAuthorizationService>();
        DurableDriveProvider? Provider(AuthenticatedResourceActor actor) => actor == workspace.Actor ? workspace.Provider : null;
        ValueTask<FilesCommitAuthorityGuard> Guard(AuthenticatedResourceActor actor, DurableDriveProvider provider, CancellationToken token) =>
            authority.CaptureCommitAuthorityAsync(actor, provider, WriteAvailable, token);
        _files = new(actors, Provider, workspace.Directories, resources, WriteAvailable, Guard);
        var media = Get<NativeFilesMediaAssetSourceResolver>();
        _renderer = new((source, token) => media.ResolveRetainedAsync(source.FileId.ToString(), new MediaAssetId(source.AssetId), source.RevisionId.ToString(), token), resources);
        _import = new(Get<HomeResourceOperationBroker>(), actors, Provider, workspace.Directories, resources, WriteAvailable, Guard);
        _export = new(_files, _renderer, new(), new(), Get<HomeResourceOperationBroker>(), actors, Provider, workspace.Directories, resources, WriteAvailable, Guard);
        _edits = new(_files, Get<HomeResourceOperationBroker>(), actors);
        if (!workspace.Configuration.AppFolders.TryGetValue("picture", out var folder)) throw new InvalidDataException("The Files workspace has no Pictures folder.");
        var listed = await workspace.Provider.ListAsync(folder, new("", Limit: 100), page, ct);
        var documents = new List<HostedItemMetadata>();
        foreach (var item in listed.Items.Where(item => item.Kind == HostedItemKind.Artifact))
        {
            var reference = await workspace.Provider.GetArtifactAsync(item.Id, ct);
            if (reference.IsSuccess && reference.Value!.OwnerAppId == "picture") documents.Add(item);
        }
        if (await authority.GetCurrentAsync(ct) is not { } current || current.Actor != workspace.Actor || !ReferenceEquals(current.Provider, workspace.Provider))
            throw new UnauthorizedAccessException("The Files workspace changed while listing Pictures.");
        _documents = documents.ToArray(); _nextPage = listed.NextPageToken;
        SetStatus(_documents.Length == 0 ? "No Picture documents on this page. Import an image to begin." : "Choose a Picture document to open.");
        RefreshBindings();
    }
    private void RefreshBindings()
    {
        var idle = !_busy && !_closed && !_requestUncertain;
        var noPending = _pendingRequest is null && _pendingOwnership is null;
        _model.Set("CanNavigate", idle && noPending);
        _model.Set("CanSetup", idle && _ready && _configuration is null && noPending);
        _model.Set("CanWrite", idle && WriteAvailable() && noPending);
        _model.Set("CanFinish", idle && !noPending);
        _model.Set("CanReviewWorkspace", idle && _ready && _workspace is null && _configuration is not null && noPending);
        if (_view is PictureNativeCuiSurface surface) surface.RefreshActionAvailability();
        _model.Set("CanPrevious", idle && _selected > 0); _model.Set("CanNext", idle && _selected + 1 < _documents.Length);
        _model.Set("CanOpen", idle && _documents.Length > 0 && noPending);
        _model.Set("CanPage", idle && _nextPage is not null && noPending);
        _model.Set("SelectedName", _documents.Length == 0 ? "No document selected" : _documents[_selected].Name);
    }
    private void SetStatus(string message) => _model.Set("Status", message);
    private void ShowView(Control control, IDisposable? owner)
    { _view?.Dispose(); _view = owner; _content.Content = control; }
    private async Task OpenAsync(PictureFilesOpenResult opened, CancellationToken ct)
    {
        var files = _files!; var captured = opened;
        var readiness = new HomeResourceCuiReadiness(Get<HomeCoreRuntime>(), Get<IAuthenticatedResourceActorSource>(), Get<ResourceAuthorizationService>(),
            "picture.file.open", _ => ValueTask.FromResult<IReadOnlyList<ResourceScope>>([new("files.item", captured.Artifact.BackingFileId.ToString("D"), captured.CasRevisionId.ToString(), ResourceAccess.Read)]));
        var surface = new PictureNativeCuiSurface(async token =>
        {
            var current = await files.OpenAsync(new(captured.Artifact.BackingFileId), token);
            if (current.CasRevisionId != captured.CasRevisionId) throw new InvalidOperationException("This Picture changed. Refresh and reopen it.");
            return current;
        }, _renderer!, new(), readiness, DispatchDocumentAsync,
            kind => !_busy && _pendingRequest is null && WriteAvailable() && kind is PictureWorkspaceCommandKind.RotateClockwise or
                PictureWorkspaceCommandKind.FlipHorizontal or PictureWorkspaceCommandKind.Crop or PictureWorkspaceCommandKind.Resize or PictureWorkspaceCommandKind.Export);
        try { await surface.InitializeAsync(ct); }
        catch { surface.Dispose(); throw; }
        _opened = opened; _model.Set("Width", opened.Artifact.Document.CanvasWidth.ToString(CultureInfo.InvariantCulture));
        _model.Set("Height", opened.Artifact.Document.CanvasHeight.ToString(CultureInfo.InvariantCulture));
        ShowView(surface, surface);
    }
    private async Task ShowRequestAsync(CuiDocument document, ICuiBindingContext bindings, ICuiActionDispatcher actions, IDisposable? owner, CancellationToken ct)
    {
        var scene = new CuiSceneHost();
        try { await scene.ShowAsync(new("picture", "Picture", "Picture request", document, bindings, actions, new HostReadiness(this)), ct); }
        catch { scene.Dispose(); owner?.Dispose(); throw; }
        ShowView(scene, new OwnedView(scene, owner));
    }
    private async Task<string> RequestAsync(string action, IReadOnlyList<ResourceScope> scopes, JsonElement arguments, string preview,
        Func<HomeResourceExecutionCapability, CancellationToken, Task<PictureFilesOpenResult?>> execute, IDisposable? owned, CancellationToken ct)
    {
        if (_pendingRequest is not null || _pendingOwnership is not null || _requestUncertain) { owned?.Dispose(); throw new InvalidOperationException("Finish the existing Home request first."); }
        _pendingDisposable = owned; _requestUncertain = true; RefreshBindings();
        var pending = await Get<HomeResourceOperationBroker>().AuthorizeAsync("picture", action, scopes, arguments, preview, null, "picture-native-host", ct);
        _requestUncertain = false; _pendingRequest = pending.RequestId; _pendingArguments = arguments.Clone(); _pendingExecute = execute;
        await _approvals.FocusRequestAsync(pending.RequestId, ct);
        RefreshBindings(); return "Review this request in Home, then choose Finish approved request.";
    }
    private int Number(string key) => _model.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
        ? result : throw new ArgumentException("Enter a whole number for " + key + ".");
    private async ValueTask DispatchDocumentAsync(PictureWorkspaceCommand command, CancellationToken ct)
    {
        if (_busy || _requestUncertain || _opened is not { } opened || command.DocumentId != opened.Artifact.Document.DocumentId || command.BaseRevision != opened.Artifact.Document.Revision ||
            command.BackingFileId != opened.Artifact.BackingFileId || !WriteAvailable() || _pendingRequest is not null)
            throw new UnauthorizedAccessException("This Picture action is no longer available.");
        _busy = true; RefreshBindings();
        try
        {
        if (command.Kind == PictureWorkspaceCommandKind.Export)
        {
            var owner = _export!;
            var request = new PicturePngExportCuiRequest(owner, new(opened.Artifact.BackingFileId), opened.CasRevisionId,
                opened.Artifact.Document.DocumentId, opened.Artifact.Document.Revision, WriteAvailable,
                (intent, token) => RequestAsync(PicturePngExportIntent.ActionId, intent.Scopes, intent.Arguments, "Create a new flattened first-frame PNG without source metadata",
                    async (cap, cancel) => { await owner.ExecuteAsync(intent, cap, cancel); return null; }, null, token));
            await ShowRequestAsync(PicturePngExportCuiRequest.LoadDocument(), request, request, null, ct); return;
        }
        PictureOperation operation = command.Kind switch
        {
            PictureWorkspaceCommandKind.RotateClockwise => new RotateOperation(1),
            PictureWorkspaceCommandKind.FlipHorizontal => new FlipOperation(true),
            PictureWorkspaceCommandKind.Crop => new CropOperation(Number("CropX"), Number("CropY"), Number("Width"), Number("Height")),
            PictureWorkspaceCommandKind.Resize => new ResizeOperation(Number("Width"), Number("Height")),
            _ => throw new NotSupportedException("This Picture action has no owning implementation.")
        };
        var edit = PictureEditIntent.Capture(opened, operation); var edits = _edits!;
        SetStatus(await RequestAsync(PictureEditIntent.ActionId, edit.Scopes, edit.Arguments, "Apply " + operation + " non-destructively",
            async (cap, cancel) => await edits.ExecuteAsync(edit, cap, cancel), null, ct));
        }
        finally { _busy = false; RefreshBindings(); }
    }

    private async ValueTask DispatchAsync(string action, object? parameter, CancellationToken ct)
    {
        if (parameter is not null || _busy || _closed) throw new InvalidOperationException("This Picture action is unavailable.");
        _busy = true; RefreshBindings();
        try
        {
            if (action == "picture.host.finish" && _pendingOwnership is { } ownershipRequest)
            {
                await Get<HomeLocalStoreOwnership>().CompleteImportAsync(ownershipRequest, ct);
                _pendingOwnership = null; await RefreshAsync(null, ct);
            }
            else if (action == "picture.host.finish")
            {
                if (_pendingRequest is null || _pendingExecute is null) throw new InvalidOperationException("There is no captured request to finish.");
                var cap = await Get<HomeResourceOperationBroker>().BeginExecutionCapabilityAsync(_pendingRequest, _pendingArguments, ct);
                if (cap is null) { SetStatus("Home has not approved this exact request. Review it in Home."); return; }
                var execute = _pendingExecute; _pendingExecute = null; _pendingRequest = null;
                try { var committed = await execute(cap, ct); if (committed is not null) await OpenAsync(committed, ct); SetStatus("The approved Picture operation was committed."); }
                finally { _pendingDisposable?.Dispose(); _pendingDisposable = null; }
            }
            else if (_pendingRequest is not null || _pendingOwnership is not null || _requestUncertain) throw new InvalidOperationException("Finish the captured Home request first.");
            else if (action == "picture.host.refresh") await RefreshAsync(null, ct);
            else if (action == "picture.host.page") await RefreshAsync(_nextPage, ct);
            else if (action == "picture.host.previous" && _selected > 0) _selected--;
            else if (action == "picture.host.next" && _selected + 1 < _documents.Length) _selected++;
            else if (action == "picture.host.open" && _documents.Length > 0) await OpenAsync(await _files!.OpenAsync(_documents[_selected].Id, ct), ct);
            else if (action == "picture.host.import" && WriteAvailable())
            {
                var owner = _import!;
                var request = new PictureImportCuiRequest(owner, NativePicker, new(), WriteAvailable,
                    (intent, token) => RequestAsync(PictureImportIntent.ActionId, intent.Scopes, intent.Arguments, "Import original image bytes and a separate editable Picture document",
                        async (cap, cancel) => await owner.ExecuteAsync(intent, cap, cancel), intent, token));
                await ShowRequestAsync(PictureImportCuiRequest.LoadDocument(), request, request, request, ct);
            }
            else if (action == "picture.host.reviewOwnership" && _workspace is null && _configuration is { } configuration)
            {
                var pending = await Get<HomeLocalStoreOwnership>().RequestImportAsync("files", configuration.StoreId.ToString("D"), "picture-native-host", ct);
                _pendingOwnership = pending.RequestId;
                await _approvals.FocusRequestAsync(pending.RequestId, ct);
                SetStatus("Review the existing Files workspace in Home, then finish the approved request.");
            }
            else if (action == "picture.host.setup" && _configuration is null)
            {
                var selected = await NativePicker.OpenFolderPickerAsync(new() { Title = "Choose an empty Files workspace folder", AllowMultiple = false });
                if (selected.Count == 0) return;
                using var folder = selected.Single();
                var path = folder.TryGetLocalPath() ?? throw new NotSupportedException("Files setup needs a local folder selected by the native picker.");
                await Get<NativeFilesWorkspaceService>().ConfigureNewAsync(path, Get<HomeLocalStoreOwnership>(), ct);
                await RefreshAsync(null, ct);
            }
            else throw new InvalidOperationException("This Picture action is unavailable.");
        }
        catch (Exception error) { SetStatus(error.Message); throw; }
        finally { _busy = false; RefreshBindings(); }
    }
    private sealed class Actions(MainWindow owner) : ICuiActionDispatcher
    { public ValueTask DispatchAsync(string action, object? parameter, CancellationToken cancellationToken = default) => owner.DispatchAsync(action, parameter, cancellationToken); }
    private sealed class HostReadiness(MainWindow owner) : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) => owner.CheckHostAsync(cancellationToken); }
    private sealed class OwnedView(IDisposable scene, IDisposable? bindings) : IDisposable
    { public void Dispose() { scene.Dispose(); bindings?.Dispose(); } }
}
