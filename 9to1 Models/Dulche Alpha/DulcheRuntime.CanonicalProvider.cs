using Haven.Application;

namespace Dulche.Runtime;

/// <summary>Trusted adapter composition binds a real issued Task/Run before the original request is enqueued.</summary>
public interface IDulcheOriginalProviderAdapter : IDulcheAdapter
{
    Uri OriginalConfiguredTarget { get; }
    Task BindOriginalRequestAsync(DulcheEndpoint endpoint, DulcheRequest originalFrozenRequest,
        RuntimeRequestHandle originalHandle, TaskRunAttemptAdmission originalAdmission,
        Guid originalActionId, CancellationToken cancellationToken);
    void CaptureOriginalDispatchRequest(RuntimeRequestHandle originalHandle,
        DulcheRequest originalFrozenRequest, DulcheRequest actualDispatchRequest);
}

/// <summary>Private source-issued cancellation evidence changes response status only; it never settles or authorizes a Task/Run.</summary>
public interface IDulcheOriginalCancellationSource
{
    bool TryObserveOriginalCancellation(RuntimeRequestHandle originalHandle, Exception originalOutwardFailure,
        CancellationToken originalOwnerCancellation, out DulcheOriginalCancellationObservation? observation);
}

public sealed class DulcheOriginalCancellationObservation
{
    internal DulcheOriginalCancellationObservation(RuntimeRequestHandle handle, AggregateException error,
        Task reader, Exception readerCause, Task raw, Exception rawCause, Task frame, Task producer,
        Task terminal, Task readerDispose, Task cancel, Task release, Task resourceClose,
        Task outwardMove, Task outwardDispose, CancellationToken ownerToken, object turn)
    {
        OriginalHandle = handle; OriginalOutwardFailure = error; OriginalReaderMove = reader;
        OriginalReaderCause = readerCause; OriginalRawMove = raw; OriginalRawCause = rawCause;
        OriginalProviderFrame = frame; OriginalProducer = producer; OriginalTerminalObserver = terminal;
        OriginalReaderDispose = readerDispose; OriginalTurnCancel = cancel; OriginalTurnRelease = release;
        OriginalResourceClose = resourceClose; OriginalInnerMove = outwardMove;
        OriginalInnerDispose = outwardDispose; OwnerToken = ownerToken; Turn = turn;
    }
    public RuntimeRequestHandle OriginalHandle { get; }
    public AggregateException OriginalOutwardFailure { get; }
    public Task OriginalReaderMove { get; }
    public Exception OriginalReaderCause { get; }
    public Task OriginalRawMove { get; }
    public Exception OriginalRawCause { get; }
    public Task OriginalProviderFrame { get; }
    public Task OriginalProducer { get; }
    public Task OriginalTerminalObserver { get; }
    public Task OriginalReaderDispose { get; }
    public Task OriginalTurnCancel { get; }
    public Task OriginalTurnRelease { get; }
    public Task OriginalResourceClose { get; }
    public Task OriginalInnerMove { get; }
    public Task OriginalInnerDispose { get; }
    internal CancellationToken OwnerToken { get; }
    internal object Turn { get; }
}

public sealed partial class DulcheRuntime
{
    private readonly AsyncLocal<OriginalEndpointPhase?> _originalSubmitting = new();
    private readonly AsyncLocal<OriginalEndpointPhase?> _originalExecutingEndpoint = new();
    private readonly AsyncLocal<OriginalEndpointPhase?> _originalRetiringEndpoint = new();
    [ThreadStatic] private static List<EndpointSlot>? _physicalOriginalEndpointCalls;

