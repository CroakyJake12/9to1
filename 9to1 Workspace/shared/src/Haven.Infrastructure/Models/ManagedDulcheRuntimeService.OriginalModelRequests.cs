using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Typed original local inference only. The caller supplies an already-issued admission
/// and an actually acknowledged response action; this source creates neither. Tool-call data
/// returns to the existing Chat tool owner. No request is projected through the Dulche tool loop.</summary>
public interface IManagedDulcheOriginalModelRequestConsumer
{
  Task<string> CompleteOriginalSelectedModelAsync(OllamaChatRequest sameOriginalRequest, OllamaChatRequest sameRequest,
    TaskRunAttemptAdmission sameAdmission, TaskRunOriginalResponseActionAcknowledgment sameAcknowledgment, CancellationToken cancellationToken);
  IAsyncEnumerable<string> StreamOriginalSelectedModelAsync(OllamaChatRequest sameOriginalRequest, OllamaChatRequest sameRequest,
    TaskRunAttemptAdmission sameAdmission, TaskRunOriginalResponseActionAcknowledgment sameAcknowledgment, CancellationToken cancellationToken);
  Task<OllamaToolResponse> ToolsOriginalSelectedModelAsync(OllamaToolRequest sameOriginalRequest, OllamaToolRequest sameRequest,
    TaskRunAttemptAdmission sameAdmission, TaskRunOriginalResponseActionAcknowledgment sameAcknowledgment, CancellationToken cancellationToken);
}

public sealed partial class ManagedDulcheRuntimeService : IManagedDulcheOriginalModelRequestConsumer
{
  private readonly List<SelectedRequestEndpoint> _selectedRequestEndpoints = [];
  private readonly List<SelectedRequestCall> _selectedRequestCalls = [];

