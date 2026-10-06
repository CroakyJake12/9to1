using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

// This is the actual private Chat response step. Its reserved GUID is identity only.
// Registration belongs to the SAME coordinator and occurs after genuine Running ACK.
internal sealed class TaskRunOriginalResponseOperation(TaskExecutionCoordinator issuer,
    TaskRunInvocationCustody custody, Guid action, Guid? parent, TaskRunOriginalResponseOperation? prior)
    : ITaskRunOriginalResponseActionSource
{
    internal sealed class Binding(TaskRunAttemptAdmission admission, TaskExecutionSnapshot current,
        Action<Action> scope, Action<Task> retain)
    {
        internal readonly TaskRunAttemptAdmission Admission = admission;
        internal readonly TaskExecutionSnapshot Running = current;
        internal readonly Action<Action> Scope = scope;
        internal readonly Action<Task> Retain = retain;
        internal Task<TaskRunOriginalResponseActionAcknowledgment> Registration = null!;
        internal TaskRunProcessStageCustody Stage = null!;
        internal readonly List<Task> Sources = [];
        internal TaskRunOriginalResponseActionAcknowledgment? Acknowledgment;
    }
    internal readonly object Gate = new();
    internal readonly TaskExecutionCoordinator Issuer = issuer;
    internal readonly TaskRunInvocationCustody Custody = custody;
    internal readonly Guid ActionId = action;
    internal readonly Guid? ParentActionId = parent;
    internal readonly TaskRunOriginalResponseOperation? Prior = prior;
    internal TaskRunColdContinuationBinding? ColdPrior;
    internal object? Request;
    internal string? RequestFingerprint;
    internal readonly List<Binding> Bindings = [];
    internal readonly List<Task<bool>> Moves = [];
    internal Task<OllamaToolResponse>? ActualToolCall;
    internal Task? ActualStreamDispose;
    internal bool StreamReachedEnd;
    internal Exception? ActualFailure;
    internal readonly TaskCompletionSource TerminalReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource CleanupReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<TaskRunOriginalResponseOutcome?>? OriginalTerminal;
    internal TaskRunOriginalResponseOutcome? Outcome;
    internal bool DeferredToWholeContinuation => Prior is not null || ColdPrior is not null;

    public Task<TaskRunOriginalResponseActionAcknowledgment> BindOriginalResponseAsync(OllamaChatRequest request,
        TaskRunAttemptAdmission admission, TaskExecutionSnapshot running, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        Issuer.BindOriginalResponseAsync(this, request, admission, running, scope, retain, token);
    public Task<TaskRunOriginalResponseActionAcknowledgment> BindOriginalResponseAsync(OllamaToolRequest request,
        TaskRunAttemptAdmission admission, TaskExecutionSnapshot running, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        Issuer.BindOriginalResponseAsync(this, request, admission, running, scope, retain, token);
    public bool IsIssuedOriginalResponseActionAcknowledgment(TaskRunOriginalResponseActionAcknowledgment acknowledgment,
        OllamaChatRequest request, TaskRunAttemptAdmission admission) => Issuer.IsOriginalResponseAcknowledgment(this, acknowledgment, request, admission);
    public bool IsIssuedOriginalResponseActionAcknowledgment(TaskRunOriginalResponseActionAcknowledgment acknowledgment,
        OllamaToolRequest request, TaskRunAttemptAdmission admission) => Issuer.IsOriginalResponseAcknowledgment(this, acknowledgment, request, admission);

    internal bool HasActuallyClosedResult => Issuer.HasSuccessfulOriginalResponseOutcome(this)
        || Issuer.HasResolvedOriginalResponseFailure(this);
}

// No public DTO, JSON, ReadOnly label or copied node constructs this evidence.
internal sealed class TaskRunOriginalResponseOutcome(TaskRunOriginalResponseOperation operation,
    TaskExecutionSnapshot before, TaskExecutionSnapshot acknowledged, TaskPlanNode node, Task actualWrite)
{
    internal readonly TaskRunOriginalResponseOperation Operation = operation;
    internal readonly TaskExecutionSnapshot Before = before;
    internal readonly TaskExecutionSnapshot Acknowledged = acknowledged;
    internal readonly TaskPlanNode OriginalNode = node;
    internal readonly Task ActualWrite = actualWrite;
}

internal sealed partial class TaskRunInvocationCustody
{
    private readonly List<TaskRunOriginalResponseOperation> _originalResponses = [];
    internal void RetainOriginalResponse(TaskRunOriginalResponseOperation sameOriginal)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sameOriginal.Issuer, Issuer) || !ReferenceEquals(sameOriginal.Custody, this))
                throw new InvalidOperationException("A foreign original response cannot replace Chat custody.");
            if (_originalResponses.Count >= 256)
                throw new InvalidOperationException("Finite actual response custody is exhausted; retained work requires inspection.");
            _originalResponses.Add(sameOriginal);
        }
    }
    internal TaskRunOriginalResponseOperation[] CaptureOriginalResponses()
    { lock (_gate) return _originalResponses.ToArray(); }
}

