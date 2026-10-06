using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

/// <summary>One source-issued Ask and explicit UI decision. This is not a provider continuation,
/// Home grant, resource-egress permit, monetary reservation or proof from remediation Completed.</summary>
public sealed partial class TaskRunCloudPermissionRemediationOwner : IAsyncDisposable
{
    public const string ComponentId = "haven.task-cloud-permission";
    private readonly TaskRunCentralCloudUsePermissionSource _source;
    private readonly TaskRunPermissionAuthority _authority;
    private readonly Func<TaskExecutionCoordinator> _tasks;
    private readonly RemediationCoordinator _remediation;
    private readonly IRemediationRepository _repository;
    private readonly RemediationContinuationRegistry _continuations;
    private readonly IExecutionEventSink? _canonicalEvents;
    public bool HasCanonicalTelemetry => _canonicalEvents is not null; // Configuration observation only.
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _ownerToken;
    private readonly Dictionary<ITaskRunCloudPermissionOriginalRequest, Binding> _requests = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Guid, Binding> _ids = [];
    private bool _closing; private Task? _close; private Exception? _capacityRefusal;
    private readonly AsyncLocal<OriginalPhase?> _executing = new();
    [ThreadStatic] private static PhysicalPhase? _physical;
    private sealed class OriginalPhase(TaskRunCloudPermissionRemediationOwner owner, Task original, OriginalPhase? previous)
    {
        public readonly TaskRunCloudPermissionRemediationOwner Owner = owner;
        public readonly Task Original = original;
        public readonly OriginalPhase? Previous = previous;
        public int Live = 1;
    }
    private sealed class PhysicalPhase(TaskRunCloudPermissionRemediationOwner owner, PhysicalPhase? previous)
    {
        public readonly TaskRunCloudPermissionRemediationOwner Owner = owner;
        public readonly PhysicalPhase? Previous = previous;
    }
    private OriginalPhase EnterOriginal(Task actual)
    {
        var phase = new OriginalPhase(this, actual, _executing.Value);
        _executing.Value = phase; return phase;
    }
    private void ExitOriginal(OriginalPhase phase)
    { Volatile.Write(ref phase.Live, 0); _executing.Value = phase.Previous; }
    private T CallOriginal<T>(Func<T> callback)
    {
        var previous = _physical; _physical = new(this, previous);
        try { return callback(); } finally { _physical = previous; }
    }
    private void CallOriginal(Action callback) => CallOriginal(() => { callback(); return true; });
    private void RefuseOriginalSelfJoin()
    {
        for (var phase = _physical; phase is not null; phase = phase.Previous)
            if (ReferenceEquals(phase.Owner, this))
                throw new InvalidOperationException("A physical original callback cannot join its encompassing permission owner close.");
        for (var phase = _executing.Value; phase is not null; phase = phase.Previous)
            if (ReferenceEquals(phase.Owner, this) && Volatile.Read(ref phase.Live) != 0 && !phase.Original.IsCompleted)
                throw new InvalidOperationException("A live permission original cannot join its encompassing owner close.");
    }
    private sealed class Binding(ITaskRunCloudPermissionOriginalRequest original)
    {
        public ITaskRunCloudPermissionOriginalRequest Original { get; } = original;
        public Guid Id { get; } = Guid.NewGuid();
        public Guid ActionId { get; } = Guid.NewGuid();
        public Guid? ExpectedAttempt; public bool Captured, ApprovalValidated;
        public Task<RemediationRequest> Publication = null!;
        public RemediationRequest? Published;
        public Task<PermissionDecision>? Decision, OriginalSourceDecision;
        public bool? Approved;
        public Task<RemediationRequest>? Approval;
        public Task<PermissionDecision>? Denial;
        public Task<RemediationContinuationResult>? Callback;
        public Task? RequestEvent, DecisionEvent;
        public TaskRunCloudPermissionRequiredException? OriginalAsk;
        public Task<ITaskRunUnstartedContinuationPermissionLease>? Continuation;
        public long ContinuationRevision;
        public UnstartedPermissionLease? ContinuationLease;
        public Task<ITaskRunCloudUsePermissionLease>? ActualContinuationPermissionAcquisition;
        public ITaskRunCloudUsePermissionLease? AcquiredContinuationPermission;
        public Task? ActualUnpublishedContinuationClose;
    }
    public TaskRunCloudPermissionRemediationOwner(TaskRunCentralCloudUsePermissionSource sameSource,
        TaskRunPermissionAuthority sameTaskAuthority, Func<TaskExecutionCoordinator> originalTaskLookup,
        RemediationCoordinator sameRemediation, IRemediationRepository sameRemediationRepository,
        RemediationContinuationRegistry sameOriginalContinuations)
        : this(sameSource, sameTaskAuthority, originalTaskLookup, sameRemediation,
            sameRemediationRepository, sameOriginalContinuations, canonicalEvents: null) { }

