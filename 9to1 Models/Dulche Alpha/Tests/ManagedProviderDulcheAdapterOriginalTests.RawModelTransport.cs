using System.Reflection;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using System.Collections;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Actual old coordinator/frame originals with controlled model observation/raw provider.
/// These controls prove typed forwarding/cause custody, not native loading or a response issuer grant.</summary>
public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
  [Fact]
  public async Task Typed_raw_completion_preserves_same_full_wire_and_actual_provider_task()
  {
    await Control(h => WithRawTransportScope(h, async (source, scope) =>
    {
      var (action, context) = await RawTransportResponseControl(h);
      var wire = new OllamaChatRequest("model", [new("system", "kept role"), new("user", "kept body")],
        EffortLevel.High, "separate original system", Options: new(0.31, 128, 9)) { ExecutionContext = context };
      OllamaChatRequest? actualWire = null;
      h.Provider.CompleteFactory = (request, token) => { actualWire = request; return Task.FromResult("raw original result"); };
      var frame = h.Frames.StartOriginalFrameAsync(h.Admission,
        token => source.CompleteOriginalAsync(wire, h.Admission, action, scope, token), CancellationToken.None);
      scope.Tasks.Add(frame);
      Assert.Equal("raw original result", await frame);
      Assert.Same(wire, actualWire);
      Assert.Contains(scope.Tasks, task => ReferenceEquals(task, h.Provider.OriginalComplete));
      Assert.Equal(1, h.Provider.CompleteCalls);
      Assert.Equal(0, h.Tools.BindCalls);
    }));
  }

  [Fact]
  public async Task Typed_raw_tool_response_preserves_data_without_internal_tool_binding()
  {
    await Control(h => WithRawTransportScope(h, async (source, scope) =>
    {
      var (action, context) = await RawTransportResponseControl(h);
      var wire = new OllamaToolRequest("model", [new("user", "exact original tool turn")],
        [new("fixture.echo", "exact definition", new Dictionary<string, object>(), [])], EffortLevel.Medium,
        "original separate system", new(0.2, 512, 7)) { ExecutionContext = context };
      var returned = new OllamaToolResponse("exact original tool data", []);
      OllamaToolRequest? actualWire = null;
      h.Provider.ToolFactory = (request, token) => { actualWire = request; return Task.FromResult(returned); };
      var frame = h.Frames.StartOriginalFrameAsync(h.Admission,
        token => source.ToolsOriginalAsync(wire, h.Admission, action, scope, token), CancellationToken.None);
      scope.Tasks.Add(frame);
      Assert.Same(returned, await frame);
      Assert.Same(wire, actualWire);
      Assert.Equal(1, h.Provider.ToolCalls);
      Assert.Equal(0, h.Tools.BindCalls);
    }), required: [ToolCapability.Text, ToolCapability.Tools]);
  }

  [Fact]
  public async Task Actual_native_transport_refuses_tools_before_any_model_or_raw_callback()
  {
    await Control(h => WithRawTransportScope(h, async (source, scope) =>
    {
      var (action, context) = await RawTransportResponseControl(h);
      var wire = new OllamaToolRequest("model", [new("user", "tools are not text")], [], EffortLevel.Medium)
        { ExecutionContext = context };
      var frame = h.Frames.StartOriginalFrameAsync(h.Admission,
        token => source.ToolsOriginalAsync(wire, h.Admission, action, scope, token), CancellationToken.None);
      scope.Tasks.Add(frame);
      var cause = await Record.ExceptionAsync(() => frame);
      if (cause is not null) h.Expect(cause);
      var refusal = Assert.IsType<InferenceEngineException>(cause);
      Assert.Equal(DulcheErrorCode.UnsupportedCapability, refusal.Error.Code);
      Assert.Equal(0, h.Provider.CatalogueCalls);
      Assert.Equal(0, h.Provider.ToolCalls);
      Assert.Equal(0, h.Tools.BindCalls);
    }, nativeContextLimit: 128), required: [ToolCapability.Text, ToolCapability.Tools]);
  }

  [Fact]
  public async Task Post_acquisition_scope_fault_still_disposes_and_joins_same_raw_reader()
  {
    await Control(h => WithRawTransportScope(h, async (source, scope) =>
    {
      var (action, context) = await RawTransportResponseControl(h);
      var released = Signal(); var raw = new OriginalStream(0) { DisposeOriginal = released.Task };
      var primary = new InvalidOperationException("actual post-reader scope failure");
      var cleanup = new InvalidOperationException("actual post-dispose scope failure");
      h.Provider.StreamFactory = (_, _) => raw;
      scope.AfterFactory = value => { if (ReferenceEquals(value, raw)) throw primary; };
      scope.AfterCleanup = value => { if (ReferenceEquals(value, raw.DisposeOriginal)) throw cleanup; };
      IAsyncEnumerator<string>? reader = null; Task<bool>? frame = null; Task? disposal = null;
      var errors = new List<Exception>();
      try
      {
        var wire = new OllamaChatRequest("model", [new("user", "kept raw stream")], EffortLevel.Medium)
          { ExecutionContext = context };
        reader = source.StreamOriginalAsync(wire, h.Admission, action, scope, CancellationToken.None).GetAsyncEnumerator(CancellationToken.None);
        frame = h.Frames.StartOriginalFrameAsync(h.Admission, _ => reader.MoveNextAsync().AsTask(), CancellationToken.None);
        scope.Tasks.Add(frame);
        await raw.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(frame.IsCompleted);
        released.TrySetResult();
        var failure = await Record.ExceptionAsync(() => frame);
        if (failure is not null) h.Expect(failure);
        Assert.NotNull(failure);
        Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, primary));
        Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, cleanup));
        Assert.Contains(scope.Tasks, task => ReferenceEquals(task, raw.DisposeOriginal));
        Assert.Equal(1, raw.Disposes);
        Assert.Empty(raw.Moves);
      }
      catch (Exception cause) { errors.Add(cause); }
      finally
      {
        released.TrySetResult();
        if (frame is not null) await JoinUnexpected(h, errors, frame);
        if (reader is not null)
          try { disposal = reader.DisposeAsync().AsTask(); scope.Tasks.Add(disposal); }
          catch (Exception cause) { AddUnexpected(h, errors, cause); }
        if (disposal is not null) await JoinUnexpected(h, errors, disposal);
      }
      ThrowInitializedModelControlErrors(errors);
    }), required: [ToolCapability.Text, ToolCapability.Streaming]);
  }

  [Fact]
  public async Task Selected_owner_close_releases_held_actual_initialization_before_join()
  {
    // Reflection enrolls a controlled producer/lease only; it issues no response, model-use,
    // installed artifact or native grant. Runtime startup/stop and service close are genuine.
    await Control(async h =>
    {
      var service = new ManagedDulcheRuntimeService(new SyntheticRegistry(h.Provider), new SyntheticConfigurations(h.Provider), h.Coordinator, h.Frames);
      var runtime = new DulcheRuntime([h.Adapter]); var lease = new ControlledSelectedCloseLease(h.Adapter);
      DulcheEndpointStartupOriginal? startup = null; Task<bool>? initialization = null; Task? close = null;
      var entered = Signal(); var errors = new List<Exception>();
      try
      {
        startup = runtime.PrepareManagedProviderOriginal(h.Provider.Id, CancellationToken.None);
        var actualStartup = startup.OriginalStartup;
        initialization = HoldOriginalInitialization();
        var type = typeof(ManagedDulcheRuntimeService).GetNestedType("SelectedRequestEndpoint", BindingFlags.NonPublic)!;
        var endpoint = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
          null, [h.Admission, new ModelIdentity(h.Provider.Id, "model")], null)!;
        foreach (var (name, value) in new (string, object)[] { ("Lease", lease), ("Runtime", runtime), ("Startup", startup), ("Initialization", initialization) })
          type.GetField(name, BindingFlags.Public | BindingFlags.Instance)!.SetValue(endpoint, value);
        ((IList)typeof(ManagedDulcheRuntimeService).GetField("_selectedRequestEndpoints", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!).Add(endpoint);
        ((IList)typeof(ManagedDulcheRuntimeService).GetField("_originalWork", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!).Add(initialization);
        startup.StartOriginal();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(initialization.IsCompleted); Assert.Equal(0, lease.Closes);
        close = service.CloseAndDrainAsync();
        await close.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(initialization.IsCompletedSuccessfully); Assert.True(await initialization);
        Assert.Equal(1, lease.Closes); Assert.True(actualStartup.IsCompletedSuccessfully);
        async Task<bool> HoldOriginalInitialization()
        {
          var result = await actualStartup;
          if (!result.Succeeded) throw new InferenceEngineException(result.Error!);
          entered.TrySetResult(); await lease.Released.Task; return true;
        }
      }
      catch (Exception cause) { errors.Add(cause); }
      finally
      {
        // Independent unexpected cleanup must release this actual controlled producer even on a failed oracle.
        Task? leaseClose = null; Task<OperationResult<DulcheEndpoint>>? stop = null;
        try { leaseClose = lease.CloseOriginalAsync(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
        if (startup is { WasAcquired: true })
          try { stop = runtime.JoinEndpointStopAsync(startup.OriginalAcquisition.EndpointId); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
        try { close ??= service.CloseAndDrainAsync(); } catch (Exception cause) { AddUnexpected(h, errors, cause); }
        if (initialization is not null) await JoinUnexpected(h, errors, initialization);
        if (leaseClose is not null) await JoinUnexpected(h, errors, leaseClose);
        if (stop is not null)
        {
          await JoinUnexpected(h, errors, stop);
          if (stop.IsCompletedSuccessfully && !stop.Result.Succeeded) errors.Add(new InferenceEngineException(stop.Result.Error!));
        }
        if (close is not null) await JoinUnexpected(h, errors, close);
        if (startup is not null) await JoinUnexpected(h, errors, startup.OriginalStartup);
      }
      ThrowInitializedModelControlErrors(errors);
    });
  }
  [Fact]
  public async Task Held_initialized_model_cannot_dispatch_after_same_lease_retirement()
  {
    await Control(async h =>
    {
      var released = new TaskCompletionSource<OperationResult<Unit>>(TaskCreationOptions.RunContinuationsAsynchronously);
      var entered = Signal(); var initialized = new InitializedModelProvider(h.Provider);
      initialized.Factory = (_, _) => released.Task; initialized.ObservationCaptured = () => entered.TrySetResult();
      await WithRawTransportScope(h, async (source, scope) =>
      {
        var (action, context) = await RawTransportResponseControl(h);
        var wire = new OllamaChatRequest("model", [new("user", "same acknowledged request")], EffortLevel.Medium)
          { ExecutionContext = context };
        Task<string>? frame = null; var errors = new List<Exception>();
        try
        {
          frame = h.Frames.StartOriginalFrameAsync(h.Admission,
            token => source.CompleteOriginalAsync(wire, h.Admission, action, scope, token), CancellationToken.None);
          scope.Tasks.Add(frame);
          await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
          Assert.False(frame.IsCompleted);
          await h.Authority.Lease!.DisposeAsync();
          released.TrySetResult(OperationResult<Unit>.Success(Unit.Value));
          var failure = await Record.ExceptionAsync(() => frame);
          if (failure is not null) h.Expect(failure);
          Assert.IsType<ObjectDisposedException>(failure);
          Assert.Contains(scope.Tasks, actual => ReferenceEquals(actual, released.Task));
          Assert.True(h.Authority.Lease!.ScopedRevalidations >= 2);
          Assert.Equal(0, h.Provider.CompleteCalls);
          Assert.Equal(0, h.Provider.StreamCalls);
          Assert.Equal(0, h.Provider.ToolCalls);
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
          released.TrySetResult(OperationResult<Unit>.Success(Unit.Value));
          if (frame is not null) await JoinUnexpected(h, errors, frame);
        }
        ThrowInitializedModelControlErrors(errors);
      }, initialized: initialized);
    });
  }
  [Fact]
  public async Task Held_final_row_cannot_dispatch_after_same_lease_retirement()
  {
    await Control(h => WithRawTransportScope(h, async (source, scope) =>
    {
      var (action, context) = await RawTransportResponseControl(h);
      var current = await h.Coordinator.GetAsync(h.Snapshot.TaskId, CancellationToken.None);
      Assert.NotNull(current);
      var released = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
      var entered = Signal(); var catalogueBefore = h.Provider.CatalogueCalls;
      var wire = new OllamaChatRequest("model", [new("user", "same final row")], EffortLevel.Medium)
        { ExecutionContext = context };
      Task<string>? frame = null; var errors = new List<Exception>();
      h.Repository.OriginalRead = (_, _) =>
      {
        if (h.Provider.CatalogueCalls <= catalogueBefore) return Task.FromResult<TaskExecutionSnapshot?>(current);
        entered.TrySetResult(); return released.Task;
      };
      try
      {
        frame = h.Frames.StartOriginalFrameAsync(h.Admission,
          token => source.CompleteOriginalAsync(wire, h.Admission, action, scope, token), CancellationToken.None);
        scope.Tasks.Add(frame);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(frame.IsCompleted);
        await h.Authority.Lease!.DisposeAsync();
        released.TrySetResult(current);
        var failure = await Record.ExceptionAsync(() => frame);
        if (failure is not null) h.Expect(failure);
        Assert.IsType<ObjectDisposedException>(failure);
        Assert.Contains(scope.Tasks, actual => ReferenceEquals(actual, released.Task));
        Assert.Equal(0, h.Provider.CompleteCalls);
        Assert.Equal(0, h.Provider.StreamCalls);
        Assert.Equal(0, h.Provider.ToolCalls);
      }
      catch (Exception cause) { errors.Add(cause); }
      finally
      {
        released.TrySetResult(current);
        if (frame is not null) await JoinUnexpected(h, errors, frame);
        h.Repository.OriginalRead = null;
      }
      ThrowInitializedModelControlErrors(errors);
    }));
  }

  // Controlled SAME issued lease source; no production model/install authority is minted.
  private sealed partial class SyntheticLease : ITaskRunOriginalInferenceLeaseSource, ITaskRunOriginalInferenceLeaseCurrentnessSource
  {
    public int ScopedRevalidations;
    public void DemandOriginalInferenceWithinSource(TaskRunAttemptAdmission admission)
    {
      if (!ReferenceEquals(admission.Lease, this)) throw new UnauthorizedAccessException("Not the SAME synthetic issued lease.");
      if (Disposes != 0) throw new ObjectDisposedException(nameof(SyntheticLease));
    }
    public async Task RevalidateOriginalInferenceWithinSourceAsync(TaskRunAttemptAdmission admission,
      Action<Action> originalScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
      if (!ReferenceEquals(admission.Lease, this)) throw new UnauthorizedAccessException("Not the SAME synthetic issued lease.");
      Interlocked.Increment(ref ScopedRevalidations);
      Task? raw = null; var errors = new List<Exception>();
      try { originalScope(() => { raw = RevalidateAsync(token).AsTask(); retainOriginalTask(raw); }); }
      catch (Exception cause) { errors.Add(cause is OperationCanceledException
        ? new AggregateException("The original synthetic lease factory faulted synchronously.", cause) : cause); }
      if (raw is null && errors.Count == 0) errors.Add(new InvalidOperationException("No original lease callback was admitted."));
      if (raw is not null)
        try { await raw; }
        catch (Exception cause) { errors.Add(raw.IsFaulted && raw.Exception is { } group
          ? new AggregateException("The actual synthetic lease Task faulted.", group.InnerExceptions) : cause); }
      ThrowInitializedModelControlErrors(errors);
    }
  }

  private sealed class ControlledSelectedCloseLease(ManagedProviderDulcheAdapter adapter) : OriginalInferenceEngineLease
  {
    public readonly TaskCompletionSource Released = Signal(); public int Closes; private Task? _close;
    public override IDulcheOriginalProviderAdapter Adapter => adapter;
    public override void DemandExternalOriginalJoin() => adapter.RequireIndependentOriginalProviderJoin();
    public override Task CloseOriginalAsync()
    { DemandExternalOriginalJoin(); return _close ??= CloseActual(); }
    private async Task CloseActual()
    { Interlocked.Increment(ref Closes); Released.TrySetResult(); await adapter.DisposeAsync(); }
  }

  private static async Task<(Guid Action, ProviderExecutionContext Context)> RawTransportResponseControl(Harness h)
  {
    var action = Guid.NewGuid();
    var acknowledged = await h.Coordinator.RegisterActionAsync(h.Snapshot.TaskId, action, null,
      "controlled raw inference response", TaskActionInterruptionPolicy.ReadOnlyCancellable, null, [],
      CancellationToken.None, h.Admission.AttemptId);
    return (action, new(acknowledged.TaskId, acknowledged.ContextId, acknowledged.ExecutionId, h.Admission.AttemptId,
      acknowledged.PersistenceRevision, action) { RequestedCandidate = h.Admission.Lease.Candidate, SelectedCandidate = h.Admission.Lease.Candidate });
  }
  private static async Task WithRawTransportScope(Harness h,
    Func<IOriginalInferenceEngineRequestSource, RawTransportScope, Task> body, int? nativeContextLimit = null, InitializedModelProvider? initialized = null)
  {
    var scope = new RawTransportScope(); var errors = new List<Exception>();
    var type = typeof(ManagedProviderDulcheAdapter).Assembly.GetType("Dulche.Runtime.ManagedInferenceEngineRequestSource", throwOnError: true)!;
    var source = (IOriginalInferenceEngineRequestSource)Activator.CreateInstance(type,
      BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, binder: null,
      args: [initialized ?? new InitializedModelProvider(h.Provider), new ModelIdentity(h.Provider.Id, "model"), h.Coordinator, nativeContextLimit], culture: null)!;
    try { await body(source, scope); }
    catch (Exception cause) { errors.Add(cause); }
    finally
    {
      foreach (var original in scope.Tasks.ToArray()) await JoinUnexpected(h, errors, original);
    }
    ThrowInitializedModelControlErrors(errors);
  }
  private sealed class RawTransportScope : IInferenceEngineOriginalSourceScope
  {
    public readonly List<Task> Tasks = [];
    public Action<object?>? AfterFactory, AfterCleanup;
    public T InvokeOriginalFactory<T>(Func<T> callback) { var actual = callback(); AfterFactory?.Invoke(actual); return actual; }
    public T InvokeOriginalCleanup<T>(Func<T> callback) { var actual = callback(); AfterCleanup?.Invoke(actual); return actual; }
    public void RetainOriginalTask(Task actual) { if (!Tasks.Any(task => ReferenceEquals(task, actual))) Tasks.Add(actual); }
  }
}