  public Task<string> CompleteOriginalSelectedModelAsync(OllamaChatRequest sameOriginalRequest, OllamaChatRequest sameRequest,
    TaskRunAttemptAdmission sameAdmission, TaskRunOriginalResponseActionAcknowledgment sameAcknowledgment, CancellationToken cancellationToken)
    => BeginSelectedRequest(sameRequest, sameAdmission, OriginalResponseAction(sameAcknowledgment), "complete", async () =>
    {
      DemandOriginalResponseAcknowledgment(sameOriginalRequest, sameRequest, sameAdmission, sameAcknowledgment);
      using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_ownerStop.Token, cancellationToken);
      var source = await GetSelectedRequestSourceAsync(sameAdmission).ConfigureAwait(false);
      var raw = AcquireOriginalModelSource(() => source.CompleteOriginalAsync(sameRequest, sameAdmission,
        sameAcknowledgment.ActualActionId, new SelectedRequestCallerScope(this), lifetime.Token));
      try { return await raw.ConfigureAwait(false); }
      catch (Exception cause) { ThrowObservedModelTask(cause, raw); throw; }
    });

  public Task<OllamaToolResponse> ToolsOriginalSelectedModelAsync(OllamaToolRequest sameOriginalRequest, OllamaToolRequest sameRequest,
    TaskRunAttemptAdmission sameAdmission, TaskRunOriginalResponseActionAcknowledgment sameAcknowledgment, CancellationToken cancellationToken)
    => BeginSelectedRequest(sameRequest, sameAdmission, OriginalResponseAction(sameAcknowledgment), "tools", async () =>
    {
      DemandOriginalResponseAcknowledgment(sameOriginalRequest, sameRequest, sameAdmission, sameAcknowledgment);
      using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_ownerStop.Token, cancellationToken);
      var source = await GetSelectedRequestSourceAsync(sameAdmission).ConfigureAwait(false);
      var raw = AcquireOriginalModelSource(() => source.ToolsOriginalAsync(sameRequest, sameAdmission,
        sameAcknowledgment.ActualActionId, new SelectedRequestCallerScope(this), lifetime.Token));
      try { return await raw.ConfigureAwait(false); }
      catch (Exception cause) { ThrowObservedModelTask(cause, raw); throw; }
    });

  public async IAsyncEnumerable<string> StreamOriginalSelectedModelAsync(OllamaChatRequest sameOriginalRequest, OllamaChatRequest sameRequest,
    TaskRunAttemptAdmission sameAdmission, TaskRunOriginalResponseActionAcknowledgment sameAcknowledgment,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(sameRequest); ArgumentNullException.ThrowIfNull(sameAdmission);
    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_ownerStop.Token, cancellationToken);
    var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    var producer = BeginSelectedRequest(sameRequest, sameAdmission, OriginalResponseAction(sameAcknowledgment), "stream", Produce);
    var readFinished = false;
    var errors = new List<Exception>();
    try
    {
      while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        while (channel.Reader.TryRead(out var value)) yield return value;
      readFinished = true;
    }
    finally
    {
      if (!readFinished)
        try { InvokePhysical(() => { lifetime.Cancel(); return true; }); } catch (Exception cause) { Add(errors, cause); }
      // EOF/channel notification is never proof of the actual producer or reader disposal.
      await Join(producer, errors).ConfigureAwait(false);
      Throw(errors);
    }
    async Task<bool> Produce()
    {
      IAsyncEnumerator<string>? reader = null;
      Task<bool>? move = null; Task? dispose = null;
      var failures = new List<Exception>();
      try
      {
        DemandOriginalResponseAcknowledgment(sameOriginalRequest, sameRequest, sameAdmission, sameAcknowledgment);
        var source = await GetSelectedRequestSourceAsync(sameAdmission).ConfigureAwait(false);
        reader = InvokeSelectedRequestFactory(() => source.StreamOriginalAsync(sameRequest, sameAdmission,
          sameAcknowledgment.ActualActionId, new SelectedRequestCallerScope(this), lifetime.Token).GetAsyncEnumerator(lifetime.Token));
        for (var count = 0; ; count++)
        {
          if (count >= 4096) throw new InvalidOperationException("Original request stream move custody is full.");
          move = AcquireOriginalModelSource(() => reader.MoveNextAsync().AsTask());
          bool available;
          try { available = await move.ConfigureAwait(false); }
          catch (Exception cause) { ThrowObservedModelTask(cause, move); throw; }
          if (!available) break;
          var value = InvokeSelectedRequestFactory(() => reader.Current);
          await channel.Writer.WriteAsync(value, lifetime.Token).ConfigureAwait(false);
        }
      }
      catch (Exception cause) { AddTask(failures, cause, move); }
      finally
      {
        if (reader is not null)
        {
          try { dispose = InvokePhysical(() => {
            var actual = reader.DisposeAsync().AsTask(); RetainOriginalModelSource(actual); return actual;
          }); }
          catch (Exception cause) { Add(failures, cause is OperationCanceledException
            ? new AggregateException("The original stream dispose factory faulted synchronously.", cause) : cause); }
          if (dispose is not null) await Join(dispose, failures).ConfigureAwait(false);
        }
        channel.Writer.TryComplete();
      }
      if (failures.Any(cause => cause is OperationCanceledException) && (move?.IsFaulted == true || dispose?.IsFaulted == true))
        throw new AggregateException("The actual original stream operation faulted.", failures);
      Throw(failures); return true;
    }
  }

  private T InvokeSelectedRequestFactory<T>(Func<T> callback)
  {
    try { return InvokePhysical(callback); }
    catch (OperationCanceledException cause) { throw new AggregateException("The actual request factory faulted synchronously.", cause); }
  }

  private static Guid OriginalResponseAction(TaskRunOriginalResponseActionAcknowledgment acknowledgment)
  { ArgumentNullException.ThrowIfNull(acknowledgment); return acknowledgment.ActualActionId; }

  private void DemandOriginalResponseAcknowledgment(OllamaChatRequest original, OllamaChatRequest routed,
    TaskRunAttemptAdmission admission, TaskRunOriginalResponseActionAcknowledgment acknowledgment)
  {
    ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(routed);
    ArgumentNullException.ThrowIfNull(acknowledgment);
    var source = InvokePhysical(() => _coordinator.TryGetOriginalResponseActionSource(original));
    if (source is null || !InvokePhysical(() => source.IsIssuedOriginalResponseActionAcknowledgment(acknowledgment, original, admission))
      || (routed with { Model = original.Model, ExecutionContext = original.ExecutionContext }) != original)
      throw new UnauthorizedAccessException("No SAME privately acknowledged original response/wire exists.");
    DemandAcknowledgedContext(routed.ExecutionContext, admission, acknowledgment);
  }
  private void DemandOriginalResponseAcknowledgment(OllamaToolRequest original, OllamaToolRequest routed,
    TaskRunAttemptAdmission admission, TaskRunOriginalResponseActionAcknowledgment acknowledgment)
  {
    ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(routed);
    ArgumentNullException.ThrowIfNull(acknowledgment);
    var source = InvokePhysical(() => _coordinator.TryGetOriginalResponseActionSource(original));
    if (source is null || !InvokePhysical(() => source.IsIssuedOriginalResponseActionAcknowledgment(acknowledgment, original, admission))
      || (routed with { Model = original.Model, ExecutionContext = original.ExecutionContext }) != original)
      throw new UnauthorizedAccessException("No SAME privately acknowledged original tool-response/wire exists.");
    DemandAcknowledgedContext(routed.ExecutionContext, admission, acknowledgment);
  }
  private static void DemandAcknowledgedContext(ProviderExecutionContext? context, TaskRunAttemptAdmission admission,
    TaskRunOriginalResponseActionAcknowledgment acknowledgment)
  {
    var acknowledged = acknowledgment.AcknowledgedSnapshot;
    if (!ReferenceEquals(acknowledgment.OriginalAdmission, admission) || !acknowledgment.OriginalRegistration.IsCompletedSuccessfully
      || acknowledgment.OriginalSources.Any(task => !task.IsCompletedSuccessfully)
      || context is null || context.TaskId != acknowledged.TaskId || context.ContextId != acknowledged.ContextId
      || context.ExecutionId != acknowledged.ExecutionId || context.AttemptId != admission.AttemptId
      || context.PersistenceRevision != acknowledged.PersistenceRevision || context.ActionId != acknowledgment.ActualActionId)
      throw new UnauthorizedAccessException("The actual response registration/context has not been acknowledged.");
  }

  private Task<T> BeginSelectedRequest<T>(object sameRequest, TaskRunAttemptAdmission sameAdmission,
    Guid action, string kind, Func<Task<T>> body)
  {
    ArgumentNullException.ThrowIfNull(sameRequest); ArgumentNullException.ThrowIfNull(sameAdmission);
    if (action == Guid.Empty) throw new UnauthorizedAccessException("An actual response action acknowledgment is required.");
    TaskCompletionSource start; Task<T> actual;
    lock (_sync)
    {
      RequireOpen();
      var prior = _selectedRequestCalls.FirstOrDefault(call => ReferenceEquals(call.Request, sameRequest));
      if (prior is not null)
        throw new InvalidOperationException("This original typed request is already owned; retained results are not disclosed by another enrollment.");
      if (_inferenceComposition is null)
        throw new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,
          "Genuine request-bound inference observation/installation sources are not configured.", sameAdmission.Lease.Candidate.ModelId, false));
      if (_selectedRequestCalls.Count >= RetainedWorkCapacity || _originalWork.Count >= RetainedWorkCapacity)
        throw new InvalidOperationException("Original selected request custody is full.");
      start = Signal();
      var call = new SelectedRequestCall(sameRequest, sameAdmission, action, kind);
      actual = RunOriginalAsync(start.Task, body); call.Driver = actual;
      _selectedRequestCalls.Add(call); _originalWork.Add(actual);
    }
    start.SetResult(); return actual;
  }

  private async Task<IOriginalInferenceEngineRequestSource> GetSelectedRequestSourceAsync(TaskRunAttemptAdmission admission)
  {
    SelectedRequestEndpoint original; TaskCompletionSource? start = null;
    lock (_sync)
    {
      RequireOpen();
      original = _selectedRequestEndpoints.FirstOrDefault(item => ReferenceEquals(item.Admission, admission))!;
      if (original is null)
      {
        if (_selectedRequestEndpoints.Count >= RetainedWorkCapacity || _originalWork.Count >= RetainedWorkCapacity) throw new InvalidOperationException("Original request endpoint custody is full.");
        var route = admission.Lease.Candidate;
        original = new(admission, new(route.ProviderId, route.ModelId, route.ArtifactIdentity));
        start = Signal(); original.Initialization = RunOriginalAsync(start.Task, () => InitializeSelectedRequestEndpointAsync(original));
        _selectedRequestEndpoints.Add(original); _originalWork.Add(original.Initialization);
      }
    }
    start?.SetResult();
    try { await original.Initialization!.ConfigureAwait(false); }
    catch (Exception cause) { ThrowObservedModelTask(cause, original.Initialization!); throw; }
    await ValidateSelectedRequestAdmissionAsync(original).ConfigureAwait(false);
    return InvokePhysical(() => {
      lock (_sync) RequireOpen();
      if (original.Lease?.Adapter is not InferenceEngineDispatcher dispatcher || original.Startup is not { WasAcquired: true } startup)
        throw new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,
          "No actual initialized typed dispatcher endpoint exists.", original.Model.StableKey, false));
      return dispatcher.GetOriginalRequestSource(startup.OriginalAcquisition.EndpointId);
    });
  }

  private async Task<bool> InitializeSelectedRequestEndpointAsync(SelectedRequestEndpoint original)
  {
    await ValidateSelectedRequestAdmissionAsync(original).ConfigureAwait(false);
    original.RequestedEngine = CaptureOriginalEnginePreference(original.Model);
    var factoryStart = Signal();
    original.Factory = AcquireOriginalModelSource(() => _inferenceComposition!.CreateAfterPublicationAsync(factoryStart.Task,
      original.Model.ProviderId, original.Model, original.Admission, _registry, _configurations, _coordinator, _frames,
      _tools, _contextSource, _contextAuthority, this, _ownerStop.Token));
    factoryStart.SetResult();
    try { original.Lease = await original.Factory.ConfigureAwait(false); }
    catch (Exception cause) { ThrowObservedModelTask(cause, original.Factory); throw; }
    // An actual late acquired lease is retained before any current-use/seal checks.
    ApplyOriginalEnginePreference(original.Lease, original.RequestedEngine, original.Model);
    original.Runtime = InvokePhysical(() => new DulcheRuntime([original.Lease.Adapter], toolCoordinator: _toolCoordinator));
    lock (_sync) { RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested(); }
    original.Startup = InvokePhysical(() => original.Runtime.PrepareManagedProviderOriginal(original.Model.ProviderId, original.Model, _ownerStop.Token));
    InvokePhysical(() => { original.Startup.StartOriginal(); return true; });
    OperationResult<DulcheEndpoint> result;
    try { result = await original.Startup.OriginalStartup.ConfigureAwait(false); }
    catch (Exception cause) { ThrowObservedModelTask(cause, original.Startup.OriginalStartup); throw; }
    if (!result.Succeeded) throw new InferenceEngineException(result.Error!);
    await ValidateSelectedRequestAdmissionAsync(original).ConfigureAwait(false); return true;
  }

  private async Task ValidateSelectedRequestAdmissionAsync(SelectedRequestEndpoint original)
  {
    lock (_sync) { RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested(); }
    var admission = original.Admission;
    var lookup = AcquireOriginalModelSource(() => _coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission,
      callback => InvokePhysical(() => { callback(); return true; }), RetainOriginalModelSource, _ownerStop.Token));
    TaskRunAttemptAdmission? issued;
    try { issued = await lookup.ConfigureAwait(false); }
    catch (Exception cause) { ThrowObservedModelTask(cause, lookup); throw; }
    if (!ReferenceEquals(issued, admission)) throw new UnauthorizedAccessException("No SAME private issued request admission exists.");
    var currentTask = AcquireOriginalModelSource(() => _coordinator.GetAsync(admission.Snapshot.TaskId, _ownerStop.Token));
    TaskExecutionSnapshot? current;
    try { current = await currentTask.ConfigureAwait(false); }
    catch (Exception cause) { ThrowObservedModelTask(cause, currentTask); throw; }
    var route = admission.Lease.Candidate;
    if (current is null || current.ExecutionId != admission.Snapshot.ExecutionId || current.OwnerBinding != admission.Lease.Owner
      || current.Attempts.LastOrDefault() is not { } attempt || attempt.Id != admission.AttemptId
      || attempt.State is not (TaskRunAttemptState.Admitted or TaskRunAttemptState.Running)
      || route.UsesCloud || route.ProviderId != "llama-cpp" || original.Model.ProviderId != route.ProviderId
      || original.Model.ModelId != route.ModelId || original.Model.ArtifactRevision != route.ArtifactIdentity)
      throw new UnauthorizedAccessException("The actual current selected local request admission/model is unavailable.");
  }

  private async Task CloseSelectedRequestEndpointsAsync(List<Exception> failures)
  {
    SelectedRequestEndpoint[] endpoints; lock (_sync) endpoints = _selectedRequestEndpoints.ToArray();
    StartAvailableOriginalRetirements();
    foreach (var endpoint in endpoints)
      if (endpoint.Initialization is { } initialization) await Join(initialization, failures).ConfigureAwait(false);
    // Initialization may publish a genuine lease/runtime/startup after the first snapshot.
    StartAvailableOriginalRetirements();
    void StartAvailableOriginalRetirements()
    {
      foreach (var endpoint in endpoints)
      {
        if (endpoint.Stop is null && endpoint.Runtime is { } runtime && endpoint.Startup is { WasAcquired: true } startup)
          try { endpoint.Stop = InvokePhysical(() => runtime.JoinEndpointStopAsync(startup.OriginalAcquisition.EndpointId)); }
          catch (Exception cause) { Add(failures, cause); }
        // Actual close can release a held Start/Load. Acquire it BEFORE joining initialization.
        if (endpoint.Close is null && endpoint.Lease is { } lease)
          try { endpoint.Close = InvokePhysical(lease.CloseOriginalAsync); }
          catch (Exception cause) { Add(failures, cause); }
      }
    }
    foreach (var endpoint in endpoints)
    {
      if (endpoint.Stop is { } stop)
      {
        await Join(stop, failures).ConfigureAwait(false);
        if (stop.IsCompletedSuccessfully && !stop.Result.Succeeded) Add(failures, new ManagedDulcheRetirementException(stop.Result.Error!));
      }
    }
    foreach (var endpoint in endpoints) if (endpoint.Close is { } close) await Join(close, failures).ConfigureAwait(false);
  }

  private void DemandSelectedRequestIndependentClose()
  {
    foreach (var endpoint in _selectedRequestEndpoints)
    {
      endpoint.Lease?.DemandExternalOriginalJoin();
      if (endpoint.Runtime is { } runtime && endpoint.Startup is { WasAcquired: true } startup)
        runtime.RequireIndependentOriginalEndpointJoin(startup.OriginalAcquisition.EndpointId);
    }
  }
  private sealed class SelectedRequestCallerScope(ManagedDulcheRuntimeService owner) : IInferenceEngineOriginalSourceScope
  {
    public T InvokeOriginalFactory<T>(Func<T> callback) => owner.InvokePhysical(() => {
      lock (owner._sync) owner.RequireOpen(); return callback();
    });
    public T InvokeOriginalCleanup<T>(Func<T> callback) => owner.InvokePhysical(callback);
    public void RetainOriginalTask(Task sameActualTask) => owner.RetainOriginalModelSource(sameActualTask);
  }
  private sealed class SelectedRequestCall(object request, TaskRunAttemptAdmission admission, Guid action, string kind)
  { public object Request { get; } = request; public TaskRunAttemptAdmission Admission { get; } = admission;
   public Guid Action { get; } = action; public string Kind { get; } = kind; public Task? Driver; }
  private sealed class SelectedRequestEndpoint(TaskRunAttemptAdmission admission, ModelIdentity model)
  {
    public TaskRunAttemptAdmission Admission { get; } = admission; public ModelIdentity Model { get; } = model;
    public InferenceEngine RequestedEngine;
    public Task<bool>? Initialization; public Task<OriginalInferenceEngineLease>? Factory; public OriginalInferenceEngineLease? Lease;
    public DulcheRuntime? Runtime; public DulcheEndpointStartupOriginal? Startup;
    public Task<OperationResult<DulcheEndpoint>>? Stop; public Task? Close;
  }
}
