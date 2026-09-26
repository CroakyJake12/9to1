using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Dulche.Runtime;

public sealed record RuntimeRequestHandle(string RequestId, string SessionId, string EndpointId, Func<CancellationToken, Task<DulcheResult>> AwaitResult, Func<long, CancellationToken, IAsyncEnumerable<RuntimeEvent>> ReadEvents);

/// <summary>Single shared runtime coordinator. It serializes generation per logical endpoint and isolates sessions.</summary>
public sealed class DulcheRuntime
{
    private readonly ConcurrentDictionary<string, IDulcheAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EndpointSlot> _endpoints = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SessionSlot> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RequestSlot> _requests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DulcheRequest> _preparedPrompts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GenerationSettings> _endpointSettings = new(StringComparer.Ordinal);
    private readonly RuntimeEventHub _eventHub = new();
    private readonly int _maximumQueueDepth;

    public DulcheRuntime(IEnumerable<IDulcheAdapter> adapters, int maximumQueueDepth = 64)
    {
        if (maximumQueueDepth < 1) throw new ArgumentOutOfRangeException(nameof(maximumQueueDepth));
        _maximumQueueDepth = maximumQueueDepth;
        foreach (var adapter in adapters ?? throw new ArgumentNullException(nameof(adapters)))
            if (!_adapters.TryAdd(adapter.ProviderId, adapter)) throw new ArgumentException($"Duplicate provider '{adapter.ProviderId}'.", nameof(adapters));
    }

    public RuntimeCapabilities GetCapabilities() => new("1.0.0-alpha", "1", "1", _adapters.Values.SelectMany(adapter => adapter.Capabilities).Append("sessions").Append("streaming").Append("request-controls").Append("endpoint-health").Append("provider-routing").ToHashSet(StringComparer.OrdinalIgnoreCase), _maximumQueueDepth, _adapters.Values.Any(adapter => adapter.SupportsExactPause), _adapters.Values.Any(adapter => adapter.SupportsExactResume), true, true);

