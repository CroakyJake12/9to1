using System.Text.Json;
using Avalonia;
using Avalonia.VisualTree;
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
    private string _listedQuery = "";
    // Observe the authored CUI surfaces for native capture/currentness only. Navigation state
    // and every transition remain the CUI document's local variables and guarded Set actions.
    private Control? _launchSurface, _workspaceSurface;
    private CanvasLaunchRow[] _launchRows = [];
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
        controls.BindAuthoredControl("canvas-native-content", _ => _content);
        controls.BindAuthoredControl("canvas-home-approvals", _ => _approvals);
        _shell = new(controls, IsSettledNoEffectActionFailure);
        _model.Set("NewName", "Untitled canvas");
        _model.Set("SearchQuery", "");
        Content = _shell;
        Opened += (_, _) =>
        {
            Initialization = _originalWork.RunOriginalAsync(InitializeAsync, _lifetime.Token);
            _ = ObserveStatusAsync(Initialization);
        };
        Activated += (_, _) =>
        {
            if (_view is not CanvasNativeCuiSurface surface || !EditorViewActive()) return;
            var actual = _originalWork.RunOriginalAsync(async token =>
            {
                if (!await surface.RefreshAsync(token))
                {
                    await _originalWork.ObserveOriginalAsync(surface.CloseAndDrainAsync());
                    _view = null; _content.Content = null;
                    _opened = null; _openedFile = null;
                    RefreshBindings();
                }
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
    private bool HasRetainedCanvas() => _view is CanvasNativeCuiSurface { OriginalCloseTask: null } && _opened is not null && _openedFile is not null;
    private bool EditorViewActive() => !_closed && _workspaceSurface?.IsEffectivelyVisible == true &&
        HasRetainedCanvas() && ReferenceEquals(_content.Content, _view);
    private Control FindAuthoredSurface(string id) => AuthoredTree(_shell).Single(control => CuiRuntimeIdentity.GetStableId(control) == "id:" + id);
    private static IEnumerable<Control> AuthoredTree(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container ? container.AuthoredChildren : root.GetVisualChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var descendant in AuthoredTree(child)) yield return descendant;
    }
    private async Task InitializeAsync(CancellationToken ct)
    {
        using var stream = typeof(CanvasHostWindow).Assembly.GetManifestResourceStream("HavenOS.Apps.Canvas.UI.CanvasHost.cui")
            ?? throw new InvalidDataException("The Canvas host CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(await reader.ReadToEndAsync(ct));
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error)) throw new InvalidDataException("Canvas host CUI is invalid.");
        await _originalWork.ObserveOriginalAsync(_shell.ShowAsync(new("canvas", "Canvas", "Canvas", document, _model, new Actions(this), new HostReadiness(this))
        { IsPublicationCurrent = () => !_closed && !_lifetime.IsCancellationRequested }, ct));
        _launchSurface = FindAuthoredSurface("canvas-launch");
        _workspaceSurface = FindAuthoredSurface("canvas-active-workspace");
        _workspaceSurface.PropertyChanged += (_, args) =>
        {
            // Active=False is authored navigation. Detach native input to cancel unsubmitted
            // captures while retaining the exact document/session for a later genuine resume.
            if (args.Property == IsVisibleProperty && args.NewValue is false && ReferenceEquals(_content.Content, _view))
                _content.Content = null;
        };
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
    private async Task RefreshAsync(string? page, CancellationToken ct, NoEffectReadProof? proof = null)
    {
        var query = _model.TryGetValue("SearchQuery", out var search) ? search?.ToString()?.Trim() ?? "" : "";
        if (query.Length > 256) throw RejectBeforeEffect(proof, new ArgumentException("Canvas search must be at most 256 characters."));
        if (page is not null && query != _listedQuery)
            throw new InvalidOperationException("Choose Search before loading another page of changed search results.");
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
        var listed = query.Length == 0
            ? await workspace.Provider.ListAsync(folder, new("", Limit: 100), page, ct)
            : await workspace.Provider.SearchAsync(new(query, ParentId: folder, Limit: 100), page, ct);
        var documents = new List<HostedItemMetadata>();
        foreach (var item in listed.Items.Where(item => item.Kind == HostedItemKind.Artifact))
        {
            var reference = await workspace.Provider.GetArtifactAsync(item.Id, ct);
            if (reference.IsSuccess && reference.Value!.OwnerAppId == "canvas") documents.Add(item);
        }
        if (await authority.GetCurrentAsync(ct) is not { } current || current.Actor != workspace.Actor || !ReferenceEquals(current.Provider, workspace.Provider))
            throw new UnauthorizedAccessException("The Files workspace changed while listing Canvases.");
        _documents = documents.OrderByDescending(item => item.ModifiedAt).ThenBy(item => item.Id.Value).ToArray();
        _listedQuery = query; _nextPage = listed.NextPageToken;
        SetStatus(_documents.Length == 0 ? "No Canvas documents on this page. Create a canvas to begin." : "Choose a Canvas document to open.");
        RefreshBindings();
    }
    private void RefreshBindings()
    {
        var idle = !_busy && !_closed && !_requestUncertain;
        var noPending = _pendingRequest is null && _pendingOwnership is null && _pendingAudit is null && _pendingBeginAudit is null;
        var retained = HasRetainedCanvas();
        _model.Set("HasRetainedCanvas", retained);
        _model.Set("HasNoRetainedCanvas", !retained);
        _model.Set("CanReturnStart", idle && noPending);
        _model.Set("CanResume", idle && noPending && retained);
        _model.Set("OpenedName", _opened?.Artifact.DisplayName ?? "Canvas");
        _model.Set("HasPendingRequest", !noPending || _requestUncertain);
        _model.Set("CanSearch", idle && noPending);
        _model.Set("HasNoDocuments", _documents.Length == 0);
        _model.Set("HasMoreDocuments", _nextPage is not null);
        _launchRows = _documents.Select(item => new CanvasLaunchRow(item)).ToArray();
        _model.Set("Canvases", _launchRows);
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
    private async Task ShowViewAsync(Control control, IDisposable owner, Func<Task> validatePublication)
    {
        var previous = _view;
        if (previous is CanvasNativeCuiSurface native) await _originalWork.ObserveOriginalAsync(native.CloseAndDrainAsync());
        else previous?.Dispose();
        await validatePublication();
        if (_closed) throw new OperationCanceledException("Canvas closed before native publication.");
        _content.Content = control;
        _view = owner;
    }
    private async Task OpenAsync(HostedItemId fileId, CanvasFilesOpenResult opened, CancellationToken ct, NoEffectReadProof? proof = null)
    {
        var files = _files!; var captured = opened with { Artifact=CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(opened.Artifact)) };
        var original=_workspace ?? throw new UnauthorizedAccessException("The original Files workspace is unavailable.");
        if(captured.StoreId!=original.Configuration.StoreId)throw new UnauthorizedAccessException("Canvas store identity differs from its original workspace.");
        // Reject this unsupported payload before constructing/acquiring any native owner.
        // Only an explicitly admitted read command can supply the private proof; Finish
        // and all post-commit callers leave it null and can never acquire this stamp.
        if (!captured.Artifact.DocumentSettings.Properties.ContainsKey("9to1.Canvas.RnoteState"))
            throw RejectBeforeEffect(proof, new NotSupportedException(
                "The artifact has no Rnote engine state; open it using the registered native Canvas renderer."));
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
        _nativeOwnerAcquired = true; // Monotonic native custody, never a document-discard witness.
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
                if (!EditorViewActive() || _busy || !WriteAvailable() || _pendingRequest is not null || _pendingAudit is not null || _pendingBeginAudit is not null || _requestUncertain)
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
        Task? publication = null;
        try
        {
            publication = ShowViewAsync(surface, surface, async () =>
            {
                await RequireOriginalOwnerAsync(original, captured.StoreId, ct);
                if (!await _originalWork.ObserveOriginalAsync(surface.RefreshAsync(ct)))
                    throw new UnauthorizedAccessException("Canvas source changed before native publication.");
                ct.ThrowIfCancellationRequested();
            });
            await publication;
            _opened = captured; _openedFile = fileId;
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            CaptureOriginalFailure(failures, publication, error);
            if (ReferenceEquals(_content.Content, surface))
                try { _content.Content = null; } catch (Exception detachError) { AddOriginalFailure(failures, detachError); }
            if (ReferenceEquals(_view, surface))
            { _view = null; _opened = null; _openedFile = null; }
            Task? close = null;
            try { close = surface.CloseAndDrainAsync(); await close; }
            catch (Exception closeError) { CaptureOriginalFailure(failures, close, closeError); }
            if (failures.Count == 1 && failures[0] is not OperationCanceledException)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("Canvas publication and acquired surface cleanup failed.", failures);
        }
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
    {
        var proof = CaptureReadAdmission(action);
        var actual = _originalWork.RunOriginalAsync(token => DispatchOriginalAsync(action, parameter, token, proof), ct);
        _latestOriginalCommand = actual;
        // The callback may already have begun. It only records its private exact cause;
        // this proof becomes observable to close policy AFTER binding the actual issued Task.
        if (proof is not null)
            lock (_noEffectGate) { proof.Original = actual; _noEffectReads.Add(proof); }
        return new(actual);
    }
    private async Task DispatchOriginalAsync(string action, object? parameter, CancellationToken ct, NoEffectReadProof? proof)
    {
        if (_busy || _closed || parameter is not null && action != "canvas.host.openItem")
            throw new InvalidOperationException("This Canvas action is unavailable.");
        var startOnly = action is "canvas.host.openItem" or "canvas.host.open" or "canvas.host.create" or
            "canvas.host.search" or "canvas.host.page" or "canvas.host.previous" or "canvas.host.next" or
            "canvas.host.setup" or "canvas.host.reviewOwnership";
        if (startOnly && _launchSurface?.IsEffectivelyVisible != true ||
            action == "canvas.host.resume" && _launchSurface?.IsEffectivelyVisible != true ||
            action == "canvas.host.start" && _workspaceSurface?.IsEffectivelyVisible != true)
            throw new InvalidOperationException("This Canvas command does not belong to the active view.");
        // CUI passes the actual row from this current Files listing. A caller
        // cannot turn an arbitrary path, ID or stale row into an open command.
        var selectedRow = parameter as CanvasLaunchRow;
        if (action == "canvas.host.openItem" && (selectedRow is null ||
            !_launchRows.Any(row => ReferenceEquals(row, selectedRow))))
            throw new InvalidOperationException("Choose a Canvas from the current Files listing.");
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
            else if (action == "canvas.host.start")
            {
                // Navigation retains the same canonical document and its native
                // lifetime. It is neither a close nor permission to discard work.
                await RefreshAsync(null, ct);
            }
            else if (action == "canvas.host.resume" && _view is CanvasNativeCuiSurface retained && _opened is not null)
            {
                if (!await _originalWork.ObserveOriginalAsync(retained.RefreshAsync(ct)))
                {
                    await _originalWork.ObserveOriginalAsync(retained.CloseAndDrainAsync());
                    _view = null; _content.Content = null;
                    _opened = null; _openedFile = null;
                    throw new UnauthorizedAccessException("This Canvas source changed. Open its current version from Files.");
                }
                _content.Content = retained;
            }
            else if (action == "canvas.host.search") await RefreshAsync(null, ct, proof);
            else if (action == "canvas.host.refresh") await RefreshAsync(null, ct, proof);
            else if (action == "canvas.host.page") await RefreshAsync(_nextPage, ct, proof);
            else if (action == "canvas.host.previous" && _selected > 0) _selected--;
            else if (action == "canvas.host.next" && _selected + 1 < _documents.Length) _selected++;
            else if (action == "canvas.host.open" && _documents.Length > 0) await OpenAsync(_documents[_selected].Id, await _files!.OpenAsync(_documents[_selected].Id, ct), ct, proof);
            else if (action == "canvas.host.openItem" && selectedRow is not null)
                await OpenAsync(selectedRow.Item.Id, await _files!.OpenAsync(selectedRow.Item.Id, ct), ct, proof);
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
    private sealed class Actions(CanvasHostWindow owner) : ICuiResultReportingDispatcher
    {
        public ValueTask DispatchAsync(string action, object? parameter, CancellationToken cancellationToken = default) => owner.DispatchAsync(action, parameter, cancellationToken);
        public async ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string action, object? parameter, CancellationToken cancellationToken = default)
        {
            var priorSurface = owner._view;
            await owner.DispatchAsync(action, parameter, cancellationToken);
            var pending = owner._pendingRequest is not null || owner._pendingOwnership is not null ||
                owner._pendingAudit is not null || owner._pendingBeginAudit is not null || owner._requestUncertain;
            if (pending) return new(false, false, "PendingHomeApproval", "Review the exact request in Home.", null);
            if (action is "canvas.host.open" or "canvas.host.openItem" or "canvas.host.finish" or "canvas.host.resume")
            {
                var published = owner.HasRetainedCanvas() && ReferenceEquals(owner._content.Content, owner._view) &&
                    (action == "canvas.host.resume" || !ReferenceEquals(priorSurface, owner._view));
                return new(published, false, published ? "CanvasSessionPublished" : "NoCanvasSessionPublished", null, null);
            }
            return new(true, false, "Completed", null, null);
        }
    }
    private sealed class HostReadiness(CanvasHostWindow owner) : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) => owner.CheckHostAsync(cancellationToken); }

    private sealed class CanvasLaunchRow(HostedItemMetadata item) : ICuiBindingContext
    {
        internal HostedItemMetadata Item { get; } = item;
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Item.Id.ToString(),
                "Name" => Item.Name,
                "Modified" => "Modified " + Item.ModifiedAt.ToLocalTime().ToString("g"),
                _ => null
            };
            return path is "Id" or "Name" or "Modified";
        }
    }
}
