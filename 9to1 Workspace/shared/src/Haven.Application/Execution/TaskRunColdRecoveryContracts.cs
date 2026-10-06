using Haven.Core;

namespace Haven.Application;

/// <summary>Exact original hosted input. Serialized values are observations, never a recovered grant.</summary>
public sealed record TaskRunColdChatInput(
    Conversation Conversation, string Prompt, ModelDescriptor Model, EffortLevel Effort,
    IReadOnlyList<ActiveCapability> Capabilities, string AgentName, string AgentInstructions,
    DuoMode DuoMode, string? WorkspaceRoot, string? ProjectContext, string? ProjectInstructions,
    IReadOnlyList<string>? Images, IReadOnlyList<ActivePrompt>? Prompts, string? RegisteredContext,
    GenerationOptions? GenerationOptions, PermissionMode FilePermission, PermissionMode CommandPermission,
    PermissionMode BrowserPermission, IReadOnlyList<ToolCapability>? ExplicitCapabilities,
    IReadOnlyList<ActiveCapability>? AvailableCapabilities, ComputerUseRequest? ComputerUseRequest);

/// <summary>The initially supported boundary has no invoked attempt or accepted/uncertain effect.
/// An interrupted claim is permanently unavailable for automatic replay.</summary>
public enum TaskRunColdBoundaryKind { NeverStartedAcceptedInput = 1, SettledUnfinishedToolResponse = 2 }

public sealed record TaskRunColdCapsule(int SchemaVersion, Guid CapsuleId,
    TaskRunColdBoundaryKind Boundary, TaskExecutionSnapshot AcknowledgedTask,
    Conversation AcceptedConversation, ChatMessage AcceptedUserMessage,
    TaskRunColdChatInput OriginalInput, DateTimeOffset CapturedAt)
{
    public TaskRunColdToolCheckpoint? OriginalToolCheckpoint { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TaskRunColdProjectIdentity? OriginalProjectIdentity { get; init; }
}

/// <summary>Issued only by the actual Chat producer after its entire original body and cleanup.
/// The journal must validate the issuing source, not the Capsule fields.</summary>
public sealed class TaskRunOriginalColdCapture
{
    internal TaskRunOriginalColdCapture(ChatSessionService issuer, object marker,
        HostedInitialTaskSend original, TaskRunColdCapsule capsule)
    { Issuer = issuer; Marker = marker; Original = original; Capsule = capsule; }
    internal ChatSessionService Issuer { get; }
    internal object Marker { get; }
    internal HostedInitialTaskSend Original { get; }
    public TaskRunColdCapsule Capsule { get; }
}

public interface ITaskRunOriginalColdCaptureSource
{
    bool IsIssuedOriginalColdCapture(TaskRunOriginalColdCapture sameCapture);
    Task ValidateOriginalColdCaptureAsync(TaskRunOriginalColdCapture sameCapture, CancellationToken token);
}

/// <summary>Opaque journal reference. Arbitrary implementations/DTO copies grant nothing;
/// only the configured journal's private reference validators establish issuance.</summary>
public interface ITaskRunColdJournalEntry { TaskRunColdCapsule Capsule { get; } }
public interface ITaskRunColdJournalClaim : IAsyncDisposable
{
    ITaskRunColdJournalEntry OriginalEntry { get; }
    TaskExecutionSnapshot OriginalExpected { get; }
    Guid ClaimId { get; }
}
public interface ITaskRunColdJournalAcknowledgment
{
    ITaskRunColdJournalClaim OriginalClaim { get; }
    TaskExecutionSnapshot AcknowledgedTask { get; }
}

/// <summary>Only Application can create this scope over an actual admitted driver. It preserves
/// physical callback ancestry and enrolls returned raw Tasks; it supplies no permission.</summary>
public sealed class TaskRunColdOriginalSourceScope
{
    private readonly TaskRunColdOriginalSourceScope? _parent;
    private readonly Action<Action>? _caller;
    private readonly Action<Task>? _retainer;
    private readonly AgentRuntimeOriginalCustody? _operation;
    private readonly TaskRunInvocationCustody? _invocation;
    internal TaskRunColdOriginalSourceScope(AgentRuntimeOriginalCustody operation) => _operation = operation;
    internal TaskRunColdOriginalSourceScope(TaskRunInvocationCustody invocation) => _invocation = invocation;
    private TaskRunColdOriginalSourceScope(TaskRunColdOriginalSourceScope parent, Action<Action> caller, Action<Task> retainer)
    { _parent = parent; _caller = caller; _retainer = retainer; }
    // Composes source ancestry only; the actual private journal/claim validators remain mandatory.
    public TaskRunColdOriginalSourceScope WithinOriginalCaller(Action<Action> caller, Action<Task> retainer)
    { ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(retainer); return new(this, caller, retainer); }
    private void RetainOriginalTask(Task actual)
    {
        if (_parent is not null) _parent.RetainOriginalTask(actual);
        else if (_invocation is { } invocation) invocation.RetainAdditionalOriginal("cold.body-source", actual);
        else _operation!.RetainSource(actual);
    }
    public T Invoke<T>(Func<T> actualSource)
    {
        if (_parent is { } parent)
        {
            T actual = default!;
            return parent.Invoke(() =>
            {
                int active = 1, invoked = 0;
                var thread = Environment.CurrentManagedThreadId;
                var errors = new List<Exception>();
                var causeGate = new object();
                void Keep(Exception cause)
                { lock (causeGate) if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause); }
                void Run()
                {
                    try
                    {
                        if (Volatile.Read(ref active) == 0 || Environment.CurrentManagedThreadId != thread
                            || Interlocked.CompareExchange(ref invoked, 1, 0) != 0)
                            throw new InvalidOperationException("The cold source scope requires its active issuing thread and one finite invocation.");
                        actual = actualSource();
                        if (actual is Task task) { parent.RetainOriginalTask(task); _retainer!(task); }
                    }
                    catch (Exception cause) { Keep(cause); throw; }
                }
                try { _caller!(Run); }
                catch (Exception cause) { Keep(cause); }
                finally { Volatile.Write(ref active, 0); }
                if (Volatile.Read(ref invoked) == 0)
                    Keep(new InvalidOperationException("The original cold caller did not invoke its finite source."));
                Exception[] retained; lock (causeGate) retained = errors.ToArray();
                if (retained.Length != 0) throw new AggregateException("Every actual cold source/scope refusal is retained.", retained);
                return actual;
            });
        }
        if (_invocation is { } invocation)
        {
            T actual = default!;
            (invocation.OriginalProcessProducer ?? throw new InvalidOperationException("No same actual input producer exists."))
                .InvokeOriginalCallback(() =>
                {
                    actual = actualSource();
                    if (actual is Task task) invocation.RetainAdditionalOriginal("cold.body-source", task);
                });
            return actual;
        }
        return _operation!.Invoke(() =>
        {
            var actual = actualSource();
            if (actual is Task task) _operation!.RetainSource(task);
            return actual;
        }, owningCleanup: true);
    }
}

