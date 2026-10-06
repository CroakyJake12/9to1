using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Actual adapter/runtime/frame/coordinator implementations; explicitly synthetic raw
/// providers, durable repository, permission leases and context scope. No network/OS authority.</summary>
public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
    [Fact]
    public async Task Actual_runtime_managed_start_and_original_context_submission_reach_selected_raw_provider()
    {
        await Control(async h =>
        {
            var runtime = new DulcheRuntime([h.Adapter]);
            var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded);
            Assert.Equal(EndpointState.Ready, started.Value!.State);
            var session = runtime.CreateSession(started.Value.EndpointId);
            Assert.True(session.Succeeded);
            var action = await h.RegisterActionAsync();
            var submit = runtime.SubmitWithContextAsync(h.Request() with { SessionId = session.Value!.SessionId }, started.Value.EndpointId, h.Admission, action);
            var admitted = await submit;
            Assert.True(admitted.Succeeded);
            var originalHandle = admitted.Value!;
            var result = await originalHandle.AwaitResult(default);
            Assert.Equal(RequestState.Completed, result.Status);
            Assert.Equal("synthetic actual reply", result.Text);
            Assert.Equal(1, h.Provider.CompleteCalls);
            Assert.Equal(originalHandle.SessionId, result.SessionId);
            Assert.Same(h.Admission, await h.Coordinator.GetIssuedAttemptAsync(h.Snapshot.TaskId,
                h.Snapshot.ExecutionId, h.Admission.AttemptId, default));
            var stop = runtime.StopEndpointAsync(started.Value.EndpointId);
            var sameStop = runtime.StopEndpointAsync(started.Value.EndpointId);
            Assert.Same(stop, sameStop);
            Assert.True((await stop).Succeeded);
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Expanded_request_capability_refuses_before_catalogue_tool_or_context_callbacks(bool stream, bool tools)
    {
        await Control(async h =>
        {
            var error = await Record.ExceptionAsync(() => h.BindAsync(stream, tools));
            Assert.IsType<UnauthorizedAccessException>(error);
            Assert.Equal(0, h.Provider.CatalogueCalls);
            Assert.Equal(0, h.Tools.BindCalls);
            Assert.Equal(0, h.Context.Captures);
            Assert.Equal(0, h.Provider.CompleteCalls);
            Assert.Equal(0, h.Provider.StreamCalls);
            h.Expect(error!);
        }, required: [ToolCapability.Text]);
    }

    [Fact]
    public async Task One_long_stream_observes_over_128_actual_moves_without_growing_successful_custody_to_refusal()
    {
        await Control(async h =>
        {
            var stream = new OriginalStream(2048);
            h.Provider.StreamFactory = (_, _) => stream;
            var bound = await h.BindAsync(stream: true);
            var deltas = await h.ConsumeAsync(bound);
            Assert.Equal(2048, deltas.Count(d => d.Text == "x"));
            Assert.Equal(2049, stream.Moves.Count);
            Assert.All(stream.Moves, task => Assert.True(task.IsCompletedSuccessfully));
            Assert.Equal(1, stream.Disposes);
            Assert.Contains(deltas, d => d.FinishReason == "completed");
            Assert.Equal(1, h.Provider.StreamCalls);
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Outward_turn_and_same_close_retain_both_direct_original_task_exception_members()
    {
        await Control(async h =>
        {
            var one = new IOException("first original provider fault");
            var two = new InvalidOperationException("second original provider fault");
            var actual = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.SetException([one, two]);
            h.Provider.CompleteFactory = (_, _) => actual.Task;
            var bound = await h.BindAsync();
            var outward = await Record.ExceptionAsync(() => h.ConsumeAsync(bound));
            Assert.NotNull(outward);
            Assert.Same(actual.Task, h.Provider.OriginalComplete);
            Assert.Equal(2, actual.Task.Exception!.InnerExceptions.Count);
            Assert.Contains(Leaves(outward!), e => ReferenceEquals(e, one));
            Assert.Contains(Leaves(outward!), e => ReferenceEquals(e, two));
            var first = h.Adapter.StopAsync(h.Endpoint, default).AsTask();
            var second = h.Adapter.StopAsync(h.Endpoint, default).AsTask();
            Assert.Same(first, second);
            var close = await Record.ExceptionAsync(() => first);
            Assert.Contains(Leaves(close!), e => ReferenceEquals(e, one));
            Assert.Contains(Leaves(close!), e => ReferenceEquals(e, two));
            h.Expect(one); h.Expect(two);
        });
    }

    [Fact]
    public async Task Actual_stream_body_enumerator_and_context_cleanup_causes_remain_distinct_and_block_provider_ack()
    {
        await Control(async h =>
        {
            var body = new IOException("actual first MoveNext fault");
            var enumeratorCleanup = new InvalidOperationException("actual enumerator Dispose fault");
            var scopeCleanup = new ApplicationException("actual context Dispose fault");
            var stream = new OriginalStream(1) { MoveFailure = body, DisposeOriginal = Task.FromException(enumeratorCleanup) };
            h.Provider.StreamFactory = (_, _) => stream;
            h.Context.Scope.DisposeOriginal = Task.FromException(scopeCleanup);
            var bound = await h.BindAsync(stream: true);
            var error = await Record.ExceptionAsync(() => h.ConsumeAsync(bound));
            foreach (var original in new Exception[] { body, enumeratorCleanup, scopeCleanup })
                Assert.Contains(Leaves(error!), e => ReferenceEquals(e, original));
            Assert.Equal(1, stream.Disposes);
            Assert.Equal(1, h.Context.Scope.Disposes);
            Assert.True(stream.Moves.Single().IsFaulted);
            Assert.Null(h.Frames.TryObserveProviderFailure(h.Admission, h.ObservedFrames.LastResource!));
            h.Expect(body); h.Expect(enumeratorCleanup); h.Expect(scopeCleanup);
        }, remote: true, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Held_actual_enumerator_dispose_keeps_context_and_original_endpoint_close_held()
    {
        await Control(async h =>
        {
            var release = Signal();
            var stream = new OriginalStream(1) { DisposeOriginal = release.Task };
            h.Provider.StreamFactory = (_, _) => stream;
            var bound = await h.BindAsync(stream: true);
            var consume = h.ConsumeAsync(bound);
            Task? close = null;
            try
            {
                await stream.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(consume.IsCompleted);
                Assert.Equal(0, h.Context.Scope.Disposes);
                close = h.Adapter.StopAsync(h.Endpoint, default).AsTask();
                Assert.Same(close, h.Adapter.StopAsync(h.Endpoint, default).AsTask());
                Assert.False(close.IsCompleted);
                Assert.Equal(0, h.Context.Scope.Disposes);
            }
            finally
            {
                release.TrySetResult();
                var consumeError = await Record.ExceptionAsync(() => consume);
                if (consumeError is not null)
                {
                    Assert.All(Leaves(consumeError), e => Assert.IsAssignableFrom<OperationCanceledException>(e));
                    h.Expect(consumeError);
                }
                if (close is not null)
                {
                    var closeError = await Record.ExceptionAsync(() => close);
                    if (closeError is not null)
                    {
                        Assert.All(Leaves(closeError), e => Assert.IsAssignableFrom<OperationCanceledException>(e));
                        h.Expect(closeError);
                    }
                }
            }
            Assert.True(release.Task.IsCompletedSuccessfully);
            Assert.Equal(1, stream.Disposes);
            Assert.Equal(1, h.Context.Scope.Disposes);
        }, remote: true, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Deferred_factory_revocation_refuses_first_actual_move_without_second_one_use_fence_call()
    {
        await Control(async h =>
        {
            var stream = new OriginalStream(1);
            h.Provider.StreamFactory = (_, _) => { h.Context.Scope.Allowed = false; return stream; };
            var bound = await h.BindAsync(stream: true);
            var error = await Record.ExceptionAsync(() => h.ConsumeAsync(bound));
            Assert.NotNull(error);
            Assert.All(Leaves(error!), cause => Assert.IsType<UnauthorizedAccessException>(cause));
            Assert.Single(Leaves(error!).Distinct());
            Assert.Empty(stream.Moves);
            Assert.Equal(0, h.Context.Scope.Invocations);
            Assert.Equal(1, stream.Disposes);
            Assert.Equal(1, h.Context.Scope.Disposes);
            h.Expect(error!);
        }, remote: true, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Tool_arguments_detach_actual_json_elements_before_original_document_disposal()
    {
        await Control(async h =>
        {
            using var document = JsonDocument.Parse("{\"value\":\"original-json\"}");
            var element = document.RootElement.GetProperty("value");
            h.Provider.ToolFactory = (_, _) => Task.FromResult(new OllamaToolResponse("",
                [new("fixture.echo", new Dictionary<string, JsonElement> { ["value"] = element }, "actual-call")]));
            var bound = await h.BindAsync(tools: true);
            var values = await h.ConsumeAsync(bound);
            var proposal = Assert.Single(values.Where(v => v.ToolProposal is not null)).ToolProposal!;
            document.Dispose();
            Assert.Equal("actual-call", proposal.InvocationId);
            Assert.Equal("original-json", proposal.Arguments.GetProperty("value").GetString());
            Assert.Equal(1, h.Tools.BindCalls);
            Assert.Equal(1, h.Provider.ToolCalls);
        }, required: [ToolCapability.Text, ToolCapability.Tools]);
    }

    [Fact]
    public async Task Concurrent_original_binding_does_not_hold_adapter_lock_while_entering_shared_frame_owner()
    {
        await Control(async h =>
        {
            var first = await h.BindAsync();
            using var release = new ManualResetEventSlim();
            var entered = Signal(); var secondRegistration = Signal();
            var once = 0;
            h.Authority.Lease!.BeforeRevalidation = () =>
            {
                if (Interlocked.Increment(ref once) == 1)
                {
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("actual first frame revalidation barrier");
                }
            };
            var generation = Task.Run(() => h.ConsumeAsync(first));
            Task<Bound>? binding = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                h.ObservedFrames.RegistrationEntering = () => secondRegistration.TrySetResult();
                binding = Task.Run(() => h.BindAsync());
                await secondRegistration.Task.WaitAsync(TimeSpan.FromSeconds(10));
                release.Set();
                await Task.WhenAll(generation, binding).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(1, h.Provider.CompleteCalls);
                Assert.Equal(2, h.Provider.CatalogueCalls);
                var second = await binding;
                Assert.Contains(await h.ConsumeAsync(second), d => d.Text == "synthetic actual reply");
            }
            finally
            {
                release.Set();
                await generation;
                if (binding is not null) await binding;
            }
        });
    }

    [Fact]
    public async Task Actual_token_cancellation_callback_can_join_other_adapter_control_outside_state_locks()
    {
        await Control(async h =>
        {
            Task<RuntimeHealth>? actualHealth = null;
            var callbacks = 0; var entered = Signal(); var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = new OriginalStream(1);
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return held.Task; };
            stream.CaptureToken = token => token.Register(() =>
            {
                Interlocked.Increment(ref callbacks);
                actualHealth = Task.Run(async () => await h.Adapter.HealthAsync(h.Endpoint, default));
                try { actualHealth.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
                finally { held.TrySetCanceled(token); }
            });
            h.Provider.StreamFactory = (_, _) => stream;
            var bound = await h.BindAsync(stream: true); var consume = h.ConsumeAsync(bound); Task? cancel = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                cancel = h.Adapter.CancelAsync(h.Endpoint, bound.Handle.RequestId, default).AsTask(); await cancel;
                await JoinCanceledOriginal(h, consume);
                Assert.Equal(1, callbacks);
                Assert.NotNull(actualHealth);
                Assert.True(actualHealth!.IsCompletedSuccessfully);
                Assert.Equal(h.Endpoint.EndpointId, (await actualHealth).EndpointId);
            }
            finally
            {
                held.TrySetCanceled();
                await JoinCanceledOriginal(h, consume);
                if (cancel is not null) await cancel;
                stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Suppressed_execution_context_token_callback_refuses_joining_same_adapter_close()
    {
        await Control(async h =>
        {
            var entered = Signal(); var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? sameOwnerRefusal = null; var callbacks = 0;
            var stream = new OriginalStream(1);
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return held.Task; };
            stream.CaptureToken = token =>
            {
                using (ExecutionContext.SuppressFlow())
                    return token.Register(() =>
                    {
                        Interlocked.Increment(ref callbacks);
                        try { h.Adapter.StopAsync(h.Endpoint, default).AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
                        catch (Exception error) { sameOwnerRefusal = error; }
                        finally { held.TrySetCanceled(token); }
                    });
            };
            h.Provider.StreamFactory = (_, _) => stream;
            var bound = await h.BindAsync(stream: true); var consume = h.ConsumeAsync(bound); Task? cancel = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                cancel = h.Adapter.CancelAsync(h.Endpoint, bound.Handle.RequestId, default).AsTask(); await cancel;
                await JoinCanceledOriginal(h, consume);
                Assert.IsType<InvalidOperationException>(sameOwnerRefusal);
                Assert.Contains("own endpoint", sameOwnerRefusal!.Message);
                Assert.Equal(1, callbacks);
            }
            finally { held.TrySetCanceled(); await JoinCanceledOriginal(h, consume); if (cancel is not null) await cancel; stream.CapturedRegistration.Dispose(); }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Retired_captured_adapter_phase_allows_independent_later_child_close()
    {
        await Control(async h =>
        {
            var release = Signal(); Task<OperationResult<Unit>>? child = null;
            h.Provider.CompleteFactory = (_, _) =>
            {
                child = Task.Run(async () => { await release.Task; return await h.Adapter.StopAsync(h.Endpoint, default); });
                return Task.FromResult("synthetic actual reply");
            };
            var bound = await h.BindAsync();
            try
            {
                Assert.Contains(await h.ConsumeAsync(bound), value => value.Text == "synthetic actual reply");
                Assert.NotNull(child); Assert.False(child!.IsCompleted);
                release.TrySetResult();
                Assert.True((await child.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
            }
            finally { release.TrySetResult(); if (child is not null) await child; }
        });
    }

    [Fact]
    public async Task Nested_owned_control_preserves_live_ancestor_endpoint_self_join_exclusion()
    {
        await Control(async h =>
        {
            var second = h.Endpoint with { EndpointId = Guid.NewGuid().ToString("N") };
            Assert.True((await h.Adapter.StartAsync(second, default)).Succeeded);
            Exception? refusal = null;
            h.Provider.HealthFactory = async _ =>
            {
                await Task.Yield(); // The finite B control outlives the physical start call.
                try { await h.Adapter.StopAsync(h.Endpoint, default).AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception error) { refusal = error; }
                return h.Provider.Healthy();
            };
            h.Provider.CompleteFactory = async (_, _) =>
            { await h.Adapter.HealthAsync(second, default); return "synthetic actual reply"; };
            var bound = await h.BindAsync();
            Assert.Contains(await h.ConsumeAsync(bound), value => value.Text == "synthetic actual reply");
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains("own endpoint", refusal!.Message);
        });
    }

    [Fact]
    public async Task Suppressed_token_callback_refuses_same_real_runtime_endpoint_stop_and_original_worker_drains()
    {
        await Control(async h =>
        {
            var runtime = new DulcheRuntime([h.Adapter]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            Assert.True(session.Succeeded); var entered = Signal(); var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = new OriginalStream(1); Exception? refusal = null; Task? stop = null;
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return held.Task; };
            stream.CaptureToken = token =>
            {
                using (ExecutionContext.SuppressFlow())
                    return token.Register(() =>
                    {
                        try { runtime.JoinEndpointStopAsync(endpoint.EndpointId).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
                        catch (Exception error) { refusal = error; }
                        finally { held.TrySetCanceled(token); }
                    });
            };
            h.Provider.StreamFactory = (_, _) => stream;
            var admitted = await runtime.SubmitWithContextAsync(h.Request(stream: true) with { SessionId = session.Value!.SessionId }, endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var response = admitted.Value!.AwaitResult(default);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                stop = runtime.StopEndpointAsync(endpoint.EndpointId);
                await JoinCanceledOriginal(h, stop, allowSuccess: true);
                Assert.IsType<InvalidOperationException>(refusal);
                Assert.Contains("containing it", refusal!.Message);
                Assert.Equal(RequestState.Cancelled, (await response).Status);
                Assert.True(stop.IsCompleted); Assert.True(stream.Moves.Last().IsCanceled);
            }
            finally
            {
                held.TrySetCanceled(); stop ??= runtime.StopEndpointAsync(endpoint.EndpointId);
                await JoinCanceledOriginal(h, stop, allowSuccess: true); await response; stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Retired_real_submission_phase_allows_captured_child_endpoint_stop()
    {
        await Control(async h =>
        {
            var runtime = new DulcheRuntime([h.Adapter]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            Assert.True(session.Succeeded); var release = Signal(); Task<OperationResult<DulcheEndpoint>>? child = null;
            h.Tools.BeforeBind = () => child = Task.Run(async () => { await release.Task; return await runtime.StopEndpointAsync(endpoint.EndpointId); });
            try
            {
                var submit = runtime.SubmitWithContextAsync(h.Request(tools: true) with { SessionId = session.Value!.SessionId }, endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
                var admitted = await submit; Assert.True(admitted.Succeeded);
                Assert.Equal(RequestState.Completed, (await admitted.Value!.AwaitResult(default)).Status);
                Assert.True(submit.IsCompletedSuccessfully); Assert.NotNull(child); Assert.False(child!.IsCompleted);
                release.TrySetResult(); Assert.True((await child.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
            }
            finally { release.TrySetResult(); if (child is not null) await child; else await runtime.StopEndpointAsync(endpoint.EndpointId); }
        }, required: [ToolCapability.Text, ToolCapability.Tools]);
    }

    private static async Task JoinCanceledOriginal(Harness h, Task actual, bool allowSuccess = false)
    {
        var error = await Record.ExceptionAsync(() => actual);
        if (error is null) { Assert.True(allowSuccess, "The real canceled original must retain its cause."); return; }
        var causes = Leaves(error).Concat(actual.Exception?.InnerExceptions.SelectMany(Leaves) ?? []);
        Assert.All(causes, cause => Assert.IsAssignableFrom<OperationCanceledException>(cause));
        h.Expect(error);
        if (actual.Exception is { } compound) foreach (var cause in compound.InnerExceptions) h.Expect(cause);
    }

    private static async Task Control(Func<Harness, Task> body, bool remote = false, ToolCapability[]? required = null)
    {
        var h = await Harness.CreateAsync(remote, required ?? [ToolCapability.Text]);
        var errors = new List<Exception>();
        try { await body(h); } catch (Exception error) { errors.Add(error); }
        finally
        {
            Task? adapterClose = null, framesClose = null;
            try { adapterClose = h.Adapter.DisposeAsync().AsTask(); } catch (Exception error) { AddUnexpected(h, errors, error); }
            if (adapterClose is not null) await JoinUnexpected(h, errors, adapterClose);
            try { framesClose = h.Frames.CloseAndDrainAsync(); } catch (Exception error) { AddUnexpected(h, errors, error); }
            if (framesClose is not null) await JoinUnexpected(h, errors, framesClose);
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    private static void AddUnexpected(Harness h, List<Exception> errors, Exception error)
    {
        foreach (var cause in Leaves(error))
            if (!h.Expected.Any(e => ReferenceEquals(e, cause))
                && !(cause is TaskCanceledException { Task: { IsCanceled: true } actual } && h.ExpectedCanceledOriginals.Contains(actual))
                && !errors.Any(e => ReferenceEquals(e, cause))) errors.Add(cause);
    }
    private static async Task JoinUnexpected(Harness h, List<Exception> errors, Task actual)
    {
        try { await actual; }
        catch (Exception error)
        {
            AddUnexpected(h, errors, error);
            if (actual.Exception is { } compound) foreach (var cause in compound.InnerExceptions) AddUnexpected(h, errors, cause);
        }
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException { InnerExceptions.Count: > 0 } group
        ? group.InnerExceptions.SelectMany(Leaves) : [error];
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed record Bound(DulcheRequest Request, RuntimeRequestHandle Handle);

    private sealed class Harness
    {
        public required SyntheticProvider Provider { get; init; }
        public required SyntheticAuthority Authority { get; init; }
        public required SyntheticRepository Repository { get; init; }
        public required TaskExecutionCoordinator Coordinator { get; init; }
        public required TaskRunOriginalFrameOwner Frames { get; init; }
        public required ObservedFrames ObservedFrames { get; init; }
        public required TaskExecutionSnapshot Snapshot { get; init; }
        public required TaskRunAttemptAdmission Admission { get; init; }
        public required ManagedProviderDulcheAdapter Adapter { get; init; }
        public required DulcheEndpoint Endpoint { get; init; }
        public required SyntheticTools Tools { get; init; }
        public required SyntheticContext Context { get; init; }
        public readonly List<Exception> Expected = [];
        public readonly HashSet<Task> ExpectedCanceledOriginals = [];
        public void Expect(Exception actual)
        {
            foreach (var e in Leaves(actual))
            {
                if (!Expected.Contains(e)) Expected.Add(e);
                if (e is TaskCanceledException { Task: { IsCanceled: true } original }) ExpectedCanceledOriginals.Add(original);
            }
        }
        public static async Task<Harness> CreateAsync(bool remote, ToolCapability[] required)
        {
            var provider = new SyntheticProvider(remote); var repository = new SyntheticRepository();
            var authority = new SyntheticAuthority(); TaskExecutionCoordinator? coordinator = null;
            var frames = new TaskRunOriginalFrameOwner((task, run, attempt, token) => coordinator!.GetIssuedAttemptAsync(task, run, attempt, token));
            var observed = new ObservedFrames(frames);
            coordinator = new(repository, new NullSink(), admissionAuthority: authority, runtimeSettlement: frames);
            var snapshot = await coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "synthetic adapter control",
                TaskExecutionDurability.RecoverableCheckpoint, [], default);
            var candidate = new TaskRunRouteCandidate("synthetic-route", 1, provider.Id, "model", null, remote, required.Select(capability => capability.ToString()).ToArray());
            var admission = await coordinator.StartAttemptAsync(snapshot.TaskId, snapshot.ExecutionId, candidate, default);
            var tools = new SyntheticTools(); var context = new SyntheticContext();
            var adapter = await ManagedProviderDulcheAdapter.CreateAsync(provider.Id, new SyntheticRegistry(provider),
                new SyntheticConfigurations(provider), coordinator, observed, tools, contextSource: context, contextAuthority: context);
            var endpoint = new DulcheEndpoint(Guid.NewGuid().ToString("N"), provider.Id, adapter.OriginalConfiguredTarget.GetLeftPart(UriPartial.Path),
                remote ? null : adapter.OriginalConfiguredTarget.Port, EndpointState.Starting, null, adapter.Capabilities, remote, DateTimeOffset.UtcNow);
            Assert.True((await adapter.StartAsync(endpoint, default)).Succeeded);
            return new() { Provider = provider, Authority = authority, Repository = repository, Coordinator = coordinator, Frames = frames,
                ObservedFrames = observed, Snapshot = snapshot, Admission = admission, Adapter = adapter, Endpoint = endpoint,
                Tools = tools, Context = context };
        }
        public DulcheRequest Request(bool stream = false, bool tools = false) => new("synthetic prompt", SessionId: Guid.NewGuid().ToString("N"),
            Model: new(Provider.Id, "model"), Stream: stream, CallerId: "synthetic-actor", AllowCloudContext: !Provider.IsLocal,
            ToolPolicy: tools ? new(ToolCallMode.Selected, new HashSet<string> { "fixture.echo" }, "synthetic-actor", "synthetic-scope") : null);
        public async Task<Guid> RegisterActionAsync()
        {
            var id = Guid.NewGuid();
            await Coordinator.RegisterActionAsync(Snapshot.TaskId, id, null, "synthetic finite provider action",
                TaskActionInterruptionPolicy.AtomicCommit, null, [], default, Admission.AttemptId);
            return id;
        }
        public async Task<Bound> BindAsync(bool stream = false, bool tools = false)
        {
            var action = await RegisterActionAsync(); var request = Request(stream, tools);
            // This fixture owns a PUBLIC typed handle used only to exercise the adapter binding.
            // The first control separately covers real runtime-owned submission/handle production.
            var handle = new RuntimeRequestHandle(Guid.NewGuid().ToString("N"), request.SessionId!, Endpoint.EndpointId,
                _ => throw new NotSupportedException("Synthetic adapter-only handle has no runtime result producer."), EmptyEvents);
            var actual = Adapter.BindOriginalRequestAsync(Endpoint, request, handle, Admission, action, default);
            await actual;
            Adapter.CaptureOriginalDispatchRequest(handle, request, request);
            return new(request, handle);
        }
        public async Task<List<AdapterDelta>> ConsumeAsync(Bound original)
        {
            var values = new List<AdapterDelta>();
            await foreach (var value in Adapter.GenerateAsync(Endpoint, original.Request, original.Handle.RequestId, default)) values.Add(value);
            return values;
        }
        private static async IAsyncEnumerable<RuntimeEvent> EmptyEvents(long sequence, [EnumeratorCancellation] CancellationToken token)
        { token.ThrowIfCancellationRequested(); await Task.CompletedTask; yield break; }
    }

    private sealed class SyntheticProvider(bool remote) : IModelProvider
    {
        public string Id => remote ? "synthetic-remote" : "synthetic-local";
        public string DisplayName => "Synthetic test provider";
        public ModelProviderKind Kind => remote ? ModelProviderKind.OpenAICompatible : ModelProviderKind.Ollama;
        public bool IsLocal => !remote;
        public bool CanManageModels => false;
        public int CatalogueCalls, CompleteCalls, StreamCalls, ToolCalls;
        public Task<string>? OriginalComplete;
        public Func<OllamaChatRequest, CancellationToken, Task<string>> CompleteFactory = (_, _) => Task.FromResult("synthetic actual reply");
        public Func<OllamaChatRequest, CancellationToken, IAsyncEnumerable<string>> StreamFactory = (_, _) => new OriginalStream(1);
        public Func<OllamaToolRequest, CancellationToken, Task<OllamaToolResponse>> ToolFactory = (_, _) => Task.FromResult(new OllamaToolResponse("synthetic tools reply", []));
        public Func<CancellationToken, Task<ProviderHealthStatus>>? HealthFactory = null;
        public ProviderHealthStatus Healthy() => new(Id, true, "synthetic observed health", TimeSpan.Zero, DateTimeOffset.UtcNow);
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => HealthFactory?.Invoke(token) ?? Task.FromResult(Healthy());
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        {
            Interlocked.Increment(ref CatalogueCalls);
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new(Id, IsLocal,
                new("model", 0, "synthetic", "none", "none", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools, ToolCapability.Streaming }, DateTimeOffset.UtcNow))]);
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { Interlocked.Increment(ref CompleteCalls); return OriginalComplete = CompleteFactory(request, token); }
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token)
        { Interlocked.Increment(ref StreamCalls); return StreamFactory(request, token); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        { Interlocked.Increment(ref ToolCalls); return ToolFactory(request, token); }
    }
    private sealed class OriginalStream(int count) : IAsyncEnumerable<string>, IAsyncEnumerator<string>
    {
        private int _index;
        public string Current => "x";
        public int Disposes;
        public Exception? MoveFailure;
        public Func<int, Task<bool>>? MoveFactory = null;
        public Task DisposeOriginal = Task.CompletedTask;
        public readonly List<Task<bool>> Moves = [];
        public TaskCompletionSource DisposeEntered { get; } = Signal();
        public Func<CancellationToken, CancellationTokenRegistration>? CaptureToken;
        public CancellationTokenRegistration CapturedRegistration;
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token = default)
        { if (CaptureToken is { } capture) CapturedRegistration = capture(token); return this; }
        public ValueTask<bool> MoveNextAsync()
        {
            if (MoveFactory is { } factory)
            {
                var original = factory(_index++); Moves.Add(original); return new(original);
            }
            var actual = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (MoveFailure is { } failure) actual.SetException(failure); else actual.SetResult(_index++ < count);
            Moves.Add(actual.Task); return new(actual.Task);
        }
        public ValueTask DisposeAsync()
        { Interlocked.Increment(ref Disposes); DisposeEntered.TrySetResult(); return new(DisposeOriginal); }
    }
    private sealed class SyntheticTools : IOriginalDulcheProviderToolSource
    {
        public int BindCalls;
        public Action? BeforeBind = null;
        public Task<IReadOnlyList<OllamaToolDefinition>> BindOriginalAsync(TaskRunAttemptAdmission admission, Guid action,
            ToolExecutionContext context, CancellationToken token)
        { BindCalls++; BeforeBind?.Invoke(); return Task.FromResult<IReadOnlyList<OllamaToolDefinition>>([new("fixture.echo", "synthetic", new Dictionary<string, object>(), [])]); }
    }
    private sealed class SyntheticContext : IOriginalDulcheProviderContextSource, ITaskRunProviderContextAuthority
    {
        public int Captures;
        public SyntheticScope Scope { get; } = new();
        public Task CaptureOriginalAsync(TaskRunAttemptAdmission admission, TaskExecutionSnapshot snapshot, DulcheRequest request, OllamaChatRequest wire, CancellationToken token)
        { Captures++; return Task.CompletedTask; }
        public Task CaptureOriginalAsync(TaskRunAttemptAdmission admission, TaskExecutionSnapshot snapshot, DulcheRequest request, OllamaToolRequest wire, CancellationToken token)
        { Captures++; return Task.CompletedTask; }
        public ValueTask<ITaskRunProviderContextFrame> AcquireOriginalFrameAsync(TaskRunAttemptAdmission admission, TaskExecutionSnapshot snapshot, OllamaChatRequest original, OllamaChatRequest routed, CancellationToken token) => ValueTask.FromResult<ITaskRunProviderContextFrame>(Scope);
        public ValueTask<ITaskRunProviderContextFrame> AcquireOriginalFrameAsync(TaskRunAttemptAdmission admission, TaskExecutionSnapshot snapshot, OllamaToolRequest original, OllamaToolRequest routed, CancellationToken token) => ValueTask.FromResult<ITaskRunProviderContextFrame>(Scope);
    }
    private sealed class SyntheticScope : ITaskRunProviderContextFrame, ITaskRunProviderInvocationFence
    {
        public bool Allowed = true;
        public int Invocations, Disposes;
        public Task DisposeOriginal = Task.CompletedTask;
        public ValueTask RevalidateAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!Allowed) throw new UnauthorizedAccessException("synthetic scope revoked"); return ValueTask.CompletedTask; }
        public T RunOriginalInvocation<T>(Func<T> body)
        {
            if (!Allowed || Invocations != 0) throw new UnauthorizedAccessException("synthetic one-use current invocation refused");
            Invocations++; return body();
        }
        public ValueTask DisposeAsync() { Disposes++; return new(DisposeOriginal); }
    }
    private sealed class SyntheticAuthority : ITaskRunCommandAuthority
    {
        public SyntheticLease? Lease;
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot proposed, CancellationToken token)
            => Task.FromResult(new TaskExecutionOwnerBinding(proposed.TaskId, proposed.ContextId, proposed.ExecutionId,
                "synthetic-actor", "synthetic-profile", null, null, "synthetic-auth", "synthetic-authority"));
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot snapshot, Guid attempt, TaskRunRouteCandidate candidate, Guid? previous, CancellationToken token)
            => Task.FromResult<ITaskRunAdmissionLease>(Lease = new(snapshot.OwnerBinding!, attempt, candidate));
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot snapshot, Guid attempt, Guid action, string reference, CancellationToken token)
            => throw new NotSupportedException("No synthetic control action is a native mutation receipt.");
    }
    private sealed partial class SyntheticLease(TaskExecutionOwnerBinding owner, Guid attempt, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => attempt;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "synthetic original issuance";
        public Action? BeforeRevalidation;
        public int Disposes;
        public ValueTask RevalidateAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (Disposes != 0) throw new ObjectDisposedException(nameof(SyntheticLease)); BeforeRevalidation?.Invoke(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }
    private sealed class SyntheticRepository : ITaskExecutionRepository
    {
        private readonly ConcurrentDictionary<Guid, TaskExecutionSnapshot> _rows = new();
        public Func<Guid, CancellationToken, Task<TaskExecutionSnapshot?>>? OriginalRead;
        public Task UpsertAsync(TaskExecutionSnapshot snapshot, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_rows.TryGetValue(snapshot.TaskId, out var existing))
            {
                if (snapshot.PersistenceRevision != existing.PersistenceRevision + 1 || !_rows.TryUpdate(snapshot.TaskId, snapshot, existing))
                    throw new TaskExecutionRevisionConflictException(snapshot.TaskId, snapshot.PersistenceRevision - 1, snapshot.PersistenceRevision);
            }
            else if (snapshot.PersistenceRevision != 1 || !_rows.TryAdd(snapshot.TaskId, snapshot))
                throw new TaskExecutionRevisionConflictException(snapshot.TaskId, snapshot.PersistenceRevision - 1, snapshot.PersistenceRevision);
            return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid task, CancellationToken token) => OriginalRead?.Invoke(task, token) ?? Task.FromResult(_rows.GetValueOrDefault(task));
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid context, CancellationToken token) => Task.FromResult(_rows.Values.FirstOrDefault(row => row.ContextId == context));
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.ToArray());
    }
    private sealed class NullSink : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
    private sealed class SyntheticRegistry(IModelProvider provider) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => [provider];
        public IModelProvider? Find(string id) => id == provider.Id ? provider : null;
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => provider.GetModelsAsync(token);
    }
    private sealed class SyntheticConfigurations(SyntheticProvider provider) : IProviderConfigurationStore
    {
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult<ProviderConfiguration?>(new(provider.Id, provider.Kind,
            provider.DisplayName, provider.IsLocal ? "http://127.0.0.1:9477" : "https://synthetic.invalid", true, provider.IsLocal, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow));
        public async Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => [(await GetAsync(provider.Id, token))!];
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ObservedFrames(TaskRunOriginalFrameOwner actual) : ITaskRunOriginalFrameOwner
    {
        public Action? RegistrationEntering;
        public Task? LastResource;
        public Task RegisterOriginalAttemptAsync(TaskRunAttemptAdmission admission, CancellationToken token) { RegistrationEntering?.Invoke(); return actual.RegisterOriginalAttemptAsync(admission, token); }
        public Task<T> StartOriginalFrameAsync<T>(TaskRunAttemptAdmission admission, Func<CancellationToken, Task<T>> body, CancellationToken token) => actual.StartOriginalFrameAsync(admission, body, token);
        public Task<T> StartOriginalResourceFrameAsync<T, TResource>(TaskRunAttemptAdmission admission, Func<CancellationToken, Task<TResource>> acquire, Func<TResource, CancellationToken, Task> validate, Func<CancellationToken, Task<T>> body, CancellationToken token) where TResource : class, IAsyncDisposable
        { var original = actual.StartOriginalResourceFrameAsync(admission, acquire, validate, body, token); LastResource = original; return original; }
        public Task<T> StartOriginalResourceFrameAsync<T, TResource>(TaskRunAttemptAdmission admission, Func<CancellationToken, Task<TResource>> acquire, Func<TResource, CancellationToken, Task> validate, Func<TResource, CancellationToken, Task<T>> body, CancellationToken token) where TResource : class, IAsyncDisposable
        { var original = actual.StartOriginalResourceFrameAsync(admission, acquire, validate, body, token); LastResource = original; return original; }
        public Task<T> StartOriginalToolFrameAsync<T>(TaskRunAttemptAdmission admission, Func<CancellationToken, Task<T>> body, CancellationToken token) => actual.StartOriginalToolFrameAsync(admission, body, token);
        public IAsyncEnumerable<T> StreamOriginalFrame<T>(TaskRunAttemptAdmission admission, Func<CancellationToken, IAsyncEnumerable<T>> body, CancellationToken token) => actual.StreamOriginalFrame(admission, body, token);
        public TaskRunOriginalFailureObservation CreateProviderFailureObservation(TaskRunAttemptAdmission admission, Task original, Exception cause) => actual.CreateProviderFailureObservation(admission, original, cause);
        public TaskRunOriginalFailureObservation? TryObserveProviderFailure(TaskRunAttemptAdmission admission, Task original) => actual.TryObserveProviderFailure(admission, original);
        public bool ValidateProviderFailureObservation(TaskRunOriginalFailureObservation observation, TaskRunAttemptAdmission admission) => actual.ValidateProviderFailureObservation(observation, admission);
        public void AcknowledgeProviderFailure(TaskRunOriginalFailureObservation observation, TaskRunAttemptAdmission admission, TaskRunFailurePersistenceAcknowledgment receipt) => actual.AcknowledgeProviderFailure(observation, admission, receipt);
        public Task AwaitSettlementAsync(Guid task, Guid run, Guid attempt, CancellationToken token) => actual.AwaitSettlementAsync(task, run, attempt, token);
        public ValueTask RetireAcknowledgedOriginalAttemptAsync(TaskRunOriginalRetirementAcknowledgment receipt, CancellationToken token) => actual.RetireAcknowledgedOriginalAttemptAsync(receipt, token);
        public Task CloseAndDrainAsync() => actual.CloseAndDrainAsync();
        public ValueTask DisposeAsync() => actual.DisposeAsync();
    }
}