public sealed partial class TaskExecutionCoordinator
{
    private readonly ConditionalWeakTable<object, TaskRunOriginalResponseOperation> _originalResponseRequests = new();

    internal TaskRunOriginalResponseOperation ReserveOriginalResponse(TaskRunInvocationCustody custody,
        ChatSessionService chat, Guid? parent, TaskRunToolCheckpointContinuationBinding? continuation = null)
    {
        RequireOriginalInvocation(custody);
        if (!ReferenceEquals(custody.OriginalChatOwner, chat) || custody.OriginalProcessProducer is not { } producer)
            throw new InvalidOperationException("The actual Chat producer does not own this response reservation.");
        TaskRunOriginalResponseOperation? prior = null;
        producer.InvokeAdmittedOriginalCallback(() =>
        {
            if (continuation is not null)
            {
                DemandOriginalToolCheckpointFactory(continuation, chat);
                if (!ReferenceEquals(continuation.Next, custody) || !continuation.Bound
                    || continuation.DispatchWitness is null || continuation.SettledFailure is null
                    || !_originalResponseRequests.TryGetValue(continuation.Boundary.OriginalRequest, out prior)
                    || !ReferenceEquals(prior.Custody, continuation.Original) || prior.Outcome is not null
                    || prior.ActualToolCall is not { IsFaulted: true }
                    || !HasOriginalWholeCallNativeDispatchEvidence(continuation.DispatchWitness, continuation.FinalFailure!))
                    throw new InvalidOperationException("No SAME authentic failed response/settlement boundary exists.");
            }
        });
        var original = new TaskRunOriginalResponseOperation(this, custody, prior?.ActionId ?? Guid.NewGuid(),
            prior?.ParentActionId ?? parent, prior);
        custody.RetainOriginalResponse(original);
        return original; // No graph mutation, admission or permission is issued by reservation.
    }

    internal void CaptureOriginalResponseRequest(TaskRunOriginalResponseOperation original, object request)
    {
        RequireOriginalResponse(original);
        original.Custody.OriginalProcessProducer!.InvokeAdmittedOriginalCallback(() =>
        {
            var context = ResponseContext(request);
            var binding = original.Custody.OriginalBinding!;
            if (context is null || context.TaskId != binding.TaskId || context.ContextId != binding.ContextId
                || context.ExecutionId != binding.ExecutionId || context.ActionId != original.ActionId)
                throw new InvalidOperationException("The reserved response does not bind the SAME actual request context.");
            lock (original.Gate)
            {
                if (original.Request is not null) throw new InvalidOperationException("An original response request was already captured.");
                original.Request = request; original.RequestFingerprint = JsonSerializer.Serialize(request);
                _originalResponseRequests.Add(request, original);
            }
        });
    }

    public ITaskRunOriginalResponseActionSource? TryGetOriginalResponseActionSource(OllamaChatRequest sameRequest) =>
        _originalResponseRequests.TryGetValue(sameRequest, out var original) ? original : null;
    public ITaskRunOriginalResponseActionSource? TryGetOriginalResponseActionSource(OllamaToolRequest sameRequest) =>
        _originalResponseRequests.TryGetValue(sameRequest, out var original) ? original : null;

    private static ProviderExecutionContext? ResponseContext(object request) => request switch
    { OllamaChatRequest chat => chat.ExecutionContext, OllamaToolRequest tools => tools.ExecutionContext, _ => null };
    private void RequireOriginalResponse(TaskRunOriginalResponseOperation original)
    {
        RequireOriginalInvocation(original.Custody);
        if (!ReferenceEquals(original.Issuer, this)
            || !original.Custody.CaptureOriginalResponses().Any(value => ReferenceEquals(value, original)))
            throw new InvalidOperationException("This is not the SAME privately reserved response operation.");
    }

