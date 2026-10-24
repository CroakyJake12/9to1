using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalCurrentProjectExecutionBridge
{
    /// <summary>Exact workspace fence owned by the requesting old command cohort only.
    /// Original reads/permission leases remain with their existing owners. Disposing this
    /// reservation never revives the superseded old execution bindings.</summary>
    public sealed class OriginalMetadataRebindReservation : IDisposable
    {
        internal readonly FilesDeveloperOriginalCurrentProjectExecutionBridge _owner;
        internal readonly IDisposable _fence;
        internal readonly IDeveloperOriginalProjectMetadataRebindPreparation _preparation;
        internal bool _closed, _completionStarted;
        internal Task? _completion;
        internal IDeveloperOriginalProjectMetadataRebindResult? _originalResult;
        internal OriginalMetadataRebindReservation(FilesDeveloperOriginalCurrentProjectExecutionBridge owner,
            IDisposable fence, IDeveloperOriginalProjectMetadataRebindPreparation preparation)
        { _owner = owner; _fence = fence; _preparation = preparation; }
        public IDeveloperOriginalProjectMetadataRebindPreparation OriginalPreparation => _preparation;
        // Historical acquired-product custody only; caller must ask the original Home source
        // about live issuance before using its new read. No copied observation grants effects.
        public IDeveloperOriginalProjectMetadataRebindResult? OriginalAcquiredResult
        { get { lock (_owner._gate) return _originalResult; } }
        public void Dispose()
        {
            lock (_owner._gate)
            {
                if (_closed) return;
                if (_completionStarted && (_completion is null || !_completion.IsCompleted))
                    throw new InvalidOperationException("Join the actual original metadata-rebind driver before releasing its workspace fence.");
                if (!_owner._metadataRebindReservations.Contains(this))
                    throw new UnauthorizedAccessException("The original metadata reservation was replaced.");
                _fence.Dispose(); _owner._metadataRebindReservations.Remove(this); _closed = true;
            }
        }
    }
    private readonly HashSet<OriginalMetadataRebindReservation> _metadataRebindReservations = [];

    /// <summary>Only a SAME-source healthy before-native preparation can request this fence.
    /// Active commands/pins and another live cohort refuse it. Only already-issued idle bindings
    /// for this exact old read lose execution eligibility; no other READ or lease is closed.</summary>
    public OriginalMetadataRebindReservation AcquireOriginalMetadataRebindUpdate(
        IDeveloperOriginalProjectMetadataRebindPreparation samePreparation)
    {
        var source = _reads as IDeveloperOriginalProjectMetadataRebindSource
            ?? throw new InvalidOperationException("The actual Home command READ source has no original metadata rebind protocol.");
        if (!source.IsIssuedOriginalMetadataRebindPreparation(samePreparation))
            throw new UnauthorizedAccessException("A SAME successful Home before-native preparation is required.");
        var oldRead = samePreparation.OriginalCommandRead;
        if (!_reads.IsIssuedOriginalCommandRead(oldRead)) throw new UnauthorizedAccessException("The original command READ retired.");
        var before = samePreparation.BeforeIdentity;
        var actor = before.OriginalHomeResourceActor;
        if (actor.AccountId is not null || actor.OrganisationId is not null ||
            !Guid.TryParse(actor.ProfileId, out var profile) || profile == Guid.Empty)
            throw new UnauthorizedAccessException("The actual personal Home command actor is required.");
        EditorSessionUpdate fence; Binding[] all, own;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(FilesDeveloperOriginalCurrentProjectExecutionBridge));
            _editorSessionCommands.RemoveAll(value => value.Actual is { IsCompletedSuccessfully: true });
            if (_editorSessionUpdates.ContainsKey(before.WorkspaceId) ||
                _editorSessionCommands.Any(value => value.WorkspaceId == before.WorkspaceId) ||
                _pins.Any(value => value.Binding.WorkspaceId == before.WorkspaceId && (!value.Sealed || value.Close is not { IsCompletedSuccessfully: true })))
                throw new InvalidOperationException("An active command, pin, metadata update or unresolved cleanup still owns this workspace.");
            all = _bindings.Where(value => value.WorkspaceId == before.WorkspaceId).ToArray();
            own = all.Where(value => ReferenceEquals(value.Frame.Read, oldRead) && !value.MetadataSuperseded).ToArray();
            if (own.Length == 0 || own.Any(value => !value.Original.Driver.IsCompletedSuccessfully ||
                value.DocumentSha != before.SavedWorkspaceDocumentSha256 || value.RootFingerprint != before.RegisteredRootFingerprint ||
                value.ProjectId != before.ProjectId || value.RootId != before.RootId || value.WorkspaceRevision != before.WorkspaceRevision ||
                value.OriginalActor != actor))
                throw new UnauthorizedAccessException("No exact successful idle execution cohort matches the before-native preparation.");
            var cohort = own[0].Frame.Context;
            if (own.Any(value => value.Frame.Context.TaskId != cohort.TaskId || value.Frame.Context.ExecutionId != cohort.ExecutionId ||
                value.Frame.Context.ContextId != cohort.ContextId))
                throw new UnauthorizedAccessException("One Task/Run/context command cohort is required; other tasks cannot be retired by this request.");
            fence = new EditorSessionUpdate(this, before.WorkspaceId, actor);
            _editorSessionUpdates.Add(before.WorkspaceId, fence);
        }
        try
        {
            // Issuer callbacks remain OUTSIDE the bridge gate; the actual workspace fence
            // excludes new commands/pins/bindings while these private ownership probes run.
            if (all.Except(own).Any(value => !value.MetadataSuperseded && _reads.IsIssuedOriginalCommandRead(value.Frame.Read)))
                throw new InvalidOperationException("Another live command READ cohort still owns this workspace; its leases were preserved.");
            if (!source.IsIssuedOriginalMetadataRebindPreparation(samePreparation) || !_reads.IsIssuedOriginalCommandRead(oldRead))
                throw new UnauthorizedAccessException("The actual before-native Home preparation retired while reserving its metadata update.");
            lock (_gate)
            {
                if (_retiring || !_editorSessionUpdates.TryGetValue(before.WorkspaceId, out var actual) || !ReferenceEquals(actual, fence) ||
                    _bindings.Count(value => value.WorkspaceId == before.WorkspaceId) != all.Length)
                    throw new InvalidOperationException("Execution ownership changed during original metadata reservation.");
                foreach (var binding in own) binding.MetadataSuperseded = true;
                var reservation = new OriginalMetadataRebindReservation(this, fence, samePreparation);
                _metadataRebindReservations.Add(reservation); return reservation;
            }
        }
        catch { fence.Dispose(); throw; }
    }

    /// <summary>After the SAME canonical atomic metadata write, obtain a NEW separately
    /// approved actual Home READ and native identity. Returns no execution approval and
    /// changes no Task/cold capsule. Keep this exact fence until the actual driver settles.</summary>
    public Task<IDeveloperOriginalProjectCommandRead> CompleteOriginalMetadataRebindWithinSourceAsync(
        OriginalMetadataRebindReservation sameReservation,
        IDeveloperProjectOriginalMetadataTransition sameTransition,
        IDeveloperProjectOriginalMetadataCommitValue sameCommittedValue,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = _reads as IDeveloperOriginalProjectMetadataRebindSource
            ?? throw new InvalidOperationException("The actual Home command READ source has no original metadata rebind protocol.");
        lock (_gate)
        {
            DemandMetadataReservation(sameReservation);
            if (sameReservation._completionStarted)
                throw new InvalidOperationException("The original metadata rebind was already submitted; inspect/join its actual result instead of replaying it.");
            sameReservation._completionStarted = true;
        }
        var actual = Start<IDeveloperOriginalProjectCommandRead>(scope, retain, async (original, sources) =>
        {
            sources.Invoke(() => { lock (_gate) DemandMetadataReservation(sameReservation); return true; });
            var result = await sources.Observe(() => source.CompleteOriginalMetadataRebindWithinSourceAsync(
                sameReservation._preparation, sameTransition, sameCommittedValue,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                if (!source.IsOwnedOriginalMetadataRebindResult(result))
                    throw new UnauthorizedAccessException("Returned metadata rebind has no actual Home source custody.");
                lock (_gate) sameReservation._originalResult = result; // Retain before any live-use/post-publication guard.
                return true;
            });
            sources.Invoke(() =>
            {
                lock (_gate) DemandMetadataReservation(sameReservation);
                if (!source.IsIssuedOriginalMetadataRebindResult(result) ||
                    !ReferenceEquals(result.OriginalPreparation, sameReservation._preparation) ||
                    !ReferenceEquals(result.OriginalTransition, sameTransition))
                    throw new UnauthorizedAccessException("The SAME successful newly approved Home/native result is required.");
                var before = sameReservation._preparation.BeforeIdentity; var after = result.AfterIdentity;
                if (before with { SavedWorkspaceDocumentSha256 = after.SavedWorkspaceDocumentSha256 } != after ||
                    !ReferenceEquals(result.NewOriginalCommandRead.OriginalDescriptor.OriginalStore, sameReservation._preparation.OriginalStore) ||
                    !_reads.IsIssuedOriginalCommandRead(result.NewOriginalCommandRead))
                    throw new UnauthorizedAccessException("The new actual command READ identity/root/actor differs from this original cohort.");
                token.ThrowIfCancellationRequested(); return true;
            });
            return result.NewOriginalCommandRead;
        });
        lock (_gate) sameReservation._completion = actual;
        return actual;
    }
    private void DemandMetadataReservation(OriginalMetadataRebindReservation value)
    {
        if (_retiring || value is null || !ReferenceEquals(value._owner, this) || value._closed ||
            !_metadataRebindReservations.Contains(value) ||
            !_editorSessionUpdates.TryGetValue(value._preparation.BeforeIdentity.WorkspaceId, out var fence) || !ReferenceEquals(fence, value._fence))
            throw new UnauthorizedAccessException("The SAME actual live metadata workspace reservation is required.");
    }
}
