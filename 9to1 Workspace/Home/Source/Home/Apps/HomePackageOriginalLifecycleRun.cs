using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Home.Apps;

internal sealed record HomePackageOriginalLifecycleObservation(
    string OperationId, HomePackageOperationState State, int CompletedSteps,
    string? CurrentPackageId, string? OriginalPermissionRequestId,
    IReadOnlyList<HomePackageActionResult> KnownOriginalResults,
    bool AuditOnlyAfterAttempt, Task? OriginalRun);

// One continuing installer run, built only from a SAME-session original admission.
// It never constructs a Core session, authenticates from an actor DTO, opens another
// socket, approves a permission request, or disposes the borrowed platform owner.
internal sealed class HomePackageOriginalLifecycleRun : IAsyncDisposable
{
    private readonly HomePackageOriginalOperationAdmission _admission;
    private readonly HomePackageOriginalDeviceOwner _owner;
    private readonly IHomePackageOriginalPlatformOwner _platform;
    private readonly HomePackageOriginalLifecyclePlan _plan;
    private HomePackageDatabaseSnapshot _expectedRegistry;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Task> _originalRuns = [];
    private readonly List<HomePackageActionResult> _known = [];
    private readonly string _operationId = Guid.NewGuid().ToString("N");
    private int _position;
    private HomePackageOriginalReview? _review;
    private Task<HomePackageOriginalReview>? _request;
    private Task<HomePackageActionResult>? _execute;
    private Task? _reuseObservation;
    private Task<HomePackageOriginalOperationObservation>? _rootRecovery;
    private Task? _approvalSettlement;
    private Task<HomePackageActionResult>? _run;
    private Task? _close, _admissionClose;
    private Task<HomePackageOriginalLifecycleObservation>? _recoveryRun;
    private HomePackageOperationState? _terminalState;
    private bool _interrupted, _auditOnly, _completed;

    internal HomePackageOriginalLifecycleRun(
        HomePackageOriginalOperationAdmission sameOriginalAdmission,
        HomePackageOriginalDeviceOwner sameOriginalOwner,
        HomePackageOriginalDependencyLifecycle sameOriginalPlanner,
        HomePackageOriginalLifecyclePlan sameOriginalPlan)
    {
        ArgumentNullException.ThrowIfNull(sameOriginalAdmission);
        ArgumentNullException.ThrowIfNull(sameOriginalOwner);
        ArgumentNullException.ThrowIfNull(sameOriginalPlanner);
        ArgumentNullException.ThrowIfNull(sameOriginalPlan);
        if (!sameOriginalAdmission.IsBoundToOriginalPlatform(sameOriginalOwner) ||
            !sameOriginalPlanner.IssuedBy(sameOriginalOwner) || !sameOriginalPlan.IssuedBy(sameOriginalPlanner))
            throw new UnauthorizedAccessException("The SAME private installer/session/owner/plan is required.");
        _admission = sameOriginalAdmission; _owner = sameOriginalOwner; _platform = sameOriginalOwner; _plan = sameOriginalPlan;
        _expectedRegistry = sameOriginalPlan.CopyOriginalRegistry();
    }

