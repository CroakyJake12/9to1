using System.Runtime.CompilerServices;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Runtime.Tests;

public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
    [Fact]
    public async Task Private_cancellation_observation_waits_real_cleanup_and_retains_faulted_inner_and_public_moves()
    {
        await Control(async h =>
        {
            await CancellationTurn(h, 0, "held", false, async actual =>
            {
                var source = (IDulcheOriginalCancellationSource)h.Adapter;
                Assert.True(source.TryObserveOriginalCancellation(actual.Bound.Handle, actual.Error, actual.Token, out var proof));
                Assert.NotNull(proof);
                Assert.Same(actual.Raw, proof!.OriginalRawMove);
                Assert.True(actual.PublicMove.IsFaulted);
                Assert.True(proof.OriginalInnerMove.IsFaulted);
                Assert.NotSame(actual.PublicMove, proof.OriginalInnerMove);
                Assert.Same(actual.Error, proof.OriginalOutwardFailure);
                Assert.Contains(actual.PublicMove.Exception!.InnerExceptions, cause => ReferenceEquals(cause, actual.Error));
                Assert.True(proof.OriginalRawMove.IsCanceled);
                Assert.True(proof.OriginalReaderMove.IsCanceled);
                Assert.True(proof.OriginalProviderFrame.IsCanceled);
                Assert.True(proof.OriginalProducer.IsCanceled);
                Assert.True(proof.OriginalResourceClose.IsCompletedSuccessfully);
                Assert.True(proof.OriginalTurnRelease.IsCompletedSuccessfully);
                Assert.False(source.TryObserveOriginalCancellation(actual.Bound.Handle, new AggregateException(actual.Error), actual.Token, out _));
                var copiedHandle = new RuntimeRequestHandle(actual.Bound.Handle.RequestId, actual.Bound.Handle.SessionId,
                    actual.Bound.Handle.EndpointId, actual.Bound.Handle.AwaitResult, actual.Bound.Handle.ReadEvents);
                Assert.False(source.TryObserveOriginalCancellation(copiedHandle, actual.Error, actual.Token, out _));
                using var wrongOwner = new CancellationTokenSource(); wrongOwner.Cancel();
                Assert.False(source.TryObserveOriginalCancellation(actual.Bound.Handle, actual.Error, wrongOwner.Token, out _));
                var other = await h.BindAsync(stream: true);
                Assert.False(source.TryObserveOriginalCancellation(other.Handle, actual.Error, actual.Token, out _));
                var close = h.Adapter.StopAsync(h.Endpoint, default).AsTask();
                var error = await Record.ExceptionAsync(() => close);
                Assert.NotNull(error);
                Assert.Contains(Leaves(error!), cause => ReferenceEquals(cause, proof.OriginalRawCause));
                Assert.Contains(Leaves(error!), cause => ReferenceEquals(cause, proof.OriginalReaderCause));
                h.Expect(error!);
                Assert.True(actual.PublicMove.IsFaulted); // Response observation never changes an original into success.
            });
        }, remote: true, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Actual_faulted_or_synchronous_raw_OCE_never_issues_cancellation_proof(int rawMode)
    {
        await Control(async h =>
        {
            await CancellationTurn(h, rawMode, "healthy", false, actual =>
            {
                Assert.False(((IDulcheOriginalCancellationSource)h.Adapter).TryObserveOriginalCancellation(
                    actual.Bound.Handle, actual.Error, actual.Token, out _));
                if (rawMode != 3) Assert.True(actual.Raw.IsFaulted);
                Assert.Contains(Leaves(actual.Error), cause => ReferenceEquals(cause, actual.RawFault));
                if (rawMode == 2) Assert.Contains(Leaves(actual.Error), cause => ReferenceEquals(cause, actual.Sibling));
                return Task.CompletedTask;
            });
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Theory]
    [InlineData("enumerator-fault")]
    [InlineData("enumerator-sync")]
    [InlineData("context-fault")]
    public async Task Actual_canceled_raw_move_with_faulted_or_synchronous_cleanup_OCE_refuses_proof(string cleanup)
    {
        await Control(async h =>
        {
            await CancellationTurn(h, 0, cleanup, false, actual =>
            {
                Assert.True(actual.Raw.IsCanceled);
                Assert.False(((IDulcheOriginalCancellationSource)h.Adapter).TryObserveOriginalCancellation(
                    actual.Bound.Handle, actual.Error, actual.Token, out _));
                Assert.Contains(Leaves(actual.Error), cause => ReferenceEquals(cause, actual.CleanupFault));
                return Task.CompletedTask;
            });
        }, remote: true, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Known_original_adapter_cancel_callback_fault_refuses_private_cancellation_proof()
    {
        await Control(async h =>
        {
            await CancellationTurn(h, 0, "healthy", true, actual =>
            {
                Assert.True(actual.Raw.IsCanceled);
                Assert.False(((IDulcheOriginalCancellationSource)h.Adapter).TryObserveOriginalCancellation(
                    actual.Bound.Handle, actual.Error, actual.Token, out _));
                Assert.NotNull(actual.AdapterCancel);
                Assert.False(actual.AdapterCancel!.IsCompletedSuccessfully);
                return Task.CompletedTask;
            });
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Retained_prior_turn_observation_cannot_classify_a_new_turn_on_same_original_request()
    {
        await Control(async h =>
        {
            await CancellationTurn(h, 0, "healthy", false, async actual =>
            {
                var source = (IDulcheOriginalCancellationSource)h.Adapter;
                Assert.True(source.TryObserveOriginalCancellation(actual.Bound.Handle, actual.Error, actual.Token, out _));
                var entered = Signal(); var release = Signal(); var second = new OriginalStream(1);
                second.MoveFactory = index => index == 0 ? Hold() : Task.FromResult(false);
                async Task<bool> Hold() { entered.TrySetResult(); await release.Task; return true; }
                h.Provider.StreamFactory = (_, _) => second;
                var consume = h.ConsumeAsync(actual.Bound);
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.False(source.TryObserveOriginalCancellation(actual.Bound.Handle, actual.Error, actual.Token, out _));
                }
                finally
                {
                    release.TrySetResult();
                    var error = await Record.ExceptionAsync(() => consume);
                    if (error is not null) h.Expect(error);
                }
            });
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_runtime_managed_faulted_raw_OCE_remains_Failed_even_with_endpoint_owner_cancel_requested(bool sibling)
    {
        await Control(async h =>
        {
            var runtime = new DulcheRuntime([h.Adapter]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            var entered = Signal(); var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = new OriginalStream(1); OperationCanceledException? rawFault = null;
            var extra = new IOException("real raw direct sibling");
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return raw.Task; };
            stream.CaptureToken = token => token.Register(() =>
            {
                rawFault = new OperationCanceledException("actual Faulted raw task", token);
                if (sibling) raw.TrySetException([rawFault, extra]); else raw.TrySetException(rawFault);
            });
            h.Provider.StreamFactory = (_, _) => stream;
            var admitted = await runtime.SubmitWithContextAsync(h.Request(stream: true) with { SessionId = session.Value!.SessionId },
                endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var response = admitted.Value!.AwaitResult(default); Task? stop = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); stop = runtime.StopEndpointAsync(endpoint.EndpointId);
                var stopError = await Record.ExceptionAsync(() => stop);
                Assert.NotNull(stopError); h.Expect(stopError!);
                Assert.Equal(RequestState.Failed, (await response).Status);
                Assert.True(raw.Task.IsFaulted); Assert.Same(rawFault, raw.Task.Exception!.InnerExceptions[0]);
                Assert.Contains(Leaves(stopError!), cause => ReferenceEquals(cause, rawFault));
                if (sibling) Assert.Contains(Leaves(stopError!), cause => ReferenceEquals(cause, extra));
            }
            finally
            {
                raw.TrySetException(new IOException("owned fixture abort release"));
                stop ??= runtime.StopEndpointAsync(endpoint.EndpointId);
                var error = await Record.ExceptionAsync(() => stop); if (error is not null) h.Expect(error);
                await response; stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }


    [Fact]
    public async Task Buffered_managed_delta_after_owner_stop_is_discarded_before_real_next_cancellation_and_cleanup()
    {
        await Control(async h =>
        {
            var buffered = Signal(); var release = Signal(); var rawEntered = Signal();
            var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = new OriginalStream(1);
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); rawEntered.TrySetResult(); return raw.Task; };
            stream.CaptureToken = token => token.Register(() => raw.TrySetCanceled(token));
            h.Provider.StreamFactory = (_, _) => stream;
            var delayedConsumer = new HeldActualManagedConsumer(h.Adapter, buffered, release);
            var runtime = new DulcheRuntime([delayedConsumer]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            var admitted = await runtime.SubmitWithContextAsync(h.Request(stream: true) with { SessionId = session.Value!.SessionId },
                endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var response = admitted.Value!.AwaitResult(default); Task? stop = null;
            try
            {
                await buffered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await rawEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                stop = runtime.StopEndpointAsync(endpoint.EndpointId);
                await stream.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(raw.Task.IsCanceled); Assert.False(response.IsCompleted); Assert.False(stop.IsCompleted);
                release.TrySetResult();
                var stopError = await Record.ExceptionAsync(() => stop); Assert.NotNull(stopError); h.Expect(stopError!);
                var result = await response; Assert.Equal(RequestState.Cancelled, result.Status);
                Assert.True(string.IsNullOrEmpty(result.Text));
                Assert.DoesNotContain(result.FullLog, item => item.Type is "TextDelta" or "ToolProposed" or "GeneratedUI");
                Assert.Equal(1, stream.Disposes); Assert.True(raw.Task.IsCanceled);
            }
            finally
            {
                release.TrySetResult(); raw.TrySetCanceled(); stop ??= runtime.StopEndpointAsync(endpoint.EndpointId);
                var error = await Record.ExceptionAsync(() => stop); if (error is not null) h.Expect(error);
                await response; stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Ordinary_managed_stop_response_preserves_Cancelled_from_same_real_request_originals()
    {
        await Control(async h =>
        {
            var runtime = new DulcheRuntime([h.Adapter]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            var entered = Signal(); var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = new OriginalStream(1);
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return raw.Task; };
            stream.CaptureToken = token => token.Register(() => raw.TrySetCanceled(token)); h.Provider.StreamFactory = (_, _) => stream;
            var admitted = await runtime.SubmitWithContextAsync(h.Request(stream: true) with { SessionId = session.Value!.SessionId },
                endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var response = admitted.Value!.AwaitResult(default); Task? stop = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True((await runtime.StopResponseAsync(admitted.Value.RequestId)).Succeeded);
                Assert.Equal(RequestState.Cancelled, (await response).Status); Assert.True(raw.Task.IsCanceled);
            }
            finally
            {
                raw.TrySetCanceled(); stop = runtime.StopEndpointAsync(endpoint.EndpointId);
                var error = await Record.ExceptionAsync(() => stop); if (error is not null) h.Expect(error);
                await response; stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    // Explicit fixture-only consumer scheduling boundary; all provider binding, raw stream/frame,
    // context authority, private observation and stop are the actual ManagedProviderDulcheAdapter.
    private sealed class HeldActualManagedConsumer(ManagedProviderDulcheAdapter inner,
        TaskCompletionSource buffered, TaskCompletionSource release) : IDulcheOriginalProviderAdapter, IDulcheOriginalCancellationSource
    {
        public string ProviderId => inner.ProviderId;
        public string RuntimeVersion => inner.RuntimeVersion;
        public bool IsLocal => inner.IsLocal;
        public IReadOnlySet<string> Capabilities => inner.Capabilities;
        public Uri OriginalConfiguredTarget => inner.OriginalConfiguredTarget;
        public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint, CancellationToken token) => inner.StartAsync(endpoint, token);
        public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint, string id, CancellationToken token) => inner.CancelAsync(endpoint, id, token);
        public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint, CancellationToken token) => inner.StopAsync(endpoint, token);
        public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint, string id, CancellationToken token) => inner.PauseAsync(endpoint, id, token);
        public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint, string id, CancellationToken token) => inner.ResumeAsync(endpoint, id, token);
        public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint, CancellationToken token) => inner.HealthAsync(endpoint, token);
        public Task BindOriginalRequestAsync(DulcheEndpoint endpoint, DulcheRequest frozen, RuntimeRequestHandle handle,
            TaskRunAttemptAdmission admission, Guid action, CancellationToken token) => inner.BindOriginalRequestAsync(endpoint, frozen, handle, admission, action, token);
        public void CaptureOriginalDispatchRequest(RuntimeRequestHandle handle, DulcheRequest frozen, DulcheRequest actual)
            => inner.CaptureOriginalDispatchRequest(handle, frozen, actual);
        public bool TryObserveOriginalCancellation(RuntimeRequestHandle handle, Exception error, CancellationToken token,
            out DulcheOriginalCancellationObservation? observation) => inner.TryObserveOriginalCancellation(handle, error, token, out observation);
        public IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint, DulcheRequest request,
            string id, ToolInvocationResult result, CancellationToken token) => inner.ContinueWithToolResultAsync(endpoint, request, id, result, token);
        public IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint, DulcheRequest request, string id, CancellationToken token)
            => HoldFirstActualDelta(inner.GenerateAsync(endpoint, request, id, token), token);
        private async IAsyncEnumerable<AdapterDelta> HoldFirstActualDelta(IAsyncEnumerable<AdapterDelta> actual,
            [EnumeratorCancellation] CancellationToken token)
        {
            var first = true;
            await foreach (var delta in actual.WithCancellation(token))
            {
                if (first) { first = false; buffered.TrySetResult(); await release.Task; }
                yield return delta;
            }
        }
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Suppressed_context_request_cancel_callback_cannot_join_Stop_or_Replace_driver(bool replace)
    {
        await Control(async h =>
        {
            var runtime = new DulcheRuntime([h.Adapter]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            var entered = Signal(); var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = new OriginalStream(1); string requestId = ""; Exception? denied = null; var callbacks = 0;
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return raw.Task; };
            stream.CaptureToken = token =>
            {
                using (ExecutionContext.SuppressFlow())
                    return token.Register(() =>
                    {
                        Interlocked.Increment(ref callbacks);
                        try
                        {
                            if (replace) runtime.ReplacePromptAsync(requestId, "callback must not replace its original").GetAwaiter().GetResult();
                            else runtime.StopResponseAsync(requestId).GetAwaiter().GetResult();
                        }
                        catch (Exception error) { denied = error; }
                        finally { raw.TrySetCanceled(token); }
                    });
            };
            h.Provider.StreamFactory = (_, _) => stream;
            var admitted = await runtime.SubmitWithContextAsync(h.Request(stream: true) with { SessionId = session.Value!.SessionId },
                endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); requestId = admitted.Value!.RequestId; var response = admitted.Value.AwaitResult(default); Task? stop = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True((await runtime.StopResponseAsync(requestId).WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
                Assert.Equal(1, callbacks); Assert.IsType<InvalidOperationException>(denied);
                Assert.True(raw.Task.IsCanceled); Assert.Equal(RequestState.Cancelled, (await response).Status);
                var requests = (System.Collections.IDictionary)typeof(DulcheRuntime).GetField("_requests",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(runtime)!;
                var original = requests[requestId]!;
                Assert.Same(admitted.Value, original.GetType().GetField("OriginalProviderHandle")!.GetValue(original));
            }
            finally
            {
                raw.TrySetCanceled(); stop = runtime.StopEndpointAsync(endpoint.EndpointId);
                var error = await Record.ExceptionAsync(() => stop); if (error is not null) h.Expect(error);
                await response; stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    [Fact]
    public async Task Synchronous_original_cancel_OCE_keeps_faulted_finite_stage_and_exact_cause_in_endpoint_close()
    {
        await Control(async h =>
        {
            var originalCause = new OperationCanceledException("synchronous original adapter cancel fault"); var calls = 0;
            var wrapper = new InterceptOriginalManagedCancel(h.Adapter, (endpoint, id, token) =>
                Interlocked.Increment(ref calls) == 1 ? throw originalCause : h.Adapter.CancelAsync(endpoint, id, token));
            var runtime = new DulcheRuntime([wrapper]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            var entered = Signal(); var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = new OriginalStream(1);
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return raw.Task; };
            stream.CaptureToken = token => token.Register(() => raw.TrySetCanceled(token)); h.Provider.StreamFactory = (_, _) => stream;
            var admitted = await runtime.SubmitWithContextAsync(h.Request(stream: true) with { SessionId = session.Value!.SessionId },
                endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var response = admitted.Value!.AwaitResult(default); Task? stop = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var failure = await Record.ExceptionAsync(() => runtime.StopResponseAsync(admitted.Value.RequestId));
                Assert.NotNull(failure); Assert.Equal(RequestState.Failed, (await response).Status); Assert.True(raw.Task.IsCanceled);
                var requests = (System.Collections.IDictionary)typeof(DulcheRuntime).GetField("_requests",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(runtime)!;
                var original = requests[admitted.Value.RequestId]!;
                var stage = (TaskCompletionSource)original.GetType().GetField("OriginalManagedRequestCancellationAdmission")!.GetValue(original)!;
                Assert.True(stage.Task.IsFaulted); Assert.Same(originalCause, Assert.Single(stage.Task.Exception!.InnerExceptions));
                var endpoints = (System.Collections.IDictionary)typeof(DulcheRuntime).GetField("_endpoints",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(runtime)!;
                var owner = endpoints[endpoint.EndpointId]!;
                var retained = (Task[])owner.GetType().GetMethod("CaptureOriginalSubmissions")!.Invoke(owner, null)!;
                Assert.Contains(retained, task => ReferenceEquals(task, stage.Task));
                stop = runtime.StopEndpointAsync(endpoint.EndpointId);
                var error = await Record.ExceptionAsync(() => stop); Assert.NotNull(error);
                Assert.Contains(Leaves(error!), cause => ReferenceEquals(cause, originalCause)); h.Expect(error!);
                Assert.True(stage.Task.IsFaulted); Assert.True(raw.Task.IsCanceled);
            }
            finally
            {
                raw.TrySetCanceled(); stop ??= runtime.StopEndpointAsync(endpoint.EndpointId);
                var error = await Record.ExceptionAsync(() => stop); if (error is not null) h.Expect(error);
                await response; stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    // Scheduling/fault fixture only. Exact binding, raw frame, source-issued proof and stop
    // remain the actual managed adapter; the single intercepted finite cancel-start fault is explicit.
    private sealed class InterceptOriginalManagedCancel(ManagedProviderDulcheAdapter inner,
        Func<DulcheEndpoint, string, CancellationToken, ValueTask<OperationResult<Unit>>> cancel) : IDulcheOriginalProviderAdapter, IDulcheOriginalCancellationSource
    {
        public string ProviderId => inner.ProviderId;
        public string RuntimeVersion => inner.RuntimeVersion;
        public bool IsLocal => inner.IsLocal;
        public IReadOnlySet<string> Capabilities => inner.Capabilities;
        public Uri OriginalConfiguredTarget => inner.OriginalConfiguredTarget;
        public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint, CancellationToken token) => inner.StartAsync(endpoint, token);
        public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint, string id, CancellationToken token) => cancel(endpoint, id, token);
        public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint, CancellationToken token) => inner.StopAsync(endpoint, token);
        public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint, string id, CancellationToken token) => inner.PauseAsync(endpoint, id, token);
        public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint, string id, CancellationToken token) => inner.ResumeAsync(endpoint, id, token);
        public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint, CancellationToken token) => inner.HealthAsync(endpoint, token);
        public Task BindOriginalRequestAsync(DulcheEndpoint endpoint, DulcheRequest frozen, RuntimeRequestHandle handle,
            TaskRunAttemptAdmission admission, Guid action, CancellationToken token) => inner.BindOriginalRequestAsync(endpoint, frozen, handle, admission, action, token);
        public void CaptureOriginalDispatchRequest(RuntimeRequestHandle handle, DulcheRequest frozen, DulcheRequest actual)
            => inner.CaptureOriginalDispatchRequest(handle, frozen, actual);
        public bool TryObserveOriginalCancellation(RuntimeRequestHandle handle, Exception error, CancellationToken token,
            out DulcheOriginalCancellationObservation? observation) => inner.TryObserveOriginalCancellation(handle, error, token, out observation);
        public IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint, DulcheRequest request,
            string id, ToolInvocationResult result, CancellationToken token) => inner.ContinueWithToolResultAsync(endpoint, request, id, result, token);
        public IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint, DulcheRequest request, string id, CancellationToken token)
            => inner.GenerateAsync(endpoint, request, id, token);
    }


    [Fact]
    public async Task Buffered_managed_replace_retains_old_original_Replaced_after_real_cancel_without_issuing_new_grant()
    {
        await Control(async h =>
        {
            var buffered = Signal(); var release = Signal(); var entered = Signal();
            var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = new OriginalStream(1);
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return raw.Task; };
            stream.CaptureToken = token => token.Register(() => raw.TrySetCanceled(token)); h.Provider.StreamFactory = (_, _) => stream;
            var wrapper = new HeldActualManagedConsumer(h.Adapter, buffered, release);
            var runtime = new DulcheRuntime([wrapper]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            var admitted = await runtime.SubmitWithContextAsync(h.Request(stream: true) with { SessionId = session.Value!.SessionId },
                endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var originalHandle = admitted.Value!; var oldResponse = originalHandle.AwaitResult(default);
            Task<OperationResult<RuntimeRequestHandle>>? replacement = null; Task? stop = null;
            try
            {
                await buffered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                replacement = runtime.ReplacePromptAsync(originalHandle.RequestId, "new prompt has no canonical binding");
                await stream.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(raw.Task.IsCanceled); Assert.False(oldResponse.IsCompleted);
                release.TrySetResult();
                var next = await replacement; Assert.True(next.Succeeded);
                var prior = await oldResponse; Assert.Equal(RequestState.Replaced, prior.Status);
                Assert.Equal(FinishReason.Replaced, prior.FinishReason); Assert.True(string.IsNullOrEmpty(prior.Text));
                Assert.DoesNotContain(prior.FullLog, item => item.Type is "TextDelta" or "ToolProposed" or "GeneratedUI");
                Assert.NotSame(originalHandle, next.Value); Assert.Equal(originalHandle.RequestId, next.Value!.RequestId);
                Assert.Equal(RequestState.Failed, (await next.Value.AwaitResult(default)).Status);
                Assert.Equal(1, h.Provider.StreamCalls); Assert.Equal(0, h.Provider.CompleteCalls); Assert.Equal(1, stream.Disposes);
            }
            finally
            {
                release.TrySetResult(); raw.TrySetCanceled();
                if (replacement is not null)
                {
                    var next = await replacement;
                    if (next.Succeeded) await next.Value!.AwaitResult(default);
                }
                stop = runtime.StopEndpointAsync(endpoint.EndpointId);
                var error = await Record.ExceptionAsync(() => stop); if (error is not null) h.Expect(error);
                await oldResponse; stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }


    [Fact]
    public async Task Sealed_endpoint_refuses_replacement_without_changing_old_original_cancellation_attribution()
    {
        await Control(async h =>
        {
            var runtime = new DulcheRuntime([h.Adapter]); var started = await runtime.StartManagedProviderAsync(h.Provider.Id);
            Assert.True(started.Succeeded); var endpoint = started.Value!; var session = runtime.CreateSession(endpoint.EndpointId);
            var entered = Signal(); var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopEntered = Signal(); var releaseStop = Signal(); var stream = new OriginalStream(1);
            stream.MoveFactory = index => { if (index == 0) return Task.FromResult(true); entered.TrySetResult(); return raw.Task; };
            stream.CaptureToken = token => token.Register(() => raw.TrySetCanceled(token)); h.Provider.StreamFactory = (_, _) => stream;
            var admitted = await runtime.SubmitWithContextAsync(h.Request(stream: true) with { SessionId = session.Value!.SessionId },
                endpoint.EndpointId, h.Admission, await h.RegisterActionAsync());
            Assert.True(admitted.Succeeded); var response = admitted.Value!.AwaitResult(default); Task? stop = null;
            CancellationTokenRegistration heldStop = default;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var endpoints = (System.Collections.IDictionary)typeof(DulcheRuntime).GetField("_endpoints",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(runtime)!;
                var owner = endpoints[endpoint.EndpointId]!;
                var stopping = (CancellationTokenSource)owner.GetType().GetProperty("Stopping")!.GetValue(owner)!;
                using (ExecutionContext.SuppressFlow())
                    heldStop = stopping.Token.Register(() => { stopEntered.TrySetResult(); releaseStop.Task.GetAwaiter().GetResult(); });
                stop = runtime.StopEndpointAsync(endpoint.EndpointId);
                await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var refused = await Record.ExceptionAsync(() => runtime.ReplacePromptAsync(admitted.Value.RequestId, "sealed replacement must refuse"));
                Assert.IsType<ObjectDisposedException>(refused); Assert.False(response.IsCompleted);
                var requests = (System.Collections.IDictionary)typeof(DulcheRuntime).GetField("_requests",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(runtime)!;
                var original = requests[admitted.Value.RequestId]!;
                Assert.False((bool)original.GetType().GetField("OriginalManagedReplacementRequested")!.GetValue(original)!);
                Assert.Same(admitted.Value, original.GetType().GetField("OriginalProviderHandle")!.GetValue(original));
                releaseStop.TrySetResult();
                var error = await Record.ExceptionAsync(() => stop); Assert.NotNull(error); h.Expect(error!);
                Assert.Equal(RequestState.Cancelled, (await response).Status); Assert.True(raw.Task.IsCanceled);
                Assert.Equal(1, h.Provider.StreamCalls); Assert.Equal(0, h.Provider.CompleteCalls); Assert.Equal(1, stream.Disposes);
            }
            finally
            {
                releaseStop.TrySetResult(); raw.TrySetCanceled(); stop ??= runtime.StopEndpointAsync(endpoint.EndpointId);
                var error = await Record.ExceptionAsync(() => stop); if (error is not null) h.Expect(error);
                await response; heldStop.Dispose(); stream.CapturedRegistration.Dispose();
            }
        }, required: [ToolCapability.Text, ToolCapability.Streaming]);
    }

    private sealed record CancellationTurnResult(Bound Bound, Task<bool> PublicMove, Task<bool> Raw,
        Exception Error, CancellationToken Token, Exception RawFault, Exception Sibling, Exception CleanupFault,
        Task? AdapterCancel);

    private static async Task CancellationTurn(Harness h, int rawMode, string cleanup, bool callbackFault,
        Func<CancellationTurnResult, Task> inspect)
    {
        using var caller = new CancellationTokenSource(); var token = caller.Token;
        var entered = Signal(); var release = Signal(); var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OperationCanceledException? rawFault = null;
        var sibling = new IOException("original direct raw sibling");
        var cleanupFault = new OperationCanceledException("original cleanup fault, never provider cancellation");
        var callbackError = new IOException("actual owned cancellation callback fault");
        var inner = new OriginalStream(1);
        var stream = new CancellationTestStream(inner, cleanup == "enumerator-sync" ? () => throw cleanupFault : null);
        inner.MoveFactory = index =>
        {
            if (rawMode == 3) { entered.TrySetResult(); caller.Cancel(); rawFault = new OperationCanceledException("synchronous original raw OCE", stream.OwnerToken); throw rawFault; }
            if (index == 0) return Task.FromResult(true);
            entered.TrySetResult();
            return raw.Task;
        };
        inner.CaptureToken = ct =>
        {
            stream.OwnerToken = ct;
            return ct.Register(() =>
            {
                if (rawMode == 0) raw.TrySetCanceled(ct);
                else if (rawMode != 3)
                {
                    rawFault = new OperationCanceledException("Faulted original raw OCE", ct);
                    if (rawMode == 2) raw.TrySetException([rawFault, sibling]); else raw.TrySetException(rawFault);
                }
                if (callbackFault) throw callbackError;
            });
        };
        if (cleanup == "held") inner.DisposeOriginal = release.Task;
        else if (cleanup == "enumerator-fault") inner.DisposeOriginal = Task.FromException(cleanupFault);
        else if (cleanup == "context-fault") h.Context.Scope.DisposeOriginal = Task.FromException(cleanupFault);
        h.Provider.StreamFactory = (_, _) => stream;
        var bound = await h.BindAsync(stream: true); var enumerator = h.Adapter.GenerateAsync(h.Endpoint, bound.Request, bound.Handle.RequestId, token).GetAsyncEnumerator(token);
        Task<bool>? move = null; Task? cancel = null;
        Exception? failure = null;
        try
        {
            if (rawMode == 3) move = enumerator.MoveNextAsync().AsTask();
            else { Assert.True(await enumerator.MoveNextAsync()); move = enumerator.MoveNextAsync().AsTask(); }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (callbackFault)
            {
                cancel = h.Adapter.CancelAsync(h.Endpoint, bound.Handle.RequestId, default).AsTask();
                var error = await Record.ExceptionAsync(() => cancel); Assert.NotNull(error);
                Assert.Contains(Leaves(error!), cause => ReferenceEquals(cause, callbackError)); h.Expect(error!);
            }
            if (!caller.IsCancellationRequested) caller.Cancel();
            if (cleanup == "held")
            {
                await inner.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(move.IsCompleted); Assert.Equal(0, h.Context.Scope.Disposes);
                Assert.False(((IDulcheOriginalCancellationSource)h.Adapter).TryObserveOriginalCancellation(bound.Handle,
                    new AggregateException(), token, out _));
                release.TrySetResult();
            }
            failure = await Record.ExceptionAsync(() => move);
            Assert.NotNull(failure); h.Expect(failure!);
            if (move.Exception is { } group) foreach (var cause in group.InnerExceptions) h.Expect(cause);
            await inspect(new(bound, move, raw.Task, failure!, token, (Exception?)rawFault ?? new InvalidOperationException("no raw fault"),
                sibling, cleanupFault, cancel));
        }
        finally
        {
            release.TrySetResult(); raw.TrySetCanceled(stream.OwnerToken);
            try { if (!caller.IsCancellationRequested) caller.Cancel(); } catch (Exception error) { h.Expect(error); }
            if (move is not null) { var error = await Record.ExceptionAsync(() => move); if (error is not null) h.Expect(error); }
            Task? dispose = null;
            try { dispose = enumerator.DisposeAsync().AsTask(); } catch (Exception error) { h.Expect(error); }
            if (dispose is not null) { var error = await Record.ExceptionAsync(() => dispose); if (error is not null) h.Expect(error); }
            if (cancel is not null) { var error = await Record.ExceptionAsync(() => cancel); if (error is not null) h.Expect(error); }
            inner.CapturedRegistration.Dispose();
        }
    }

    private sealed class CancellationTestStream(OriginalStream original, Func<ValueTask>? synchronousDispose)
        : IAsyncEnumerable<string>, IAsyncEnumerator<string>
    {
        public CancellationToken OwnerToken;
        public string Current => original.Current;
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token = default)
        { _ = original.GetAsyncEnumerator(token); return this; }
        public ValueTask<bool> MoveNextAsync() => original.MoveNextAsync();
        public ValueTask DisposeAsync() => synchronousDispose is null ? original.DisposeAsync() : synchronousDispose();
    }
}
