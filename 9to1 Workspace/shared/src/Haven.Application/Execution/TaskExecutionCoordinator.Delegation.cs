using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

/// <summary>Actual source-issued intent acknowledgement. Public snapshots are observations, never grants.</summary>
public sealed class TaskRunDelegationIntentAcknowledgment
{
    internal TaskRunDelegationIntentAcknowledgment(TaskExecutionCoordinator issuer, TaskRunDelegationOriginal original,
        TaskExecutionSnapshot parent, TaskRunDelegationIntent intent)
    { Issuer = issuer; Original = original; Parent = parent; Intent = intent; }
    internal TaskExecutionCoordinator Issuer { get; }
    internal TaskRunDelegationOriginal Original { get; }
    public TaskExecutionSnapshot Parent { get; }
    public TaskRunDelegationIntent Intent { get; }
}

/// <summary>Only the actual child CAS and actual creation-scope close can issue this process-local object.</summary>
public sealed class TaskRunDelegatedChildCreationAcknowledgment
{
    internal TaskRunDelegatedChildCreationAcknowledgment(TaskExecutionCoordinator issuer, TaskRunDelegationOriginal original,
        TaskExecutionSnapshot child)
    { Issuer = issuer; Original = original; Child = child; }
    internal TaskExecutionCoordinator Issuer { get; }
    internal TaskRunDelegationOriginal Original { get; }
    public TaskExecutionSnapshot Child { get; }
}

/// <summary>Actual parent-link acknowledgement. Dispatch still needs the private original input and fresh child authority.</summary>
public sealed class TaskRunDelegatedChildLinkAcknowledgment
{
    internal TaskRunDelegatedChildLinkAcknowledgment(TaskExecutionCoordinator issuer, TaskRunDelegationOriginal original,
        TaskExecutionSnapshot parent, TaskExecutionSnapshot child)
    { Issuer = issuer; Original = original; Parent = parent; Child = child; }
    internal TaskExecutionCoordinator Issuer { get; }
    internal TaskRunDelegationOriginal Original { get; }
    public TaskExecutionSnapshot Parent { get; }
    public TaskExecutionSnapshot Child { get; }
}

/// <summary>Genuine whole child producer completion plus the SAME parent's acknowledgement, never durable-status inference.</summary>
public sealed class TaskRunDelegatedChildCompletionAcknowledgment
{
    internal TaskRunDelegatedChildCompletionAcknowledgment(TaskExecutionCoordinator issuer, TaskRunDelegationOriginal original,
        TaskExecutionSnapshot parent, TaskExecutionSnapshot child)
    { Issuer = issuer; Original = original; Parent = parent; Child = child; }
    internal TaskExecutionCoordinator Issuer { get; }
    internal TaskRunDelegationOriginal Original { get; }
    public TaskExecutionSnapshot Parent { get; }
    public TaskExecutionSnapshot Child { get; }
}

/// <summary>Actual source Task custody only. The single TaskExecutionSnapshot remains the durable authority.</summary>
internal sealed class TaskRunDelegationOriginal(TaskExecutionCoordinator issuer, TaskRunAttemptAdmission originalParent,
    TaskRunDelegationIntent proposed)
{
    internal readonly object Gate = new();
    internal readonly TaskExecutionCoordinator Issuer = issuer;
    internal readonly TaskRunAttemptAdmission InitialParent = originalParent;
    internal readonly TaskRunDelegationIntent Proposed = proposed;
    internal readonly AgentRuntimeOriginalCustody IntentOperation = new();
    internal readonly AgentRuntimeOriginalCustody CreationOperation = new();
    internal readonly List<AgentRuntimeOriginalCustody> LinkOperations = [];
    internal Task<TaskRunDelegationIntentAcknowledgment>? IntentTask;
    internal Task<TaskRunDelegatedChildCreationAcknowledgment>? CreationTask;
    internal Task? OriginalIntentWrite;
    internal Task? OriginalChildWrite;
    internal Task<ITaskRunDelegationOriginal>? OriginalAuthorityCapture;
    internal readonly List<Task> OriginalParentLinkWrites = [];
    internal Task? OriginalPinClose;
    internal Task? OriginalScopeClose;
    internal TaskExecutionSnapshot? AcknowledgedParent;
    internal TaskExecutionSnapshot? AcknowledgedChild;
    internal ITaskRunDelegationOriginal? AuthorityOriginal;
    internal TaskRunDelegationIntentAcknowledgment? IntentReceipt;
    internal TaskRunDelegatedChildCreationAcknowledgment? CreationReceipt;
    internal TaskRunDelegatedChildLinkAcknowledgment? LinkReceipt;
    internal ChatOriginalAgentInvocation? ActualChildInvocation;
    internal TaskRunDelegatedAgentInput? ActualChildInput;
    internal readonly List<AgentRuntimeOriginalCustody> CompletionOperations = [];
    internal TaskRunDelegatedChildCompletionAcknowledgment? CompletionReceipt;
}

