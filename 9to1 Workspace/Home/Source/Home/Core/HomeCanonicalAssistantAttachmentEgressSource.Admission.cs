using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;
public sealed partial class HomeCanonicalAssistantAttachmentEgressSource
{
    private sealed partial class Read(HomeCanonicalAssistantAttachmentEgressSource owner, ICanonicalAttachmentEgressIntent selection)
        : ICanonicalAttachmentHomeEgressLease
    {
        internal HomeCanonicalAssistantAttachmentEgressSource Owner => owner;
        public ICanonicalAttachmentEgressIntent OriginalIntent => selection;
        private sealed record EgressSnapshot(Guid RequestId, TaskExecutionOwnerBinding TaskOwner, Guid AttemptId,
            string ProviderId, string ModelId, string PayloadSha256, ChatOriginalAttachmentLineage Lineage, string Digest);
        public string OriginalApprovalRequestId { get { lock (_sync) return _review?.RequestId ?? ""; } }
        internal Task<ICanonicalAttachmentHomeEgressLease> Acquisition = null!;
        private readonly object _sync = new();
        private EgressContext _sources = null!;
        private readonly List<Task> _operations = [];
        private readonly List<EgressContext> _contexts = [];
        private AuthenticatedResourceActor? _actor;
        private EgressSnapshot? _file;
        private ResourceScope? _scope;
        private JsonElement _arguments;
        private HomeResourcePreparedReview? _review;
        private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation;
        private Exception? _declined;
        private bool _refusalSettled, _capabilityStarting;
        private Task? _close;
        private Task<bool>? _withdrawal;
        private EgressContext? _withdrawalSources;
        private readonly TaskCompletionSource<Task<HomePreparedReviewObservation>?> _prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<HomePreparedReviewObservation>? _preparedTask;
        internal bool IsAdmitted { get { lock (_sync) return owner.HasHealthyOriginalCallbacks && _close is null && Acquisition.IsCompletedSuccessfully && _capability is not null && _attestation is not null; } }
        internal bool Matches(AuthenticatedResourceActor actor, ResourceScope scope)
        { lock (_sync) return (_close is null || !Acquisition.IsCompleted) && _actor == actor && _scope == scope; }
        private EgressContext Context(Action<Action> scope, Action<Task> retain, bool productive = true) =>
            new(owner, this, scope, retain, productive);
        internal TaskCompletionSource Prepare(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            _sources = Context(scope, retain); _contexts.Add(_sources);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Acquisition = Acquire(start.Task, token); return start;
        }
        internal void Publish(Action<Task> retain)
        { try { _sources.Run(() => retain(Acquisition)); } catch { /* accepted driver owns every publication cause */ } }
        internal bool IsKnownRefusal(Task actual)
        {
            lock (_sync) return ReferenceEquals(Acquisition, actual) && _refusalSettled && _declined is not null &&
                _sources.Errors.Length == 0 && actual.Exception is { InnerExceptions.Count: 1 } group &&
                ReferenceEquals(group.InnerExceptions[0], _declined);
        }
        private async Task<ICanonicalAttachmentHomeEgressLease> Acquire(Task start, CancellationToken callerToken)
        {
            await start.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            // The process owns an admitted review. Caller cancellation can refuse only
            // before preparation; pending withdrawal is explicit and issuer-bound.
            var token = CancellationToken.None;
            try
            {
                _sources.Run(callerToken.ThrowIfCancellationRequested);
                await ValidateSelection(_sources, token).ConfigureAwait(false);
                _sources.Run(() =>
                {
                    _arguments = JsonSerializer.SerializeToElement(new { selection = _file, actor = _actor, purpose = "Send selected attachment content to this Task model" });
                    _scope = new(owner.ResourceKind, _file!.TaskOwner.TaskId.ToString("D") + ":" + _file.RequestId.ToString("D"),
                        _file.Digest, ResourceAccess.Write);
                    _review = owner._broker.PrepareReviewForActor(_actor!, "assistants", DiscloseAction, [_scope], _arguments,
                        "Allow this Task to send the selected attachment content to " + _file.ProviderId + " / " + _file.ModelId + ". This review applies only to this request.", null,
                        "assistant-attachment-disclose:" + Guid.NewGuid().ToString("N"));
                });
                HomePreparedReviewObservation observed;
                try
                {
                    observed = await _sources.ReadAsync(() =>
                    {
                        _preparedTask = owner._broker.AuthorizePreparedReviewWithinOriginalSourceAsync(_review!, _sources.Run, _sources.Retain, token, originalPermissionSources: true);
                        _prepared.TrySetResult(_preparedTask); return _preparedTask;
                    }).ConfigureAwait(false);
                }
                finally { _prepared.TrySetResult(null); }
                for (var poll = 0; observed.Request?.State == HomePermissionRequestState.PendingApproval && poll < 300; poll++)
                {
                    await DemandNotWithdrawn().ConfigureAwait(false);
                    await _sources.ReadAsync(async () => { await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
                    await DemandNotWithdrawn().ConfigureAwait(false);
                    observed = await _sources.ReadAsync(() => owner._broker.ObservePreparedReviewWithinOriginalSourceAsync(_review!, _sources.Run, _sources.Retain, token, originalPermissionSources: true)).ConfigureAwait(false);
                }
                await DemandNotWithdrawn().ConfigureAwait(false);
                if (observed.State == HomePreparedReviewState.RequestObserved && observed.Request?.RequestId == _review!.RequestId &&
                    observed.Request.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked)
                {
                    var retired = await _sources.ReadAsync(() => owner._broker.RetireSetupPreparedReviewWithinOriginalSourceAsync(_review!, _sources.Run, _sources.Retain, token)).ConfigureAwait(false);
                    if (!retired) throw new InvalidOperationException("The exact declined disclosure review did not retire.");
                    _declined = new UnauthorizedAccessException("Sending the selected attachments to this model was declined in Home."); throw _declined;
                }
                if (observed.Request?.State != HomePermissionRequestState.Approved)
                    throw new UnauthorizedAccessException("This attachment requires its individual Home disclosure approval.");
                await ValidateSelection(_sources, token).ConfigureAwait(false);
                await DemandNotWithdrawn().ConfigureAwait(false);
                Task<bool>? withdrawal;
                lock (_sync) { _capabilityStarting = true; withdrawal = _withdrawal; }
                if (withdrawal is not null) await DemandNotWithdrawn().ConfigureAwait(false);
                Task<HomeResourceExecutionCapability?>? rawCapability = null;
                try
                {
                    _capability = await _sources.ReadAsync(() => rawCapability = owner._broker.BeginExecutionCapabilityWithinOriginalSourceAsync(
                        _review!.RequestId, _arguments, _sources.Run, _sources.Retain, body => body(), token, originalPermissionSources: true)).ConfigureAwait(false);
                }
                finally
                {
                    // A late actual result after a caller postguard failure is retained
                    // for cleanup only; it cannot turn the failed acquisition into access.
                    if (rawCapability?.IsCompletedSuccessfully == true) _capability ??= rawCapability.GetAwaiter().GetResult();
                }
                if (_capability is null) throw new UnauthorizedAccessException("The exact approved attachment disclosure capability is unavailable.");
                var claim = await _sources.ReadAsync(() => owner._broker.ClaimExecutionWithinOriginalSourceAsync(_capability,
                    "assistants", DiscloseAction, [_scope!], _arguments, _sources.Run, _sources.Retain, body => body(), token, originalPermissionSources: true)).ConfigureAwait(false);
                if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != _actor)
                    throw new UnauthorizedAccessException("The actual attachment disclosure claim was refused.");
                _sources.Run(() => _attestation = owner._broker.CaptureClaimedAttestation(_capability));
                if (_attestation is null) throw new UnauthorizedAccessException("The exact manual disclosure attestation is unavailable.");
                await ValidateSelection(_sources, token).ConfigureAwait(false); owner.DemandHealthyOriginalCallbacks(); return this;
            }
            catch (Exception cause)
            {
                if (ReferenceEquals(cause, _declined) && _sources.Errors.Length == 0) _refusalSettled = true;
                throw;
            }
            finally { _prepared.TrySetResult(null); _sources.CloseProductive(); }
        }
        private async Task ValidateSelection(EgressContext source, CancellationToken token)
        {
            source.Run(() =>
            {
                if (!owner._producer.IsIssuedOriginalEgressIntent(selection)) throw new UnauthorizedAccessException("The SAME original attachment producer must issue this disclosure intent.");
                var actor = selection.OriginalHomeActor;
                var lineage = selection.OriginalLineage;
                var file = new EgressSnapshot(selection.OriginalRequestId, selection.OriginalTaskOwner, selection.AttemptId,
                    selection.ProviderId, selection.ModelId, selection.PayloadSha256,
                    lineage with { AttachmentIds = Array.AsReadOnly(lineage.AttachmentIds.ToArray()) },
                    owner._producer.GetOriginalEgressIntentDigest(selection));
                if (_actor is not null && (_actor != actor || _file?.Digest != file.Digest))
                    throw new UnauthorizedAccessException("The source-issued attachment disclosure changed.");
                _actor ??= actor; _file ??= file;
                if (file.RequestId == Guid.Empty || file.TaskOwner.TaskId == Guid.Empty || file.TaskOwner.ExecutionId == Guid.Empty ||
                    file.AttemptId == Guid.Empty || file.Lineage.Schema != 1 || file.Lineage.ConversationId != file.TaskOwner.ContextId ||
                    file.Lineage.AttachmentIds.Count is < 1 or > 64 || string.IsNullOrWhiteSpace(file.ProviderId) ||
                    string.IsNullOrWhiteSpace(file.ModelId) || file.PayloadSha256.Length != 64 || file.Digest.Length != 64)
                    throw new UnauthorizedAccessException("The actual Task/request/model/attachment disclosure is incomplete.");
            });
            var current = await source.ReadAsync(() => owner._profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            if (current != _actor) throw new UnauthorizedAccessException("The Home profile changed before attachment access.");
            await ReadVoid(source, () => owner._producer.ValidateOriginalEgressIntentWithinSourceAsync(selection, source.Run, source.Retain, token)).ConfigureAwait(false);
            current = await source.ReadAsync(() => owner._profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            source.Run(() => { if (current != _actor || !owner._producer.IsIssuedOriginalEgressIntent(selection) ||
                selection.OriginalHomeActor != _actor || owner._producer.GetOriginalEgressIntentDigest(selection) != _file!.Digest) throw new UnauthorizedAccessException("The original attachment source changed during review."); });
        }
        private static Task<bool> ReadVoid(EgressContext source, Func<Task> factory) => source.ReadAsync(() =>
        {
            var raw = factory() ?? throw new InvalidOperationException("The actual attachment source returned no Task.");
            source.Retain(raw); return Join(raw);
            static async Task<bool> Join(Task original) { await original.ConfigureAwait(false); return true; }
        });
        internal Task<ResourceAccessDecision> Evaluate(AuthenticatedResourceActor actor, ResourceScope scope,
            Action<Action> callback, Action<Task> retain, CancellationToken token) => Run(callback, retain, async source =>
        {
            await ValidateSelection(source, token).ConfigureAwait(false);
            return new ResourceAccessDecision(Matches(actor, scope), "ATTACHMENT_PRIVATE_SELECTED_DISCLOSURE", actor.ActorId, scope.Revision, actor.OrganisationId);
        }, requireAdmitted: false);
        public Task ValidateOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) => Run(scope, retain, async source =>
        {
            await ValidateSelection(source, token).ConfigureAwait(false);
            if (!await source.ReadAsync(() => owner._permissions.IsSetupExecutionCurrentWithinOriginalSourceAsync(_capability!.RequestId, source.Run, source.Retain, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("This individual attachment disclosure was revoked.");
            await ValidateSelection(source, token).ConfigureAwait(false); return true;
        }, requireAdmitted: true);
        private IHomeLocalOperationLease? _home;
        private IAsyncDisposable? _completion;
        private Task? _entry, _entryRelease;
        private bool _invoked;
        public Task AcquireOriginalInvocationEntryWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            DemandExternalOriginalJoin(); owner.DemandHealthyOriginalCallbacks();
            lock (_sync)
            {
                if (_invoked) throw new UnauthorizedAccessException("The finite disclosure was already started.");
                if (_entry is not null) return _entry;
                if (_entryRelease is not null && !_entryRelease.IsCompletedSuccessfully)
                    throw new InvalidOperationException("The preceding actual Home entry release is unresolved.");
                _entryRelease?.GetAwaiter().GetResult(); _entryRelease = null;
                return _entry = Run(scope, retain, async source =>
                {
                    await ValidateSelection(source, token).ConfigureAwait(false);
                    Task<IAsyncDisposable?>? completion = null;
                    try { _completion = await source.ReadAsync(() => completion = _capability!.AcquireCommitCompletionLeaseAsync(owner._broker, token, new(source.Run, source.Retain)).AsTask()).ConfigureAwait(false); }
                    finally { if (completion?.IsCompletedSuccessfully == true) _completion ??= completion.GetAwaiter().GetResult(); }
                    if (_completion is null) throw new UnauthorizedAccessException("The exact disclosure completion pin is unavailable.");
                    Task<IHomeLocalOperationLease?>? home = null;
                    try { _home = await source.ReadAsync(() => home = owner._store.AcquireLocalOperationLeaseCoreAsync(owner._profiles,
                        _actor!, new HeldDisclosureGuard(this), source.Run, source.Retain, token).AsTask()).ConfigureAwait(false); }
                    finally { if (home?.IsCompletedSuccessfully == true) _home ??= home.GetAwaiter().GetResult(); }
                    if (_home is not IHomeOriginalScopedLocalOperationLease held ||
                        !await source.ReadAsync(() => held.IsCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The held Home disclosure approval changed before dispatch.");
                    return true;
                }, requireAdmitted: true);
            }
        }
        public T RunOriginalInvocation<T>(Func<T> originalRawStart)
        {
            ArgumentNullException.ThrowIfNull(originalRawStart);
            var source = Context(body => body(), _ => { }); T result = default!;
            lock (_sync) _contexts.Add(source);
            try
            {
                source.Run(() =>
                {
                    lock (_sync)
                    {
                        if (!IsAdmitted || _entry?.IsCompletedSuccessfully != true || _home is null || _completion is null ||
                            _invoked || _contexts.Any(value => value.Errors.Length != 0))
                            throw new UnauthorizedAccessException("The exact current one-use Home disclosure entry is required.");
                        if (!owner._producer.IsIssuedOriginalEgressIntent(selection) ||
                            owner._producer.GetOriginalEgressIntentDigest(selection) != _file!.Digest)
                            throw new UnauthorizedAccessException("The admitted disclosure intent changed.");
                        _invoked = true;
                        result = originalRawStart();
                    }
                });
                owner.DemandHealthyOriginalCallbacks(); return result;
            }
            finally { source.CloseProductive(); _ = BeginEntryRelease(body => body(), _ => { }); }
        }
        public Task ReleaseOriginalInvocationEntryWithinSourceAsync(Action<Action> scope, Action<Task> retain)
        { DemandExternalOriginalJoin(); return BeginEntryRelease(scope, retain); }
        private Task ReleaseEntryForClose() => BeginEntryRelease(body => body(), _ => { });
        private Task BeginEntryRelease(Action<Action> scope, Action<Task> retain)
        {
            Task raw; TaskCompletionSource start; EgressContext source;
            lock (_sync)
            {
                if (_entryRelease is not null) return _entryRelease;
                source = Context(scope, retain, productive: false); _contexts.Add(source);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                raw = _entryRelease = ReleaseEntry(start.Task, source, _entry); _operations.Add(raw);
            }
            try { source.Run(() => retain(raw)); } catch { /* cached release owns callback failures */ }
            finally { start.SetResult(); } return raw;
        }
        private async Task ReleaseEntry(Task start, EgressContext source, Task? entry)
        {
            await start.ConfigureAwait(false); using var active = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var errors = new List<Exception>();
            if (entry is not null) try { await entry.ConfigureAwait(false); } catch (Exception cause) { errors.Add(entry.Exception ?? cause); }
            Task? homeClose = null, completionClose = null;
            if (_home is { } home)
            {
                try
                {
                    await source.ReadAsync(async () =>
                    {
                        homeClose = (home as IHomeOriginalScopedLocalOperationLeaseCleanup
                            ?? throw new InvalidOperationException("The actual Home entry lacks original cleanup."))
                            .DisposeWithinOriginalSourceAsync(source.Run, source.Retain).AsTask();
                        source.Retain(homeClose); await homeClose.ConfigureAwait(false); return true;
                    }).ConfigureAwait(false);
                }
                catch (Exception cause) { errors.Add(homeClose?.Exception ?? cause); }
                if (homeClose?.IsCompletedSuccessfully == true) { homeClose.GetAwaiter().GetResult(); _home = null; }
            }
            if (_completion is { } completion)
            {
                try
                {
                    await source.ReadAsync(async () =>
                    {
                        completionClose = completion.DisposeAsync().AsTask(); source.Retain(completionClose);
                        await completionClose.ConfigureAwait(false); return true;
                    }).ConfigureAwait(false);
                }
                catch (Exception cause) { errors.Add(completionClose?.Exception ?? cause); }
                if (completionClose?.IsCompletedSuccessfully == true) { completionClose.GetAwaiter().GetResult(); _completion = null; }
            }
            foreach (var cause in source.Errors) if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
            if (errors.Count != 0) throw new AggregateException("Original disclosure entry release did not settle.", errors);
            lock (_sync) _entry = null;
        }
        private sealed class HeldDisclosureGuard(Read read) : IHomeOriginalScopedStateCommitActorGuard
        {
            public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, CancellationToken token) => CheckAsync(state, expected, phase, body => body(), _ => { }, token);
            public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, Action<Action> scope, Action<Task> retain, CancellationToken token)
            {
                var source = read.Context(scope, retain); lock (read._sync) read._contexts.Add(source);
                try
                {
                    var valid = false;
                    source.Run(() => valid = expected == read._actor && read._attestation is not null &&
                        read._capability?.IsUncompletedClaim(read.Owner._broker) == true &&
                        read.Owner._broker.IsClaimedAttestationCurrentInState(read._attestation, expected, state) &&
                        read.Owner._producer.IsIssuedOriginalEgressIntent(read.OriginalIntent) &&
                        read.Owner._producer.GetOriginalEgressIntentDigest(read.OriginalIntent) == read._file!.Digest);
                    if (!valid) return false;
                    return await source.ReadAsync(() => read.Owner._profiles.CheckAsync(state, expected, phase,
                        source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
                }
                finally { source.CloseProductive(); }
            }
        }
        private Task<T> Run<T>(Action<Action> scope, Action<Task> retain, Func<EgressContext, Task<T>> body, bool requireAdmitted)
        {
            owner.DemandHealthyOriginalCallbacks();
            Task<T> raw; TaskCompletionSource start; EgressContext source;
            lock (_sync)
            {
                if ((_close is not null && (requireAdmitted || Acquisition.IsCompleted)) || (requireAdmitted && !IsAdmitted)) throw new UnauthorizedAccessException("The actual attachment disclosure is not active.");
                if (_operations.Count >= 4096) throw new InvalidOperationException("Original attachment disclosure custody is full.");
                source = Context(scope, retain); _contexts.Add(source); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                raw = Drive(start.Task, source, body); _operations.Add(raw);
            }
            try { source.Run(() => retain(raw)); } catch { /* driver retains source errors */ } finally { start.SetResult(); }
            return raw;
        }
        private async Task<T> Drive<T>(Task start, EgressContext source, Func<EgressContext, Task<T>> body)
        {
            await start.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try
            {
                source.Run(() => { }); var result = await body(source).ConfigureAwait(false);
                owner.DemandHealthyOriginalCallbacks(); return result;
            }
            finally { source.CloseProductive(); }
        }
    }
    private sealed class EgressContext
    {
        private readonly HomeCanonicalAssistantAttachmentEgressSource _owner;
        private readonly bool _productive;
        private int _closed;
        private readonly object _lifetimeGate = new();
        private readonly HomeOwnershipOriginalSourceCallbacks _protocol;
        internal EgressContext(HomeCanonicalAssistantAttachmentEgressSource owner, Read read,
            Action<Action> caller, Action<Task> retain, bool productive)
        {
            _owner = owner; _productive = productive;
            _protocol = new(
                body => CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                    CloudflareOriginalExecutionGuard.InvokeOriginal(read, () => { caller(body); return true; })),
                raw =>
                {
                    try { CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                        CloudflareOriginalExecutionGuard.InvokeOriginal(read, () => { retain(raw); return true; })); }
                    catch (Exception cause)
                    {
                        owner.RetainUnexpectedOriginalTask(raw); owner.RememberUnexpectedOriginalCallback(cause); throw;
                    }
                }, owner.RememberUnexpectedOriginalCallback);
        }
        internal Exception[] Errors => _protocol.Errors;
        internal void CloseProductive() { if (_productive) lock (_lifetimeGate) Volatile.Write(ref _closed, 1); }
        private void RefuseClosedProductive()
        {
            var cause = new InvalidOperationException("The original attachment disclosure productive context has closed.");
            _owner.RememberUnexpectedOriginalCallback(cause); throw cause;
        }
        private void DemandProductive()
        {
            if (!_productive) return;
            if (Volatile.Read(ref _closed) != 0) RefuseClosedProductive();
            _owner.DemandHealthyOriginalCallbacks();
        }
        internal void Run(Action body)
        {
            lock (_lifetimeGate)
            {
                DemandProductive();
                _protocol.Run(() => { DemandProductive(); body(); DemandProductive(); });
                DemandProductive();
            }
        }
        // Retention never gates independent custody behind a productive failure.
        internal void Retain(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            lock (_lifetimeGate)
            {
                if (_productive && Volatile.Read(ref _closed) != 0)
                {
                    _owner.RetainUnexpectedOriginalTask(actual); RefuseClosedProductive();
                }
                _protocol.Retain(actual);
            }
        }
        internal async Task<T> ReadAsync<T>(Func<Task<T>> factory)
        {
            DemandProductive();
            var result = await _protocol.ReadAsync(() => { DemandProductive(); return factory(); }).ConfigureAwait(false);
            DemandProductive(); return result;
        }
    }

}