    private sealed class OriginalEndpointPhase(EndpointSlot owner, OriginalEndpointPhase? parent)
    {
        public EndpointSlot Owner { get; } = owner;
        public OriginalEndpointPhase? Parent { get; } = parent;
        private int _live = 1;
        public bool IsLive => Volatile.Read(ref _live) != 0;
        public void Retire() => Interlocked.Exchange(ref _live, 0);
    }
    private bool IsLiveOriginalEndpointCall(EndpointSlot endpoint)
        => IsLiveEndpointPhase(_originalSubmitting.Value, endpoint)
            || IsLiveEndpointPhase(_originalExecutingEndpoint.Value, endpoint)
            || IsLiveEndpointPhase(_originalRetiringEndpoint.Value, endpoint)
            || IsLiveEndpointPhase(_originalStartingEndpoint.Value, endpoint)
            || _physicalOriginalEndpointCalls?.Any(actual => ReferenceEquals(actual, endpoint)) == true;
    private static bool IsLiveEndpointPhase(OriginalEndpointPhase? phase, EndpointSlot endpoint)
    {
        for (; phase is not null; phase = phase.Parent)
            if (phase.IsLive && ReferenceEquals(phase.Owner, endpoint)) return true;
        return false;
    }
    private static T InvokePhysicalOriginalEndpoint<T>(EndpointSlot endpoint, Func<T> callback)
    {
        var calls = _physicalOriginalEndpointCalls ??= new(); calls.Add(endpoint);
        try { return callback(); }
        finally { calls.RemoveAt(calls.Count - 1); }
    }
    private async Task<bool> TryProjectOriginalManagedCancellationAsync(EndpointSlot endpoint, RequestSlot slot,
        Exception error, CancellationToken ownerToken)
    {
        if (!ownerToken.IsCancellationRequested || slot.OriginalProviderHandle is not { } handle
            || endpoint.Adapter is not IDulcheOriginalCancellationSource source) return false;
        var admission = ReferenceEquals(slot.OriginalManagedRequestCancellationHandle, handle)
            ? slot.OriginalManagedRequestCancellationAdmission
            : ReferenceEquals(endpoint.OriginalManagedCancellationHandle, handle)
                ? endpoint.OriginalManagedCancellationAdmission : null;
        if (admission is null) return false;
        // The SAME finite stage is published BEFORE callbacks and never joins provider work.
        try { await admission.Task.ConfigureAwait(false); }
        catch { return false; } // Exact faults remain on the stage and existing endpoint original-work ledger.
        var cancel = ReferenceEquals(slot.OriginalManagedRequestCancellationAdmission, admission)
            ? slot.OriginalManagedRequestAdapterCancel : endpoint.OriginalAdapterCancel;
        if (cancel is null) return false;
        try { await cancel.ConfigureAwait(false); }
        catch { return false; }
        if (!cancel.IsCompletedSuccessfully || !cancel.Result.Succeeded) return false;
        // These are the actual PUBLIC adapter iterator originals acquired by this Runtime,
        // distinct from the adapter's retained inner iterator/opaque source observation.
        if (slot.OriginalAdapterConsumerMove is not { IsFaulted: true } publicMove
            || slot.OriginalAdapterConsumerDispose is not { IsCompletedSuccessfully: true } publicDispose
            || publicMove.Exception?.InnerExceptions.Any(cause => ReferenceEquals(cause, error)) != true) return false;
        if (!source.TryObserveOriginalCancellation(handle, error, ownerToken, out var observation)
            || observation is null) return false;
        return endpoint.TryRetainOriginalManagedCancellation(observation, publicMove, publicDispose);
    }

    private async IAsyncEnumerable<AdapterDelta> CaptureOriginalAdapterConsumptionAsync(RequestSlot slot,
        IAsyncEnumerable<AdapterDelta> originalSource,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ownerToken)
    {
        var originalEnumerator = originalSource.GetAsyncEnumerator(ownerToken);
        Task<bool>? originalMove = null;
        var failures = new List<Exception>();
        try
        {
            while (true)
            {
                bool more;
                try
                {
                    originalMove = originalEnumerator.MoveNextAsync().AsTask();
                    slot.OriginalAdapterConsumerMove = originalMove;
                    more = await originalMove.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    AddOriginalStopCause(failures, error);
                    if (originalMove?.Exception is { } compound)
                        foreach (var cause in compound.InnerExceptions) AddOriginalStopCause(failures, cause);
                    break;
                }
                if (!more) break;
                yield return originalEnumerator.Current;
            }
        }
        finally
        {
            Task? originalDispose = null;
            try
            {
                originalDispose = originalEnumerator.DisposeAsync().AsTask();
                slot.OriginalAdapterConsumerDispose = originalDispose;
            }
            catch (Exception error) { AddOriginalStopCause(failures, error); }
            if (originalDispose is not null) await JoinOriginalStopTaskAsync(originalDispose, failures).ConfigureAwait(false);
            ThrowOriginalStopCauses(failures); // No replacement of the actual public failed move or its full payload.
        }
    }

