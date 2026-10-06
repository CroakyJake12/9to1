using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class ResilientProviderRoutingModelClient
{
    private readonly object _originalRequestFailureSync = new();
    private readonly ConditionalWeakTable<object, OriginalRequestFailureSlot> _originalRequestFailures = new();
    private readonly ConditionalWeakTable<TaskRunOriginalRequestFailure, OriginalRequestFailureBody> _issuedOriginalRequestFailures = new();

    private readonly ConditionalWeakTable<TaskRunOriginalFinalRequestFailure, OriginalRequestFailureBody> _issuedOriginalFinalFailures = new();

    public TaskRunOriginalFinalRequestFailure? TryGetOriginalFinalRequestFailure(OllamaToolRequest sameRequest,
        Exception sameOutwardFailure) => TryGetOriginalFinalFiniteFailure(sameRequest, sameOutwardFailure);
    public TaskRunOriginalFinalRequestFailure? TryGetOriginalFinalRequestFailure(OllamaChatRequest sameRequest,
        Exception sameOutwardFailure) => TryGetOriginalFinalFiniteFailure(sameRequest, sameOutwardFailure);
    private TaskRunOriginalFinalRequestFailure? TryGetOriginalFinalFiniteFailure(object request, Exception outward)
    {
        if (request is null || outward is null || originalFrames is null) return null;
        OriginalRequestFailureBody? body;
        lock (_originalRequestFailureSync) body = GetOriginalTerminalFailure(request, outward);
        if (body is null || !originalFrames.ValidateProviderFailureObservation(body.Observation!, body.Admission!)) return null;
        lock (_originalRequestFailureSync)
        {
            if (!ReferenceEquals(GetOriginalTerminalFailure(request, outward), body)) return null;
            var binding = new OriginalFinalFailure(request, outward, body.Observation!, body.Admission!);
            _issuedOriginalFinalFailures.Add(binding, body);
            return binding;
        }
    }
    public bool IsIssuedOriginalFinalRequestFailure(TaskRunOriginalFinalRequestFailure binding, object sameRequest,
        Exception sameOutwardFailure)
    {
        if (binding is null || sameRequest is null || sameOutwardFailure is null || originalFrames is null) return false;
        OriginalRequestFailureBody? body;
        lock (_originalRequestFailureSync)
        {
            if (!_issuedOriginalFinalFailures.TryGetValue(binding, out body)
                || !ReferenceEquals(GetOriginalTerminalFailure(sameRequest, sameOutwardFailure), body)
                || !ReferenceEquals(binding.OriginalCallerRequest, sameRequest)
                || !ReferenceEquals(binding.OriginalOutwardFailure, sameOutwardFailure)
                || !ReferenceEquals(binding.OriginalAdmission, body.Admission)
                || !ReferenceEquals(binding.OriginalObservation, body.Observation)) return false;
        }
        if (!originalFrames.ValidateProviderFailureObservation(body.Observation!, body.Admission!)) return false;
        lock (_originalRequestFailureSync)
            return ReferenceEquals(GetOriginalTerminalFailure(sameRequest, sameOutwardFailure), body);
    }

    public TaskRunOriginalRequestFailure? TryGetOriginalRequestFailure(OllamaChatRequest sameRequest,
        TaskRunAttemptAdmission sameAdmission, Exception sameOutwardFailure) =>
        TryGetOriginalFiniteRequestFailure(sameRequest, sameAdmission, sameOutwardFailure);
    public TaskRunOriginalRequestFailure? TryGetOriginalRequestFailure(OllamaToolRequest sameRequest,
        TaskRunAttemptAdmission sameAdmission, Exception sameOutwardFailure) =>
        TryGetOriginalFiniteRequestFailure(sameRequest, sameAdmission, sameOutwardFailure);

    private TaskRunOriginalRequestFailure? TryGetOriginalFiniteRequestFailure(object request,
        TaskRunAttemptAdmission admission, Exception outward)
    {
        if (request is null || admission is null || outward is null
            || originalFrames is not ITaskRunOriginalFailedAttemptSettlementSource source) return null;
        OriginalRequestFailureBody? body;
        lock (_originalRequestFailureSync)
        {
            body = GetOriginalTerminalFailure(request, admission, outward);
            if (body is null) return null;
        }
        // No router gate is held while consulting the separate actual frame owner.
        var settled = source.TryGetOriginalFailedAttemptSettlement(body.Observation!, admission);
        if (settled is null) return null;
        lock (_originalRequestFailureSync)
        {
            if (!ReferenceEquals(GetOriginalTerminalFailure(request, admission, outward), body)) return null;
            var receipt = new OriginalSettledRequestFailure(request, outward, settled);
            _issuedOriginalRequestFailures.Add(receipt, body);
            return receipt;
        }
    }

    public bool IsIssuedOriginalRequestFailure(TaskRunOriginalRequestFailure receipt, object sameRequest,
        TaskRunAttemptAdmission sameAdmission, Exception sameOutwardFailure)
    {
        if (receipt is null || sameRequest is null || sameAdmission is null || sameOutwardFailure is null
            || originalFrames is not ITaskRunOriginalFailedAttemptSettlementSource source) return false;
        OriginalRequestFailureBody? body;
        lock (_originalRequestFailureSync)
        {
            if (!_issuedOriginalRequestFailures.TryGetValue(receipt, out body)
                || !ReferenceEquals(GetOriginalTerminalFailure(sameRequest, sameAdmission, sameOutwardFailure), body)
                || !ReferenceEquals(receipt.OriginalCallerRequest, sameRequest)
                || !ReferenceEquals(receipt.OriginalOutwardFailure, sameOutwardFailure)) return false;
        }
        if (!source.IsIssuedOriginalFailedAttemptSettlement(receipt.OriginalFailedAttempt, body.Observation!, sameAdmission)) return false;
        lock (_originalRequestFailureSync)
            return ReferenceEquals(GetOriginalTerminalFailure(sameRequest, sameAdmission, sameOutwardFailure), body);
    }

    private RoutingState CreateOriginalFailureRoutingState(object request, ProviderExecutionContext? context, CancellationToken caller,
        IReadOnlySet<ToolCapability> required, IReadOnlyCollection<RestrictedModelCapability> restrictions)
    {
        var state = new RoutingState(context);
        if (context is null) return state; // Ordinary/free calls gain no canonical source binding.
        lock (_originalRequestFailureSync)
        {
            var slot = _originalRequestFailures.GetValue(request, _ => new());
            if (slot.Current is { } previous) previous.Invalidated = true;
            slot.ActiveCount++;
            var body = new OriginalRequestFailureBody(request, caller, slot, required.ToArray(), restrictions.ToArray()) { Invalidated = slot.ActiveCount != 1 };
            slot.Current = body;
            state.OriginalRequestFailure = body;
        }
        return state;
    }

    private void ClearOriginalRequestFailure(RoutingState state)
    {
        if (state.OriginalRequestFailure is not { } body) return;
        lock (_originalRequestFailureSync)
        { body.Observation = null; body.Admission = null; body.ExpectedOutward = null; body.ToolDispatch = null; body.ToolDispatchFrame = null; }
    }
    private void RecordOriginalAcknowledgedRequestFailure(RoutingState state, TaskRunOriginalFailureObservation? observation)
    {
        if (state.OriginalRequestFailure is not { } body || observation is null || state.Admission is not { } admission) return;
        lock (_originalRequestFailureSync)
        {
            body.Observation = observation; body.Admission = admission;
            if (body.ToolDispatch is { } invocation
                && ReferenceEquals(invocation.Admission, admission)
                && ReferenceEquals(invocation.Frame, observation.OriginalFrame))
                invocation.Observation = observation;
        }
    }
    private Exception CreateOriginalExhaustedRequestFailure(RoutingState state, string message, Exception? firstCause)
    {
        var outward = new InvalidOperationException(message, firstCause);
        if (state.OriginalRequestFailure is { } body)
            lock (_originalRequestFailureSync)
                if (body.Observation is not null && ReferenceEquals(body.Admission, state.Admission)) body.ExpectedOutward = outward;
        return outward;
    }
    private void ObserveOriginalFiniteRequestFailure(RoutingState state, Exception outward)
    {
        if (state.OriginalRequestFailure is not { } body) return;
        lock (_originalRequestFailureSync)
        {
            if (ReferenceEquals(body.ExpectedOutward, outward)) body.Outward = outward;
            else body.Invalidated = true;
        }
    }
    private void EndOriginalFiniteRequest(RoutingState state)
    {
        if (state.OriginalRequestFailure is not { } body) return;
        lock (_originalRequestFailureSync)
        {
            body.Active = false;
            body.Slot.ActiveCount--;
            if (body.Outward is null) body.Invalidated = true;
        }
    }
    private OriginalRequestFailureBody? GetOriginalTerminalFailure(object request, TaskRunAttemptAdmission admission, Exception outward)
    {
        var body = GetOriginalTerminalFailure(request, outward);
        return body is not null && ReferenceEquals(body.Admission, admission) ? body : null;
    }
    private OriginalRequestFailureBody? GetOriginalTerminalFailure(object request, Exception outward)
    {
        if (!_originalRequestFailures.TryGetValue(request, out var slot) || slot.ActiveCount != 0
            || slot.Current is not { Active: false, Invalidated: false } body
            || body.Caller.IsCancellationRequested || body.Observation is null || body.Admission is null
            || !ReferenceEquals(body.Request, request)
            || !ReferenceEquals(body.Outward, outward)) return null;
        return body;
    }
    private sealed class OriginalFinalFailure(object request, Exception outward,
        TaskRunOriginalFailureObservation observation, TaskRunAttemptAdmission admission) : TaskRunOriginalFinalRequestFailure
    {
        public object OriginalCallerRequest => request;
        public Exception OriginalOutwardFailure => outward;
        public TaskRunOriginalFailureObservation OriginalObservation => observation;
        public TaskRunAttemptAdmission OriginalAdmission => admission;
    }
    private sealed class OriginalSettledRequestFailure(object request, Exception outward,
        TaskRunOriginalFailedAttemptSettlement settled) : TaskRunOriginalRequestFailure
    {
        public object OriginalCallerRequest => request;
        public Exception OriginalOutwardFailure => outward;
        public TaskRunOriginalFailedAttemptSettlement OriginalFailedAttempt => settled;
    }
    private sealed class OriginalRequestFailureSlot
    {
        public int ActiveCount;
        public OriginalRequestFailureBody? Current;
    }
    private sealed class OriginalRequestFailureBody(object request, CancellationToken caller, OriginalRequestFailureSlot slot,
        ToolCapability[] required, RestrictedModelCapability[] restrictions)
    {
        public readonly object Request = request;
        public readonly CancellationToken Caller = caller;
        public readonly OriginalRequestFailureSlot Slot = slot;
        public readonly ToolCapability[] Required = required;
        public readonly RestrictedModelCapability[] Restrictions = restrictions;
        public readonly List<OriginalCheckpointSelectionBody> CheckpointSelections = [];
        public bool Active = true, Invalidated;
        public TaskRunAttemptAdmission? Admission;
        public TaskRunOriginalFailureObservation? Observation;
        public OriginalToolDispatchInvocation? ToolDispatch;
        public Task<OllamaToolResponse>? ToolDispatchFrame;
        public readonly List<OriginalToolDispatchInvocation> ToolDispatchInvocations = [];
        public bool ToolDispatchCohortUnknown;
        public Exception? ExpectedOutward, Outward;
    }
}
