using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator : ITaskRunOriginalActionAdmissionSource
{
    private sealed class OriginalActionRegistration(ITaskRunToolActionPreparation preparation, Guid? parent, string summary)
    {
        internal readonly ITaskRunToolActionPreparation Preparation = preparation;
        internal readonly Guid? Parent = parent;
        internal readonly string Summary = summary;
        internal Task<TaskExecutionSnapshot> Driver = null!;
        internal readonly List<Task> Sources = [];
        internal TaskRunAttemptAdmission? Attempt;
        internal Guid ActionId;
        internal TaskOriginalToolIntent? Intent;
        internal string[] Scopes = [];
        internal TaskExecutionSnapshot? Acknowledged;
        internal TaskExecutionSnapshot? LastValidated;
        internal TaskExecutionSnapshot? LatestAcknowledged;
        internal TaskRunOriginalActionAdmission? Receipt;
        internal int SourceAdmissions;
        internal readonly List<Task> Validations = [];
        internal bool RetirementSealed;
        internal TaskExecutionSnapshot? RetirementAcknowledged;
        internal Task? Retirement;
    }
    private readonly object _originalActionGate = new();
    private readonly ConditionalWeakTable<ITaskRunToolActionPreparation, OriginalActionRegistration> _originalActionPreparations = new();
    private readonly Dictionary<(Guid Task, Guid Action), OriginalActionRegistration> _unresolvedOriginalActions = [];
    private readonly HashSet<OriginalActionRegistration> _retainedOriginalActionRegistrations = [];
    private const int OriginalActionCapacity = 4096;

    private Task<TaskExecutionSnapshot> RegisterOriginalActionWithCustodyAsync(
        ITaskRunToolActionPreparation preparation, Guid? parent, string summary, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        TaskCompletionSource? start = null;
        Task<TaskExecutionSnapshot> actual;
        lock (_originalActionGate)
        {
            if (_originalActionPreparations.TryGetValue(preparation, out var retained))
            {
                if (retained.Parent != parent || retained.Summary != summary)
                    throw new InvalidOperationException("The SAME original preparation cannot acquire another action intent.");
                return retained.Driver;
            }
            if (_retainedOriginalActionRegistrations.Count >= OriginalActionCapacity)
                throw new InvalidOperationException("Finite retained original action custody is full.");
            var original = new OriginalActionRegistration(preparation, parent, summary);
            _retainedOriginalActionRegistrations.Add(original);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = original.Driver = RegisterOriginalActionBodyAsync(start.Task, original, token);
            _originalActionPreparations.Add(preparation, original);
        }
        start.SetResult();
        return actual;
    }

    private async Task<TaskExecutionSnapshot> RegisterOriginalActionBodyAsync(Task start, OriginalActionRegistration original,
        CancellationToken token)
    {
        await start.ConfigureAwait(false);
        using var context = EnterOriginalActionContext(original);
        token.ThrowIfCancellationRequested();
        var preparation = original.Preparation;
        var owner = _toolActionOwner ?? throw new InvalidOperationException("The actual tool-action owner is unavailable.");
        var attempt = preparation.OriginalAttempt;
        var actionId = preparation.ActionId;
        if (actionId == Guid.Empty) throw new ArgumentException("An original canonical action identity is required.");
        original.ActionId = actionId;
        original.Attempt = attempt;
        var snapshot = await AwaitOriginalActionSourceAsync(original, () => repository.GetAsync(attempt.Snapshot.TaskId, token)).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The original action's canonical task was not found.");
        RequireRun(snapshot, attempt.Snapshot.ExecutionId);
        RequireCurrentAttempt(snapshot, attempt.AttemptId);
        var live = RequireOriginalActionIssued(snapshot, attempt.AttemptId);
        if (!ReferenceEquals(live, attempt)) throw new InvalidOperationException("The SAME private issued attempt is required.");
        await AwaitOriginalActionSourceAsync(original, () => owner.ValidateOriginalPreparationAsync(preparation, snapshot, token).AsTask()).ConfigureAwait(false);
        var intent = preparation.OriginalToolIntent;
        if (intent is null || string.IsNullOrWhiteSpace(intent.RuntimeKey) || intent.RuntimeKey.Length > 256
            || string.IsNullOrWhiteSpace(intent.ToolName) || intent.ToolName.Length > 256
            || string.IsNullOrWhiteSpace(intent.CallDigest) || intent.CallDigest.Length > 128
            || intent.CanonicalWorkspaceRoot is { Length: > 32768 })
            throw new InvalidOperationException("The original owner supplied no bounded exact tool intent.");
        original.Intent = intent;
        original.Scopes = NormalizeScopes(preparation.RequiredPermissionScopes).ToArray();
        lock (_originalActionGate)
        {
            var key = (snapshot.TaskId, original.ActionId);
            if (_unresolvedOriginalActions.TryGetValue(key, out var existing) && !ReferenceEquals(existing, original))
                throw new InvalidOperationException("Another actual preparation already owns this canonical ActionId; unresolved effects cannot replay.");
            if (_unresolvedOriginalActions.Count >= OriginalActionCapacity)
                throw new InvalidOperationException("Finite unresolved original action custody is full.");
            _unresolvedOriginalActions[key] = original;
        }
        // This durable check also denies a different process/issuer after a crash. A stored intent is
        // never a fresh effect receipt. Genuine planned Pending work may acquire its first owner.
        var prior = snapshot.Plan.FirstOrDefault(node => node.ActionId == original.ActionId);
        if (prior is not null && (prior.State != TaskPlanNodeState.Pending || prior.Acceptance is not null))
            throw new InvalidOperationException("An already admitted, accepted or uncertain action requires owning recovery; the ActionId cannot be reacquired.");
        var node = new TaskPlanNode(original.ActionId, original.Parent, SensitiveTextRedactor.Redact(original.Summary, 256),
            TaskPlanNodeState.Running, preparation.InterruptionPolicy, snapshot.PlanVersion,
            RequiredPermissionScopes: original.Scopes) { OriginalToolIntent = intent };
        var plan = snapshot.Plan.ToList();
        var index = plan.FindIndex(item => item.ActionId == original.ActionId);
        if (index >= 0) plan[index] = node; else plan.Add(node);
        var acknowledged = await AwaitOriginalActionSourceAsync(original, () => PersistOriginalWriteOnlyAsync(snapshot with
        {
            Plan = plan, State = TaskExecutionLifecycle.Running, UpdatedAt = _time.GetUtcNow()
        }, token, actual => { lock (_originalActionGate) original.Sources.Add(actual); })).ConfigureAwait(false);
        original.Acknowledged = acknowledged;
        lock (_originalActionGate)
            if (original.LatestAcknowledged is not { } priorAcknowledged || priorAcknowledged.PersistenceRevision < acknowledged.PersistenceRevision)
                original.LatestAcknowledged = acknowledged;
        original.Receipt = new(this, preparation, attempt, original, acknowledged);
        _ = InvokeOriginalActionSource(original, () =>
        {
            PublishAcknowledgedSnapshot(acknowledged); // Known write ACK survives separately retained observer failures.
            return true;
        });
        return acknowledged;
    }

    public TaskRunOriginalActionAdmission RequireOriginalActionAdmission(
        ITaskRunToolActionPreparation samePreparation, TaskRunAttemptAdmission sameOriginalAttempt)
    {
        lock (_originalActionGate)
        {
            if (!_originalActionPreparations.TryGetValue(samePreparation, out var original)
                || !ReferenceEquals(original.Attempt, sameOriginalAttempt)
                || original.Driver is not { IsCompletedSuccessfully: true } || original.Receipt is not { } receipt)
                throw new InvalidOperationException("No successful SAME original action intent acknowledgment exists.");
            DemandOriginalActionIdentity(receipt, samePreparation, sameOriginalAttempt);
            return receipt;
        }
    }

    public Task ValidateOriginalActionAdmissionAsync(TaskRunOriginalActionAdmission originalReceipt,
        ITaskRunToolActionPreparation samePreparation, TaskRunAttemptAdmission sameOriginalAttempt, CancellationToken token)
    {
        TaskCompletionSource start;
        Task actual;
        lock (_originalActionGate)
        {
            var original = DemandOriginalActionIdentity(originalReceipt, samePreparation, sameOriginalAttempt);
            if (original.RetirementSealed)
                throw new InvalidOperationException("Original action validation admission is sealed for retirement.");
            if (original.Validations.Count >= 64)
                throw new InvalidOperationException("Finite actual action validation custody is full.");
            original.LastValidated = null;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = ValidateOriginalActionBodyAsync(start.Task, original, originalReceipt, samePreparation, sameOriginalAttempt, token);
            original.Validations.Add(actual);
        }
        start.SetResult();
        return actual;
    }

    private async Task ValidateOriginalActionBodyAsync(Task start, OriginalActionRegistration original,
        TaskRunOriginalActionAdmission originalReceipt, ITaskRunToolActionPreparation samePreparation,
        TaskRunAttemptAdmission sameOriginalAttempt, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        using var context = EnterOriginalActionContext(original);
        token.ThrowIfCancellationRequested();
        var current = await AwaitOriginalActionSourceAsync(original, () => repository.GetAsync(originalReceipt.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The canonical action task is unavailable.");
        RequireRun(current, originalReceipt.ExecutionId);
        RequireCurrentAttempt(current, originalReceipt.AttemptId);
        RequireOriginalActionNode(original, current);
        var issued = RequireOriginalActionIssued(current, originalReceipt.AttemptId);
        if (!ReferenceEquals(issued, sameOriginalAttempt)) throw new InvalidOperationException("Original action attempt authority changed.");
        // Fresh real authority after the final repository await; persisted owner IDs are never the check.
        await AwaitOriginalActionSourceAsync(original, () => ValidateTaskCommandAsync(current, "task:execute-original-action", token)).ConfigureAwait(false);
        await AwaitOriginalActionSourceAsync(original, () => sameOriginalAttempt.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        // Reobserve the actual node after awaited actor/lease work; stale earlier observations cannot fence an effect.
        var final = await AwaitOriginalActionSourceAsync(original, () => repository.GetAsync(originalReceipt.TaskId, token)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The final canonical action observation is unavailable.");
        RequireOriginalActionNode(original, final);
        if (!ReferenceEquals(RequireOriginalActionIssued(final, originalReceipt.AttemptId), sameOriginalAttempt))
            throw new InvalidOperationException("The final original action attempt changed.");
        await AwaitOriginalActionSourceAsync(original, () => ValidateTaskCommandAsync(final, "task:execute-original-action", token)).ConfigureAwait(false);
        var finalDetached = DetachOriginalActionObservation(final);
        lock (_originalActionGate)
        {
            _ = DemandOriginalActionIdentity(originalReceipt, samePreparation, sameOriginalAttempt);
            if (original.RetirementSealed)
                throw new InvalidOperationException("Original action retirement began during validation.");
            if (original.LatestAcknowledged is not { } latest || latest.PersistenceRevision < final.PersistenceRevision)
                original.LatestAcknowledged = finalDetached;
            RequireOriginalActionNode(original, original.LatestAcknowledged!);
            original.LastValidated = original.LatestAcknowledged;
        }
    }

    private TaskRunAttemptAdmission? RequireOriginalActionIssued(TaskExecutionSnapshot current, Guid attemptId)
    {
        RequireCurrentAttempt(current, attemptId);
        if (!_issuedAdmissions.TryGetValue(attemptId, out var issued) || issued.Snapshot.TaskId != current.TaskId
            || issued.Snapshot.ContextId != current.ContextId || issued.Snapshot.ExecutionId != current.ExecutionId
            || issued.Lease.Owner != current.OwnerBinding) return null;
        return issued;
    }

    public void DemandOriginalActionAdmission(TaskRunOriginalActionAdmission originalReceipt,
        ITaskRunToolActionPreparation samePreparation, TaskRunAttemptAdmission sameOriginalAttempt)
    {
        lock (_originalActionGate)
        {
            var original = DemandOriginalActionIdentity(originalReceipt, samePreparation, sameOriginalAttempt);
            if (original.RetirementSealed || original.Validations.Any(task => !task.IsCompletedSuccessfully)
                || original.LastValidated is not { } validated)
                throw new InvalidOperationException("The SAME original action claim has not completed current authority validation.");
            RequireOriginalActionNode(original, validated);
            RequireOriginalActionNode(original, original.LatestAcknowledged
                ?? throw new InvalidOperationException("No latest original canonical claim observation exists."));
        }
    }

    private OriginalActionRegistration DemandOriginalActionIdentity(TaskRunOriginalActionAdmission receipt,
        ITaskRunToolActionPreparation preparation, TaskRunAttemptAdmission attempt)
    {
        if (receipt is null || !ReferenceEquals(receipt.Issuer, this) || !ReferenceEquals(receipt.OriginalSelf, receipt)
            || !ReferenceEquals(receipt.OriginalPreparation, preparation) || !ReferenceEquals(receipt.OriginalAttempt, attempt)
            || !_originalActionPreparations.TryGetValue(preparation, out var original)
            || !ReferenceEquals(receipt.OriginalRegistration, original) || !ReferenceEquals(original.Receipt, receipt)
            || original.Driver is not { IsCompletedSuccessfully: true }
            || !_unresolvedOriginalActions.TryGetValue((receipt.TaskId, receipt.ActionId), out var same) || !ReferenceEquals(same, original)
            || original.Acknowledged is not { } acknowledged || receipt.AcknowledgedRevision != acknowledged.PersistenceRevision
            || !ReferenceEquals(original.Driver.Result, acknowledged) || receipt.TaskId != attempt.Snapshot.TaskId
            || receipt.ContextId != attempt.Snapshot.ContextId || receipt.ExecutionId != attempt.Snapshot.ExecutionId
            || receipt.AttemptId != attempt.AttemptId || receipt.ActionId != original.ActionId)
            throw new InvalidOperationException("Only the SAME privately issued active action claim is available.");
        return original;
    }

    private static void RequireOriginalActionNode(OriginalActionRegistration original, TaskExecutionSnapshot current)
    {
        var acknowledged = original.Acknowledged ?? throw new InvalidOperationException("No actual action acknowledgment exists.");
        var expected = acknowledged.Plan.Single(node => node.ActionId == original.ActionId);
        var node = current.Plan.FirstOrDefault(value => value.ActionId == original.ActionId);
        if (current.TaskId != acknowledged.TaskId || current.ContextId != acknowledged.ContextId
            || current.ExecutionId != acknowledged.ExecutionId || current.OwnerBinding != acknowledged.OwnerBinding
            || current.PersistenceRevision < acknowledged.PersistenceRevision || current.RecoveryObservation is not null
            || current.State is not (TaskExecutionLifecycle.Running or TaskExecutionLifecycle.WaitingSafeBoundary)
            || current.Attempts.LastOrDefault() is not { } currentAttempt || currentAttempt.Id != original.Attempt?.AttemptId
            || currentAttempt.State is not (TaskRunAttemptState.Admitted or TaskRunAttemptState.Running)
            || node is null || node.State is not (TaskPlanNodeState.Running or TaskPlanNodeState.WaitingSafeBoundary)
            || node.Acceptance is not null || node.ParentActionId != expected.ParentActionId || node.PlanVersion != expected.PlanVersion
            || node.OriginalToolIntent != original.Intent || node.InterruptionPolicy != expected.InterruptionPolicy
            || !(node.RequiredPermissionScopes ?? []).SequenceEqual(original.Scopes, StringComparer.Ordinal))
            throw new InvalidOperationException("The current canonical action no longer matches its original admission.");
    }

    private static TaskExecutionSnapshot DetachOriginalActionObservation(TaskExecutionSnapshot snapshot) => snapshot with
    {
        Plan = Array.AsReadOnly(snapshot.Plan.Select(node => node with
        { RequiredPermissionScopes = Array.AsReadOnly((node.RequiredPermissionScopes ?? []).ToArray()) }).ToArray()),
        Attempts = Array.AsReadOnly(snapshot.Attempts.ToArray())
    };

    // The sole canonical write-ACK path advances each live claim before any external observer.
    // This is private source currentness, never authority reconstructed from a public snapshot.
    private void ObserveOriginalActionAcknowledgedSnapshot(TaskExecutionSnapshot acknowledged)
    {
        lock (_originalActionGate)
            foreach (var original in _unresolvedOriginalActions.Values)
            {
                if (original.Attempt?.Snapshot.TaskId != acknowledged.TaskId) continue;
                if (original.LatestAcknowledged is not { } current || acknowledged.PersistenceRevision > current.PersistenceRevision)
                    original.LatestAcknowledged = acknowledged;
            }
    }

    private void ReserveOriginalActionSource(OriginalActionRegistration original)
    {
        lock (_originalActionGate)
        {
            if (original.SourceAdmissions >= 512)
                throw new InvalidOperationException("Finite retained action-source admission requires original inspection.");
            original.SourceAdmissions++;
        }
    }

    /// <summary>Housekeeping after the SAME accepted outcome and whole actual tool-owner close.
    /// This invokes the actual owner once and never infers healthy retirement from node text alone.</summary>
    private Task RetireAcknowledgedOriginalToolActionAsync(ITaskRunToolActionPreparation samePreparation,
        TaskExecutionSnapshot acknowledged, CancellationToken token)
    {
        DemandExternalOriginalActionJoin();
        token.ThrowIfCancellationRequested();
        TaskCompletionSource? start = null;
        Task actual;
        lock (_originalActionGate)
        {
            if (!_originalActionPreparations.TryGetValue(samePreparation, out var original)
                || original.Receipt is null || original.Attempt is null)
                throw new InvalidOperationException("No original action registration custody exists for retirement.");
            if (original.Retirement is not null)
            {
                if (!ReferenceEquals(original.RetirementAcknowledged, acknowledged))
                    throw new InvalidOperationException("Only the SAME acknowledged retirement original is available.");
                return original.Retirement;
            }
            _ = DemandOriginalActionIdentity(original.Receipt, samePreparation, original.Attempt);
            original.RetirementSealed = true;
            original.LastValidated = null;
            original.RetirementAcknowledged = acknowledged;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original.Retirement = RetireOriginalActionBodyAsync(start.Task, original, acknowledged);
            actual = original.Retirement;
        }
        start?.SetResult();
        return actual;
    }
    private async Task RetireOriginalActionBodyAsync(Task start, OriginalActionRegistration original,
        TaskExecutionSnapshot acknowledged)
    {
        await start.ConfigureAwait(false);
        using var context = EnterOriginalActionContext(original);
        Task[] validations;
        lock (_originalActionGate) validations = original.Validations.ToArray();
        List<Exception> validationCauses = [];
        foreach (var actualValidation in validations)
        {
            try { await actualValidation.ConfigureAwait(false); }
            catch (Exception cause) { validationCauses.Add(actualValidation.IsFaulted ? actualValidation.Exception! : cause); }
        }
        if (validationCauses.Count != 0)
            throw new AggregateException("Original action validation did not close successfully.", validationCauses);
        Task[] actualSources;
        lock (_originalActionGate) actualSources = original.Sources.ToArray();
        if (actualSources.Any(task => !task.IsCompletedSuccessfully))
            throw new InvalidOperationException("Actual original action sources remain failed, canceled or unresolved.");
        var receipt = original.Receipt ?? throw new InvalidOperationException("Original action receipt is unavailable.");
        var expected = acknowledged.Plan.FirstOrDefault(node => node.ActionId == receipt.ActionId);
        if (acknowledged.TaskId != receipt.TaskId || acknowledged.ContextId != receipt.ContextId
            || acknowledged.ExecutionId != receipt.ExecutionId || acknowledged.PersistenceRevision <= receipt.AcknowledgedRevision
            || expected is null || expected.State != TaskPlanNodeState.Completed || expected.OriginalToolIntent != original.Intent
            || expected.Acceptance is null && expected.InterruptionPolicy != TaskActionInterruptionPolicy.ReadOnlyCancellable)
            throw new InvalidOperationException("The SAME action has no acknowledged accepted or read-only outcome.");
        var owner = _toolActionOwner ?? throw new InvalidOperationException("The actual tool-action owner is unavailable.");
        // Actual owner verifies completed execution/physical receipts/scope close before releasing its custody.
        await AwaitOriginalActionSourceAsync(original, () => owner.RetireAcknowledgedOriginalAsync(
            original.Preparation, acknowledged, CancellationToken.None).AsTask()).ConfigureAwait(false);
        var current = await AwaitOriginalActionSourceAsync(original, () => repository.GetAsync(receipt.TaskId, CancellationToken.None)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The acknowledged original task is no longer available.");
        var actual = current.Plan.FirstOrDefault(node => node.ActionId == receipt.ActionId);
        if (current.ContextId != acknowledged.ContextId || current.ExecutionId != acknowledged.ExecutionId
            || current.OwnerBinding != acknowledged.OwnerBinding || current.PersistenceRevision < acknowledged.PersistenceRevision
            || actual is null || actual.State != TaskPlanNodeState.Completed || actual.Acceptance != expected.Acceptance
            || actual.OriginalToolIntent != original.Intent || actual.InterruptionPolicy != expected.InterruptionPolicy)
            throw new InvalidOperationException("The accepted outcome changed before original action retirement.");
        lock (_originalActionGate)
        {
            if (!_unresolvedOriginalActions.TryGetValue((receipt.TaskId, receipt.ActionId), out var same) || !ReferenceEquals(same, original))
                throw new InvalidOperationException("Original action custody changed during retirement.");
            if (original.Sources.Any(task => !task.IsCompletedSuccessfully))
                throw new InvalidOperationException("Original action retirement sources did not finish successfully.");
            _unresolvedOriginalActions.Remove((receipt.TaskId, receipt.ActionId));
            _retainedOriginalActionRegistrations.Remove(original);
        }
        // The original preparation's weak issued record retains exact driver/raw sources/receipt;
        // completed work remains in the canonical plan and checkpoints. No global healthy archive.
    }

    private async Task<T> AwaitOriginalActionSourceAsync<T>(OriginalActionRegistration original, Func<Task<T>> source)
    {
        ReserveOriginalActionSource(original);
        Task<T> actual;
        try { actual = InvokeOriginalActionSource(original, source) ?? throw new InvalidOperationException("No actual action source Task was returned."); }
        catch (OperationCanceledException synchronousCause) { throw new AggregateException("Actual synchronous action-source fault.", synchronousCause); }
        lock (_originalActionGate) original.Sources.Add(actual);
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted && actual.Exception is { }) { throw actual.Exception; }
    }
    private async Task AwaitOriginalActionSourceAsync(OriginalActionRegistration original, Func<Task> source)
    {
        ReserveOriginalActionSource(original);
        Task actual;
        try { actual = InvokeOriginalActionSource(original, source) ?? throw new InvalidOperationException("No actual action source Task was returned."); }
        catch (OperationCanceledException synchronousCause) { throw new AggregateException("Actual synchronous action-source fault.", synchronousCause); }
        lock (_originalActionGate) original.Sources.Add(actual);
        try { await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted && actual.Exception is { }) { throw actual.Exception; }
    }
    private sealed class OriginalActionContext(TaskExecutionCoordinator owner, OriginalActionRegistration original,
        OriginalActionContext? parent) : IDisposable
    {
        internal readonly TaskExecutionCoordinator Owner = owner;
        internal readonly OriginalActionRegistration Original = original;
        internal readonly OriginalActionContext? Parent = parent;
        internal volatile bool Active = true;
        public void Dispose() { Active = false; OriginalActionAncestry.Value = Parent; }
    }
    private static readonly AsyncLocal<OriginalActionContext?> OriginalActionAncestry = new();
    [ThreadStatic] private static OriginalActionContext? _physicalOriginalActionContext;

    private OriginalActionContext EnterOriginalActionContext(OriginalActionRegistration original)
    {
        var context = new OriginalActionContext(this, original, OriginalActionAncestry.Value);
        OriginalActionAncestry.Value = context;
        return context;
    }
    private T InvokeOriginalActionSource<T>(OriginalActionRegistration original, Func<T> source)
    {
        var previous = _physicalOriginalActionContext;
        var context = new OriginalActionContext(this, original, previous);
        _physicalOriginalActionContext = context;
        try { return source(); }
        finally { context.Active = false; _physicalOriginalActionContext = previous; }
    }
    private void DemandExternalOriginalActionJoin()
    {
        for (var context = _physicalOriginalActionContext; context is not null; context = context.Parent)
            if (context.Active && ReferenceEquals(context.Owner, this))
                throw new InvalidOperationException("An actual original action callback cannot join its own retirement.");
        for (var context = OriginalActionAncestry.Value; context is not null; context = context.Parent)
            if (context.Active && ReferenceEquals(context.Owner, this))
                throw new InvalidOperationException("An actual original action driver cannot join its own retirement.");
    }

}
