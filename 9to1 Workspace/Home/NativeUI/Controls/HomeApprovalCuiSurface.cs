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
    private volatile bool _disposed;
    private readonly object _originalSync = new();
    private readonly List<OriginalOperation> _originalOperations = [];
    private Task? _originalInitialization;
    private Task? _originalClose;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return RunOriginal(InitializeOriginalCoreAsync, static _ => true, cancellationToken,
            original => _originalInitialization = original, () => _originalInitialization as Task<bool>);
    }

    private async Task InitializeOriginalCoreAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        RequireOriginalCurrent(cancellationToken);
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
            _model, new Decisions(this), new Readiness(this)), cancellationToken);
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
        var snapshot = _snapshot!;
        var request = CurrentRequest;
        var grant = snapshot.Grants.Count == 0 ? null : snapshot.Grants[_grantIndex];
        _shownDigest = request is null ? null : Digest(request);
        if (request?.RequestId != _warningRequest) _warningRequest = null;
        SetOriginalModel("Status", "Review the caller, action and affected objects before deciding.");
        SetOriginalModel("PendingCount", $"Pending requests: {snapshot.PendingRequests.Count}");
        SetOriginalModel("HasPending", request is not null);
        SetOriginalModel("Caller", request is null ? "" : "Requested by " + CallerName(request.Caller));
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
                if (command == "ShowTrustWarning")
                {
                    result = await permissions.MarkAlwaysTrustWarningShownAsync(shown.RequestId, cancellationToken).ConfigureAwait(false);
                    RequireOriginalCurrent(cancellationToken);
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

    private void RequireOriginalCurrent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void SetOriginalModel(string key, object? value)
    {
        RequireOriginalCurrent(_lifetime.Token);
        _model.Set(key, value);
        RequireOriginalCurrent(_lifetime.Token);
    }

    private abstract class OriginalOperation
    {
        internal abstract Task Original { get; }
        internal Task? Body;
        internal CancellationToken Caller;
        internal CancellationToken Token;
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
            linked = CancellationTokenSource.CreateLinkedTokenSource(operation.Caller, _lifetime.Token);
            operation.Token = linked.Token;
            RequireOriginalCurrent(operation.Token);
            operation.Body = acquire(operation.Token)
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
    /// Destructive consumers must retain and await this same task while the UI dispatcher is running.</summary>
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
        try { _lifetime.Cancel(); }
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
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try { Content = null; }
                catch (Exception error) { AddOriginalError(errors, error); }
                try { _host.Dispose(); }
                catch (Exception error) { AddOriginalError(errors, error); }
                _actor = null; _snapshot = null;
            });
        }
        catch (Exception error) { AddOriginalError(errors, error); }
        try { _gate.Dispose(); }
        catch (Exception error) { AddOriginalError(errors, error); }
        try { _lifetime.Dispose(); }
        catch (Exception error) { AddOriginalError(errors, error); }
        if (errors.Count == 0) completion.TrySetResult();
        else if (errors.Count == 1) completion.TrySetException(errors[0]);
        else completion.TrySetException(new AggregateException("Original Home approval callbacks and retirement failed.", errors));
    }

    private bool IsOriginalRetirementCancellation(OriginalOperation original, Exception error) =>
        error is OperationCanceledException cancelled &&
        (cancelled.CancellationToken == original.Token || cancelled.CancellationToken == _lifetime.Token) &&
        _lifetime.IsCancellationRequested && !original.Caller.IsCancellationRequested;

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
            "Accept" or "Trust" or "Decline" or "DeclineBlock" or "ShowTrustWarning" or "ConfirmAlwaysTrust" or "TemporaryDuration" or "TemporaryActions" or "RevokeGrant" or "UnblockCaller";
        public bool? IsActionAvailable(string command) => HasAction(command);
    }
}
