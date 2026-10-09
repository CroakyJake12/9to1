using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Runtime.Tests;

public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  public async Task Actual_managed_request_and_provider_cancellation_preserve_source_status_and_full_causes(int mode)
  {
    // The real initialized managed adapter, dispatcher and raw request source use the
    // maintained coordinator/frame harness. Observed fit and permission issuers are controlled.
    // This issues no protected installation, model, response or native production grant.
    await Control(async h =>
    {
      var initialized = new InitializedModelProvider(h.Provider);
      await WithRawTransportScope(h, async (source, caller) =>
      {
        var model = new ModelIdentity(h.Provider.Id, "model");
        var oce = new OperationCanceledException("actual provider Faulted OCE");
        var sibling = new IOException("actual provider fault sibling");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Task<string> providerTask;
        if (mode == 0) providerTask = Task.FromCanceled<string>(canceled.Token);
        else
        {
          var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
          if (mode == 1) failed.SetException([oce, sibling]); else failed.SetException(oce);
          providerTask = failed.Task;
        }
        h.Provider.CompleteFactory = (_, _) => providerTask;
        var acquisition = ManagedProviderDulcheAdapter.CreateAsync(h.Provider.Id,
          new SyntheticRegistry(initialized), new SyntheticConfigurations(h.Provider), h.Coordinator, h.ObservedFrames);
        ManagedProviderDulcheAdapter? adapter = null;
        InferenceEngineDispatcher? dispatcher = null;
        Task<string>? actual = null, frame = null; Task? close = null;
        var errors = new List<Exception>();
        try
        {
          adapter = await acquisition;
          var endpoint = h.Endpoint with { EndpointId = Guid.NewGuid().ToString("N"), Model = model };
          var lease = new ComposedCanceledLease(adapter, source);
          var requirements = new InferenceModelRequirements(model, "controlled artifact", "controlled architecture",
            "controlled family", "GGUF", "Q4", new HashSet<string> { "streaming" }, 0, 0, 128);
          var hardware = new InferenceHardware("controlled observation", "Linux", "x64", 4096, [], new HashSet<string>());
          var support = new InferenceEngineSupport(InferenceEngine.LlamaCpp, "controlled managed build", true, null,
            new HashSet<string> { "controlled architecture" }, new HashSet<string> { "controlled family" },
            new HashSet<string> { "GGUF" }, new HashSet<string> { "Q4" }, new HashSet<string> { "streaming" },
            new HashSet<string> { "Linux" }, new HashSet<string> { "x64" }, new HashSet<string>());
          dispatcher = new(h.Provider.Id, adapter.OriginalConfiguredTarget,
            [new(InferenceEngine.LlamaCpp, new ComposedCanceledFactory(lease))],
            new ComposedCanceledObservation(new(requirements, hardware, [support])));
          Assert.True((await dispatcher.StartAsync(endpoint, CancellationToken.None)).Succeeded);
          var selected = dispatcher.GetOriginalRequestSource(endpoint.EndpointId);
          var (action, context) = await RawTransportResponseControl(h);
          var wire = new OllamaChatRequest("model", [new("user", "same original canceled input")], EffortLevel.Medium)
            { ExecutionContext = context };
          frame = h.Frames.StartOriginalFrameAsync(h.Admission, token =>
          {
            actual = selected.CompleteOriginalAsync(wire, h.Admission, action, caller, token);
            return actual;
          }, CancellationToken.None);
          var failure = await Record.ExceptionAsync(() => frame);
          ObserveComposedExpected(h, failure, actual, frame, providerTask, caller.Tasks, oce, sibling);
          Assert.NotNull(actual); Assert.NotNull(failure);
          Assert.Same(providerTask, h.Provider.OriginalComplete);
          Assert.Contains(caller.Tasks, task => ReferenceEquals(task, providerTask));
          Assert.Equal(1, h.Provider.CompleteCalls); Assert.Equal(0, h.Tools.BindCalls);
          if (mode == 0)
          {
            Assert.True(providerTask.IsCanceled); Assert.True(actual!.IsCanceled); Assert.False(actual.IsFaulted);
            var grouped = Assert.IsType<AggregateException>(failure!.InnerException);
            var canceledCauses = ComposedCauseGraph(grouped).OfType<TaskCanceledException>().ToArray();
            Assert.Contains(canceledCauses, cause => ReferenceEquals(cause.Task, providerTask));
            Assert.True(grouped.InnerExceptions.Count >= 2);
            Assert.Contains(caller.Tasks, task => task.IsCanceled && !ReferenceEquals(task, providerTask));
          }
          else
          {
            Assert.True(providerTask.IsFaulted); Assert.True(actual!.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(ComposedCauseGraph(actual.Exception!), cause => ReferenceEquals(cause, oce));
            if (mode == 1) Assert.Contains(ComposedCauseGraph(actual.Exception!), cause => ReferenceEquals(cause, sibling));
          }
          close = dispatcher.DisposeAsync().AsTask();
          var closingFailure = await Record.ExceptionAsync(() => close);
          ObserveComposedExpected(h, closingFailure, actual, frame, providerTask, caller.Tasks, oce, sibling);
          Assert.NotNull(closingFailure); Assert.False(close.IsCompletedSuccessfully);
          if (mode != 0)
          {
            Assert.Contains(ComposedCauseGraph(closingFailure!), cause => ReferenceEquals(cause, oce));
            if (mode == 1) Assert.Contains(ComposedCauseGraph(closingFailure!), cause => ReferenceEquals(cause, sibling));
          }
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
          // Every exact acquired original is joined even if an assertion or publication fails.
          if (adapter is null && acquisition.IsCompletedSuccessfully) adapter = acquisition.Result;
          if (dispatcher is not null)
            try { close ??= dispatcher.DisposeAsync().AsTask(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
          Task? adapterClose = null;
          if (adapter is not null)
            try { adapterClose = adapter.DisposeAsync().AsTask(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
          foreach (var original in new Task?[] { frame, actual, providerTask, acquisition, close, adapterClose }
            .Concat(caller.Tasks).Where(task => task is not null).Cast<Task>().Distinct())
            await JoinUnexpected(h, errors, original);
        }
        ThrowInitializedModelControlErrors(errors);
      }, initialized: initialized);
    });
  }

  [Fact]
  public async Task Natural_managed_stream_end_with_canceled_actual_dispose_remains_faulted_cleanup()
  {
    await Control(async h =>
    {
      var initialized = new InitializedModelProvider(h.Provider);
      await WithRawTransportScope(h, async (source, caller) =>
      {
        var model = new ModelIdentity(h.Provider.Id, "model");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var raw = new OriginalStream(0) { DisposeOriginal = Task.FromCanceled(canceled.Token) };
        h.Provider.StreamFactory = (_, _) => raw;
        var acquisition = ManagedProviderDulcheAdapter.CreateAsync(h.Provider.Id,
          new SyntheticRegistry(initialized), new SyntheticConfigurations(h.Provider), h.Coordinator, h.ObservedFrames);
        ManagedProviderDulcheAdapter? adapter = null;
        InferenceEngineDispatcher? dispatcher = null;
        IAsyncEnumerator<string>? reader = null;
        Task<bool>? move = null, frame = null; Task? close = null, disposal = null;
        var errors = new List<Exception>();
        var unmatchedBody = new InvalidOperationException("no body failure is expected in this control");
        var unmatchedSibling = new IOException("no body sibling is expected in this control");
        try
        {
          adapter = await acquisition;
          var endpoint = h.Endpoint with { EndpointId = Guid.NewGuid().ToString("N"), Model = model };
          var lease = new ComposedCanceledLease(adapter, source);
          var requirements = new InferenceModelRequirements(model, "controlled artifact", "controlled architecture",
            "controlled family", "GGUF", "Q4", new HashSet<string> { "streaming" }, 0, 0, 128);
          var hardware = new InferenceHardware("controlled observation", "Linux", "x64", 4096, [], new HashSet<string>());
          var support = new InferenceEngineSupport(InferenceEngine.LlamaCpp, "controlled managed build", true, null,
            new HashSet<string> { "controlled architecture" }, new HashSet<string> { "controlled family" },
            new HashSet<string> { "GGUF" }, new HashSet<string> { "Q4" }, new HashSet<string> { "streaming" },
            new HashSet<string> { "Linux" }, new HashSet<string> { "x64" }, new HashSet<string>());
          dispatcher = new(h.Provider.Id, adapter.OriginalConfiguredTarget,
            [new(InferenceEngine.LlamaCpp, new ComposedCanceledFactory(lease))],
            new ComposedCanceledObservation(new(requirements, hardware, [support])));
          Assert.True((await dispatcher.StartAsync(endpoint, CancellationToken.None)).Succeeded);
          var selected = dispatcher.GetOriginalRequestSource(endpoint.EndpointId);
          var (action, context) = await RawTransportResponseControl(h);
          var wire = new OllamaChatRequest("model", [new("user", "same original naturally terminal stream")], EffortLevel.Medium)
            { ExecutionContext = context };
          frame = h.Frames.StartOriginalFrameAsync(h.Admission, token =>
          {
            reader = selected.StreamOriginalAsync(wire, h.Admission, action, caller, token).GetAsyncEnumerator(token);
            move = reader.MoveNextAsync().AsTask(); return move;
          }, CancellationToken.None);
          var failure = await Record.ExceptionAsync(() => frame);
          ObserveComposedExpected(h, failure, move, frame, raw.DisposeOriginal,
            caller.Tasks.Concat(raw.Moves), unmatchedBody, unmatchedSibling);
          Assert.NotNull(move); Assert.NotNull(failure);
          var originalMove = Assert.Single(raw.Moves);
          Assert.True(originalMove.IsCompletedSuccessfully); Assert.False(originalMove.Result);
          Assert.True(raw.DisposeOriginal.IsCanceled);
          Assert.Contains(caller.Tasks, task => ReferenceEquals(task, raw.DisposeOriginal));
          Assert.True(move!.IsFaulted); Assert.False(move.IsCanceled);
          Assert.Contains(ComposedCauseGraph(move.Exception!).OfType<TaskCanceledException>(),
            cause => ReferenceEquals(cause.Task, raw.DisposeOriginal));
          Assert.Equal(1, raw.Disposes); Assert.Equal(0, h.Tools.BindCalls);
          close = dispatcher.DisposeAsync().AsTask();
          var closingFailure = await Record.ExceptionAsync(() => close);
          ObserveComposedExpected(h, closingFailure, move, frame, raw.DisposeOriginal,
            caller.Tasks.Concat(raw.Moves), unmatchedBody, unmatchedSibling);
          Assert.NotNull(closingFailure); Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
          Assert.Contains(ComposedCauseGraph(closingFailure!).OfType<TaskCanceledException>(),
            cause => ReferenceEquals(cause.Task, raw.DisposeOriginal));
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
          // Every actual body/cleanup original is joined independently, including a reader
          // acquired before a failing assertion. Unrelated cleanup failures remain unexpected.
          if (reader is not null)
            try { disposal = reader.DisposeAsync().AsTask(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
          if (adapter is null && acquisition.IsCompletedSuccessfully) adapter = acquisition.Result;
          if (dispatcher is not null)
            try { close ??= dispatcher.DisposeAsync().AsTask(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
          Task? adapterClose = null;
          if (adapter is not null)
            try { adapterClose = adapter.DisposeAsync().AsTask(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
          foreach (var original in new Task?[] { frame, move, raw.DisposeOriginal, acquisition, disposal, close, adapterClose }
            .Concat(caller.Tasks).Concat(raw.Moves).Where(task => task is not null).Cast<Task>().Distinct())
            await JoinUnexpected(h, errors, original);
        }
        ThrowInitializedModelControlErrors(errors);
      }, initialized: initialized);
    }, required: [ToolCapability.Text, ToolCapability.Streaming]);
  }

  private static IEnumerable<Exception> ComposedCauseGraph(Exception cause)
  {
    yield return cause;
    if (cause is AggregateException group)
      foreach (var child in group.InnerExceptions) foreach (var item in ComposedCauseGraph(child)) yield return item;
    else if (cause.InnerException is { } inner)
      foreach (var item in ComposedCauseGraph(inner)) yield return item;
  }
  private static void ObserveComposedExpected(Harness h, Exception? cause, Task? actual, Task? frame,
    Task providerTask, IEnumerable<Task> originals, Exception oce, Exception sibling)
  {
    var all = originals.Concat(new Task?[] { actual, frame, providerTask }.Where(task => task is not null).Cast<Task>()).ToArray();
    foreach (var original in all)
      if (original.IsCanceled) h.ExpectedCanceledOriginals.Add(original);
    bool Known(Exception error) => ReferenceEquals(error, oce) || ReferenceEquals(error, sibling)
      || error is TaskCanceledException { Task: { IsCanceled: true } task } && h.ExpectedCanceledOriginals.Contains(task)
      || error is AggregateException group && group.InnerExceptions.Count > 0 && group.InnerExceptions.All(Known)
      || error is OperationCanceledException { InnerException: { } inner } && Known(inner);
    if (cause is not null && Known(cause)) h.Expect(cause);
    foreach (var original in all)
    {
      if (original.Exception is { } group)
        foreach (var child in group.InnerExceptions) if (Known(child)) h.Expect(child);
    }
  }
  private sealed class ComposedCanceledFactory(ComposedCanceledLease lease) : IInferenceEngineAdapterFactory
  {
    public Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity model,
      IInferenceEngineOriginalSourceScope scope, CancellationToken token) => Task.FromResult<OriginalInferenceEngineLease>(lease);
  }
  private sealed class ComposedCanceledObservation(InferenceRuntimeObservation observation) : IInferenceRuntimeObservationSource
  {
    public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,
      IInferenceEngineOriginalSourceScope scope, CancellationToken token) => Task.FromResult(observation);
  }
  private sealed class ComposedCanceledLease(ManagedProviderDulcheAdapter adapter,
    IOriginalInferenceEngineRequestSource source) : OriginalInferenceEngineLease, IOriginalInferenceEngineRequestLease
  {
    private Task? _close;
    public override IDulcheOriginalProviderAdapter Adapter => adapter;
    public IOriginalInferenceEngineRequestSource OriginalRequestSource => source;
    public override void DemandExternalOriginalJoin() => adapter.RequireIndependentOriginalProviderJoin();
    public override Task CloseOriginalAsync()
    { DemandExternalOriginalJoin(); return _close ??= adapter.DisposeAsync().AsTask(); }
  }
}
