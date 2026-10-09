using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Task-owned child issuance. Persisted links/scopes remain observations; every
/// child dispatch additionally needs its own ordinary fresh route/tool/context permission.</summary>
public sealed partial class TaskRunPermissionAuthority
{
    private readonly Dictionary<(Guid Task, Guid Intent), DelegationOriginal> _delegations = [];
    private readonly Dictionary<Guid, DelegationOriginal> _delegatedChildren = [];
    private readonly AsyncLocal<CreationScope.Phase?> _delegationPhase = new();
    [ThreadStatic] private static List<CreationScope>? _physicalDelegationScopes;

    private TaskExecutionCoordinator OriginalDelegationTasks => _originalTasks is { } read
        ? RunDelegationCallback(read)
        : throw new InvalidOperationException("The actual canonical Task issuer is unavailable for child delegation.");

    public async ValueTask<ITaskRunDelegationOriginal> CaptureOriginalDelegationAsync(
        TaskRunAttemptAdmission sameOriginalParent, TaskExecutionSnapshot currentParent,
        TaskRunDelegationIntent acknowledgedIntent, TaskExecutionSnapshot proposedChild, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(acknowledgedIntent);
        var actual = await DemandDelegationParentAsync(sameOriginalParent, currentParent, token).ConfigureAwait(false);
        var intent = FindCurrentIntent(actual, acknowledgedIntent);
        if (intent.State != TaskRunDelegationState.IntentAcknowledged || intent.CreatedByAttemptId != sameOriginalParent.AttemptId)
            throw new UnauthorizedAccessException("Initial child capture requires its actual creating parent attempt and intent acknowledgement.");
        var child = DetachChild(proposedChild);
        DemandChildIntent(actual, intent, child);
        var detached = intent with { RequestedPermissionScopes = Array.AsReadOnly(intent.RequestedPermissionScopes.ToArray()) };
        var fingerprint = ChildFingerprint(child);
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var owner = RequireOwner(actual);
            var key = (actual.TaskId, intent.Id);
            if (_delegations.TryGetValue(key, out var old))
            {
                if (!ReferenceEquals(old.ParentOwner, owner) || !SameIntent(old.OriginalIntent, detached) || old.ChildFingerprint != fingerprint)
                    throw new UnauthorizedAccessException("An issued child intent cannot be replaced.");
                return old;
            }
            if (_delegations.Keys.Count(key => key.Task == actual.TaskId) >= 128)
                throw new InvalidOperationException("Retained original child intent capacity exhausted.");
            var original = new DelegationOriginal(owner, detached, fingerprint);
            _delegations.Add(key, original); // Task-owned, independent of the creator's provider lease.
            return original;
        }
    }

    public async ValueTask<ITaskRunDelegationAdmission> AuthorizeOriginalChildAsync(
        ITaskRunDelegationOriginal sameTaskOwnedOriginal, TaskRunAttemptAdmission sameCurrentParent,
        TaskExecutionSnapshot currentParent, TaskExecutionSnapshot proposedChild, CancellationToken token)
    {
        var original = RequireDelegationOriginal(sameTaskOwnedOriginal);
        var actual = await DemandDelegationParentAsync(sameCurrentParent, currentParent, token).ConfigureAwait(false);
        var intent = FindCurrentIntent(actual, original.OriginalIntent);
        if (intent.State != TaskRunDelegationState.IntentAcknowledged)
            throw new UnauthorizedAccessException("A linked/completed child cannot be created again.");
        var child = DetachChild(proposedChild);
        DemandChildIntent(actual, intent, child);
        if (ChildFingerprint(child) != original.ChildFingerprint)
            throw new UnauthorizedAccessException("The original fixed child input changed.");
        lock (_sync)
        {
            RequireDelegationOriginal(original);
            if (!ReferenceEquals(RequireOwner(actual), original.ParentOwner) || original.HasUnknownScope)
                throw new UnauthorizedAccessException("Original parent issuance or child creation scope is unavailable.");
            if (original.ChildOwner is null)
            {
                if (_owners.ContainsKey(intent.ChildTaskId) || _delegatedChildren.ContainsKey(intent.ChildTaskId))
                    throw new UnauthorizedAccessException("Fixed child identity already belongs to another original.");
                if (_owners.Count >= 1024) throw new InvalidOperationException("Retained task authority capacity exhausted.");
                var parent = original.ParentOwner.Binding;
                var binding = new TaskExecutionOwnerBinding(intent.ChildTaskId, intent.ChildContextId, intent.ChildExecutionId,
                    parent.ActorId, parent.ProfileId, parent.AccountId, parent.OrganisationId, parent.AuthenticationRevision,
                    "task-child-owner:" + Guid.NewGuid().ToString("N"));
                var owner = new Owner(binding);
                original.ChildOwner = owner;
                _owners.Add(binding.TaskId, owner);
                _delegatedChildren.Add(binding.TaskId, original);
            }
            original.Scopes.RemoveAll(static scope => scope.IsHealthyClosed);
            if (original.Scopes.Count >= 128)
                throw new InvalidOperationException("Retained child creation scope capacity exhausted.");
            var scope = new CreationScope(this, original, sameCurrentParent, actual, child);
            original.Scopes.Add(scope); // Owned before returning or acquiring any parent pin.
            return scope;
        }
    }

    private DelegationOriginal RequireDelegationOriginal(ITaskRunDelegationOriginal original)
    {
        ArgumentNullException.ThrowIfNull(original);
        lock (_sync)
        {
            if (original is not DelegationOriginal actual ||
                !ReferenceEquals(_delegations.GetValueOrDefault((actual.ParentTaskId, actual.OriginalIntent.Id)), actual) ||
                !ReferenceEquals(_owners.GetValueOrDefault(actual.ParentTaskId), actual.ParentOwner))
                throw new UnauthorizedAccessException("The same live Task-owned delegation issuance is required.");
            return actual;
        }
    }

    private async Task<TaskExecutionSnapshot> DemandDelegationParentAsync(TaskRunAttemptAdmission original,
        TaskExecutionSnapshot observed, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(observed);
        var owner = RequireOwner(observed);
        if (original.Lease is not Lease || !IsIssuedOriginal(original.Lease) ||
            original.Lease.Owner != owner.Binding || original.AttemptId != original.Lease.AttemptId)
            throw new UnauthorizedAccessException("Actual parent attempt issuance is required.");
        var tasks = OriginalDelegationTasks;
        var issuedTask = RunDelegationCallback(() => tasks.TryGetIssuedAttemptAsync(observed.TaskId,
            observed.ExecutionId, original.AttemptId, token), token);
        if (!ReferenceEquals(await ObserveOriginalPermissionReadAsync(issuedTask).ConfigureAwait(false), original))
            throw new UnauthorizedAccessException("A copied, retired or superseded parent admission cannot delegate.");
        var currentTask = RunDelegationCallback(() => tasks.GetAsync(observed.TaskId, token), token);
        var current = await ObserveOriginalPermissionReadAsync(currentTask).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current parent Task is unavailable.");
        if (current.TaskId != observed.TaskId || current.ContextId != observed.ContextId ||
            current.ExecutionId != observed.ExecutionId || current.OwnerBinding != owner.Binding ||
            current.PersistenceRevision < observed.PersistenceRevision ||
            current.State != TaskExecutionLifecycle.Running || current.RecoveryObservation is not null ||
            current.Attempts.LastOrDefault() is not { } attempt || attempt.Id != original.AttemptId ||
            attempt.State is not (TaskRunAttemptState.Admitted or TaskRunAttemptState.Running) ||
            current.Plan.Any(node => node.State is TaskPlanNodeState.RequiresReexecution or TaskPlanNodeState.WaitingSafeBoundary))
            throw new UnauthorizedAccessException("Current parent run/attempt or an unresolved action refuses delegation.");
        // Last repository await precedes the fresh actual parent actor/model/cloud-policy read.
        var validation = RunDelegationCallback(() => original.Lease.RevalidateAsync(token).AsTask(), token);
        await ObserveOriginalPermissionReadAsync(validation).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (_sync)
            if (!ReferenceEquals(_owners.GetValueOrDefault(current.TaskId), owner))
                throw new UnauthorizedAccessException("Parent Task owner retired during delegation validation.");
        return current;
    }

    private async Task DemandDelegatedParentAsync(Owner childOwner, CancellationToken token)
    {
        DelegationOriginal? original;
        lock (_sync) original = _delegatedChildren.GetValueOrDefault(childOwner.Binding.TaskId);
        if (original is null) return; // Ordinary free/local Task authority is unchanged.
        RequireDelegationOriginal(original);
        if (!ReferenceEquals(original.ChildOwner, childOwner) || original.HasUnknownScope)
            throw new UnauthorizedAccessException("Actual child issuance or creation settlement is unavailable.");
        var tasks = OriginalDelegationTasks;
        var childTask = RunDelegationCallback(() => tasks.GetAsync(childOwner.Binding.TaskId, token), token);
        var child = await ObserveOriginalPermissionReadAsync(childTask).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current acknowledged child is unavailable.");
        var parentTask = RunDelegationCallback(() => tasks.GetAsync(original.ParentTaskId, token), token);
        var parent = await ObserveOriginalPermissionReadAsync(parentTask).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current linked parent is unavailable.");
        var intent = FindCurrentIntent(parent, original.OriginalIntent);
        if (intent.State != TaskRunDelegationState.ChildLinked || intent.AcknowledgedChildRevision is not > 0 ||
            child.PersistenceRevision < intent.AcknowledgedChildRevision || child.OwnerBinding != childOwner.Binding ||
            child.TaskId != original.OriginalIntent.ChildTaskId || child.ContextId != original.OriginalIntent.ChildContextId ||
            child.ExecutionId != original.OriginalIntent.ChildExecutionId || !SameParentLink(child.ParentDelegation, original) ||
            child.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled or TaskExecutionLifecycle.Failed)
            throw new UnauthorizedAccessException("An actual live acknowledged parent-child link is required for child dispatch.");
        var latest = parent.Attempts.LastOrDefault()
            ?? throw new UnauthorizedAccessException("Current parent provider attempt is unavailable.");
        var issuedTask = RunDelegationCallback(() => tasks.TryGetIssuedAttemptAsync(parent.TaskId, parent.ExecutionId, latest.Id, token), token);
        var fresh = await ObserveOriginalPermissionReadAsync(issuedTask).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current parent original cannot be reconstructed from its persisted link.");
        await DemandDelegationParentAsync(fresh, parent, token).ConfigureAwait(false);
    }

    // Narrowing is deny-only. Child scope observations never replace fresh central/model
    // permission or the physical owner's final fence. Ordinary Task intent stays unchanged.
    internal void DemandOriginalToolIntent(ITaskRunAdmissionLease actualLease, string exactToolName)
    {
        if (!IsIssuedOriginal(actualLease)) throw new UnauthorizedAccessException("Actual Task lease required.");
        lock (_sync)
        {
            if (_delegatedChildren.TryGetValue(actualLease.Owner.TaskId, out var original) &&
                (original.HasUnknownScope || !original.OriginalIntent.RequestedPermissionScopes.Contains(
                    "workspace:" + exactToolName, StringComparer.OrdinalIgnoreCase)))
                throw new UnauthorizedAccessException("This child intent did not request the original workspace tool.");
        }
    }

    private static TaskRunDelegationIntent FindCurrentIntent(TaskExecutionSnapshot parent, TaskRunDelegationIntent expected)
    {
        var intents = parent.Delegations.Where(intent => intent.Id == expected.Id).Take(2).ToArray();
        if (intents.Length != 1 || !SameIntent(intents[0], expected))
            throw new UnauthorizedAccessException("The original acknowledged child intent is no longer current.");
        return intents[0];
    }
    private static bool SameIntent(TaskRunDelegationIntent first, TaskRunDelegationIntent second) =>
        first.Id == second.Id && first.ParentTaskId == second.ParentTaskId && first.ParentExecutionId == second.ParentExecutionId &&
        first.CreatedByAttemptId == second.CreatedByAttemptId && first.ChildTaskId == second.ChildTaskId &&
        first.ChildContextId == second.ChildContextId && first.ChildExecutionId == second.ChildExecutionId &&
        first.RequestKey == second.RequestKey && first.OriginalRequestDigest == second.OriginalRequestDigest &&
        first.PromptSummary == second.PromptSummary && first.CreatedAt == second.CreatedAt &&
        first.RequestedPermissionScopes.SequenceEqual(second.RequestedPermissionScopes, StringComparer.OrdinalIgnoreCase);
    private static bool SameParentLink(TaskRunParentDelegation? link, DelegationOriginal original) =>
        link is not null && link.ParentTaskId == original.ParentTaskId && link.ParentContextId == original.ParentContextId &&
        link.ParentExecutionId == original.ParentExecutionId && link.DelegationId == original.OriginalIntent.Id;
    private static void DemandChildIntent(TaskExecutionSnapshot parent, TaskRunDelegationIntent intent, TaskExecutionSnapshot child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (intent.Id == Guid.Empty || intent.ParentTaskId != parent.TaskId || intent.ParentExecutionId != parent.ExecutionId ||
            intent.ChildTaskId == Guid.Empty || intent.ChildContextId == Guid.Empty || intent.ChildExecutionId == Guid.Empty ||
            intent.ChildTaskId == parent.TaskId || intent.ChildExecutionId == parent.ExecutionId ||
            string.IsNullOrWhiteSpace(intent.RequestKey) || intent.RequestKey.Length > 256 ||
            intent.OriginalRequestDigest.Length != 64 || !intent.OriginalRequestDigest.All(Uri.IsHexDigit) ||
            intent.RequestedPermissionScopes.Count > 128 || intent.RequestedPermissionScopes.Any(scope =>
                string.IsNullOrWhiteSpace(scope) || scope.Length > 256 ||
                !parent.ApprovedPermissionScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)) ||
            child.TaskId != intent.ChildTaskId || child.ContextId != intent.ChildContextId || child.ExecutionId != intent.ChildExecutionId ||
            child.OwnerBinding is not null || child.PersistenceRevision != 0 || child.State != TaskExecutionLifecycle.Running ||
            child.PlanVersion != 1 || child.Plan.Count != 0 || child.Steers.Count != 0 || child.Queue.Count != 0 ||
            child.Attempts.Count != 0 || child.RecoveryObservation is not null || child.RecoveryHistory.Count != 0 ||
            child.CheckpointId is not null || child.LastCheckpointActionId is not null || child.Delegations.Count != 0 ||
            child.PromptSummary != intent.PromptSummary || !child.ApprovedPermissionScopes.SequenceEqual(intent.RequestedPermissionScopes, StringComparer.OrdinalIgnoreCase) ||
            child.ParentDelegation is not { } link || link.ParentTaskId != parent.TaskId || link.ParentContextId != parent.ContextId ||
            link.ParentExecutionId != parent.ExecutionId || link.DelegationId != intent.Id)
            throw new UnauthorizedAccessException("The fixed acknowledged child input or narrowed scope intent changed.");
    }
    private static TaskExecutionSnapshot DetachChild(TaskExecutionSnapshot child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return child with
        {
            ApprovedPermissionScopes = Array.AsReadOnly(child.ApprovedPermissionScopes.ToArray()),
            Plan = Array.AsReadOnly(child.Plan.ToArray()), Steers = Array.AsReadOnly(child.Steers.ToArray()),
            Queue = Array.AsReadOnly(child.Queue.ToArray()), Attempts = Array.AsReadOnly(child.Attempts.ToArray()),
            Delegations = Array.AsReadOnly(child.Delegations.ToArray()), RecoveryHistory = Array.AsReadOnly(child.RecoveryHistory.ToArray())
        };
    }
    private static string ChildFingerprint(TaskExecutionSnapshot child) => Digest(JsonSerializer.SerializeToElement(child));

    // The actual finite source callbacks retain a physical stack even if a callback restores
    // another ExecutionContext. The async marker is a live parent chain, not a stale task ID.
    private T RunDelegationCallback<T>(Func<T> callback, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); // Caller cancellation BEFORE a producer is invoked.
        var added = new List<CreationScope>();
        var physical = _physicalDelegationScopes ??= [];
        for (var phase = _delegationPhase.Value; phase is not null; phase = phase.Parent)
            if (phase.Active) { physical.Add(phase.Owner); added.Add(phase.Owner); }
        try { return callback(); }
        catch (OperationCanceledException error)
        { throw new AggregateException("Actual child authority synchronous callback fault.", error); }
        finally { for (var i = 0; i < added.Count; i++) physical.RemoveAt(physical.Count - 1); }
    }

    private sealed class DelegationOriginal(Owner parent, TaskRunDelegationIntent intent, string fingerprint) : ITaskRunDelegationOriginal
    {
        public Owner ParentOwner { get; } = parent;
        public Guid ParentTaskId => ParentOwner.Binding.TaskId;
        public Guid ParentContextId => ParentOwner.Binding.ContextId;
        public Guid ParentExecutionId => ParentOwner.Binding.ExecutionId;
        public TaskRunDelegationIntent OriginalIntent { get; } = intent;
        public string ChildFingerprint { get; } = fingerprint;
        public Owner? ChildOwner { get; set; }
        public volatile bool HasUnknownScope;
        public List<CreationScope> Scopes { get; } = [];
    }

    private sealed class CreationScope : ITaskRunDelegationAdmission
    {
        private readonly TaskRunPermissionAuthority _issuer;
        private readonly DelegationOriginal _original;
        private readonly TaskExecutionSnapshot _parent;
        private readonly TaskExecutionSnapshot _child;
        private readonly object _gate = new();
        private readonly SemaphoreSlim _pins = new(1, 1);
        private readonly List<Task> _validations = [];
        private readonly List<Task> _pinCloses = [];
        private readonly List<Exception> _pinErrors = [];
        private bool _closing;
        private Task? _close;
        public ITaskRunDelegationOriginal OriginalDelegation => _original;
        public TaskRunAttemptAdmission OriginalParentAttempt { get; }
        public TaskExecutionOwnerBinding ChildOwner => _original.ChildOwner!.Binding;
        internal bool IsHealthyClosed { get { lock (_gate) return _close?.IsCompletedSuccessfully == true; } }
        internal sealed class Phase(CreationScope owner, Phase? parent)
        {
            internal CreationScope Owner { get; } = owner;
            internal Phase? Parent { get; } = parent;
            internal volatile bool Active = true;
        }
        internal CreationScope(TaskRunPermissionAuthority issuer, DelegationOriginal original,
            TaskRunAttemptAdmission parent, TaskExecutionSnapshot current, TaskExecutionSnapshot child)
        { _issuer = issuer; _original = original; OriginalParentAttempt = parent; _parent = current; _child = child; }

        public ValueTask RevalidateAsync(CancellationToken token)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task task;
            lock (_gate)
            {
                if (_closing) throw new ObjectDisposedException(nameof(CreationScope));
                _validations.RemoveAll(static task => task.IsCompletedSuccessfully);
                if (_validations.Count >= 128) throw new InvalidOperationException("Retained child validation capacity exhausted.");
                task = RevalidatePublishedAsync(start.Task, token);
                _validations.Add(task); // Published before repository/actor callbacks.
            }
            start.SetResult();
            return new(task);
        }
        private async Task RevalidatePublishedAsync(Task start, CancellationToken token)
        {
            await start.ConfigureAwait(false);
            var phase = new Phase(this, _issuer._delegationPhase.Value);
            _issuer._delegationPhase.Value = phase;
            try
            {
                _issuer.RequireDelegationOriginal(_original);
                var current = await _issuer.DemandDelegationParentAsync(OriginalParentAttempt, _parent, token).ConfigureAwait(false);
                var intent = FindCurrentIntent(current, _original.OriginalIntent);
                if (intent.State != TaskRunDelegationState.IntentAcknowledged || _original.HasUnknownScope ||
                    ChildFingerprint(_child) != _original.ChildFingerprint)
                    throw new UnauthorizedAccessException("Child creation original retired or changed.");
                DemandChildIntent(current, intent, _child);
                lock (_gate) if (_closing) throw new ObjectDisposedException(nameof(CreationScope));
            }
            finally { phase.Active = false; _issuer._delegationPhase.Value = phase.Parent; }
        }

        public async ValueTask<IAsyncDisposable?> AcquireOriginalParentCommitPinAsync(CancellationToken token)
        {
            await _pins.WaitAsync(token).ConfigureAwait(false);
            lock (_gate) if (_closing) { _pins.Release(); return null; }
            Task<IAsyncDisposable?>? actual = null;
            try
            {
                // Pure borrowed attempt lifetime pin only. NO task/actor/policy/repository reads here.
                actual = ((ITaskRunAdmissionCommitLease)OriginalParentAttempt.Lease).AcquireOriginalCommitPinAsync(token).AsTask();
                var pin = await ObserveOriginalPermissionReadAsync(actual).ConfigureAwait(false);
                if (pin is null) { _pins.Release(); return null; }
                return new CreationPin(this, pin);
            }
            catch (Exception error)
            {
                lock (_gate) CaptureDelegationErrors(_pinErrors, actual, error);
                lock (_issuer._sync) _original.HasUnknownScope = true;
                _pins.Release();
                if (actual?.IsFaulted == true) ExceptionDispatchInfo.Capture(actual.Exception!).Throw();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            for (var phase = _issuer._delegationPhase.Value; phase is not null; phase = phase.Parent)
                if (phase.Active && ReferenceEquals(phase.Owner, this))
                    throw new InvalidOperationException("Child validation must return before its external owner joins creation scope close.");
            if (_physicalDelegationScopes?.Any(scope => ReferenceEquals(scope, this)) == true)
                throw new InvalidOperationException("A child source callback cannot join its own creation scope.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task actualClose;
            lock (_gate)
            {
                if (_close is not null) return new(_close);
                _closing = true;
                actualClose = ClosePublishedAsync(start.Task, _validations.ToArray());
                _close = actualClose; // SAME whole driver published before any cleanup callback.
            }
            start.SetResult();
            return new(actualClose);
        }
        private async Task ClosePublishedAsync(Task start, Task[] validations)
        {
            await start.ConfigureAwait(false);
            var errors = new List<Exception>();
            foreach (var actual in validations)
                try { await actual.ConfigureAwait(false); } catch (Exception error) { CaptureDelegationErrors(errors, actual, error); }
            try
            {
                await _pins.WaitAsync().ConfigureAwait(false);
                try { lock (_gate) foreach (var error in _pinErrors) AddDelegationError(errors, error); }
                finally { _pins.Release(); }
            }
            catch (Exception error) { AddDelegationError(errors, error); }
            Task[] pinCloses;
            lock (_gate) pinCloses = _pinCloses.ToArray();
            // Raw pin gate release precedes its driver's final return. Join the SAME driver too.
            foreach (var actual in pinCloses)
                try { await actual.ConfigureAwait(false); } catch (Exception error) { CaptureDelegationErrors(errors, actual, error); }
            if (errors.Count > 0)
            {
                lock (_issuer._sync) _original.HasUnknownScope = true;
                throw new AggregateException("Original child creation scope failed.", errors);
            }
        }
        private sealed class CreationPin(CreationScope scope, IAsyncDisposable original) : IAsyncDisposable
        {
            private readonly object _gate = new();
            private Task? _close;
            public ValueTask DisposeAsync()
            {
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task actualClose;
                lock (_gate)
                {
                    if (_close is not null) return new(_close);
                    actualClose = CloseOriginalPinAsync(start.Task);
                    _close = actualClose;
                    lock (scope._gate) scope._pinCloses.Add(actualClose); // Retained before release or callback.
                }
                start.SetResult();
                return new(actualClose);
            }
            private async Task CloseOriginalPinAsync(Task start)
            {
                await start.ConfigureAwait(false);
                Task? actual = null;
                var errors = new List<Exception>();
                try
                {
                    actual = original.DisposeAsync().AsTask();
                    await ObserveOriginalPermissionReadAsync(actual).ConfigureAwait(false);
                }
                catch (Exception error) { CaptureDelegationErrors(errors, actual, error); }
                finally
                {
                    // Publish known disposal faults BEFORE opening the scope close gate.
                    // Its waiting close cannot certify success between Release and cause custody.
                    if (errors.Count > 0)
                    {
                        lock (scope._gate) foreach (var error in errors) AddDelegationError(scope._pinErrors, error);
                        lock (scope._issuer._sync) scope._original.HasUnknownScope = true;
                    }
                    try { scope._pins.Release(); }
                    catch (Exception error)
                    {
                        AddDelegationError(errors, error);
                        lock (scope._gate) AddDelegationError(scope._pinErrors, error);
                        lock (scope._issuer._sync) scope._original.HasUnknownScope = true;
                    }
                }
                // Terminal notification follows both real pin disposal and independent scope cleanup.
                if (errors.Count > 0) throw new AggregateException("Original parent pin disposal failed.", errors);
            }
        }
    }
    private static void CaptureDelegationErrors(List<Exception> errors, Task? actual, Exception caught)
    {
        if (actual?.Exception is { InnerExceptions.Count: > 0 } group)
            foreach (var cause in group.InnerExceptions) AddDelegationError(errors, cause);
        else AddDelegationError(errors, caught);
    }
    private static void AddDelegationError(List<Exception> errors, Exception cause)
    { if (!errors.Any(error => ReferenceEquals(error, cause))) errors.Add(cause); }
}