    public TaskRunCloudPermissionRemediationOwner(TaskRunCentralCloudUsePermissionSource sameSource,
        TaskRunPermissionAuthority sameTaskAuthority, Func<TaskExecutionCoordinator> originalTaskLookup,
        RemediationCoordinator sameRemediation, IRemediationRepository sameRemediationRepository,
        RemediationContinuationRegistry sameOriginalContinuations, IExecutionEventSink? canonicalEvents)
    {
        _canonicalEvents = canonicalEvents;
        _source = sameSource ?? throw new ArgumentNullException(nameof(sameSource));
        _authority = sameTaskAuthority ?? throw new ArgumentNullException(nameof(sameTaskAuthority));
        if (!ReferenceEquals(_source.OriginalTaskActors, _authority.OriginalTaskActors))
            throw new ArgumentException("SAME genuine Task actor source is required.");
        _tasks = originalTaskLookup ?? throw new ArgumentNullException(nameof(originalTaskLookup));
        _remediation = sameRemediation ?? throw new ArgumentNullException(nameof(sameRemediation));
        _repository = sameRemediationRepository ?? throw new ArgumentNullException(nameof(sameRemediationRepository));
        _continuations = sameOriginalContinuations ?? throw new ArgumentNullException(nameof(sameOriginalContinuations));
        // Root's trusted factory supplies SAME registry/repository already passed to remediation.
        // A second registry is not a cleanup/retirement proof and is forbidden in composition.
        _ownerToken = _stop.Token;
    }
    public Task<RemediationRequest> RequestOriginalAsync(TaskRunCloudPermissionRequiredException original,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        var request = original.OriginalRequest
            ?? throw new UnauthorizedAccessException("A legacy/caller-created permission exception is not an original Ask issuer.");
        var publication = RequestOriginalAsync(request, token);
        lock (_sync)
            if (_requests.TryGetValue(request, out var record)) record.OriginalAsk ??= original;
        return publication;
    }

