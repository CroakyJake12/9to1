using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Channels;
using Haven.Application;
using Haven.Core;

namespace Dulche.Runtime;

/// <summary>The actual application owner binds its canonical tool context and supplies existing tool schemas.</summary>
public interface IOriginalDulcheProviderToolSource
{
    Task<IReadOnlyList<OllamaToolDefinition>> BindOriginalAsync(TaskRunAttemptAdmission originalAdmission,
        Guid originalActionId, ToolExecutionContext actualRuntimeContext, CancellationToken cancellationToken);
}

/// <summary>Trusted application producer captures its actual context selections for this SAME original wire request.
/// Dulche request IDs/history/settings are never sufficient to reconstruct source authority.</summary>
public interface IOriginalDulcheProviderContextSource
{
    Task CaptureOriginalAsync(TaskRunAttemptAdmission originalAdmission, TaskExecutionSnapshot currentSnapshot,
        DulcheRequest sameFrozenRuntimeRequest, OllamaChatRequest sameOriginalProviderRequest,
        CancellationToken cancellationToken);
    Task CaptureOriginalAsync(TaskRunAttemptAdmission originalAdmission, TaskExecutionSnapshot currentSnapshot,
        DulcheRequest sameFrozenRuntimeRequest, OllamaToolRequest sameOriginalProviderRequest,
        CancellationToken cancellationToken);
}

/// <summary>Finite caller-lifetime callback scope only. It is not model, route, paid-use or tool authority.</summary>
public interface IDulcheOriginalFactoryCallbackScope
{
    T RunOriginalFactoryInvocation<T>(Func<T> actualCallback);
}

/// <summary>
/// Adapts the selected RAW provider; never calls the resilient router from inside a registered frame.
/// A Task/Run issuer lease is borrowed, retained by the shared frame owner, and never disposed here.
/// Model residency, installation and exact pause/resume are unavailable through this provider contract.
/// </summary>
public sealed class ManagedProviderDulcheAdapter : IDulcheOriginalProviderAdapter, IDulcheOriginalCancellationSource, IAsyncDisposable
{
    private const int Capacity = 128;
    private readonly object _sync = new();
    private readonly IModelProvider _provider;
    private readonly TaskExecutionCoordinator _coordinator;
    private readonly ITaskRunOriginalFrameOwner _frames;
    private readonly IOriginalDulcheProviderToolSource? _tools;
    private readonly IOriginalDulcheProviderContextSource? _contextSource;
    private readonly ITaskRunProviderContextAuthority? _contextAuthority;
    private readonly Dictionary<string, Endpoint> _endpoints = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<DulcheRequest, Request> _requests = new();
    private readonly AsyncLocal<OriginalPhase?> _executing = new();
    [ThreadStatic] private static List<Endpoint>? _physicalOriginalCalls;
    private Task? _close;
    private bool _closing;

    private ManagedProviderDulcheAdapter(IModelProvider provider, Uri configuredTarget,
        TaskExecutionCoordinator coordinator, ITaskRunOriginalFrameOwner frames, IOriginalDulcheProviderToolSource? tools,
        IOriginalDulcheProviderContextSource? contextSource, ITaskRunProviderContextAuthority? contextAuthority)
    {
        _provider = provider; OriginalConfiguredTarget = configuredTarget;
        _coordinator = coordinator; _frames = frames; _tools = tools;
        _contextSource = contextSource; _contextAuthority = contextAuthority;
    }

    /// <summary>Composition must retain/join this original factory task before disposing its configuration owner.</summary>
    public static Task<ManagedProviderDulcheAdapter> CreateAsync(string providerId,
        IModelProviderRegistry registry, IProviderConfigurationStore configurations,
        TaskExecutionCoordinator coordinator, ITaskRunOriginalFrameOwner frames,
        IOriginalDulcheProviderToolSource? tools = null, CancellationToken cancellationToken = default,
        IOriginalDulcheProviderContextSource? contextSource = null, ITaskRunProviderContextAuthority? contextAuthority = null)
        => CreateCoreAsync(providerId, registry, configurations, coordinator, frames, tools, cancellationToken, contextSource, contextAuthority, null);