    private Task<OperationResult<Unit>> StartOriginalManagedRequestCancellation(EndpointSlot endpoint, RequestSlot slot,
        bool originalReplacementIntent = false)
        => slot.StartOriginalManagedCancellation(endpoint, start => CancelOriginalManagedRequestBodyAsync(endpoint, slot, start), originalReplacementIntent);

    private async Task<OperationResult<Unit>> CancelOriginalManagedRequestBodyAsync(EndpointSlot endpoint,
        RequestSlot slot, Task start)
    {
        var failures = new List<Exception>();
        Task<OperationResult<Unit>>? cancel = null;
        try
        {
            await start.ConfigureAwait(false); // Whole original and finite admission were retained before callbacks.
            try { CancelOriginalRequest(slot); } catch (Exception error) { AddOriginalStopCause(failures, error); }
            try
            {
                cancel = InvokePhysicalOriginalEndpoint(endpoint,
                    () => endpoint.Adapter.CancelAsync(endpoint.Endpoint, slot.RequestId, CancellationToken.None).AsTask());
                slot.OriginalManagedRequestAdapterCancel = cancel;
            }
            catch (Exception error) { AddOriginalStopCause(failures, error); }
        }
        catch (Exception error) { AddOriginalStopCause(failures, error); }
        finally
        {
            if (failures.Count == 0) slot.OriginalManagedRequestCancellationAdmission!.TrySetResult();
            else slot.OriginalManagedRequestCancellationAdmission!.TrySetException(failures);
        }
        if (cancel is not null) await JoinOriginalStopTaskAsync(cancel, failures).ConfigureAwait(false);
        ThrowOriginalStopCauses(failures);
        return cancel?.IsCompletedSuccessfully == true ? cancel.Result
            : Failure<Unit>(DulcheErrorCode.ProviderUnavailable, "No original request cancellation task was acquired.", slot.RequestId);
    }

    private void CancelOriginalRequest(RequestSlot request)
    {
        if (_endpoints.TryGetValue(request.EndpointId, out var endpoint))
            InvokePhysicalOriginalEndpoint(endpoint, () => { request.Cancellation.Cancel(); return true; });
        else request.Cancellation.Cancel(); // No endpoint exists that a callback could join.
    }

    private async Task ProcessOneWithOriginalEndpointContextAsync(EndpointSlot endpoint, RequestSlot slot, Task start)
    {
        await start.ConfigureAwait(false);
        var previous = _originalExecutingEndpoint.Value;
        var phase = new OriginalEndpointPhase(endpoint, previous); _originalExecutingEndpoint.Value = phase;
        Task? original = null;
        try
        {
            original = ProcessOneOriginalAsync(endpoint, slot, start);
            await original.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            AddOriginalStopCause(failures, error);
            if (original?.Exception is { } compound)
                foreach (var cause in compound.InnerExceptions) AddOriginalStopCause(failures, cause);
            ThrowOriginalStopCauses(failures);
            throw;
        }
        finally { phase.Retire(); _originalExecutingEndpoint.Value = previous; }
    }