    // Explicit continuation after a permission prompt uses the same retained review.
    // If an effect has been attempted, this entry never invokes Execute again.
    internal Task<HomePackageActionResult> ContinueOriginalAsync(CancellationToken caller = default)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HomePackageActionResult> original;
        lock (_sync)
        {
            if (_close is not null) throw new ObjectDisposedException(nameof(HomePackageOriginalLifecycleRun));
            if (_run is { IsCompleted: false }) return _run;
            if (_completed || _terminalState is not null) return _run ?? throw new InvalidOperationException("Original completion task missing.");
            if (_interrupted || _auditOnly)
                throw new InvalidOperationException("Observe/reconcile the original interrupted effect; no redispatch is permitted.");
            _originalRuns.RemoveAll(item => item.IsCompletedSuccessfully);
            if (_originalRuns.Count >= 128)
                throw new InvalidOperationException("Retain original installer failures before admitting more continuations.");
            original = _run = ContinueAfterStartAsync(start.Task, caller);
            _originalRuns.Add(original);
        }
        start.SetResult(); // Publish the exact full continuation before any callback.
        return original;
    }

    private async Task<HomePackageActionResult> ContinueAfterStartAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        try
        {
            while (_position < _plan.Steps.Count)
            {
                var step = _plan.Steps[_position];
                await DemandExactRegistryAsync(linked.Token).ConfigureAwait(false);
                if (step.ReuseCompatibleInstallation)
                {
                    var original = _admission.DemandOriginalReuseAsync(step.OriginalSelection, _expectedRegistry, linked.Token);
                    lock (_sync) _reuseObservation = original;
                    await original.ConfigureAwait(false);
                    lock (_sync) _position++;
                    continue; // Protected/current observation only; no graph readiness is minted.
                }
                if (_review is null)
                {
                    var request = StepRequest(step);
                    var original = _admission.RequestAsync(request, linked.Token);
                    lock (_sync) _request = original;
                    var review = await original.ConfigureAwait(false);
                    lock (_sync) _review = review;
                    if (review.RegistryRevision != _expectedRegistry.Revision ||
                        review.OriginalPackageEntryRevision != step.OriginalEntryRevision ||
                        review.Artifact.SignedDescriptorSha256 != step.OriginalSelection.SignedDescriptorSha256 ||
                        review.Artifact.DescriptorPayloadSha256 != step.OriginalSelection.DescriptorPayloadSha256)
                        throw new UnauthorizedAccessException("The exact original planned package/revision changed.");
                }
                var retained = _review ?? throw new InvalidOperationException("Original step review was not retained.");
                if (Volatile.Read(ref retained.DispatchAttempted) != 0)
                {
                    lock (_sync) _auditOnly = true;
                    throw new InvalidOperationException("The SAME attempted effect is audit-only.");
                }
                var execute = _admission.ExecuteAsync(retained, linked.Token);
                lock (_sync) _execute = execute;
                var result = await execute.ConfigureAwait(false);
                if (result.State == HomePackageOperationState.Pending)
                    return Result(HomePackageOperationState.Pending, "HomePackages.PermissionRequired");
                lock (_sync) _auditOnly = Volatile.Read(ref retained.DispatchAttempted) != 0;
                CaptureHistoricOutcome(retained);
                if (result.State != HomePackageOperationState.Succeeded)
                {
                    lock (_sync) _terminalState = result.State;
                    return Result(result.State, result.Code);
                }
                await ConfirmSameCanonicalOutcomeAsync(retained, linked.Token).ConfigureAwait(false);
                lock (_sync)
                {
                    _position++;
                    _review = null; _request = null; _execute = null;
                    _auditOnly = false;
                }
            }
            lock (_sync) _completed = true;
            return Result(HomePackageOperationState.Succeeded, "HomePackages.LifecycleSucceeded");
        }
        catch (Exception primary)
        {
            lock (_sync)
            {
                _interrupted = true;
                if (_review is not null) _auditOnly |= Volatile.Read(ref _review.DispatchAttempted) != 0;
            }
            // Retain a known historic effect and the exact original body fault independently.
            // A step that never reached the owning device ledger remains explicitly unknown.
            var failures = new List<Exception> { primary };
            try { if (_review is not null) CaptureHistoricOutcome(_review); }
            catch (Exception error) { Add(failures, error); }
            Throw(failures);
            throw;
        }
    }

    // Audit-only recovery: all original effect tasks must already be settled.
    // It never calls Request, Execute, opens a replacement channel or renews approval.
    internal Task<HomePackageOriginalLifecycleObservation> RecoverOriginalAsync(CancellationToken settlement = default)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HomePackageOriginalLifecycleObservation> original;
        lock (_sync)
        {
            if (_close is not null) throw new ObjectDisposedException(nameof(HomePackageOriginalLifecycleRun));
            if (_recoveryRun is { IsCompleted: false }) return _recoveryRun;
            if (_run is not { IsCompleted: true } || _review is null ||
                Volatile.Read(ref _review.DispatchAttempted) == 0)
                throw new InvalidOperationException("A settled privately retained original attempt is required.");
            if (_originalRuns.Count >= 128)
                throw new InvalidOperationException("Retain original recovery causes before another audit.");
            original = _recoveryRun = RecoverAfterStartAsync(start.Task, _review, settlement);
            _originalRuns.Add(original);
        }
        start.SetResult();
        return original;
    }

    private async Task<HomePackageOriginalLifecycleObservation> RecoverAfterStartAsync(
        Task start, HomePackageOriginalReview review, CancellationToken settlement)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        // Both owning recoveries are retained independently; neither grants new work.
        try
        {
            var original = _owner.RecoverOriginalSettlementAsync(review.OperationId, settlement);
            lock (_sync) _rootRecovery = original;
            await original.ConfigureAwait(false);
        }
        catch (Exception error) { Add(failures, error); }
        try
        {
            var original = _admission.RetryOriginalSettlementAsync(review, settlement);
            lock (_sync) _approvalSettlement = original;
            await original.ConfigureAwait(false);
        }
        catch (Exception error) { Add(failures, error); }
        try { CaptureHistoricOutcome(review); }
        catch (Exception error) { Add(failures, error); }
        // Recovery does not authorize automatic next-step execution. The caller may
        // close this run and request a new current plan after canonical reconciliation.
        Throw(failures);
        return ObserveOriginal();
    }

    internal HomePackageOriginalLifecycleObservation ObserveOriginal()
    {
        lock (_sync)
            return new(_operationId, _completed ? HomePackageOperationState.Succeeded :
                _terminalState ?? (_auditOnly || _interrupted ? HomePackageOperationState.Unknown : HomePackageOperationState.Pending),
                _position, _position < _plan.Steps.Count ? _plan.Steps[_position].OriginalSelection.Descriptor.PackageId : null,
                _review?.Prepared.RequestId, Array.AsReadOnly(_known.ToArray()), _auditOnly, _run);
    }

    private void CaptureHistoricOutcome(HomePackageOriginalReview review)
    {
        if (Volatile.Read(ref review.DispatchAttempted) == 0) return;
        var observation = _owner.TryObserveOriginalOperation(review.OperationId);
        if (observation is null || !observation.RootOutcomeKnown || observation.ObservedRootResult is null) return;
        var result = observation.ObservedRootResult;
        if (result.OperationId != review.OperationId || result.PackageId != review.Request.PackageId ||
            result.Action != review.Request.Action)
            throw new InvalidDataException("The owning historic result does not bind the original step.");
        lock (_sync)
        {
            var existing = _known.SingleOrDefault(item => item.OperationId == result.OperationId);
            if (existing is not null && !Encode(existing).AsSpan().SequenceEqual(Encode(result)))
                throw new InvalidDataException("The SAME known original outcome cannot be overwritten.");
            if (existing is null) _known.Add(result);
        }
    }

    private async Task DemandExactRegistryAsync(CancellationToken token)
    {
        var read = await _platform.Database.ReadAsync(token).ConfigureAwait(false);
        if (!read.Succeeded) throw new HomePackageDatabaseException(read.Failure!);
        var observed = HomePackageDatabase.CaptureGuardedSnapshot(read.Snapshot!);
        if (!Encode(observed).AsSpan().SequenceEqual(Encode(_expectedRegistry)))
            throw new UnauthorizedAccessException("The canonical original installer snapshot changed; replan before any effect.");
    }

    private async Task ConfirmSameCanonicalOutcomeAsync(HomePackageOriginalReview review, CancellationToken token)
    {
        var original = _owner.ObserveOriginalOperation(review.OperationId);
        if (!original.RootOutcomeKnown || !original.CanonicalSettlementConfirmed ||
            original.ConfirmedRegistryRevision is not long revision ||
            original.ObservedRootResult?.State != HomePackageOperationState.Succeeded)
            throw new InvalidOperationException("The SAME known canonical outcome requires audit-only reconciliation.");
        var read = await _platform.Database.ReadAsync(token).ConfigureAwait(false);
        if (!read.Succeeded) throw new HomePackageDatabaseException(read.Failure!);
        var snapshot = HomePackageDatabase.CaptureGuardedSnapshot(read.Snapshot!);
        var journals = snapshot.RecentOperations.Where(item => item.OperationId == review.OperationId).ToArray();
        if (snapshot.Revision != revision || journals.Length != 1 ||
            journals[0].IdempotencyKey != review.Request.IdempotencyKey ||
            journals[0].PackageId != review.Request.PackageId ||
            journals[0].Action != review.Request.Action.ToString() ||
            journals[0].State != HomePackageJournalState.Succeeded)
            throw new UnauthorizedAccessException("The SAME terminal canonical journal/revision changed.");
        var previousOthers = _expectedRegistry.Packages.Where(item => item.PackageId != review.Request.PackageId)
            .OrderBy(item => item.PackageId, StringComparer.Ordinal).ToArray();
        var observedOthers = snapshot.Packages.Where(item => item.PackageId != review.Request.PackageId)
            .OrderBy(item => item.PackageId, StringComparer.Ordinal).ToArray();
        if (!Encode(previousOthers).AsSpan().SequenceEqual(Encode(observedOthers)))
            throw new UnauthorizedAccessException("Another canonical package changed during the original step.");
        // The device owner independently verified its exact observed entry/result and
        // original raw commit guard. This sequential observation is no cross-store atomic grant.
        _expectedRegistry = snapshot;
    }

    private HomePackageActionRequest StepRequest(HomePackageOriginalLifecycleStep step)
    {
        var selected = step.OriginalSelection;
        if (selected.Descriptor.PackageId == _plan.OriginalRequest.PackageId)
            return _plan.OriginalRequest with { Action = step.Action };
        var digest = Convert.ToHexString(SHA256.HashData(Encode(new {
            Parent = _plan.OriginalRequest, selected.Descriptor.PackageId, step.Action,
            selected.Descriptor.Version, selected.Descriptor.Channel,
            selected.SignedDescriptorSha256, selected.DescriptorPayloadSha256
        })));
        return new(selected.Descriptor.PackageId, step.Action, "dependency-" + digest,
            selected.Descriptor.Version, selected.Descriptor.Channel, selected.CatalogueRevision);
    }

    private HomePackageActionResult Result(HomePackageOperationState state, string code)
    {
        lock (_sync)
            return new(_operationId, _plan.OriginalRequest.PackageId, _plan.OriginalRequest.Action, state, code,
                state == HomePackageOperationState.Succeeded ? "The original dependency lifecycle completed." :
                    "The original installer needs approval or audit; known effects remain retained.",
                false, true, _known.Any(item => item.PreviousKnownGoodVersionRetained),
                Array.AsReadOnly(_known.Where(item => item.State == HomePackageOperationState.Succeeded)
                    .Select(item => item.PackageId + ":" + item.Code).ToArray()),
                Array.AsReadOnly(_known.Where(item => item.State == HomePackageOperationState.Failed)
                    .Select(item => item.PackageId + ":" + item.Code).ToArray()),
                Array.AsReadOnly(_plan.Steps.Take(_position).Where(item => item.ReuseCompatibleInstallation)
                    .Select(item => item.OriginalSelection.Descriptor.PackageId).ToArray()),
                Array.AsReadOnly(_known.SelectMany(item => item.RolledBackSteps).ToArray()));
    }

    internal Task CloseAndDrainAsync()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task original;
        lock (_sync)
        {
            if (_close is not null) return _close;
            original = _close = CloseAfterStartAsync(start.Task, _originalRuns.ToArray());
        }
        start.SetResult();
        return original;
    }

    private async Task CloseAfterStartAsync(Task start, Task[] originals)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { Add(failures, error); }
        foreach (var original in originals)
            try { await original.ConfigureAwait(false); } catch (Exception error) { Add(failures, error); }
        Task?[] inner;
        lock (_sync) inner = new Task?[] { _request, _execute, _rootRecovery, _approvalSettlement, _reuseObservation };
        foreach (var original in inner)
            if (original is not null)
                try { await original.ConfigureAwait(false); } catch (Exception error) { Add(failures, error); }
        // Exact request/execute/recovery tasks were retained before every await. The
        // admission owns their final broker/resource settlement and is closed only
        // after the installer frames have settled. Device/store/socket remain borrowed.
        try
        {
            var original = _admission.CloseAndDrainAsync();
            lock (_sync) _admissionClose = original;
            await original.ConfigureAwait(false);
        }
        catch (Exception error) { Add(failures, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
        Throw(failures);
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(prior => ReferenceEquals(prior, error))) failures.Add(error); }
    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original installer body/audit/close failures retained.", failures);
    }
}