    internal Task<TaskRunOriginalResponseActionAcknowledgment> BindOriginalResponseAsync(
        TaskRunOriginalResponseOperation original, object request, TaskRunAttemptAdmission admission,
        TaskExecutionSnapshot running, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        RequireOriginalResponse(original);
        if (!ReferenceEquals(original.Request, request) || !_originalResponseRequests.TryGetValue(request, out var actual)
            || !ReferenceEquals(actual, original))
            throw new InvalidOperationException("Copied requests cannot bind a source-owned response.");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var binding = new TaskRunOriginalResponseOperation.Binding(admission, running, scope, retain);
        lock (original.Gate)
        {
            if (original.ActualFailure is not null || original.Outcome is not null || original.TerminalReady.Task.IsCompleted
                || original.Bindings.Count >= OriginalInvocationCapacity
                || original.Bindings.Any(value => ReferenceEquals(value.Admission, admission)
                    || !value.Registration.IsCompletedSuccessfully))
                throw new InvalidOperationException("An original response is closed, unresolved, already bound or full.");
            binding.Registration = StartOriginalProcessStage("bind-original-response-action", token,
                stageToken => BindOriginalResponseBodyAsync(original, binding, gate.Task, stageToken), stage =>
                {
                    binding.Stage = stage;
                    stage.OriginalResultClosed = () => original.HasActuallyClosedResult;
                    stage.OriginalResultJoin = () => original.OriginalTerminal
                        ?? throw new InvalidOperationException("No actual response terminal observation driver exists.");
                });
            original.Bindings.Add(binding);
            original.Custody.RetainAdditionalOriginal("response.registration", binding.Registration);
            if (original.OriginalTerminal is null)
            {
                original.OriginalTerminal = ObserveOriginalResponseTerminalAsync(original);
                binding.Stage.RetainSource(original.OriginalTerminal);
                original.Custody.RetainAdditionalOriginal("response.terminal-observation", original.OriginalTerminal);
            }
            else binding.Stage.RetainSource(original.OriginalTerminal);
        }
        gate.SetResult();
        return binding.Registration;
    }