    private static async Task<ManagedProviderDulcheAdapter> CreateCoreAsync(string providerId,
        IModelProviderRegistry registry, IProviderConfigurationStore configurations,
        TaskExecutionCoordinator coordinator, ITaskRunOriginalFrameOwner frames,
        IOriginalDulcheProviderToolSource? tools, CancellationToken cancellationToken,
        IOriginalDulcheProviderContextSource? contextSource, ITaskRunProviderContextAuthority? contextAuthority,
        IDulcheOriginalFactoryCallbackScope? actualFactoryOwner)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(configurations);
        ArgumentNullException.ThrowIfNull(coordinator); ArgumentNullException.ThrowIfNull(frames);
        if (actualFactoryOwner is not null) cancellationToken.ThrowIfCancellationRequested();
        Task<ProviderConfiguration?> originalConfiguration;
        try
        {
            originalConfiguration = (actualFactoryOwner is null ? configurations.GetAsync(providerId, cancellationToken)
                : actualFactoryOwner.RunOriginalFactoryInvocation(() => configurations.GetAsync(providerId, cancellationToken)))
                ?? throw new InvalidOperationException("The configuration owner returned no original task.");
        }
        catch (OperationCanceledException original) when (actualFactoryOwner is not null)
        { throw new AggregateException("The actual configuration callback faulted synchronously.", original); }
        ProviderConfiguration? configuration;
        try { configuration = await originalConfiguration.ConfigureAwait(false); }
        catch (OperationCanceledException original) when (actualFactoryOwner is not null && originalConfiguration.IsFaulted)
        {
            var causes = new List<Exception>(); Add(causes, original);
            foreach (var cause in originalConfiguration.Exception!.InnerExceptions) Add(causes, cause);
            throw new AggregateException("The original configuration task faulted.", causes);
        }
        catch (Exception error) { ThrowTask(error, originalConfiguration); throw; }
        IModelProvider provider;
        try
        {
            provider = actualFactoryOwner is null ? registry.GetRequired(providerId)
                : actualFactoryOwner.RunOriginalFactoryInvocation(() => registry.GetRequired(providerId)); // Exact raw provider, never resilient recursion.
        }
        catch (OperationCanceledException original) when (actualFactoryOwner is not null)
        { throw new AggregateException("The actual registry callback faulted synchronously.", original); }
        Uri ValidateOriginalTarget()
        {
            if (configuration is null || !configuration.IsEnabled || configuration.Id != provider.Id
                || configuration.Kind != provider.Kind || configuration.IsLocal != provider.IsLocal
                || !Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var target)
                || target.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(target.UserInfo)
                || !string.IsNullOrEmpty(target.Fragment) || !string.IsNullOrEmpty(target.Query)
                || target.Scheme != Uri.UriSchemeHttps && !target.IsLoopback)
                throw new UnauthorizedAccessException("No genuine enabled, matching configured provider target is available.");
            return target;
        }
        Uri originalTarget;
        try
        {
            originalTarget = actualFactoryOwner is null ? ValidateOriginalTarget()
                : actualFactoryOwner.RunOriginalFactoryInvocation(ValidateOriginalTarget);
        }
        catch (OperationCanceledException original) when (actualFactoryOwner is not null)
        { throw new AggregateException("The actual provider metadata callback faulted synchronously.", original); }
        cancellationToken.ThrowIfCancellationRequested();
        return new(provider, originalTarget, coordinator, frames, tools, contextSource, contextAuthority);
    }

    /// <summary>Caller publishes this actual factory Task before releasing originalStart. The
    /// original nested configuration/factory failures are retained directly, never hidden by await.</summary>
    public static async Task<ManagedProviderDulcheAdapter> CreateAfterPublicationAsync(Task originalStart, string providerId,
        IModelProviderRegistry registry, IProviderConfigurationStore configurations,
        TaskExecutionCoordinator coordinator, ITaskRunOriginalFrameOwner frames,
        IOriginalDulcheProviderToolSource? tools = null, CancellationToken cancellationToken = default,
        IOriginalDulcheProviderContextSource? contextSource = null, ITaskRunProviderContextAuthority? contextAuthority = null,
        IDulcheOriginalFactoryCallbackScope? actualFactoryOwner = null)
    {
        ArgumentNullException.ThrowIfNull(originalStart);
        await originalStart.ConfigureAwait(false);
        var actual = CreateCoreAsync(providerId, registry, configurations, coordinator, frames, tools,
            cancellationToken, contextSource, contextAuthority, actualFactoryOwner);
        try { return await actual.ConfigureAwait(false); }
        catch (Exception error) { ThrowTask(error, actual); throw; }
    }

    public string ProviderId => _provider.Id;
    public string RuntimeVersion => "managed-raw-provider-1";
    public bool IsLocal => _provider.IsLocal;
    public Uri OriginalConfiguredTarget { get; }
    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.Ordinal)
        { "chat", "streaming", "request-cancellation", "provider-health", "canonical-task-context" };

    public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken)
    {
        Endpoint owner;
        lock (_sync)
        {
            RequireOpen(); RequireConfiguredEndpoint(endpoint);
            if (_endpoints.ContainsKey(endpoint.EndpointId))
                return ValueTask.FromResult(Failure("This original endpoint was already started.", endpoint.EndpointId));
            if (_endpoints.Count >= Capacity) throw new InvalidOperationException("Original provider endpoint custody is full.");
            owner = new(this, endpoint.EndpointId);
            _endpoints.Add(owner.Id, owner);
        }
        return new(StartOwned(owner, async () =>
        {
            var original = _provider.CheckHealthAsync(cancellationToken)
                ?? throw new InvalidOperationException("The provider returned no original health task.");
            RetainRaw(owner, original);
            var health = await original.ConfigureAwait(false);
            if (health.ProviderId != ProviderId) throw new InvalidDataException("The real health result names a different provider.");
            return health.IsHealthy ? OperationResult<Unit>.Success(Unit.Value)
                : Failure("The actual configured provider is unhealthy: " + health.Message, owner.Id);
        }, cancellationToken));
    }

    public Task BindOriginalRequestAsync(DulcheEndpoint endpoint, DulcheRequest originalFrozenRequest,
        RuntimeRequestHandle originalHandle, TaskRunAttemptAdmission originalAdmission, Guid originalActionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalFrozenRequest); ArgumentNullException.ThrowIfNull(originalHandle);
        ArgumentNullException.ThrowIfNull(originalAdmission);
        Endpoint owner;
        Request request;
        lock (_sync)
        {
            owner = RequireEndpoint(endpoint);
            if (originalHandle.EndpointId != endpoint.EndpointId || originalHandle.SessionId != originalFrozenRequest.SessionId
                || string.IsNullOrEmpty(originalHandle.RequestId) || originalActionId == Guid.Empty
                || _requests.TryGetValue(originalFrozenRequest, out _)
                || owner.Requests.Any(item => item.Handle.RequestId == originalHandle.RequestId))
                throw new UnauthorizedAccessException("No fresh original frozen request/handle binding is available.");
            PruneHealthyRequests(owner);
            if (owner.CapacityRefusal is not null || owner.Requests.Count >= Capacity)
                throw CapacityRefusal(owner);
            request = new(owner, originalFrozenRequest, originalHandle, originalAdmission, originalActionId);
            owner.Requests.Add(request);
            _requests.Add(originalFrozenRequest, request);
        }
        // StartOwned publishes its exact whole task before canonical lookup/tool binding callbacks.
        return request.Binding = StartOwned(owner, async () =>
        {
            request.Registration = _frames.RegisterOriginalAttemptAsync(originalAdmission, cancellationToken);
            RetainRaw(owner, request.Registration);
            await request.Registration.ConfigureAwait(false);
            var snapshot = originalAdmission.Snapshot;
            request.IssuedLookup = _coordinator.GetIssuedAttemptAsync(snapshot.TaskId, snapshot.ExecutionId,
                originalAdmission.AttemptId, cancellationToken);
            RetainRaw(owner, request.IssuedLookup);
            var issued = await request.IssuedLookup.ConfigureAwait(false);
            if (!ReferenceEquals(issued, originalAdmission)) throw new UnauthorizedAccessException("A copied admission is not issued context.");
            request.CurrentLookup = _coordinator.GetAsync(snapshot.TaskId, cancellationToken);
            RetainRaw(owner, request.CurrentLookup);
            var current = await request.CurrentLookup.ConfigureAwait(false);
            var route = originalAdmission.Lease.Candidate;
            if (current is null || current.ExecutionId != snapshot.ExecutionId || current.OwnerBinding != originalAdmission.Lease.Owner
                || current.Attempts.LastOrDefault() is not { } attempt || attempt.Id != originalAdmission.AttemptId
                || attempt.State is not (TaskRunAttemptState.Admitted or TaskRunAttemptState.Running)
                || !current.Plan.Any(node => node.ActionId == originalActionId && node.State is TaskPlanNodeState.Pending or TaskPlanNodeState.Running)
                || route.ProviderId != ProviderId || route.UsesCloud == IsLocal
                || originalFrozenRequest.Model is not { } model || model.ProviderId != route.ProviderId
                || model.ModelId != route.ModelId || model.ArtifactRevision != route.ArtifactIdentity
                || originalFrozenRequest.CallerId != originalAdmission.Lease.Owner.ActorId)
                throw new UnauthorizedAccessException("The actual acknowledged action/model/caller does not match this issued attempt.");
            request.Context = new(snapshot.TaskId, snapshot.ContextId, snapshot.ExecutionId, originalAdmission.AttemptId,
                current.PersistenceRevision, originalActionId) { RequestedCandidate = route, SelectedCandidate = route };
            ValidateRepresentableRequest(originalFrozenRequest);
            var actualRequired = new HashSet<ToolCapability> { ToolCapability.Text };
            if (originalFrozenRequest.Stream) actualRequired.Add(ToolCapability.Streaming);
            if (originalFrozenRequest.ToolPolicy is { Mode: not ToolCallMode.None }) actualRequired.Add(ToolCapability.Tools);
            if (!actualRequired.All(capability => route.RequiredCapabilities.Contains(capability.ToString(), StringComparer.Ordinal)))
                throw new UnauthorizedAccessException("This original request expands the issued model capabilities.");
            if (model.ArtifactRevision is not null)
                throw new NotSupportedException("The raw provider catalogue exposes no exact artifact-residency witness.");
            request.Catalogue = EnrollOriginalFrame(request, () =>
                _frames.StartOriginalFrameAsync(originalAdmission,
                    token => StartActualRaw(request, _provider.GetModelsAsync, token), cancellationToken));
            var catalogue = await request.Catalogue.ConfigureAwait(false);
            var selectedModel = catalogue.SingleOrDefault(item => item.ProviderId == ProviderId
                && item.IsLocal == IsLocal && item.Name == model.ModelId);
            if (selectedModel is null || !selectedModel.Supports(ToolCapability.Text)
                || originalFrozenRequest.Stream && !selectedModel.Supports(ToolCapability.Streaming)
                || originalFrozenRequest.ToolPolicy is { Mode: not ToolCallMode.None } && !selectedModel.Supports(ToolCapability.Tools)
                || originalFrozenRequest.ContextLimit is { } contextLimit && selectedModel.ContextWindow is { } maximum && contextLimit > maximum)
                throw new NotSupportedException("The actual catalogue does not support this selected model/request capability.");
            if (originalFrozenRequest.ToolPolicy is { Mode: not ToolCallMode.None } policy)
            {
                if (_tools is null || policy.CallerId != originalFrozenRequest.CallerId || string.IsNullOrWhiteSpace(policy.ScopeId))
                    throw new UnauthorizedAccessException("The genuine original tool binding source is unavailable.");
                var runtimeContext = new ToolExecutionContext(originalHandle.RequestId, 1, originalAdmission.AttemptId.ToString("D"),
                    originalHandle.SessionId, originalHandle.EndpointId, model, policy.CallerId, originalFrozenRequest.Messages ?? [], policy,
                    cancellationToken);
                lock (_sync) RequireLive(owner);
                // This binding runs inside the already published whole adapter control task.
                // Never enter another owner/permission/frame callback while holding the adapter state lock.
                request.ToolBinding = _tools.BindOriginalAsync(originalAdmission, originalActionId, runtimeContext, cancellationToken)
                    ?? throw new InvalidOperationException("The tool owner returned no actual binding task.");
                RetainRaw(owner, request.ToolBinding);
                var definitions = await request.ToolBinding.ConfigureAwait(false);
                if (definitions.Any(tool => !policy.AllowedTools.Contains(tool.Name)
                    || originalFrozenRequest.PermittedTools is { } names && !names.Contains(tool.Name))
                    || definitions.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
                    throw new UnauthorizedAccessException("The actual supplied tool schemas exceed the original tool scope.");
                request.Tools = definitions.Select(tool => tool with
                {
                    Properties = new ReadOnlyDictionary<string, object>(tool.Properties.ToDictionary(pair => pair.Key,
                        pair => (object)JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal)),
                    Required = tool.Required.ToArray(), InputSchema = tool.InputSchema?.Clone()
                }).ToArray();
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync) { RequireLive(owner); request.IsBound = true; }
            return true;
        }, cancellationToken);
    }

    public void CaptureOriginalDispatchRequest(RuntimeRequestHandle originalHandle,
        DulcheRequest originalFrozenRequest, DulcheRequest actualDispatchRequest)
    {
        lock (_sync)
        {
            if (!_requests.TryGetValue(originalFrozenRequest, out var request) || !ReferenceEquals(request.Handle, originalHandle)
                || !ReferenceEquals(request.Frozen, originalFrozenRequest) || !request.IsBound
                || request.Frozen with { Messages = actualDispatchRequest.Messages } != actualDispatchRequest)
                throw new UnauthorizedAccessException("No original runtime-owned request projection is available.");
            RequireLive(request.Owner);
            if (_requests.TryGetValue(actualDispatchRequest, out var previous))
            {
                if (!ReferenceEquals(previous, request)) throw new UnauthorizedAccessException("A different original owns this request.");
            }
            else _requests.Add(actualDispatchRequest, request);
            request.Dispatch = actualDispatchRequest;
            request.Turns = (actualDispatchRequest.Messages ?? []).Select(message => new OllamaToolTurn(message.Role, message.Text ?? "")).ToList();
            if (actualDispatchRequest.Input is { } input) request.Turns.Add(new("user", input));
        }
    }

    public IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint, DulcheRequest request,
        string requestId, CancellationToken cancellationToken)
        => ObserveOriginalOutwardAsync(RequireRequest(endpoint, request, requestId), cancellationToken);

    public async IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint, DulcheRequest request,
        string requestId, ToolInvocationResult result, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var original = RequireRequest(endpoint, request, requestId);
        bool allReceived;
        lock (_sync)
        {
            RequireLive(original.Owner);
            if (result.Status != ToolInvocationStatus.Executed || result.Error is not null
                || !original.PendingCalls.Remove(result.InvocationId, out var actualCall)
                || original.ToolResults.Contains(result.InvocationId))
                throw new UnauthorizedAccessException("The continuation is not an executed result for an actual pending original call.");
            original.ToolResults.Add(result.InvocationId);
            original.Turns.Add(new("tool", result.Result?.GetRawText() ?? "null", ToolName: actualCall.Name));
            allReceived = original.PendingCalls.Count == 0;
        }
        if (!allReceived) yield break;
        await foreach (var delta in GenerateOriginalAsync(original, cancellationToken).ConfigureAwait(false)) yield return delta;
    }

    private async IAsyncEnumerable<AdapterDelta> ObserveOriginalOutwardAsync(Request request,
        [EnumeratorCancellation] CancellationToken ownerToken)
    {
        var enumerator = GenerateOriginalAsync(request, ownerToken).GetAsyncEnumerator(ownerToken);
        Task<bool>? move = null;
        Exception? originalFailure = null;
        var failures = new List<Exception>();
        try
        {
            while (true)
            {
                bool more;
                try
                {
                    move = enumerator.MoveNextAsync().AsTask(); RetainRequestRaw(request, move);
                    more = await move.ConfigureAwait(false); ReleaseObservedHealthyRaw(request, move);
                }
                catch (Exception error) { originalFailure = error; AddTask(failures, error, move); break; }
                if (!more) break;
                yield return enumerator.Current;
            }
        }
        finally
        {
            Task? dispose = null;
            try { dispose = enumerator.DisposeAsync().AsTask(); RetainRequestRaw(request, dispose); }
            catch (Exception error) { Add(failures, error); }
            if (dispose is not null) await Join(dispose, failures).ConfigureAwait(false);
            if (dispose is not null) ReleaseObservedHealthyRaw(request, dispose);
            if (originalFailure is AggregateException aggregate && move is { IsFaulted: true }
                && dispose is { IsCompletedSuccessfully: true } && failures.Count == 1)
                PublishOriginalCancellationObservation(request, aggregate, move, dispose);
            Throw(failures); // SAME aggregate and actual Faulted outward move survive the response projection.
        }
    }

    public bool TryObserveOriginalCancellation(RuntimeRequestHandle originalHandle, Exception originalOutwardFailure,
        CancellationToken originalOwnerCancellation, out DulcheOriginalCancellationObservation? observation)
    {
        lock (_sync)
        {
            observation = null;
            var request = _endpoints.Values.SelectMany(owner => owner.Requests)
                .SingleOrDefault(item => ReferenceEquals(item.Handle, originalHandle));
            if (request?.ActiveTurn is not { } turn || request.CancellationObservation is not { } actual
                || !ReferenceEquals(actual.Turn, turn) || !ReferenceEquals(actual.OriginalOutwardFailure, originalOutwardFailure)
                || actual.OwnerToken != originalOwnerCancellation || !originalOwnerCancellation.IsCancellationRequested
                || !actual.OriginalReaderMove.IsCanceled || !actual.OriginalRawMove.IsCanceled
                || !actual.OriginalProviderFrame.IsCanceled || !actual.OriginalProducer.IsCanceled
                || !actual.OriginalInnerMove.IsFaulted || !actual.OriginalInnerDispose.IsCompletedSuccessfully
                || !actual.OriginalReaderDispose.IsCompletedSuccessfully || !actual.OriginalResourceClose.IsCompletedSuccessfully
                || !actual.OriginalTerminalObserver.IsCompletedSuccessfully || !actual.OriginalTurnCancel.IsCompletedSuccessfully
                || !actual.OriginalTurnRelease.IsCompletedSuccessfully || turn.OriginalErrors.Count != 0
                || request.Owner.Errors.Any(error => !ReferenceEquals(error, actual.OriginalOutwardFailure)
                    && !ReferenceEquals(error, actual.OriginalReaderCause) && !ReferenceEquals(error, actual.OriginalRawCause))) return false;
            observation = actual; return true;
        }
    }

    private void PublishOriginalCancellationObservation(Request request, AggregateException error, Task outwardMove, Task outwardDispose)
    {
        lock (_sync)
        {
            if (request.ActiveTurn is not { } turn || turn.Candidate is not { } candidate
                || !ReferenceEquals(candidate.Error, error) || outwardMove.Exception?.InnerExceptions.Any(cause => ReferenceEquals(cause, error)) != true)
                return;
            var resource = turn.Resource!;
            request.CancellationObservation = new(request.Handle, error, candidate.Reader, candidate.ReaderCause,
                resource.CanceledRawMove!, resource.CanceledRawCause!, turn.ProviderFrame!, candidate.Producer,
                candidate.Terminal, candidate.ReaderDispose, candidate.Cancel, turn.OriginalRelease!, resource.OriginalClose!,
                outwardMove, outwardDispose, candidate.OwnerToken, turn);
        }
    }

    private async IAsyncEnumerable<AdapterDelta> GenerateOriginalAsync(Request request,
        [EnumeratorCancellation] CancellationToken callerCancellation)
    {
        var channel = Channel.CreateBounded<AdapterDelta>(new BoundedChannelOptions(8)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var cancellation = new OriginalTurnCancellation(this, request.Owner,
            CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, request.Lifetime.Token));
        lock (_sync) { request.ActiveTurn = cancellation; request.CancellationObservation = null; }
        Task<bool> original;
        try { original = StartOwned(request.Owner, () => ProduceOriginalTurnAsync(request, channel.Writer, cancellation), callerCancellation); }
        catch (Exception error)
        {
            var startFailures = new List<Exception>(); Add(startFailures, error);
            try { cancellation.Source.Dispose(); } catch (Exception cleanup) { Add(startFailures, cleanup); }
            Throw(startFailures); throw;
        }
        var terminal = CompleteWriterAsync(original, channel.Writer);
        RetainRaw(request.Owner, terminal);
        var reader = channel.Reader.ReadAllAsync(callerCancellation).GetAsyncEnumerator(callerCancellation);
        var failures = new List<Exception>();
        Task<bool>? move = null;
        Exception? readerCause = null;
        try
        {
            while (true)
            {
                bool more;
                try { move = reader.MoveNextAsync().AsTask(); more = await move.ConfigureAwait(false); }
                catch (Exception error) { readerCause = error; AddTask(failures, error, move); break; }
                if (!more) break;
                yield return reader.Current;
            }
        }
        finally
        {
            // Publish/coalesce the actual cancellation original on its own owner gate.
            // Callback execution never occurs under adapter, frame or policy state locks.
            var actualCancel = cancellation.CancelOriginalAsync();
            await Join(actualCancel, failures).ConfigureAwait(false);
            foreach (var error in cancellation.OriginalErrors) Add(failures, error);
            Task? dispose = null;
            try { dispose = reader.DisposeAsync().AsTask(); } catch (Exception error) { Add(failures, error); }
            if (dispose is not null) await Join(dispose, failures).ConfigureAwait(false);
            await Join(original, failures).ConfigureAwait(false);
            await Join(terminal, failures).ConfigureAwait(false);
            if (move is { IsCanceled: true } && readerCause is OperationCanceledException readerCanceled
                && readerCanceled.CancellationToken == callerCancellation && callerCancellation.IsCancellationRequested
                && original.IsCanceled && terminal.IsCompletedSuccessfully && dispose is { IsCompletedSuccessfully: true }
                && actualCancel.IsCompletedSuccessfully && cancellation.OriginalRelease is { IsCompletedSuccessfully: true }
                && cancellation.OriginalErrors.Count == 0 && cancellation.ProviderFrame is { IsCanceled: true }
                && cancellation.Resource is { OriginalClose.IsCompletedSuccessfully: true, CanceledRawMove.IsCanceled: true,
                    CanceledRawCause: not null } resource
                && failures.Count > 1 && failures.All(error => ReferenceEquals(error, readerCause) || ReferenceEquals(error, resource.CanceledRawCause)))
            {
                var aggregate = new AggregateException(failures);
                cancellation.Candidate = new(aggregate, move, readerCause, original, terminal, dispose, actualCancel, callerCancellation);
                lock (_sync) { Add(request.Owner.Errors, aggregate); foreach (var cause in failures) Add(request.Owner.Errors, cause); }
                throw aggregate;
            }
            Throw(failures);
        }
    }

    private async Task<bool> ProduceOriginalTurnAsync(Request request, ChannelWriter<AdapterDelta> writer,
        OriginalTurnCancellation cancellation)
    {
        var failures = new List<Exception>();
        var hasPendingCalls = false;
        Task? actualProvider = null;
        try
        {
            var token = cancellation.Source.Token;
            if (request.Tools.Count == 0 && request.Dispatch!.Stream)
            {
                var wire = ChatRequest(request);
                var actual = StartStreamFrame(request, wire, writer, cancellation, token);
                cancellation.ProviderFrame = actual;
                actualProvider = actual;
                await actual.ConfigureAwait(false);
            }
            else if (request.Tools.Count == 0)
            {
                var wire = ChatRequest(request);
                var actual = StartProviderFrame(request, wire, ct => _provider.CompleteAsync(wire, ct), token);
                actualProvider = actual;
                await writer.WriteAsync(new(Text: await actual.ConfigureAwait(false)), token).ConfigureAwait(false);
            }
            else
            {
                var wire = ToolRequest(request);
                var actual = StartProviderFrame(request, wire, ct => _provider.ChatWithToolsAsync(wire, ct), token);
                actualProvider = actual;
                var returned = await actual.ConfigureAwait(false);
                ToolProposal[] proposals;
                lock (_sync)
                {
                    RequireLive(request.Owner);
                    if (request.PendingCalls.Count != 0) throw new InvalidOperationException("Original pending calls have not all settled.");
                    var calls = returned.ToolCalls.Select(call => call with
                    {
                        Id = call.Id ?? Guid.NewGuid().ToString("N"),
                        Arguments = new ReadOnlyDictionary<string, JsonElement>(call.Arguments.ToDictionary(pair => pair.Key,
                            pair => pair.Value.Clone(), StringComparer.Ordinal))
                    }).ToArray();
                    if (calls.Select(call => call.Id).Distinct(StringComparer.Ordinal).Count() != calls.Length
                        || calls.Any(call => string.IsNullOrWhiteSpace(call.Id) || !request.Tools.Any(tool => tool.Name == call.Name)))
                        throw new InvalidDataException("The raw provider returned unknown or duplicated tool calls.");
                    request.Turns.Add(new("assistant", returned.Content, calls));
                    proposals = calls.Select(call =>
                    {
                        request.PendingCalls.Add(call.Id!, call);
                        return new ToolProposal(call.Id!, call.Name, JsonSerializer.SerializeToElement(call.Arguments), IsConsequential: true);
                    }).ToArray();
                }
                if (returned.Content.Length != 0) await writer.WriteAsync(new(Text: returned.Content), token).ConfigureAwait(false);
                foreach (var proposal in proposals) await writer.WriteAsync(new(ToolProposal: proposal), token).ConfigureAwait(false);
                hasPendingCalls = proposals.Length != 0;
            }
            if (!hasPendingCalls)
            {
                lock (_sync) request.Finished = true;
                await writer.WriteAsync(new(FinishReason: "completed"), token).ConfigureAwait(false);
            }
        }
        catch (Exception error) { AddTask(failures, error, actualProvider); }
        finally
        {
            var actualRelease = cancellation.ReleaseOriginalAsync();
            await Join(actualRelease, failures).ConfigureAwait(false);
            foreach (var error in cancellation.OriginalErrors) Add(failures, error);
        }
        Throw(failures);
        return true;
    }

    private async Task<bool> ProduceRawStreamAsync(Request request, OllamaChatRequest wire,
        ChannelWriter<AdapterDelta> writer, OriginalStreamResource lifetime, CancellationToken token)
    {
        Task<bool>? actualMove = null;
        try
        {
            // The maintained raw provider factories are deferred async iterators. Capture the
            // actual enumerator before a SEPARATE fresh first-MoveNext policy admission.
            var originalEnumerator = StartActualFactory(request,
                () => _provider.StreamChatAsync(wire, token).GetAsyncEnumerator(token), token);
            lifetime.Enumerator = originalEnumerator;
            var originalValue = lifetime.Context is { } context
                ? ((ITaskRunProviderInvocationFence)context).RunOriginalInvocation(
                    () => StartActualFactory(request, originalEnumerator.MoveNextAsync, token))
                : StartActualFactory(request, originalEnumerator.MoveNextAsync, token);
            actualMove = originalValue.AsTask(); // Consume this SAME original ValueTask exactly once, outside the policy callback.
            RetainRequestRaw(request, actualMove);
            while (true)
            {
                var more = await actualMove.ConfigureAwait(false);
                ReleaseObservedHealthyRaw(request, actualMove);
                if (!more) break;
                await writer.WriteAsync(new(Text: originalEnumerator.Current), token).ConfigureAwait(false);
                // Only the first actual start uses the one-use context fence. Every subsequent
                // original is retained/observed and the whole stream stays inside that frame.
                actualMove = StartActualRaw(request, _ => originalEnumerator.MoveNextAsync().AsTask(), token);
            }
        }
        catch (Exception error)
        {
            if (actualMove is { IsCanceled: true } && error is OperationCanceledException canceled && canceled.CancellationToken == token)
            { lifetime.CanceledRawMove = actualMove; lifetime.CanceledRawCause = error; }
            ThrowTask(error, actualMove); throw;
        }
        // Enumerator/context Dispose are performed by the resource owner in the frame's
        // NON-provider cleanup, not folded into acknowledgment-eligible raw body failures.
        return true;
    }

    private Task<bool> StartStreamFrame(Request request, OllamaChatRequest wire,
        ChannelWriter<AdapterDelta> writer, OriginalTurnCancellation cancellation, CancellationToken token)
        => EnrollOriginalFrame(request, () => _frames.StartOriginalResourceFrameAsync<bool, OriginalStreamResource>(
            request.Admission,
            ct => AcquireOriginalStreamResourceAsync(request, wire, cancellation, ct),
            (resource, ct) => resource.RevalidateOriginalAsync(ct),
            (resource, ct) => ProduceRawStreamAsync(request, wire, writer, resource, ct), token));

    private async Task<OriginalStreamResource> AcquireOriginalStreamResourceAsync(Request request,
        OllamaChatRequest wire, OriginalTurnCancellation cancellation, CancellationToken token)
    {
        if (IsLocal) return cancellation.Resource = new(this, request, null);
        if (_contextSource is null || _contextAuthority is null)
            throw new UnauthorizedAccessException("No trusted original source/context owner is composed for remote egress.");
        var actualAcquire = AcquireOriginalCloudFrameAsync(request, wire, token);
        RetainRaw(request.Owner, actualAcquire);
        ITaskRunProviderContextFrame context;
        try { context = await actualAcquire.ConfigureAwait(false); }
        catch (Exception error) { ThrowTask(error, actualAcquire); throw; }
        // Capture the actual acquired scope BEFORE resource revalidation can refuse.
        return cancellation.Resource = new(this, request, context);
    }

    private Task<T> StartProviderFrame<T>(Request request, object originalWire,
        Func<CancellationToken, Task<T>> rawBody, CancellationToken cancellationToken)
        => EnrollOriginalFrame(request, () =>
        {
            if (IsLocal)
                return _frames.StartOriginalFrameAsync(request.Admission,
                    ct => StartActualRaw(request, rawBody, ct), cancellationToken);
            if (_contextSource is null || _contextAuthority is null)
                throw new UnauthorizedAccessException("No trusted original source/context owner is composed for remote egress.");
            return _frames.StartOriginalResourceFrameAsync<T, ITaskRunProviderContextFrame>(request.Admission,
                ct => AcquireOriginalCloudFrameAsync(request, originalWire, ct),
                (frame, ct) =>
                {
                    if (frame is not ITaskRunProviderInvocationFence)
                        throw new UnauthorizedAccessException("The acquired context owner has no original invocation fence.");
                    var actual = frame.RevalidateAsync(ct).AsTask();
                    RetainRaw(request.Owner, actual); return actual;
                },
                (frame, ct) => ((ITaskRunProviderInvocationFence)frame).RunOriginalInvocation(
                    () => StartActualRaw(request, rawBody, ct)), cancellationToken);
        });

    private Task<T> EnrollOriginalFrame<T>(Request request, Func<Task<T>> actualStart)
    {
        lock (_sync)
        {
            RequireLive(request.Owner);
            request.OriginalFrames.RemoveAll(task => task.IsCompletedSuccessfully);
            request.RawTasks.RemoveAll(task => task.IsCompletedSuccessfully);
            if (request.CapacityRefusal is not null
                || request.OriginalFrames.Count + request.ReservedFrames >= Capacity
                || request.RawTasks.Count >= Capacity - 2)
                throw RequestCapacityRefusal(request);
            request.ReservedFrames++;
        }
        // The encompassing adapter original is already published. Never enter the shared
        // frame owner while holding adapter _sync (shared frame callbacks may enter adapter).
        try
        {
            var actual = actualStart() ?? throw new InvalidOperationException("No original frame task was returned.");
            lock (_sync) request.OriginalFrames.Add(actual); // Capture even if close sealed during start.
            return actual;
        }
        finally { lock (_sync) request.ReservedFrames--; }
    }

    private Task<T> StartActualRaw<T>(Request request, Func<CancellationToken, Task<T>> originalBody, CancellationToken token)
        => StartActualFactory(request, () =>
        {
            var original = originalBody(token) ?? throw new InvalidOperationException("The selected raw provider returned no task.");
            request.RawTasks.Add(original); return original;
        }, token);

    private T StartActualFactory<T>(Request request, Func<T> actualStart, CancellationToken token)
    {
        lock (_sync)
        {
            RequireLive(request.Owner); token.ThrowIfCancellationRequested();
            request.RawTasks.RemoveAll(task => task.IsCompletedSuccessfully);
            if (request.CapacityRefusal is not null || request.RawTasks.Count >= Capacity - 2)
                throw RequestCapacityRefusal(request); // Reserve two fixed cleanup slots; never refuse capture after acquisition.
            // The encompassing published whole task carries its live async phase.
            // The physical guard survives a callback restoring an earlier/null ExecutionContext.
            return InvokePhysicalOriginal(request.Owner, actualStart);
        }
    }

    private void RetainRequestRaw(Request request, Task actual)
    {
        lock (_sync)
        {
            if (!request.RawTasks.Any(item => ReferenceEquals(item, actual))) request.RawTasks.Add(actual);
        }
    }
    private void ReleaseObservedHealthyRaw(Request request, Task actual)
    {
        lock (_sync) if (actual.IsCompletedSuccessfully) request.RawTasks.Remove(actual);
    }
    private static Exception RequestCapacityRefusal(Request request)
    {
        var refusal = request.CapacityRefusal ??= new InvalidOperationException("Original raw provider custody is full.");
        Add(request.Owner.Errors, refusal); return refusal;
    }

    private async Task<ITaskRunProviderContextFrame> AcquireOriginalCloudFrameAsync(Request request,
        object wire, CancellationToken cancellationToken)
    {
        var lookup = _coordinator.GetAsync(request.Admission.Snapshot.TaskId, cancellationToken);
        RetainRaw(request.Owner, lookup);
        TaskExecutionSnapshot? current;
        try { current = await lookup.ConfigureAwait(false); } catch (Exception error) { ThrowTask(error, lookup); throw; }
        if (current is null || current.ExecutionId != request.Admission.Snapshot.ExecutionId
            || current.Attempts.LastOrDefault()?.Id != request.Admission.AttemptId)
            throw new UnauthorizedAccessException("The actual original Task/Run attempt is no longer current.");
        Task capture; Task<ITaskRunProviderContextFrame> acquire;
        if (wire is OllamaChatRequest chat)
        {
            capture = _contextSource!.CaptureOriginalAsync(request.Admission, current, request.Frozen, chat, cancellationToken);
            RetainRaw(request.Owner, capture);
            try { await capture.ConfigureAwait(false); } catch (Exception error) { ThrowTask(error, capture); throw; }
            acquire = _contextAuthority!.AcquireOriginalFrameAsync(request.Admission, current, chat, chat, cancellationToken).AsTask();
        }
        else if (wire is OllamaToolRequest tool)
        {
            capture = _contextSource!.CaptureOriginalAsync(request.Admission, current, request.Frozen, tool, cancellationToken);
            RetainRaw(request.Owner, capture);
            try { await capture.ConfigureAwait(false); } catch (Exception error) { ThrowTask(error, capture); throw; }
            acquire = _contextAuthority!.AcquireOriginalFrameAsync(request.Admission, current, tool, tool, cancellationToken).AsTask();
        }
        else throw new InvalidOperationException("No actual typed provider request was captured.");
        RetainRaw(request.Owner, acquire);
        try { return await acquire.ConfigureAwait(false); } catch (Exception error) { ThrowTask(error, acquire); throw; }
    }

    public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken)
    {
        Endpoint owner;
        Request request;
        lock (_sync)
        {
            owner = RequireEndpoint(endpoint);
            request = owner.Requests.SingleOrDefault(item => item.Handle.RequestId == requestId)
                ?? throw new InvalidOperationException("No actual owned request is registered.");
        }
        return new(StartOwned(owner, () =>
        {
            InvokePhysicalOriginal(owner, () => { request.Lifetime.Cancel(); return true; }); // Only this owned request.
            return Task.FromResult(OperationResult<Unit>.Success(Unit.Value));
        }, cancellationToken, allowRetiredCancellationBody: true));
    }
    public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken)
        => ValueTask.FromResult(Unsupported("Exact provider pause is not exposed.", requestId));
    public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken)
        => ValueTask.FromResult(Unsupported("Exact provider resume is not exposed.", requestId));

    public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken)
    {
        Endpoint owner;
        lock (_sync) owner = RequireEndpoint(endpoint);
        return new(StartOwned(owner, async () =>
        {
            var actual = _provider.CheckHealthAsync(cancellationToken);
            RetainRaw(owner, actual);
            var health = await actual.ConfigureAwait(false);
            if (health.ProviderId != ProviderId) throw new InvalidDataException("The real health result belongs to another provider.");
            return new RuntimeHealth(owner.Id, health.IsHealthy ? endpoint.State : EndpointState.Failed,
                health.CheckedAt, health.Message, Metric<double>.Na, Metric<double>.Na, Metric<double>.Na);
        }, cancellationToken));
    }

    public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<OperationResult<Unit>> original;
        lock (_sync)
        {
            if (!_endpoints.TryGetValue(endpoint.EndpointId, out var owner)) throw new InvalidOperationException("No actual provider endpoint was acquired.");
            RequireConfiguredEndpoint(endpoint);
            if (IsLiveOriginalCall(owner)) throw new InvalidOperationException("A provider original cannot join its own endpoint close.");
            original = StartClose(owner);
        }
        return new(cancellationToken.CanBeCanceled ? original.WaitAsync(cancellationToken) : original);
    }
    private Task<OperationResult<Unit>> StartClose(Endpoint owner)
    {
        if (owner.Close is not null) return owner.Close;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Close = CloseEndpointAsync(owner, gate.Task);
        owner.Sealed = true;
        gate.SetResult();
        return owner.Close;
    }
    private async Task<OperationResult<Unit>> CloseEndpointAsync(Endpoint owner, Task start)
    {
        await start.ConfigureAwait(false);
        var previous = _executing.Value;
        var phase = new OriginalPhase(owner, previous); _executing.Value = phase;
        Task<OperationResult<Unit>>? actual = null;
        try
        {
            actual = owner.OriginalCloseBody = CloseEndpointBodyAsync(owner, start);
            return await actual.ConfigureAwait(false);
        }
        catch (Exception error) { ThrowTask(error, actual); throw; }
        finally { phase.Retire(); _executing.Value = previous; }
    }
    private async Task<OperationResult<Unit>> CloseEndpointBodyAsync(Endpoint owner, Task start)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        Request[] requests; Task[] work;
        lock (_sync) { requests = owner.Requests.ToArray(); work = owner.Work.ToArray(); }
        foreach (var request in requests)
            try { InvokePhysicalOriginal(owner, () => { request.Lifetime.Cancel(); return true; }); } catch (Exception error) { Add(failures, error); }
        foreach (var original in work) await Join(original, failures).ConfigureAwait(false);
        // All published bindings/control originals are now terminal; capture any raw tasks acquired inside them.
        Task[] raw;
        lock (_sync) raw = owner.RawTasks.Concat(requests.SelectMany(request => request.OriginalFrames.Concat(request.RawTasks))).ToArray();
        foreach (var original in raw) await Join(original, failures).ConfigureAwait(false);
        foreach (var request in requests)
            try { request.Lifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
        lock (_sync) foreach (var error in owner.Errors) Add(failures, error);
        Throw(failures);
        return OperationResult<Unit>.Success(Unit.Value);
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_endpoints.Values.Any(IsLiveOriginalCall)) throw new InvalidOperationException("A provider original cannot join the adapter owner containing it.");
            if (_close is null)
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseAllAsync(gate.Task);
                _closing = true;
                foreach (var owner in _endpoints.Values) owner.Sealed = true;
                gate.SetResult();
            }
            return new(_close);
        }
    }
    private async Task CloseAllAsync(Task start)
    {
        await start.ConfigureAwait(false);
        Task[] originals;
        lock (_sync) originals = _endpoints.Values.Select(StartClose).ToArray();
        var failures = new List<Exception>();
        foreach (var original in originals) await Join(original, failures).ConfigureAwait(false);
        Throw(failures);
    }

    private Task<T> StartOwned<T>(Endpoint owner, Func<Task<T>> body, CancellationToken cancellationToken,
        bool allowRetiredCancellationBody = false)
    {
        TaskCompletionSource gate;
        Task<T> actual;
        lock (_sync)
        {
            RequireLive(owner); cancellationToken.ThrowIfCancellationRequested();
            owner.Work.RemoveAll(task => task.IsCompletedSuccessfully);
            owner.RawTasks.RemoveAll(task => task.IsCompletedSuccessfully);
            if (owner.CapacityRefusal is not null || owner.Work.Count >= Capacity) throw CapacityRefusal(owner);
            gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = RunOwnedAsync(owner, gate.Task, body, cancellationToken, allowRetiredCancellationBody);
            owner.Work.Add(actual);
        }
        gate.SetResult(); return actual;
    }
    private async Task<T> RunOwnedAsync<T>(Endpoint owner, Task start, Func<Task<T>> body, CancellationToken cancellationToken,
        bool allowRetiredCancellationBody)
    {
        await start.ConfigureAwait(false);
        var old = _executing.Value;
        var phase = new OriginalPhase(owner, old); _executing.Value = phase;
        Task<T>? original = null;
        try
        {
            lock (_sync)
            {
                // Admission was already checked and the SAME whole task published before close.
                // Only its finite owned CTS cancellation may run after retirement; all provider
                // callbacks/actual raw starts retain the independent live-owner admission gates.
                if (!allowRetiredCancellationBody) RequireLive(owner);
                cancellationToken.ThrowIfCancellationRequested();
            }
            // The whole original was enrolled by StartOwned before this callback. Shared owner
            // APIs/permission/tool callbacks never execute while adapter state is locked.
            original = InvokePhysicalOriginal(owner, body) ?? throw new InvalidOperationException("No original provider control task was returned.");
            RetainRaw(owner, original);
            return await original.ConfigureAwait(false);
        }
        catch (Exception error) { ThrowTask(error, original); throw; }
        finally { phase.Retire(); _executing.Value = old; }
    }
    private void RetainRaw(Endpoint owner, Task actual) { lock (_sync) owner.RawTasks.Add(actual); }
    private void PruneHealthyRequests(Endpoint owner)
    {
        // Refusing at a fixed bound is preferable to retiring unknown work or clearing a fault history.
        // A complete runtime/attempt consumer may later provide acknowledged request retirement.
        _ = owner;
    }
    private Request RequireRequest(DulcheEndpoint endpoint, DulcheRequest actual, string requestId)
    {
        lock (_sync)
        {
            var owner = RequireEndpoint(endpoint);
            if (!_requests.TryGetValue(actual, out var request) || !ReferenceEquals(request.Owner, owner)
                || !ReferenceEquals(request.Dispatch, actual) || request.Handle.RequestId != requestId || !request.IsBound)
                throw new UnauthorizedAccessException("This is not the actual runtime's captured dispatch request.");
            return request;
        }
    }
    private Endpoint RequireEndpoint(DulcheEndpoint endpoint)
    {
        RequireConfiguredEndpoint(endpoint);
        if (!_endpoints.TryGetValue(endpoint.EndpointId, out var owner)) throw new InvalidOperationException("No actual managed endpoint was acquired.");
        RequireLive(owner); return owner;
    }
    private void RequireConfiguredEndpoint(DulcheEndpoint endpoint)
    {
        if (endpoint.ProviderId != ProviderId || !Uri.TryCreate(endpoint.Target, UriKind.Absolute, out var target)
            || target.GetLeftPart(UriPartial.Path) != OriginalConfiguredTarget.GetLeftPart(UriPartial.Path)
            || endpoint.IsRemote == IsLocal)
            throw new UnauthorizedAccessException("This endpoint is not the original configured provider target.");
    }
    private void RequireOpen() { if (_closing) throw new ObjectDisposedException(nameof(ManagedProviderDulcheAdapter)); }
    private void RequireLive(Endpoint owner) { RequireOpen(); if (owner.Sealed) throw new ObjectDisposedException("Original provider endpoint"); }
    private static Exception CapacityRefusal(Endpoint owner)
    {
        var refusal = owner.CapacityRefusal ??= new InvalidOperationException("Original provider custody is full; a new owner is required.");
        Add(owner.Errors, refusal); return refusal;
    }
    private static void ValidateRepresentableRequest(DulcheRequest request)
    {
        var settings = request.Settings;
        if (request.Messages?.Any(message => message.Inputs is { Count: > 0 }) == true
            || request.OutputSchema is not null || request.GenerativeContainer is not null
            || request.Budget is { MaximumOutputTokens: not null } or { MaximumCost: not null }
            || settings is { MaximumOutputTokens: not null } or { TopP: not null } or { TopK: not null }
                or { Seed: not null } or { StopSequences: not null } or { Penalties: not null } or { OutputSchema: not null }
            || settings?.ReasoningLevel is { } effort && !Enum.TryParse<EffortLevel>(effort, true, out _))
            throw new NotSupportedException("The raw provider contract cannot faithfully express this requested input/settings capability.");
    }
    private static EffortLevel Effort(Request request) => request.Dispatch?.Settings?.ReasoningLevel is { } value
        ? Enum.Parse<EffortLevel>(value, true) : EffortLevel.Medium;
    private static GenerationOptions Options(Request request) => new(request.Dispatch?.Settings?.Temperature ?? 0.7,
        request.Dispatch?.ContextLimit ?? 32768, request.Dispatch?.Budget?.MaximumSteps ?? 24);
    private static OllamaChatRequest ChatRequest(Request request) => new(request.Frozen.Model!.ModelId,
        request.Turns.Select(turn => new OllamaMessage(turn.Role, turn.Content, turn.Images)).ToArray(), Effort(request),
        Options: Options(request)) { ExecutionContext = request.Context };
    private static OllamaToolRequest ToolRequest(Request request) => new(request.Frozen.Model!.ModelId,
        request.Turns.ToArray(), request.Tools, Effort(request), Options: Options(request)) { ExecutionContext = request.Context };
    private static async Task CompleteWriterAsync<T>(Task original, ChannelWriter<T> writer)
    {
        Exception? failure = null;
        try { await original.ConfigureAwait(false); } catch (Exception error) { failure = (Exception?)original.Exception ?? error; }
        finally { writer.TryComplete(failure); }
    }
    private static OperationResult<Unit> Failure(string message, string target) => OperationResult<Unit>.Failure(
        new(DulcheErrorCode.ProviderUnavailable, message, target, false));
    private static OperationResult<Unit> Unsupported(string message, string target) => OperationResult<Unit>.Failure(
        new(DulcheErrorCode.UnsupportedCapability, message, target, false));
    private static void Add(List<Exception> errors, Exception error) { if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error); }
    private static void AddTask(List<Exception> errors, Exception error, Task? actual)
    {
        Add(errors, error); if (actual?.Exception is { } group) foreach (var cause in group.InnerExceptions) Add(errors, cause);
    }
    private static async Task Join(Task original, List<Exception> errors)
    { try { await original.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, error, original); } }
    private static void ThrowTask(Exception error, Task? original) { var failures = new List<Exception>(); AddTask(failures, error, original); Throw(failures); }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }
    private sealed class OriginalStreamResource(ManagedProviderDulcheAdapter adapter, Request request,
        ITaskRunProviderContextFrame? context) : IAsyncDisposable
    {
        public readonly ITaskRunProviderContextFrame? Context = context;
        public IAsyncEnumerator<string>? Enumerator;
        private Task? _close;
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        public Task? CanceledRawMove;
        public Exception? CanceledRawCause;
        private readonly object _gate = new();

        public Task RevalidateOriginalAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Context is null) return Task.CompletedTask; // Local resource has no cloud authority to acquire.
            if (Context is not ITaskRunProviderInvocationFence)
                throw new UnauthorizedAccessException("The actual context has no original invocation fence.");
            var actual = Context.RevalidateAsync(token).AsTask();
            adapter.RetainRaw(request.Owner, actual);
            return actual;
        }
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource? start = null;
            Task actual;
            lock (_gate)
            {
                if (_close is null)
                {
                    start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _close = CloseOriginalAsync(start.Task);
                }
                actual = _close;
            }
            start?.SetResult();
            return new(actual);
        }
        private async Task CloseOriginalAsync(Task start)
        {
            await start.ConfigureAwait(false);
            var failures = new List<Exception>();
            Task? actualEnumeratorClose = null, actualContextClose = null;
            // Keep the SAME cloud context alive through original enumerator/finally drain.
            // Failure of that cleanup never skips acquiring/joining context cleanup.
            if (Enumerator is { } enumerator)
            {
                try { actualEnumeratorClose = enumerator.DisposeAsync().AsTask(); adapter.RetainRequestRaw(request, actualEnumeratorClose); }
                catch (Exception error) { Add(failures, error); }
            }
            if (actualEnumeratorClose is not null) await Join(actualEnumeratorClose, failures).ConfigureAwait(false);
            if (Context is { } scope)
            {
                try { actualContextClose = scope.DisposeAsync().AsTask(); adapter.RetainRequestRaw(request, actualContextClose); }
                catch (Exception error) { Add(failures, error); }
            }
            if (actualContextClose is not null) await Join(actualContextClose, failures).ConfigureAwait(false);
            if (actualEnumeratorClose is not null) adapter.ReleaseObservedHealthyRaw(request, actualEnumeratorClose);
            if (actualContextClose is not null) adapter.ReleaseObservedHealthyRaw(request, actualContextClose);
            Throw(failures);
        }
    }

    private sealed class OriginalTurnCancellation(ManagedProviderDulcheAdapter adapter, Endpoint owner,
        CancellationTokenSource source)
    {
        public readonly CancellationTokenSource Source = source;
        private readonly object _gate = new();
        private readonly List<Exception> _errors = new();
        private Task? _cancel, _release;
        public Task? OriginalRelease { get { lock (_gate) return _release; } }
        public OriginalStreamResource? Resource;
        public Task? ProviderFrame;
        public CancellationCandidate? Candidate;
        public IReadOnlyList<Exception> OriginalErrors { get { lock (_gate) return _errors.ToArray(); } }

        public Task CancelOriginalAsync()
        {
            TaskCompletionSource? start = null; Task actual;
            lock (_gate)
            {
                if (_release is not null) return _release;
                if (_cancel is null)
                {
                    start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _cancel = CancelCoreAsync(start.Task);
                }
                actual = _cancel;
            }
            start?.SetResult();
            return actual;
        }
        private async Task CancelCoreAsync(Task start)
        {
            await start.ConfigureAwait(false);
            var old = adapter._executing.Value;
            var phase = new OriginalPhase(owner, old); adapter._executing.Value = phase;
            var errors = new List<Exception>();
            try { InvokePhysicalOriginal(owner, () => { Source.Cancel(); return true; }); } catch (Exception error) { Add(errors, error); }
            finally { phase.Retire(); adapter._executing.Value = old; }
            lock (_gate) foreach (var error in errors) Add(_errors, error);
            Throw(errors); // Exact callback causes also stay in OriginalErrors if async cancellation normalizes status.
        }
        public Task ReleaseOriginalAsync()
        {
            TaskCompletionSource? start = null; Task actual;
            lock (_gate)
            {
                if (_release is null)
                {
                    start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _release = ReleaseCoreAsync(start.Task, _cancel);
                }
                actual = _release;
            }
            start?.SetResult();
            return actual;
        }
        private async Task ReleaseCoreAsync(Task start, Task? actualCancel)
        {
            await start.ConfigureAwait(false);
            var errors = new List<Exception>();
            if (actualCancel is not null) await Join(actualCancel, errors).ConfigureAwait(false);
            try { Source.Dispose(); } catch (Exception error) { Add(errors, error); }
            lock (_gate)
            {
                foreach (var error in errors) Add(_errors, error);
                foreach (var error in _errors) Add(errors, error);
            }
            Throw(errors);
        }
    }
    /// <summary>Finite, lock-free dependency check for a containing lifetime owner before it seals close.
    /// The physical stack survives restored ExecutionContext inside an actual provider callback.</summary>
    public void RequireIndependentOriginalProviderJoin()
    {
        for (var phase = _executing.Value; phase is not null; phase = phase.Parent)
            if (phase.IsLive) throw new InvalidOperationException("A provider original cannot join its containing service.");
        if (_physicalOriginalCalls?.Any(actual => ReferenceEquals(actual.OriginalOwner, this)) == true)
            throw new InvalidOperationException("A provider original cannot join its containing service.");
    }

    private bool IsLiveOriginalCall(Endpoint owner)
    {
        for (var phase = _executing.Value; phase is not null; phase = phase.Parent)
            if (phase.IsLive && ReferenceEquals(phase.Owner, owner)) return true;
        return _physicalOriginalCalls?.Any(actual => ReferenceEquals(actual, owner)) == true;
    }
    private static T InvokePhysicalOriginal<T>(Endpoint owner, Func<T> callback)
    {
        var calls = _physicalOriginalCalls ??= new(); calls.Add(owner);
        try { return callback(); }
        finally { calls.RemoveAt(calls.Count - 1); }
    }
    private sealed class OriginalPhase(Endpoint owner, OriginalPhase? parent)
    {
        public Endpoint Owner { get; } = owner;
        public OriginalPhase? Parent { get; } = parent;
        private int _live = 1;
        public bool IsLive => Volatile.Read(ref _live) != 0;
        public void Retire() => Interlocked.Exchange(ref _live, 0);
    }
    private sealed record CancellationCandidate(AggregateException Error, Task Reader, Exception ReaderCause,
        Task Producer, Task Terminal, Task ReaderDispose, Task Cancel, CancellationToken OwnerToken);

    private sealed class Endpoint(ManagedProviderDulcheAdapter originalOwner, string id)
    {
        public readonly ManagedProviderDulcheAdapter OriginalOwner = originalOwner;
        public readonly string Id = id;
        public readonly List<Request> Requests = new();
        public readonly List<Task> Work = new(), RawTasks = new();
        public readonly List<Exception> Errors = new();
        public Task<OperationResult<Unit>>? Close, OriginalCloseBody;
        public bool Sealed;
        public Exception? CapacityRefusal;
    }
    private sealed class Request(Endpoint owner, DulcheRequest frozen, RuntimeRequestHandle handle,
        TaskRunAttemptAdmission admission, Guid actionId)
    {
        public readonly Endpoint Owner = owner;
        public readonly DulcheRequest Frozen = frozen;
        public readonly RuntimeRequestHandle Handle = handle;
        public readonly TaskRunAttemptAdmission Admission = admission;
        public readonly Guid ActionId = actionId;
        public readonly CancellationTokenSource Lifetime = new();
        public readonly List<Task> OriginalFrames = new(), RawTasks = new();
        public readonly Dictionary<string, OllamaToolCall> PendingCalls = new(StringComparer.Ordinal);
        public readonly HashSet<string> ToolResults = new(StringComparer.Ordinal);
        public List<OllamaToolTurn> Turns = new();
        public IReadOnlyList<OllamaToolDefinition> Tools = [];
        public ProviderExecutionContext? Context;
        public DulcheRequest? Dispatch;
        public bool IsBound, Finished;
        public int ReservedFrames;
        public Task? Binding, Registration;
        public Task<TaskRunAttemptAdmission?>? IssuedLookup;
        public Task<TaskExecutionSnapshot?>? CurrentLookup;
        public Task<IReadOnlyList<OllamaToolDefinition>>? ToolBinding;
        public Task<IReadOnlyList<ProviderModelDescriptor>>? Catalogue;
        public Exception? CapacityRefusal;
        public OriginalTurnCancellation? ActiveTurn;
        public DulcheOriginalCancellationObservation? CancellationObservation;
    }
}