    /// <summary>Starts the configured managed provider. Ready means actual provider health, never model residency.</summary>
    public Task<OperationResult<DulcheEndpoint>> StartManagedProviderAsync(string providerId,
        CancellationToken cancellationToken = default)
    {
        if (!_adapters.TryGetValue(providerId, out var registered) || registered is not IDulcheOriginalProviderAdapter adapter)
            return Task.FromResult(Failure<DulcheEndpoint>(DulcheErrorCode.ProviderUnavailable,
                "No original managed provider is composed.", providerId));
        var target = adapter.OriginalConfiguredTarget;
        if (!target.IsAbsoluteUri || target.Scheme is not ("https" or "http")
            || target.Scheme != Uri.UriSchemeHttps && !target.IsLoopback)
            return Task.FromResult(Failure<DulcheEndpoint>(DulcheErrorCode.PermissionDenied,
                "The actual configured provider target is unavailable.", providerId));
        return StartCoreAsync(providerId, target.GetLeftPart(UriPartial.Path),
            adapter.IsLocal ? target.Port : null, !adapter.IsLocal, null, cancellationToken);
    }

    /// <summary>Context is the canonical coordinator's exact issued object, not an ID or persisted receipt grant.</summary>
    public Task<OperationResult<RuntimeRequestHandle>> SubmitWithContextAsync(DulcheRequest request,
        string endpointId, TaskRunAttemptAdmission originalAdmission, Guid originalActionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(originalAdmission);
        if (!_endpoints.TryGetValue(endpointId, out var endpoint))
            return Task.FromResult(Failure<RuntimeRequestHandle>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId));
        if (endpoint.Adapter is not IDulcheOriginalProviderAdapter adapter)
            return Task.FromResult(Failure<RuntimeRequestHandle>(DulcheErrorCode.UnsupportedCapability,
                "This adapter does not accept canonical original context.", endpointId));
        cancellationToken.ThrowIfCancellationRequested();
        return endpoint.StartOriginalSubmission(start => SubmitOriginalContextCoreAsync(endpoint, adapter,
            request, originalAdmission, originalActionId, start, cancellationToken));
    }

    private async Task<OperationResult<RuntimeRequestHandle>> SubmitOriginalContextCoreAsync(EndpointSlot endpoint,
        IDulcheOriginalProviderAdapter adapter, DulcheRequest request, TaskRunAttemptAdmission admission,
        Guid actionId, Task start, CancellationToken callerCancellation)
    {
        await start.ConfigureAwait(false);
        var previous = _originalSubmitting.Value;
        var phase = new OriginalEndpointPhase(endpoint, previous); _originalSubmitting.Value = phase;
        CancellationTokenSource? cancellation = null;
        Task<OperationResult<RuntimeRequestHandle>>? originalBody = null;
        OperationResult<RuntimeRequestHandle> result = default!;
        var failures = new List<Exception>();
        try
        {
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, endpoint.Stopping.Token);
            originalBody = SubmitOriginalContextBodyAsync(endpoint, adapter, request, admission, actionId, cancellation);
            result = await originalBody.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            AddOriginalStopCause(failures, error);
            if (originalBody?.Exception is { } compound)
                foreach (var cause in compound.InnerExceptions) AddOriginalStopCause(failures, cause);
        }
        finally
        {
            if (cancellation is not null)
                try { cancellation.Dispose(); } catch (Exception error) { AddOriginalStopCause(failures, error); }
            phase.Retire(); _originalSubmitting.Value = previous;
        }
        ThrowOriginalStopCauses(failures);
        return result;
    }

    private async Task<OperationResult<RuntimeRequestHandle>> SubmitOriginalContextBodyAsync(EndpointSlot endpoint,
        IDulcheOriginalProviderAdapter adapter, DulcheRequest request, TaskRunAttemptAdmission admission,
        Guid actionId, CancellationTokenSource cancellation)
    {
        RequestSlot? slot = null;
        Task? originalBinding = null;
        try
        {
            endpoint.RequireOriginalSubmissionOpen();
            cancellation.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(request.EffectivePrompt))
                return Failure<RuntimeRequestHandle>(DulcheErrorCode.MissingPrompt, "Missing prompt.", "request.input");
            if (request.ContextLimit is <= 0 || request.QueueTimeoutSeconds is <= 0)
                return Failure<RuntimeRequestHandle>(DulcheErrorCode.InvalidArgument, "Context and queue limits must be positive.", "request.limits");
            if (request.Settings is { } settings && ValidateSettings(settings) is { } invalid)
                return OperationResult<RuntimeRequestHandle>.Failure(invalid);
            if (endpoint.Endpoint.State is EndpointState.Stopped or EndpointState.Stopping or EndpointState.Failed)
                return Failure<RuntimeRequestHandle>(DulcheErrorCode.InvalidState, "Endpoint is unavailable.", endpoint.Endpoint.EndpointId);
            var selected = admission.Lease.Candidate;
            if (request.CallerId is { } caller && caller != admission.Lease.Owner.ActorId
                || !StringComparer.OrdinalIgnoreCase.Equals(selected.ProviderId, adapter.ProviderId)
                || request.Model is { } requested && (requested.ProviderId != selected.ProviderId
                    || requested.ModelId != selected.ModelId || requested.ArtifactRevision != selected.ArtifactIdentity))
                return Failure<RuntimeRequestHandle>(DulcheErrorCode.PermissionDenied, "Original caller/model differs from the issued attempt.", "request.context");
            var sessionId = request.SessionId;
            if (sessionId is null)
            {
                var created = CreateSession(endpoint.Endpoint.EndpointId);
                if (!created.Succeeded) return OperationResult<RuntimeRequestHandle>.Failure(created.Error!);
                sessionId = created.Value!.SessionId;
            }
            if (!_sessions.TryGetValue(sessionId, out var session) || session.Session.EndpointId != endpoint.Endpoint.EndpointId)
                return Failure<RuntimeRequestHandle>(DulcheErrorCode.PermissionDenied, "Session is unavailable in this endpoint.", sessionId);
            var frozen = Snapshot(request with
            {
                SessionId = sessionId, CallerId = admission.Lease.Owner.ActorId,
                Model = new(selected.ProviderId, selected.ModelId, selected.ArtifactIdentity),
                Settings = SnapshotSettings(request.Settings ?? _endpointSettings.GetValueOrDefault(endpoint.Endpoint.EndpointId) ?? new())
            });
            slot = new(Guid.NewGuid().ToString("N"), admission.AttemptId.ToString("D"), sessionId,
                endpoint.Endpoint.EndpointId, frozen);
            var actualHandle = Handle(slot);
            slot.OriginalProviderHandle = actualHandle;
            if (!_requests.TryAdd(slot.RequestId, slot))
                return Failure<RuntimeRequestHandle>(DulcheErrorCode.Conflict, "Request identity collision.", slot.RequestId);
            // The enclosing original submission is published in this endpoint before this callback is possible.
            endpoint.RequireOriginalSubmissionOpen();
            originalBinding = InvokePhysicalOriginalEndpoint(endpoint, () => endpoint.CaptureOriginalSubmissionCallback(() =>
                adapter.BindOriginalRequestAsync(endpoint.Endpoint, frozen, actualHandle, admission,
                    actionId, cancellation.Token)))
                ?? throw new InvalidOperationException("No original provider binding task was returned.");
            slot.OriginalProviderBinding = originalBinding;
            await originalBinding.ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!endpoint.Enqueue(slot))
            {
                slot.State = RequestState.Cancelled; slot.FinishReason = FinishReason.Cancelled; slot.Complete();
                return endpoint.IsRetiring
                    ? Failure<RuntimeRequestHandle>(DulcheErrorCode.InvalidState, "Endpoint retirement sealed admission.", endpoint.Endpoint.EndpointId)
                    : Failure<RuntimeRequestHandle>(DulcheErrorCode.QueueFull, "Endpoint request queue is full.", endpoint.Endpoint.EndpointId, true);
            }
            slot.RunTask = endpoint.EnsureWorker(ProcessOneAsync);
            return OperationResult<RuntimeRequestHandle>.Success(actualHandle);
        }
        catch (Exception error)
        {
            if (slot is not null) { slot.State = RequestState.Failed; slot.FinishReason = FinishReason.Error; slot.Complete(); }
            var failures = new List<Exception>();
            AddOriginalStopCause(failures, error);
            if (originalBinding?.Exception is { } compound)
                foreach (var cause in compound.InnerExceptions) AddOriginalStopCause(failures, cause);
            ThrowOriginalStopCauses(failures);
            throw;
        }
    }
}
