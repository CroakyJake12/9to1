using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    // Issuance evidence is process-local. Persisted receipt text cannot recreate a live grant.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskRunAttemptAdmission> _issuedAdmissions = new();
    // Actual live original settlement custody only. A persisted state cannot recreate this witness.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CompletionSettlementCustody> _completionSettlements = new();

    private readonly System.Collections.Concurrent.ConcurrentQueue<TaskObservationFailure> _observationFailures = new();
    public IReadOnlyList<TaskObservationFailure> ObservationFailures => _observationFailures.ToArray();
    public event EventHandler<TaskExecutionSnapshot>? SnapshotChanged;
    public bool HasAttemptAuthority => _admissionAuthority is not null && _runtimeSettlement is not null;
    /// <summary>Configuration observation only; every command still requires the actual fresh authority check.</summary>
    public bool HasCommandAuthority => _admissionAuthority is ITaskRunCommandAuthority;

    public Task<TaskExecutionSnapshot> BeginAuthorizedAsync(
        Guid contextId, Guid executionId, string promptSummary, TaskExecutionDurability durability,
        IReadOnlyCollection<string>? requestedPermissionScopes, CancellationToken cancellationToken) =>
        BeginAuthorizedOriginalAsync(contextId, executionId, promptSummary, durability, requestedPermissionScopes,
            cancellationToken, custody: null);

    private Task<TaskExecutionSnapshot> BeginAuthorizedOriginalAsync(
        Guid contextId, Guid executionId, string promptSummary, TaskExecutionDurability durability,
        IReadOnlyCollection<string>? requestedPermissionScopes, CancellationToken cancellationToken, TaskRunInvocationCustody? custody)
    {
        return StartOriginalProcessStage("begin-original-task", cancellationToken, token =>
            BeginAuthorizedProcessBodyAsync(contextId, executionId, promptSummary, durability, requestedPermissionScopes, token, custody));
    }

    private async Task<TaskExecutionSnapshot> BeginAuthorizedProcessBodyAsync(
        Guid contextId, Guid executionId, string promptSummary, TaskExecutionDurability durability,
        IReadOnlyCollection<string>? requestedPermissionScopes, CancellationToken cancellationToken, TaskRunInvocationCustody? custody)
    {
        var stage = RequireOriginalProcessStage();
        var authority = RequireAuthority();
        if (contextId == Guid.Empty || executionId == Guid.Empty)
            throw new ArgumentException("Canonical context and execution identities are required.");
        var now = _time.GetUtcNow();
        var proposed = new TaskExecutionSnapshot(Guid.NewGuid(), contextId, executionId,
            SensitiveTextRedactor.Redact(promptSummary, 240), TaskExecutionLifecycle.Running, durability,
            1, [], [], [], NormalizeScopes(requestedPermissionScopes), null, now, now);
        if (custody is not null) custody.ProposedBinding = proposed;
        var owner = await stage.Await(() => authority.AuthorizeStartAsync(proposed, cancellationToken)).ConfigureAwait(false);
        ValidateOwner(proposed, owner);
        proposed = proposed with { OwnerBinding = owner };
        if (custody is not null)
        {
            custody.ProposedBinding = proposed;
            if (!_originalInvocations.TryAdd(proposed.TaskId, custody))
                throw new InvalidOperationException("Another original invocation owns this exact new task identity.");
        }
        var originalBegin = stage.Invoke(() => PersistAsync(proposed, cancellationToken));
        stage.RetainSource(originalBegin);
        if (custody is not null) custody.OriginalBegin = originalBegin;
        var acknowledged = await originalBegin.ConfigureAwait(false);
        if (custody is not null)
        {
            custody.BoundByActualBegin = true;
            BindOriginalInvocation(custody, acknowledged, alreadyRegistered: true);
        }
        RetainOriginalBeginProcessResult(stage, acknowledged, custody);
        if (HasSealedOriginalProcessProducerAdmission || cancellationToken.IsCancellationRequested)
        {
            // The actual CAS acknowledged despite withdrawal. Preserve that SAME task/run and
            // bind the original receipt before recording this owning producer's suspended outcome.
            await SuspendOriginalLateAdmissionAsync(stage, acknowledged, null).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The original task begin acknowledged after process admission sealed.");
        }
        return acknowledged;
    }

    public Task<TaskRunAttemptAdmission> StartAttemptAsync(
        Guid taskId, Guid expectedExecutionId, TaskRunRouteCandidate candidate, CancellationToken cancellationToken) =>
        AdmitAttemptAsync(taskId, expectedExecutionId, null, candidate, cancellationToken);

    public Task<TaskRunAttemptAdmission> ResumeAttemptAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId,
        TaskRunRouteCandidate candidate, CancellationToken cancellationToken) =>
        AdmitAttemptAsync(taskId, expectedExecutionId, expectedAttemptId, candidate, cancellationToken);

    private Task<TaskRunAttemptAdmission> AdmitAttemptAsync(
        Guid taskId, Guid expectedExecutionId, Guid? previousAttemptId,
        TaskRunRouteCandidate candidate, CancellationToken cancellationToken) =>
        StartOriginalProcessStage(previousAttemptId is null ? "start-original-attempt" : "resume-original-attempt",
            cancellationToken, token => AdmitAttemptProcessBodyAsync(taskId, expectedExecutionId, previousAttemptId, candidate, token));

    private async Task<TaskRunAttemptAdmission> AdmitAttemptProcessBodyAsync(
        Guid taskId, Guid expectedExecutionId, Guid? previousAttemptId,
        TaskRunRouteCandidate candidate, CancellationToken cancellationToken,
        TaskRunColdContinuationBinding? originalColdBoundary = null)
    {
        var stage = RequireOriginalProcessStage();
        var authority = RequireAuthority();
        TaskRunAttemptAdmission? retiringOriginal = null;
        var snapshot = await RequireOriginalProcessSnapshotAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(snapshot, expectedExecutionId);
        ValidateOwner(snapshot, snapshot.OwnerBinding);
        ValidateCandidate(candidate);
        RequireNoUnresolvedOriginalInvocation(taskId);
        if (snapshot.RecoveryObservation is not null)
            throw new InvalidOperationException("Original orchestration needs owning-service inspection before any new attempt or replay.");
        if (snapshot.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled)
            throw new InvalidOperationException("A terminal task cannot admit another provider attempt.");
        if (previousAttemptId is null)
        {
            if (snapshot.Attempts.Count != 0)
                throw new InvalidOperationException("An existing run must resume its exact previous attempt.");
        }
        else
        {
            var old = RequireCurrentAttempt(snapshot, previousAttemptId, activeRequired: false);
            if (old.State is not (TaskRunAttemptState.Failed or TaskRunAttemptState.Suspended))
                throw new InvalidOperationException("Only a failed or suspended attempt can be resumed.");
            if (snapshot.Plan.Any(node => node.InterruptionPolicy is TaskActionInterruptionPolicy.AtomicCommit or TaskActionInterruptionPolicy.SafeBoundary
                && node.State is TaskPlanNodeState.Running or TaskPlanNodeState.WaitingSafeBoundary or TaskPlanNodeState.RequiresReexecution))
                throw new InvalidOperationException("An unresolved owner mutation requires inspection before any fallback.");
            var settlement = _runtimeSettlement ?? throw new InvalidOperationException("The original runtime settlement owner is unavailable.");
            if (originalColdBoundary is null)
                await stage.Await(() => settlement.AwaitSettlementAsync(snapshot.TaskId, snapshot.ExecutionId, old.Id, cancellationToken)).ConfigureAwait(false);
            else
                await DemandOriginalColdHistoricalSettlementAsync(originalColdBoundary, snapshot, old.Id, stage, cancellationToken).ConfigureAwait(false);
            if (_issuedAdmissions.TryGetValue(old.Id, out var prior))
            {
                // Keep this authentic closed original until the successor CAS acknowledges the same run.
                // A lost CAS must not erase the custody needed for an explicit safe retry.
                retiringOriginal = prior;
            }
            var settledOwner = snapshot.OwnerBinding;
            snapshot = await RequireOriginalProcessSnapshotAsync(taskId, cancellationToken).ConfigureAwait(false);
            RequireRun(snapshot, expectedExecutionId);
            ValidateOwner(snapshot, snapshot.OwnerBinding);
            if (snapshot.OwnerBinding != settledOwner || snapshot.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled)
                throw new InvalidOperationException("The canonical owner or task changed while the original attempt settled.");
            old = RequireCurrentAttempt(snapshot, previousAttemptId, activeRequired: false);
            if (old.State is not (TaskRunAttemptState.Failed or TaskRunAttemptState.Suspended)
                || snapshot.Plan.Any(node => node.InterruptionPolicy is TaskActionInterruptionPolicy.AtomicCommit or TaskActionInterruptionPolicy.SafeBoundary
                    && node.State is TaskPlanNodeState.Running or TaskPlanNodeState.WaitingSafeBoundary or TaskPlanNodeState.RequiresReexecution))
                throw new InvalidOperationException("The settled attempt no longer has a safe current continuation boundary.");
        }
        // No fresh model, worker, or permission is selected by this state owner.
        var attemptId = Guid.NewGuid();
        if (_originalInvocations.TryGetValue(taskId, out var originalInvocation)) originalInvocation.AttemptAdmissionInvoked = true;
        var lease = await stage.Await(() => authority.AuthorizeAttemptAsync(snapshot, attemptId, candidate, previousAttemptId, cancellationToken)).ConfigureAwait(false);
        var resource = new OriginalAttemptProcessResource(this, stage, lease);
        stage.OriginalResultClosed = resource.HasSuccessfulOriginalTerminal;
        stage.OriginalResultJoin = resource.JoinOriginalTerminalAsync;
        try
        {
            stage.Invoke(() =>
            {
                ValidateOwner(snapshot, lease.Owner);
                if (lease.Owner != snapshot.OwnerBinding || lease.AttemptId != attemptId || !CandidatesEqual(lease.Candidate, candidate)
                    || string.IsNullOrWhiteSpace(lease.ReceiptReference))
                    throw new InvalidOperationException("The actual attempt admission does not bind this canonical owner and candidate.");
                return true;
            });
            await stage.Await(() => lease.RevalidateAsync(cancellationToken).AsTask()).ConfigureAwait(false);
            var now = _time.GetUtcNow();
            var receiptReference = stage.Invoke(() => lease.ReceiptReference);
            var attempt = new TaskRunAttempt(attemptId, candidate, TaskRunAttemptState.Admitted,
                receiptReference, now, now, previousAttemptId);
            var next = await stage.Await(() => PersistAsync(snapshot with
            {
                Attempts = snapshot.Attempts.Append(attempt).ToArray(),
                State = TaskExecutionLifecycle.Running,
                UpdatedAt = now
            }, cancellationToken)).ConfigureAwait(false);
            var issued = new TaskRunAttemptAdmission(next, attemptId, lease);
            if (!_issuedAdmissions.TryAdd(attemptId, issued))
                throw new InvalidOperationException("The original attempt admission could not be retained.");
            resource.BindIssuedOriginal(issued);
            await stage.Await(() => resource.RegisterOriginalOwnershipAsync(cancellationToken), owningCleanup: true).ConfigureAwait(false);
            TaskRunProcessProducerContext.Invoke(this, () =>
            {
                PublishAttempt(next, attempt, previousAttemptId is null ? ExecutionActionType.Resume : ExecutionActionType.ModelFallback,
                    previousAttemptId is null ? "Provider attempt admitted" : "Same task resumed through authorised fallback");
                return true;
            });
            if (retiringOriginal is not null)
                await stage.Await(() => RetireOriginalAfterAcknowledgmentAsync(retiringOriginal, next).AsTask(), owningCleanup: true).ConfigureAwait(false);
            // Registration transfers cleanup ownership only. Fresh process admission is still
            // required before this caller can hand the attempt to any provider/effect dispatcher.
            cancellationToken.ThrowIfCancellationRequested();
            stage.Invoke(() => true);
            return issued;
        }
        catch (Exception admissionFailure)
        {
            // Even a late CAS or a registered lookup fault cannot prove absence of frame ownership.
            // The privately retained resource chooses cleanup ONLY from the actual owner receipt.
            if (resource.IssuedOriginal is { } issued)
                try { await SuspendOriginalLateAdmissionAsync(stage, issued.Snapshot, issued.AttemptId).ConfigureAwait(false); }
                catch (Exception observationFailure) { resource.RetainObservationFailure(observationFailure); }
            try { await stage.Await(resource.CloseAfterOriginalAdmissionFailureAsync, owningCleanup: true).ConfigureAwait(false); }
            catch (Exception closeFailure)
            {
                throw new AggregateException("The original admission and its actual cleanup owner failed.",
                    admissionFailure, closeFailure);
            }
            throw;
        }
    }

    public Task<TaskExecutionSnapshot> MarkAttemptRunningAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId, CancellationToken cancellationToken) =>
        StartOriginalProcessStage("mark-original-attempt-running", cancellationToken, token =>
            MarkAttemptRunningProcessBodyAsync(taskId, expectedExecutionId, expectedAttemptId, token));

    private async Task<TaskExecutionSnapshot> MarkAttemptRunningProcessBodyAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId, CancellationToken cancellationToken)
    {
        var stage = RequireOriginalProcessStage();
        var snapshot = await RequireOriginalProcessSnapshotAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(snapshot, expectedExecutionId);
        var attempt = RequireCurrentAttempt(snapshot, expectedAttemptId);
        var issued = await stage.Await(() => GetIssuedAttemptAsync(taskId, expectedExecutionId, expectedAttemptId, cancellationToken)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original issuer-owned attempt is unavailable.");
        await stage.Await(() => issued.Lease.RevalidateAsync(cancellationToken).AsTask()).ConfigureAwait(false);
        if (attempt.State == TaskRunAttemptState.Running) return snapshot;
        var updated = attempt with { State = TaskRunAttemptState.Running, UpdatedAt = _time.GetUtcNow() };
        return await PersistAsync(snapshot with
        {
            Attempts = snapshot.Attempts.Select(item => item.Id == attempt.Id ? updated : item).ToArray(),
            UpdatedAt = _time.GetUtcNow()
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Completes only the current admitted attempt after every original frame and cleanup has settled.</summary>
    public Task<TaskExecutionSnapshot> CompleteAttemptAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId, CancellationToken cancellationToken) =>
        CompleteAttemptOriginalAsync(taskId, expectedExecutionId, expectedAttemptId, cancellationToken, invocation: null);

    private Task<TaskExecutionSnapshot> CompleteAttemptOriginalAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId, CancellationToken cancellationToken,
        TaskRunInvocationCustody? invocation) =>
        StartOriginalProcessStage("complete-original-attempt", cancellationToken, token =>
            CompleteAttemptProcessBodyAsync(taskId, expectedExecutionId, expectedAttemptId, token, invocation));

    private async Task<TaskExecutionSnapshot> CompleteAttemptProcessBodyAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId, CancellationToken cancellationToken,
        TaskRunInvocationCustody? invocation)
    {
        var stage = RequireOriginalProcessStage();
        var snapshot = await RequireOriginalProcessSnapshotAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(snapshot, expectedExecutionId);
        RequireNoUnresolvedOriginalInvocation(taskId, invocation);
        ValidateOwner(snapshot, snapshot.OwnerBinding);
        var attempt = RequireCurrentAttempt(snapshot, expectedAttemptId, activeRequired: false);
        if (attempt.State == TaskRunAttemptState.Completed && snapshot.State == TaskExecutionLifecycle.Completed)
            return snapshot;
        RequireCurrentAttempt(snapshot, expectedAttemptId);
        RequireAcceptedPlan(snapshot);
        RequireAcknowledgedDelegationCompletions(snapshot);
        var issued = await stage.Await(() => GetIssuedAttemptAsync(taskId, expectedExecutionId, expectedAttemptId, cancellationToken)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original issuer-owned attempt is unavailable.");
        CompletionSettlementCustody custody;
        if (_completionSettlements.TryGetValue(expectedAttemptId, out var retained))
        {
            if (!ReferenceEquals(retained.OriginalAdmission, issued))
                throw new InvalidOperationException("A copied admission cannot replace original completion custody.");
            RequireCompletionBasis(retained.CompletionBasis, snapshot);
            // This is an explicit retry of the SAME original join, not a rebuilt or revalidated closed lease.
            await ValidateOriginalProcessCommandAsync(snapshot, "complete-task-after-original-settlement", cancellationToken).ConfigureAwait(false);
            custody = retained;
        }
        else
        {
            await stage.Await(() => issued.Lease.RevalidateAsync(cancellationToken).AsTask()).ConfigureAwait(false);
            var settlement = _runtimeSettlement ?? throw new InvalidOperationException("The original runtime settlement owner is unavailable.");
            // Retain the actual owning join. Caller cancellation may cancel only its wait, never this original.
            var originalSettlement = stage.Invoke(() => settlement.AwaitSettlementAsync(taskId, expectedExecutionId, expectedAttemptId, CancellationToken.None))
                ?? throw new InvalidOperationException("The original runtime returned no settlement task.");
            var proposed = new CompletionSettlementCustody(issued, snapshot, originalSettlement);
            custody = _completionSettlements.GetOrAdd(expectedAttemptId, proposed);
            if (!ReferenceEquals(custody.OriginalAdmission, issued) || !ReferenceEquals(custody.OriginalSettlement, originalSettlement))
                throw new InvalidOperationException("The original completion join changed during concurrent admission.");
        }
        if (invocation is not null)
        {
            if (invocation.OriginalSettlement is { } prior && !ReferenceEquals(prior, custody.OriginalSettlement))
                throw new InvalidOperationException("A different runtime join cannot replace this actual invocation's original settlement.");
            invocation.OriginalSettlement = custody.OriginalSettlement; // SAME actual owner join, retained before wait.
        }
        await AwaitOriginalProcessSettlementAsync(stage, custody.OriginalSettlement, cancellationToken).ConfigureAwait(false);
        // Neither durable Completed text nor absence in a fresh runtime can satisfy this live witness.
        if (!custody.OriginalSettlement.IsCompletedSuccessfully)
            throw new InvalidOperationException("The actual original settlement did not complete successfully.");
        snapshot = await RequireOriginalProcessSnapshotAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(snapshot, expectedExecutionId);
        RequireCompletionBasis(custody.CompletionBasis, snapshot);
        attempt = RequireCurrentAttempt(snapshot, expectedAttemptId);
        RequireAcceptedPlan(snapshot);
        RequireAcknowledgedDelegationCompletions(snapshot);
        await ValidateOriginalProcessCommandAsync(snapshot, "complete-task-after-original-settlement", cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var completed = attempt with { State = TaskRunAttemptState.Completed, UpdatedAt = now };
        var originalCompletionWrite = stage.Invoke(() => PersistAsync(snapshot with
        {
            Attempts = snapshot.Attempts.Select(item => item.Id == expectedAttemptId ? completed : item).ToArray(),
            State = TaskExecutionLifecycle.Completed,
            UpdatedAt = now
        }, cancellationToken));
        stage.RetainSource(originalCompletionWrite);
        var next = await originalCompletionWrite.ConfigureAwait(false);
        if (invocation is not null)
            BindOriginalCompletionCasAcknowledgment(invocation, stage, issued, custody.OriginalSettlement, originalCompletionWrite, next);
        TaskRunProcessProducerContext.Invoke(this, () =>
        {
            PublishAttempt(next, completed, ExecutionActionType.FinalResponse, "Canonical task completed after original runtime settlement");
            return true;
        });
        await stage.Await(() => RetireOriginalAfterAcknowledgmentAsync(issued, next).AsTask(), owningCleanup: true).ConfigureAwait(false);
        return next;
    }

    private sealed record CompletionSettlementCustody(
        TaskRunAttemptAdmission OriginalAdmission, TaskExecutionSnapshot CompletionBasis, Task OriginalSettlement);

    private static void RequireCompletionBasis(TaskExecutionSnapshot basis, TaskExecutionSnapshot current)
    {
        if (current.TaskId != basis.TaskId || current.ContextId != basis.ContextId || current.ExecutionId != basis.ExecutionId
            || current.OwnerBinding != basis.OwnerBinding || current.CreatedAt != basis.CreatedAt
            || current.PlanVersion != basis.PlanVersion || current.CheckpointId != basis.CheckpointId
            || current.LastCheckpointActionId != basis.LastCheckpointActionId
            || !current.ApprovedPermissionScopes.SequenceEqual(basis.ApprovedPermissionScopes, StringComparer.Ordinal)
            || current.Plan.Count != basis.Plan.Count || current.Steers.Count != basis.Steers.Count
            || current.ParentDelegation != basis.ParentDelegation || !SameDelegationObservations(current.Delegations, basis.Delegations))
            throw new InvalidOperationException("The canonical completion basis changed while the original runtime settled.");
        for (var index = 0; index < current.Plan.Count; index++)
        {
            var left = basis.Plan[index]; var right = current.Plan[index];
            if (left.ActionId != right.ActionId || left.ParentActionId != right.ParentActionId || left.Summary != right.Summary
                || left.State != right.State || left.InterruptionPolicy != right.InterruptionPolicy || left.PlanVersion != right.PlanVersion
                || left.SupersedesActionId != right.SupersedesActionId || left.RemediationId != right.RemediationId
                || left.Acceptance != right.Acceptance || left.OriginalToolIntent != right.OriginalToolIntent
                || !(left.RequiredPermissionScopes ?? []).SequenceEqual(right.RequiredPermissionScopes ?? [], StringComparer.Ordinal))
                throw new InvalidOperationException("An action or owning-service acceptance changed during original settlement.");
        }
        for (var index = 0; index < current.Steers.Count; index++)
        {
            var left = basis.Steers[index]; var right = current.Steers[index];
            if (left.Id != right.Id || left.TaskId != right.TaskId || left.ExecutionId != right.ExecutionId
                || left.Sequence != right.Sequence || left.Summary != right.Summary || left.Inference != right.Inference
                || left.State != right.State || left.CreatedAt != right.CreatedAt || left.SupersededById != right.SupersededById
                || !left.AffectedActionIds.SequenceEqual(right.AffectedActionIds)
                || !(left.RequiredPermissionScopes ?? []).SequenceEqual(right.RequiredPermissionScopes ?? [], StringComparer.Ordinal))
                throw new InvalidOperationException("The canonical steering intent changed during original settlement.");
        }
    }

    private async ValueTask RetireOriginalAfterAcknowledgmentAsync(
        TaskRunAttemptAdmission originalAdmission, TaskExecutionSnapshot acknowledgedSnapshot)
    {
        Task? originalRetirement = null;
        var processStage = TaskRunProcessStageCustody.CurrentFor(this);
        try
        {
            var retirement = _runtimeSettlement as ITaskRunOriginalAttemptRetirement
                ?? throw new InvalidOperationException("The actual healthy-original retirement owner is unavailable.");
            var receipt = new TaskRunOriginalRetirementAcknowledgment(originalAdmission, acknowledgedSnapshot);
            originalRetirement = processStage is null
                ? retirement.RetireAcknowledgedOriginalAttemptAsync(receipt, CancellationToken.None).AsTask()
                : processStage.Invoke(() => retirement.RetireAcknowledgedOriginalAttemptAsync(receipt, CancellationToken.None).AsTask(), owningCleanup: true);
            processStage?.RetainSource(originalRetirement);
            await originalRetirement.ConfigureAwait(false);
            ObserveOriginalRetiredProcessResource(originalAdmission, originalRetirement);
            _issuedAdmissions.TryRemove(new KeyValuePair<Guid, TaskRunAttemptAdmission>(originalAdmission.AttemptId, originalAdmission));
            if (_completionSettlements.TryGetValue(originalAdmission.AttemptId, out var settled)
                && ReferenceEquals(settled.OriginalAdmission, originalAdmission))
                _completionSettlements.TryRemove(new KeyValuePair<Guid, CompletionSettlementCustody>(originalAdmission.AttemptId, settled));
        }
        catch (Exception originalRetirementFailure)
        {
            processStage?.RetainOriginalFailure(originalRetirementFailure, originalRetirement);
            _observationFailures.Enqueue(new TaskObservationFailure(acknowledgedSnapshot.TaskId, acknowledgedSnapshot.ExecutionId,
                acknowledgedSnapshot.PersistenceRevision, "original-attempt-retirement-after-cas",
                (Exception?)originalRetirement?.Exception ?? originalRetirementFailure));
        }
    }

    private static void RequireAcceptedPlan(TaskExecutionSnapshot snapshot)
    {
        if (snapshot.RecoveryObservation is not null)
            throw new InvalidOperationException("An unresolved original orchestration cannot be projected as completed.");
        if (snapshot.Plan.Any(node => node.State != TaskPlanNodeState.Superseded
            && (node.State != TaskPlanNodeState.Completed
                || node.Acceptance is null && node.InterruptionPolicy != TaskActionInterruptionPolicy.ReadOnlyCancellable)))
            throw new InvalidOperationException("Unresolved actions require owning-service acceptance or explicit recovery before task completion.");
    }

    /// <summary>Records an owner-correlated read/no-effect observation without inventing an accepted mutation checkpoint.</summary>
    public Task<TaskExecutionSnapshot> RegisterOriginalToolActionAsync(
        ITaskRunToolActionPreparation originalPreparation, Guid? parentActionId,
        string summary, CancellationToken cancellationToken) =>
        RegisterOriginalActionWithCustodyAsync(originalPreparation, parentActionId, summary, cancellationToken);

    /// <summary>Records an owner-correlated read/no-effect observation without inventing an accepted mutation checkpoint.</summary>
    public async Task<TaskExecutionSnapshot> RecordObservedActionOutcomeAsync(
        ITaskRunToolActionPreparation originalPreparation, TaskRunToolActionResult originalResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalPreparation);
        ArgumentNullException.ThrowIfNull(originalResult);
        var owner = _toolActionOwner ?? throw new InvalidOperationException("The actual tool-action owner is unavailable.");
        await owner.ValidateOriginalResultAsync(originalPreparation, originalResult, cancellationToken).ConfigureAwait(false);
        var original = originalPreparation.OriginalAttempt;
        var snapshot = await RequireAsync(original.Snapshot.TaskId, cancellationToken).ConfigureAwait(false);
        RequireRun(snapshot, original.Snapshot.ExecutionId);
        RequireCurrentAttempt(snapshot, original.AttemptId);
        var live = await GetIssuedAttemptAsync(snapshot.TaskId, snapshot.ExecutionId, original.AttemptId, cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(live, original))
            throw new InvalidOperationException("The observed tool outcome does not retain the actual original attempt.");
        var action = snapshot.Plan.FirstOrDefault(node => node.ActionId == originalPreparation.ActionId)
            ?? throw new KeyNotFoundException("The observed action was not durably registered.");
        if (action.State is TaskPlanNodeState.Completed or TaskPlanNodeState.Superseded) return snapshot;
        if (action.InterruptionPolicy != originalPreparation.InterruptionPolicy
            || action.OriginalToolIntent != originalPreparation.OriginalToolIntent)
            throw new InvalidOperationException("The observed action policy changed after admission.");
        var readComplete = action.InterruptionPolicy == TaskActionInterruptionPolicy.ReadOnlyCancellable
            && originalResult.ReadOnlyObservationComplete;
        var nextState = readComplete ? TaskPlanNodeState.Completed
            : originalResult.KnownNoEffect ? TaskPlanNodeState.Failed : TaskPlanNodeState.RequiresReexecution;
        return await PersistAsync(snapshot with
        {
            Plan = snapshot.Plan.Select(node => node.ActionId == action.ActionId ? node with { State = nextState } : node).ToArray(),
            State = readComplete ? snapshot.State : TaskExecutionLifecycle.Suspended,
            UpdatedAt = _time.GetUtcNow()
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<TaskExecutionSnapshot> RecordAttemptFailureAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId,
        ExecutionFailure observedFailure, CancellationToken cancellationToken,
        TaskRunOriginalFailureObservation? originalFailure = null) =>
        StartOriginalProcessStage("record-original-attempt-failure", cancellationToken, token =>
            RecordAttemptFailureProcessBodyAsync(taskId, expectedExecutionId, expectedAttemptId, observedFailure, token, originalFailure));

    private async Task<TaskExecutionSnapshot> RecordAttemptFailureProcessBodyAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId,
        ExecutionFailure observedFailure, CancellationToken cancellationToken,
        TaskRunOriginalFailureObservation? originalFailure)
    {
        var stage = RequireOriginalProcessStage();
        ArgumentNullException.ThrowIfNull(observedFailure);
        var snapshot = await RequireOriginalProcessSnapshotAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(snapshot, expectedExecutionId);
        var attempt = RequireCurrentAttempt(snapshot, expectedAttemptId, activeRequired: false);
        if (attempt.State == TaskRunAttemptState.Completed)
            throw new InvalidOperationException("A completed attempt cannot be downgraded by a late provider failure.");
        if (attempt.State is TaskRunAttemptState.Failed or TaskRunAttemptState.Suspended) return snapshot;
        ITaskRunProviderFailureSettlement? originalFailureOwner = null;
        TaskRunAttemptAdmission? originalAdmission = null;
        if (originalFailure is not null)
        {
            originalFailureOwner = _runtimeSettlement as ITaskRunProviderFailureSettlement
                ?? throw new InvalidOperationException("The actual provider-frame failure owner is unavailable.");
            originalAdmission = await stage.Await(() => GetIssuedAttemptAsync(taskId, expectedExecutionId, expectedAttemptId, cancellationToken)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original issued attempt for this provider failure is unavailable.");
            if (!stage.Invoke(() => originalFailureOwner.ValidateProviderFailureObservation(originalFailure, originalAdmission)))
                throw new InvalidOperationException("This is not the original runtime-owned failure observation for this attempt.");
        }
        var safeFailure = observedFailure with
        {
            Code = SensitiveTextRedactor.Redact(observedFailure.Code, 128),
            Title = SensitiveTextRedactor.Redact(observedFailure.Title, 256),
            Message = SensitiveTextRedactor.Redact(observedFailure.Message, 2000),
            ProviderMessage = SensitiveTextRedactor.Redact(observedFailure.ProviderMessage, 2000)
        };
        var failed = attempt with { State = TaskRunAttemptState.Failed, Failure = safeFailure, UpdatedAt = _time.GetUtcNow() };
        var next = await PersistAsync(snapshot with
        {
            Attempts = snapshot.Attempts.Select(item => item.Id == attempt.Id ? failed : item).ToArray(),
            State = TaskExecutionLifecycle.Suspended,
            UpdatedAt = _time.GetUtcNow()
        }, cancellationToken).ConfigureAwait(false);
        if (originalFailure is not null)
        {
            var acknowledgedFailure = new TaskRunFailurePersistenceAcknowledgment(originalFailure, originalAdmission!, next);
            try { stage.Invoke(() =>
            {
                originalFailureOwner!.AcknowledgeProviderFailure(originalFailure, originalAdmission!, acknowledgedFailure);
                return true;
            }, owningCleanup: true); }
            catch (Exception acknowledgmentFailure)
            {
                _observationFailures.Enqueue(new TaskObservationFailure(next.TaskId, next.ExecutionId,
                    next.PersistenceRevision, "provider-failure-acknowledgment-after-cas", acknowledgmentFailure));
                throw;
            }
        }
        TaskRunProcessProducerContext.Invoke(this, () =>
        {
            PublishAttempt(next, failed, ExecutionActionType.Error, "Provider attempt suspended; accepted work retained");
            return true;
        });
        return next;
    }

    public async Task<TaskExecutionSnapshot> AcceptActionAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId, Guid actionId,
        string ownerReceiptReference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerReceiptReference);
        var snapshot = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(snapshot, expectedExecutionId);
        RequireCurrentAttempt(snapshot, expectedAttemptId);
        var action = snapshot.Plan.FirstOrDefault(item => item.ActionId == actionId)
            ?? throw new KeyNotFoundException("The action was not admitted to this canonical task.");
        if (action.State == TaskPlanNodeState.Superseded)
            throw new InvalidOperationException("Superseded work cannot be newly accepted.");
        if (action.State == TaskPlanNodeState.Completed)
        {
            if (action.Acceptance?.AttemptId == expectedAttemptId && action.Acceptance.OwnerReceiptReference == ownerReceiptReference) return snapshot;
            throw new InvalidOperationException("An accepted action cannot be replaced by a different receipt or attempt.");
        }
        await RequireAuthority().ValidateAcceptedActionAsync(snapshot, expectedAttemptId, actionId, ownerReceiptReference, cancellationToken).ConfigureAwait(false);
        var accepted = action with
        {
            State = TaskPlanNodeState.Completed,
            Acceptance = new TaskActionAcceptance(expectedAttemptId, ownerReceiptReference, _time.GetUtcNow())
        };
        var acknowledged = await PersistAsync(snapshot with
        {
            Plan = snapshot.Plan.Select(item => item.ActionId == actionId ? accepted : item).ToArray(),
            LastCheckpointActionId = actionId,
            UpdatedAt = _time.GetUtcNow()
        }, cancellationToken).ConfigureAwait(false);
        ObserveEvent(acknowledged, new ExecutionEvent(Guid.NewGuid(), acknowledged.ExecutionId, actionId,
            accepted.ParentActionId, ExecutionOrigin.Haven, ExecutionActionType.ToolResult,
            ExecutionActionStatus.Completed, "Owning service accepted action", null, null, "task-coordination",
            _time.GetUtcNow(), TaskId: acknowledged.TaskId,
            SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ownerAccepted"] = "true", ["attemptId"] = expectedAttemptId.ToString(),
                ["persistenceRevision"] = acknowledged.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
        return acknowledged;
    }

    public async Task<TaskExecutionSnapshot> RecordCheckpointAsync(
        Guid taskId, Guid expectedExecutionId, Guid checkpointId, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        RequireRun(snapshot, expectedExecutionId);
        var checkpoints = _checkpointRepository ?? throw new InvalidOperationException("The canonical checkpoint repository is unavailable.");
        var originalCheckpointOwner = _checkpointObservationSource
            ?? throw new InvalidOperationException("The original checkpoint execution observation owner is unavailable.");
        var originalCheckpoint = await originalCheckpointOwner.GetOriginalCheckpointAsync(expectedExecutionId,
            checkpointId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("This checkpoint was not issued by the actual service for the same original execution.");
        var checkpoint = await checkpoints.GetAsync(checkpointId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The original durable checkpoint was not found.");
        if (checkpoint != originalCheckpoint || checkpoint.ConversationId != snapshot.ContextId)
            throw new InvalidOperationException("The checkpoint does not belong to this canonical task context.");
        return await PersistAsync(snapshot with { CheckpointId = checkpoint.Id, UpdatedAt = _time.GetUtcNow() }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the original live issuer object only; persisted records cannot recreate it.</summary>
    public async Task<TaskRunAttemptAdmission?> GetIssuedAttemptAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId, CancellationToken cancellationToken)
    {
        var snapshot = await ReadOriginalAttemptSnapshotAsync(taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Task execution was not found.");
        RequireRun(snapshot, expectedExecutionId);
        RequireCurrentAttempt(snapshot, expectedAttemptId);
        if (!_issuedAdmissions.TryGetValue(expectedAttemptId, out var issued) || issued.Snapshot.TaskId != taskId
            || issued.Snapshot.ExecutionId != expectedExecutionId) return null;
        return issued;
    }

    /// <summary>Missing, retired, or superseded original issuance returns null. Repository and unknown failures propagate.</summary>
    public async Task<TaskRunAttemptAdmission?> TryGetIssuedAttemptAsync(
        Guid taskId, Guid expectedExecutionId, Guid expectedAttemptId, CancellationToken cancellationToken)
    {
        if (taskId == Guid.Empty || expectedExecutionId == Guid.Empty || expectedAttemptId == Guid.Empty)
            throw new ArgumentException("Exact canonical task, execution, and attempt identities are required.");
        var snapshot = await ReadOriginalAttemptSnapshotAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.ExecutionId != expectedExecutionId
            || snapshot.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled)
            return null;
        var attempt = snapshot.Attempts.LastOrDefault();
        if (attempt is null || attempt.Id != expectedAttemptId
            || attempt.State is not (TaskRunAttemptState.Admitted or TaskRunAttemptState.Running))
            return null;
        if (!_issuedAdmissions.TryGetValue(expectedAttemptId, out var issued)
            || issued.Snapshot.TaskId != taskId || issued.Snapshot.ContextId != snapshot.ContextId
            || issued.Snapshot.ExecutionId != expectedExecutionId)
            return null;
        return issued;
    }

    private async Task<TaskExecutionSnapshot?> ReadOriginalAttemptSnapshotAsync(Guid taskId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); // Genuine pre-call withdrawal invokes no repository callback.
        Task<TaskExecutionSnapshot?> actual;
        try
        {
            var stage = TaskRunProcessStageCustody.CurrentFor(this);
            actual = stage is null ? repository.GetAsync(taskId, token)
                : stage.Invoke(() => repository.GetAsync(taskId, token), owningCleanup: true);
            if (actual is null) throw new InvalidOperationException("The canonical repository returned no original attempt read Task.");
            stage?.RetainSource(actual);
        }
        catch (OperationCanceledException synchronousCause)
        {
            // No canceled original Task was returned by this synchronous callback.
            throw new AggregateException("The actual synchronous attempt read failed.", synchronousCause);
        }
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted && actual.Exception is { })
        {
            // Preserve the actual read's complete fault envelope, including faulted OCE.
            // A genuine canceled original follows normal canceled-task semantics instead.
            throw actual.Exception;
        }
    }

    private ITaskRunAdmissionAuthority RequireAuthority() => _admissionAuthority
        ?? throw new InvalidOperationException("The genuine task admission authority is unavailable.");

    private Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken cancellationToken)
    {
        if (snapshot.OwnerBinding is null) return Task.CompletedTask;
        var commandAuthority = _admissionAuthority as ITaskRunCommandAuthority
            ?? throw new InvalidOperationException("The current authenticated task-command owner is unavailable.");
        return commandAuthority.ValidateTaskCommandAsync(snapshot, command, cancellationToken);
    }

    /// <summary>Retires only an acknowledged original; a cleanup failure is retained without erasing or replaying its accepted write.</summary>
    public async ValueTask RetireAcknowledgedToolOriginalAsync(
        ITaskRunToolActionPreparation originalPreparation, TaskExecutionSnapshot acknowledgedSnapshot)
    {
        var action = acknowledgedSnapshot.Plan.FirstOrDefault(node => node.ActionId == originalPreparation.ActionId);
        if (action?.State != TaskPlanNodeState.Completed
            || action.Acceptance is null && originalPreparation.InterruptionPolicy != TaskActionInterruptionPolicy.ReadOnlyCancellable)
            return; // Unknown, failed and CAS-lost originals remain in their actual owner's custody.
        _ = _toolActionOwner ?? throw new InvalidOperationException("The actual tool-action owner is unavailable.");
        try
        {
            // Cleanup owns its lifetime. Caller cancellation after ACK cannot suppress or mislabel it.
            await RetireAcknowledgedOriginalToolActionAsync(originalPreparation, acknowledgedSnapshot, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception originalRetirementFailure)
        {
            _observationFailures.Enqueue(new TaskObservationFailure(acknowledgedSnapshot.TaskId,
                acknowledgedSnapshot.ExecutionId, acknowledgedSnapshot.PersistenceRevision,
                "tool-original-retirement-after-cas", originalRetirementFailure));
        }
    }

    private static void RequireRun(TaskExecutionSnapshot snapshot, Guid expectedExecutionId)
    {
        if (expectedExecutionId == Guid.Empty || snapshot.ExecutionId != expectedExecutionId)
            throw new InvalidOperationException("The requested continuation is not the same canonical execution.");
    }

    private static TaskRunAttempt RequireCurrentAttempt(TaskExecutionSnapshot snapshot, Guid? expectedAttemptId, bool activeRequired = true)
    {
        var attempt = snapshot.Attempts.LastOrDefault();
        if (expectedAttemptId is null || attempt is null || attempt.Id != expectedAttemptId)
            throw new InvalidOperationException("The original attempt identity is missing or stale.");
        if (activeRequired && attempt.State is not (TaskRunAttemptState.Admitted or TaskRunAttemptState.Running))
            throw new InvalidOperationException("The original attempt is no longer active.");
        return attempt;
    }

    private static void ValidateOwner(TaskExecutionSnapshot snapshot, TaskExecutionOwnerBinding? owner)
    {
        if (owner is null || owner.TaskId != snapshot.TaskId || owner.ContextId != snapshot.ContextId || owner.ExecutionId != snapshot.ExecutionId
            || string.IsNullOrWhiteSpace(owner.ActorId) || string.IsNullOrWhiteSpace(owner.ProfileId)
            || string.IsNullOrWhiteSpace(owner.AuthenticationRevision) || string.IsNullOrWhiteSpace(owner.AuthorizationReceiptReference))
            throw new InvalidOperationException("The genuine admission owner does not bind this canonical task and run.");
    }

    private static void ValidateCandidate(TaskRunRouteCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(candidate.RouteId) || candidate.RouteId.Length > 256 || candidate.RouteRevision < 0
            || string.IsNullOrWhiteSpace(candidate.ProviderId) || candidate.ProviderId.Length > 256
            || string.IsNullOrWhiteSpace(candidate.ModelId) || candidate.ModelId.Length > 512
            || candidate.RequiredCapabilities is null || candidate.RequiredCapabilities.Count > 128
            || candidate.RequiredCapabilities.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("An exact configured provider candidate is required.", nameof(candidate));
    }

    private static bool CandidatesEqual(TaskRunRouteCandidate left, TaskRunRouteCandidate right) =>
        left.RouteId == right.RouteId && left.RouteRevision == right.RouteRevision && left.ProviderId == right.ProviderId
        && left.ModelId == right.ModelId && left.ArtifactIdentity == right.ArtifactIdentity && left.UsesCloud == right.UsesCloud
        && left.RequiredCapabilities.SequenceEqual(right.RequiredCapabilities, StringComparer.Ordinal);

    private async Task<TaskExecutionSnapshot> PersistAsync(TaskExecutionSnapshot snapshot, CancellationToken cancellationToken,
        bool owningProcessCleanup = false)
    {
        var acknowledged = await PersistOriginalWriteOnlyAsync(snapshot, cancellationToken,
            owningProcessCleanup: owningProcessCleanup).ConfigureAwait(false);
        TaskRunProcessProducerContext.Invoke(this, () => { PublishAcknowledgedSnapshot(acknowledged); return true; });
        return acknowledged;
    }

    private async Task<TaskExecutionSnapshot> PersistOriginalWriteOnlyAsync(TaskExecutionSnapshot snapshot, CancellationToken cancellationToken,
        Action<Task>? retainActualRepositoryWrite = null, bool owningProcessCleanup = false)
    {
        if (snapshot.PersistenceRevision < 0) throw new InvalidDataException("The persisted task revision is invalid.");
        var proposed = snapshot with
        {
            PersistenceRevision = checked(snapshot.PersistenceRevision + 1),
            Plan = Array.AsReadOnly(snapshot.Plan.Select(node => node with
            {
                RequiredPermissionScopes = Array.AsReadOnly((node.RequiredPermissionScopes ?? []).ToArray())
            }).ToArray()),
            Steers = Array.AsReadOnly(snapshot.Steers.Select(steer => steer with
            {
                AffectedActionIds = Array.AsReadOnly(steer.AffectedActionIds.ToArray()),
                RequiredPermissionScopes = Array.AsReadOnly((steer.RequiredPermissionScopes ?? []).ToArray())
            }).ToArray()),
            Queue = Array.AsReadOnly(snapshot.Queue.ToArray()),
            Delegations = Array.AsReadOnly(snapshot.Delegations.Select(intent => intent with
            { RequestedPermissionScopes = Array.AsReadOnly(intent.RequestedPermissionScopes.ToArray()) }).ToArray()),
            RecoveryHistory = Array.AsReadOnly(snapshot.RecoveryHistory.Select(history => history with
            { Causes = Array.AsReadOnly(history.Causes.ToArray()) }).ToArray()),
            ApprovedPermissionScopes = Array.AsReadOnly(snapshot.ApprovedPermissionScopes.ToArray()),
            Attempts = Array.AsReadOnly(snapshot.Attempts.Select(attempt => attempt with
            {
                Candidate = attempt.Candidate with { RequiredCapabilities = Array.AsReadOnly(attempt.Candidate.RequiredCapabilities.ToArray()) }
            }).ToArray())
        };
        var stage = TaskRunProcessStageCustody.CurrentFor(this);
        var actualWrite = stage is null ? repository.UpsertAsync(proposed, cancellationToken)
            : stage.Invoke(() => repository.UpsertAsync(proposed, cancellationToken), owningProcessCleanup);
        if (actualWrite is null) throw new InvalidOperationException("The canonical repository returned no actual write Task.");
        stage?.RetainSource(actualWrite);
        retainActualRepositoryWrite?.Invoke(actualWrite);
        try { await actualWrite.ConfigureAwait(false); }
        catch (Exception) when (actualWrite.IsFaulted && actualWrite.Exception is { })
        {
            // The actual repository Task owns every direct cause; preserve faulted-OCE
            // and sibling custody at the async persistence boundary. No retry is inferred.
            throw actualWrite.Exception;
        }
        ObserveOriginalActionAcknowledgedSnapshot(proposed);
        return proposed;
    }

    private void PublishAcknowledgedSnapshot(TaskExecutionSnapshot proposed)
    {
        // This is an acknowledged write. Observer exceptions or late cancellation cannot erase it.
        if (SnapshotChanged is { } handlers)
            foreach (EventHandler<TaskExecutionSnapshot> handler in handlers.GetInvocationList())
                try { handler(this, proposed); }
                catch (Exception observerFailure)
                {
                    _observationFailures.Enqueue(new TaskObservationFailure(proposed.TaskId, proposed.ExecutionId,
                        proposed.PersistenceRevision, "snapshot-observer", observerFailure));
                }
    }

    private void ObserveEvent(TaskExecutionSnapshot snapshot, ExecutionEvent executionEvent)
    {
        try { events.TryPublish(executionEvent); }
        catch (Exception observerFailure)
        {
            _observationFailures.Enqueue(new TaskObservationFailure(snapshot.TaskId, snapshot.ExecutionId,
                snapshot.PersistenceRevision, "execution-event-sink", observerFailure));
        }
    }

    private void PublishAttempt(TaskExecutionSnapshot snapshot, TaskRunAttempt attempt, ExecutionActionType type, string name)
    {
        ObserveEvent(snapshot, new ExecutionEvent(Guid.NewGuid(), snapshot.ExecutionId, attempt.Id, snapshot.LastCheckpointActionId,
            ExecutionOrigin.Haven, type, attempt.State switch
            {
                TaskRunAttemptState.Failed or TaskRunAttemptState.Suspended => ExecutionActionStatus.Suspended,
                TaskRunAttemptState.Completed => ExecutionActionStatus.Completed,
                TaskRunAttemptState.Running => ExecutionActionStatus.Running,
                _ => ExecutionActionStatus.Queued
            },
            name, null, null, "task-coordination", _time.GetUtcNow(), TaskId: snapshot.TaskId,
            RetryOfActionId: attempt.RetryOfAttemptId, Failure: attempt.Failure,
            SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["attemptId"] = attempt.Id.ToString(), ["routeId"] = attempt.Candidate.RouteId,
                ["routeRevision"] = attempt.Candidate.RouteRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["persistenceRevision"] = snapshot.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
    }
}