    public async Task<OperationResult<DulcheEndpoint>> StartLocalAsync(string providerId, int port, ModelIdentity? model = null, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535) return Failure<DulcheEndpoint>(DulcheErrorCode.InvalidArgument, "Local endpoints require a valid port.", "port");
        return await StartCoreAsync(providerId, $"http://127.0.0.1:{port}", port, false, model, cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult<DulcheEndpoint>> StartRemoteAsync(string providerId, Uri target, ModelIdentity model, CancellationToken cancellationToken = default)
    {
        if (target is null || target.Scheme is not ("https" or "http") || string.IsNullOrWhiteSpace(target.Host))
            return Task.FromResult(Failure<DulcheEndpoint>(DulcheErrorCode.InvalidArgument, "Remote target must be an explicit HTTP(S) URI.", "target"));
        if (target.Scheme != Uri.UriSchemeHttps && !target.IsLoopback)
            return Task.FromResult(Failure<DulcheEndpoint>(DulcheErrorCode.PermissionDenied, "Non-loopback remote endpoints require HTTPS.", "target"));
        return StartCoreAsync(providerId, target.GetLeftPart(UriPartial.Path), null, true, model, cancellationToken);
    }

    private async Task<OperationResult<DulcheEndpoint>> StartCoreAsync(string providerId, string target, int? port, bool remote, ModelIdentity? model, CancellationToken cancellationToken)
    {
        if (!_adapters.TryGetValue(providerId, out var adapter)) return Failure<DulcheEndpoint>(DulcheErrorCode.ProviderUnavailable, "Provider is not registered.", providerId, true);
        var endpoint = new DulcheEndpoint(Guid.NewGuid().ToString("N"), adapter.ProviderId, target, port, EndpointState.Starting, model, adapter.Capabilities, remote, DateTimeOffset.UtcNow);
        var slot = new EndpointSlot(endpoint, adapter, _maximumQueueDepth);
        if (!_endpoints.TryAdd(endpoint.EndpointId, slot)) return Failure<DulcheEndpoint>(DulcheErrorCode.Conflict, "Endpoint identity collision.", endpoint.EndpointId);
        var started = await adapter.StartAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (!started.Succeeded)
        {
            slot.Endpoint = endpoint with { State = EndpointState.Failed, LastError = started.Error, UpdatedAt = DateTimeOffset.UtcNow };
            return OperationResult<DulcheEndpoint>.Failure(started.Error!);
        }
        slot.Endpoint = endpoint with { State = model is null ? EndpointState.Ready : EndpointState.Loading, UpdatedAt = DateTimeOffset.UtcNow };
        if (model is not null)
        {
            var loaded = await adapter.LoadModelAsync(slot.Endpoint, model, cancellationToken).ConfigureAwait(false);
            if (!loaded.Succeeded)
            {
                slot.Endpoint = slot.Endpoint with { State = EndpointState.Failed, LastError = loaded.Error, UpdatedAt = DateTimeOffset.UtcNow };
                return OperationResult<DulcheEndpoint>.Failure(loaded.Error!);
            }
            slot.Endpoint = slot.Endpoint with { State = EndpointState.Ready, UpdatedAt = DateTimeOffset.UtcNow };
        }
        return OperationResult<DulcheEndpoint>.Success(slot.Endpoint);
    }

    public OperationResult<Unit> PreparePrompt(string callerId, DulcheRequest request)
    {
        if (string.IsNullOrWhiteSpace(callerId)) return Failure<Unit>(DulcheErrorCode.InvalidArgument, "Caller identity is required for prepared input.", "callerId");
        if (string.IsNullOrWhiteSpace(request.EffectivePrompt)) return Failure<Unit>(DulcheErrorCode.MissingPrompt, "Missing prompt.", "request.input");
        _preparedPrompts[callerId] = Snapshot(request with { CallerId = callerId });
        return OperationResult<Unit>.Success(Unit.Value);
    }

    public OperationResult<RuntimeRequestHandle> Prompt(string callerId, string endpointId)
    {
        if (!_preparedPrompts.TryRemove(callerId, out var prepared)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.MissingPrompt, "Missing prompt.", callerId);
        return Submit(prepared, endpointId);
    }

    public OperationResult<RuntimeRequestHandle> Prompt(DulcheRequest request, string endpointId) => Submit(request, endpointId);
    public OperationResult<RuntimeRequestHandle> PromptStream(DulcheRequest request, string endpointId) => Submit(request with { Stream = true }, endpointId);

    public OperationResult<GenerationSettings> SetFutureSettings(string endpointId, GenerationSettings settings)
    {
        if (!_endpoints.ContainsKey(endpointId)) return Failure<GenerationSettings>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        if (ValidateSettings(settings) is { } error) return OperationResult<GenerationSettings>.Failure(error);
        var snapshot = SnapshotSettings(settings);
        _endpointSettings[endpointId] = snapshot;
        return OperationResult<GenerationSettings>.Success(snapshot);
    }

    public OperationResult<DulcheEndpoint> SetModelForFutureRequests(string endpointId, ModelIdentity model)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint)) return Failure<DulcheEndpoint>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        if (string.IsNullOrWhiteSpace(model.ProviderId) || string.IsNullOrWhiteSpace(model.ModelId)) return Failure<DulcheEndpoint>(DulcheErrorCode.InvalidArgument, "Model identity is required.", "model");
        endpoint.Endpoint = endpoint.Endpoint with { Model = model, UpdatedAt = DateTimeOffset.UtcNow };
        return OperationResult<DulcheEndpoint>.Success(endpoint.Endpoint);
    }

    public OperationResult<DulcheSession> CreateSession(string endpointId)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint)) return Failure<DulcheSession>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        if (endpoint.Endpoint.IsRemote && endpoint.Endpoint.State is EndpointState.Stopped or EndpointState.Failed) return Failure<DulcheSession>(DulcheErrorCode.InvalidState, "Endpoint is not available.", endpointId);
        var now = DateTimeOffset.UtcNow;
        var session = new DulcheSession(Guid.NewGuid().ToString("N"), endpointId, 0, now, now, Array.Empty<DulcheMessage>());
        _sessions[session.SessionId] = new SessionSlot(session);
        return OperationResult<DulcheSession>.Success(session);
    }

    public OperationResult<DulcheSession> GetSession(string sessionId) => _sessions.TryGetValue(sessionId, out var slot)
        ? OperationResult<DulcheSession>.Success(slot.Session)
        : Failure<DulcheSession>(DulcheErrorCode.InvalidArgument, "Session not found.", sessionId);

    public OperationResult<DulcheSession> ResetSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var slot)) return Failure<DulcheSession>(DulcheErrorCode.InvalidArgument, "Session not found.", sessionId);
        lock (slot.Gate) slot.Session = slot.Session with { Revision = slot.Session.Revision + 1, Messages = Array.Empty<DulcheMessage>(), UpdatedAt = DateTimeOffset.UtcNow };
        return OperationResult<DulcheSession>.Success(slot.Session);
    }

    public OperationResult<DulcheSession> CloseSession(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var slot)) return Failure<DulcheSession>(DulcheErrorCode.InvalidArgument, "Session not found.", sessionId);
        return OperationResult<DulcheSession>.Success(slot.Session);
    }

    public OperationResult<DulcheSession> ExportContext(string sessionId) => GetSession(sessionId);
    public OperationResult<DulcheSession> RestoreContext(DulcheSession snapshot, string endpointId, int? contextLimit = null)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(endpointId) || !_endpoints.ContainsKey(endpointId)) return Failure<DulcheSession>(DulcheErrorCode.EndpointNotFound, "Restore endpoint not found.", endpointId ?? "");
        if (contextLimit is < 1) return Failure<DulcheSession>(DulcheErrorCode.InvalidArgument, "Context limit must be positive.", "contextLimit");
        var session = snapshot with { SessionId = Guid.NewGuid().ToString("N"), EndpointId = endpointId, Revision = 0, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _sessions[session.SessionId] = new SessionSlot(session);
        return OperationResult<DulcheSession>.Success(session);
    }

    public OperationResult<RuntimeRequestHandle> Submit(DulcheRequest request, string endpointId)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        if (string.IsNullOrWhiteSpace(request.EffectivePrompt)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.MissingPrompt, "Missing prompt.", "request.input");
        if (request.ContextLimit is <= 0 || request.QueueTimeoutSeconds is <= 0) return Failure<RuntimeRequestHandle>(DulcheErrorCode.InvalidArgument, "Context and queue limits must be positive when specified.", "request.limits");
        if (request.Settings is { } requestedSettings && ValidateSettings(requestedSettings) is { } settingsError) return OperationResult<RuntimeRequestHandle>.Failure(settingsError);
        if (endpoint.Endpoint.State is EndpointState.Stopped or EndpointState.Stopping or EndpointState.Failed) return Failure<RuntimeRequestHandle>(DulcheErrorCode.InvalidState, "Endpoint cannot accept requests in its current state.", endpointId);
        if (request.Model is not null && endpoint.Endpoint.Model is not null && request.Model.StableKey != endpoint.Endpoint.Model.StableKey)
            return Failure<RuntimeRequestHandle>(DulcheErrorCode.Conflict, "Request model differs from endpoint model; route/load the model before submission.", request.Model.StableKey);
        var sessionId = request.SessionId;
        if (sessionId is null)
        {
            var created = CreateSession(endpointId);
            if (!created.Succeeded) return OperationResult<RuntimeRequestHandle>.Failure(created.Error!);
            sessionId = created.Value!.SessionId;
        }
        if (!_sessions.TryGetValue(sessionId, out var session) || session.Session.EndpointId != endpointId) return Failure<RuntimeRequestHandle>(DulcheErrorCode.PermissionDenied, "Session is unavailable in this endpoint scope.", sessionId);
        var requestId = Guid.NewGuid().ToString("N");
        var effective = request with { SessionId = sessionId, Settings = SnapshotSettings(request.Settings ?? _endpointSettings.GetValueOrDefault(endpointId) ?? new GenerationSettings()), Model = request.Model ?? endpoint.Endpoint.Model };
        var slot = new RequestSlot(requestId, Guid.NewGuid().ToString("N"), sessionId, endpointId, Snapshot(effective));
        if (!_requests.TryAdd(requestId, slot)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.Conflict, "Request identity collision.", requestId);
        if (!endpoint.Enqueue(slot))
        {
            _requests.TryRemove(requestId, out _);
            return Failure<RuntimeRequestHandle>(DulcheErrorCode.QueueFull, "Endpoint request queue is full.", endpointId, true);
        }
        slot.RunTask = endpoint.EnsureWorker(ProcessOneAsync);
        return OperationResult<RuntimeRequestHandle>.Success(new(requestId, sessionId, endpointId,
            cancellationToken => AwaitRequestAsync(slot, cancellationToken),
            (after, cancellationToken) => _eventHub.ReadAsync(requestId, after, cancellationToken)));
    }

    private async Task ProcessOneAsync(EndpointSlot endpoint, RequestSlot slot)
    {
        if (slot.State is RequestState.Cancelled or RequestState.Replaced) { slot.Complete(); return; }
        if (slot.PauseRequested || endpoint.ManualPause)
        {
            slot.State = RequestState.Paused;
            endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Paused, UpdatedAt = DateTimeOffset.UtcNow };
            Publish(slot, "Paused");
            try { await slot.WaitToResumeAsync(endpoint.Stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { slot.State = RequestState.Cancelled; slot.FinishReason = FinishReason.Cancelled; slot.Complete(); return; }
        }
        if (endpoint.Stopping.IsCancellationRequested) { slot.State = RequestState.Cancelled; slot.FinishReason = FinishReason.Cancelled; slot.Complete(); return; }
        await ExecuteAsync(endpoint, slot).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(EndpointSlot endpoint, RequestSlot slot)
    {
        slot.State = RequestState.Running;
        endpoint.Current = slot;
        endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Busy, UpdatedAt = DateTimeOffset.UtcNow };
        slot.StartedAt = Stopwatch.GetTimestamp();
        Publish(slot, "Started");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(endpoint.Stopping.Token, slot.Cancellation.Token);
        try
        {
            await foreach (var delta in endpoint.Adapter.GenerateAsync(endpoint.Endpoint, slot.Request, slot.RequestId, linked.Token).WithCancellation(linked.Token).ConfigureAwait(false))
            {
                if (slot.State is RequestState.Replaced or RequestState.Cancelled) break;
                if (delta.Text is { } text) { slot.Text.Append(text); slot.FirstOutputAt ??= Stopwatch.GetTimestamp(); Publish(slot, "TextDelta", text); }
                if (delta.Thinking is { } thinking) { slot.Thinking.Append(thinking); Publish(slot, "ThinkingDelta", thinking); }
                if (delta.InputTokens is { } input) slot.InputTokens = input;
                if (delta.OutputTokens is { } output) slot.OutputTokens = (slot.OutputTokens ?? 0) + output;
                if (delta.Type is { } type) Publish(slot, type, delta.Detail);
                if (delta.FinishReason is { } reason) slot.ProviderFinishReason = reason;
            }
            if (slot.State == RequestState.Running) { slot.State = RequestState.Completed; slot.FinishReason = FinishReason.Completed; }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (slot.State != RequestState.Replaced) { slot.State = RequestState.Cancelled; slot.FinishReason = FinishReason.Cancelled; }
        }
        catch (Exception ex)
        {
            slot.State = RequestState.Failed; slot.FinishReason = FinishReason.Error;
            slot.Errors.Add(new(DulcheErrorCode.ProviderUnavailable, "Provider generation failed.", endpoint.Endpoint.ProviderId, true, Details: new Dictionary<string, string> { ["exceptionType"] = ex.GetType().Name }));
        }
        finally
        {
            slot.CompletedAt = Stopwatch.GetTimestamp();
            Publish(slot, "Completed", slot.FinishReason?.ToString());
            slot.Complete();
            endpoint.Current = null;
            if (endpoint.Endpoint.State is not (EndpointState.Stopped or EndpointState.Stopping))
                endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Ready, UpdatedAt = DateTimeOffset.UtcNow };
        }
    }

    public async Task<OperationResult<Unit>> StopResponseAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (!_requests.TryGetValue(requestId, out var slot)) return Failure<Unit>(DulcheErrorCode.RequestNotFound, "Request not found.", requestId);
        if (slot.IsTerminal) return OperationResult<Unit>.Success(Unit.Value);
        slot.Cancellation.Cancel();
        slot.State = RequestState.Cancelled; slot.FinishReason = FinishReason.Cancelled;
        if (_endpoints.TryGetValue(slot.EndpointId, out var endpoint)) await endpoint.Adapter.CancelAsync(endpoint.Endpoint, requestId, cancellationToken).ConfigureAwait(false);
        Publish(slot, "Cancelled");
        return OperationResult<Unit>.Success(Unit.Value);
    }

    public async Task<OperationResult<Unit>> PauseResponseAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (!_requests.TryGetValue(requestId, out var slot)) return Failure<Unit>(DulcheErrorCode.RequestNotFound, "Request not found.", requestId);
        if (slot.IsTerminal) return Failure<Unit>(DulcheErrorCode.InvalidState, "Completed request cannot be paused.", requestId);
        slot.PauseRequested = true;
        if (_endpoints.TryGetValue(slot.EndpointId, out var endpoint) && endpoint.Current == slot)
        {
            slot.State = RequestState.Pausing;
            endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Pausing, UpdatedAt = DateTimeOffset.UtcNow };
            var paused = await endpoint.Adapter.PauseAsync(endpoint.Endpoint, requestId, cancellationToken).ConfigureAwait(false);
            if (!paused.Succeeded) { slot.PauseRequested = false; return OperationResult<Unit>.Failure(paused.Error!); }
            slot.State = RequestState.Paused; endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Paused, UpdatedAt = DateTimeOffset.UtcNow };
        }
        Publish(slot, "Pausing");
        return OperationResult<Unit>.Success(Unit.Value);
    }

    public async Task<OperationResult<Unit>> ResumeResponseAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (!_requests.TryGetValue(requestId, out var slot)) return Failure<Unit>(DulcheErrorCode.RequestNotFound, "Request not found.", requestId);
        if (slot.State != RequestState.Paused) return Failure<Unit>(DulcheErrorCode.InvalidState, "Request is not paused.", requestId);
        if (!_endpoints.TryGetValue(slot.EndpointId, out var endpoint)) return Failure<Unit>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", slot.EndpointId);
        if (endpoint.Current == slot)
        {
            var resumed = await endpoint.Adapter.ResumeAsync(endpoint.Endpoint, requestId, cancellationToken).ConfigureAwait(false);
            if (!resumed.Succeeded) return OperationResult<Unit>.Failure(resumed.Error!);
        }
        slot.PauseRequested = false; slot.ResumeSignal.TrySetResult(); slot.State = RequestState.Queued;
        endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Busy, UpdatedAt = DateTimeOffset.UtcNow };
        Publish(slot, "Resumed");
        return OperationResult<Unit>.Success(Unit.Value);
    }

    public OperationResult<RuntimeRequestHandle> QueuePrompt(string requestId, string prompt)
    {
        if (!_requests.TryGetValue(requestId, out var prior)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.RequestNotFound, "Request not found.", requestId);
        if (string.IsNullOrWhiteSpace(prompt)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.MissingPrompt, "Missing prompt.", requestId);
        if (!_endpoints.TryGetValue(prior.EndpointId, out var endpoint)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", prior.EndpointId);
        var copy = Submit(prior.Request with { Input = prompt, Messages = null }, prior.EndpointId);
        if (!copy.Succeeded) return copy;
        if (_requests.TryGetValue(copy.Value!.RequestId, out var queued)) queued.DependsOnRequestId = requestId;
        endpoint.MoveAfterDependency(copy.Value!.RequestId, requestId);
        return copy;
    }

    public OperationResult<RuntimeRequestHandle> StackPrompt(string requestId, string prompt)
    {
        var queued = QueuePrompt(requestId, prompt);
        if (!queued.Succeeded) return queued;
        if (_requests.TryGetValue(queued.Value!.RequestId, out var slot) && _endpoints.TryGetValue(slot.EndpointId, out var endpoint)) endpoint.MoveToFront(slot.RequestId);
        return queued;
    }

    public async Task<OperationResult<RuntimeRequestHandle>> ReplacePromptAsync(string requestId, string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.MissingPrompt, "Missing prompt.", requestId);
        if (!_requests.TryGetValue(requestId, out var previous)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.RequestNotFound, "Request not found.", requestId);
        if (!_endpoints.TryGetValue(previous.EndpointId, out var endpoint)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", previous.EndpointId);
        if (previous.State is RequestState.Queued or RequestState.Paused)
        {
            endpoint.RemoveQueued(requestId);
            var replacement = CreateReplacement(previous, prompt, previous.Revision + 1);
            previous.State = RequestState.Replaced; previous.FinishReason = FinishReason.Replaced; previous.Complete();
            _requests[requestId] = replacement;
            endpoint.Enqueue(replacement, priority: previous.State == RequestState.Paused);
            replacement.RunTask = endpoint.EnsureWorker(ProcessOneAsync);
            Publish(replacement, "Replaced", "Queued prompt replaced in place.");
            return OperationResult<RuntimeRequestHandle>.Success(Handle(replacement));
        }
        if (previous.State == RequestState.Running)
        {
            previous.State = RequestState.Replaced; previous.FinishReason = FinishReason.Replaced; previous.Cancellation.Cancel();
            await endpoint.Adapter.CancelAsync(endpoint.Endpoint, requestId, cancellationToken).ConfigureAwait(false);
        }
        var next = CreateReplacement(previous, prompt, previous.Revision + 1);
        _requests[requestId] = next;
        if (!endpoint.Enqueue(next, priority: true)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.QueueFull, "Endpoint request queue is full.", previous.EndpointId, true);
        next.RunTask = endpoint.EnsureWorker(ProcessOneAsync);
        Publish(next, "Replaced", "New attempt created with stable logical RequestID.");
        return OperationResult<RuntimeRequestHandle>.Success(Handle(next));
    }

    private RequestSlot CreateReplacement(RequestSlot previous, string prompt, int revision) => new(previous.RequestId, Guid.NewGuid().ToString("N"), previous.SessionId, previous.EndpointId, previous.Request with { Input = prompt, Messages = null }) { Revision = revision };
    private RuntimeRequestHandle Handle(RequestSlot slot) => new(slot.RequestId, slot.SessionId, slot.EndpointId, token => AwaitRequestAsync(slot, token), (after, token) => _eventHub.ReadAsync(slot.RequestId, after, token));

    public async Task<OperationResult<DulcheEndpoint>> PauseEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint)) return Failure<DulcheEndpoint>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        if (endpoint.Endpoint.State is EndpointState.Stopped or EndpointState.Stopping or EndpointState.Failed) return Failure<DulcheEndpoint>(DulcheErrorCode.InvalidState, "Endpoint cannot be paused.", endpointId);
        endpoint.ManualPause = true;
        if (endpoint.Current is { } request) await PauseResponseAsync(request.RequestId, cancellationToken).ConfigureAwait(false);
        endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Paused, UpdatedAt = DateTimeOffset.UtcNow };
        PublishEndpoint(endpoint, "EndpointPaused");
        return OperationResult<DulcheEndpoint>.Success(endpoint.Endpoint);
    }

    public OperationResult<DulcheEndpoint> ResumeEndpoint(string endpointId)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint)) return Failure<DulcheEndpoint>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        if (!endpoint.ManualPause) return Failure<DulcheEndpoint>(DulcheErrorCode.InvalidState, "Endpoint is not manually paused.", endpointId);
        endpoint.ManualPause = false;
        if (endpoint.Current is { } current && current.State == RequestState.Paused) _ = ResumeResponseAsync(current.RequestId);
        endpoint.Signal();
        endpoint.Endpoint = endpoint.Endpoint with { State = endpoint.Current is null ? EndpointState.Ready : EndpointState.Busy, UpdatedAt = DateTimeOffset.UtcNow };
        PublishEndpoint(endpoint, "EndpointResumed");
        return OperationResult<DulcheEndpoint>.Success(endpoint.Endpoint);
    }

    public async Task<OperationResult<DulcheEndpoint>> StopEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint)) return Failure<DulcheEndpoint>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Stopping, UpdatedAt = DateTimeOffset.UtcNow };
        endpoint.Stopping.Cancel();
        foreach (var pending in endpoint.Drain()) { pending.State = RequestState.Cancelled; pending.FinishReason = FinishReason.Cancelled; pending.Cancellation.Cancel(); pending.Complete(); Publish(pending, "Cancelled", "Endpoint stopped."); }
        if (endpoint.Current is { } active) await StopResponseAsync(active.RequestId, cancellationToken).ConfigureAwait(false);
        var stopped = await endpoint.Adapter.StopAsync(endpoint.Endpoint, cancellationToken).ConfigureAwait(false);
        endpoint.Endpoint = endpoint.Endpoint with { State = stopped.Succeeded ? EndpointState.Stopped : EndpointState.Failed, LastError = stopped.Error, UpdatedAt = DateTimeOffset.UtcNow };
        PublishEndpoint(endpoint, endpoint.Endpoint.State.ToString());
        return stopped.Succeeded ? OperationResult<DulcheEndpoint>.Success(endpoint.Endpoint) : OperationResult<DulcheEndpoint>.Failure(stopped.Error!);
    }

    public OperationResult<DulcheEndpoint> InspectEndpoint(string endpointId) => _endpoints.TryGetValue(endpointId, out var endpoint)
        ? OperationResult<DulcheEndpoint>.Success(endpoint.Endpoint)
        : Failure<DulcheEndpoint>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);

    public OperationResult<GenerationSettings> InspectRequestSettings(string requestId) => _requests.TryGetValue(requestId, out var request)
        ? OperationResult<GenerationSettings>.Success(SnapshotSettings(request.Request.Settings ?? new GenerationSettings()))
        : Failure<GenerationSettings>(DulcheErrorCode.RequestNotFound, "Request not found.", requestId);

    public async Task<OperationResult<RuntimeHealth>> GetHealthAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint)) return Failure<RuntimeHealth>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        try { return OperationResult<RuntimeHealth>.Success(await endpoint.Adapter.HealthAsync(endpoint.Endpoint, cancellationToken).ConfigureAwait(false)); }
        catch (Exception ex) { return OperationResult<RuntimeHealth>.Failure(new(DulcheErrorCode.ProviderUnavailable, "Endpoint health check failed.", endpointId, true, Details: new Dictionary<string, string> { ["exceptionType"] = ex.GetType().Name })); }
    }

    private async Task<DulcheResult> AwaitRequestAsync(RequestSlot slot, CancellationToken cancellationToken)
    {
        await slot.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_sessions.TryGetValue(slot.SessionId, out var session) && slot.State == RequestState.Completed)
        {
            lock (session.Gate)
                session.Session = session.Session with { Revision = session.Session.Revision + 1, Messages = session.Session.Messages.Concat([new DulcheMessage("user", slot.Request.EffectivePrompt), new DulcheMessage("assistant", slot.Text.ToString())]).ToArray(), UpdatedAt = DateTimeOffset.UtcNow };
        }
        var elapsed = slot.StartedAt is null ? Metric<double>.Empty : Metric<double>.Measured(Stopwatch.GetElapsedTime(slot.StartedAt.Value, slot.CompletedAt ?? Stopwatch.GetTimestamp()).TotalSeconds);
        var firstOutput = slot.FirstOutputAt is null || slot.StartedAt is null ? Metric<double>.Empty : Metric<double>.Measured(Stopwatch.GetElapsedTime(slot.StartedAt.Value, slot.FirstOutputAt.Value).TotalSeconds);
        var input = slot.InputTokens is null ? Metric<long>.Na : Metric<long>.Measured(slot.InputTokens.Value);
        var output = slot.OutputTokens is null ? Metric<long>.Na : Metric<long>.Measured(slot.OutputTokens.Value);
        var total = input.Availability == Availability.Value && output.Availability == Availability.Value ? Metric<long>.Measured(input.Value + output.Value) : Metric<long>.Na;
        var tokens = new TokenMetrics(input, output, total, output, Metric<long>.Empty, Metric<long>.Na, Metric<long>.Na);
        return new(slot.RequestId, slot.SessionId, slot.EndpointId, slot.State, slot.FinishReason, slot.Revision, slot.AttemptId, slot.Text.Length == 0 ? null : slot.Text.ToString(),
            slot.Thinking.Length == 0 ? Metric<string>.Empty : Metric<string>.Measured(slot.Thinking.ToString()), null, null, tokens, elapsed, firstOutput,
            slot.OutputTokens is > 0 && slot.StartedAt.HasValue && slot.CompletedAt.HasValue ? Metric<double>.Measured(slot.OutputTokens.Value / Math.Max(.0001, Stopwatch.GetElapsedTime(slot.StartedAt.Value, slot.CompletedAt.Value).TotalSeconds)) : Metric<double>.Na,
            Array.Empty<ModelInvocation>(), slot.Errors.ToArray(), slot.Events.ToArray(), slot.Errors.FirstOrDefault());
    }

    private void Publish(RequestSlot slot, string type, string? detail = null)
    {
        var item = new RuntimeEvent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, slot.RequestId, slot.Revision, slot.AttemptId, Interlocked.Increment(ref slot.Sequence), type, detail);
        slot.Events.Enqueue(item); _eventHub.Publish(slot.RequestId, item);
    }
    private void PublishEndpoint(EndpointSlot endpoint, string type) { if (endpoint.Current is { } request) Publish(request, type, endpoint.Endpoint.State.ToString()); }
    private static DulcheRequest Snapshot(DulcheRequest request) => request with
    {
        Messages = request.Messages?.Select(message => message with { Inputs = message.Inputs?.ToArray() }).ToArray(),
        PermittedTools = request.PermittedTools?.ToHashSet(StringComparer.Ordinal),
        Settings = request.Settings is null ? null : SnapshotSettings(request.Settings)
    };
    private static GenerationSettings SnapshotSettings(GenerationSettings settings) => settings with
    {
        StopSequences = settings.StopSequences?.ToArray(),
        Penalties = settings.Penalties is null ? null : new Dictionary<string, double>(settings.Penalties, StringComparer.Ordinal)
    };
    private static DulcheError? ValidateSettings(GenerationSettings settings)
    {
        if (settings.Temperature is { } temperature && (!double.IsFinite(temperature) || temperature is < 0 or > 2)) return new(DulcheErrorCode.InvalidArgument, "Temperature must be finite and between 0 and 2.", "settings.temperature", false);
        if (settings.MaximumOutputTokens is <= 0 || settings.TopK is <= 0) return new(DulcheErrorCode.InvalidArgument, "Output token and top-k limits must be positive.", "settings", false);
        if (settings.TopP is { } topP && (!double.IsFinite(topP) || topP is <= 0 or > 1)) return new(DulcheErrorCode.InvalidArgument, "Top-p must be greater than 0 and at most 1.", "settings.topP", false);
        if (settings.Penalties?.Any(pair => !double.IsFinite(pair.Value)) == true) return new(DulcheErrorCode.InvalidArgument, "Penalty values must be finite.", "settings.penalties", false);
        if (settings.OutputSchema is { } schema) { try { using var _ = System.Text.Json.JsonDocument.Parse(schema); } catch (System.Text.Json.JsonException) { return new(DulcheErrorCode.InvalidArgument, "Output schema must be valid JSON.", "settings.outputSchema", false); } }
        return null;
    }
    private static OperationResult<T> Failure<T>(DulcheErrorCode code, string message, string target, bool retryable = false) => OperationResult<T>.Failure(new(code, message, target, retryable));

    private sealed class SessionSlot(DulcheSession session) { public readonly object Gate = new(); public DulcheSession Session = session; }
    private sealed class RequestSlot(string requestId, string attemptId, string sessionId, string endpointId, DulcheRequest request)
    {
        public string RequestId { get; } = requestId; public string AttemptId { get; } = attemptId; public string SessionId { get; } = sessionId; public string EndpointId { get; } = endpointId; public DulcheRequest Request { get; } = request;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public Task? RunTask; public CancellationTokenSource Cancellation { get; } = new(); public TaskCompletionSource ResumeSignal { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<RuntimeEvent> Events { get; } = new(); public List<DulcheError> Errors { get; } = []; public StringBuilder Text { get; } = new(); public StringBuilder Thinking { get; } = new(); public RequestState State = RequestState.Queued; public FinishReason? FinishReason; public int Revision = 1; public long Sequence; public long? OutputTokens; public long? InputTokens; public string? ProviderFinishReason; public long? StartedAt; public long? FirstOutputAt; public long? CompletedAt; public bool PauseRequested; public string? DependsOnRequestId;
        public bool IsTerminal => State is RequestState.Completed or RequestState.Cancelled or RequestState.Replaced or RequestState.Blocked or RequestState.Failed;
        public void Complete() => Completion.TrySetResult();
        public async Task WaitToResumeAsync(CancellationToken cancellationToken) { await ResumeSignal.Task.WaitAsync(cancellationToken).ConfigureAwait(false); ResumeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    }
    private sealed class EndpointSlot(DulcheEndpoint endpoint, IDulcheAdapter adapter, int maxQueue)
    {
        private readonly object _gate = new(); private readonly LinkedList<RequestSlot> _queue = []; private readonly SemaphoreSlim _signal = new(0); private Task? _worker;
        public DulcheEndpoint Endpoint = endpoint; public IDulcheAdapter Adapter { get; } = adapter; public int MaxQueue { get; } = maxQueue; public CancellationTokenSource Stopping { get; } = new(); public RequestSlot? Current; public bool ManualPause;
        public bool Enqueue(RequestSlot slot, bool priority = false) { lock (_gate) { if (_queue.Count >= MaxQueue) return false; if (priority) _queue.AddFirst(slot); else _queue.AddLast(slot); _signal.Release(); return true; } }
        public bool TryDequeue(out RequestSlot slot) { lock (_gate) { if (_queue.First is null) { slot = null!; return false; } slot = _queue.First.Value; _queue.RemoveFirst(); return true; } }
        public Task EnsureWorker(Func<EndpointSlot, RequestSlot, Task> run) { lock (_gate) { if (_worker is { IsCompleted: false }) { Signal(); return _worker; } _worker = Task.Run(async () => { try { while (!Stopping.IsCancellationRequested) { await _signal.WaitAsync(Stopping.Token).ConfigureAwait(false); if (TryDequeue(out var next)) await run(this, next).ConfigureAwait(false); } } catch (OperationCanceledException) when (Stopping.IsCancellationRequested) { } }, Stopping.Token); return _worker; } }
        public void Signal() { try { _signal.Release(); } catch (SemaphoreFullException) { } }
        public void RemoveQueued(string requestId) { lock (_gate) { var node = _queue.First; while (node is not null) { var next = node.Next; if (node.Value.RequestId == requestId) _queue.Remove(node); node = next; } } }
        public void MoveAfterDependency(string queuedId, string dependencyId) { lock (_gate) { var queued = Find(queuedId); var dependency = Find(dependencyId); if (queued is null || dependency is null || queued == dependency) return; _queue.Remove(queued); _queue.AddAfter(dependency, queued); } }
        public void MoveToFront(string requestId) { lock (_gate) { var node = Find(requestId); if (node is null) return; _queue.Remove(node); _queue.AddFirst(node); } }
        public IEnumerable<RequestSlot> Drain() { lock (_gate) { var all = _queue.ToArray(); _queue.Clear(); return all; } }
        private LinkedListNode<RequestSlot>? Find(string requestId) { var node = _queue.First; while (node is not null) { if (node.Value.RequestId == requestId) return node; node = node.Next; } return null; }
    }
}