/// <summary>Same-store authenticated provenance, actual task/input reads and sticky one-use claims.
/// Commit performs one transaction containing claim ownership and the exact task binding CAS.</summary>
public interface ITaskRunColdRecoveryJournal
{
    Task PublishOriginalAsync(TaskRunOriginalColdCapture sameCapture, TaskRunColdOriginalSourceScope sources, CancellationToken token);
    Task<ITaskRunColdJournalEntry?> ReadOriginalAsync(Guid taskId, Guid expectedRunId,
        TaskRunColdOriginalSourceScope sources, CancellationToken token);
    bool IsIssuedOriginalEntry(ITaskRunColdJournalEntry sameEntry);
    Task<ITaskRunColdJournalClaim> ClaimOriginalAsync(ITaskRunColdJournalEntry sameEntry,
        TaskExecutionSnapshot actualExpected, CancellationToken token);
    Task ValidateOriginalClaimAsync(ITaskRunColdJournalClaim sameClaim,
        TaskExecutionSnapshot actualExpected, CancellationToken token);
    Task<ITaskRunColdJournalAcknowledgment> CommitOriginalAsync(ITaskRunColdJournalClaim sameClaim,
        TaskExecutionSnapshot proposedNext, CancellationToken token);
    bool IsIssuedOriginalAcknowledgment(ITaskRunColdJournalAcknowledgment sameAcknowledgment,
        ITaskRunColdJournalClaim sameClaim);
    Task ValidateOriginalAcknowledgmentAsync(ITaskRunColdJournalAcknowledgment sameAcknowledgment,
        CancellationToken token);
    Task ValidateOriginalRestoredInputAsync(ITaskRunColdJournalAcknowledgment sameAcknowledgment,
        TaskExecutionSnapshot actualCurrent, TaskRunColdOriginalSourceScope currentSources, CancellationToken token);
    Task RecordOriginalTerminalAsync(ITaskRunColdJournalAcknowledgment sameAcknowledgment,
        TaskExecutionSnapshot actualTerminal, CancellationToken token);
}

