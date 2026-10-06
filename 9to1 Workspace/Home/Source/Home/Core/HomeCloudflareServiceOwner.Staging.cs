using System.Globalization;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

public sealed partial class HomeCloudflareServiceOwner : ICloudflareStagingDelegationSource
{
    private const string StagingAction = "cloudflare.staging.delegateTaskMarker";
    private sealed record BorrowedNamespace(string ProfileId, Guid ConnectionId, string AccountId,
        CloudflareStagingBindingSelection Selection, string NamespaceId, string OriginalProjectionDigest);
    private static string StagingRecordId(string account, string worker, string binding) => "home.cloudflare.staging:" + account + ":" + worker + ":" + binding;
    private static string WorkerScopeId(string account, string worker, string binding) => "cloudflare:worker:" + account + "/" + worker + "/kv/" + binding;
    private readonly List<StagingReview> _stagingReviews = [];
    private bool _stagingSealed; private Task? _stagingClose;
    private readonly CloudflareOriginalTaskLedger _stagingCloseStages = new();
    public void RequestOriginalStagingRetirement()
    {
        StagingReview[] originals; lock (_sync) { _stagingSealed = true; originals = _stagingReviews.ToArray(); }
        foreach (var original in originals) original.RequestRetirement();
    }
    public void DemandExternalOriginalStagingJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        StagingReview[] originals; lock (_sync) originals = _stagingReviews.ToArray();
        foreach (var original in originals) original.DemandExternalJoin();
    }
    public Task CloseAndDrainOriginalStagingReviewsAsync()
    {
        DemandExternalOriginalStagingJoin();
        lock (_sync)
        {
            if (_stagingClose is not null) return _stagingClose; _stagingSealed = true;
            var originals = _stagingReviews.ToArray(); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stagingCloseStages.BindOriginalOwner(this); _stagingClose = CloseStagingPublishedAsync(begin.Task, originals); begin.SetResult(); return _stagingClose;
        }
    }
    private async Task CloseStagingPublishedAsync(Task begin, StagingReview[] originals)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>(); foreach (var original in originals) original.RequestRetirement();
        foreach (var original in originals)
            try { await _stagingCloseStages.AwaitAsync(_stagingCloseStages.Invoke(() => original.DisposeAsync())).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Original staging setup retirement failed.", errors);
    }
    public Task<ICloudflareOriginalStagingReview> PrepareStagingDelegationAsync(CloudflareStagingBindingSelection selection,
        long expectedRevision, CancellationToken token)
    {
        CloudflareStagingBindingContract.DemandSelection(selection);
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        lock (_sync)
        {
            if (_stagingSealed) throw new ObjectDisposedException("Original staging delegation admission");
            if (_stagingReviews.Count >= 128) throw new InvalidOperationException("Original staging review custody is full.");
            if (_mcp is not ICloudflareWorkerBindingClient) throw new NotSupportedException("The configured actual MCP client has no fixed Worker binding producer.");
            // Detach user metadata once; only the retained review can subsequently use it.
            var original = new StagingReview(this, selection with { }, expectedRevision);
            _stagingReviews.Add(original); return original.StartPreparation(token);
        }
    }
    private HomeCoreStateRecord? FindBorrowedNamespace(HomeCoreStoredState state, CloudflareCompiledInvocation invocation)
    {
        if (invocation.Descriptor.Kind is not (CloudflareOperationKind.KvMarkerPut or CloudflareOperationKind.KvMarkerGet or CloudflareOperationKind.KvMarkerDelete)) return null;
        var found = state.Records.Where(x => RecordShape(x, "home.cloudflare.staging") && x.Payload.Deserialize<BorrowedNamespace>() is { } ns &&
            ns.NamespaceId == invocation.NamespaceId && ns.AccountId == invocation.Service.AccountId && ns.ConnectionId == invocation.Service.Connection.Id).Take(2).ToArray();
        if (found.Length > 1) throw new InvalidDataException("Ambiguous borrowed staging namespace; review its original provenance.");
        return found.SingleOrDefault();
    }
    private static bool IsBorrowedMarker(CloudflareCompiledInvocation invocation, HomeCoreStateRecord record) =>
        RecordShape(record, "home.cloudflare.staging") && invocation.Descriptor.Kind is
            CloudflareOperationKind.KvMarkerPut or CloudflareOperationKind.KvMarkerGet or CloudflareOperationKind.KvMarkerDelete;

    private sealed class StagingReview(HomeCloudflareServiceOwner owner, CloudflareStagingBindingSelection selection, long expectedRevision)
        : ICloudflareOriginalStagingReview
    {
        private readonly object _gate = new(); private readonly CloudflareOriginalTaskLedger _stages = new();
        private readonly CloudflareOriginalTaskLedger _closingStages = new();
        private Task<ICloudflareOriginalStagingReview>? _prepare; private Task<CloudflareSetupObservation>? _submit, _commit;
        private CancellationTokenSource? _stop; private Task? _close; private bool _sealed;
        private CloudflareSavedService? _service; private Capture? _capture; private HomeCoreStateRecord? _prior;
        private HomeResourcePreparedReview? _review; private JsonElement _arguments;
        private CloudflareWorkerBindingObservation? _binding;
        public string RequestId => _review?.RequestId ?? throw new InvalidOperationException("The actual staging review is not prepared.");
        internal void RequestRetirement() { lock (_gate) _sealed = true; }
        private void DemandLive() { lock (_gate) if (_sealed) throw new ObjectDisposedException(nameof(StagingReview)); }
        internal void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        internal Task<ICloudflareOriginalStagingReview> StartPreparation(CancellationToken token)
        {
            lock (_gate)
            {
                if (_sealed) throw new ObjectDisposedException(nameof(StagingReview)); _stages.BindOriginalOwner(this);
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _prepare = PreparePublishedAsync(begin.Task, token); begin.SetResult(); return _prepare;
            }
        }
        private async Task<ICloudflareOriginalStagingReview> PreparePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _stages.RunToOriginalSettlementAsync(async () =>
            {
                _stop = _stages.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(token));
                _service = await _stages.AwaitAsync(_stages.Invoke(() => owner.AcquireOriginalAsync(_stop.Token))).ConfigureAwait(false);
                if (!owner._services.TryGetValue(_service, out _capture)) throw new UnauthorizedAccessException("SAME configured Home service required.");
                _prior = Single(await owner.StateAsync(_stages, _stop.Token).ConfigureAwait(false), StagingRecordId(_service.AccountId, selection.WorkerName, selection.BindingName));
                if ((_prior?.Revision ?? 0) != expectedRevision || _prior is not null &&
                    (!RecordShape(_prior, "home.cloudflare.staging") || _prior.Payload.Deserialize<BorrowedNamespace>()?.ProfileId != _capture.Actor.ProfileId))
                    throw new CloudflareSetupRequiredException(CloudflareSetupStage.ConfigurationConflict, "CF_STAGING_CONFLICT", "Reload the original staging delegation before reviewing it.");
                _arguments = JsonSerializer.SerializeToElement(new { selection, expectedRevision, connectionId = _service.Connection.Id, accountId = _service.AccountId,
                    operation = "Read the exact staging Worker KV binding and save a marker-only delegation. No remote mutation or borrowed namespace deletion." });
                _review = _stages.Invoke(() => owner._broker.PrepareReviewForActor(_capture.Actor, "cloudflare", StagingAction,
                    [new ResourceScope(owner.ResourceKind, "cloudflare:account:" + _service.AccountId, _capture.Record.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write)],
                    _arguments, "Read Worker " + selection.WorkerName + ", binding " + selection.BindingName + "; delegate only individually reviewed Task/Run marker keys.", null,
                    "cloudflare:staging:" + _capture.Actor.AuthenticationRevision));
                lock (_gate) if (_sealed) throw new ObjectDisposedException(nameof(StagingReview)); return this;
            }).ConfigureAwait(false);
        }
        public Task<CloudflareSetupObservation> SubmitOriginalAsync(CancellationToken token)
        {
            DemandExternalJoin(); lock (_gate)
            {
                if (_submit is not null) return _submit; if (_sealed || _prepare?.IsCompletedSuccessfully != true) throw new ObjectDisposedException(nameof(StagingReview));
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _submit = SubmitPublishedAsync(begin.Task, token); begin.SetResult(); return _submit;
            }
        }
        private async Task<CloudflareSetupObservation> SubmitPublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await WithLinkedAsync(async activeToken =>
            {
            var observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(_review!, activeToken))).ConfigureAwait(false);
            if (observed.State != HomePreparedReviewState.RequestObserved || observed.Request is null) throw new UnauthorizedAccessException(observed.Code);
            return new CloudflareSetupObservation(false, "CF_STAGING_REVIEW_REQUIRED", RequestId, expectedRevision);
            }, token).ConfigureAwait(false);
        }
        public Task<CloudflareSetupObservation> CommitOriginalAsync(CancellationToken token)
        {
            DemandExternalJoin(); lock (_gate)
            {
                if (_commit is not null) return _commit;
                if (_sealed || _submit?.IsCompletedSuccessfully != true) throw new InvalidOperationException("Publish and explicitly approve the original staging review first.");
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _commit = CommitPublishedAsync(begin.Task, token); begin.SetResult(); return _commit;
            }
        }
        private async Task<CloudflareSetupObservation> CommitPublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _stages.RunToOriginalSettlementAsync(() => WithLinkedAsync(async activeToken =>
            {
                var observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ObservePreparedReviewAsync(_review!, activeToken))).ConfigureAwait(false);
                if (observed.Request?.State != HomePermissionRequestState.Approved)
                    throw new CloudflareSetupRequiredException(CloudflareSetupStage.ApprovalRequired, "CF_STAGING_REVIEW_REQUIRED", "Accept the exact staging binding setup in Home; no read or save was dispatched.", RequestId);
                await _stages.AwaitAsync(_stages.Invoke(() => owner.RevalidateOriginalAsync(_service!, activeToken))).ConfigureAwait(false);
                DemandLive();
                var capability = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(RequestId, _arguments, activeToken))).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The actual staging capability is unavailable.");
                var claim = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ClaimExecutionObservedAsync(capability, "cloudflare", StagingAction, _review!.Scopes, _arguments, activeToken))).ConfigureAwait(false);
                if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != _capture!.Actor) throw new UnauthorizedAccessException("Original staging claim rejected.");
                var attestation = owner._broker.CaptureClaimedAttestation(capability) ?? throw new UnauthorizedAccessException("Individual Home Accept is required.");
                var reader = new WorkerReadAdmission(owner, _service!, selection, _capture!, capability, attestation,
                    _prior is null ? [_capture!.Record] : [_capture!.Record, _prior], _stages, null, DemandLive);
                var sdk = (ICloudflareWorkerBindingClient)owner._mcp;
                _binding = await _stages.AwaitAsync(_stages.Invoke(() => sdk.ReadOriginalWorkerBindingAsync(_service!, selection, reader, InvokeOriginalCallback, activeToken))).ConfigureAwait(false);
                foreach (var actual in _binding.OriginalTasks) _ = _stages.Track(actual);
                foreach (var cause in _binding.OriginalErrors) _stages.Retain(cause);
                if (_binding.OriginalErrors.Count != 0) throw new AggregateException("The actual original Worker read/cleanup failed.", _binding.OriginalErrors);
                if (!sdk.IsIssuedOriginalWorkerBinding(_service!, selection, _binding)) throw new UnauthorizedAccessException("SAME healthy original settings response required.");
                await _stages.AwaitAsync(_stages.Invoke(() => owner.RevalidateOriginalAsync(_service!, activeToken))).ConfigureAwait(false);
                if (CloudflareStagingBindingContract.DemandNamespace(_binding.OriginalProjection, selection) != _binding.NamespaceId)
                    throw new UnauthorizedAccessException("The actual SDK binding projection and retained namespace disagree.");
                var recordId = StagingRecordId(_service!.AccountId, selection.WorkerName, selection.BindingName);
                var value = new BorrowedNamespace(_capture!.Actor.ProfileId, _service.Connection.Id, _service.AccountId, selection, _binding.NamespaceId, Fingerprint(_binding.OriginalProjection));
                var record = new HomeCoreStateRecord(recordId, "home.cloudflare.staging", 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
                    checked(expectedRevision + 1), JsonSerializer.SerializeToElement(value));
                var guard = new StagingCommitGuard(this, new ClaimedStateGuard(owner, _capture.Actor, attestation, _prior is null ? [_capture.Record] : [_capture.Record, _prior], recordId, expectedRevision));
                DemandLive();
                var saved = await _stages.AwaitAsync(_stages.Invoke(() => owner._store.WriteGuardedAsync(record, expectedRevision, _capture.Actor, guard, activeToken))).ConfigureAwait(false);
                if (!saved.IsSuccess) throw new CloudflareSetupRequiredException(CloudflareSetupStage.ConfigurationConflict, "CF_STAGING_CAS_UNCONFIRMED", "Retain the original setup; its guarded save was not acknowledged.", RequestId);
                var audit = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.CompleteExecutionAsync(capability,
                    new(HomePermissionRequestState.Succeeded, "CF_STAGING_DELEGATION_SAVED", "Saved the actual original KV binding for marker-only individual Task reviews; no remote mutation.", []), CancellationToken.None))).ConfigureAwait(false);
                if (!audit.Succeeded) throw new InvalidOperationException("Staging delegation save is known but Home audit is unfinished: " + audit.Code);
                return new CloudflareSetupObservation(true, "CF_STAGING_DELEGATION_SAVED", RequestId, record.Revision);
            }, token)).ConfigureAwait(false);
        }
        private void InvokeOriginalCallback(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; });
        private Task<T> WithLinkedAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken token)
        {
            CancellationTokenSource? linked = null;
            return CloudflareOriginalPartialEntryCustody.RunOriginalAsync(_stages, async () =>
            {
                linked = _stages.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(token, _stop!.Token));
                return await _stages.AwaitAsync(_stages.Invoke(() => body(linked.Token))).ConfigureAwait(false);
            }, () => linked is { } actual ? [() => { _stages.Invoke(() => { actual.Dispose(); return true; }); return ValueTask.CompletedTask; }] : []);
        }
        private sealed class StagingCommitGuard(StagingReview original, IHomeStateCommitActorGuard inner) : IHomeStateCommitActorGuard
        {
            public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor, HomeStateCommitPhase phase, CancellationToken token)
            {
                original.DemandLive(); var allowed = await original._stages.AwaitAsync(original._stages.Invoke(() => inner.CheckAsync(state, actor, phase, token))).ConfigureAwait(false);
                original.DemandLive(); return allowed;
            }
        }
        public ValueTask DisposeAsync()
        {
            DemandExternalJoin(); lock (_gate)
            {
                if (_close is not null) return new(_close); _sealed = true;
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _closingStages.BindOriginalOwner(this);
                _close = ClosePublishedAsync(begin.Task); begin.SetResult(); return new(_close);
            }
        }
        private async Task ClosePublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var failures = new List<Exception>();
            try { _closingStages.Invoke(() => { _stop?.Cancel(); return true; }); } catch (Exception cause) { failures.Add(cause); }
            Task[] drivers; lock (_gate) drivers = new Task?[] { _prepare, _submit, _commit }.OfType<Task>().ToArray();
            foreach (var driver in drivers) try { await _closingStages.AwaitAsync(driver).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            await _stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            try { _closingStages.Invoke(() => { _stop?.Dispose(); return true; }); } catch (Exception cause) { failures.Add(cause); }
            await _closingStages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _closingStages.OriginalErrors) if (!failures.Any(x => ReferenceEquals(x, cause))) failures.Add(cause);
            foreach (var cause in _stages.OriginalErrors) if (!failures.Any(x => ReferenceEquals(x, cause))) failures.Add(cause);
            if (failures.Count != 0) throw new AggregateException("Original staging setup drivers/children/cleanup failed.", failures);
        }
    }

    // Private admission issued only from an actual Home claim; no caller can construct this scope.
    private sealed class WorkerReadAdmission(HomeCloudflareServiceOwner owner, CloudflareSavedService service,
        CloudflareStagingBindingSelection selection, Capture capture, HomeResourceExecutionCapability capability,
        HomeClaimedResourceAttestation attestation, HomeCoreStateRecord[] records, CloudflareOriginalTaskLedger stages,
        CloudflareOriginalTaskBinding? taskBinding, Action demandCurrent) : ICloudflareOriginalWorkerReadAuthority
    {
        private HomeCloudflareServiceOwner Owner => owner;
        private CloudflareSavedService Service => service;
        private CloudflareStagingBindingSelection Selection => selection;
        private void DemandLive() => demandCurrent();
        // Raw acquisitions have their own cohort; the enclosing SDK/review driver is never
        // enrolled into the cohort that its child is settling.
        private readonly CloudflareOriginalTaskLedger _workerStages = CreateWorkerStages(stages);
        private static CloudflareOriginalTaskLedger CreateWorkerStages(CloudflareOriginalTaskLedger parent)
        {
            var originals = new CloudflareOriginalTaskLedger();
            originals.BindOriginalCallerCallback(action => parent.Invoke(() => { action(); return true; })); return originals;
        }
        private CloudflareOriginalTaskLedger Stages => _workerStages;
        private T Invoke<T>(Func<T> finite) => _workerStages.Invoke(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, finite));
        private void TransferOriginalCustody()
        {
            foreach (var actual in _workerStages.OriginalTasks) _ = stages.Track(actual);
            foreach (var cause in _workerStages.OriginalErrors) stages.Retain(cause);
        }
        private WorkerReadEntry? _entry; private Task<ICloudflareOriginalWorkerReadEntry>? _acquire;
        private readonly object _gate = new();
        public ValueTask<ICloudflareOriginalWorkerReadEntry> EnterOriginalWorkerReadAsync(CloudflareSavedService actualService,
            CloudflareStagingBindingSelection actualSelection, CancellationToken token)
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            DemandLive();
            lock (_gate)
            {
                if (!ReferenceEquals(service, actualService) || !ReferenceEquals(selection, actualSelection)) throw new UnauthorizedAccessException("SAME original binding read required.");
                if (_acquire is not null) return new(_acquire);
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _workerStages.BindOriginalOwner(this);
                _acquire = EnterPublishedAsync(begin.Task, token); _ = stages.Track(_acquire); begin.SetResult(); return new(_acquire);
            }
        }
        private async Task<ICloudflareOriginalWorkerReadEntry> EnterPublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); IAsyncDisposable? completion = null, pin = null; IHomeLocalOperationLease? home = null;
            try
            {
            return await CloudflareOriginalPartialEntryCustody.RunOriginalAsync(_workerStages, async () =>
            {
                await _workerStages.AwaitAsync(Invoke(() => owner.RevalidateCallerScopedOriginalAsync(service, stages.OriginalCallerCallback, token))).ConfigureAwait(false);
                if (taskBinding is not null)
                {
                    await _workerStages.AwaitAsync(Invoke(() => owner._actions.ValidateOriginalActionAdmissionAsync(taskBinding.ActionAdmission, taskBinding.OriginalPreparation, taskBinding.OriginalAttempt, token))).ConfigureAwait(false);
                    await _workerStages.AwaitAsync(Invoke(() => taskBinding.OriginalAttempt.Lease.RevalidateAsync(token))).ConfigureAwait(false);
                }
                DemandLive();
                completion = await _workerStages.CaptureOriginalAcquisitionAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => capability.AcquireCommitCompletionLeaseAsync(owner._broker, token).AsTask()), actual => completion = actual).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Original binding read completion is closing.");
                home = await _workerStages.CaptureOriginalAcquisitionAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => owner._store.AcquireLocalOperationLeaseCoreAsync(owner._profiles, capture.Actor,
                    new WorkerReadStateGuard(this, new ClaimedStateGuard(owner, capture.Actor, attestation, records)), token).AsTask()), actual => home = actual).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Original binding read Home entry is stale.");
                if (!await _workerStages.AwaitAsync(Invoke(() => home.IsCurrentAsync(token).AsTask())).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original binding read Home state changed.");
                if (taskBinding is not null)
                {
                    if (taskBinding.OriginalAttempt.Lease is not ITaskRunAdmissionCommitLease lease) throw new UnauthorizedAccessException("Actual attempt lifetime pin required.");
                    pin = await _workerStages.CaptureOriginalAcquisitionAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => lease.AcquireOriginalCommitPinAsync(token).AsTask()), actual => pin = actual).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("Original binding read attempt is retiring.");
                }
                var issued = new WorkerReadEntry(this, home, completion, pin, _workerStages, taskBinding);
                lock (_gate) { if (_entry is not null) throw new UnauthorizedAccessException("Original binding read entry is one-use."); _entry = issued; }
                home = null; completion = null; pin = null; return issued;
            }, () =>
            {
                var closes = new List<Func<ValueTask>>(); if (pin is { } actualPin) closes.Add(actualPin.DisposeAsync);
                if (home is { } actualHome) closes.Add(actualHome.DisposeAsync); if (completion is { } actualCompletion) closes.Add(actualCompletion.DisposeAsync); return closes;
            }).ConfigureAwait(false);
            }
            finally
            {
                await _workerStages.ObserveAllOriginalTasksAsync().ConfigureAwait(false); TransferOriginalCustody();
            }
        }
        private sealed class WorkerReadStateGuard(WorkerReadAdmission admission, IHomeStateCommitActorGuard inner) : IHomeStateCommitActorGuard
        {
            public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor, HomeStateCommitPhase phase, CancellationToken token)
            {
                admission.DemandLive(); var allowed = await admission.Stages.AwaitAsync(admission.Invoke(() => inner.CheckAsync(state, actor, phase, token).AsTask())).ConfigureAwait(false);
                admission.DemandLive(); return allowed;
            }
        }
        public void DemandOriginalWorkerReadEntry(CloudflareSavedService actualService, CloudflareStagingBindingSelection actualSelection, ICloudflareOriginalWorkerReadEntry actualEntry)
        {
            DemandLive();
            lock (_gate) if (!ReferenceEquals(actualService, service) || !ReferenceEquals(actualSelection, selection) ||
                !ReferenceEquals(actualEntry, _entry) || _acquire?.IsCompletedSuccessfully != true || _entry?.IsClosed != false || !capability.IsUncompletedClaim(owner._broker))
                throw new UnauthorizedAccessException("SAME private current held binding read entry required.");
        }
        private sealed class WorkerReadEntry(WorkerReadAdmission admission, IHomeLocalOperationLease home, IAsyncDisposable completion,
            IAsyncDisposable? pin, CloudflareOriginalTaskLedger stages, CloudflareOriginalTaskBinding? taskBinding) : ICloudflareOriginalWorkerReadEntry
        {
            private readonly object _sync = new(); private bool _used; private Task? _close;
            internal bool IsClosed { get { lock (_sync) return _close is not null; } }
            public T RunOriginalRead<T>(Func<T> body, CancellationToken token)
            {
                lock (admission._gate) lock (_sync)
                {
                    admission.DemandOriginalWorkerReadEntry(admission.Service, admission.Selection, this); token.ThrowIfCancellationRequested();
                    if (_used) throw new UnauthorizedAccessException("The original Worker settings read is one-use."); _used = true;
                    if (taskBinding is not null) admission.Owner._actions.DemandOriginalActionAdmission(taskBinding.ActionAdmission, taskBinding.OriginalPreparation, taskBinding.OriginalAttempt);
                    return CloudflareOriginalExecutionGuard.InvokeOriginal(this, body);
                }
            }
            public ValueTask DisposeAsync()
            {
                CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
                lock (_sync) { if (_close is not null) return new(_close); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublishedAsync(begin.Task); begin.SetResult(); return new(_close); }
            }
            private async Task ClosePublishedAsync(Task begin)
            {
                await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                var failures = new List<Exception>();
                if (pin is not null) try { await stages.ObserveOriginalCloseAsync(pin.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                try { await stages.ObserveOriginalCloseAsync(home.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                try { await stages.ObserveOriginalCloseAsync(completion.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                await stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                admission.TransferOriginalCustody();
                foreach (var cause in stages.OriginalErrors) if (!failures.Any(x => ReferenceEquals(x, cause))) failures.Add(cause);
                if (failures.Count != 0) throw new AggregateException("Original binding read entry closes failed.", failures);
            }
        }
    }
}
