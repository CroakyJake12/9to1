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
    HomePermissionTrustService permissions) : UserControl, IDisposable
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
    private bool _disposed;

    /// <summary>Snapshot the same original current Home scene action pipelines after a native click.
    /// Capture before deactivation/disposal and await this exact task; completion never grants permission or proves an action outcome.</summary>
    public Task WhenActionsIdleAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _host.WhenActionsIdleAsync();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        Content = _host;
        _model.Set("Minutes", "15");
        _model.Set("Actions", "5");
        _model.Set("RetainTrust", false);
        _model.Set("Status", "Checking Home permissions…");
        using var stream = typeof(HomeApprovalCuiSurface).Assembly.GetManifestResourceStream("HavenOS.Home.NativeUI.Resources.Cui.HomeApprovals.cui")
            ?? throw new InvalidDataException("The canonical Home permissions scene is missing.");
        using var reader = new StreamReader(stream);
        var document = new CuiRichParser().Parse(await reader.ReadToEndAsync(cancellationToken));
        var availability = await _host.ShowAsync(new("home.permissions", "Permissions", "Home", document,
            _model, new Decisions(this), new Readiness(this)), cancellationToken);
        if (availability.State == CuiSceneAvailabilityState.Ready) await RefreshAsync(cancellationToken);
    }

    /// <summary>Navigation only. Selecting a displayed request does not approve, trust or begin execution.</summary>
    public async Task<bool> FocusRequestAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (_disposed || _actor is null) return false;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || cancellationToken.IsCancellationRequested || _snapshot is null) return false;
            var index = Array.FindIndex(_snapshot.PendingRequests.ToArray(), item => item.RequestId == requestId);
            if (index < 0) return false;
            _requestIndex = index;
            Update();
            return true;
        });
    }

    /// <summary>Records actual native visibility of this exact request. Navigation or initialization alone is not display.</summary>
    public async Task<bool> AcknowledgeDisplayedRequestAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var shown = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TopLevel.GetTopLevel(this)?.UpdateLayout();
            return IsPromptVisible() ? CurrentRequest : null;
        }, DispatcherPriority.Render);
        if (shown is null) return false;
        var digest = Digest(shown);
        await RequireActorAsync(linked.Token).ConfigureAwait(false);
        var stillShown = await Dispatcher.UIThread.InvokeAsync(() =>
            IsPromptVisible() && CurrentRequest is { } current && Digest(current) == digest && _shownDigest == digest,
            DispatcherPriority.Render);
        if (!stillShown) return false;
        var result = await permissions.AcknowledgePromptDisplayedAsync(shown.RequestId, digest, linked.Token).ConfigureAwait(false);
        await RequireActorAsync(linked.Token).ConfigureAwait(false);
        return result.Succeeded;
    }

    private bool IsPromptVisible() => !_disposed && _host.Availability?.State == CuiSceneAvailabilityState.Ready &&
        IsEffectivelyVisible && _host.IsEffectivelyVisible && Bounds.Width > 0 && Bounds.Height > 0 &&
        TopLevel.GetTopLevel(this) is { IsVisible: true } root && root.IsEffectivelyVisible;

    private async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
    {
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
            if (_disposed || cancellationToken.IsCancellationRequested) return;
            _snapshot = snapshot;
            _requestIndex = Clamp(_requestIndex, snapshot.PendingRequests.Count);
            _grantIndex = Clamp(_grantIndex, snapshot.Grants.Count);
            _blockedIndex = Clamp(_blockedIndex, snapshot.BlockedCallerIds.Count);
            Update();
        });
    }

    private void Update()
    {
        var snapshot = _snapshot!;
        var request = CurrentRequest;
        var grant = snapshot.Grants.Count == 0 ? null : snapshot.Grants[_grantIndex];
        _shownDigest = request is null ? null : Digest(request);
        if (request?.RequestId != _warningRequest) _warningRequest = null;
        _model.Set("Status", "Review the caller, action and affected objects before deciding.");
        _model.Set("PendingCount", $"Pending requests: {snapshot.PendingRequests.Count}");
        _model.Set("HasPending", request is not null);
        _model.Set("Caller", request is null ? "" : "Requested by " + CallerName(request.Caller));
        _model.Set("Identity", request is null ? "" : $"Caller ID: {request.Caller.CallerId}. Origin: {request.Caller.Origin ?? "unavailable"}. Identity: {(request.Caller.IsVerified ? "verified" : "unverified")}.");
        _model.Set("Target", request is null ? "" : "Target app: " + request.Scope.TargetAppId);
        _model.Set("Action", request is null ? "" : "Action: " + request.Scope.ActionName);
        _model.Set("Scope", request is null ? "" : request.Scope.IncludesAllObjects ? "Scope: all objects in the displayed target action." :
            request.Scope.Objects.Count == 0 ? "Scope: no specific object IDs supplied." : "Scope: " + string.Join(", ", request.Scope.Objects.Select(item => item.ObjectType + ":" + item.ObjectId)));
        _model.Set("Preview", request?.Impact.ChangePreview ?? "The change preview is unavailable. Review the impact carefully.");
        _model.Set("Impact", request is null ? "" : request.Impact.IsUnknown
            ? "The affected objects are unknown." : $"Affected objects: {request.Impact.AffectedObjectCount?.ToString(CultureInfo.CurrentCulture) ?? "unknown"}. " +
              (request.Impact.BackupId is null ? "No backup is identified." : "A backup is available."));
        _model.Set("Risk", request is null ? "" : $"{request.Policy.Risk} risk. " +
            (request.Policy.IsReversible ? "This action can be reversed. " : "This action may not be reversible. ") +
            (request.Policy.HasExternalSideEffects ? "It has effects outside this workspace." : ""));
        _model.Set("WarningVisible", request is not null && _warningRequest == request.RequestId);
        _model.Set("GrantCount", $"Trusted access grants: {snapshot.Grants.Count}");
        _model.Set("HasGrant", grant is not null);
        _model.Set("GrantSummary", grant is null ? "" : $"{CallerName(grant.Caller)} — {grant.TrustLevel}. " +
            (grant.IsRevoked ? "Revoked." : grant.ExpiresAt is { } expiry ? "Expires " + expiry.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "No expiry.") +
            (grant.RemainingActions is { } count ? $" {count} actions remaining." : ""));
        _model.Set("BlockedCount", $"Blocked callers: {snapshot.BlockedCallerIds.Count}");
        _model.Set("HasBlocked", snapshot.BlockedCallerIds.Count != 0);
        _model.Set("BlockedSummary", snapshot.BlockedCallerIds.Count == 0 ? "" : "Caller " + (_blockedIndex + 1) + " of " + snapshot.BlockedCallerIds.Count);
        _model.Set("AuditSummary", "Recent permission activity\n" + string.Join("\n", snapshot.RecentAuditEvents.Take(20).Select(item => item.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + " — " + item.Kind)));
    }

    private HomePermissionRequest? CurrentRequest => _snapshot?.PendingRequests.Count > 0 ? _snapshot.PendingRequests[_requestIndex] : null;
    private string CallerName(HomePermissionCallerIdentity caller) => caller.CallerId == _actor?.ActorId ? "You" : caller.DisplayName;
    private static int Clamp(int index, int count) => count == 0 ? 0 : Math.Clamp(index, 0, count - 1);
    private static string Digest(HomePermissionRequest request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    private async Task RequireActorAsync(CancellationToken cancellationToken)
    {
        if (_actor is null || await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != _actor)
            throw new UnauthorizedAccessException("The trusted Home profile changed. Reopen permissions.");
    }

    private async ValueTask DispatchAsync(string command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await RequireActorAsync(linked.Token).ConfigureAwait(false);
            if (command == "Refresh") { await RefreshAsync(linked.Token).ConfigureAwait(false); return; }
            if (command == "BackFromTrustWarning")
            {
                await Dispatcher.UIThread.InvokeAsync(() => { _warningRequest = null; Update(); });
                return;
            }
            if (command.StartsWith("Previous", StringComparison.Ordinal) || command.StartsWith("Next", StringComparison.Ordinal))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
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
                result = await permissions.RevokeGrantAsync(_snapshot!.Grants[_grantIndex].GrantId, linked.Token).ConfigureAwait(false);
            }
            else if (command == "UnblockCaller")
            {
                if (_snapshot?.BlockedCallerIds.Count is not > 0) throw new InvalidOperationException("No blocked caller is selected.");
                result = await permissions.UnblockCallerAsync(_snapshot!.BlockedCallerIds[_blockedIndex], linked.Token).ConfigureAwait(false);
            }
            else
            {
                var shown = CurrentRequest ?? throw new InvalidOperationException("No pending request is selected.");
                var fresh = (await permissions.GetSnapshotAsync(cancellationToken: linked.Token).ConfigureAwait(false)).PendingRequests.SingleOrDefault(item => item.RequestId == shown.RequestId);
                if (fresh is null || Digest(fresh) != _shownDigest) throw new InvalidOperationException("The request changed. Refresh and review it again.");
                if (!await AcknowledgeDisplayedRequestAsync(linked.Token).ConfigureAwait(false))
                    throw new InvalidOperationException("The exact Home prompt is not visibly displayed. Reopen it before deciding.");
                if (command == "ShowTrustWarning")
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { _warningRequest = shown.RequestId; Update(); }, DispatcherPriority.Render);
                    var warningVisible = await Dispatcher.UIThread.InvokeAsync(() => IsPromptVisible() &&
                        CurrentRequest?.RequestId == shown.RequestId && _warningRequest == shown.RequestId &&
                        this.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Name == "approval-trust-warning" && text.IsEffectivelyVisible),
                        DispatcherPriority.Render);
                    if (!warningVisible) throw new InvalidOperationException("The extended trust warning could not be displayed.");
                    await RequireActorAsync(linked.Token).ConfigureAwait(false);
                    result = await permissions.MarkAlwaysTrustWarningShownAsync(shown.RequestId, linked.Token).ConfigureAwait(false);
                    if (!result.Succeeded) _warningRequest = null;
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
                    await RequireActorAsync(linked.Token).ConfigureAwait(false);
                    result = await permissions.DecideAsync(shown.RequestId, choice, extended, options, linked.Token).ConfigureAwait(false);
                }
            }
            await RefreshAsync(linked.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => _model.Set("Status", result.Message));
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        { await Dispatcher.UIThread.InvokeAsync(() => _model.Set("Status", exception.Message)); }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _host.Dispose();
        _lifetime.Dispose();
    }

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