/// <summary>Fresh domain authorization, separate from model, resource, account and Home permission.
/// Implementations must validate private same-store/actor/input identity, not public IDs.</summary>
public interface ITaskRunColdContextLease : IAsyncDisposable
{
    ITaskRunColdJournalClaim OriginalClaim { get; }
    AuthenticatedResourceActor CurrentActor { get; }
}
public interface ITaskRunColdContextAuthority
{
    ValueTask<ITaskRunColdContextLease> AuthorizeOriginalAsync(ITaskRunColdJournalClaim sameClaim,
        TaskExecutionSnapshot actualExpected, CancellationToken token);
    ValueTask ValidateOriginalAsync(ITaskRunColdContextLease sameLease,
        TaskExecutionSnapshot actualExpected, CancellationToken token);
    bool IsIssuedOriginal(ITaskRunColdContextLease sameLease, ITaskRunColdJournalClaim sameClaim);
    ValueTask ValidateOriginalClosedContextAsync(ITaskRunColdContextLease sameLease,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, CancellationToken token);
}

/// <summary>Staged fresh activation. No old attempt/lease is reconstructed. Pure pin only encloses
/// the journal/task CAS; all actor/domain/policy reads and cleanup occur outside it.</summary>
public interface ITaskRunColdOwnerAdmission : IAsyncDisposable
{
    ITaskRunColdJournalClaim OriginalClaim { get; }
    ITaskRunColdContextLease OriginalContext { get; }
    TaskExecutionOwnerBinding PreviousOwner { get; }
    TaskExecutionOwnerBinding NextOwner { get; }
    ValueTask RevalidateAsync(CancellationToken token);
    ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken token);
}
public interface ITaskRunColdOwnerAuthority
{
    // Callback-free exact composition proof only; no capsule readiness, permission or settlement.
    bool HasOriginalColdRecoveryComposition(ITaskRunColdRecoveryJournal sameJournal, ITaskRunColdContextAuthority sameContext);
    ValueTask<ITaskRunColdOwnerAdmission> PrepareOriginalColdOwnerAsync(
        ITaskRunColdJournalClaim sameClaim, ITaskRunColdContextLease sameContext,
        TaskExecutionSnapshot actualExpected, CancellationToken token);
    bool IsIssuedOriginalColdOwner(ITaskRunColdOwnerAdmission sameAdmission,
        ITaskRunColdJournalClaim sameClaim, ITaskRunColdContextLease sameContext);
    ValueTask ActivateAcknowledgedOriginalColdOwnerAsync(ITaskRunColdOwnerAdmission sameAdmission,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, CancellationToken token);
}

public static partial class TaskRunColdRecoveryBoundary
{
    public static void DemandNeverStarted(TaskRunColdCapsule capsule, TaskExecutionSnapshot actual)
    {
        if (capsule.SchemaVersion != 1 || capsule.CapsuleId == Guid.Empty
            || capsule.Boundary != TaskRunColdBoundaryKind.NeverStartedAcceptedInput
            || actual.TaskId != capsule.AcknowledgedTask.TaskId
            || actual.ContextId != capsule.AcceptedConversation.Id
            || actual.ExecutionId != capsule.AcknowledgedTask.ExecutionId
            || actual.OwnerBinding is null || actual.State != TaskExecutionLifecycle.Suspended
            || actual.Attempts.Count != 0 || actual.Plan.Count != 0 || actual.Steers.Count != 0
            || actual.Queue.Count != 0 || actual.Delegations.Count != 0 || actual.ParentDelegation is not null
            || actual.RecoveryHistory.Count != 0
            || actual.RecoveryObservation is not { SettlementOutcome: TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked }
            || actual.CheckpointId is not null || actual.LastCheckpointActionId is not null
            || capsule.AcceptedConversation.IsTemporary || capsule.AcceptedConversation.Mode != HavenMode.Tasks
            || capsule.AcceptedUserMessage.ConversationId != actual.ContextId
            || capsule.AcceptedUserMessage.Role != MessageRole.User
            || capsule.AcceptedUserMessage.Content != capsule.OriginalInput.Prompt
            || capsule.OriginalInput.Conversation.Id != actual.ContextId)
            throw new InvalidOperationException("Only the exact acknowledged, closed, never-started input can be reconstructed; accepted/uncertain work requires owning reconciliation.");
        // Resource-bearing recovery needs its own fresh source-issued resource reconciliation.
        var input = capsule.OriginalInput;
        if (input.WorkspaceRoot is not null || input.ProjectContext is not null || input.ProjectInstructions is not null
            || input.RegisteredContext is not null || input.ComputerUseRequest is not null || input.Images is { Count: > 0 }
            || capsule.AcceptedConversation.ContainerId is not null || capsule.AcceptedConversation.LessonId is not null)
            throw new InvalidOperationException("This cold boundary has no configured fresh resource reconciliation owner.");
    }
}
