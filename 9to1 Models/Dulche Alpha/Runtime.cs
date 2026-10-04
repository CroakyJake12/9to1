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
    private readonly ConcurrentDictionary<string, IDulcheAcquisitionAdapter> _acquisition = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentBag<(string ProviderId, ModelIdentity Model)>> _temporaryOwnership = new(StringComparer.Ordinal);
    private readonly IDulcheToolCoordinator? _toolCoordinator;
    private readonly ConcurrentDictionary<string, EndpointSlot> _endpoints = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SessionSlot> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RequestSlot> _requests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DulcheRequest> _preparedPrompts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GenerationSettings> _endpointSettings = new(StringComparer.Ordinal);
    private readonly RuntimeEventHub _eventHub = new();
    private readonly int _maximumQueueDepth;

    public DulcheRuntime(IEnumerable<IDulcheAdapter> adapters, int maximumQueueDepth = 64, IEnumerable<IDulcheAcquisitionAdapter>? acquisitionAdapters = null, IDulcheToolCoordinator? toolCoordinator = null)
    {
        if (maximumQueueDepth < 1) throw new ArgumentOutOfRangeException(nameof(maximumQueueDepth));
        _maximumQueueDepth = maximumQueueDepth;
        _toolCoordinator = toolCoordinator;
        foreach (var adapter in adapters ?? throw new ArgumentNullException(nameof(adapters)))
            if (!_adapters.TryAdd(adapter.ProviderId, adapter)) throw new ArgumentException($"Duplicate provider '{adapter.ProviderId}'.", nameof(adapters));
        foreach (var adapter in acquisitionAdapters ?? [])
            if (!_acquisition.TryAdd(adapter.ProviderId, adapter)) throw new ArgumentException($"Duplicate acquisition provider '{adapter.ProviderId}'.", nameof(acquisitionAdapters));
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

    public Task<OperationResult<DulcheEndpoint>> StartRemoteAsync(RemoteEndpointTarget target, CancellationToken cancellationToken = default)
    {
        if (target is null || target.BaseUri is null || target.Model is null || !StringComparer.OrdinalIgnoreCase.Equals(target.ProviderId, target.Model.ProviderId))
            return Task.FromResult(Failure<DulcheEndpoint>(DulcheErrorCode.InvalidArgument, "Remote target requires a provider-scoped model identity.", "target"));
        if (target.BaseUri.Scheme != Uri.UriSchemeHttps && !target.BaseUri.IsLoopback)
            return Task.FromResult(Failure<DulcheEndpoint>(DulcheErrorCode.PermissionDenied, "Non-loopback remote endpoints require HTTPS.", "target"));
        return StartCoreAsync(target.ProviderId, target.BaseUri.GetLeftPart(UriPartial.Path), null, true, target.Model, cancellationToken, target);
    }

    private async Task<OperationResult<DulcheEndpoint>> StartCoreAsync(string providerId, string target, int? port, bool remote, ModelIdentity? model, CancellationToken cancellationToken, RemoteEndpointTarget? remoteTarget = null)
    {
        if (!_adapters.TryGetValue(providerId, out var adapter)) return Failure<DulcheEndpoint>(DulcheErrorCode.ProviderUnavailable, "Provider is not registered.", providerId, true);
        var endpoint = new DulcheEndpoint(Guid.NewGuid().ToString("N"), adapter.ProviderId, target, port, EndpointState.Starting, model, adapter.Capabilities, remote, DateTimeOffset.UtcNow, RemoteTarget: remoteTarget);
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

    public Task<IReadOnlyList<ModelArtifact>> ListModelsAsync(string providerId, CancellationToken cancellationToken = default) =>
        _acquisition.TryGetValue(providerId, out var adapter) ? adapter.ListModelsAsync(cancellationToken) : Task.FromException<IReadOnlyList<ModelArtifact>>(new InvalidOperationException($"Provider '{providerId}' does not expose model catalogue/acquisition."));

    public Task<OperationResult<ModelArtifact>> FindModelAsync(string providerId, string query, CancellationToken cancellationToken = default) =>
        _acquisition.TryGetValue(providerId, out var adapter) ? adapter.FindModelAsync(query, cancellationToken) : Task.FromResult(Failure<ModelArtifact>(DulcheErrorCode.ProviderUnavailable, "Provider does not expose model acquisition.", providerId));

    public async Task<OperationResult<AcquisitionProgress>> PullModelAsync(string providerId, ModelArtifact model, string ownerScopeId, IProgress<AcquisitionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!_acquisition.TryGetValue(providerId, out var adapter)) return Failure<AcquisitionProgress>(DulcheErrorCode.ProviderUnavailable, "Provider does not expose model acquisition.", providerId);
        if (string.IsNullOrWhiteSpace(ownerScopeId) || !StringComparer.OrdinalIgnoreCase.Equals(model.Identity.ProviderId, providerId)) return Failure<AcquisitionProgress>(DulcheErrorCode.InvalidArgument, "Model provider and temporary ownership scope must match.", ownerScopeId);
        try
        {
            var result = await adapter.PullAsync(model, ownerScopeId, progress, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) _temporaryOwnership.GetOrAdd(ownerScopeId, _ => new()).Add((providerId, model.Identity));
            return result;
        }
        catch (OperationCanceledException) { return Failure<AcquisitionProgress>(DulcheErrorCode.InvalidState, "Model pull was cancelled and can be resumed through its provider operation.", model.Identity.StableKey, true); }
        catch (Exception ex) { return OperationResult<AcquisitionProgress>.Failure(new(DulcheErrorCode.ProviderUnavailable, "Model acquisition failed.", model.Identity.StableKey, true, Details: new Dictionary<string, string> { ["exceptionType"] = ex.GetType().Name })); }
    }

    public Task<OperationResult<ModelArtifact>> InstallModelAsync(string providerId, ModelArtifact model, string location, CancellationToken cancellationToken = default) =>
        _acquisition.TryGetValue(providerId, out var adapter) ? adapter.InstallAsync(model, location, cancellationToken) : Task.FromResult(Failure<ModelArtifact>(DulcheErrorCode.ProviderUnavailable, "Provider does not expose installation.", providerId));

    public Task<OperationResult<ModelReplaceResult>> ReplaceModelAsync(string providerId, ModelArtifact current, ModelArtifact replacement, string policy, CancellationToken cancellationToken = default) =>
        _acquisition.TryGetValue(providerId, out var adapter) ? adapter.ReplaceAsync(current, replacement, policy, cancellationToken) : Task.FromResult(Failure<ModelReplaceResult>(DulcheErrorCode.ProviderUnavailable, "Provider does not expose model replacement.", providerId));

    public Task<OperationResult<ModelArtifact>> RollbackModelReplacementAsync(string providerId, string rollbackId, CancellationToken cancellationToken = default) =>
        _acquisition.TryGetValue(providerId, out var adapter) ? adapter.RollbackAsync(rollbackId, cancellationToken) : Task.FromResult(Failure<ModelArtifact>(DulcheErrorCode.ProviderUnavailable, "Provider does not expose rollback.", providerId));

    private async Task ReleaseTemporaryOwnershipAsync(string ownerScopeId, CancellationToken cancellationToken)
    {
        if (!_temporaryOwnership.TryRemove(ownerScopeId, out var models)) return;
        foreach (var (providerId, model) in models)
            if (_acquisition.TryGetValue(providerId, out var adapter)) await adapter.ReleaseTemporaryOwnershipAsync(model, ownerScopeId, cancellationToken).ConfigureAwait(false);
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

    public OperationResult<RuntimeRequestHandle> Submit(DulcheRequest request, string endpointId) => SubmitCore(request, endpointId);

    private OperationResult<RuntimeRequestHandle> SubmitCore(DulcheRequest request, string endpointId, string? dependencyId = null, bool priority = false)
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
        slot.DependsOnRequestId = dependencyId;
        if (!endpoint.Enqueue(slot, priority, dependencyId))
        {
            _requests.TryRemove(requestId, out _);
            return Failure<RuntimeRequestHandle>(DulcheErrorCode.QueueFull, "Endpoint request queue is full.", endpointId, true);
        }
        slot.RunTask = endpoint.EnsureWorker(ProcessOneAsync);
        return OperationResult<RuntimeRequestHandle>.Success(new(requestId, sessionId, endpointId,
            cancellationToken => AwaitRequestAsync(slot, cancellationToken),
            (after, cancellationToken) => _eventHub.ReadAsync(requestId, after, cancellationToken)));
    }

    public OperationResult<DulcheQueueSnapshot> GetQueueSnapshot(string endpointId)
    {
        if (!_endpoints.TryGetValue(endpointId, out var endpoint)) return Failure<DulcheQueueSnapshot>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", endpointId);
        return OperationResult<DulcheQueueSnapshot>.Success(endpoint.SnapshotQueue());
    }

    private async Task ProcessOneAsync(EndpointSlot endpoint, RequestSlot slot)
    {
        if (slot.State is RequestState.Cancelled or RequestState.Replaced) { slot.Complete(); return; }
        if (slot.DependsOnRequestId is { } dependency && (!_requests.TryGetValue(dependency, out var parent) || parent.State != RequestState.Completed))
        {
            slot.State = RequestState.Blocked; slot.FinishReason = FinishReason.Blocked;
            slot.Errors.Add(new(DulcheErrorCode.Conflict, "Queued request dependency did not complete successfully; input is retained for explicit retry.", dependency, true));
            Publish(slot, "Blocked", dependency); slot.Complete(); return;
        }
        if (slot.PauseRequested || endpoint.ManualPause)
        {
            slot.State = RequestState.Paused;
            slot.PausedAt ??= Stopwatch.GetTimestamp();
            endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Paused, UpdatedAt = DateTimeOffset.UtcNow };
            Publish(slot, "Paused");
            try { await slot.WaitToResumeAsync(endpoint.Stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { slot.State = RequestState.Cancelled; slot.FinishReason = FinishReason.Cancelled; slot.Complete(); return; }
        }
        if (endpoint.Stopping.IsCancellationRequested) { slot.State = RequestState.Cancelled; slot.FinishReason = FinishReason.Cancelled; slot.Complete(); return; }
        if (_sessions.TryGetValue(slot.SessionId, out var sessionContext))
        {
            lock (sessionContext.Gate)
                slot.Request = slot.Request with { Messages = sessionContext.Session.Messages.Concat(slot.Request.Messages ?? []).ToArray() };
        }
        if (slot.Request.QueueTimeoutSeconds is { } timeout && Stopwatch.GetElapsedTime(slot.AcceptedAt) > TimeSpan.FromSeconds(timeout))
        {
            slot.State = RequestState.Blocked; slot.FinishReason = FinishReason.Error;
            slot.Errors.Add(new(DulcheErrorCode.QueueFull, "Request exceeded its configured queue timeout.", slot.EndpointId, true));
            Publish(slot, "QueueTimeout"); slot.Complete(); return;
        }
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
        var budget = slot.Request.Budget ?? new ExecutionBudget();
        if (budget.MaximumSteps < 1 || budget.MaximumDurationSeconds < 1 || budget.MaximumOutputTokens is <= 0 || budget.MaximumCost is < 0)
        {
            slot.State = RequestState.Failed; slot.FinishReason = FinishReason.Error;
            slot.Errors.Add(new(DulcheErrorCode.InvalidArgument, "Execution budgets must be positive and non-negative where applicable.", slot.RequestId, false));
        }
        else linked.CancelAfter(TimeSpan.FromSeconds(budget.MaximumDurationSeconds));
        try
        {
            if (slot.State == RequestState.Failed) return;
            var adapterEndpoint = endpoint.Endpoint with { Model = slot.Request.Model };
            await ConsumeAsync(endpoint.Adapter.GenerateAsync(adapterEndpoint, slot.Request, slot.RequestId, linked.Token), 0).ConfigureAwait(false);
            if (slot.State == RequestState.Running) { slot.State = RequestState.Completed; slot.FinishReason = FinishReason.Completed; }

            async Task ConsumeAsync(IAsyncEnumerable<AdapterDelta> deltas, int step)
            {
                await foreach (var delta in deltas.WithCancellation(linked.Token).ConfigureAwait(false))
                {
                    if (slot.State is RequestState.Replaced or RequestState.Cancelled or RequestState.Blocked || linked.IsCancellationRequested) break;
                    if (delta.Text is { } text)
                    {
                        slot.Text.Append(text); slot.FirstOutputAt ??= Stopwatch.GetTimestamp(); Publish(slot, "TextDelta", text);
                    }
                    if (delta.Thinking is { } thinking) { slot.Thinking.Append(thinking); Publish(slot, "ThinkingDelta", thinking); }
                    if (delta.InputTokens is { } input) slot.InputTokens = (slot.InputTokens ?? 0) + input;
                    if (delta.OutputTokens is { } output)
                    {
                        slot.OutputTokens = (slot.OutputTokens ?? 0) + output;
                        var maximum = slot.Request.Settings?.MaximumOutputTokens ?? budget.MaximumOutputTokens;
                        if (maximum is { } limit && slot.OutputTokens > limit) { slot.FinishReason = FinishReason.OutputLimit; slot.State = RequestState.Cancelled; slot.Cancellation.Cancel(); await endpoint.Adapter.CancelAsync(adapterEndpoint, slot.RequestId, CancellationToken.None).ConfigureAwait(false); break; }
                    }
                    if (delta.Type is { } type) Publish(slot, type, delta.Detail);
                    if (delta.GeneratedUI is { } ui) ValidateGeneratedUi(slot, ui);
                    if (delta.ToolProposal is { } proposal)
                    {
                        if (++slot.ToolSteps > budget.MaximumSteps) { Block(slot, DulcheErrorCode.ToolFailed, "Agent/tool step budget was exhausted.", proposal.Name); break; }
                        if (_toolCoordinator is null || slot.Request.ToolPolicy is not { } toolPolicy || toolPolicy.Mode == ToolCallMode.None
                            || string.IsNullOrWhiteSpace(toolPolicy.CallerId) || string.IsNullOrWhiteSpace(toolPolicy.ScopeId)
                            || !toolPolicy.AllowedTools.Contains(proposal.Name)
                            || slot.Request.PermittedTools is { } permitted && !permitted.Contains(proposal.Name))
                        { Block(slot, DulcheErrorCode.PermissionDenied, "Tool call is outside the request's authorised tool scope.", proposal.Name); break; }

                        Publish(slot, "ToolProposed", proposal.Name);
                        var context = new ToolExecutionContext(slot.RequestId, slot.Revision, slot.AttemptId, slot.SessionId, slot.EndpointId, slot.Request.Model,
                            toolPolicy.CallerId, _sessions.TryGetValue(slot.SessionId, out var sessionSlot) ? sessionSlot.Session.Messages.ToArray() : [], toolPolicy, linked.Token);
                        OperationResult<ToolInvocationResult> toolResult;
                        try { toolResult = await _toolCoordinator.ExecuteAsync(proposal, context, linked.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                        catch (Exception error) { toolResult = OperationResult<ToolInvocationResult>.Failure(new(DulcheErrorCode.ToolFailed, "Tool coordinator failed.", proposal.Name, true, Details: new Dictionary<string, string> { ["exceptionType"] = error.GetType().Name })); }
                        if (!toolResult.Succeeded || toolResult.Value!.Status != ToolInvocationStatus.Executed)
                        {
                            var failure = toolResult.Error ?? toolResult.Value?.Error ?? new(DulcheErrorCode.PermissionDenied, "Tool was not approved or did not complete.", proposal.Name, false);
                            slot.Errors.Add(failure); slot.State = RequestState.Blocked; slot.FinishReason = failure.Code == DulcheErrorCode.PermissionDenied ? FinishReason.Blocked : FinishReason.Error;
                            Publish(slot, toolResult.Value?.Status.ToString() ?? "ToolFailed", proposal.Name);
                            break;
                        }
                        slot.Tools.Add(proposal.Name); Publish(slot, "ToolCompleted", proposal.Name);
                        await ConsumeAsync(endpoint.Adapter.ContinueWithToolResultAsync(adapterEndpoint, slot.Request, slot.RequestId, toolResult.Value, linked.Token), step + 1).ConfigureAwait(false);
                    }
                    if (delta.FinishReason is { } reason) slot.ProviderFinishReason = reason;
                }
            }
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
            if (slot.State == RequestState.Completed && _sessions.TryGetValue(slot.SessionId, out var sessionSlot))
            {
                lock (sessionSlot.Gate)
                    sessionSlot.Session = sessionSlot.Session with { Revision = sessionSlot.Session.Revision + 1, Messages = sessionSlot.Session.Messages.Concat([new DulcheMessage("user", slot.Request.EffectivePrompt), new DulcheMessage("assistant", slot.Text.ToString())]).ToArray(), UpdatedAt = DateTimeOffset.UtcNow };
            }
            Publish(slot, "Completed", slot.FinishReason?.ToString());
            slot.Complete();
            endpoint.Current = null;
            if (endpoint.Endpoint.State is not (EndpointState.Stopped or EndpointState.Stopping))
                endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Ready, UpdatedAt = DateTimeOffset.UtcNow };
            await ReleaseTemporaryOwnershipAsync(slot.RequestId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void Block(RequestSlot slot, DulcheErrorCode code, string message, string target)
    {
        slot.State = RequestState.Blocked; slot.FinishReason = code == DulcheErrorCode.PermissionDenied ? FinishReason.Blocked : FinishReason.Error;
        slot.Errors.Add(new(code, message, target, false));
    }

    private void ValidateGeneratedUi(RequestSlot slot, GeneratedUiDocument document)
    {
        var warnings = new List<string>();
        var capability = slot.Request.GenerativeContainer;
        var size = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document).Length;
        var isRenderable = capability is not null;
        if (document.SchemaVersion < 1 || string.IsNullOrWhiteSpace(document.Schema) || document.Components is null) warnings.Add("Generated UI document has an invalid schema header.");
        if (document.Components is null)
        {
            warnings.Add("Generated UI has no component collection and cannot be rendered.");
            slot.GeneratedUI = new(document, false, warnings);
            Publish(slot, "GeneratedUI", System.Text.Json.JsonSerializer.Serialize(new { renderable = false, warnings }), System.Text.Json.JsonSerializer.SerializeToElement(document));
            slot.Errors.Add(new(DulcheErrorCode.UnsupportedCapability, "Generated UI was retained as data but is not safe to render.", slot.RequestId, false, Details: new Dictionary<string, string> { ["warnings"] = string.Join(" | ", warnings) }));
            return;
        }
        if (capability is null) warnings.Add("No GenerativeContainer is registered; structured UI is returned as data for fallback handling.");
        else
        {
            if (size > capability.MaximumPayloadBytes) warnings.Add("Generated UI payload exceeds the host-advertised size limit.");
            if (document.Components!.Count > capability.MaximumComponents) warnings.Add("Generated UI component count exceeds the host-advertised limit.");
            if (!capability.Schemas.Contains(document.Schema)) warnings.Add("Generated UI schema was not advertised by the host.");
            if (document.Components.Select(component => component.ComponentId).Distinct(StringComparer.Ordinal).Count() != document.Components.Count) warnings.Add("Generated UI contains duplicate component identities.");
            foreach (var component in document.Components)
            {
                if (string.IsNullOrWhiteSpace(component.ComponentId) || string.IsNullOrWhiteSpace(component.ComponentType) || component.ComponentType.Contains("script", StringComparison.OrdinalIgnoreCase)) warnings.Add("Generated UI contains an invalid or executable component type.");
                if (!capability.Components.Contains(component.ComponentType)) warnings.Add($"Generated UI component '{component.ComponentType}' is not supported by the host.");
                if (component.Actions?.Any(action => !capability.ActionIds.Contains(action.ActionId)) == true) warnings.Add("Generated UI references an action not advertised by the host.");
            }
            isRenderable &= warnings.Count == 0;
        }
        if (warnings.Count > 0) slot.Errors.Add(new(DulcheErrorCode.UnsupportedCapability, "Generated UI was retained as data but is not safe to render.", slot.RequestId, false, Details: new Dictionary<string, string> { ["warnings"] = string.Join(" | ", warnings) }));
        slot.GeneratedUI = new(document, isRenderable, warnings);
        Publish(slot, "GeneratedUI", System.Text.Json.JsonSerializer.Serialize(new { renderable = isRenderable, warnings }), System.Text.Json.JsonSerializer.SerializeToElement(document));
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
            slot.State = RequestState.Paused; slot.PausedAt ??= Stopwatch.GetTimestamp(); endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Paused, UpdatedAt = DateTimeOffset.UtcNow };
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
        slot.PauseRequested = false;
        if (slot.PausedAt is { } pausedAt) { slot.PausedTicks += Stopwatch.GetTimestamp() - pausedAt; slot.PausedAt = null; }
        slot.ResumeSignal.TrySetResult(); slot.State = RequestState.Queued;
        endpoint.Endpoint = endpoint.Endpoint with { State = EndpointState.Busy, UpdatedAt = DateTimeOffset.UtcNow };
        Publish(slot, "Resumed");
        return OperationResult<Unit>.Success(Unit.Value);
    }

    public OperationResult<RuntimeRequestHandle> QueuePrompt(string requestId, string prompt)
    {
        if (!_requests.TryGetValue(requestId, out var prior)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.RequestNotFound, "Request not found.", requestId);
        if (string.IsNullOrWhiteSpace(prompt)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.MissingPrompt, "Missing prompt.", requestId);
        if (!_endpoints.TryGetValue(prior.EndpointId, out var endpoint)) return Failure<RuntimeRequestHandle>(DulcheErrorCode.EndpointNotFound, "Endpoint not found.", prior.EndpointId);
        return SubmitCore(prior.Request with { Input = prompt, Messages = null, SessionId = prior.SessionId }, prior.EndpointId, dependencyId: requestId, priority: false);
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
        var now = slot.CompletedAt ?? Stopwatch.GetTimestamp();
        var elapsed = Metric<double>.Measured(Stopwatch.GetElapsedTime(slot.AcceptedAt, now).TotalSeconds);
        var queued = Metric<double>.Measured(Stopwatch.GetElapsedTime(slot.AcceptedAt, slot.StartedAt ?? now).TotalSeconds);
        var pausedTicks = slot.PausedTicks + (slot.PausedAt is { } pauseStart ? now - pauseStart : 0);
        var pausedSeconds = Metric<double>.Measured(Stopwatch.GetElapsedTime(0, pausedTicks).TotalSeconds);
        var executingSeconds = slot.StartedAt is null ? Metric<double>.Empty : Metric<double>.Measured(Math.Max(0, Stopwatch.GetElapsedTime(slot.StartedAt.Value, now).TotalSeconds - pausedSeconds.Value));
        var firstOutput = slot.FirstOutputAt is null || slot.StartedAt is null ? Metric<double>.Empty : Metric<double>.Measured(Stopwatch.GetElapsedTime(slot.StartedAt.Value, slot.FirstOutputAt.Value).TotalSeconds);
        var input = slot.InputTokens is null ? Metric<long>.Na : Metric<long>.Measured(slot.InputTokens.Value);
        var output = slot.OutputTokens is null ? Metric<long>.Na : Metric<long>.Measured(slot.OutputTokens.Value);
        var total = input.Availability == Availability.Value && output.Availability == Availability.Value ? Metric<long>.Measured(input.Value + output.Value) : Metric<long>.Na;
        var tokens = new TokenMetrics(input, output, total, output, Metric<long>.Empty, Metric<long>.Na, Metric<long>.Na);
        return new(slot.RequestId, slot.SessionId, slot.EndpointId, slot.State, slot.FinishReason, slot.Revision, slot.AttemptId, slot.Text.Length == 0 ? null : slot.Text.ToString(),
            slot.Thinking.Length == 0 ? Metric<string>.Empty : Metric<string>.Measured(slot.Thinking.ToString()), slot.Tools.Count == 0 ? null : slot.Tools.ToArray(), null, tokens, elapsed, queued, pausedSeconds, executingSeconds, firstOutput,
            slot.OutputTokens is > 0 && executingSeconds.Availability == Availability.Value ? Metric<double>.Measured(slot.OutputTokens.Value / Math.Max(.0001, executingSeconds.Value)) : Metric<double>.Na,
            Array.Empty<ModelInvocation>(), slot.Errors.ToArray(), slot.Events.ToArray(), slot.Errors.FirstOrDefault(), slot.GeneratedUI);
    }

    private void Publish(RequestSlot slot, string type, string? detail = null, System.Text.Json.JsonElement? payload = null)
    {
        var item = new RuntimeEvent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, slot.RequestId, slot.Revision, slot.AttemptId, Interlocked.Increment(ref slot.Sequence), type, detail, Payload: payload);
        slot.Events.Enqueue(item); _eventHub.Publish(slot.RequestId, item);
    }
    private void PublishEndpoint(EndpointSlot endpoint, string type) { if (endpoint.Current is { } request) Publish(request, type, endpoint.Endpoint.State.ToString()); }
    private static DulcheRequest Snapshot(DulcheRequest request) => request with
    {
        Messages = request.Messages?.Select(message => message with { Inputs = message.Inputs?.ToArray() }).ToArray(),
        PermittedTools = request.PermittedTools?.ToHashSet(StringComparer.Ordinal),
        Settings = request.Settings is null ? null : SnapshotSettings(request.Settings),
        ToolPolicy = request.ToolPolicy is null ? null : request.ToolPolicy with { AllowedTools = request.ToolPolicy.AllowedTools.ToHashSet(StringComparer.Ordinal) },
        GenerativeContainer = request.GenerativeContainer is null ? null : request.GenerativeContainer with
        {
            Schemas = request.GenerativeContainer.Schemas.ToHashSet(StringComparer.Ordinal),
            Components = request.GenerativeContainer.Components.ToHashSet(StringComparer.Ordinal),
            ActionIds = request.GenerativeContainer.ActionIds.ToHashSet(StringComparer.Ordinal)
        }
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
        public string RequestId { get; } = requestId; public string AttemptId { get; } = attemptId; public string SessionId { get; } = sessionId; public string EndpointId { get; } = endpointId; public DulcheRequest Request { get; set; } = request;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public Task? RunTask; public CancellationTokenSource Cancellation { get; } = new(); public TaskCompletionSource ResumeSignal { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<RuntimeEvent> Events { get; } = new(); public List<DulcheError> Errors { get; } = []; public List<string> Tools { get; } = []; public StringBuilder Text { get; } = new(); public StringBuilder Thinking { get; } = new(); public RequestState State = RequestState.Queued; public FinishReason? FinishReason; public int Revision = 1; public long Sequence; public long? OutputTokens; public long? InputTokens; public string? ProviderFinishReason; public long AcceptedAt = Stopwatch.GetTimestamp(); public long? StartedAt; public long? FirstOutputAt; public long? CompletedAt; public long? PausedAt; public long PausedTicks; public int ToolSteps; public GeneratedUiPayload? GeneratedUI; public bool PauseRequested; public string? DependsOnRequestId;
        public bool IsTerminal => State is RequestState.Completed or RequestState.Cancelled or RequestState.Replaced or RequestState.Blocked or RequestState.Failed;
        public void Complete() => Completion.TrySetResult();
        public async Task WaitToResumeAsync(CancellationToken cancellationToken) { await ResumeSignal.Task.WaitAsync(cancellationToken).ConfigureAwait(false); ResumeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    }
    private sealed class EndpointSlot(DulcheEndpoint endpoint, IDulcheAdapter adapter, int maxQueue)
    {
        private readonly object _gate = new(); private readonly LinkedList<RequestSlot> _queue = []; private readonly SemaphoreSlim _signal = new(0); private Task? _worker;
        public DulcheEndpoint Endpoint = endpoint; public IDulcheAdapter Adapter { get; } = adapter; public int MaxQueue { get; } = maxQueue; public CancellationTokenSource Stopping { get; } = new(); public RequestSlot? Current; public bool ManualPause;
        public bool Enqueue(RequestSlot slot, bool priority = false, string? afterRequestId = null) { lock (_gate) { if (_queue.Count >= MaxQueue) return false; var dependency = afterRequestId is null ? null : Find(afterRequestId); if (dependency is not null) _queue.AddAfter(dependency, slot); else if (priority || afterRequestId is not null) _queue.AddFirst(slot); else _queue.AddLast(slot); _signal.Release(); return true; } }
        public DulcheQueueSnapshot SnapshotQueue() { lock (_gate) return new(Endpoint.EndpointId, Current?.RequestId, _queue.Select((slot, index) => new DulcheQueueItem(slot.RequestId, slot.SessionId, slot.State, index + 1, DateTimeOffset.UtcNow - Stopwatch.GetElapsedTime(slot.AcceptedAt), slot.Request.QueueTimeoutSeconds)).ToArray(), MaxQueue, DateTimeOffset.UtcNow); }
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