    private async Task<TaskRunOriginalResponseActionAcknowledgment> BindOriginalResponseBodyAsync(
        TaskRunOriginalResponseOperation original, TaskRunOriginalResponseOperation.Binding binding,
        Task gate, CancellationToken token)
    {
        await gate.ConfigureAwait(false);
        var source = new ResponseCallbacks(original, binding, cleanup: false);
        TaskRunOriginalResponseActionAcknowledgment? acknowledgment = null;
        try
        {
            source.Invoke(() => { binding.Retain(binding.Registration); return true; });
            source.Invoke(() => { RequireOriginalResponse(original); DemandOriginalResponseRequest(original); return true; });
            var issued = await source.Read(() => GetIssuedAttemptWithinOriginalSourceAsync(binding.Admission,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            if (!ReferenceEquals(issued, binding.Admission))
                throw new InvalidOperationException("The SAME privately issued current response admission is unavailable.");
            var current = await source.Read(() => repository.GetAsync(binding.Running.TaskId, token)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The original response task disappeared.");
            source.Invoke(() => { DemandResponseRunning(original, binding.Admission, current); return true; });
            if (!source.Invoke(() => SameOriginalCheckpointRow(binding.Running, current)))
                throw new InvalidOperationException("The actual Running ACK changed before response registration.");
            var authority = _admissionAuthority as ITaskRunOriginalResponseAdmissionSource
                ?? throw new InvalidOperationException("The actual response authority has no scoped currentness source.");
            await source.Read(() => authority.ValidateOriginalResponseAdmissionAsync(binding.Admission, source.Run, source.Retain, token)).ConfigureAwait(false);
            if (original.Custody.OriginalInputCurrentness is { } input)
                await source.Read(() => input(token)).ConfigureAwait(false);
            var latest = await source.Read(() => repository.GetAsync(current.TaskId, token)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The original response task disappeared after validation.");
            if (!source.Invoke(() => SameOriginalCheckpointRow(current, latest)))
                throw new InvalidOperationException("The canonical response row changed during actual authority/input reads.");
            // Actor/current route last after the final repository/input await.
            await source.Read(() => authority.ValidateOriginalResponseAdmissionAsync(binding.Admission, source.Run, source.Retain, token)).ConfigureAwait(false);
            var node = source.Invoke(() =>
            {
                DemandResponseRunning(original, binding.Admission, latest); DemandOriginalResponseRequest(original);
                authority.DemandOriginalResponseAdmission(binding.Admission);
                var existing = latest.Plan.SingleOrDefault(value => value.ActionId == original.ActionId);
                if (existing is not null && !HasSameRunningResponseNode(original, existing))
                    throw new InvalidOperationException("Another or acknowledged action cannot become this original response.");
                return existing ?? new TaskPlanNode(original.ActionId, original.ParentActionId,
                    "Read the original model response", TaskPlanNodeState.Running,
                    TaskActionInterruptionPolicy.ReadOnlyCancellable, latest.PlanVersion, RequiredPermissionScopes: []);
            });
            var nodes = source.Invoke(() => latest.Plan.Where(value => value.ActionId != original.ActionId).Append(node).ToArray());
            var ack = await source.Read(() => PersistOriginalWriteOnlyAsync(latest with
                { Plan = nodes, UpdatedAt = _time.GetUtcNow() }, token, source.Retain)).ConfigureAwait(false);
            acknowledgment = new(original, original.Request!, binding.Admission, original.ActionId, ack,
                binding.Registration, binding.Sources.ToArray());
            binding.Acknowledgment = acknowledgment; // The genuine ACK survives a later observer/scope fault.
            source.Invoke(() => { PublishAcknowledgedSnapshot(ack); return true; });
        }
        catch (Exception cause) { source.Add(cause); }
        finally { await source.JoinAll().ConfigureAwait(false); }
        source.Throw();
        return acknowledgment ?? throw new InvalidOperationException("No actual response CAS was acknowledged.");
    }

    private void DemandResponseRunning(TaskRunOriginalResponseOperation original, TaskRunAttemptAdmission admission, TaskExecutionSnapshot current)
    {
        RequireOriginalResponse(original);
        if (!IsSamePrivatelyIssuedInferenceAttempt(admission) || current.State != TaskExecutionLifecycle.Running
            || current.TaskId != original.Custody.OriginalBinding!.TaskId
            || current.ContextId != original.Custody.OriginalBinding.ContextId
            || current.ExecutionId != original.Custody.OriginalBinding.ExecutionId
            || current.OwnerBinding != admission.Snapshot.OwnerBinding)
            throw new InvalidOperationException("The actual response Task/run/owner/admission is not current.");
        if (RequireCurrentAttempt(current, admission.AttemptId).State != TaskRunAttemptState.Running)
            throw new InvalidOperationException("A genuine current Running attempt ACK is required.");
    }
    private static void DemandOriginalResponseRequest(TaskRunOriginalResponseOperation original)
    {
        if (original.Request is null || JsonSerializer.Serialize(original.Request) != original.RequestFingerprint)
            throw new InvalidOperationException("The actual original response request was changed.");
    }
    private bool HasSameRunningResponseNode(TaskRunOriginalResponseOperation original, TaskPlanNode node)
    {
        var acknowledged = original.Bindings.Select(value => value.Acknowledgment).LastOrDefault(value => value is not null)
            ?? original.Prior?.Bindings.Select(value => value.Acknowledgment).LastOrDefault(value => value is not null);
        if (acknowledged is not null) return node.State == TaskPlanNodeState.Running
            && SameOriginalToolCheckpointNode(acknowledged.AcknowledgedSnapshot.Plan.Single(value => value.ActionId == original.ActionId), node);
        // Fresh private cold journal provenance, not a revived response receipt. This
        // permits only the exact unresolved node to receive its first new binding ACK.
        return original.ColdPrior is { } cold && HasOriginalColdUnfinishedResponseNode(cold, original, node);
    }
    internal bool IsOriginalResponseAcknowledgment(TaskRunOriginalResponseOperation original,
        TaskRunOriginalResponseActionAcknowledgment ack, object request, TaskRunAttemptAdmission admission)
    {
        lock (original.Gate) return ReferenceEquals(ack.Issuer, original) && ReferenceEquals(ack.OriginalSelf, ack)
            && ReferenceEquals(ack.OriginalRequest, request) && ReferenceEquals(original.Request, request)
            && ReferenceEquals(ack.OriginalAdmission, admission) && ack.ActualActionId == original.ActionId
            && original.Bindings.Any(value => ReferenceEquals(value.Admission, admission)
                && ReferenceEquals(value.Acknowledgment, ack) && ReferenceEquals(value.Registration, ack.OriginalRegistration));
    }

    internal Task<TaskRunOriginalResponseOutcome?>? RecordOriginalResponseTerminal(TaskRunOriginalResponseOperation original,
        Task<OllamaToolResponse>? toolCall = null, Exception? failure = null)
    {
        lock (original.Gate)
        {
            if (toolCall is not null) original.ActualToolCall = toolCall;
            if (failure is not null) original.ActualFailure ??= failure;
            original.TerminalReady.TrySetResult();
            return original.OriginalTerminal;
        }
    }
    internal void CaptureOriginalResponseMove(TaskRunOriginalResponseOperation original, Task<bool> actual)
    {
        lock (original.Gate)
        {
            if (original.Moves.Count >= CanonicalChatProcessProducer.OriginalMoveCapacity)
                throw new InvalidOperationException("Finite actual response Move custody is exhausted.");
            original.Moves.Add(actual);
        }
    }
    internal void CaptureOriginalResponseDispose(TaskRunOriginalResponseOperation original, Task actual)
    { lock (original.Gate) original.ActualStreamDispose = actual; }

    private async Task<TaskRunOriginalResponseOutcome?> ObserveOriginalResponseTerminalAsync(
        TaskRunOriginalResponseOperation original)
    {
        await original.TerminalReady.Task.ConfigureAwait(false); // Notification only; real tasks below decide terminal evidence.
        using var live = TaskRunProcessProducerContext.EnterAsync(this);
        TaskRunOriginalResponseOperation.Binding binding;
        lock (original.Gate) binding = original.Bindings[^1];
        if (original.ActualFailure is not null || !HasSuccessfulOriginalResponseModelTerminal(original)) return null;
        if (original.DeferredToWholeContinuation)
        {
            await original.CleanupReady.Task.ConfigureAwait(false);
            if (!HasOriginalResponseContinuationCleanup(original)) return null;
        }
        var source = new ResponseCallbacks(original, binding, cleanup: true);
        TaskRunOriginalResponseOutcome? outcome = null;
        try
        {
            var ack = binding.Acknowledgment ?? throw new InvalidOperationException("No SAME response registration ACK exists.");
            if (!binding.Registration.IsCompletedSuccessfully)
                throw new InvalidOperationException("The actual response registration failed independently of the provider result.");
            var current = await source.Read(() => repository.GetAsync(ack.AcknowledgedSnapshot.TaskId, CancellationToken.None)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The actual response Task disappeared before terminal recording.");
            source.Invoke(() =>
            {
                if (original.DeferredToWholeContinuation)
                {
                    var actualOwner = original.Custody.OriginalBinding!;
                    if (!HasOriginalResponseContinuationCleanup(original) || current.State != TaskExecutionLifecycle.Running
                        || current.TaskId != actualOwner.TaskId || current.ContextId != actualOwner.ContextId
                        || current.ExecutionId != actualOwner.ExecutionId || current.OwnerBinding != actualOwner.OwnerBinding)
                        throw new InvalidOperationException("The actual completed continuation changed its Task/run/owner.");
                }
                else DemandResponseRunning(original, binding.Admission, current);
                DemandOriginalResponseRequest(original); return true;
            });
            var node = source.Invoke(() => current.Plan.SingleOrDefault(value => value.ActionId == original.ActionId));
            if (node is null || !source.Invoke(() => HasSameRunningResponseNode(original, node)))
                throw new InvalidOperationException("The actual response graph node changed before terminal ACK.");
            Task<TaskExecutionSnapshot>? write = null;
            var acknowledged = await source.Read(() => write = PersistOriginalWriteOnlyAsync(current with
            {
                Plan = current.Plan.Select(value => value.ActionId == original.ActionId ? value with { State = TaskPlanNodeState.Completed } : value).ToArray(),
                UpdatedAt = _time.GetUtcNow()
            }, CancellationToken.None, source.Retain, owningProcessCleanup: true)).ConfigureAwait(false);
            var completed = source.Invoke(() => acknowledged.Plan.Single(value => value.ActionId == original.ActionId));
            outcome = new(original, current, acknowledged, completed, write!);
            original.Outcome = outcome;
            source.Invoke(() => { PublishAcknowledgedSnapshot(acknowledged); return true; });
        }
        catch (Exception cause) { source.Add(cause); }
        finally { await source.JoinAll().ConfigureAwait(false); }
        source.Throw(); return outcome;
    }
    private static bool HasSuccessfulOriginalResponseModelTerminal(TaskRunOriginalResponseOperation original) =>
        original.ActualFailure is null && (original.ActualToolCall is { IsCompletedSuccessfully: true, Result: not null }
            || original.Moves.Count != 0 && original.Moves.All(value => value.IsCompletedSuccessfully)
                && !original.Moves[^1].Result && original.StreamReachedEnd
                && original.ActualStreamDispose is { IsCompletedSuccessfully: true });
    private static bool HasOriginalResponseContinuationCleanup(TaskRunOriginalResponseOperation original)
    {
        var current = original.Custody;
        return current.OwnedCleanupTerminal && current.ReachedEnd && current.Causes.Count == 0 && current.ResourcesDisposed
            && current.OriginalDispose is { IsCompletedSuccessfully: true }
            && (current.OriginalTracker is null || current.OriginalTrackerDispose is { IsCompletedSuccessfully: true })
            && current.OriginalMoves.All(value => value.IsCompletedSuccessfully);
    }
    internal bool HasSuccessfulOriginalResponseOutcome(TaskRunOriginalResponseOperation original) =>
        ReferenceEquals(original.Issuer, this) && original.Outcome is { } outcome && ReferenceEquals(outcome.Operation, original)
        && original.OriginalTerminal is { IsCompletedSuccessfully: true } && ReferenceEquals(original.OriginalTerminal.Result, outcome)
        && outcome.ActualWrite.IsCompletedSuccessfully && HasSuccessfulOriginalResponseModelTerminal(original)
        && original.Bindings.All(value => value.Registration.IsCompletedSuccessfully && value.Acknowledgment is not null
            && value.Sources.All(actual => actual.IsCompletedSuccessfully));

    // This later private resolution never changes the failed raw Task or says it was healthy.
    internal bool HasResolvedOriginalResponseFailure(TaskRunOriginalResponseOperation original)
    {
        var resolution = original.Custody.OriginalResolvedToolCheckpoint;
        if (resolution is null || original.ActualToolCall is not { IsFaulted: true }
            || original.OriginalTerminal is not { IsCompletedSuccessfully: true }
            || original.Outcome is not null || original.Bindings.Any(value => !value.Registration.IsCompletedSuccessfully
                || value.Sources.Any(actual => !actual.IsCompletedSuccessfully))) return false;
        var binding = resolution.Binding;
        return ReferenceEquals(binding.Original, original.Custody) && ReferenceEquals(binding.Issuer, this)
            && ReferenceEquals(binding.Boundary.OriginalRequest, original.Request)
            && ReferenceEquals(binding.Boundary.ActualCall, original.ActualToolCall)
            && binding.DispatchWitness is { } dispatch && binding.FinalFailure is { } final
            && HasOriginalWholeCallNativeDispatchEvidence(dispatch, final)
            && resolution.Completion.IsCompletedSuccessfully && resolution.ActualBusinessDriver.IsCompletedSuccessfully
            && resolution.ActualResolutionValidation.IsCompletedSuccessfully
            && binding.Next.OriginalProcessProducer is { HasHealthyClosedOriginal: true }
            && binding.Next.CaptureOriginalResponses().Any(next => ReferenceEquals(next.Prior, original)
                && HasSuccessfulOriginalResponseOutcome(next) && HasOriginalResponseContinuationCleanup(next));
    }

    private sealed class ResponseCallbacks(TaskRunOriginalResponseOperation original,
        TaskRunOriginalResponseOperation.Binding binding, bool cleanup)
    {
        private readonly object _causeGate = new();
        private readonly List<Exception> _errors = [];
        private readonly Dictionary<Exception, Task> _knownCanceled = new(ReferenceEqualityComparer.Instance);
        private bool _faulted;
        internal void Add(Exception cause, Task? actual = null)
        {
            lock (_causeGate)
            {
                if (actual is { IsCanceled: true }) _knownCanceled[cause] = actual;
                else if (actual is null && _knownCanceled.TryGetValue(cause, out var sameCanceled)) actual = sameCanceled;
                if (actual is { IsFaulted: true }
                    || actual is null && !_errors.Any(value => ReferenceEquals(value, cause))) _faulted = true;
                IEnumerable<Exception> errors = actual?.Exception is { } fault ? fault.InnerExceptions : [cause];
                foreach (var error in errors)
                    if (!_errors.Any(value => ReferenceEquals(value, error))) _errors.Add(error);
            }
            original.Custody.Retain(cause, actual);
        }
        internal void Retain(Task actual)
        {
            binding.Stage.RetainSource(actual);
            lock (original.Gate) if (!binding.Sources.Any(value => ReferenceEquals(value, actual))) binding.Sources.Add(actual);
            original.Custody.RetainAdditionalOriginal("response.registration-source", actual);
            binding.Retain(actual); // Actual owner enrollment precedes this external callback.
        }
        internal void Run(Action callback) => Invoke(() => { callback(); return true; });
        internal T Invoke<T>(Func<T> callback)
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            var failures = new List<Exception>(); T result = default!;
            void Record(Exception cause)
            { lock (failures) if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
            Exception[] Snapshot() { lock (failures) return failures.ToArray(); }
            try
            {
                var producer = original.Custody.OriginalProcessProducer
                    ?? throw new InvalidOperationException("The original response has no actual Chat producer.");
                void Finite() => binding.Scope(() =>
                {
                    if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                    {
                        var cause = new InvalidOperationException("The response callback is inactive, foreign-thread or consumed.");
                        Record(cause); throw cause;
                    }
                    try { result = binding.Stage.Invoke(callback, cleanup); }
                    catch (Exception cause) { Record(cause); throw; }
                });
                if (cleanup) producer.InvokeOriginalCallback(Finite); else producer.InvokeAdmittedOriginalCallback(Finite);
                if (Snapshot().Length != 0)
                    throw new AggregateException("Every actual response callback refusal remains retained.", Snapshot());
                if (Volatile.Read(ref used) == 0) throw new InvalidOperationException("No finite original response callback occurred.");
                return binding.Stage.Invoke(() => result, cleanup); // Caller may seal after its callback.
            }
            catch (Exception cause)
            {
                Record(cause);
                foreach (var originalFailure in Snapshot()) Add(originalFailure);
                Throw(); throw;
            }
            finally { Interlocked.Exchange(ref active, 0); }
        }
        internal async Task<T> Read<T>(Func<Task<T>> factory)
        {
            Task<T>? actual = null; Exception? scopeFailure = null; T result = default!;
            try { Invoke(() => { actual = factory() ?? throw new InvalidOperationException("No actual response source Task returned."); Retain(actual); return true; }); }
            catch (Exception cause) { scopeFailure = cause; }
            if (actual is not null)
                try { result = await actual.ConfigureAwait(false); }
                catch (Exception cause) { Add(cause, actual); if (scopeFailure is null && actual.IsCanceled) throw; }
            Throw(); return actual is null ? throw new InvalidOperationException("No actual response source was acquired.") : result;
        }
        internal async Task Read(Func<Task> factory)
        {
            Task? actual = null; Exception? scopeFailure = null;
            try { Invoke(() => { actual = factory() ?? throw new InvalidOperationException("No actual response validation Task returned."); Retain(actual); return true; }); }
            catch (Exception cause) { scopeFailure = cause; }
            if (actual is not null)
                try { await actual.ConfigureAwait(false); }
                catch (Exception cause) { Add(cause, actual); if (scopeFailure is null && actual.IsCanceled) throw; }
            Throw(); if (actual is null) throw new InvalidOperationException("No actual response validation was acquired.");
        }
        internal async Task JoinAll()
        {
            Task[] sources; lock (original.Gate) sources = binding.Sources.ToArray();
            foreach (var actual in sources)
                try { await actual.ConfigureAwait(false); } catch (Exception cause) { Add(cause, actual); }
        }
        internal void Throw()
        {
            Exception[] causes; bool fault;
            lock (_causeGate) { causes = _errors.ToArray(); fault = _faulted; }
            if (causes.Length == 0) return;
            if (!fault && causes.All(value => value is OperationCanceledException))
                ExceptionDispatchInfo.Capture(causes[0]).Throw();
            throw new AggregateException("Actual original response source/cleanup failed.", causes);
        }
    }
}