/// <summary>Private custody of actual child creation/link and its original input binding.
/// Never reconstructed from a child row, empty attempt list or public context observation.</summary>
internal sealed class TaskRunDelegatedUnstartedInvocationWitness(TaskExecutionCoordinator issuer,
    TaskRunDelegationOriginal original, TaskRunDelegatedChildLinkAcknowledgment link,
    ChatOriginalAgentInvocation initial, TaskRunDelegatedAgentInput input, AgentRuntimeOriginalCustody binding)
{
    internal readonly TaskExecutionCoordinator Issuer = issuer;
    internal readonly TaskRunDelegationOriginal Original = original;
    internal readonly TaskRunDelegatedChildLinkAcknowledgment Link = link;
    internal readonly ChatOriginalAgentInvocation Initial = initial;
    internal readonly TaskRunDelegatedAgentInput Input = input;
    internal readonly AgentRuntimeOriginalCustody Binding = binding;
}

public sealed partial class TaskExecutionCoordinator
{
    private readonly object _delegationAdmissionGate = new();
    private readonly ConcurrentDictionary<(Guid ParentTask, string RequestKey), TaskRunDelegationOriginal> _delegationOriginals = new();

    /// <summary>Acknowledges fixed child identities in the SAME parent before any child creation can be invoked.</summary>
    public Task<TaskRunDelegationIntentAcknowledgment> RegisterOriginalDelegationIntentAsync(
        TaskRunAttemptAdmission sameParent, string requestKey, string originalRequestDigest,
        string promptSummary, IReadOnlyCollection<string>? requestedPermissionScopes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameParent);
        if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 256
            || string.IsNullOrWhiteSpace(originalRequestDigest) || originalRequestDigest.Length != 64 || originalRequestDigest.Any(value => !Uri.IsHexDigit(value)))
            throw new ArgumentException("An exact bounded delegation request key and SHA256 input digest are required.");
        var scopes = Array.AsReadOnly(NormalizeScopes(requestedPermissionScopes).ToArray());
        var summary = SensitiveTextRedactor.Redact(promptSummary, 240);
        var now = _time.GetUtcNow();
        TaskRunDelegationOriginal actual;
        lock (_delegationAdmissionGate)
        {
            var key = (sameParent.Snapshot.TaskId, requestKey);
            if (_delegationOriginals.TryGetValue(key, out var retained))
            {
                if (!ReferenceEquals(retained.InitialParent, sameParent)
                    || retained.InitialParent.Snapshot.ContextId != sameParent.Snapshot.ContextId
                    || retained.InitialParent.Snapshot.ExecutionId != sameParent.Snapshot.ExecutionId)
                    throw new InvalidOperationException("Only the SAME privately issued parent may retrieve its original intent; copied IDs are not custody.");
                if (retained.Proposed.OriginalRequestDigest != originalRequestDigest
                    || retained.Proposed.PromptSummary != summary
                    || !retained.Proposed.RequestedPermissionScopes.SequenceEqual(scopes, StringComparer.Ordinal))
                    throw new InvalidOperationException("A pending delegation key cannot be reused for different original work.");
                return retained.IntentTask ?? throw new InvalidOperationException("The actual delegation registration is not published.");
            }
            if (_delegationOriginals.Count >= 128)
                throw new InvalidOperationException("Failed or unknown child creation originals require owning-service inspection.");
            var intent = new TaskRunDelegationIntent(Guid.NewGuid(), sameParent.Snapshot.TaskId,
                sameParent.Snapshot.ExecutionId, sameParent.AttemptId,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), requestKey, originalRequestDigest,
                summary, scopes, TaskRunDelegationState.IntentAcknowledged, now, now);
            actual = new(this, sameParent, intent);
            _delegationOriginals[key] = actual;
            // Start publishes the SAME original driver before actor/repository/issuer callbacks.
            actual.IntentTask = actual.IntentOperation.Start(operation => RegisterDelegationIntentBodyAsync(actual, operation, token));
        }
        return actual.IntentTask;
    }

    private async Task<TaskRunDelegationIntentAcknowledgment> RegisterDelegationIntentBodyAsync(
        TaskRunDelegationOriginal original, AgentRuntimeOriginalCustody operation, CancellationToken token)
    {
        var parent = await RequireDelegationSnapshotAsync(original.Proposed.ParentTaskId, operation, token).ConfigureAwait(false);
        RequireRun(parent, original.Proposed.ParentExecutionId);
        var live = await RequireOriginalDelegationParentAsync(parent, operation, token).ConfigureAwait(false);
        if (!ReferenceEquals(live, original.InitialParent))
            throw new InvalidOperationException("Intent registration requires the exact actual current parent admission.");
        if (parent.Delegations.Any(intent => intent.RequestKey == original.Proposed.RequestKey))
            throw new InvalidOperationException("Historical delegation intent exists without this original source acknowledgement; reconstruction is unavailable.");
        if (original.Proposed.RequestedPermissionScopes.Except(parent.ApprovedPermissionScopes, StringComparer.OrdinalIgnoreCase).Any())
            throw new InvalidOperationException("Child requested scope intent cannot be broader than the parent's recorded scope intent.");
        await operation.AwaitAsync(() => live.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        parent = await RequireDelegationSnapshotAsync(parent.TaskId, operation, token).ConfigureAwait(false);
        RequireRun(parent, original.Proposed.ParentExecutionId);
        if (!ReferenceEquals(await RequireOriginalDelegationParentAsync(parent, operation, token).ConfigureAwait(false), live))
            throw new InvalidOperationException("The genuine current parent attempt changed before intent acknowledgement.");
        await operation.AwaitAsync(() => ValidateTaskCommandAsync(parent, "task:delegate:intent", token)).ConfigureAwait(false);
        var lease = live.Lease as ITaskRunAdmissionCommitLease
            ?? throw new InvalidOperationException("The actual parent lifetime commit pin is unavailable.");
        IAsyncDisposable? pin = null;
        TaskExecutionSnapshot? acknowledged = null;
        var causes = new List<Exception>();
        try
        {
            pin = await operation.AwaitAsync(() => lease.AcquireOriginalCommitPinAsync(token).AsTask()).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original parent lifetime pin was refused.");
            acknowledged = await operation.AwaitAsync(() => PersistOriginalWriteOnlyAsync(parent with
            {
                Delegations = Array.AsReadOnly(parent.Delegations.Append(original.Proposed).ToArray()),
                UpdatedAt = _time.GetUtcNow()
            }, token, actual => original.OriginalIntentWrite = actual)).ConfigureAwait(false);
        }
        catch (Exception cause) { causes.Add(cause); }
        finally
        {
            if (pin is not null)
                try { await operation.AwaitAsync(() => pin.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception cause) { causes.Add(cause); }
        }
        ThrowDelegationStageFailures(causes);
        if (acknowledged is null || original.OriginalIntentWrite is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("No actual parent intent write acknowledgement exists.");
        original.AcknowledgedParent = acknowledged;
        // Task-owned original is captured while the SAME creator admission is live. A later
        // authorized parent provider fallback may rebind creation using its fresh current lease.
        var authority = _admissionAuthority as ITaskRunDelegationAuthority
            ?? throw new InvalidOperationException("The genuine Task-owned delegation authority is unavailable.");
        original.AuthorityOriginal = await operation.AwaitAsync(() =>
        {
            var actualCapture = authority.CaptureOriginalDelegationAsync(live, acknowledged,
                acknowledged.Delegations.Single(intent => intent.Id == original.Proposed.Id),
                BuildProposedDelegatedChild(original, acknowledged), token).AsTask();
            original.OriginalAuthorityCapture = actualCapture;
            return actualCapture;
        }).ConfigureAwait(false);
        PublishAcknowledgedSnapshot(acknowledged); // Task-owned capture precedes observer-triggered attempt retirement.
        var receipt = new TaskRunDelegationIntentAcknowledgment(this, original, acknowledged, original.Proposed);
        original.IntentReceipt = receipt;
        return receipt;
    }

    /// <summary>Returns the SAME retained original creation; retries cannot allocate a replacement child.</summary>
    public Task<TaskRunDelegatedChildCreationAcknowledgment> CreateOriginalDelegatedChildAsync(
        TaskRunDelegationIntentAcknowledgment actualIntent, CancellationToken token)
    {
        var original = RequireOriginalDelegation(actualIntent.Issuer, actualIntent.Original);
        if (!ReferenceEquals(original.IntentReceipt, actualIntent)
            || original.IntentTask is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual intent acknowledgement is not complete.");
        lock (original.Gate)
            return original.CreationTask ??= original.CreationOperation.Start(operation => CreateDelegatedChildBodyAsync(original, operation, token));
    }

    private async Task<TaskRunDelegatedChildCreationAcknowledgment> CreateDelegatedChildBodyAsync(
        TaskRunDelegationOriginal original, AgentRuntimeOriginalCustody operation, CancellationToken token)
    {
        var authority = _admissionAuthority as ITaskRunDelegationAuthority
            ?? throw new InvalidOperationException("The genuine Task-owned delegation authority is unavailable.");
        var parent = await RequireDelegationSnapshotAsync(original.Proposed.ParentTaskId, operation, token).ConfigureAwait(false);
        RequireRun(parent, original.Proposed.ParentExecutionId);
        DemandExactDelegationIntent(parent, original.Proposed, TaskRunDelegationState.IntentAcknowledged);
        var live = await RequireOriginalDelegationParentAsync(parent, operation, token).ConfigureAwait(false);
        var proposedChild = BuildProposedDelegatedChild(original, parent);
        var authorityOriginal = original.AuthorityOriginal
            ?? throw new InvalidOperationException("The actual creator's Task-owned delegation capture is unavailable.");
        if (original.OriginalAuthorityCapture is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual original Task-owned authority capture is unresolved.");
        ITaskRunDelegationAdmission? scope = null;
        IAsyncDisposable? pin = null;
        TaskExecutionSnapshot? child = null;
        var causes = new List<Exception>();
        try
        {
            // Capture and fresh rebind validate the actual source original, never persisted receipt text.
            scope = await operation.AwaitAsync(() => authority.AuthorizeOriginalChildAsync(authorityOriginal,
                live, parent, proposedChild, token).AsTask()).ConfigureAwait(false);
            if (!ReferenceEquals(scope.OriginalDelegation, authorityOriginal)
                || !ReferenceEquals(scope.OriginalParentAttempt, live))
                throw new InvalidOperationException("The actual delegation issuer returned foreign original authority.");
            ValidateOwner(proposedChild, scope.ChildOwner);
            await operation.AwaitAsync(() => scope.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
            pin = await operation.AwaitAsync(() => scope.AcquireOriginalParentCommitPinAsync(token).AsTask()).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The actual child creation lifetime pin was refused.");
            child = await operation.AwaitAsync(() => PersistOriginalWriteOnlyAsync(proposedChild with
            { OwnerBinding = scope.ChildOwner }, token, actual => original.OriginalChildWrite = actual)).ConfigureAwait(false);
        }
        catch (Exception cause) { causes.Add(cause); }
        finally
        {
            if (pin is not null)
                try
                {
                    var actualPinClose = pin.DisposeAsync().AsTask();
                    original.OriginalPinClose = actualPinClose;
                    await operation.AwaitAsync(() => actualPinClose).ConfigureAwait(false);
                }
                catch (Exception cause) { causes.Add(cause); }
            if (scope is not null)
                try
                {
                    var actualScopeClose = scope.DisposeAsync().AsTask();
                    original.OriginalScopeClose = actualScopeClose;
                    await operation.AwaitAsync(() => actualScopeClose).ConfigureAwait(false);
                }
                catch (Exception cause) { causes.Add(cause); }
        }
        ThrowDelegationStageFailures(causes);
        if (child is null || original.OriginalChildWrite is not { IsCompletedSuccessfully: true }
            || original.OriginalPinClose is not { IsCompletedSuccessfully: true }
            || original.OriginalScopeClose is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual child CAS and whole creation scope are not acknowledged.");
        original.AcknowledgedChild = child;
        PublishAcknowledgedSnapshot(child);
        var receipt = new TaskRunDelegatedChildCreationAcknowledgment(this, original, child);
        original.CreationReceipt = receipt;
        return receipt;
    }

    /// <summary>Explicitly retries only this SAME child creation receipt after a lost parent link CAS.</summary>
    public Task<TaskRunDelegatedChildLinkAcknowledgment> LinkOriginalDelegatedChildAsync(
        TaskRunDelegatedChildCreationAcknowledgment actualCreation, CancellationToken token)
    {
        var original = RequireOriginalDelegation(actualCreation.Issuer, actualCreation.Original);
        if (!ReferenceEquals(actualCreation, original.CreationReceipt)
            || original.CreationTask is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual original child creation and cleanup are unavailable.");
        AgentRuntimeOriginalCustody operation;
        lock (original.Gate)
        {
            if (original.LinkOperations.Count >= 128)
                throw new InvalidOperationException("Original parent-link failures require owning-service inspection.");
            operation = new();
            original.LinkOperations.Add(operation);
        }
        return operation.Start(actual => LinkDelegatedChildBodyAsync(original, actualCreation, actual, token));
    }

    private async Task<TaskRunDelegatedChildLinkAcknowledgment> LinkDelegatedChildBodyAsync(
        TaskRunDelegationOriginal original, TaskRunDelegatedChildCreationAcknowledgment actualCreation,
        AgentRuntimeOriginalCustody operation, CancellationToken token)
    {
        var parent = await RequireDelegationSnapshotAsync(original.Proposed.ParentTaskId, operation, token).ConfigureAwait(false);
        RequireRun(parent, original.Proposed.ParentExecutionId);
        var live = await RequireOriginalDelegationParentAsync(parent, operation, token).ConfigureAwait(false);
        var intent = parent.Delegations.SingleOrDefault(value => value.Id == original.Proposed.Id)
            ?? throw new InvalidOperationException("The exact acknowledged parent intent is unavailable.");
        if (intent.State == TaskRunDelegationState.ChildLinked && original.LinkReceipt is { } previous)
        {
            DemandExactDelegationIntent(parent, original.Proposed, TaskRunDelegationState.ChildLinked);
            return previous; // Original acknowledged observation; never a fresh dispatch grant.
        }
        DemandExactDelegationIntent(parent, original.Proposed, TaskRunDelegationState.IntentAcknowledged);
        var child = await RequireDelegationSnapshotAsync(actualCreation.Child.TaskId, operation, token).ConfigureAwait(false);
        DemandUnstartedChildCreationBasis(actualCreation.Child, child);
        await operation.AwaitAsync(() => live.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        await operation.AwaitAsync(() => ValidateTaskCommandAsync(parent, "task:delegate:link", token)).ConfigureAwait(false);
        var linked = intent with { State = TaskRunDelegationState.ChildLinked,
            AcknowledgedChildRevision = child.PersistenceRevision, ObservedChildState = child.State, UpdatedAt = _time.GetUtcNow() };
        var acknowledged = await PersistDelegationParentWithOriginalPinAsync(parent with
        {
            Delegations = Array.AsReadOnly(parent.Delegations.Select(value => value.Id == linked.Id ? linked : value).ToArray()),
            UpdatedAt = _time.GetUtcNow()
        }, live, original, operation, token).ConfigureAwait(false);
        var receipt = new TaskRunDelegatedChildLinkAcknowledgment(this, original, acknowledged, child);
        original.LinkReceipt = receipt;
        return receipt;
    }

    internal ChatOriginalAgentInvocation CreateOriginalDelegatedChatProducer(
        TaskRunDelegatedChildLinkAcknowledgment actualLink, TaskRunDelegatedAgentInput actualInput,
        Func<ChatOriginalAgentInvocation> actualFactory)
    {
        var original = RequireOriginalDelegation(actualLink.Issuer, actualLink.Original);
        lock (original.Gate)
        {
            if (!ReferenceEquals(original.LinkReceipt, actualLink)
                || !original.LinkOperations.Any(operation => operation.Healthy)
                || original.Proposed.OriginalRequestDigest != actualInput.Digest)
                throw new InvalidOperationException("Only the actual child link acknowledgement and exact detached input can issue its producer.");
            if (original.ActualChildInvocation is { } previous)
            {
                if (!ReferenceEquals(original.ActualChildInput, actualInput))
                    throw new InvalidOperationException("The actual child input producer cannot be replaced or reconstructed.");
                return previous; // Its actual stream is still single-consumption, never replayed here.
            }
            original.ActualChildInput = actualInput;
            var actual = actualFactory();
            if (!ReferenceEquals(actual.Original.OriginalDelegatedChildLink, actualLink)
                || actual.OriginalConversation.Id != original.Proposed.ChildContextId)
                throw new InvalidOperationException("The actual child factory changed its original context or custody.");
            original.ActualChildInvocation = actual;
            return actual;
        }
    }

    private async Task<TaskExecutionSnapshot> BindOriginalDelegatedInvocationAsync(
        TaskRunInvocationCustody custody, TaskRunDelegatedChildLinkAcknowledgment actualLink,
        ProviderExecutionContext observation, Guid actualContextId, CancellationToken token)
    {
        var original = RequireOriginalDelegation(actualLink.Issuer, actualLink.Original);
        if (!ReferenceEquals(actualLink, original.LinkReceipt)
            || original.ActualChildInvocation is not { } actualProducer
            || !ReferenceEquals(actualProducer.Original, custody)
            || original.ActualChildInput is null)
            throw new InvalidOperationException("A copied or historical child projection cannot issue original input.");
        var operation = new AgentRuntimeOriginalCustody();
        var child = await RequireDelegationSnapshotAsync(actualLink.Child.TaskId, operation, token, custody).ConfigureAwait(false);
        DemandUnstartedChildCreationBasis(actualLink.Child, child);
        if (observation.TaskId != child.TaskId || observation.ContextId != child.ContextId
            || observation.ExecutionId != child.ExecutionId || observation.AttemptId is not null
            || actualContextId != child.ContextId)
            throw new InvalidOperationException("The original child input changed its fixed Task/context/run.");
        await ValidateOriginalChildCommandAsync(child).ConfigureAwait(false);
        child = await RequireDelegationSnapshotAsync(child.TaskId, operation, token, custody).ConfigureAwait(false);
        DemandUnstartedChildCreationBasis(actualLink.Child, child);
        // Final fresh child actor + live linked-parent authority follows the last repository await.
        await ValidateOriginalChildCommandAsync(child).ConfigureAwait(false);
        Task ValidateOriginalChildCommandAsync(TaskExecutionSnapshot current) => operation.AwaitAsync(() =>
        {
            var actual = ValidateTaskCommandAsync(current, "task:invoke-linked-original-child", token);
            custody.RetainAdditionalOriginal("delegation.child-command-authority", actual);
            return actual;
        });
        ReserveOriginalInvocation(custody);
        BindOriginalInvocation(custody, child);
        // This privately issued witness refers to the actual creation CAS, pin/scope close,
        // parent-link operations and the fresh child command originals retained above.
        // It deliberately does not set BoundByActualBegin or manufacture a Begin Task.
        custody.OriginalDelegatedNeverStarted = new(this, original, actualLink, actualProducer,
            original.ActualChildInput ?? throw new InvalidOperationException("The actual detached child input is unavailable."), operation);
        if (!HasOriginalNeverStartedBinding(custody, child))
            throw new InvalidOperationException("The actual child creation/link/input custody is not healthy and complete.");
        return child;
    }

    internal Task<TaskRunDelegatedChildCompletionAcknowledgment> AcknowledgeCompletedOriginalDelegatedChildAsync(
        TaskRunDelegatedChildLinkAcknowledgment actualLink, ChatOriginalAgentInvocation actualChild, CancellationToken token)
    {
        var original = RequireOriginalDelegation(actualLink.Issuer, actualLink.Original);
        if (!ReferenceEquals(original.LinkReceipt, actualLink) || !ReferenceEquals(original.ActualChildInvocation, actualChild)
            || !actualChild.HasAcknowledgedCompletion)
            throw new InvalidOperationException("Whole successful original child producer/cleanup/settlement/completion is required.");
        AgentRuntimeOriginalCustody operation;
        lock (original.Gate)
        {
            if (original.CompletionOperations.Count >= 128)
                throw new InvalidOperationException("Original child completion acknowledgement failures require owning inspection.");
            operation = new(); original.CompletionOperations.Add(operation);
        }
        return operation.Start(actual => AcknowledgeCompletedChildBodyAsync(original, actualChild, actual, token));
    }

    private async Task<TaskRunDelegatedChildCompletionAcknowledgment> AcknowledgeCompletedChildBodyAsync(
        TaskRunDelegationOriginal original, ChatOriginalAgentInvocation actualChild,
        AgentRuntimeOriginalCustody operation, CancellationToken token)
    {
        var childBasis = actualChild.AcknowledgedObservation
            ?? throw new InvalidOperationException("No original child completion observation exists.");
        var child = await RequireDelegationSnapshotAsync(childBasis.TaskId, operation, token).ConfigureAwait(false);
        RequireCompletionBasis(childBasis, child);
        if (child.State != TaskExecutionLifecycle.Completed || !actualChild.HasAcknowledgedCompletion)
            throw new InvalidOperationException("The actual child completion was changed or its original custody is unavailable.");
        var parent = await RequireDelegationSnapshotAsync(original.Proposed.ParentTaskId, operation, token).ConfigureAwait(false);
        RequireRun(parent, original.Proposed.ParentExecutionId);
        var live = await RequireOriginalDelegationParentAsync(parent, operation, token).ConfigureAwait(false);
        var state = parent.Delegations.Single(value => value.Id == original.Proposed.Id);
        if (state.State == TaskRunDelegationState.ChildCompleted && original.CompletionReceipt is { } previous)
        {
            DemandExactDelegationIntent(parent, original.Proposed, TaskRunDelegationState.ChildCompleted);
            return previous;
        }
        DemandExactDelegationIntent(parent, original.Proposed, TaskRunDelegationState.ChildLinked);
        await operation.AwaitAsync(() => live.Lease.RevalidateAsync(token).AsTask()).ConfigureAwait(false);
        await operation.AwaitAsync(() => ValidateTaskCommandAsync(parent, "task:delegate:accept-child-completion", token)).ConfigureAwait(false);
        var complete = state with { State = TaskRunDelegationState.ChildCompleted,
            AcknowledgedChildRevision = child.PersistenceRevision, ObservedChildState = child.State, UpdatedAt = _time.GetUtcNow() };
        var acknowledged = await PersistDelegationParentWithOriginalPinAsync(parent with
        {
            Delegations = Array.AsReadOnly(parent.Delegations.Select(value => value.Id == complete.Id ? complete : value).ToArray()),
            UpdatedAt = _time.GetUtcNow()
        }, live, original, operation, token).ConfigureAwait(false);
        var receipt = new TaskRunDelegatedChildCompletionAcknowledgment(this, original, acknowledged, child);
        original.CompletionReceipt = receipt;
        return receipt;
    }

    private void RequireAcknowledgedDelegationCompletions(TaskExecutionSnapshot parent)
    {
        foreach (var intent in parent.Delegations)
        {
            if (intent.State != TaskRunDelegationState.ChildCompleted
                || !_delegationOriginals.TryGetValue((parent.TaskId, intent.RequestKey), out var actual)
                || actual.CompletionReceipt is not { } receipt || !ReferenceEquals(receipt.Issuer, this)
                || !ReferenceEquals(receipt.Original, actual) || !actual.CompletionOperations.Any(operation => operation.Healthy)
                || actual.ActualChildInvocation is not { HasAcknowledgedCompletion: true })
                throw new InvalidOperationException("Unfinished or historical child work cannot manufacture parent completion.");
            DemandExactDelegationIntent(parent, actual.Proposed, TaskRunDelegationState.ChildCompleted);
        }
    }

    private static bool SameDelegationObservations(IReadOnlyList<TaskRunDelegationIntent> left, IReadOnlyList<TaskRunDelegationIntent> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            var a = left[index]; var b = right[index];
            if (a.Id != b.Id || a.ParentTaskId != b.ParentTaskId || a.ParentExecutionId != b.ParentExecutionId
                || a.CreatedByAttemptId != b.CreatedByAttemptId || a.ChildTaskId != b.ChildTaskId || a.ChildContextId != b.ChildContextId
                || a.ChildExecutionId != b.ChildExecutionId || a.RequestKey != b.RequestKey || a.OriginalRequestDigest != b.OriginalRequestDigest
                || a.PromptSummary != b.PromptSummary || a.State != b.State || a.CreatedAt != b.CreatedAt || a.UpdatedAt != b.UpdatedAt
                || a.AcknowledgedChildRevision != b.AcknowledgedChildRevision || a.ObservedChildState != b.ObservedChildState
                || !a.RequestedPermissionScopes.SequenceEqual(b.RequestedPermissionScopes, StringComparer.Ordinal)) return false;
        }
        return true;
    }

    private static TaskExecutionSnapshot BuildProposedDelegatedChild(TaskRunDelegationOriginal original, TaskExecutionSnapshot parent) =>
        new TaskExecutionSnapshot(original.Proposed.ChildTaskId, original.Proposed.ChildContextId,
            original.Proposed.ChildExecutionId, original.Proposed.PromptSummary, TaskExecutionLifecycle.Running,
            parent.Durability, 1, [], [], [], original.Proposed.RequestedPermissionScopes, null,
            original.Proposed.CreatedAt, original.Proposed.CreatedAt)
        { ParentDelegation = new(parent.TaskId, parent.ContextId, parent.ExecutionId, original.Proposed.Id) };

    private async Task<TaskExecutionSnapshot> PersistDelegationParentWithOriginalPinAsync(
        TaskExecutionSnapshot proposed, TaskRunAttemptAdmission live, TaskRunDelegationOriginal original,
        AgentRuntimeOriginalCustody operation, CancellationToken token)
    {
        var commit = live.Lease as ITaskRunAdmissionCommitLease
            ?? throw new InvalidOperationException("The actual parent lifetime commit pin is unavailable.");
        IAsyncDisposable? pin = null;
        TaskExecutionSnapshot? acknowledged = null;
        Task? actualWrite = null;
        var causes = new List<Exception>();
        try
        {
            pin = await operation.AwaitAsync(() => commit.AcquireOriginalCommitPinAsync(token).AsTask()).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The parent original lifetime pin was refused.");
            acknowledged = await operation.AwaitAsync(() => PersistOriginalWriteOnlyAsync(proposed, token, actual =>
            {
                actualWrite = actual;
                lock (original.Gate) original.OriginalParentLinkWrites.Add(actual);
            })).ConfigureAwait(false);
        }
        catch (Exception failure) { causes.Add(failure); }
        finally
        {
            if (pin is not null)
                try { await operation.AwaitAsync(() => pin.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception failure) { causes.Add(failure); }
        }
        ThrowDelegationStageFailures(causes);
        if (acknowledged is null || actualWrite is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual parent link CAS has no owning acknowledgement.");
        PublishAcknowledgedSnapshot(acknowledged);
        return acknowledged;
    }

    private static void DemandUnstartedChildCreationBasis(TaskExecutionSnapshot acknowledged, TaskExecutionSnapshot current)
    {
        // JSON/SQLite reads reconstruct collection objects. Their reference identities are not child identity.
        if (current.TaskId != acknowledged.TaskId || current.ContextId != acknowledged.ContextId
            || current.ExecutionId != acknowledged.ExecutionId || current.PersistenceRevision != acknowledged.PersistenceRevision
            || current.OwnerBinding != acknowledged.OwnerBinding || current.ParentDelegation != acknowledged.ParentDelegation
            || current.PromptSummary != acknowledged.PromptSummary || current.State != TaskExecutionLifecycle.Running
            || current.Durability != acknowledged.Durability || current.PlanVersion != 1
            || current.CreatedAt != acknowledged.CreatedAt || current.UpdatedAt != acknowledged.UpdatedAt
            || current.CheckpointId is not null || current.LastCheckpointActionId is not null
            || current.RecoveryObservation is not null || current.RecoveryHistory.Count != 0
            || current.Plan.Count != 0 || current.Steers.Count != 0 || current.Queue.Count != 0
            || current.Attempts.Count != 0 || current.Delegations.Count != 0
            || !current.ApprovedPermissionScopes.SequenceEqual(acknowledged.ApprovedPermissionScopes, StringComparer.Ordinal))
            throw new InvalidOperationException("The acknowledged child creation changed before its parent link.");
    }

    internal bool HasOriginalNeverStartedBinding(TaskRunInvocationCustody custody, TaskExecutionSnapshot current)
    {
        if (!ReferenceEquals(custody.Issuer, this) || !ReferenceEquals(custody.OriginalSelf, custody)) return false;
        if (custody.BoundByActualBegin && custody.OriginalBegin is { IsCompletedSuccessfully: true } actualBegin)
            return current.ParentDelegation is null && actualBegin.Result.TaskId == current.TaskId
                && actualBegin.Result.ContextId == current.ContextId && actualBegin.Result.ExecutionId == current.ExecutionId
                && actualBegin.Result.OwnerBinding == current.OwnerBinding;
        if (custody.OriginalDelegatedNeverStarted is not { } witness
            || !ReferenceEquals(witness.Issuer, this) || !ReferenceEquals(witness.Original.Issuer, this)
            || !_delegationOriginals.TryGetValue((witness.Original.Proposed.ParentTaskId, witness.Original.Proposed.RequestKey), out var retained)
            || !ReferenceEquals(retained, witness.Original) || !ReferenceEquals(retained.LinkReceipt, witness.Link)
            || !ReferenceEquals(custody.OriginalDelegatedChildLink, witness.Link)
            || !ReferenceEquals(witness.Initial.Original.OriginalDelegatedNeverStarted, witness)
            || !ReferenceEquals(retained.ActualChildInput, witness.Input)
            || !ReferenceEquals(witness.Input.Owner, custody.OriginalChatOwner)
            || retained.IntentTask is not { IsCompletedSuccessfully: true } intent
            || !ReferenceEquals(intent.Result, retained.IntentReceipt) || !retained.IntentOperation.Healthy
            || retained.CreationTask is not { IsCompletedSuccessfully: true } creation
            || !ReferenceEquals(creation.Result, retained.CreationReceipt) || !retained.CreationOperation.Healthy
            || retained.OriginalChildWrite is not { IsCompletedSuccessfully: true }
            || retained.OriginalPinClose is not { IsCompletedSuccessfully: true }
            || retained.OriginalScopeClose is not { IsCompletedSuccessfully: true }
            || !retained.LinkOperations.Any(actual => actual.Healthy)
            || witness.Binding.Causes.Count != 0 || witness.Binding.Sources.Any(actual => !actual.IsCompletedSuccessfully)
            || current.TaskId != witness.Link.Child.TaskId || current.ContextId != witness.Link.Child.ContextId
            || current.ExecutionId != witness.Link.Child.ExecutionId || current.OwnerBinding != witness.Link.Child.OwnerBinding
            || current.ParentDelegation != witness.Link.Child.ParentDelegation
            || current.PromptSummary != witness.Link.Child.PromptSummary || current.CreatedAt != witness.Link.Child.CreatedAt
            || current.Durability != witness.Link.Child.Durability
            || !current.ApprovedPermissionScopes.SequenceEqual(witness.Link.Child.ApprovedPermissionScopes, StringComparer.Ordinal)) return false;
        if (ReferenceEquals(witness.Initial.Original, custody)) return true;
        return custody.OriginalUnstartedContinuation is { } successor
            && ReferenceEquals(successor.Issuer, this) && ReferenceEquals(successor.Next, custody)
            && ReferenceEquals(successor.Original.OriginalDelegatedNeverStarted, witness)
            && ReferenceEquals(successor.Original, witness.Initial.Original)
            && successor.Preparation is { IsCompletedSuccessfully: true }
            && ReferenceEquals(successor.Preparation.Result, successor);
    }

    internal ChatOriginalAgentInvocation BindOriginalDelegatedAgentSuccessor(
        ChatOriginalAgentInvocation initial, TaskRunUnstartedContinuationBinding prepared,
        ChatOriginalAgentInvocation actualNext)
    {
        if (initial.Original.OriginalDelegatedChildLink is not { } actualLink) return actualNext;
        var original = RequireOriginalDelegation(actualLink.Issuer, actualLink.Original);
        lock (original.Gate)
        {
            if (!ReferenceEquals(original.ActualChildInvocation, initial)
                || !ReferenceEquals(original.LinkReceipt, actualLink) || !ReferenceEquals(prepared.Original, initial.Original)
                || !ReferenceEquals(prepared.Next, actualNext.Original) || !ReferenceEquals(initial.Owner, actualNext.Owner)
                || !ReferenceEquals(initial.OriginalConversation, actualNext.OriginalConversation)
                || prepared.Preparation is not { IsCompletedSuccessfully: true } || !prepared.Claimed
                || prepared.Acknowledged is not { } acknowledged
                || !HasOriginalNeverStartedBinding(actualNext.Original, acknowledged))
                throw new InvalidOperationException("Only the SAME privately prepared child input can bind its one-use Agent successor.");
            original.ActualChildInvocation = actualNext;
            return actualNext;
        }
    }

    private TaskRunDelegationOriginal RequireOriginalDelegation(TaskExecutionCoordinator issuer, TaskRunDelegationOriginal original)
    {
        if (!ReferenceEquals(issuer, this) || !ReferenceEquals(original.Issuer, this)
            || !_delegationOriginals.TryGetValue((original.Proposed.ParentTaskId, original.Proposed.RequestKey), out var actual)
            || !ReferenceEquals(actual, original))
            throw new InvalidOperationException("A copied or historical delegation link cannot recreate original custody.");
        return original;
    }

    private async Task<TaskExecutionSnapshot> RequireDelegationSnapshotAsync(Guid taskId,
        AgentRuntimeOriginalCustody operation, CancellationToken token, TaskRunInvocationCustody? childInvocation = null)
    {
        return await operation.AwaitAsync(() =>
        {
            var actual = repository.GetAsync(taskId, token)
                ?? throw new InvalidOperationException("The actual canonical child/parent repository returned no original read Task.");
            childInvocation?.RetainAdditionalOriginal("delegation.repository.read", actual);
            return actual;
        }).ConfigureAwait(false) ?? throw new KeyNotFoundException("The original canonical child/parent snapshot is unavailable.");
    }

    private async Task<TaskRunAttemptAdmission> RequireOriginalDelegationParentAsync(
        TaskExecutionSnapshot parent, AgentRuntimeOriginalCustody operation, CancellationToken token)
    {
        if (parent.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled
            || parent.RecoveryObservation is not null)
            throw new InvalidOperationException("The actual parent needs recovery or is terminal.");
        var attempt = parent.Attempts.LastOrDefault()
            ?? throw new InvalidOperationException("The actual parent has no current issued attempt.");
        RequireCurrentAttempt(parent, attempt.Id);
        var current = await RequireDelegationSnapshotAsync(parent.TaskId, operation, token).ConfigureAwait(false);
        RequireRun(current, parent.ExecutionId); RequireCurrentAttempt(current, attempt.Id);
        if (current.ContextId != parent.ContextId || current.OwnerBinding != parent.OwnerBinding
            || !_issuedAdmissions.TryGetValue(attempt.Id, out var issued)
            || issued.Snapshot.TaskId != current.TaskId || issued.Snapshot.ContextId != current.ContextId
            || issued.Snapshot.ExecutionId != current.ExecutionId)
            throw new InvalidOperationException("The actual current parent attempt issuer is unavailable; persisted IDs cannot reconstruct it.");
        return issued;
    }

    private static void DemandExactDelegationIntent(TaskExecutionSnapshot parent, TaskRunDelegationIntent original,
        TaskRunDelegationState expectedState)
    {
        var current = parent.Delegations.SingleOrDefault(intent => intent.Id == original.Id);
        if (current is null || current.State != expectedState || current.ParentTaskId != original.ParentTaskId
            || current.ParentExecutionId != original.ParentExecutionId || current.CreatedByAttemptId != original.CreatedByAttemptId
            || current.ChildTaskId != original.ChildTaskId
            || current.ChildContextId != original.ChildContextId || current.ChildExecutionId != original.ChildExecutionId
            || current.RequestKey != original.RequestKey || current.OriginalRequestDigest != original.OriginalRequestDigest
            || current.PromptSummary != original.PromptSummary || current.CreatedAt != original.CreatedAt
            || !current.RequestedPermissionScopes.SequenceEqual(original.RequestedPermissionScopes, StringComparer.Ordinal))
            throw new InvalidOperationException("The original acknowledged child intent changed or is unavailable.");
    }

    private static void ThrowDelegationStageFailures(IReadOnlyList<Exception> causes)
    {
        if (causes.Count > 0) throw new AggregateException("Actual canonical child creation or cleanup remains unresolved.", causes);
    }
}
