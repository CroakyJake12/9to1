using System.Globalization;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

public sealed partial class HomeCloudflareServiceOwner : ICloudflareKnownCreateReconciliationSource, ICloudflareKnownCreateRecoveryRetirementSource
{
    private const string ReconcileAction = "cloudflare.kv.reconcileKnownCreate";
    private readonly Dictionary<CloudflareCompiledInvocation, KnownCreateReview> _recoveries = new(ReferenceEqualityComparer.Instance);
    private bool _recoveryAdmissionSealed;
    private Task? _recoveryClose;
    private readonly CloudflareOriginalTaskLedger _recoveryStages = new();
    public void RequestOriginalRecoveryRetirement()
    {
        KnownCreateReview[] originals;
        lock (_sync) { _recoveryAdmissionSealed = true; originals = _recoveries.Values.ToArray(); }
        foreach (var original in originals) original.RequestOriginalRetirement();
    }
    public void DemandExternalOriginalRecoveryJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        KnownCreateReview[] originals; lock (_sync) originals = _recoveries.Values.ToArray();
        foreach (var original in originals) original.DemandExternalOriginalJoin();
    }
    public Task CloseAndDrainOriginalRecoveriesAsync()
    { DemandExternalOriginalRecoveryJoin(); return CloseOriginalHostRecoveriesAsync(); }
    private Task CloseOriginalHostRecoveriesAsync()
    {
        lock (_hostGate) lock (_sync)
        {
            if (_recoveryClose is not null) return _recoveryClose;
            _recoveryAdmissionSealed = true; _recoveryStages.BindOriginalOwner(this); _recoveryStages.BindOriginalCallerCallback(OriginalHostCaller(null)); AttachOriginalHostSources(_recoveryStages);
            var originals = _recoveries.Values.ToArray();
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _recoveryClose = CloseRecoveryPublishedAsync(begin.Task, originals); begin.SetResult(); return _recoveryClose;
        }
    }
    private async Task CloseRecoveryPublishedAsync(Task begin, KnownCreateReview[] originals)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>();
        // Request every original before awaiting any sibling. Home stays borrowed/open.
        foreach (var original in originals) original.RequestOriginalRetirement();
        foreach (var original in originals)
            try { await _recoveryStages.AwaitAsync(_recoveryStages.Invoke(original.CloseAndDrainOriginalAsync)).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Original known-create recovery retirement failed.", errors);
    }
    private sealed record KnownCreate(Permission Permission, Capture Capture, CloudflareMcpDispatchResult Response,
        string NamespaceId, string Title);
    private KnownCreate RequireKnownCreate(CloudflareCompiledInvocation invocation)
    {
        Permission original;
        lock (_sync) original = _originals.GetValueOrDefault(invocation)
            ?? throw new CloudflareSetupRequiredException(CloudflareSetupStage.NamespaceRecoveryRequired, "CF_ORIGINAL_CREATE_NOT_RETAINED",
                "The SAME original Home/SDK create must be retained. A namespace ID or matching title cannot replace it.");
        return original.ReadKnownCreateOriginal();
    }
    public Task<ICloudflareOriginalKnownCreateReview> PrepareKnownCreateReconciliationAsync(CloudflareCompiledInvocation invocation, CancellationToken token)
    {
        lock (_sync) if (_recoveryAdmissionSealed) throw new ObjectDisposedException("Original known-create recovery admission");
        if (invocation.Descriptor.Kind != CloudflareOperationKind.KvCreate || !CloudflareTypedToolCatalogue.IsIssuedOriginal(invocation))
            throw new UnauthorizedAccessException("SAME privately compiled original create required.");
        var known = RequireKnownCreate(invocation);
        lock (_hostGate) lock (_sync)
        {
            if (_recoveryAdmissionSealed) throw new ObjectDisposedException("Original known-create recovery admission");
            if (_recoveries.TryGetValue(invocation, out var prior)) { prior.DemandExternalOriginalJoin(); return prior.OriginalPreparation; }
            if (_recoveries.Count >= 128) throw new InvalidOperationException("Original reconciliation custody is full.");
            var review = new KnownCreateReview(this, invocation, known); _recoveries.Add(invocation, review);
            return review.PrepareOriginalAsync(token);
        }
    }
    private sealed class KnownCreateReview(HomeCloudflareServiceOwner owner, CloudflareCompiledInvocation invocation, KnownCreate known)
        : ICloudflareOriginalKnownCreateReview
    {
        private readonly object _sync = new(); private readonly CloudflareOriginalTaskLedger _stages = owner.CreateOriginalHostSources();
        internal Task<ICloudflareOriginalKnownCreateReview> OriginalPreparation { get; private set; } = null!;
        private Task<CloudflareNamespaceRecoveryObservation>? _submit, _commit;
        private CancellationTokenSource? _stop; private bool _retiring; private Task? _close;
        public void RequestOriginalRetirement() { lock (_sync) _retiring = true; }
        public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin();
            lock (_sync)
            {
                if (_close is not null) return _close; _retiring = true;
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = ClosePublishedAsync(begin.Task); begin.SetResult(); return _close;
            }
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
        private async Task ClosePublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var errors = new List<Exception>();
            try { _stages.Invoke(() => { _stop?.Cancel(); return true; }); } catch (Exception cause) { errors.Add(cause); }
            Task[] drivers; lock (_sync) drivers = new Task?[] { OriginalPreparation, _submit, _commit }.OfType<Task>().ToArray();
            foreach (var driver in drivers)
                try { await _stages.AwaitAsync(driver).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            // Join retained raw children independently even when their encompassing driver failed.
            foreach (var raw in _stages.OriginalTasks.ToArray())
                try { await _stages.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { _stages.Invoke(() => { _stop?.Dispose(); return true; }); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("Original reconciliation drivers and raw children failed.", errors);
        }
        private HomeResourcePreparedReview? _review; private HomeCoreStateRecord? _existing; private JsonElement _arguments;
        public string RequestId => _review?.RequestId ?? throw new InvalidOperationException("The original reconciliation preparation has not completed.");
        internal Task<ICloudflareOriginalKnownCreateReview> PrepareOriginalAsync(CancellationToken token)
        {
            lock (_sync)
            {
                if (_retiring) throw new ObjectDisposedException(nameof(KnownCreateReview));
                _stages.BindOriginalOwner(this); _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                OriginalPreparation = PreparePublishedAsync(begin.Task, _stop.Token); begin.SetResult(); return OriginalPreparation;
            }
        }
        private async Task<ICloudflareOriginalKnownCreateReview> PreparePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await DemandCurrentAsync(token).ConfigureAwait(false);
            _existing = Single(await owner.StateAsync(_stages, token).ConfigureAwait(false), NamespaceRecordId(invocation.Service.AccountId, known.NamespaceId));
            DemandExisting(_existing);
            _arguments = JsonSerializer.SerializeToElement(new { invocation.OperationKey, invocation.TaskId, invocation.ExecutionId,
                originalActionId = invocation.ActionId, originalReview = known.Permission.OriginalReviewRequestId,
                accountId = invocation.Service.AccountId, namespaceId = known.NamespaceId, known.Title,
                originalConnectionId = invocation.Service.Connection.Id, expectedRevision = _existing?.Revision ?? 0,
                disposition = "known original SDK create response; local custody/audit recovery only; no remote create or canonical acceptance" });
            _review = owner._broker.PrepareReviewForActor(known.Capture.Actor, "cloudflare", ReconcileAction,
                [new(owner.ResourceKind, invocation.AccountResourceId, known.Capture.Record.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write)],
                _arguments, "Recover local custody for retained original namespace " + known.NamespaceId + ". No new Cloudflare effect, no canonical Task completion or replay.",
                null, "cloudflare:recover:" + invocation.OperationKey);
            return this;
        }
        private void DemandExisting(HomeCoreStateRecord? current)
        {
            if (current is null) return;
            var value = RecordShape(current, "home.cloudflare.namespace") ? current.Payload.Deserialize<Namespace>() : null;
            if (value is null || value.Deleted || value.ProfileId != known.Capture.Actor.ProfileId || value.ConnectionId != invocation.Service.Connection.Id ||
                value.AccountId != invocation.Service.AccountId || value.NamespaceId != known.NamespaceId || value.TaskId != invocation.TaskId ||
                value.ExecutionId != invocation.ExecutionId || value.CreationOperationKey != invocation.OperationKey || value.Title != known.Title)
                throw new UnauthorizedAccessException("Current namespace custody is different, deleted or unconfirmed; it cannot be replaced by this recovery.");
        }
        private async Task DemandCurrentAsync(CancellationToken token)
        {
            lock (_sync) if (_retiring) throw new ObjectDisposedException(nameof(KnownCreateReview));
            if (!ReferenceEquals(owner.RequireKnownCreate(invocation).Response, known.Response)) throw new UnauthorizedAccessException("Original known create response changed.");
            await _stages.AwaitAsync(_stages.Invoke(() => owner.RevalidateOriginalAsync(invocation.Service, token))).ConfigureAwait(false);
            if (await owner.ActorAsync(_stages, token).ConfigureAwait(false) != known.Capture.Actor) throw new UnauthorizedAccessException("Original Home actor changed before recovery.");
        }
        public Task<CloudflareNamespaceRecoveryObservation> SubmitOriginalAsync(CancellationToken token)
        {
            DemandExternalOriginalJoin(); return StartSubmitOriginalAsync(token);
        }
        private Task<CloudflareNamespaceRecoveryObservation> StartSubmitOriginalAsync(CancellationToken token)
        {
            lock (_sync)
            {
                if (_submit is not null) return _submit;
                if (_retiring) throw new ObjectDisposedException(nameof(KnownCreateReview));
                token.ThrowIfCancellationRequested();
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _submit = RunOriginalDriverAsync(begin.Task, SubmitBodyAsync, token); begin.SetResult(); return _submit;
            }
        }
        private async Task<CloudflareNamespaceRecoveryObservation> RunOriginalDriverAsync(Task begin,
            Func<CancellationToken, Task<CloudflareNamespaceRecoveryObservation>> originalBody, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            CancellationTokenSource? scope = null;
            return await CloudflareOriginalPartialEntryCustody.RunOriginalAsync(_stages, async () =>
            {
                var actualScope = _stages.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(_stop!.Token, token)); scope = actualScope;
                return await _stages.AwaitAsync(_stages.Invoke(() => originalBody(actualScope.Token))).ConfigureAwait(false);
            }, () =>
            {
                if (scope is not { } actualScope) return Array.Empty<Func<ValueTask>>();
                return new Func<ValueTask>[] { () => { actualScope.Dispose(); return ValueTask.CompletedTask; } };
            }).ConfigureAwait(false);
        }
        private async Task<CloudflareNamespaceRecoveryObservation> SubmitBodyAsync(CancellationToken token)
        {
            await _stages.AwaitAsync(OriginalPreparation).ConfigureAwait(false); await DemandCurrentAsync(token).ConfigureAwait(false);
            var observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(_review!, token))).ConfigureAwait(false);
            if (observed.State != HomePreparedReviewState.RequestObserved || observed.Request is null) throw new UnauthorizedAccessException(observed.Code);
            return new(invocation.OperationKey, known.NamespaceId, _existing?.Revision ?? 0, RequestId,
                observed.Request.State == HomePermissionRequestState.Approved ? "CF_KNOWN_CREATE_RECOVERY_APPROVED_NOT_SAVED" : "CF_KNOWN_CREATE_RECOVERY_APPROVAL_REQUIRED");
        }
        public Task<CloudflareNamespaceRecoveryObservation> CommitOriginalAsync(CancellationToken token)
        {
            DemandExternalOriginalJoin();
            lock (_sync)
            {
                if (_commit is not null) return _commit;
                if (_retiring) throw new ObjectDisposedException(nameof(KnownCreateReview));
                token.ThrowIfCancellationRequested();
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _commit = RunOriginalDriverAsync(begin.Task, CommitBodyAsync, token); begin.SetResult(); return _commit;
            }
        }
        private async Task<CloudflareNamespaceRecoveryObservation> CommitBodyAsync(CancellationToken token)
        {
            await _stages.AwaitAsync(StartSubmitOriginalAsync(token)).ConfigureAwait(false);
            var observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ObservePreparedReviewAsync(_review!, token))).ConfigureAwait(false);
            if (observed.Request?.State != HomePermissionRequestState.Approved) throw new CloudflareSetupRequiredException(CloudflareSetupStage.ApprovalRequired,
                "CF_KNOWN_CREATE_RECOVERY_APPROVAL_REQUIRED", "Accept the exact retained original create recovery in Home.", RequestId);
            await DemandCurrentAsync(token).ConfigureAwait(false);
            var capability = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(RequestId, _arguments, token))).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Known create recovery capability unavailable.");
            var claim = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ClaimExecutionObservedAsync(capability, "cloudflare", ReconcileAction, _review!.Scopes, _arguments, token))).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != known.Capture.Actor) throw new UnauthorizedAccessException("Known create recovery claim rejected.");
            var attestation = owner._broker.CaptureClaimedAttestation(capability) ?? throw new UnauthorizedAccessException("Individual Home Accept required for known create recovery.");
            // A post-write audit failure does not repeat this write: this whole commit is retained/coalesced.
            long revision = _existing?.Revision ?? 0;
            var recordId = NamespaceRecordId(invocation.Service.AccountId, known.NamespaceId);
            if (_existing is null)
            {
                var ns = new Namespace(known.Capture.Actor.ProfileId, invocation.Service.Connection.Id, invocation.Service.AccountId, known.NamespaceId,
                    invocation.TaskId, invocation.ExecutionId, invocation.OperationKey, known.Title, false);
                var record = new HomeCoreStateRecord(recordId, "home.cloudflare.namespace", 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
                    1, JsonSerializer.SerializeToElement(ns));
                var guard = new ClaimedStateGuard(owner, _stages, known.Capture.Actor, attestation, [known.Capture.Record], recordId, 0);
                var saved = await _stages.AwaitAsync(_stages.Invoke(() => owner._store.WriteGuardedAsync(record, 0, known.Capture.Actor, guard, token))).ConfigureAwait(false);
                if (!saved.IsSuccess) throw new InvalidOperationException("Known original remote response retained; local custody recovery CAS is unconfirmed.");
                revision = 1;
            }
            else
            {
                // Recheck the already acknowledged exact record beneath the genuine held Home gate.
                IAsyncDisposable? completion = null; IHomeLocalOperationLease? held = null; var errors = new List<Exception>();
                try
                {
                    completion = await _stages.AwaitAsync(_stages.Invoke(() => capability.AcquireCommitCompletionLeaseAsync(owner._broker, token))).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("Known recovery completion is closing.");
                    held = await _stages.AwaitAsync(_stages.Invoke(() => owner._store.AcquireLocalOperationLeaseCoreAsync(owner._profiles, known.Capture.Actor,
                        new ClaimedStateGuard(owner, _stages, known.Capture.Actor, attestation, [known.Capture.Record, _existing!]), body => owner.RunOriginalHostCallback(_stages, body), raw => owner.RetainOriginalHostTask(_stages, raw), token))).ConfigureAwait(false)
                        ?? throw new UnauthorizedAccessException("Original known namespace custody changed.");
                    if (!await _stages.AwaitAsync(_stages.Invoke(() => (held as IHomeOriginalScopedLocalOperationLease ?? throw new InvalidOperationException("Original scoped Home lease required.")).IsCurrentAsync(body => owner.RunOriginalHostCallback(_stages, body), raw => owner.RetainOriginalHostTask(_stages, raw), token))).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original known namespace custody is stale.");
                }
                catch (Exception error) { errors.Add(error); }
                finally
                {
                    if (held is not null) try { await _stages.AwaitAsync(_stages.Invoke(() => held.DisposeAsync())).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
                    if (completion is not null) try { await _stages.AwaitAsync(_stages.Invoke(() => completion.DisposeAsync())).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
                }
                if (errors.Count != 0) throw new AggregateException("Known recovery validation or independent cleanup failed.", errors);
            }
            var audited = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.CompleteExecutionAsync(capability,
                new(HomePermissionRequestState.Succeeded, "CF_KNOWN_CREATE_LOCAL_CUSTODY_RECONCILED", "Recovered exact retained original namespace custody. Canonical action and earlier failures remain unresolved; no remote create replayed.", []), CancellationToken.None))).ConfigureAwait(false);
            if (!audited.Succeeded) throw new InvalidOperationException("Known namespace custody is saved but recovery audit is unconfirmed: " + audited.Code);
            return new(invocation.OperationKey, known.NamespaceId, revision, RequestId, "CF_KNOWN_CREATE_LOCAL_CUSTODY_RECONCILED_CANONICAL_ACTION_UNRESOLVED");
        }
    }
    private sealed partial class Permission
    {
        internal string OriginalReviewRequestId => _review?.RequestId ?? throw new InvalidOperationException("No original Home create review.");
        internal KnownCreate ReadKnownCreateOriginal()
        {
            lock (_sync)
            {
                if (Binding.Invocation.Descriptor.Kind != CloudflareOperationKind.KvCreate || _acquire?.IsCompletedSuccessfully != true ||
                    _entry?.OriginalClose?.IsCompletedSuccessfully != true || _outcome is null || _outcome.OriginalErrors.Count != 0 ||
                    _outcome.Response is null || !_outcome.DispatchStarted || !owner._mcp.IsIssuedOriginalResult(Binding.Invocation, _outcome) || _capture is null)
                    throw new CloudflareSetupRequiredException(CloudflareSetupStage.NamespaceRecoveryRequired, "CF_CREATE_RESPONSE_UNCONFIRMED_NO_OWNERSHIP",
                        "A missing/uncertain original remote response or cleanup cannot issue namespace ownership. Inspect diagnosis; do not replay or adopt a matching title.");
                var actual = CloudflareTypedToolCatalogue.DemandBoundedResponse(Binding.Invocation, _outcome.Response.Value);
                return new(this, _capture, _outcome, actual.GetProperty("id").GetString()!, actual.GetProperty("title").GetString()!);
            }
        }
    }
}
