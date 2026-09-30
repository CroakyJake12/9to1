using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace Haven.Desktop.Controls;

/// <summary>Trusted native Home front door. These decisions are never exposed as app/AI actions.
/// The displayed request digest and actual OS-profile session are rechecked before each decision.</summary>
public sealed class HomeApprovalCuiSurface(HomeCoreRuntime runtime, HomeLocalProfileIdentity profiles,
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        Content = _host;
        _model.Set("Minutes", "15");
        _model.Set("Actions", "5");
        _model.Set("RetainTrust", false);
        _model.Set("Status", "Checking Home permissions…");
        using var stream = typeof(HomeApprovalCuiSurface).Assembly.GetManifestResourceStream("Haven.Desktop.Resources.Cui.HomeApprovals.cui")
            ?? throw new InvalidDataException("The canonical Home permissions scene is missing.");
        using var reader = new StreamReader(stream);
        var document = new CuiRichParser().Parse(await reader.ReadToEndAsync(cancellationToken));
        var availability = await _host.ShowAsync(new("home.permissions", "Permissions", "Home", document,
            _model, new Decisions(this), new Readiness(this)), cancellationToken);
        if (availability.State == CuiSceneAvailabilityState.Ready) await RefreshAsync(cancellationToken);
    }

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
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
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
                if (command == "ShowTrustWarning")
                {
                    result = await permissions.MarkAlwaysTrustWarningShownAsync(shown.RequestId, linked.Token).ConfigureAwait(false);
                    if (result.Succeeded) _warningRequest = shown.RequestId;
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
            "Accept" or "Trust" or "Decline" or "DeclineBlock" or "ShowTrustWarning" or "ConfirmAlwaysTrust" or "TemporaryDuration" or "TemporaryActions" or "RevokeGrant" or "UnblockCaller";
        public bool? IsActionAvailable(string command) => HasAction(command);
    }
}