    public Task<RemediationRequest> RequestOriginalAsync(ITaskRunCloudPermissionOriginalRequest original, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original); token.ThrowIfCancellationRequested();
        if (!_source.IsIssuedOriginalRequest(original) || original.OriginalDecision.Kind != PermissionDecisionKind.Ask)
            throw new UnauthorizedAccessException("SAME source-issued pre-dispatch Ask required.");
        Binding record; TaskCompletionSource<RemediationRequest> completion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_requests.TryGetValue(original, out var existing)) return existing.Publication;
            if (_capacityRefusal is not null) ExceptionDispatchInfo.Capture(_capacityRefusal).Throw();
            if (_requests.Count == 128)
            {
                _capacityRefusal = new InvalidOperationException("Original cloud approval owner custody is full.");
                throw _capacityRefusal;
            }
            record = new(original); completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            record.Publication = completion.Task; _requests.Add(original, record); _ids.Add(record.Id, record);
        }
        _ = PublishOriginalAsync(record, completion);
        return completion.Task;
    }
    private async Task PublishOriginalAsync(Binding record, TaskCompletionSource<RemediationRequest> completion)
    {
        var phase = EnterOriginal(completion.Task);
        Task<RemediationRequest>? actual = null;
        try
        {
            var current = await DemandCurrentAsync(record, capture: true).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var request = new RemediationRequest(record.Id, current.ExecutionId, record.ActionId,
                RemediationType.PermissionRequest, "Allow remote model use for this task?",
                "Task: " + SensitiveTextRedactor.Redact(current.PromptSummary, 600) + ". " +
                "Task ID: " + current.TaskId.ToString("D") + "; run ID: " + current.ExecutionId.ToString("D") + ". " +
                "Allow only this task/run/actor/model scope. Provider costs and monetary budget are unknown. " +
                "This decision does not approve private workspace/domain egress or start/retry any provider operation.",
                ComponentId, "Task remote-use permission", record.Original.OriginalCandidate.ProviderId + "/" + record.Original.OriginalCandidate.ModelId,
                [], ["Approve", "Deny"], RemediationSensitivity.Normal, false, true,
                RecoveryPolicyDefaults.InitialUserInteractionTimeout, RecoveryPolicyDefaults.MaximumInteractiveWait,
                RemediationState.Waiting, now, now);
            actual = CallOriginal(() => _remediation.RequestAsync(request, (resolution, _) => OnOriginalResolutionAsync(record, resolution), _ownerToken));
            record.Published = await ObserveAsync(actual).ConfigureAwait(false);
            await ObserveAsync(StartOriginalCanonicalEvent(record, decision: null)).ConfigureAwait(false);
            completion.TrySetResult(record.Published);
        }
        catch (Exception error) { completion.TrySetException((Exception?)actual?.Exception ?? error); }
        finally { ExitOriginal(phase); }
    }
    // Deny-only UI observation. IDs do not grant; every response uses the retained original record.
    public bool CanRespond(Guid id)
    { lock (_sync) return !_closing && _ids.TryGetValue(id, out var record) && record.Publication.IsCompletedSuccessfully && record.Approved is null && record.Callback is null; }

    public Task<RemediationRequest> ApproveOriginalAsync(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Binding record; TaskCompletionSource<RemediationRequest> completion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            record = RequireRecord(id);
            if (record.Approval is not null) return record.Approval;
            if (record.Callback is not null) throw new InvalidOperationException("An unmatched original callback remains retained; it cannot be replayed as approval.");
            if (record.Approved == false) throw new InvalidOperationException("This original user decision was denied.");
            record.Approved = true; completion = new(TaskCreationOptions.RunContinuationsAsynchronously); record.Approval = completion.Task;
        }
        _ = ApprovePublishedAsync(record, completion);
        return completion.Task;
    }
    private async Task ApprovePublishedAsync(Binding record, TaskCompletionSource<RemediationRequest> completion)
    {
        var phase = EnterOriginal(completion.Task);
        Task<RemediationRequest>? actual = null;
        try
        {
            await ObserveAsync(record.Publication).ConfigureAwait(false);
            await DemandCurrentAsync(record, capture: false).ConfigureAwait(false);
            var persisted = await ObserveAsync(CallOriginal(() => _repository.GetAsync(record.Id, _ownerToken))).ConfigureAwait(false);
            if (persisted is null || record.Published is not { } expected || persisted.Id != expected.Id ||
                persisted.ExecutionId != expected.ExecutionId || persisted.ActionId != expected.ActionId ||
                persisted.Type != RemediationType.PermissionRequest || persisted.RequestingComponentId != ComponentId ||
                persisted.Title != expected.Title || persisted.Explanation != expected.Explanation || persisted.ProviderName != expected.ProviderName ||
                persisted.CanResume != expected.CanResume || persisted.RequiredInputs.Count != 0 || !persisted.AllowedActions.SequenceEqual(expected.AllowedActions))
                throw new UnauthorizedAccessException("The actual remediation no longer matches this source-owned Ask.");
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                if (record.Approved != true || record.Approval is null)
                    throw new UnauthorizedAccessException("No privately admitted explicit approval exists.");
                record.ApprovalValidated = true;
            }
            actual = CallOriginal(() => _remediation.ApproveAndResolveAsync(record.Id, _ownerToken));
            var metadata = await ObserveAsync(actual).ConfigureAwait(false);
            Task<PermissionDecision>? decision; lock (_sync) decision = record.Decision;
            if (decision is null) throw new UnauthorizedAccessException("Completed remediation supplied no original explicit-decision callback.");
            var resolved = await ObserveAsync(decision).ConfigureAwait(false);
            if (resolved.Kind != PermissionDecisionKind.Allowed)
                throw new UnauthorizedAccessException("This original remote-use request was not granted.");
            completion.TrySetResult(metadata); // Metadata is returned only after the separate real grant result.
        }
        catch (Exception error) { completion.TrySetException((Exception?)actual?.Exception ?? error); }
        finally { ExitOriginal(phase); }
    }
    public Task<PermissionDecision> DenyOriginalAsync(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Binding record; TaskCompletionSource<PermissionDecision> completion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this); record = RequireRecord(id);
            if (record.Denial is not null) return record.Denial;
            if (record.Approved == true) throw new InvalidOperationException("This original explicit approval is already admitted.");
            record.Approved = false; completion = new(TaskCreationOptions.RunContinuationsAsynchronously); record.Denial = completion.Task;
        }
        _ = DenyPublishedAsync(record, completion);
        return completion.Task;
    }
    private async Task DenyPublishedAsync(Binding record, TaskCompletionSource<PermissionDecision> completion)
    {
        var phase = EnterOriginal(completion.Task);
        Task<PermissionDecision>? actual = null;
        try
        {
            await ObserveAsync(record.Publication).ConfigureAwait(false);
            actual = StartOriginalDecision(record, approved: false);
            var denied = await ObserveAsync(actual).ConfigureAwait(false);
            _continuations.Remove(record.Id); // SAME actual registry: no late approval callback or leaked denied continuation.
            completion.TrySetResult(denied);
        }
        catch (Exception error) { completion.TrySetException((Exception?)actual?.Exception ?? error); }
        // Current generic coordinator has no Cancel method. This own Denied result seals the
        // response without Grant; Root's owner-aware card uses CanRespond/result, not Waiting/Completed.
        finally { ExitOriginal(phase); }
    }
    private Task<RemediationContinuationResult> OnOriginalResolutionAsync(Binding record, RemediationResolution resolution)
    {
        TaskCompletionSource<RemediationContinuationResult> completion;
        lock (_sync)
        {
            if (record.Callback is { } existing) return existing;
            if (_closing) return Task.FromResult(new RemediationContinuationResult(false, "The original permission owner is closed; no grant was issued."));
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); record.Callback = completion.Task;
        }
        _ = ResolveCallbackPublishedAsync(record, resolution, completion);
        return completion.Task;
    }
    private async Task ResolveCallbackPublishedAsync(Binding record, RemediationResolution resolution,
        TaskCompletionSource<RemediationContinuationResult> completion)
    {
        var phase = EnterOriginal(completion.Task);
        Task<RemediationContinuationResult>? actual = null;
        try
        {
            lock (_sync)
                if (!resolution.Approved || record.Approved != true || record.Approval is null || !record.ApprovalValidated)
                    throw new UnauthorizedAccessException("No matching original explicit owner approval was admitted.");
            // A denied generic callback has its own published fault task; generic registry
            // redaction cannot erase this actual cause from owner close custody.
            var decision = StartOriginalDecision(record, resolution.Approved);
            actual = ProjectResolutionAsync(decision);
            completion.TrySetResult(await ObserveAsync(actual).ConfigureAwait(false));
        }
        catch (Exception error) { completion.TrySetException((Exception?)actual?.Exception ?? error); }
        finally { ExitOriginal(phase); }
    }
    private static async Task<RemediationContinuationResult> ProjectResolutionAsync(Task<PermissionDecision> actual)
    {
        var result = await ObserveAsync(actual).ConfigureAwait(false);
        return new(result.Kind == PermissionDecisionKind.Allowed,
            result.Kind == PermissionDecisionKind.Allowed
                ? "Exact task/run/model permission recorded. No provider operation was started; request fresh admission explicitly."
                : "The original remote-use request was declined. No permission was granted.");
    }
    private Task<PermissionDecision> StartOriginalDecision(Binding record, bool approved)
    {
        TaskCompletionSource<PermissionDecision> completion;
        lock (_sync)
        {
            if (record.Decision is not null)
            {
                if (record.Approved != approved) throw new InvalidOperationException("The original explicit decision cannot be replaced.");
                return record.Decision;
            }
            ObjectDisposedException.ThrowIf(_closing, this);
            if (record.Approved != approved || (approved ? record.Approval is null || !record.ApprovalValidated : record.Denial is null))
                throw new UnauthorizedAccessException("Only the matching privately admitted explicit owner response may create the decision.");
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); record.Decision = completion.Task;
        }
        _ = DecidePublishedAsync(record, approved, completion);
        return completion.Task;
    }
    private async Task DecidePublishedAsync(Binding record, bool approved, TaskCompletionSource<PermissionDecision> completion)
    {
        var phase = EnterOriginal(completion.Task);
        Task<PermissionDecision>? actual = null;
        try
        {
            await DemandCurrentAsync(record, capture: false).ConfigureAwait(false);
            _ownerToken.ThrowIfCancellationRequested();
            actual = CallOriginal(() => _source.ResolveOriginalRequestAsync(record.Original, approved,
                token => DemandFinalOriginalCurrentAsync(record, token), _ownerToken));
            lock (_sync) record.OriginalSourceDecision = actual; // Retain SAME source task separately from its later event.
            var recorded = await ObserveAsync(actual).ConfigureAwait(false); // Real source decision ACK precedes telemetry.
            await ObserveAsync(StartOriginalCanonicalEvent(record, recorded)).ConfigureAwait(false);
            completion.TrySetResult(recorded);
        }
        catch (Exception error) { completion.TrySetException((Exception?)actual?.Exception ?? error); }
        finally { ExitOriginal(phase); }
    }
    private async Task<TaskExecutionSnapshot> DemandCurrentAsync(Binding record, bool capture)
    {
        if (!_source.IsIssuedOriginalRequest(record.Original)) throw new UnauthorizedAccessException("The original Ask issuer retired.");
        var original = record.Original;
        var current = await ObserveAsync(CallOriginal(() => _tasks().GetAsync(original.OriginalOwner.TaskId, _ownerToken))).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original canonical Task is unavailable.");
        await ObserveAsync(CallOriginal(() => _authority.ValidateOriginalCloudPermissionAsync(current,
            original.OriginalOwner, original.OriginalCandidate, _ownerToken))).ConfigureAwait(false);
        var latest = await ObserveAsync(CallOriginal(() => _tasks().GetAsync(current.TaskId, _ownerToken))).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The canonical Task disappeared during permission validation.");
        if (latest.OwnerBinding != current.OwnerBinding || latest.ContextId != current.ContextId || latest.ExecutionId != current.ExecutionId ||
            latest.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled or TaskExecutionLifecycle.Failed)
            throw new UnauthorizedAccessException("The original Task/run owner changed during permission validation.");
        var attempt = latest.Attempts.LastOrDefault()?.Id;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (capture && !record.Captured) { record.ExpectedAttempt = attempt; record.Captured = true; }
            else if (!record.Captured || record.ExpectedAttempt != attempt)
                throw new UnauthorizedAccessException("The pending request's original attempt changed.");
        }
        _ownerToken.ThrowIfCancellationRequested(); return latest;
    }
    private async Task DemandFinalOriginalCurrentAsync(Binding record, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await DemandCurrentAsync(record, capture: false).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }
    /// <summary>Detached display observation from an actual successful original publication;
    /// this never supplies actor, task, route, effect or continuation authority.</summary>
    public TaskExecutionOwnerBinding? GetOriginalOwner(Guid id)
    {
        lock (_sync)
            return _ids.TryGetValue(id, out var record) && record.Publication.IsCompletedSuccessfully
                ? record.Original.OriginalOwner : null;
    }
    /// <summary>Exact label from the privately retained source candidate; display only.</summary>
    public string? GetOriginalModelLabel(Guid id)
    {
        lock (_sync)
            return _ids.TryGetValue(id, out var record) && record.Publication.IsCompletedSuccessfully
                ? record.Original.OriginalCandidate.ProviderId + "/" + record.Original.OriginalCandidate.ModelId : null;
    }

    private Task StartOriginalCanonicalEvent(Binding record, PermissionDecision? decision)
    {
        // The exact six-argument legacy constructor keeps its original observation path.
        // Root's production seven-argument factory supplies SAME actual event sink explicitly.
        if (_canonicalEvents is null) return Task.CompletedTask;
        TaskCompletionSource completion;
        lock (_sync)
        {
            var existing = decision is null ? record.RequestEvent : record.DecisionEvent;
            if (existing is not null) return existing;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (decision is null) record.RequestEvent = completion.Task; else record.DecisionEvent = completion.Task;
        }
        PublishCanonicalEvent(record, decision, completion); // Exact original published before sink callback.
        return completion.Task;
    }
    private void PublishCanonicalEvent(Binding record, PermissionDecision? decision, TaskCompletionSource completion)
    {
        var phase = EnterOriginal(completion.Task);
        try
        {
            var owner = record.Original.OriginalOwner;
            var now = DateTimeOffset.UtcNow;
            var eventKind = decision is null ? "request" : "decision";
            var decisionKind = decision?.Kind.ToString() ?? "Ask";
            var entry = new ExecutionEvent(Guid.NewGuid(), owner.ExecutionId, record.ActionId, null,
                ExecutionOrigin.Haven, decision?.Kind == PermissionDecisionKind.Denied
                    ? ExecutionActionType.PermissionDenied : ExecutionActionType.UserActionRequired,
                ExecutionActionStatus.Suspended, "Task remote-use permission " + eventKind,
                "Only the permission response is " + (decision is null ? "waiting" : "recorded") +
                    "; permission admission stays paused and no operation resumes automatically. The canonical Task owner separately records run suspension.",
                decision is null ? "A source-issued explicit task/run/model decision is required."
                    : "Permission decision: " + decisionKind + ". No provider, tool, egress, budget or work completion is implied.",
                ComponentId, now, now, decision is null ? null : now,
                RemediationId: record.Id, TaskId: owner.TaskId,
                SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["eventKind"] = "task-cloud-permission", ["permissionPhase"] = eventKind,
                    ["permissionDecision"] = decisionKind,
                    ["permissionResponseStatus"] = decision is null ? "waiting" : "completed",
                    ["taskRunStatus"] = "not-resumed", ["taskRunCompletion"] = "not-implied"
                });
            if (!CallOriginal(() => _canonicalEvents!.TryPublish(entry)))
                throw new InvalidOperationException("The actual canonical permission event sink did not acknowledge publication; no response replay is implied.");
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
        finally { ExitOriginal(phase); }
    }
    public void RetireResolvedOriginal(Guid id)
    {
        lock (_sync)
        {
            var record = RequireRecord(id);
            if (!record.Publication.IsCompletedSuccessfully || record.Decision is not { IsCompletedSuccessfully: true } ||
                record.Callback is { IsCompletedSuccessfully: false } ||
                record.RequestEvent is { IsCompletedSuccessfully: false } || record.DecisionEvent is { IsCompletedSuccessfully: false } ||
                (record.Approval is not { IsCompletedSuccessfully: true } && record.Denial is not { IsCompletedSuccessfully: true }))
                throw new InvalidOperationException("Only all-successful actual original response tasks may retire.");
            if (record.Continuation is not null &&
                (!record.Continuation.IsCompletedSuccessfully || record.ContinuationLease is not { SuccessfullyClosed: true } ||
                 _continuationOperations.Any(value => ReferenceEquals(value.Binding, record) && (!value.Published.IsCompletedSuccessfully || !value.Runner.IsCompletedSuccessfully))))
                throw new InvalidOperationException("Live, failed or unknown original continuation custody cannot retire.");
            _source.RetireResolvedOriginalRequest(record.Original);
            if (record.ContinuationLease is { } lease) _continuationLeases.Remove(lease);
            _requests.Remove(record.Original); _ids.Remove(record.Id);
        }
    }
    /// <summary>Display only: an actual successful source decision can be known even when its
    /// later telemetry failed. It supplies no continuation, effect or domain authority.</summary>
    public PermissionDecision? GetAcknowledgedOriginalDecision(Guid id)
    {
        lock (_sync)
            return _ids.GetValueOrDefault(id)?.OriginalSourceDecision is { IsCompletedSuccessfully: true } original
                ? original.Result : null;
    }

    /// <summary>The retained original decision Task is separate from metadata Completed. This is
    /// an observation of grant/denial work, not an effect/egress/continuation authority.</summary>
    public Task<PermissionDecision>? GetOriginalDecision(Guid id)
    { lock (_sync) return _ids.GetValueOrDefault(id)?.Decision; }

    private Binding RequireRecord(Guid id) => _ids.GetValueOrDefault(id)
        ?? throw new UnauthorizedAccessException("No original cloud-permission binding exists for this ID.");
    public Task CloseAndDrainAsync()
    {
        RefuseOriginalSelfJoin(); // Also checked before returning an already-published SAME close.
        TaskCompletionSource completion; Task[] originals;
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true; completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = completion.Task;
            originals = _requests.Values.SelectMany(value => new Task?[] { value.Publication, value.Approval, value.Denial, value.Decision, value.Callback, value.RequestEvent, value.DecisionEvent, value.Continuation })
                .Where(value => value is not null).Cast<Task>()
                .Concat(_continuationOperations.SelectMany(value => new[] { value.Published, value.Runner }))
                .Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        }
        _ = ClosePublishedAsync(originals, completion); return completion.Task;
    }
    private async Task ClosePublishedAsync(Task[] originals, TaskCompletionSource completion)
    {
        var errors = new List<Exception>();
        try { CallOriginal(_stop.Cancel); } catch (Exception error) { Add(errors, error); }
        foreach (var actual in originals)
        {
            try { await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                var payload = actual.Exception;
                if (payload is null) Add(errors, error); else foreach (var cause in payload.InnerExceptions) Add(errors, cause);
            }
        }
        Task? continuationClose = null;
        try
        {
            continuationClose = CloseOriginalContinuationLifetimesAsync(errors);
            await ObserveAsync(continuationClose).ConfigureAwait(false);
        }
        catch (Exception error) { AddContinuationCauses(errors, error, continuationClose); }
        try { _stop.Dispose(); } catch (Exception error) { Add(errors, error); }
        if (errors.Count == 0) completion.TrySetResult();
        else completion.TrySetException(new AggregateException("Original cloud-permission tasks and cleanup failed.", errors));
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static void Add(List<Exception> errors, Exception original)
    { if (!errors.Any(value => ReferenceEquals(value, original))) errors.Add(original); }
    private static async Task<T> ObserveAsync<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { ExceptionDispatchInfo.Capture(actual.Exception!).Throw(); throw; }
    }
    private static async Task ObserveAsync(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { ExceptionDispatchInfo.Capture(actual.Exception!).Throw(); throw; }
    }
}
