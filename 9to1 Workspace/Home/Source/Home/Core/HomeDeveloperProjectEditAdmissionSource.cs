using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

/// <summary>Actual WRITE admission for a manual (no-AI) source edit over a SAME privately issued
/// Files selection/capture and exact file/revision. It requires the configured Files edit-scope
/// issuer to recognize the SAME selection/capture/file, an explicit high-risk Home review, broker
/// execution capability/claim, and fresh revalidation of the actor/config/root/file/revision
/// before every finite write start. A READ admission never authorizes these writes.</summary>
public sealed class HomeDeveloperProjectEditAdmissionSource : IDeveloperProjectOriginalEditAdmissionSource,
    ICanonicalResourceAccessResolver, IWorkspaceManualEditFinalFenceValidator, IAsyncDisposable
{
    public const string EditAction = "dev.project.source.edit";
    public const string CreateAction = "dev.project.source.create";
    public string ResourceKind => "dev.project.file";
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly Func<IDeveloperProjectOriginalEditScopeSource> _edits;
    private readonly object _sync = new();
    private readonly List<Edit> _originals = [];
    private volatile bool _sealed; private Task? _close; private readonly CloudflareOriginalTaskLedger _closeStages = new();

    public HomeDeveloperProjectEditAdmissionSource(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        Func<IDeveloperProjectOriginalEditScopeSource> edits, WorkspaceTaskRunEffectAuthority effectAuthority)
    {
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) || !broker.IsBoundToPermissions(permissions))
            throw new InvalidOperationException("SAME actual Home store/profile/broker/policy required.");
        ArgumentNullException.ThrowIfNull(effectAuthority);
        _store = store; _profiles = profiles; _broker = broker; _permissions = permissions; _edits = edits;
    }
    public void DemandExternalOriginalEditAdmissionJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Edit[] originals; lock (_sync) originals = _originals.ToArray();
        foreach (var original in originals) original.DemandExternalJoin();
    }
    public Task CloseAndDrainOriginalEditsAsync()
    {
        DemandExternalOriginalEditAdmissionJoin();
        lock (_sync)
        {
            if (_close is not null) return _close; _sealed = true; var originals = _originals.ToArray();
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _closeStages.BindOriginalOwner(this);
            _close = ClosePublishedAsync(begin.Task, originals); begin.SetResult(); return _close;
        }
    }
    private async Task ClosePublishedAsync(Task begin, Edit[] originals)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var errors = new List<Exception>(); foreach (var original in originals) original.RequestRetirement();
        foreach (var original in originals)
            try { await _closeStages.AwaitAsync(_closeStages.Invoke(() => original.DisposeAsync())).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Home project EDIT originals failed to drain.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalEditsAsync());

    public Task<IDeveloperProjectOriginalEditAdmission> AcquireOriginalEditAsync(
        IDeveloperProjectOriginalReadSelection selection, IDeveloperProjectOriginalExistingSourceCapture capture,
        string projectRelativePath, string expectedSha256, string reviewedAfterText, CancellationToken token)
        => AcquireOriginalWriteAsync(selection, capture, projectRelativePath, expectedSha256, reviewedAfterText, false, token);

    public Task<IDeveloperProjectOriginalEditAdmission> AcquireOriginalCreateAsync(
        IDeveloperProjectOriginalReadSelection selection, IDeveloperProjectOriginalExistingSourceCapture capture,
        string projectRelativePath, string reviewedAfterText, CancellationToken token)
        => AcquireOriginalWriteAsync(selection, capture, projectRelativePath, DeveloperProjectSourceWriteRevision.Absent,
            reviewedAfterText, true, token);

    private Task<IDeveloperProjectOriginalEditAdmission> AcquireOriginalWriteAsync(
        IDeveloperProjectOriginalReadSelection selection, IDeveloperProjectOriginalExistingSourceCapture capture,
        string projectRelativePath, string expectedRevision, string reviewedAfterText, bool requiresAbsentTarget, CancellationToken token)
    {
        DemandExternalOriginalEditAdmissionJoin();
        lock (_sync)
        {
            if (_sealed) throw new ObjectDisposedException(nameof(HomeDeveloperProjectEditAdmissionSource));
            var existing = _originals.FirstOrDefault(value => ReferenceEquals(value.Selection, selection) &&
                ReferenceEquals(value.Capture, capture) && string.Equals(value.RelativePath, projectRelativePath, StringComparison.Ordinal));
            if (existing is not null)
            {
                // The SAME reviewed operation must match the exact AFTER payload already bound to it.
                if (existing.RequiresAbsentTarget != requiresAbsentTarget ||
                    !string.Equals(existing.ExpectedSha256, expectedRevision, StringComparison.Ordinal) ||
                    !string.Equals(existing.ReviewedAfterText, reviewedAfterText, StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("The reviewed write kind, BEFORE precondition or AFTER payload differs from the SAME original operation.");
                return existing.OriginalAcquisition;
            }
            if (_originals.Count >= 128) throw new InvalidOperationException("Unresolved original EDIT custody is full.");
            var edit = new Edit(this, selection, capture, projectRelativePath, expectedRevision, reviewedAfterText, requiresAbsentTarget); _originals.Add(edit); return edit.Start(token);
        }
    }
    public Task ValidateOriginalEditAsync(IDeveloperProjectOriginalReadSelection selection,
        IDeveloperProjectOriginalExistingSourceCapture capture, string projectRelativePath, string expectedSha256,
        IDeveloperProjectOriginalEditAdmission admission, CancellationToken token)
        => ValidateOriginalWriteAsync(selection, capture, projectRelativePath, expectedSha256, admission, false, token);

    public Task ValidateOriginalCreateAsync(IDeveloperProjectOriginalReadSelection selection,
        IDeveloperProjectOriginalExistingSourceCapture capture, string projectRelativePath,
        IDeveloperProjectOriginalEditAdmission admission, CancellationToken token)
        => ValidateOriginalWriteAsync(selection, capture, projectRelativePath, DeveloperProjectSourceWriteRevision.Absent, admission, true, token);

    private Task ValidateOriginalWriteAsync(IDeveloperProjectOriginalReadSelection selection,
        IDeveloperProjectOriginalExistingSourceCapture capture, string projectRelativePath, string expectedRevision,
        IDeveloperProjectOriginalEditAdmission admission, bool requiresAbsentTarget, CancellationToken token)
    {
        Edit original;
        lock (_sync)
            original = admission is Edit actual && _originals.Contains(actual) && actual.RequiresAbsentTarget == requiresAbsentTarget &&
                ReferenceEquals(actual.Selection, selection) && ReferenceEquals(actual.Capture, capture) &&
                string.Equals(actual.RelativePath, projectRelativePath, StringComparison.Ordinal) && string.Equals(actual.ExpectedSha256, expectedRevision, StringComparison.Ordinal)
                ? actual : throw new UnauthorizedAccessException("SAME original Home EDIT issuer/selection/capture/file required.");
        return original.RevalidateOriginalEditAsync(token);
    }
    public IWorkspaceToolFinalFence MintOriginalEditFence(IDeveloperProjectOriginalEditAdmission admission,
        string projectRelativePath, string expectedSha256, string reviewedAfterText)
        => MintOriginalWriteFence(admission, projectRelativePath, expectedSha256, reviewedAfterText, false);

    public IWorkspaceToolFinalFence MintOriginalCreateFence(IDeveloperProjectOriginalEditAdmission admission,
        string projectRelativePath, string reviewedAfterText)
        => MintOriginalWriteFence(admission, projectRelativePath, DeveloperProjectSourceWriteRevision.Absent, reviewedAfterText, true);

    private IWorkspaceToolFinalFence MintOriginalWriteFence(IDeveloperProjectOriginalEditAdmission admission,
        string projectRelativePath, string expectedSha256, string reviewedAfterText, bool requiresAbsentTarget)
    {
        DemandExternalOriginalEditAdmissionJoin();
        lock (_sync)
        {
            if (_sealed) throw new ObjectDisposedException(nameof(HomeDeveloperProjectEditAdmissionSource));
            if (admission is not Edit edit || !_originals.Contains(edit) || edit.RequiresAbsentTarget != requiresAbsentTarget)
                throw new UnauthorizedAccessException("Only the SAME private Home EDIT admission can mint a manual edit fence.");
            if (!string.Equals(edit.RelativePath, projectRelativePath, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The fence relative target does not match the SAME edit.");
            if (!string.Equals(edit.ExpectedSha256, expectedSha256, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The fence BEFORE revision does not match the SAME edit.");
            if (!string.Equals(edit.ReviewedAfterText, reviewedAfterText, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The fence AFTER replacement does not match the reviewed edit.");
            if (edit.OriginalAcquisition.IsCompletedSuccessfully != true || edit.IsSealed || edit.Actor is null)
                throw new UnauthorizedAccessException("The original Home EDIT admission is not admitted or was retired.");
            var fence = edit.MintFence(this, projectRelativePath, expectedSha256, reviewedAfterText);
            return fence;
        }
    }
    /// <summary>Genuine manual (no-AI) Home fence recognition for the configured authority. Only the
    /// SAME Home edit issuer that vended the fence (and therefore privately recognizes its own Edit)
    /// can validate it; no caller-supplied, AlwaysAllow or foreign-source fence passes.</summary>
    public bool IsIssuedOriginal(IWorkspaceToolFinalFence originalFence) =>
        originalFence is HomeSourceEditCommitFence fence && ReferenceEquals(fence.Owner, this) && !fence.IsClosed;

    /// <summary>Resolves the real Home permission request identity for an admitted Edit so the
    /// rig can observe genuine withdrawal (<see cref="HomePermissionTrustService.IsExecutionCurrentAsync"/>)
    /// without completing the claimed capability.</summary>
    internal string AdmittedEditCapabilityRequestId(IDeveloperProjectOriginalEditAdmission admission)
    {
        lock (_sync)
        {
            if (admission is not Edit edit || !_originals.Contains(edit))
                throw new UnauthorizedAccessException("SAME original Home EDIT issuer required.");
            var requestId = edit.CapabilityRequestId
                ?? throw new UnauthorizedAccessException("The original Home EDIT claim is not admitted.");
            return requestId;
        }
    }
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
    {
        lock (_sync)
        {
            var original = !_sealed && action is EditAction or CreateAction
                ? _originals.SingleOrDefault(x => x.Action == action && x.HasOriginalScope(actor, scope)) : null;
            return original is null
                ? ValueTask.FromResult(new ResourceAccessDecision(false, "DEV_ORIGINAL_EDIT_SCOPE_REQUIRED", actor.ActorId, scope.Revision, actor.OrganisationId))
                : new(original.StartOriginalEvaluation(actor, scope, token));
        }
    }

    internal sealed class Edit(HomeDeveloperProjectEditAdmissionSource owner,
        IDeveloperProjectOriginalReadSelection selection, IDeveloperProjectOriginalExistingSourceCapture capture,
        string relativePath, string expectedSha256, string reviewedAfterText, bool requiresAbsentTarget = false) : IDeveloperProjectOriginalEditAdmission
    {
        private readonly object _gate = new(); private readonly CloudflareOriginalTaskLedger _stages = new();
        private readonly CloudflareOriginalTaskLedger _closingStages = new();
        private Task<IDeveloperProjectOriginalEditAdmission> _acquisition = null!;
        private readonly List<Task> _validations = []; private readonly List<Task> _evaluations = []; private CancellationTokenSource? _stop; private bool _sealed;
        private readonly List<HomeSourceEditCommitFence> _fences = [];
        private AuthenticatedResourceActor? _actor; private IDeveloperProjectOriginalEditScopeSource _source = null!;
        private ResourceScope[] _scopes = []; private HomeResourcePreparedReview? _review; private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation; private Task? _close;
        private int _finiteCalls; private readonly List<Task> _finiteAdmissions = [];
        internal IDeveloperProjectOriginalReadSelection Selection => selection;
        internal IDeveloperProjectOriginalExistingSourceCapture Capture => capture;
        internal string RelativePath => relativePath;
        internal string ExpectedSha256 => expectedSha256;
        public bool RequiresAbsentTarget => requiresAbsentTarget;
        internal string Action => requiresAbsentTarget ? CreateAction : EditAction;
        internal Task<IDeveloperProjectOriginalEditAdmission> OriginalAcquisition => _acquisition;
        internal bool IsSealed { get { lock (_gate) return _sealed; } }
        internal string? CapabilityRequestId { get { lock (_gate) return _capability?.RequestId; } }
        internal AuthenticatedResourceActor? Actor => _actor;
        internal IDeveloperProjectOriginalEditCurrentnessPinSource ScopeSource
        {
            get
            {
                lock (_gate) return _source is IDeveloperProjectOriginalEditCurrentnessPinSource pins
                    ? pins : throw new UnauthorizedAccessException("The configured Files issuer cannot supply genuine edit currentness custody.");
            }
        }
        internal HomeSourceEditCommitFence MintFence(HomeDeveloperProjectEditAdmissionSource source,
            string projectRelativePath, string expectedSha256, string reviewedAfterText)
        {
            lock (_gate)
            {
                if (_sealed || owner._sealed || _acquisition.IsCompletedSuccessfully != true ||
                    _capability is null || _attestation is null || _actor is null)
                    throw new UnauthorizedAccessException("The original Home EDIT admission is not fully admitted.");
                var root = capture.OriginalExistingProjectRoot
                    ?? throw new InvalidOperationException("The captured source project root is unavailable.");
                var afterSha256 = ReviewedAfterSha256;
                if (_fences.Count >= 128) throw new InvalidOperationException("Original source-write fence custody is full.");
                var fence = new HomeSourceEditCommitFence(source, this, owner._broker, owner._store, owner._profiles,
                    _attestation, _capability, _actor, OperationId, root, projectRelativePath, expectedSha256,
                    reviewedAfterText, afterSha256, ScopeSource);
                _fences.Add(fence); return fence;
            }
        }
        public Guid OperationId { get; } = Guid.NewGuid();
        public string ReviewedAfterText { get; } = BindReviewedAfterText(reviewedAfterText, requiresAbsentTarget);
        public string ReviewedAfterSha256 { get; } = Sha256Hex(reviewedAfterText);
        public long ReviewedAfterUtf8Length { get; } = Encoding.UTF8.GetByteCount(reviewedAfterText);
        private static string BindReviewedAfterText(string after, bool permitsEmpty)
        {
            ArgumentNullException.ThrowIfNull(after);
            if ((!permitsEmpty && after.Length == 0) || after.Length > DeveloperProjectSetupIntent.MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(reviewedAfterText));
            byte[] bytes;
            try { bytes = new UTF8Encoding(false, true).GetBytes(after); }
            catch (EncoderFallbackException) { throw new ArgumentException("The reviewed AFTER payload must be valid Unicode.", nameof(reviewedAfterText)); }
            if (bytes.Length > DeveloperProjectSetupIntent.MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(reviewedAfterText));
            if (bytes.AsSpan().IndexOf((byte)0) >= 0) throw new ArgumentException("The reviewed AFTER replacement must be canonical bounded UTF-8 without NUL bytes.", nameof(reviewedAfterText));
            try { var round = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes); }
            catch (DecoderFallbackException) { throw new ArgumentException("The reviewed AFTER replacement must be valid UTF-8.", nameof(reviewedAfterText)); }
            return after;
        }
        private static string Sha256Hex(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        internal bool HasOriginalScope(AuthenticatedResourceActor actor, ResourceScope scope)
        { lock (_gate) return !_sealed && _actor == actor && _scopes.Length == 1 && _scopes[0] == scope; }
        internal Task<ResourceAccessDecision> StartOriginalEvaluation(AuthenticatedResourceActor actor, ResourceScope scope, CancellationToken token)
        {
            lock (_gate)
            {
                if (_sealed || owner._sealed) return Task.FromResult(new ResourceAccessDecision(false, "DEV_ORIGINAL_EDIT_RETIRED",
                    actor.ActorId, scope.Revision, actor.OrganisationId));
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var original = EvaluatePublishedAsync(begin.Task, actor, scope, token); _evaluations.Add(original); begin.SetResult(); return original;
            }
        }
        private async Task<ResourceAccessDecision> EvaluatePublishedAsync(Task begin, AuthenticatedResourceActor actor, ResourceScope scope, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await ValidateSourceAsync(actor, token).ConfigureAwait(false);
            var current = await ActorAsync(token).ConfigureAwait(false);
            return new(!owner._sealed && current == actor && HasOriginalScope(actor, scope),
                "DEV_ORIGINAL_SOURCE_EDIT", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        internal void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        internal void RequestRetirement() { lock (_gate) _sealed = true; }
        private void DemandLive()
        {
            lock (owner._sync) lock (_gate) if (owner._sealed || _sealed) throw new ObjectDisposedException("Original project EDIT admission");
        }
        internal Task<IDeveloperProjectOriginalEditAdmission> Start(CancellationToken token)
        {
            _stages.BindOriginalOwner(this);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _acquisition = AcquirePublishedAsync(begin.Task, token); begin.SetResult(); return _acquisition;
        }
        private async Task<AuthenticatedResourceActor> ActorAsync(CancellationToken token) =>
            await _stages.AwaitAsync(_stages.Invoke(() => owner._profiles.GetCurrentAsync(token))).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Open the genuine installed DeviceLocal Home profile before editing project source.");
        private async Task ValidateSourceAsync(AuthenticatedResourceActor actor, CancellationToken token)
        {
            DemandLive();
            if (!_stages.Invoke(() => ReferenceEquals(owner._edits(), _source))) throw new UnauthorizedAccessException("The original configured edit-scope issuer retired.");
            await _stages.AwaitAsync(_stages.Invoke(() => requiresAbsentTarget
                ? _source.RevalidateOriginalCreateScopeAsync(selection, capture, relativePath, actor, token)
                : _source.RevalidateOriginalEditScopeAsync(selection, capture, relativePath, expectedSha256, actor, token))).ConfigureAwait(false);
            DemandLive();
        }
        private async Task<IDeveloperProjectOriginalEditAdmission> AcquirePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _stages.RunToOriginalSettlementAsync(async () =>
            {
                DemandLive();
                _source = _stages.Invoke(() => owner._edits()) ?? throw new InvalidOperationException("Configured original Files edit-scope issuer is unavailable.");
                var stop = _stages.Invoke(() => CancellationTokenSource.CreateLinkedTokenSource(token));
                lock (_gate) { _stop = stop; if (_sealed || owner._sealed) throw new ObjectDisposedException("Original project EDIT admission"); }
                _actor = await ActorAsync(_stop.Token).ConfigureAwait(false); await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                var scopes = _stages.Invoke(() => requiresAbsentTarget
                    ? _source.GetOriginalCreateScopes(selection, capture, relativePath)
                    : _source.GetOriginalEditScopes(selection, capture, relativePath, expectedSha256)).ToArray();
                if (scopes.Length != 1 || scopes[0].Kind != "dev.project.file" || scopes[0].Access != ResourceAccess.Write ||
                    string.IsNullOrWhiteSpace(scopes[0].Id) || string.IsNullOrWhiteSpace(scopes[0].Revision))
                    throw new UnauthorizedAccessException("The configured Files issuer must issue one distinct exact original source WRITE scope.");
                if (!string.Equals(scopes[0].Revision, expectedSha256, StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("The configured edit scope revision does not match the expected current source hash.");
                lock (_gate) _scopes = scopes;
                var arguments = JsonSerializer.SerializeToElement(new { sourceSelection = scopes[0].Id, sourceRevision = scopes[0].Revision,
                    file = relativePath, reviewedAfterSha256 = ReviewedAfterSha256, reviewedAfterUtf8Length = ReviewedAfterUtf8Length,
                    operationId = OperationId, requiresAbsentTarget,
                    limits = requiresAbsentTarget
                        ? "single absent file; existing captured parent; 4 MiB; exact reviewed SHA-256; atomic no-replace"
                        : "single file; 4 MiB per file; exact reviewed SHA-256; atomic replace" });
                _review = _stages.Invoke(() => owner._broker.PrepareReviewForActor(_actor, "dev", Action, scopes, arguments,
                    requiresAbsentTarget
                        ? "Create the exactly reviewed new source file beneath your verified Files root. An existing file, including an empty file, will never be replaced. READ approval does not authorize this WRITE."
                        : "Replace the exactly reviewed source file beneath your verified Files root with the reviewed text. READ approval does not authorize this WRITE.", null,
                    (requiresAbsentTarget ? "dev:create:" : "dev:edit:") + _actor.AuthenticationRevision));
                var observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.AuthorizePreparedReviewAsync(_review, _stop.Token))).ConfigureAwait(false);
                var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
                while (observed.State == HomePreparedReviewState.RequestObserved && observed.Request?.State == HomePermissionRequestState.PendingApproval)
                {
                    if (DateTimeOffset.UtcNow >= deadline) throw new UnauthorizedAccessException("DEV_SOURCE_EDIT_APPROVAL_REQUIRED: Accept the exact selected project file WRITE in Home; no bytes were written.");
                    await _stages.AwaitAsync(Task.Delay(TimeSpan.FromSeconds(1), _stop.Token)).ConfigureAwait(false);
                    await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                    observed = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ObservePreparedReviewAsync(_review, _stop.Token))).ConfigureAwait(false);
                }
                if (observed.Request?.State != HomePermissionRequestState.Approved) throw new UnauthorizedAccessException("The original selected-file WRITE was not accepted.");
                await ValidateSourceAsync(_actor, _stop.Token).ConfigureAwait(false);
                if (await ActorAsync(_stop.Token).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("The original Home actor changed before EDIT admission.");
                _capability = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.BeginExecutionCapabilityAsync(_review.RequestId, arguments, _stop.Token))).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("Original Home EDIT capability unavailable.");
                var claim = await _stages.AwaitAsync(_stages.Invoke(() => owner._broker.ClaimExecutionObservedAsync(_capability, "dev", Action, scopes, arguments, _stop.Token))).ConfigureAwait(false);
                if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != _actor) throw new UnauthorizedAccessException("Original Home EDIT claim rejected.");
                _attestation = owner._broker.CaptureClaimedAttestation(_capability) ?? throw new UnauthorizedAccessException("Individual original Home EDIT Accept is required.");
                DemandLive(); return this;
            }).ConfigureAwait(false);
        }
        public Task RevalidateOriginalEditAsync(CancellationToken token)
        {
            DemandLive(); lock (_gate)
            {
                if (_sealed || owner._sealed || _acquisition.IsCompletedSuccessfully != true || _capability is null || _attestation is null) throw new UnauthorizedAccessException("The original EDIT claim is not admitted.");
                var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var task = RevalidatePublishedAsync(begin.Task, token);
                _validations.Add(task); begin.SetResult(); return task;
            }
        }
        private async Task RevalidatePublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await ValidateSourceAsync(_actor!, token).ConfigureAwait(false);
            if (await ActorAsync(token).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("The actual project EDIT actor changed.");
            var current = await _stages.AwaitAsync(_stages.Invoke(() => owner._permissions.IsExecutionCurrentAsync(_capability!.RequestId, token))).ConfigureAwait(false);
            if (!current || await ActorAsync(token).ConfigureAwait(false) != _actor) throw new UnauthorizedAccessException("The original project EDIT was revoked or its actor changed.");
            DemandLive();
        }
        public T RunOriginalEdit<T>(Func<T> body, CancellationToken token)
        {
            DemandLive(); TaskCompletionSource admission;
            lock (_gate)
            {
                token.ThrowIfCancellationRequested(); _stop!.Token.ThrowIfCancellationRequested();
                if (_sealed || owner._sealed || _acquisition.IsCompletedSuccessfully != true || _validations.Count == 0 || _validations.Any(x => !x.IsCompletedSuccessfully) ||
                    _capability is null || !_capability.IsUncompletedClaim(owner._broker)) throw new UnauthorizedAccessException("Fresh SAME original EDIT validation is required before the finite kernel write start.");
                if (++_finiteCalls > 20_000) throw new InvalidOperationException("The bounded original source EDIT call cohort is full.");
                admission = new(TaskCreationOptions.RunContinuationsAsynchronously); _finiteAdmissions.Add(admission.Task);
            }
            try { return _stages.Invoke(body); } // Callback outside locks; exact raw child enrolled before scope exit.
            finally { admission.TrySetResult(); } // Source close first joins every admitted finite callback, then its actual raw children.
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
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this); var errors = new List<Exception>();
            try { _closingStages.Invoke(() => { _stop?.Cancel(); return true; }); } catch (Exception cause) { errors.Add(cause); }
            Task[] drivers; lock (_gate) drivers = new Task[] { _acquisition }.Concat(_validations).Concat(_evaluations).Concat(_finiteAdmissions).ToArray();
            foreach (var original in drivers) try { await _closingStages.AwaitAsync(original).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            await _stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _stages.OriginalErrors) if (!errors.Any(x => ReferenceEquals(x, cause))) errors.Add(cause);
            HomeSourceEditCommitFence[] fences; lock (_gate) fences = _fences.ToArray();
            foreach (var fence in fences)
                try { await _closingStages.AwaitAsync(_closingStages.Invoke(fence.DisposeAsync)).ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(cause); }
            if (_capability is not null)
            {
                try
                {
                    var committed = fences.Any(fence => fence.HasCommittedEffect);
                    var state = errors.Count != 0 ? HomePermissionRequestState.Failed
                        : requiresAbsentTarget && (!committed || _finiteCalls == 0)
                            ? HomePermissionRequestState.Cancelled : HomePermissionRequestState.Succeeded;
                    var audit = await _closingStages.AwaitAsync(_closingStages.Invoke(() => owner._broker.CompleteExecutionAsync(_capability,
                        new(state,
                            requiresAbsentTarget
                                ? state == HomePermissionRequestState.Cancelled ? "DEV_ORIGINAL_SOURCE_CREATE_NOT_PUBLISHED"
                                    : state == HomePermissionRequestState.Succeeded ? "DEV_ORIGINAL_SOURCE_CREATE_CLOSED" : "DEV_ORIGINAL_SOURCE_CREATE_UNFINISHED"
                                : errors.Count == 0 ? "DEV_ORIGINAL_SOURCE_EDIT_CLOSED" : "DEV_ORIGINAL_SOURCE_EDIT_UNFINISHED",
                            requiresAbsentTarget
                                ? committed ? "The original CREATE finite swap ran; its admitted writer and cleanup cohort were joined. See the terminal state for verification failures."
                                    : "The accepted CREATE was closed without an original committed publication. No CREATE success is claimed."
                                : "The actual admitted local EDIT task cohort was joined; physical source settlement remains a separate kernel original.", []), CancellationToken.None))).ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("Original project EDIT audit unfinished: " + audit.Code);
                }
                catch (Exception cause) { errors.Add(cause); }
            }
            try { _closingStages.Invoke(() => { _stop?.Dispose(); return true; }); } catch (Exception cause) { errors.Add(cause); }
            await _closingStages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _closingStages.OriginalErrors) if (!errors.Any(x => ReferenceEquals(x, cause))) errors.Add(cause);
            if (errors.Count != 0) throw new AggregateException("Original Home EDIT drivers/tasks/audit/cleanup failed.", errors);
        }
    }
}

public sealed class HomeDeveloperProjectEditActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "dev" && actionId is HomeDeveloperProjectEditAdmissionSource.EditAction or HomeDeveloperProjectEditAdmissionSource.CreateAction
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, RequiresPerActionApproval: true) : null;
}

/// <summary>The actual Home composition receives this SAME lazy resolver before it builds its
/// resource service. Resolving the edit owner later avoids a Home/Files constructor cycle.</summary>
public sealed class HomeDeveloperProjectEditResourceResolver(Func<HomeDeveloperProjectEditAdmissionSource> source)
    : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "dev.project.file";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token)
        => source().EvaluateAsync(actor, action, scope, token);
}
