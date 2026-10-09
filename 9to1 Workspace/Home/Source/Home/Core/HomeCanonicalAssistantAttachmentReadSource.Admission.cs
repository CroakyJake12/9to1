using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;
public sealed partial class HomeCanonicalAssistantAttachmentReadSource
{
    private sealed partial class Read(HomeCanonicalAssistantAttachmentReadSource owner, ICanonicalAttachmentOriginalSelection selection)
        : ICanonicalAttachmentOriginalReadAdmission
    {
        internal HomeCanonicalAssistantAttachmentReadSource Owner => owner;
        public ICanonicalAttachmentOriginalSelection OriginalSelection => selection;
        public string OriginalApprovalRequestId { get { lock (_sync) return _review?.RequestId ?? ""; } }
        internal Task<ICanonicalAttachmentOriginalReadAdmission> Acquisition = null!;
        private readonly object _sync = new();
        private HomeOwnershipOriginalSourceCallbacks _sources = null!;
        private readonly List<Task> _operations = [];
        private readonly List<HomeOwnershipOriginalSourceCallbacks> _contexts = [];
        private AuthenticatedResourceActor? _actor;
        private CanonicalAttachmentFileIdentity? _file;
        private ResourceScope? _scope;
        private JsonElement _arguments;
        private HomeResourcePreparedReview? _review;
        private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation;
        private Exception? _declined;
        private bool _refusalSettled, _capabilityStarting;
        private Task? _close;
        private Task<bool>? _withdrawal;
        private HomeOwnershipOriginalSourceCallbacks? _withdrawalSources;
        private readonly TaskCompletionSource<Task<HomePreparedReviewObservation>?> _prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<HomePreparedReviewObservation>? _preparedTask;
        internal bool IsAdmitted { get { lock (_sync) return _close is null && Acquisition.IsCompletedSuccessfully && _capability is not null && _attestation is not null; } }
        internal bool Matches(AuthenticatedResourceActor actor, ResourceScope scope)
        { lock (_sync) return (_close is null || !Acquisition.IsCompleted) && _actor == actor && _scope == scope; }
        private HomeOwnershipOriginalSourceCallbacks Context(Action<Action> scope, Action<Task> retain) => new(
            body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { scope(body); return true; }),
            raw => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { retain(raw); return true; }));
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
        private async Task<ICanonicalAttachmentOriginalReadAdmission> Acquire(Task start, CancellationToken callerToken)
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
                    _arguments = JsonSerializer.SerializeToElement(new { selection = _file, actor = _actor, purpose = "Read registered text for an Assistant draft" });
                    _scope = new(owner.ResourceKind, _file!.StoreId.ToString("D") + ":" + _file.FileId.ToString("D"),
                        _file.RevisionId.ToString("D") + ":" + _file.Sha256, ResourceAccess.Read);
                    _review = owner._broker.PrepareReviewForActor(_actor!, "assistants", ReadAction, [_scope], _arguments,
                        "Read this selected text file for your Assistant draft. Saving the attachment needs a separate review.", null,
                        "assistant-attachment-read:" + Guid.NewGuid().ToString("N"));
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
                    if (!retired) throw new InvalidOperationException("The exact declined READ review did not retire.");
                    _declined = new UnauthorizedAccessException("Reading this selected file was declined in Home."); throw _declined;
                }
                if (observed.Request?.State != HomePermissionRequestState.Approved)
                    throw new UnauthorizedAccessException("This attachment requires its individual Home READ approval.");
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
                if (_capability is null) throw new UnauthorizedAccessException("The exact approved attachment READ capability is unavailable.");
                var claim = await _sources.ReadAsync(() => owner._broker.ClaimExecutionWithinOriginalSourceAsync(_capability,
                    "assistants", ReadAction, [_scope!], _arguments, _sources.Run, _sources.Retain, body => body(), token, originalPermissionSources: true)).ConfigureAwait(false);
                if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != _actor)
                    throw new UnauthorizedAccessException("The actual attachment READ claim was refused.");
                _sources.Run(() => _attestation = owner._broker.CaptureClaimedAttestation(_capability));
                if (_attestation is null) throw new UnauthorizedAccessException("The exact manual READ attestation is unavailable.");
                await ValidateSelection(_sources, token).ConfigureAwait(false); return this;
            }
            catch (Exception cause)
            {
                if (ReferenceEquals(cause, _declined) && _sources.Errors.Length == 0) _refusalSettled = true;
                throw;
            }
            finally { _prepared.TrySetResult(null); }
        }
        private async Task ValidateSelection(HomeOwnershipOriginalSourceCallbacks source, CancellationToken token)
        {
            source.Run(() =>
            {
                if (!owner._selections.IsIssuedOriginalSelection(selection)) throw new UnauthorizedAccessException("The SAME configured Files selection is required.");
                var file = selection.OriginalFile; var actor = selection.OriginalActor;
                if (_actor is not null && (_actor != actor || _file != file)) throw new UnauthorizedAccessException("The selected Files identity changed.");
                _actor ??= actor; _file ??= file;
                if (file.StoreId == Guid.Empty || file.FileId == Guid.Empty || file.RevisionId == Guid.Empty || file.SizeBytes < 1 ||
                    string.IsNullOrWhiteSpace(file.Sha256)) throw new UnauthorizedAccessException("The actual selected Files revision is incomplete.");
            });
            var current = await source.ReadAsync(() => owner._profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            if (current != _actor) throw new UnauthorizedAccessException("The Home profile changed before attachment access.");
            await ReadVoid(source, () => owner._selections.RevalidateOriginalSelectionWithinSourceAsync(selection, source.Run, source.Retain, token)).ConfigureAwait(false);
            current = await source.ReadAsync(() => owner._profiles.GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            source.Run(() => { if (current != _actor || !owner._selections.IsIssuedOriginalSelection(selection) ||
                selection.OriginalActor != _actor || selection.OriginalFile != _file) throw new UnauthorizedAccessException("The original attachment source changed during review."); });
        }
        private static Task<bool> ReadVoid(HomeOwnershipOriginalSourceCallbacks source, Func<Task> factory) => source.ReadAsync(() =>
        {
            var raw = factory() ?? throw new InvalidOperationException("The actual attachment source returned no Task.");
            source.Retain(raw); return Join(raw);
            static async Task<bool> Join(Task original) { await original.ConfigureAwait(false); return true; }
        });
        internal Task<ResourceAccessDecision> Evaluate(AuthenticatedResourceActor actor, ResourceScope scope,
            Action<Action> callback, Action<Task> retain, CancellationToken token) => Run(callback, retain, async source =>
        {
            await ValidateSelection(source, token).ConfigureAwait(false);
            return new ResourceAccessDecision(Matches(actor, scope), "ATTACHMENT_PRIVATE_SELECTION_READ", actor.ActorId, scope.Revision, actor.OrganisationId);
        }, requireAdmitted: false);
        public Task ValidateOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token) => Run(scope, retain, async source =>
        {
            await ValidateSelection(source, token).ConfigureAwait(false);
            if (!await source.ReadAsync(() => owner._permissions.IsSetupExecutionCurrentWithinOriginalSourceAsync(_capability!.RequestId, source.Run, source.Retain, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("This individual attachment READ was revoked.");
            await ValidateSelection(source, token).ConfigureAwait(false); return true;
        }, requireAdmitted: true);
        private Task<T> Run<T>(Action<Action> scope, Action<Task> retain, Func<HomeOwnershipOriginalSourceCallbacks, Task<T>> body, bool requireAdmitted)
        {
            Task<T> raw; TaskCompletionSource start; HomeOwnershipOriginalSourceCallbacks source;
            lock (_sync)
            {
                if ((_close is not null && (requireAdmitted || Acquisition.IsCompleted)) || (requireAdmitted && !IsAdmitted)) throw new UnauthorizedAccessException("The actual attachment READ is not active.");
                if (_operations.Count >= 4096) throw new InvalidOperationException("Original attachment READ custody is full.");
                source = Context(scope, retain); _contexts.Add(source); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                raw = Drive(start.Task, source, body); _operations.Add(raw);
            }
            try { source.Run(() => retain(raw)); } catch { /* driver retains source errors */ } finally { start.SetResult(); }
            return raw;
        }
        private async Task<T> Drive<T>(Task start, HomeOwnershipOriginalSourceCallbacks source, Func<HomeOwnershipOriginalSourceCallbacks, Task<T>> body)
        {
            await start.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            source.Run(() => { }); return await body(source).ConfigureAwait(false);
        }
    }
}
