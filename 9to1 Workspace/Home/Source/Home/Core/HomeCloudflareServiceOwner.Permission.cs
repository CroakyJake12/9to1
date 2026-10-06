using System.Globalization;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

public sealed partial class HomeCloudflareServiceOwner
{
    private sealed partial class Permission(HomeCloudflareServiceOwner owner, CloudflareOriginalTaskBinding binding) : ICloudflareOriginalPermission, ICloudflareOriginalBorrowedBindingPermission
    {
        internal CloudflareOriginalTaskBinding Binding { get; } = binding;
        private readonly object _sync = new();
        private readonly CloudflareOriginalTaskLedger _stages = new();
        private Task<ICloudflareOriginalPermission>? _acquire;
        private Task? _close;
        private Capture? _capture;
        private HomeCoreStateRecord? _namespace;
        private HomeResourcePreparedReview? _review;
        private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation;
        private Entry? _entry;
        private CloudflareMcpDispatchResult? _outcome;
        private Task? _audit;
        private bool _sealed;
        internal bool IsHealthyClosed { get { lock (_sync) return _acquire?.IsCompletedSuccessfully == true && _audit?.IsCompletedSuccessfully == true &&
            _close?.IsCompletedSuccessfully == true && _outcome?.DispatchStarted == true && _outcome.OriginalErrors.Count == 0 && _stages.OriginalErrors.Count == 0 && _stages.OriginalTasks.All(x => x.IsCompletedSuccessfully); } }
        private CancellationTokenSource? _stop;
        internal Task<ICloudflareOriginalPermission> AcquireAsync(CancellationToken token)
        {
            lock (_sync)
            {
                if (_acquire is not null) return _acquire;
                if (_sealed) throw new ObjectDisposedException(nameof(Permission));
                _stages.BindOriginalOwner(this); _stages.BindOriginalCallerCallback(Binding.OriginalCallerCallback);
                _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _acquire = AcquirePublishedAsync(begin.Task, _stop.Token); begin.SetResult(); return _acquire;
            }
        }
        private async Task<ICloudflareOriginalPermission> AcquirePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await _stages.AwaitAsync(_stages.Invoke(() => owner._actions.ValidateOriginalActionAdmissionAsync(Binding.ActionAdmission, Binding.OriginalPreparation, Binding.OriginalAttempt, token))).ConfigureAwait(false);
            await _stages.AwaitAsync(_stages.Invoke(() => owner.RevalidateCallerScopedOriginalAsync(Binding.Invocation.Service, Binding.OriginalCallerCallback, token))).ConfigureAwait(false);
            if (!owner._services.TryGetValue(Binding.Invocation.Service, out _capture)) throw new UnauthorizedAccessException("SAME configured Home service required.");
            _namespace = await owner.NamespaceRecordAsync(Binding.Invocation, _stages, token).ConfigureAwait(false);
            if (_namespace is { RecordType: "home.cloudflare.namespace" } && _namespace.Payload.Deserialize<Namespace>() is { } ns && ns.ProfileId != _capture.Actor.ProfileId)
                throw new UnauthorizedAccessException("Task namespace belongs to another Home profile.");
            var invocation = Binding.Invocation;
            var access = invocation.Descriptor.IsReadOnly ? ResourceAccess.Read : ResourceAccess.Execute;
            var scopes = new List<ResourceScope> { new(owner.ResourceKind, invocation.AccountResourceId,
                _capture.Record.Revision.ToString(CultureInfo.InvariantCulture), access) };
            if (_namespace is not null) scopes.Add(new(owner.ResourceKind, invocation.NamespaceResourceId!, _namespace.Revision.ToString(CultureInfo.InvariantCulture), access));
            if (_namespace is not null && IsBorrowedMarker(invocation, _namespace))
            {
                var borrowed = _namespace.Payload.Deserialize<BorrowedNamespace>()!;
                scopes.Add(new(owner.ResourceKind, WorkerScopeId(borrowed.AccountId, borrowed.Selection.WorkerName, borrowed.Selection.BindingName),
                    _namespace.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read));
            }
            var args = Arguments();
            _review = owner._broker.PrepareReviewForActor(_capture.Actor, "cloudflare", invocation.Descriptor.ActionId, scopes, args,
                invocation.Descriptor.ActionId + " on account " + invocation.Service.AccountId + (invocation.NamespaceId is null ? "" : ", isolated namespace " + invocation.NamespaceId) +
                ". Task " + invocation.TaskId + ", run " + invocation.ExecutionId + ". No DNS, Workers, Access, secrets or billing changes.", null, "cloudflare:task:" + invocation.TaskId.ToString("N") + ":" + invocation.ExecutionId.ToString("N"));
            var observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(_review, token))).ConfigureAwait(false);
            // Retain this SAME finite tool original while Home's existing approval UI owns the decision.
            // No completed status, request ID or prose can manufacture the capability below.
            var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
            while (observed.State == HomePreparedReviewState.RequestObserved && observed.Request?.State == HomePermissionRequestState.PendingApproval)
            {
                if (DateTimeOffset.UtcNow >= deadline) throw new CloudflareSetupRequiredException(CloudflareSetupStage.ApprovalRequired, "CF_ACTION_APPROVAL_REQUIRED", "Review the exact action in Home; no Cloudflare dispatch occurred.", _review.RequestId);
                await _stages.AwaitAsync(Task.Delay(TimeSpan.FromSeconds(1), token)).ConfigureAwait(false);
                await _stages.AwaitAsync(_stages.Invoke(() => owner.RevalidateCallerScopedOriginalAsync(invocation.Service, Binding.OriginalCallerCallback, token))).ConfigureAwait(false);
                await _stages.AwaitAsync(_stages.Invoke(() => Binding.OriginalAttempt.Lease.RevalidateAsync(token))).ConfigureAwait(false);
                observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ObservePreparedReviewAsync(_review, token))).ConfigureAwait(false);
            }
            if (observed.State != HomePreparedReviewState.RequestObserved || observed.Request?.State != HomePermissionRequestState.Approved)
                throw new UnauthorizedAccessException("The exact Home Cloudflare action was not approved.");
            _capability = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(_review.RequestId, args, token))).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Home Cloudflare action capability unavailable.");
            var claim = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ClaimExecutionObservedAsync(_capability, "cloudflare", invocation.Descriptor.ActionId, scopes, args, token))).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != _capture.Actor) throw new UnauthorizedAccessException("Original Home Cloudflare claim rejected.");
            _attestation = owner._broker.CaptureClaimedAttestation(_capability)
                ?? throw new UnauthorizedAccessException("Individual Home Accept is required; generic trust grants have no final Cloudflare fence.");
            return this;
        }
        private JsonElement Arguments() => JsonSerializer.SerializeToElement(new
        {
            Binding.Invocation.TaskId, Binding.Invocation.ExecutionId, Binding.Invocation.ActionId,
            Binding.Invocation.OperationKey, Binding.Invocation.CallDigest,
            accountId = Binding.Invocation.Service.AccountId, namespaceId = Binding.Invocation.NamespaceId,
            connectionId = Binding.Invocation.Service.Connection.Id, credentialReference = Binding.Invocation.Service.CredentialReference,
            operation = Binding.Invocation.Descriptor.ActionId
        });
        private void DemandPair(CloudflareOriginalTaskBinding actual)
        {
            lock (_sync)
                if (_sealed || !ReferenceEquals(actual.Invocation, Binding.Invocation) || !ReferenceEquals(actual.OriginalPreparation, Binding.OriginalPreparation) ||
                    _acquire?.IsCompletedSuccessfully != true || _capability is null || _attestation is null || !_capability.IsUncompletedClaim(owner._broker))
                    throw new UnauthorizedAccessException("Actual current original Home Cloudflare permission required.");
        }
        public async ValueTask RevalidateOriginalAsync(CloudflareOriginalTaskBinding actual, CancellationToken token)
        {
            DemandPair(actual);
            await _stages.AwaitAsync(_stages.Invoke(() => owner.RevalidateCallerScopedOriginalAsync(Binding.Invocation.Service, Binding.OriginalCallerCallback, token))).ConfigureAwait(false);
            var ns = await owner.NamespaceRecordAsync(Binding.Invocation, _stages, token).ConfigureAwait(false);
            if ((_namespace is null) != (ns is null) || _namespace is not null && Fingerprint(_namespace) != Fingerprint(ns)) throw new UnauthorizedAccessException("Original Task namespace changed.");
            if (await owner.ActorAsync(_stages, token).ConfigureAwait(false) != _capture!.Actor) throw new UnauthorizedAccessException("Home actor changed.");
            var current = await _stages.AwaitAsync(_stages.Invoke(() => owner._permissions.IsExecutionCurrentAsync(_capability!.RequestId, token))).ConfigureAwait(false);
            if (!current) throw new UnauthorizedAccessException("Home revoked the original action.");
            DemandPair(actual);
        }
        public async Task RevalidateOriginalBorrowedBindingAsync(CloudflareOriginalTaskBinding actual, ICloudflareWorkerBindingClient sdk, CancellationToken token)
        {
            DemandPair(actual);
            if (_namespace is null || !IsBorrowedMarker(Binding.Invocation, _namespace)) return;
            if (!ReferenceEquals(sdk, owner._mcp)) throw new UnauthorizedAccessException("SAME configured actual SDK binding reader required.");
            var borrowed = _namespace.Payload.Deserialize<BorrowedNamespace>()!;
            var stages = new CloudflareOriginalTaskLedger(); stages.BindOriginalOwner(this); stages.BindOriginalCallerCallback(Binding.OriginalCallerCallback);
            try
            {
                var read = new WorkerReadAdmission(owner, Binding.Invocation.Service, borrowed.Selection, _capture!, _capability!, _attestation!,
                    [_capture!.Record, _namespace], stages, Binding, () => DemandPair(Binding));
                var observation = await stages.AwaitAsync(stages.Invoke(() => sdk.ReadOriginalWorkerBindingAsync(Binding.Invocation.Service,
                    borrowed.Selection, read, finite => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                    { if (Binding.OriginalCallerCallback is { } caller) caller(finite); else finite(); return true; }), token))).ConfigureAwait(false);
                foreach (var raw in observation.OriginalTasks) _ = stages.Track(raw);
                foreach (var cause in observation.OriginalErrors) stages.Retain(cause);
                if (observation.OriginalErrors.Count != 0) throw new AggregateException("The actual original live binding read/cleanup failed.", observation.OriginalErrors);
                if (!sdk.IsIssuedOriginalWorkerBinding(Binding.Invocation.Service, borrowed.Selection, observation) ||
                    observation.NamespaceId != borrowed.NamespaceId || Fingerprint(observation.OriginalProjection) != borrowed.OriginalProjectionDigest)
                    throw new UnauthorizedAccessException("The SAME live staging Worker KV binding changed; no marker dispatch.");
                await stages.AwaitAsync(stages.Invoke(() => RevalidateOriginalAsync(Binding, token))).ConfigureAwait(false);
            }
            finally
            {
                await stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var task in stages.OriginalTasks) _ = _stages.Track(task);
                foreach (var cause in stages.OriginalErrors) _stages.Retain(cause);
            }
        }
        public async ValueTask<ICloudflareOriginalFinalDispatch> EnterOriginalFinalDispatchAsync(CloudflareCompiledInvocation invocation, CancellationToken token)
        {
            DemandPair(Binding);
            lock (_sync) { if (!ReferenceEquals(invocation, Binding.Invocation) || _entry is not null) throw new UnauthorizedAccessException("Original final dispatch is one-use."); }
            IAsyncDisposable? completion = null; IHomeLocalOperationLease? home = null;
            return await CloudflareOriginalPartialEntryCustody.RunOriginalAsync(_stages, async () =>
            {
                completion = await _stages.CaptureOriginalAcquisitionAsync(() => _capability!.AcquireCommitCompletionLeaseAsync(owner._broker, token), actual => completion = actual).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Original Home completion is closing.");
                var records = _namespace is null ? new[] { _capture!.Record } : new[] { _capture!.Record, _namespace! };
                home = await _stages.CaptureOriginalAcquisitionAsync(() => owner._store.AcquireLocalOperationLeaseCoreAsync(owner._profiles, _capture!.Actor,
                    new ClaimedStateGuard(owner, _capture.Actor, _attestation!, records), token), actual => home = actual).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Original Home held account/namespace entry rejected.");
                if (!await _stages.AwaitAsync(_stages.Invoke(() => home.IsCurrentAsync(token))).ConfigureAwait(false)) throw new UnauthorizedAccessException("Original held Home entry is stale.");
                var entry = new Entry(this, home, completion);
                lock (_sync) { DemandPair(Binding); if (_entry is not null) throw new UnauthorizedAccessException("Repeated final dispatch."); _entry = entry; }
                home = null; completion = null; return entry;
            }, () =>
            {
                var closes = new List<Func<ValueTask>>();
                if (home is { } actualHome) closes.Add(actualHome.DisposeAsync);
                if (completion is { } actualCompletion) closes.Add(actualCompletion.DisposeAsync);
                return closes;
            }).ConfigureAwait(false);
        }
        public void DemandOriginalFinalDispatch(ICloudflareOriginalFinalDispatch actual, CloudflareCompiledInvocation invocation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); DemandPair(Binding);
            lock (_sync) if (actual is not Entry entry || !ReferenceEquals(entry, _entry) || !ReferenceEquals(invocation, Binding.Invocation) || entry.IsClosed)
                throw new UnauthorizedAccessException("SAME held original Home dispatch entry required.");
        }
        public ValueTask RecordOriginalOutcomeAsync(CloudflareOriginalTaskBinding actual, CloudflareMcpDispatchResult outcome, CancellationToken token)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(actual.Invocation, Binding.Invocation) || !ReferenceEquals(actual.OriginalPreparation, Binding.OriginalPreparation) || !owner._mcp.IsIssuedOriginalResult(Binding.Invocation, outcome))
                    throw new UnauthorizedAccessException("SAME actual typed SDK outcome required.");
                if (_entry is not null && _entry.OriginalClose?.IsCompletedSuccessfully != true) throw new InvalidOperationException("Join the actual held entry closes before Home audit.");
                if (_outcome is not null && !ReferenceEquals(_outcome, outcome)) throw new UnauthorizedAccessException("Original Cloudflare outcome cannot be replaced.");
                _outcome = outcome;
                if (_audit is null) { var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _audit = RecordPublishedAsync(begin.Task, token); begin.SetResult(); }
                return new(_audit);
            }
        }
        private async Task RecordPublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); var invocation = Binding.Invocation; var outcome = _outcome!;
            var confirmed = outcome.DispatchStarted && outcome.Response is not null && outcome.OriginalErrors.Count == 0;
            JsonElement data = default;
            Exception? responseFailure = null;
            if (confirmed) try { data = CloudflareTypedToolCatalogue.DemandBoundedResponse(invocation, outcome.Response!.Value); }
                catch (Exception error) { _stages.Retain(error); responseFailure = error; confirmed = false; }
            if (confirmed && invocation.Descriptor.Kind == CloudflareOperationKind.KvCreate)
            {
                // The maintained official transport retries non-idempotent methods on
                // network errors/429. A response proves custody of its returned ID only,
                // never absence of another effect from an internal retry. No receipt or
                // implicit namespace grant is issued; explicit known-response recovery
                // can later save this exact ID without clearing the canonical uncertainty.
                responseFailure = new CloudflareSetupRequiredException(CloudflareSetupStage.NamespaceRecoveryRequired,
                    "CF_CREATE_RETRYING_TRANSPORT_UNCERTAIN", "The original returned namespace is known; all create effects are not confirmed. Review its retained response, never replay or adopt by title.", _review!.RequestId);
                _stages.Retain(responseFailure); confirmed = false;
            }
            if (confirmed && invocation.Descriptor.Kind == CloudflareOperationKind.KvDelete)
            {
                var nsId = invocation.Descriptor.Kind == CloudflareOperationKind.KvCreate ? data.GetProperty("id").GetString()! : invocation.NamespaceId!;
                var recordId = NamespaceRecordId(invocation.Service.AccountId, nsId);
                var old = invocation.Descriptor.Kind == CloudflareOperationKind.KvCreate ? null : _namespace;
                var ns = new Namespace(_capture!.Actor.ProfileId, invocation.Service.Connection.Id, invocation.Service.AccountId, nsId,
                    invocation.TaskId, invocation.ExecutionId, invocation.Descriptor.Kind == CloudflareOperationKind.KvCreate ? invocation.OperationKey : old!.Payload.Deserialize<Namespace>()!.CreationOperationKey,
                    invocation.Descriptor.Kind == CloudflareOperationKind.KvCreate ? data.GetProperty("title").GetString()! : old!.Payload.Deserialize<Namespace>()!.Title,
                    invocation.Descriptor.Kind == CloudflareOperationKind.KvDelete);
                var record = new HomeCoreStateRecord(recordId, "home.cloudflare.namespace", 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
                    checked((old?.Revision ?? 0) + 1), JsonSerializer.SerializeToElement(ns));
                var guard = new ClaimedStateGuard(owner, _capture.Actor, _attestation!, old is null ? [_capture.Record] : [_capture.Record, old!], recordId, old?.Revision ?? 0);
                var write = await _stages.AwaitAsync(_stages.Invoke(() => owner._store.WriteGuardedAsync(record, old?.Revision ?? 0, _capture.Actor, guard, token))).ConfigureAwait(false);
                if (!write.IsSuccess) throw new InvalidOperationException("Remote response is known but isolated namespace custody save is unconfirmed; reconcile before any effect.");
            }
            var terminal = confirmed ? HomePermissionRequestState.Succeeded : outcome.DispatchStarted ? HomePermissionRequestState.PartiallyCompleted : HomePermissionRequestState.Failed;
            var result = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.CompleteExecutionAsync(_capability!,
                new(terminal, confirmed ? "CF_ORIGINAL_CONFIRMED" : "CF_ORIGINAL_UNCERTAIN", confirmed ? "Observed the exact typed service response." : "Remote effect is unconfirmed; retained original requires reconciliation, never replay.", []), token))).ConfigureAwait(false);
            if (!result.Succeeded) throw new InvalidOperationException("Known remote outcome retained; Home audit unfinished: " + result.Code);
            if (responseFailure is not null) throw new AggregateException("The original response is unconfirmed; its Home partial outcome was audited.", responseFailure);
        }
        public ValueTask DisposeAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            lock (_sync)
            {
                if (_close is not null) return new(_close); _sealed = true;
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublishedAsync(begin.Task); begin.SetResult(); return new(_close);
            }
        }
        private async Task ClosePublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); var errors = new List<Exception>();
            try { _stages.Invoke(() => { _stop?.Cancel(); return true; }); } catch (Exception error) { errors.Add(error); }
            if (_acquire is not null) try { await _stages.AwaitAsync(_acquire).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            if (_entry is not null) try { await _stages.ObserveOriginalCloseAsync(_entry.DisposeAsync).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            if (_audit is not null) try { await _stages.AwaitAsync(_audit).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            try { _stages.Invoke(() => { _stop?.Dispose(); return true; }); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Original Cloudflare Home close failed.", errors);
        }
        private sealed class Entry(Permission permission, IHomeLocalOperationLease home, IAsyncDisposable completion) : ICloudflareOriginalFinalDispatch
        {
            private readonly object _sync = new(); private Task? _close; private bool _invoked;
            internal Task? OriginalClose { get { lock (_sync) return _close; } }
            internal bool IsClosed { get { lock (_sync) return _close is not null; } }
            public T RunOriginalDispatch<T>(CloudflareCompiledInvocation invocation, Func<T> body, CancellationToken token)
            {
                lock (permission._sync) lock (_sync)
                {
                    permission.DemandOriginalFinalDispatch(this, invocation, token);
                    if (_invoked) throw new UnauthorizedAccessException("Original dispatch already started."); _invoked = true;
                    return CloudflareOriginalExecutionGuard.InvokeOriginal(permission, () => CloudflareOriginalExecutionGuard.InvokeOriginal(this, body)); // Finite actual SDK Task creation only; no network await beneath the held Home store.
                }
            }
            public ValueTask DisposeAsync()
            {
                CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
                lock (_sync)
                {
                    if (_close is not null) return new(_close);
                    var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublishedAsync(begin.Task); begin.SetResult(); return new(_close);
                }
            }
            private async Task ClosePublishedAsync(Task begin)
            {
                await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); var errors = new List<Exception>();
                try { await permission._stages.ObserveOriginalCloseAsync(home.DisposeAsync).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
                try { await permission._stages.ObserveOriginalCloseAsync(completion.DisposeAsync).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
                if (errors.Count != 0) throw new AggregateException("Independent actual Home entry/completion closes failed.", errors);
            }
        }
    }
}
