using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;

namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasHostWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly CancellationTokenSource _lifetime;
    private readonly CanvasOriginalWorkOwner _originalWork = new();
    private readonly Func<NativeFilesWorkspace, HomeResourceExecutionCapability, Func<bool>, CancellationToken, ValueTask<HomeClaimedResourceCommitFence>>? _captureHomeFence;
    private bool _closeAccepted;
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
    private CanvasFilesArtifactBridge? _files;
    private CanvasHomeCreateOperation? _create;
    private CanvasHomeStrokeOperation? _stroke;
    private CanvasFilesOpenResult? _opened;
    private HostedItemId? _openedFile;
    private IDisposable? _view;
    private string? _pendingRequest;
    private string? _pendingBeginAudit;
    private JsonElement _pendingArguments;
    private Func<HomeResourceExecutionCapability, CancellationToken, Task<OwnerCommit>>? _pendingExecute;
    private IDisposable? _pendingDisposable;
    private sealed record OwnerCommit(CanvasFilesOpenResult Opened, HostedItemId FileId);
    private sealed record PendingAudit(HomeResourceExecutionCapability Capability, HomeExecutionOutcome Outcome);
    private PendingAudit? _pendingAudit;
    private CanvasOriginalFilesCommit? _retainedOriginalCommit;
    public CanvasOriginalFilesCommit? OriginalFilesAcknowledgement => _retainedOriginalCommit is { } original
        ? new(original.FileId, CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(original.Artifact)), original.Revision) : null;
    private bool _busy, _ready, _closed;
    public Task Initialization { get; private set; } = Task.CompletedTask;

    public CanvasHostWindow(IServiceProvider services) : this(services, default, null) { }
    internal CanvasHostWindow(IServiceProvider services, CancellationToken originalProcess,
        Func<NativeFilesWorkspace, HomeResourceExecutionCapability, Func<bool>, CancellationToken, ValueTask<HomeClaimedResourceCommitFence>>? captureHomeFence)
    {
        _services = services; _captureHomeFence = captureHomeFence;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalProcess);
        Title = "Canvas"; Width = 1200; Height = 800; MinWidth = 800; MinHeight = 560;
        _approvals = new(Get<HomeCoreRuntime>(), Get<HomeLocalProfileIdentity>(), Get<HomePermissionTrustService>());
        _approvals.DemandExternalOriginalRetirementJoin();
        var controls = new CuiControlRegistry();
        controls.RegisterControlType("CanvasHostContent", _ => _content);
        controls.RegisterControlType("CanvasHostApprovals", _ => _approvals);
        _shell = new(controls);
        _model.Set("NewName", "Untitled canvas");
        Content = _shell;
        Opened += (_, _) =>
        {
            Initialization = _originalWork.RunOriginalAsync(InitializeAsync, _lifetime.Token);
            _ = ObserveStatusAsync(Initialization);
        };
        Activated += (_, _) =>
        {
            if (_view is not CanvasNativeCuiSurface surface || _closed) return;
            var actual = _originalWork.RunOriginalAsync(async token =>
            {
                if (!await surface.RefreshAsync(token)) { _opened = null; _openedFile = null; }
            }, _lifetime.Token);
            _ = ObserveStatusAsync(actual);
        };
        Closing += (_, args) =>
        {
            if (_closeAccepted) return;
            args.Cancel = true; _ = CloseFromWindowAsync();
        };

    }
    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();
    // A compiled native host may supply its platform picker; app actions never supply paths or picker results.
    private IStorageProvider NativePicker => _services.GetService<IStorageProvider>() ?? StorageProvider;
    private bool WriteAvailable() => !_closed && _ready && _workspace is not null;
    private async Task InitializeAsync(CancellationToken ct)
    {
        using var stream = typeof(CanvasHostWindow).Assembly.GetManifestResourceStream("HavenOS.Apps.Canvas.UI.CanvasHost.cui")
            ?? throw new InvalidDataException("The Canvas host CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(await reader.ReadToEndAsync(ct));
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error)) throw new InvalidDataException("Canvas host CUI is invalid.");
        await _originalWork.ObserveOriginalAsync(_shell.ShowAsync(new("canvas", "Canvas", "Canvas", document, _model, new Actions(this), new HostReadiness(this))
        { IsPublicationCurrent = () => !_closed && !_lifetime.IsCancellationRequested }, ct));
        await _originalWork.ObserveOriginalAsync(_approvals.InitializeAsync(ct));
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
            _ready ? "CanvasHomeReady" : "CanvasHomeUnavailable", _ready ? "Canvas is ready." : "Open Home to recover this profile and its services.");
    }
    private async Task RefreshAsync(string? page, CancellationToken ct)
    {
        await CheckHostAsync(ct);
        var authority = Get<NativeFilesWorkspaceAuthority>();
        _workspace = await authority.GetCurrentAsync(ct);
        _configuration = await Get<NativeFilesWorkspaceService>().GetConfigurationAsync(ct);
        _documents = []; _selected = 0; _nextPage = null;
        if (_workspace is not { } workspace) { SetStatus("Configure Canvases in Files or review its ownership in Home."); RefreshBindings(); return; }
        var actors = Get<IAuthenticatedResourceActorSource>(); var resources = Get<ResourceAuthorizationService>();
        DurableDriveProvider? Provider(AuthenticatedResourceActor actor) => actor == workspace.Actor ? workspace.Provider : null;
        ValueTask<FilesCommitAuthorityGuard> Guard(AuthenticatedResourceActor actor, DurableDriveProvider provider, CancellationToken token) =>
            authority.CaptureCommitAuthorityAsync(actor, provider, WriteAvailable, token);
        _files = new(actors, Provider, workspace.Directories, resources, WriteAvailable, Guard,
            _captureHomeFence is null ? null : (actor, provider, capability, token) =>
            {
                if (actor != workspace.Actor || !ReferenceEquals(provider, workspace.Provider))
                    throw new UnauthorizedAccessException("The original Canvas Files owner changed before Home commit capture.");
                return _captureHomeFence(workspace, capability, () => !_closed && !_lifetime.IsCancellationRequested, token);
            }, _captureHomeFence is null ? null : workspace.Configuration.RootDirectory);
        _create = new(_files, Get<HomeResourceOperationBroker>(), actors);
        _stroke = new(_files, Get<HomeResourceOperationBroker>(), actors);
        if (!workspace.Configuration.AppFolders.TryGetValue("canvas", out var folder)) throw new InvalidDataException("The Files workspace has no Canvases folder.");
        var listed = await workspace.Provider.ListAsync(folder, new("", Limit: 100), page, ct);
        var documents = new List<HostedItemMetadata>();
        foreach (var item in listed.Items.Where(item => item.Kind == HostedItemKind.Artifact))
        {
            var reference = await workspace.Provider.GetArtifactAsync(item.Id, ct);
            if (reference.IsSuccess && reference.Value!.OwnerAppId == "canvas") documents.Add(item);
        }
        if (await authority.GetCurrentAsync(ct) is not { } current || current.Actor != workspace.Actor || !ReferenceEquals(current.Provider, workspace.Provider))
            throw new UnauthorizedAccessException("The Files workspace changed while listing Canvases.");
        _documents = documents.ToArray(); _nextPage = listed.NextPageToken;
        SetStatus(_documents.Length == 0 ? "No Canvas documents on this page. Create a canvas to begin." : "Choose a Canvas document to open.");
        RefreshBindings();
    }
    private void RefreshBindings()
    {
        var idle = !_busy && !_closed && !_requestUncertain;
        var noPending = _pendingRequest is null && _pendingOwnership is null && _pendingAudit is null && _pendingBeginAudit is null;
        _model.Set("CanNavigate", idle && noPending);
        _model.Set("CanSetup", idle && _ready && _configuration is null && noPending);
        _model.Set("CanWrite", idle && WriteAvailable() && noPending);
        _model.Set("CanFinish", idle && !noPending);
        _model.Set("CanReviewWorkspace", idle && _ready && _workspace is null && _configuration is not null && noPending);
        _model.Set("CanPrevious", idle && _selected > 0); _model.Set("CanNext", idle && _selected + 1 < _documents.Length);
        _model.Set("CanOpen", idle && _documents.Length > 0 && noPending);
        _model.Set("CanPage", idle && _nextPage is not null && noPending);
        _model.Set("SelectedName", _documents.Length == 0 ? "No document selected" : _documents[_selected].Name);
    }
    private void SetStatus(string message) => _model.Set("Status", message);
    private async Task ShowViewAsync(Control control, IDisposable? owner)
    {
        var previous = _view;
        if (previous is CanvasNativeCuiSurface native) await _originalWork.ObserveOriginalAsync(native.CloseAndDrainAsync());
        else previous?.Dispose();
        _view = owner; _content.Content = control;
    }
    private async Task OpenAsync(HostedItemId fileId, CanvasFilesOpenResult opened, CancellationToken ct)
    {
        var files = _files!; var captured = opened with { Artifact=CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(opened.Artifact)) };
        var original=_workspace ?? throw new UnauthorizedAccessException("The original Files workspace is unavailable.");
        if(captured.StoreId!=original.Configuration.StoreId)throw new UnauthorizedAccessException("Canvas store identity differs from its original workspace.");
        var readiness = new CanvasOriginalResourceReadiness(Get<HomeCoreRuntime>(), Get<IAuthenticatedResourceActorSource>(), Get<ResourceAuthorizationService>(),
            "canvas.file.open", _ => ValueTask.FromResult<IReadOnlyList<ResourceScope>>([new("files.item", fileId.ToString(), captured.CasRevisionId.ToString(), ResourceAccess.Read)]));
        var originalReadiness=new OriginalCanvasReadiness(readiness,async token=>
        {
            await RequireOriginalOwnerAsync(original,captured.StoreId,token);
            var current=await files.OpenAsync(fileId,captured.StoreId,token);
            if(current.CasRevisionId!=captured.CasRevisionId || current.Artifact.ArtifactId!=captured.Artifact.ArtifactId || current.Artifact.RevisionId!=captured.Artifact.RevisionId)
                throw new InvalidOperationException("This Canvas revision changed; reopen it.");
        });
        var strokeOwner = _stroke!;
        var surface = new CanvasNativeCuiSurface(async token =>
        {
            await RequireOriginalOwnerAsync(original,captured.StoreId,token);
            var current = await files.OpenAsync(fileId,captured.StoreId, token);
            if (current.CasRevisionId != captured.CasRevisionId || current.Artifact.ArtifactId != captured.Artifact.ArtifactId ||
                current.Artifact.RevisionId != captured.Artifact.RevisionId) throw new InvalidOperationException("This Canvas changed. Refresh and reopen it.");
            return CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(current.Artifact));
        }, originalReadiness, new(fileId, opened.CasRevisionId, opened.Artifact.ArtifactId, opened.Artifact.RevisionId,
            () => WriteAvailable() && _pendingRequest is null && _pendingAudit is null && _pendingBeginAudit is null && !_requestUncertain,
            async (intent, token) =>
            {
                if (_busy || !WriteAvailable() || _pendingRequest is not null || _pendingAudit is not null || _pendingBeginAudit is not null || _requestUncertain)
                    throw new UnauthorizedAccessException("Finish the current Home request before drawing.");
                _busy = true; RefreshBindings();
                try
                {
                    SetStatus(await RequestAsync(CanvasStrokeWriteIntent.ActionId, intent.Scopes, intent.Arguments,
                        "Add this exact captured stroke to the displayed Canvas revision", async (cap, cancel) =>
                        {
                            var committed = await strokeOwner.ExecuteAsync(intent, cap, cancel);
                            await RequireOriginalOwnerAsync(original,captured.StoreId,cancel);
                            var current = await files.OpenAsync(committed.FileId,captured.StoreId, cancel);
                            if (current.Artifact.ArtifactId != committed.Artifact.ArtifactId || current.Artifact.RevisionId != committed.Artifact.RevisionId)
                                throw new InvalidOperationException("The Canvas changed after the stroke commit. Reopen its current revision.");
                            return new(current, committed.FileId);
                        }, null, token));
                }
                finally { _busy = false; RefreshBindings(); }
            },captured.StoreId),CreateEraserContext(fileId,captured,files,original));
        await InitializeOwnedSurfaceAsync(surface, ct);
        _opened = opened; _openedFile = fileId; await ShowViewAsync(surface, surface);
    }
    private async Task<string> RequestAsync(string action, IReadOnlyList<ResourceScope> scopes, JsonElement arguments, string preview,
        Func<HomeResourceExecutionCapability, CancellationToken, Task<OwnerCommit>> execute, IDisposable? owned, CancellationToken ct)
    {
        if (_pendingRequest is not null || _pendingOwnership is not null || _pendingAudit is not null || _pendingBeginAudit is not null || _requestUncertain) { owned?.Dispose(); throw new InvalidOperationException("Finish the existing Home request first."); }
        _pendingDisposable = owned; _requestUncertain = true; RefreshBindings();
        var pending = await Get<HomeResourceOperationBroker>().AuthorizeAsync("canvas", action, scopes, arguments, preview, null, "canvas-native-host", ct);
        _requestUncertain = false; _pendingRequest = pending.RequestId; _pendingArguments = arguments.Clone(); _pendingExecute = execute;
        await _approvals.FocusRequestAsync(pending.RequestId, ct);
        RefreshBindings(); return "Review this request in Home, then choose Finish approved request.";
    }
    private async Task<bool> FinishAuditAsync(CancellationToken ct)
    {
        var pending = _pendingAudit ?? throw new InvalidOperationException("There is no execution receipt to record.");
        try
        {
            var result = await Get<HomeResourceOperationBroker>().CompleteExecutionAsync(pending.Capability, pending.Outcome, ct);
            if (!result.Succeeded)
            {
                if (result.Code == "HOME_EXECUTION_COMPLETION_NOT_OWNED")
                {
                    var aborted = await Get<HomeResourceOperationBroker>().AbortUnclaimedExecutionAsync(pending.Capability, ct);
                    if (aborted.Succeeded)
                    {
                        _pendingAudit = null;
                        SetStatus("Home ended this request without issuing an execution claim. " + aborted.Message);
                        return true;
                    }
                    if (aborted.Code != "HOME_EXECUTION_ABORT_NOT_OWNED")
                    {
                        SetStatus("The operation will not run again. Home could not record the unclaimed request's outcome: " + aborted.Message + " Choose Finish to retry recording only.");
                        return false;
                    }
                    var rejected = await Get<HomeResourceOperationBroker>().RetryRejectedClaimAuditAsync(pending.Capability, ct);
                    if (rejected.Succeeded)
                    {
                        _pendingAudit = null;
                        SetStatus("Home recorded the rejected claim. No owning operation will run again. " + rejected.Message);
                        return true;
                    }
                    if (rejected.Code != "HOME_CLAIM_REJECTION_NOT_OWNED")
                    {
                        SetStatus("The operation will not run again. Home rejection recording needs recovery: " + rejected.Message + " Choose Finish to retry recording only.");
                        return false;
                    }
                    var state = await Get<HomePermissionTrustService>().GetAuthorizationAsync(pending.Capability.RequestId, ct);
                    if (state.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked or
                        HomePermissionRequestState.Cancelled or HomePermissionRequestState.Failed)
                    {
                        // This issuer never successfully claimed the capability. Read Home's actual terminal decision;
                        // do not rewrite it as an owning commit or repeat the denied operation.
                        _pendingAudit = null;
                        SetStatus("Home ended this request before a successful execution claim. " + state.Message);
                        return false;
                    }
                }
                SetStatus("The operation will not run again. Home could not record its outcome: " + result.Message + " Choose Finish to retry recording only.");
                return false;
            }
            _pendingAudit = null;
            SetStatus(pending.Outcome.State == HomePermissionRequestState.Succeeded
                ? "The approved Canvas operation was committed and recorded in Home."
                : pending.Outcome.Message);
        }
        catch (Exception error)
        {
            SetStatus("The operation will not run again. Home outcome recording needs recovery: " + error.Message + " Choose Finish to retry recording only.");
        }
        return false;
    }

    private async Task FinishRejectedBeginAuditAsync(CancellationToken ct)
    {
        var request = _pendingBeginAudit ?? throw new InvalidOperationException("There is no failed dispatch admission to record.");
        try
        {
            var result = await Get<HomeResourceOperationBroker>().RetryRejectedBeginAuditAsync(request, ct);
            if (result.Succeeded) _pendingBeginAudit = null;
            else if (result.Code == "HOME_BEGIN_AUDIT_NOT_OWNED")
            {
                // The initial automatic audit may already have finished. Read its actual terminal state.
                var state = await Get<HomePermissionTrustService>().GetAuthorizationAsync(request, ct);
                if (state.State is HomePermissionRequestState.Failed or HomePermissionRequestState.Denied or
                    HomePermissionRequestState.Blocked or HomePermissionRequestState.Cancelled) _pendingBeginAudit = null;
            }
            SetStatus(_pendingBeginAudit is null
                ? "Home ended dispatch admission. No owning operation was issued or replayed."
                : "Dispatch admission recording needs recovery. Choose Finish to retry recording only. " + result.Message);
        }
        catch (Exception error)
        {
            SetStatus("Dispatch admission recording needs recovery. Choose Finish to retry recording only. " + error.Message);
        }
    }

    private async Task<bool> ReleaseTerminalRequestAsync(string requestId, bool ownershipRequest, CancellationToken ct)
    {
        var decision = await Get<HomePermissionTrustService>().GetAuthorizationAsync(requestId, ct);
        if (decision.State is not (HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Denied or
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Blocked or
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Cancelled or
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Failed or
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Succeeded or
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.PartiallyCompleted)) return false;
        if (ownershipRequest) _pendingOwnership = null;
        else
        {
            _pendingRequest = null; _pendingExecute = null; _pendingArguments = default;
            _pendingDisposable?.Dispose(); _pendingDisposable = null;
        }
        // Reads a terminal Home decision; never converts a denial into permission or claims a commit.
        SetStatus("Home ended this request. Refresh Canvases to inspect current state. " + decision.Message);
        return true;
    }

    private ValueTask DispatchAsync(string action, object? parameter, CancellationToken ct)
        => new(_originalWork.RunOriginalAsync(token => DispatchOriginalAsync(action, parameter, token), ct));
    private async Task DispatchOriginalAsync(string action, object? parameter, CancellationToken ct)
    {
        if (parameter is not null || _busy || _closed) throw new InvalidOperationException("This Canvas action is unavailable.");
        _busy = true; RefreshBindings();
        try
        {
            if (action == "canvas.host.finish" && _pendingBeginAudit is not null)
            {
                await FinishRejectedBeginAuditAsync(ct);
            }
            else if (action == "canvas.host.finish" && _pendingAudit is not null)
            {
                await FinishAuditAsync(ct);
            }
            else if (action == "canvas.host.finish" && _pendingOwnership is { } ownershipRequest)
            {
                if (await ReleaseTerminalRequestAsync(ownershipRequest, true, ct)) return;
                await Get<HomeLocalStoreOwnership>().CompleteImportAsync(ownershipRequest, ct);
                _pendingOwnership = null; await RefreshAsync(null, ct);
            }
            else if (action == "canvas.host.finish")
            {
                if (_pendingRequest is null || _pendingExecute is null) throw new InvalidOperationException("There is no captured request to finish.");
                if (await ReleaseTerminalRequestAsync(_pendingRequest, false, ct)) return;
                HomeResourceExecutionCapability? cap;
                try { cap = await Get<HomeResourceOperationBroker>().BeginExecutionCapabilityAsync(_pendingRequest, _pendingArguments, ct); }
                catch
                {
                    // Admission may have consumed the intent before storage failed. Never dispatch it again.
                    _pendingBeginAudit = _pendingRequest;
                    _pendingRequest = null; _pendingExecute = null; _pendingArguments = default;
                    _pendingDisposable?.Dispose(); _pendingDisposable = null;
                    await FinishRejectedBeginAuditAsync(CancellationToken.None);
                    return;
                }
                if (cap is null) { SetStatus("Home has not approved this exact request. Review it in Home."); return; }
                var execute = _pendingExecute; _pendingExecute = null; _pendingRequest = null;
                OwnerCommit? committed = null;
                try
                {
                    var actual = execute(cap, ct);
                    committed = await _originalWork.ObserveOriginalAsync(actual);
                    if (_files!.TryGetOriginalCommit(cap, out var known)) _retainedOriginalCommit = known;
                    _pendingAudit = new(cap, new(HomePermissionRequestState.Succeeded, "CANVAS_COMMITTED",
                        "The owning Canvas operation returned its durable Files commit.", [new("files.item", committed.FileId.ToString())]));
                }
                catch (Exception error)
                {
                    // A thrown operation may have published before a later read failed. Never repeat its mutation.
                    if (_files!.TryGetOriginalCommit(cap, out var acknowledged)) _retainedOriginalCommit = acknowledged;
                    _pendingAudit = acknowledged is not null
                        ? new(cap, new(HomePermissionRequestState.PartiallyCompleted, "CANVAS_COMMITTED_RECOVERY_REQUIRED",
                            "Files acknowledged this exact commit before a later failure. Retry Home recording only; the mutation will not run again.",
                            [new("files.item", acknowledged.FileId.ToString())]))
                        : new(cap, new(HomePermissionRequestState.PartiallyCompleted, "CANVAS_RESULT_UNCERTAIN",
                            "The operation did not return a complete receipt. Inspect Files and Home before attempting another change.", []));
                    var abortedBeforeClaim = await FinishAuditAsync(CancellationToken.None);
                    throw new InvalidOperationException((abortedBeforeClaim
                        ? "Home ended this request before an execution claim. Refresh the document before another change. "
                        : "Canvas did not return a complete commit receipt. The operation will not run again. Inspect Files and Home. ") + error.Message, error);
                }
                finally { _pendingDisposable?.Dispose(); _pendingDisposable = null; }
                await FinishAuditAsync(CancellationToken.None);
                await OpenAsync(committed.FileId, committed.Opened, ct);
            }
            else if (_pendingRequest is not null || _pendingOwnership is not null || _pendingAudit is not null || _pendingBeginAudit is not null || _requestUncertain) throw new InvalidOperationException("Finish the captured Home request first.");
            else if (action == "canvas.host.refresh") await RefreshAsync(null, ct);
            else if (action == "canvas.host.page") await RefreshAsync(_nextPage, ct);
            else if (action == "canvas.host.previous" && _selected > 0) _selected--;
            else if (action == "canvas.host.next" && _selected + 1 < _documents.Length) _selected++;
            else if (action == "canvas.host.open" && _documents.Length > 0) await OpenAsync(_documents[_selected].Id, await _files!.OpenAsync(_documents[_selected].Id, ct), ct);
            else if (action == "canvas.host.create" && WriteAvailable())
            {
                if (!_model.TryGetValue("NewName", out var value) || string.IsNullOrWhiteSpace(value?.ToString()))
                    throw new ArgumentException("Enter a name for the new canvas.");
                var name = value.ToString()!.Trim();
                if (name.Length > 256) throw new ArgumentException("Canvas names must be at most 256 characters.");
                var workspace = _workspace!; var originalStoreId = workspace.Configuration.StoreId;
                var folderId = workspace.Configuration.AppFolders["canvas"];
                await RequireOriginalOwnerAsync(workspace, originalStoreId, ct);
                await workspace.Provider.GetStoreEvidenceAsync(originalStoreId, ct);
                var folder = await workspace.Provider.GetAsync(folderId, ct);
                if (!folder.IsSuccess) throw new InvalidOperationException(folder.Error!.Message);
                using var blank = CanvasRnoteDocument.Create(name);
                var intent = CanvasCreateIntent.Capture(blank.Snapshot, new(folderId, folder.Value!.CurrentRevisionId) { ExpectedStoreId = originalStoreId });
                var create = _create!; var files = _files!;
                SetStatus(await RequestAsync(CanvasCreateIntent.ActionId, intent.Scopes, intent.Arguments,
                    "Create this new editable Canvas in the configured Files folder", async (cap, cancel) =>
                    {
                        await RequireOriginalOwnerAsync(workspace, originalStoreId, cancel);
                        var committed = await create.ExecuteAsync(intent, cap, cancel);
                        await RequireOriginalOwnerAsync(workspace, originalStoreId, cancel);
                        var opened = await files.OpenAsync(committed.FileId, originalStoreId, cancel);
                        if (opened.Artifact.ArtifactId != committed.Artifact.ArtifactId || opened.Artifact.RevisionId != committed.Artifact.RevisionId)
                            throw new InvalidOperationException("The new Canvas changed after its commit. Reopen its current revision.");
                        return new(opened, committed.FileId);
                    }, null, ct));
            }
            else if (action == "canvas.host.reviewOwnership" && _workspace is null && _configuration is { } configuration)
            {
                var pending = await Get<HomeLocalStoreOwnership>().RequestImportAsync("files", configuration.StoreId.ToString("D"), "canvas-native-host", ct);
                _pendingOwnership = pending.RequestId;
                await _approvals.FocusRequestAsync(pending.RequestId, ct);
                SetStatus("Review the existing Files workspace in Home, then finish the approved request.");
            }
            else if (action == "canvas.host.setup" && _configuration is null)
            {
                var selected = await NativePicker.OpenFolderPickerAsync(new() { Title = "Choose an empty Files workspace folder", AllowMultiple = false });
                if (selected.Count == 0) return;
                using var folder = selected.Single();
                var path = folder.TryGetLocalPath() ?? throw new NotSupportedException("Files setup needs a local folder selected by the native picker.");
                await Get<NativeFilesWorkspaceService>().ConfigureNewAsync(path, Get<HomeLocalStoreOwnership>(), ct);
                await RefreshAsync(null, ct);
            }
            else throw new InvalidOperationException("This Canvas action is unavailable.");
        }
        catch (Exception error) { SetStatus(error.Message); throw; }
        finally { _busy = false; RefreshBindings(); }
    }
    private sealed class Actions(CanvasHostWindow owner) : ICuiActionDispatcher
    { public ValueTask DispatchAsync(string action, object? parameter, CancellationToken cancellationToken = default) => owner.DispatchAsync(action, parameter, cancellationToken); }
    private sealed class HostReadiness(CanvasHostWindow owner) : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) => owner.CheckHostAsync(cancellationToken); }
}
