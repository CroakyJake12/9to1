using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;
using HomePermissionManagementSnapshot = HavenOS.Home.PermissionsTrustNotifications.HomePermissionManagementSnapshot;
using HomePermissionCallerIdentity = HavenOS.Home.PermissionsTrustNotifications.HomePermissionCallerIdentity;
using HomePermissionOperationResult = HavenOS.Home.PermissionsTrustNotifications.HomePermissionOperationResult;
using HomeApprovalChoice = HavenOS.Home.PermissionsTrustNotifications.HomeApprovalChoice;
using HomeTrustGrantOptions = HavenOS.Home.PermissionsTrustNotifications.HomeTrustGrantOptions;
using HomeTemporaryGrantFallback = HavenOS.Home.PermissionsTrustNotifications.HomeTemporaryGrantFallback;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Home.NativeUI;

/// <summary>Trusted native Home front door. These decisions are never exposed as app/AI actions.
/// The displayed request digest and actual OS-profile session are rechecked before each decision.</summary>
public class HomeApprovalCuiSurface(HomeCoreRuntime runtime, HomeLocalProfileIdentity profiles,
    HomePermissionTrustService permissions) : UserControl, IDisposable, IAsyncDisposable
{
    private readonly CuiViewModel _model = new();
    private readonly CuiSceneHost _host = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private AuthenticatedResourceActor? _actor;
    private HomePermissionManagementSnapshot? _snapshot;
    private int _requestIndex;
    private int _grantIndex;
    private int _blockedIndex;
    private string? _shownDigest;
    private string? _warningRequest;
    private long _originalWarningGeneration;
    private HomeApprovalWarningFrame? _originalPublishedWarningFrame;
    private const string OriginalTrustWarningText = "Always Trust can allow this caller to read, modify or destroy user data within the displayed action and scope without asking again. Review the caller and affected objects carefully. You can go back without granting trust, and revoke granted trust below.";

    /// <summary>Optional maintained native-window observation source. Missing support refuses the
    /// warning audit. This does not create a platform producer or transfer Home authority.</summary>
    public HomeApprovalOriginalWarningPresentationSource? OriginalWarningPresentationSource { get; init; }
    internal Func<bool>? OriginalOwnerAdmissionCurrent { get; init; }
    public void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    private volatile bool _disposed;
    private readonly object _originalSync = new();
    private readonly List<OriginalOperation> _originalOperations = [];
    private Task? _originalInitialization;
    private Task? _originalClose;

    private bool _originalMountObserversAttached;
    private Control? _originalObservedPromptRoot;
    private WindowBase? _originalObservedPromptWindow;
    private string? _originalPublishedPromptDigest;
    private OriginalPromptDisplay? _originalPromptDisplay;
    private Exception? _originalMountAcquisitionError;

    private sealed class OriginalPromptDisplay(HomePermissionRequest request, string digest,
        Control root, WindowBase window)
    {
        internal HomePermissionRequest Request { get; } = request;
        internal string Digest { get; } = digest;
        internal Control Root { get; } = root;
        internal WindowBase Window { get; } = window;
        internal Task<HomePermissionOperationResult?>? Original;
        internal Task<HomePermissionOperationResult>? Acknowledgement;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return RunOriginal(InitializeOriginalCoreAsync, static _ => true, cancellationToken,
            original => _originalInitialization = original, () => _originalInitialization as Task<bool>);
    }

    private async Task InitializeOriginalCoreAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        RequireOriginalCurrent(cancellationToken);
        AttachOriginalMountObservers();
        Content = _host;
        RequireOriginalCurrent(cancellationToken);
        SetOriginalModel("Minutes", "15");
        SetOriginalModel("Actions", "5");
        SetOriginalModel("RetainTrust", false);
        SetOriginalModel("Status", "Checking Home permissions…");
        using var stream = typeof(HomeApprovalCuiSurface).Assembly.GetManifestResourceStream("HavenOS.Home.NativeUI.Resources.Cui.HomeApprovals.cui")
            ?? throw new InvalidDataException("The canonical Home permissions scene is missing.");
        using var reader = new StreamReader(stream);
        var sceneText = await reader.ReadToEndAsync(cancellationToken);
        RequireOriginalCurrent(cancellationToken);
        var document = new CuiRichParser().Parse(sceneText);
        RequireOriginalCurrent(cancellationToken);
        var availability = await _host.ShowAsync(new("home.permissions", "Permissions", "Home", document,
            _model, new Decisions(this), new Readiness(this))
        {
            // One coalesced initialization owns this fresh host. This only refuses retired UI publication.
            IsPublicationCurrent = () => !_disposed && !_lifetime.IsCancellationRequested
        }, cancellationToken);
        RequireOriginalCurrent(cancellationToken);
        if (availability.State == CuiSceneAvailabilityState.Ready) await RefreshAsync(cancellationToken);
    }

    /// <summary>Navigation only. Selecting a displayed request does not approve, trust or begin execution.</summary>
    public Task<bool> FocusRequestAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return RunOriginal(token => FocusRequestOriginalCoreAsync(requestId, token),
            static original => ((Task<bool>)original).GetAwaiter().GetResult(), cancellationToken);
    }

    private async Task<bool> FocusRequestOriginalCoreAsync(string requestId, CancellationToken cancellationToken)
    {
        RequireOriginalCurrent(cancellationToken);
        if (_actor is null) return false;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RequireOriginalCurrent(cancellationToken);
            if (_snapshot is null) return false;
            var index = Array.FindIndex(_snapshot.PendingRequests.ToArray(), item => item.RequestId == requestId);
            if (index < 0) return false;
            _requestIndex = index;
            Update();
            return true;
        });
    }

    private ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
        new(RunOriginal(token => CheckOriginalCoreAsync(token),
            static original => ((Task<CuiSceneAvailability>)original).GetAwaiter().GetResult(), cancellationToken));

    private async Task<CuiSceneAvailability> CheckOriginalCoreAsync(CancellationToken cancellationToken)
    {
        RequireOriginalCurrent(cancellationToken);
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null) return new(CuiSceneAvailabilityState.Unavailable, "HomeProfileUnavailable", "Open Home to recover this operating-system profile.");
        var snapshot = await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in new[] { "home.core", "home.state", "permissions.trust" })
            if (!snapshot.Services.Any(service => service.ServiceId == id && service.IsAvailable &&
                service.State == HomeServiceLifecycleState.Ready && service.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major &&
                service.ContractVersion.Minor >= HomeCoreServiceCatalog.CurrentContractVersion.Minor))
                return new(CuiSceneAvailabilityState.Unavailable, "HomePermissionsUnavailable", "Home permissions are unavailable or need repair. Existing data was preserved.");
        await permissions.GetSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (actor != await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false))
            return new(CuiSceneAvailabilityState.Unavailable, "HomeProfileChanged", "The Home profile changed. Reopen permissions.");
        RequireOriginalCurrent(cancellationToken);
        _actor = actor;
        return new(CuiSceneAvailabilityState.Ready, "HomePermissionsReady", "Home permissions are ready.");
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await permissions.GetSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RequireOriginalCurrent(cancellationToken);
            _snapshot = snapshot;
            _requestIndex = Clamp(_requestIndex, snapshot.PendingRequests.Count);
            _grantIndex = Clamp(_grantIndex, snapshot.Grants.Count);
            _blockedIndex = Clamp(_blockedIndex, snapshot.BlockedCallerIds.Count);
            Update();
        });
    }

    private void Update()
    {
        RequireOriginalCurrent(_lifetime.Token);
        _originalPublishedPromptDigest = null;
        _originalPublishedWarningFrame = null;
        _originalWarningGeneration = checked(_originalWarningGeneration + 1);
        var snapshot = _snapshot!;
        var request = CurrentRequest;
        var grant = snapshot.Grants.Count == 0 ? null : snapshot.Grants[_grantIndex];
        _shownDigest = request is null ? null : Digest(request);
        if (request?.RequestId != _warningRequest) _warningRequest = null;
        SetOriginalModel("Status", "Review the caller, action and affected objects before deciding.");
        SetOriginalModel("PendingCount", $"Pending requests: {snapshot.PendingRequests.Count}");
        SetOriginalModel("HasPending", request is not null);
        SetOriginalModel("Caller", request is null ? "" : "Requested by " + CallerName(request.Caller));
        SetOriginalModel("Identity", request is null ? "" : "Caller identity: " + request.Caller.CallerId +
            (request.Caller.Origin is { } origin ? " — origin: " + origin : "") +
            (request.Caller.IdentityVersion is { } version ? " — identity version: " + version : ""));
        SetOriginalModel("Target", request is null ? "" : "Target app: " + request.Scope.TargetAppId);
        SetOriginalModel("Action", request is null ? "" : "Action: " + request.Scope.ActionName);
        SetOriginalModel("Scope", request is null ? "" : "Scope: " + (request.Scope.IncludesAllObjects
            ? "All objects." : request.Scope.Objects.Count == 0 ? "No objects are listed." :
              string.Join(", ", request.Scope.Objects.Select(item => item.ObjectType + ":" + item.ObjectId))));
        SetOriginalModel("Preview", request?.Impact.ChangePreview ?? "The change preview is unavailable. Review the impact carefully.");
        SetOriginalModel("Impact", request is null ? "" : request.Impact.IsUnknown
            ? "The affected objects are unknown." : $"Affected objects: {request.Impact.AffectedObjectCount?.ToString(CultureInfo.CurrentCulture) ?? "unknown"}. " +
              (request.Impact.BackupId is null ? "No backup is identified." : "A backup is available."));
        SetOriginalModel("Risk", request is null ? "" : $"{request.Policy.Risk} risk. " +
            (request.Policy.IsReversible ? "This action can be reversed. " : "This action may not be reversible. ") +
            (request.Policy.HasExternalSideEffects ? "It has effects outside this workspace." : ""));
        SetOriginalModel("WarningVisible", request is not null && _warningRequest == request.RequestId);
        SetOriginalModel("GrantCount", $"Trusted access grants: {snapshot.Grants.Count}");
        SetOriginalModel("HasGrant", grant is not null);
        SetOriginalModel("GrantSummary", grant is null ? "" : $"{CallerName(grant.Caller)} — {grant.TrustLevel}. " +
            (grant.IsRevoked ? "Revoked." : grant.ExpiresAt is { } expiry ? "Expires " + expiry.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "No expiry.") +
            (grant.RemainingActions is { } count ? $" {count} actions remaining." : ""));
        SetOriginalModel("BlockedCount", $"Blocked callers: {snapshot.BlockedCallerIds.Count}");
        SetOriginalModel("HasBlocked", snapshot.BlockedCallerIds.Count != 0);
        SetOriginalModel("BlockedSummary", snapshot.BlockedCallerIds.Count == 0 ? "" : "Caller " + (_blockedIndex + 1) + " of " + snapshot.BlockedCallerIds.Count);
        SetOriginalModel("AuditSummary", "Recent permission activity\n" + string.Join("\n", snapshot.RecentAuditEvents.Take(20).Select(item => item.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + " — " + item.Kind)));
        _originalPublishedPromptDigest = _shownDigest;
        ObserveOriginalPromptMount();
    }

    private void AttachOriginalMountObservers()
    {
        Dispatcher.UIThread.VerifyAccess();
        RequireOriginalCurrent(_lifetime.Token);
        if (_originalMountObserversAttached) return;
        _originalMountObserversAttached = true;
        Loaded += OnOriginalNativeMountChanged;
        Unloaded += OnOriginalNativeMountChanged;
        SizeChanged += OnOriginalNativeMountChanged;
        _host.Loaded += OnOriginalNativeMountChanged;
        _host.Unloaded += OnOriginalNativeMountChanged;
    }

    private void OnOriginalNativeMountChanged(object? sender, EventArgs args) =>
        ObserveOriginalPromptMount();

    private void ObserveOriginalPromptMount()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_originalMountObserversAttached || _disposed || _originalMountAcquisitionError is not null) return;
        try
        {
            var root = _host.Content as Control;
            var window = TopLevel.GetTopLevel(this) as WindowBase;
            if (!ReferenceEquals(root, _originalObservedPromptRoot))
            {
                if (_originalObservedPromptRoot is { } oldRoot)
                {
                    oldRoot.Loaded -= OnOriginalNativeMountChanged;
                    oldRoot.Unloaded -= OnOriginalNativeMountChanged;
                    oldRoot.SizeChanged -= OnOriginalNativeMountChanged;
                }
                _originalObservedPromptRoot = root;
                if (root is not null)
                {
                    root.Loaded += OnOriginalNativeMountChanged;
                    root.Unloaded += OnOriginalNativeMountChanged;
                    root.SizeChanged += OnOriginalNativeMountChanged;
                }
            }
            if (!ReferenceEquals(window, _originalObservedPromptWindow))
            {
                if (_originalObservedPromptWindow is { } oldWindow)
                {
                    oldWindow.Opened -= OnOriginalNativeMountChanged;
                    oldWindow.Loaded -= OnOriginalNativeMountChanged;
                    oldWindow.Unloaded -= OnOriginalNativeMountChanged;
                    oldWindow.Closed -= OnOriginalNativeMountChanged;
                }
                _originalObservedPromptWindow = window;
                if (window is not null)
                {
                    window.Opened += OnOriginalNativeMountChanged;
                    window.Loaded += OnOriginalNativeMountChanged;
                    window.Unloaded += OnOriginalNativeMountChanged;
                    window.Closed += OnOriginalNativeMountChanged;
                }
            }
            if (CurrentRequest is not { } request || _originalPublishedPromptDigest is not { } digest ||
                root is null || window is null) return;
            var frame = new OriginalPromptDisplay(request, digest, root, window);
            if (!IsOriginalPromptVisiblyMounted(frame)) return;
            if (_originalPromptDisplay is { } retained &&
                ReferenceEquals(retained.Root, root) && ReferenceEquals(retained.Window, window) &&
                retained.Request.RequestId == request.RequestId && retained.Digest == digest &&
                retained.Original is { } retainedTask &&
                (!retainedTask.IsCompletedSuccessfully || retained.Acknowledgement is not null)) return;
            // This native event has no external caller. The existing ledger publishes its task
            // before profile/audit callbacks; a detached or partially published view never gets an ACK.
            _ = RunOriginal(token => AcknowledgeOriginalPromptCoreAsync(frame, token),
                static actualBody => ((Task<HomePermissionOperationResult?>)actualBody).GetAwaiter().GetResult(),
                CancellationToken.None, publishedTask =>
                {
                    frame.Original = publishedTask;
                    _originalPromptDisplay = frame;
                });
        }
        catch (Exception error)
        {
            // A synchronous acquisition refusal has no returned task. Retain it for the same close,
            // stop additional mount admissions, and leave the canonical audit unacknowledged.
            lock (_originalSync) _originalMountAcquisitionError ??= error;
        }
    }

    private bool IsOriginalPromptVisiblyMounted(OriginalPromptDisplay frame)
    {
        Dispatcher.UIThread.VerifyAccess();
        return !_disposed && _actor is not null && _host.Availability?.State == CuiSceneAvailabilityState.Ready &&
            ReferenceEquals(Content, _host) && ReferenceEquals(_host.Content, frame.Root) &&
            ReferenceEquals(TopLevel.GetTopLevel(this), frame.Window) &&
            ReferenceEquals(TopLevel.GetTopLevel(_host), frame.Window) &&
            ReferenceEquals(TopLevel.GetTopLevel(frame.Root), frame.Window) &&
            IsLoaded && _host.IsLoaded && frame.Root.IsLoaded && frame.Window.IsLoaded &&
            frame.Window.PlatformImpl is not null && frame.Window.IsVisible &&
            (frame.Window is not Window actualWindow || actualWindow.WindowState != WindowState.Minimized) &&
            IsEffectivelyVisible && _host.IsEffectivelyVisible && frame.Root.IsEffectivelyVisible &&
            Bounds.Width > 0 && Bounds.Height > 0 && frame.Root.Bounds.Width > 0 && frame.Root.Bounds.Height > 0 &&
            CurrentRequest is { } request && request.RequestId == frame.Request.RequestId &&
            _shownDigest == frame.Digest && _originalPublishedPromptDigest == frame.Digest &&
            Digest(request) == frame.Digest;
    }

    private async Task<HomePermissionOperationResult?> AcknowledgeOriginalPromptCoreAsync(
        OriginalPromptDisplay frame, CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        HomePermissionOperationResult? result = null;
        try
        {
            await RequireActorAsync(cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireOriginalCurrent(cancellationToken);
                if (!IsOriginalPromptVisiblyMounted(frame)) return;
                // Capture the actual canonical audit task on UI at the exact visible observation.
                // This method validates the same pending digest; its result is never an approval grant.
                frame.Acknowledgement = permissions.AcknowledgePromptDisplayedAsync(
                    frame.Request.RequestId, frame.Digest, cancellationToken)
                    ?? throw new InvalidOperationException("The canonical prompt observation returned no task.");
            });
        }
        catch (Exception error) { AddOriginalError(errors, error); }
        finally
        {
            // A dispatcher/acquisition failure cannot skip an already returned canonical audit task.
            if (frame.Acknowledgement is { } acknowledgement)
            {
                try { result = await acknowledgement.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalError(errors, error); }
            }
        }
        try { RequireOriginalCurrent(cancellationToken); }
        catch (Exception error) { AddOriginalError(errors, error); }
        if (errors.Count == 1) throw errors[0];
        if (errors.Count > 1)
            throw new AggregateException("The original prompt observation and its drain failed.", errors);
        return result;
    }

    private void DetachOriginalMountObservers(List<Exception> errors)
    {
        void Retain(Action remove)
        {
            try { remove(); }
            catch (Exception error) { AddOriginalError(errors, error); }
        }
        Retain(() => Loaded -= OnOriginalNativeMountChanged);
        Retain(() => Unloaded -= OnOriginalNativeMountChanged);
        Retain(() => SizeChanged -= OnOriginalNativeMountChanged);
        Retain(() => _host.Loaded -= OnOriginalNativeMountChanged);
        Retain(() => _host.Unloaded -= OnOriginalNativeMountChanged);
        if (_originalObservedPromptRoot is { } root)
        {
            Retain(() => root.Loaded -= OnOriginalNativeMountChanged);
            Retain(() => root.Unloaded -= OnOriginalNativeMountChanged);
            Retain(() => root.SizeChanged -= OnOriginalNativeMountChanged);
        }
        if (_originalObservedPromptWindow is { } window)
        {
            Retain(() => window.Opened -= OnOriginalNativeMountChanged);
            Retain(() => window.Loaded -= OnOriginalNativeMountChanged);
            Retain(() => window.Unloaded -= OnOriginalNativeMountChanged);
            Retain(() => window.Closed -= OnOriginalNativeMountChanged);
        }
        lock (_originalSync)
            if (_originalMountAcquisitionError is { } error) AddOriginalError(errors, error);
        _originalMountObserversAttached = false;
        _originalObservedPromptRoot = null;
        _originalObservedPromptWindow = null;
        _originalPublishedPromptDigest = null;
        _originalPromptDisplay = null;
    }

    private HomePermissionRequest? CurrentRequest => _snapshot?.PendingRequests.Count > 0 ? _snapshot.PendingRequests[_requestIndex] : null;
    private string CallerName(HomePermissionCallerIdentity caller) => caller.CallerId == _actor?.ActorId ? "You" : caller.DisplayName;
    private static int Clamp(int index, int count) => count == 0 ? 0 : Math.Clamp(index, 0, count - 1);
    private static string Digest(HomePermissionRequest request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    private async Task RequireActorAsync(CancellationToken cancellationToken)
    {
        RequireOriginalCurrent(cancellationToken);
        if (_actor is null || await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != _actor)
            throw new UnauthorizedAccessException("The trusted Home profile changed. Reopen permissions.");
        RequireOriginalCurrent(cancellationToken);
    }

    private ValueTask DispatchAsync(string command, CancellationToken cancellationToken) =>
        new(RunOriginal(token => DispatchOriginalCoreAsync(command, token), static _ => true, cancellationToken));

    private async Task DispatchOriginalCoreAsync(string command, CancellationToken cancellationToken)
    {
        RequireOriginalCurrent(cancellationToken);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RequireActorAsync(cancellationToken).ConfigureAwait(false);
            if (command == "Refresh") { await RefreshAsync(cancellationToken).ConfigureAwait(false); return; }
            if (command.StartsWith("Previous", StringComparison.Ordinal) || command.StartsWith("Next", StringComparison.Ordinal))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RequireOriginalCurrent(cancellationToken);
                    var delta = command.StartsWith("Previous", StringComparison.Ordinal) ? -1 : 1;
                    switch (command)
                    {
                        case "PreviousRequest" or "NextRequest": _requestIndex = Clamp(_requestIndex + delta, _snapshot!.PendingRequests.Count); break;
                        case "PreviousGrant" or "NextGrant": _grantIndex = Clamp(_grantIndex + delta, _snapshot!.Grants.Count); break;
                        case "PreviousBlocked" or "NextBlocked": _blockedIndex = Clamp(_blockedIndex + delta, _snapshot!.BlockedCallerIds.Count); break;
                        default: throw new InvalidOperationException("Unknown Home navigation action.");
                    }
                    Update();
                });
                return;
            }
            HomePermissionOperationResult result;
            if (command == "RevokeGrant")
            {
                if (_snapshot?.Grants.Count is not > 0) throw new InvalidOperationException("No access grant is selected.");
                result = await permissions.RevokeGrantAsync(_snapshot!.Grants[_grantIndex].GrantId, cancellationToken).ConfigureAwait(false);
            }
            else if (command == "UnblockCaller")
            {
                if (_snapshot?.BlockedCallerIds.Count is not > 0) throw new InvalidOperationException("No blocked caller is selected.");
                result = await permissions.UnblockCallerAsync(_snapshot!.BlockedCallerIds[_blockedIndex], cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var shown = CurrentRequest ?? throw new InvalidOperationException("No pending request is selected.");
                var fresh = (await permissions.GetSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).PendingRequests.SingleOrDefault(item => item.RequestId == shown.RequestId);
                RequireOriginalCurrent(cancellationToken);
                if (fresh is null || Digest(fresh) != _shownDigest) throw new InvalidOperationException("The request changed. Refresh and review it again.");
                if (command == "BackFromTrustWarning")
                {
                    await RequireActorAsync(cancellationToken).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        RequireOriginalCurrent(cancellationToken);
                        if (CurrentRequest?.RequestId != shown.RequestId || _shownDigest != Digest(fresh))
                            throw new InvalidOperationException("The request changed. Refresh and review it again.");
                        _warningRequest = null;
                        Update();
                    });
                    return;
                }
                if (command == "ShowTrustWarning")
                {
                    result = await ShowOriginalTrustWarningAsync(shown, fresh,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var choice = command switch
                    {
                        "Accept" => HomeApprovalChoice.Accept,
                        "Trust" => HomeApprovalChoice.AcceptAndTrust,
                        "Decline" => HomeApprovalChoice.Decline,
                        "DeclineBlock" => HomeApprovalChoice.DeclineAndBlock,
                        "ConfirmAlwaysTrust" => HomeApprovalChoice.AcceptAndAlwaysTrust,
                        "TemporaryDuration" or "TemporaryActions" => HomeApprovalChoice.GrantTemporaryTrustedAccess,
                        _ => throw new InvalidOperationException("Unknown Home decision action.")
                    };
                    var extended = choice is HomeApprovalChoice.AcceptAndAlwaysTrust or HomeApprovalChoice.GrantTemporaryTrustedAccess;
                    if (extended && _warningRequest != shown.RequestId) throw new InvalidOperationException("Review the extended trust warning first.");
                    HomeTrustGrantOptions? options = null;
                    if (choice == HomeApprovalChoice.GrantTemporaryTrustedAccess)
                    {
                        var text = (string?)_model.Get(command == "TemporaryDuration" ? "Minutes" : "Actions");
                        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) || value <= 0)
                            throw new InvalidOperationException("Enter a positive duration or action count.");
                        var fallback = _model.Get("RetainTrust") is true ? HomeTemporaryGrantFallback.AcceptAndTrust : HomeTemporaryGrantFallback.ManualApproval;
                        options = command == "TemporaryDuration" ? new(TimeSpan.FromMinutes(value), null, fallback) : new(null, value, fallback);
                    }
                    await RequireActorAsync(cancellationToken).ConfigureAwait(false);
                    result = await permissions.DecideAsync(shown.RequestId, choice, extended, options, cancellationToken).ConfigureAwait(false);
                }
            }
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => SetOriginalModel("Status", result.Message));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            try { await Dispatcher.UIThread.InvokeAsync(() => SetOriginalModel("Status", exception.Message)); }
            catch (Exception publicationError)
            { throw new AggregateException("The Home decision and its status publication failed.", exception, publicationError); }
        }
        finally { _gate.Release(); }
    }

    private async Task<HomePermissionOperationResult> ShowOriginalTrustWarningAsync(
        HomePermissionRequest shown, HomePermissionRequest originalFresh, CancellationToken cancellationToken)
    {
        var source = OriginalWarningPresentationSource;
        var digest = Digest(originalFresh);
        HomeApprovalWarningFrame? frame = null;
        Task<HomeApprovalWarningPresentation?>? originalPresentation = null;
        HomeApprovalWarningPresentation? presentation = null;
        Task<HomePermissionOperationResult>? originalMark = null;
        HomePermissionOperationResult? result = null;
        var errors = new List<Exception>();
        try
        {
            await RequireActorAsync(cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireOriginalCurrent(cancellationToken);
                if (CurrentRequest is not { } current || current.RequestId != shown.RequestId ||
                    _shownDigest != digest || _originalPublishedPromptDigest != digest ||
                    Digest(current) != digest) return;
                // Invalidate the old observation BEFORE any notifying warning publication.
                _originalPublishedWarningFrame = null;
                _originalWarningGeneration = checked(_originalWarningGeneration + 1);
                _warningRequest = null;
                SetOriginalModel("WarningVisible", true);
                RequireOriginalCurrent(cancellationToken);
                if (_actor is not { } actor || _host.Content is not Control root ||
                    TopLevel.GetTopLevel(this) is not WindowBase window ||
                    window.PlatformImpl is not { } platform) return;
                var warnings = root.GetVisualDescendants().OfType<TextBlock>()
                    .Where(item => item.Name == "approval-trust-warning").Take(2).ToArray();
                if (warnings.Length != 1 || warnings[0].Text != OriginalTrustWarningText) return;
                frame = new(this, _host, root, warnings[0], window, platform, actor,
                    shown.RequestId, digest, OriginalTrustWarningText, _originalWarningGeneration,
                    () => frame is not null && IsOriginalWarningFrameCurrent(frame));
                // This marker is published only after the actual notify-and-postcheck above.
                _originalPublishedWarningFrame = frame;
            });
            if (source is null || frame is null)
                return new(false, "HomeWarningPresentationUnavailable",
                    "The native warning presentation could not be verified. Extended trust remains unavailable.");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RequireOriginalCurrent(cancellationToken);
                if (!IsOriginalWarningFrameCurrent(frame)) return;
                // Retain the SAME actual acquisition before awaiting or any subsequent UI publication.
                originalPresentation = source.AcquireOriginalAsync(frame, cancellationToken)
                    ?? throw new InvalidOperationException("The original warning presentation returned no task.");
            });
        }
        catch (Exception error) { AddOriginalError(errors, error); }
        finally
        {
            if (originalPresentation is not null)
            {
                try { presentation = await originalPresentation.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalError(errors, error); }
            }
        }

        if (errors.Count == 0 && source is not null && frame is not null &&
            presentation is not null && presentation.IsBoundTo(source, frame))
        {
            try
            {
                await RequireActorAsync(cancellationToken).ConfigureAwait(false);
                var current = (await permissions.GetSnapshotAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)).PendingRequests.SingleOrDefault(item => item.RequestId == frame.RequestId);
                await RequireActorAsync(cancellationToken).ConfigureAwait(false);
                if (current is not null && Digest(current) == frame.Digest)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        RequireOriginalCurrent(cancellationToken);
                        if (!IsOriginalWarningFrameCurrent(frame)) return;
                        // The actual canonical audit is acquired only after the bound native observation
                        // and a fresh actor/request/full-digest check. Its task always has an independent join.
                        originalMark = permissions.MarkAlwaysTrustWarningShownAsync(
                            frame.RequestId, cancellationToken)
                            ?? throw new InvalidOperationException("The canonical warning observation returned no task.");
                    });
                }
            }
            catch (Exception error) { AddOriginalError(errors, error); }
            finally
            {
                if (originalMark is not null)
                {
                    try { result = await originalMark.ConfigureAwait(false); }
                    catch (Exception error) { AddOriginalError(errors, error); }
                }
            }
            if (errors.Count == 0 && result?.Succeeded == true)
            {
                try
                {
                    await RequireActorAsync(cancellationToken).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        RequireOriginalCurrent(cancellationToken);
                        if (IsOriginalWarningFrameCurrent(frame))
                            _warningRequest = frame.RequestId;
                    });
                }
                catch (Exception error) { AddOriginalError(errors, error); }
            }
        }

        if (errors.Count != 0)
            // Do not let the legacy handled-status filter discard a source, native or audit cleanup cause.
            throw new AggregateException("The original warning presentation, audit and drain failed.", errors);
        return result ?? new(false, "HomeWarningPresentationUnavailable",
            "The current native warning presentation could not be verified. Extended trust remains unavailable.");
    }

    private bool IsOriginalWarningFrameCurrent(HomeApprovalWarningFrame frame)
    {
        Dispatcher.UIThread.VerifyAccess();
        return !_disposed && !_lifetime.IsCancellationRequested &&
            ReferenceEquals(_originalPublishedWarningFrame, frame) &&
            frame.Generation == _originalWarningGeneration && _actor == frame.Actor &&
            ReferenceEquals(frame.Surface, this) && ReferenceEquals(frame.Host, _host) &&
            ReferenceEquals(Content, _host) && ReferenceEquals(_host.Content, frame.Root) &&
            ReferenceEquals(TopLevel.GetTopLevel(this), frame.Window) &&
            ReferenceEquals(TopLevel.GetTopLevel(frame.Root), frame.Window) &&
            ReferenceEquals(TopLevel.GetTopLevel(frame.Warning), frame.Window) &&
            frame.Warning.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, frame.Root)) &&
            ReferenceEquals(frame.Window.PlatformImpl, frame.Platform) &&
            frame.Warning.Name == "approval-trust-warning" && frame.Warning.Text == frame.WarningText &&
            frame.WarningText == OriginalTrustWarningText && _model.Get("WarningVisible") is true &&
            CurrentRequest is { } request && request.RequestId == frame.RequestId &&
            _shownDigest == frame.Digest && _originalPublishedPromptDigest == frame.Digest &&
            Digest(request) == frame.Digest;
    }

    private void RequireOriginalCurrent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (OriginalOwnerAdmissionCurrent is { } current)
        {
            ObjectDisposedException.ThrowIf(!current(), this);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private void SetOriginalModel(string key, object? value)
    {
        RequireOriginalCurrent(_lifetime.Token);
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _model.Set(key, value); return true; });
        RequireOriginalCurrent(_lifetime.Token);
    }

    private abstract class OriginalOperation
    {
        internal abstract Task Original { get; }
        internal Task? Body;
        internal CancellationToken Caller;
        internal CancellationToken Token;
        internal OperationCanceledException? OwnerPreBodyCancellation;
    }

    private sealed class OriginalOperation<T> : OriginalOperation
    {
        internal readonly TaskCompletionSource<T> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal override Task Original => Completion.Task;
    }

    // The returned task is in the bounded ledger before acquiring its actual callback task.
    private Task<T> RunOriginal<T>(Func<CancellationToken, Task> acquire, Func<Task, T> result,
        CancellationToken cancellationToken, Action<Task<T>>? originalPublished = null, Func<Task<T>?>? alreadyPublished = null)
    {
        OriginalOperation<T> operation;
        lock (_originalSync)
        {
            RequireOriginalCurrent(cancellationToken);
            if (alreadyPublished?.Invoke() is { } retained) return retained;
            _originalOperations.RemoveAll(item => item.Original.IsCompletedSuccessfully && item.Body?.IsCompletedSuccessfully == true);
            if (_originalOperations.Count >= 128)
                throw new InvalidOperationException("Retain every original Home approval callback before another admission.");
            operation = new() { Caller = cancellationToken };
            _originalOperations.Add(operation);
            originalPublished?.Invoke(operation.Completion.Task);
        }
        _ = SettleOriginalAsync(operation, acquire, result);
        return operation.Completion.Task;
    }

    private async Task SettleOriginalAsync<T>(OriginalOperation<T> operation,
        Func<CancellationToken, Task> acquire, Func<Task, T> result)
    {
        var errors = new List<Exception>();
        CancellationTokenSource? linked = null;
        T value = default!;
        try
        {
            using var originalScope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(operation.Caller, _lifetime.Token);
            operation.Token = linked.Token;
            // Only this directly executed owner check can identify an owner pre-body cancellation.
            // A callback's OCE is never reclassified using flags observed during a later close.
            try { operation.Token.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException error) when (_lifetime.IsCancellationRequested &&
                !operation.Caller.IsCancellationRequested)
            {
                operation.OwnerPreBodyCancellation = error;
                throw;
            }
            ObjectDisposedException.ThrowIf(_disposed, this);
            operation.Body = CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => acquire(operation.Token))
                ?? throw new InvalidOperationException("The original Home approval callback returned no task.");
            await operation.Body.ConfigureAwait(false);
            value = result(operation.Body);
        }
        catch (Exception error) { AddOriginalError(errors, error); }
        finally
        {
            try { linked?.Dispose(); }
            catch (Exception error) { AddOriginalError(errors, error); }
        }
        if (errors.Count == 0) operation.Completion.TrySetResult(value);
        else if (errors.Count == 1) operation.Completion.TrySetException(errors[0]);
        else operation.Completion.TrySetException(new AggregateException("Original Home approval callback and cleanup failed.", errors));
    }

    /// <summary>Seal callback admission before cancellation, join every original task, and retire only this surface.
    /// Destructive consumers must retain and await this same task while the UI dispatcher is running,
    /// outside accepted action/load bodies that depend on this close. Dispose only requests retirement.</summary>
    public Task CloseAndDrainAsync()
    {
        TaskCompletionSource completion;
        OriginalOperation[] originals;
        lock (_originalSync)
        {
            if (_originalClose is not null) return _originalClose;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = completion.Task;
            _disposed = true;
            originals = _originalOperations.ToArray();
        }
        _ = CloseOriginalCoreAsync(originals, completion);
        return completion.Task;
    }

    private async Task CloseOriginalCoreAsync(OriginalOperation[] originals, TaskCompletionSource completion)
    {
        var errors = new List<Exception>();
        Task? originalHostClose = null;
        // Our coalesced close and sealed native predicate are already published. Start the actual
        // host stop promptly, before a surface body could need that scoped cancellation to settle.
        try
        {
            originalHostClose = _host.CloseOriginalAsync()
                ?? throw new InvalidOperationException("The original CUI host close returned no task.");
        }
        catch (Exception error) { AddOriginalError(errors, error); }
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _lifetime.Cancel(); return true; }); }
        catch (Exception error) { AddOriginalError(errors, error); }
        foreach (var original in originals)
        {
            try { await original.Original.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (!IsOriginalRetirementCancellation(original, error)) AddOriginalError(errors, error);
            }
            if (original.Body is { } actual)
            {
                try { await actual.ConfigureAwait(false); }
                catch (Exception error)
                {
                    if (!IsOriginalRetirementCancellation(original, error)) AddOriginalError(errors, error);
                }
            }
        }
        // Join outside admitted action/load bodies; those bodies must never await their own owner close.
        if (originalHostClose is not null)
        {
            try { await originalHostClose.ConfigureAwait(false); }
            catch (Exception error) { AddOriginalError(errors, error); }
        }
        if (originalHostClose is not null && originalHostClose.IsCompleted)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                {
                    DetachOriginalMountObservers(errors);
                    try { Content = null; }
                    catch (Exception error) { AddOriginalError(errors, error); }
                    // The SAME host close was observed above. No request-only Dispose substitutes for it.
                    _actor = null; _snapshot = null;
                    return true;
                }));
            }
            catch (Exception error) { AddOriginalError(errors, error); }
            try { _gate.Dispose(); }
            catch (Exception error) { AddOriginalError(errors, error); }
            try { _lifetime.Dispose(); }
            catch (Exception error) { AddOriginalError(errors, error); }
        }
        else
        {
            // Without an actual host task, retain native resources instead of claiming a destructive drain.
            AddOriginalError(errors, new InvalidOperationException(
                "Retain the actual CUI host close before retiring the native Home approval surface."));
        }
        if (errors.Count == 0) completion.TrySetResult();
        else if (errors.Count == 1) completion.TrySetException(errors[0]);
        else completion.TrySetException(new AggregateException("Original Home approval callbacks and retirement failed.", errors));
    }

    private static bool IsOriginalRetirementCancellation(OriginalOperation original, Exception error) =>
        ReferenceEquals(original.OwnerPreBodyCancellation, error);

    private static void AddOriginalError(List<Exception> errors, Exception error)
    {
        if (!errors.Any(prior => ReferenceEquals(prior, error))) errors.Add(error);
    }

    // Request-only: a synchronous IDisposable call supplies no proof that UI callbacks have settled.
    public void Dispose() => _ = CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private sealed class Readiness(HomeApprovalCuiSurface owner) : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) => owner.CheckAsync(cancellationToken); }
    private sealed class Decisions(HomeApprovalCuiSurface owner) : ICuiActionDispatcher, ICuiActionAvailability
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) => owner.DispatchAsync(command, cancellationToken);
        public bool HasAction(string command) => command is "Refresh" or "PreviousRequest" or "NextRequest" or "PreviousGrant" or "NextGrant" or "PreviousBlocked" or "NextBlocked" or
            "Accept" or "Trust" or "Decline" or "DeclineBlock" or "ShowTrustWarning" or "BackFromTrustWarning" or "ConfirmAlwaysTrust" or "TemporaryDuration" or "TemporaryActions" or "RevokeGrant" or "UnblockCaller";
        public bool? IsActionAvailable(string command) => HasAction(command);
    }
}
