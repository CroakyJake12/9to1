using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;

namespace Dulche.Runtime;

/// <summary>Trusted factory-held SAME physical provider. Caller routing/frame/tool ownership
/// remains outside this source. Actual model observation and current private admission/action
/// are mandatory; no reconstructed wire, registry reread, independent tool loop or grant.</summary>
internal sealed class ManagedInferenceEngineRequestSource(IModelProvider provider, ModelIdentity model,
  TaskExecutionCoordinator coordinator, int? nativeContextLimit = null) : IOriginalInferenceEngineRequestSource
{
  public async Task<string> CompleteOriginalAsync(OllamaChatRequest sameRequest, TaskRunAttemptAdmission sameAdmission,
    Guid acknowledgedResponseAction, IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken)
  {
    await ValidateAsync(sameRequest.Model, sameRequest.ExecutionContext, sameAdmission, acknowledgedResponseAction,
      originalScope, [ToolCapability.Text], sameRequest.Messages.Any(message => message.Images?.Count > 0), cancellationToken).ConfigureAwait(false);
    DemandChatFeatures(sameRequest);
    var raw = Own(originalScope, () => { DemandOriginalCurrentLease(sameAdmission); return provider.CompleteAsync(sameRequest, cancellationToken); });
    return await AwaitOriginal(raw).ConfigureAwait(false);
  }
  public async Task<OllamaToolResponse> ToolsOriginalAsync(OllamaToolRequest sameRequest, TaskRunAttemptAdmission sameAdmission,
    Guid acknowledgedResponseAction, IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken)
  {
    await ValidateAsync(sameRequest.Model, sameRequest.ExecutionContext, sameAdmission, acknowledgedResponseAction,
      originalScope, [ToolCapability.Text, ToolCapability.Tools], sameRequest.Messages.Any(message => message.Images?.Count > 0), cancellationToken).ConfigureAwait(false);
    var raw = Own(originalScope, () => { DemandOriginalCurrentLease(sameAdmission); return provider.ChatWithToolsAsync(sameRequest, cancellationToken); });
    return await AwaitOriginal(raw).ConfigureAwait(false);
  }
  public async IAsyncEnumerable<string> StreamOriginalAsync(OllamaChatRequest sameRequest, TaskRunAttemptAdmission sameAdmission,
    Guid acknowledgedResponseAction, IInferenceEngineOriginalSourceScope originalScope,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    await ValidateAsync(sameRequest.Model, sameRequest.ExecutionContext, sameAdmission, acknowledgedResponseAction,
      originalScope, [ToolCapability.Text, ToolCapability.Streaming], sameRequest.Messages.Any(message => message.Images?.Count > 0), cancellationToken).ConfigureAwait(false);
    DemandChatFeatures(sameRequest);
    IAsyncEnumerator<string>? reader = null; Task<bool>? move = null; Task? cleanup = null;
    Exception? bodyFailure = null; var errors = new List<Exception>();
    try
    {
      try { Capture(originalScope, () => { DemandOriginalCurrentLease(sameAdmission); return reader = provider.StreamChatAsync(sameRequest, cancellationToken).GetAsyncEnumerator(cancellationToken); }); }
      catch (Exception cause) { bodyFailure = cause is OperationCanceledException
        ? new AggregateException("The actual original stream factory faulted synchronously.", cause) : cause; }
      for (var count = 0; bodyFailure is null && reader is not null; count++)
      {
        if (count >= 4096) { bodyFailure = new InvalidOperationException("Original raw stream move custody is full."); break; }
        try { move = Own(originalScope, () => { DemandOriginalCurrentLease(sameAdmission); return reader.MoveNextAsync().AsTask(); }); }
        catch (Exception cause) { bodyFailure = cause; break; }
        bool available;
        try { available = await AwaitOriginal(move).ConfigureAwait(false); }
        catch (Exception cause) { bodyFailure = cause; break; }
        if (!available) break;
        string current;
        try { current = Capture(originalScope, () => { DemandOriginalCurrentLease(sameAdmission); return reader.Current; }); }
        catch (Exception cause) { bodyFailure = cause; break; }
        yield return current;
      }
    }
    finally
    {
      if (bodyFailure is not null) Add(errors, bodyFailure);
      if (reader is not null)
      {
        try { Capture(originalScope, () => {
          cleanup = reader.DisposeAsync().AsTask(); originalScope.RetainOriginalTask(cleanup); return cleanup;
        }, cleanup: true); }
        catch (Exception cause) { Add(errors, cause is OperationCanceledException
          ? new AggregateException("The actual original stream dispose factory faulted synchronously.", cause) : cause); }
        if (cleanup is not null)
          try { await AwaitOriginal(cleanup).ConfigureAwait(false); } catch (Exception cause) { Add(errors, cause); }
      }
      Throw(errors);
    }
  }
  private void DemandOriginalCurrentLease(TaskRunAttemptAdmission admission)
  {
    var live = admission.Lease as ITaskRunOriginalInferenceLeaseCurrentnessSource
      ?? throw Unsupported("No SAME original lease pure currentness source exists.");
    live.DemandOriginalInferenceWithinSource(admission);
  }
  private void DemandChatFeatures(OllamaChatRequest request)
  {
    if (request.EnableTools) throw Unsupported("Structured tools require the actual typed tool-response path.");
    if (nativeContextLimit is { } limit && (request.Effort != EffortLevel.Medium
      || request.Options is { } options && (options.ContextLimit != limit || options.ActionLimit != 24)))
      throw Unsupported("The actual native worker has no faithful nondefault effort/context/action-budget mapping.");
  }
  private async Task ValidateAsync(string wireModel, ProviderExecutionContext? context, TaskRunAttemptAdmission admission,
    Guid action, IInferenceEngineOriginalSourceScope scope, IReadOnlyList<ToolCapability> required, bool images, CancellationToken token)
  {
    ArgumentNullException.ThrowIfNull(admission); ArgumentNullException.ThrowIfNull(scope);
    if (nativeContextLimit is not null && (required.Contains(ToolCapability.Tools) || images))
      throw Unsupported("The actual native Strata transport supports text/stream only.");
    var lookup = Own(scope, () => coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission,
      callback => scope.InvokeOriginalFactory(() => { callback(); return true; }), scope.RetainOriginalTask, token));
    if (!ReferenceEquals(await AwaitOriginal(lookup).ConfigureAwait(false), admission))
      throw new UnauthorizedAccessException("No SAME privately issued original request admission exists.");
    var leaseSource = admission.Lease as ITaskRunOriginalInferenceLeaseSource
      ?? throw Unsupported("No SAME original lease scoped permission-revalidation source exists.");
    await RevalidateAsync().ConfigureAwait(false);
    await DemandCurrentAsync().ConfigureAwait(false);
    var loadedSource = provider as IOriginalInferenceEngineModelSource
      ?? throw Unsupported("No actual initialized-model observation source exists.");
    var loaded = Own(scope, () => loadedSource.ObserveOriginalInitializedModelAsync(model, token));
    var observed = await AwaitOriginal(loaded).ConfigureAwait(false);
    if (!observed.Succeeded) throw new InferenceEngineException(observed.Error!);
    await RevalidateAsync().ConfigureAwait(false);
    await DemandCurrentAsync().ConfigureAwait(false);
    var catalogue = Own(scope, () => provider.GetModelsAsync(token));
    var rows = await AwaitOriginal(catalogue).ConfigureAwait(false);
    var selected = rows.SingleOrDefault(row => row.ProviderId == model.ProviderId && row.Name == model.ModelId && row.IsLocal);
    if (selected is null || !required.All(selected.Supports) || images && !selected.Supports(ToolCapability.Vision))
      throw Unsupported("No actual same-model catalogue proof covers this typed request.");
    await DemandCurrentAsync().ConfigureAwait(false);
    await RevalidateAsync().ConfigureAwait(false);
    async Task RevalidateAsync()
    {
      var raw = Own(scope, () => leaseSource.RevalidateOriginalInferenceWithinSourceAsync(admission,
        callback => scope.InvokeOriginalFactory(() => { callback(); return true; }), scope.RetainOriginalTask, token));
      await AwaitOriginal(raw).ConfigureAwait(false);
    }
    async Task DemandCurrentAsync()
    {
      var read = Own(scope, () => coordinator.GetAsync(admission.Snapshot.TaskId, token));
      var current = await AwaitOriginal(read).ConfigureAwait(false); var route = admission.Lease.Candidate;
      if (context is null || current is null || current.ExecutionId != admission.Snapshot.ExecutionId || current.OwnerBinding != admission.Lease.Owner
        || current.Attempts.LastOrDefault() is not { } attempt || attempt.Id != admission.AttemptId
        || attempt.State is not (TaskRunAttemptState.Admitted or TaskRunAttemptState.Running)
        || context.TaskId != current.TaskId || context.ContextId != current.ContextId || context.ExecutionId != current.ExecutionId
        || context.AttemptId != admission.AttemptId || context.PersistenceRevision != current.PersistenceRevision || context.ActionId != action
        || action == Guid.Empty || !current.Plan.Any(node => node.ActionId == action && node.State is TaskPlanNodeState.Pending or TaskPlanNodeState.Running)
        || route.UsesCloud || route.ProviderId != model.ProviderId || route.ModelId != model.ModelId || route.ArtifactIdentity != model.ArtifactRevision
        || wireModel != model.ModelId || !required.All(capability => route.RequiredCapabilities.Contains(capability.ToString(), StringComparer.Ordinal)))
        throw new UnauthorizedAccessException("The actual current acknowledged response/model/wire is unavailable.");
    }
  }
  private InferenceEngineException Unsupported(string reason) => new(new(DulcheErrorCode.UnsupportedCapability, reason, model.StableKey, false));
  private static Task<T> Own<T>(IInferenceEngineOriginalSourceScope scope, Func<Task<T>> factory)
  {
    try { return Capture(scope, () => {
      var raw = factory() ?? throw new InvalidOperationException("No actual original raw Task was returned.");
      scope.RetainOriginalTask(raw); return raw;
    }); }
    catch (OperationCanceledException cause) { throw new AggregateException("The actual original raw factory faulted synchronously.", cause); }
  }
  private static Task Own(IInferenceEngineOriginalSourceScope scope, Func<Task> factory)
  {
    try { return Capture(scope, () => {
      var raw = factory() ?? throw new InvalidOperationException("No actual original raw Task was returned.");
      scope.RetainOriginalTask(raw); return raw;
    }); }
    catch (OperationCanceledException cause) { throw new AggregateException("The actual original raw factory faulted synchronously.", cause); }
  }
  private static T Capture<T>(IInferenceEngineOriginalSourceScope scope, Func<T> callback, bool cleanup = false)
  {
    T captured = default!; var invoked = false;
    T Run() { if (invoked) throw new InvalidOperationException("The original factory callback was invoked twice.");
      invoked = true; captured = callback(); return captured; }
    if (cleanup) scope.InvokeOriginalCleanup(Run); else scope.InvokeOriginalFactory(Run);
    if (!invoked) throw new InvalidOperationException("The original factory callback was not admitted.");
    return captured; // A caller-supplied scope return never substitutes the actual callback product.
  }
  private static async Task<T> AwaitOriginal<T>(Task<T> raw)
  {
    try { return await raw.ConfigureAwait(false); }
    catch (Exception cause)
    {
      if (raw.IsFaulted && raw.Exception is { } group)
      {
        if (group.InnerExceptions.Count == 1 && group.InnerExceptions[0] is not OperationCanceledException)
          ExceptionDispatchInfo.Capture(group.InnerExceptions[0]).Throw();
        throw new AggregateException("The actual original raw Task faulted.", group.InnerExceptions);
      }
      ExceptionDispatchInfo.Capture(cause).Throw(); throw;
    }
  }
  private static async Task AwaitOriginal(Task raw)
  {
    try { await raw.ConfigureAwait(false); }
    catch (Exception cause)
    {
      if (raw.IsFaulted && raw.Exception is { } group)
      {
        if (group.InnerExceptions.Count == 1 && group.InnerExceptions[0] is not OperationCanceledException)
          ExceptionDispatchInfo.Capture(group.InnerExceptions[0]).Throw();
        throw new AggregateException("The actual original raw Task faulted.", group.InnerExceptions);
      }
      ExceptionDispatchInfo.Capture(cause).Throw();
    }
  }
  private static void Add(List<Exception> errors, Exception cause) { if (!errors.Any(actual => ReferenceEquals(actual, cause))) errors.Add(cause); }
  private static void Throw(List<Exception> errors)
  { if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw(); if (errors.Count > 1) throw new AggregateException(errors); }
}
