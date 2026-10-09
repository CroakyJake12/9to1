using System.Net;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

/// <summary>Actual original frame owner with synthetic issued lease; HttpClient timeout uses its real managed cancellation path. No installed authority/provider acceptance.</summary>
public sealed partial class TaskRunOriginalFrameOwnerTests
{
    [Fact]
    public async Task Registration_failure_completes_stream_reader_without_entering_original_body()
    {
        var h = Harness.Create();
        var cause = new IOException("Original canonical lookup failure");
        h.LookupFailure = cause;
        var registration = h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => registration));
        using var caller = new CancellationTokenSource();
        var called = false;
        var iterator = h.Runtime.StreamOriginalFrame(h.Admission, _ => Body(), caller.Token).GetAsyncEnumerator();
        var move = iterator.MoveNextAsync().AsTask();
        try
        {
            var observed = await Record.ExceptionAsync(() => move.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.NotNull(observed);
            Assert.IsNotType<TimeoutException>(observed);
            Assert.Contains(Causes(observed!), error => ReferenceEquals(error, cause));
            Assert.False(called);
        }
        finally
        {
            caller.Cancel();
            _ = await Record.ExceptionAsync(() => move);
            var actualDispose = iterator.DisposeAsync().AsTask();
            _ = await Record.ExceptionAsync(() => actualDispose);
            await CloseKnownAsync(h.Runtime, cause);
        }
        async IAsyncEnumerable<int> Body()
        {
            called = true;
            await Task.Yield();
            yield return 1;
        }
    }

    [Fact]
    public async Task Actual_HttpClient_timeout_retains_exact_body_cause_even_when_returned_frame_is_canceled()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        using var http = new HttpClient(new WaitForActualCancellationHandler()) { Timeout = TimeSpan.FromMilliseconds(40) };
        Task<string>? raw = null;
        Exception? originalTimeout = null;
        var frame = h.Runtime.StartOriginalFrameAsync(h.Admission, async token =>
        {
            raw = http.GetStringAsync("https://synthetic.invalid/timeout", token);
            try { return await raw; }
            catch (Exception error) { originalTimeout = error; throw; }
        }, default);
        _ = await Record.ExceptionAsync(() => frame);
        Assert.NotNull(raw);
        Assert.True(raw.IsCanceled);
        Assert.True(frame.IsCanceled);
        var exact = Assert.IsType<TaskCanceledException>(originalTimeout);
        Assert.IsType<TimeoutException>(exact.InnerException);
        var observation = h.Runtime.TryObserveProviderFailure(h.Admission, frame);
        Assert.NotNull(observation);
        Assert.Same(frame, observation.OriginalFrame);
        Assert.Same(exact, observation.OriginalCause);
        Assert.True(h.Runtime.ValidateProviderFailureObservation(observation, h.Admission));
        await CloseKnownAsync(h.Runtime, exact); // Unacknowledged timeout remains blocking; observation alone is no waiver.
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Multiple_healthy_provider_and_tool_frames_share_one_original_lease_and_retire_once()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        var model = h.Runtime.StartOriginalFrameAsync(h.Admission, _ => Task.FromResult("first"), default);
        Assert.Equal("first", await model);
        var tool = h.Runtime.StartOriginalToolFrameAsync(h.Admission, _ => Task.FromResult("tool"), default);
        Assert.Equal("tool", await tool);
        var next = h.Runtime.StartOriginalFrameAsync(h.Admission, _ => Task.FromResult("continued"), default);
        Assert.Equal("continued", await next);
        Assert.Equal(0, h.Lease.Disposes);
        Assert.Equal(3, h.Lease.Revalidations);
        var close = h.Runtime.AwaitSettlementAsync(h.Admission.Snapshot.TaskId, h.Admission.Snapshot.ExecutionId, h.Admission.AttemptId, default);
        Assert.Same(close, h.Runtime.AwaitSettlementAsync(h.Admission.Snapshot.TaskId, h.Admission.Snapshot.ExecutionId, h.Admission.AttemptId, default));
        await close;
        await h.Runtime.CloseAndDrainAsync();
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Held_last_finally_prevents_settlement_and_new_frame_admission_before_lease_disposal()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frame = h.Runtime.StartOriginalFrameAsync(h.Admission, async _ =>
        {
            try { return "result"; }
            finally { entered.TrySetResult(); await release.Task; }
        }, default);
        Task? close = null;
        try
        {
            await entered.Task;
            close = h.Runtime.AwaitSettlementAsync(h.Admission.Snapshot.TaskId, h.Admission.Snapshot.ExecutionId, h.Admission.AttemptId, default);
            Assert.False(frame.IsCompleted);
            Assert.False(close.IsCompleted);
            Assert.Equal(0, h.Lease.Disposes);
            Assert.Throws<InvalidOperationException>(() => { _ = h.Runtime.StartOriginalFrameAsync(h.Admission, _ => Task.FromResult("late"), default); });
        }
        finally
        {
            release.TrySetResult();
            await frame;
            if (close is not null) await close;
            await h.Runtime.CloseAndDrainAsync();
        }
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Actual_compound_body_siblings_cannot_be_single_provider_failure_proof()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        var first = new IOException("First original provider sibling");
        var second = new IOException("Second original provider sibling");
        var frame = h.Runtime.StartOriginalFrameAsync(h.Admission,
            _ => Task.WhenAll(Task.FromException<string>(first), Task.FromException<string>(second)), default);
        _ = await Record.ExceptionAsync(() => frame);
        Assert.Null(h.Runtime.TryObserveProviderFailure(h.Admission, frame));
        Assert.Throws<UnauthorizedAccessException>(() => h.Runtime.CreateProviderFailureObservation(h.Admission with { }, frame, first));
        await CloseKnownAsync(h.Runtime, first, second);
    }

    [Fact]
    public async Task Successful_tool_result_keeps_known_effect_and_exact_runtime_error_blocking_after_another_healthy_frame()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        var originalError = new IOException("Original history observation failed after effect");
        var returned = new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), "synthetic known effect", "not authority",
            false, TimeSpan.Zero, DateTimeOffset.UnixEpoch), "actual typed result") { OriginalRuntimeError = originalError };
        var result = new TaskRunToolActionResult(returned, "synthetic-original-owner-receipt", false, false);
        var tool = h.Runtime.StartOriginalToolFrameAsync(h.Admission, _ => Task.FromResult(result), default);
        Assert.Same(result, await tool);
        Assert.True(tool.IsCompletedSuccessfully);
        Assert.Same(returned, result.OriginalResult);
        Assert.Same(originalError, result.OriginalResult.OriginalRuntimeError);
        var healthy = h.Runtime.StartOriginalFrameAsync(h.Admission, _ => Task.FromResult("next healthy model"), default);
        Assert.Equal("next healthy model", await healthy);
        Assert.Null(h.Runtime.TryObserveProviderFailure(h.Admission, tool));
        await CloseKnownAsync(h.Runtime, originalError);
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Held_original_resource_dispose_keeps_frame_and_attempt_unsettled_until_same_cleanup_finishes()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new OriginalResource(release.Task, entered);
        var actualAcquire = Task.FromResult(resource);
        var actualRevalidation = Task.CompletedTask;
        Task<string>? frame = null; Task? settlement = null;
        var errors = new List<Exception>(); var bodyFinished = false;
        try
        {
            frame = h.Runtime.StartOriginalResourceFrameAsync(h.Admission, _ => actualAcquire,
                (same, _) => { Assert.Same(resource, same); return actualRevalidation; },
                _ => { bodyFinished = true; return Task.FromResult("actual body result"); }, default);
            await Task.WhenAny(entered.Task, frame);
            if (!entered.Task.IsCompleted) await frame;
            Assert.True(bodyFinished);
            Assert.False(frame.IsCompleted);
            Assert.Equal(1, resource.DisposeCalls);
            settlement = h.Runtime.AwaitSettlementAsync(h.Admission.Snapshot.TaskId,
                h.Admission.Snapshot.ExecutionId, h.Admission.AttemptId, default);
            Assert.False(settlement.IsCompleted);
            Assert.Equal(0, h.Lease.Disposes);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (frame is not null) await CollectNewOriginalAsync(frame, errors, []);
            if (settlement is not null) await CollectNewOriginalAsync(settlement, errors, []);
            await CollectNewOriginalAsync(h.Runtime.CloseAndDrainAsync(), errors, []);
        }
        ThrowNewControlErrors(errors);
        Assert.Equal(1, resource.DisposeCalls);
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Actual_compound_resource_dispose_siblings_block_raw_provider_failure_acknowledgment()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        var bodyCause = new HttpRequestException("actual synthetic provider quota");
        var firstCleanup = new IOException("first actual resource cleanup sibling");
        var secondCleanup = new IOException("second actual resource cleanup sibling");
        var actualDispose = Task.WhenAll(Task.FromException(firstCleanup), Task.FromException(secondCleanup));
        var resource = new OriginalResource(actualDispose);
        var actualAcquire = Task.FromResult(resource);
        var actualBody = Task.FromException<string>(bodyCause);
        Task<string>? frame = null;
        var errors = new List<Exception>();
        Exception[] expected = [bodyCause, firstCleanup, secondCleanup];
        try
        {
            frame = h.Runtime.StartOriginalResourceFrameAsync(h.Admission, _ => actualAcquire,
                (_, _) => Task.CompletedTask, _ => actualBody, default);
            var returned = await Record.ExceptionAsync(() => frame);
            Assert.NotNull(returned);
            var actual = Causes(returned!).ToArray();
            Assert.Contains(actual, error => ReferenceEquals(error, bodyCause));
            Assert.Contains(actual, error => ReferenceEquals(error, firstCleanup));
            Assert.Contains(actual, error => ReferenceEquals(error, secondCleanup));
            Assert.True(actualBody.IsFaulted);
            Assert.True(actualDispose.IsFaulted);
            Assert.Null(h.Runtime.TryObserveProviderFailure(h.Admission, frame));
            Assert.Throws<InvalidOperationException>(() => h.Runtime.CreateProviderFailureObservation(h.Admission, frame, bodyCause));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (frame is not null) await CollectNewOriginalAsync(frame, errors, expected);
            await CollectNewOriginalAsync(h.Runtime.CloseAndDrainAsync(), errors, expected);
        }
        ThrowNewControlErrors(errors);
        Assert.Equal(1, resource.DisposeCalls);
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Actual_canceled_resource_revalidation_never_enters_raw_body_and_retains_distinct_cleanup()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        var actualRevalidation = Task.FromCanceled(new CancellationToken(canceled: true));
        var cleanupCause = new IOException("actual cleanup after canceled resource revalidation");
        var actualDispose = Task.FromException(cleanupCause);
        var resource = new OriginalResource(actualDispose);
        var actualAcquire = Task.FromResult(resource);
        Task<string>? frame = null; var bodyCalled = false;
        var errors = new List<Exception>(); var expected = new List<Exception> { cleanupCause };
        try
        {
            frame = h.Runtime.StartOriginalResourceFrameAsync(h.Admission, _ => actualAcquire,
                (_, _) => actualRevalidation,
                _ => { bodyCalled = true; return Task.FromResult("must remain absent"); }, default);
            var returned = await Record.ExceptionAsync(() => frame);
            Assert.NotNull(returned);
            var actual = Causes(returned!).ToArray();
            var retainedCancellation = Assert.Single(actual.OfType<TaskCanceledException>());
            expected.Add(retainedCancellation); // SAME cause from this exact frame; never a type-wide cleanup exemption.
            Assert.Contains(actual, error => ReferenceEquals(error, cleanupCause));
            Assert.True(actualRevalidation.IsCanceled);
            Assert.False(bodyCalled);
            Assert.True(frame.IsFaulted); // Canceled prerequisite plus distinct cleanup is a compound failure.
            Assert.Null(h.Runtime.TryObserveProviderFailure(h.Admission, frame));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (frame is not null) await CollectNewOriginalAsync(frame, errors, expected);
            await CollectNewOriginalAsync(h.Runtime.CloseAndDrainAsync(), errors, expected);
        }
        ThrowNewControlErrors(errors);
        Assert.Equal(1, resource.DisposeCalls);
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Typed_resource_body_receives_same_actual_acquired_object_and_returns_original_raw_task_result()
    {
        var h = Harness.Create();
        await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        var resource = new OriginalResource(Task.CompletedTask);
        var acquire = Task.FromResult(resource);
        var raw = Task.FromResult("actual raw result");
        Task<string>? frame = null; var errors = new List<Exception>();
        OriginalResource? delivered = null;
        try
        {
            frame = h.Runtime.StartOriginalResourceFrameAsync(h.Admission, _ => acquire,
                (same, _) => { Assert.Same(resource, same); return Task.CompletedTask; },
                (same, _) => { delivered = same; Assert.Same(resource, same); return raw; }, default);
            Assert.Equal("actual raw result", await frame);
            Assert.Same(resource, delivered);
            Assert.True(raw.IsCompletedSuccessfully);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (frame is not null) await CollectNewOriginalAsync(frame, errors, []);
            await CollectNewOriginalAsync(h.Runtime.CloseAndDrainAsync(), errors, []);
        }
        ThrowNewControlErrors(errors);
        Assert.Equal(1, resource.DisposeCalls);
        Assert.Equal(1, h.Lease.Disposes);
    }

    private static async Task CollectNewOriginalAsync(Task actualOriginal, List<Exception> errors, IReadOnlyList<Exception> expected)
    {
        var failure = await Record.ExceptionAsync(() => actualOriginal);
        if (failure is null) return;
        var causes = Causes(failure).ToArray();
        if (causes.Length == 0 || causes.Any(cause => !expected.Any(known => ReferenceEquals(cause, known))))
            errors.Add(failure);
    }
    private static void ThrowNewControlErrors(List<Exception> errors)
    {
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }
    private sealed class OriginalResource(Task actualDispose, TaskCompletionSource? entered = null) : IAsyncDisposable
    {
        public int DisposeCalls { get; private set; }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            entered?.TrySetResult();
            return new(actualDispose); // The SAME original task is captured once by the production owner.
        }
    }

    private static IEnumerable<Exception> Causes(Exception error) => error is AggregateException group
        ? group.InnerExceptions.SelectMany(Causes) : [error];
    private static async Task CloseKnownAsync(TaskRunOriginalFrameOwner runtime, params Exception[] known)
    {
        var original = runtime.CloseAndDrainAsync();
        var failure = await Record.ExceptionAsync(() => original);
        Assert.NotNull(failure);
        var actual = Causes(failure!).ToArray();
        Assert.NotEmpty(actual);
        Assert.All(actual, error => Assert.Contains(known, expected => ReferenceEquals(error, expected)));
    }

    private sealed class WaitForActualCancellationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(HttpStatusCode.OK);
        }
    }
    private sealed class Harness
    {
        public required TaskRunOriginalFrameOwner Runtime { get; init; }
        public required TaskRunAttemptAdmission Admission { get; init; }
        public required Lease Lease { get; init; }
        public Exception? LookupFailure;
        public static Harness Create()
        {
            var task = Guid.NewGuid(); var context = Guid.NewGuid(); var run = Guid.NewGuid(); var attempt = Guid.NewGuid();
            var binding = new TaskExecutionOwnerBinding(task, context, run, "synthetic-actor", "synthetic-profile", null, null, "synthetic-auth", "synthetic-receipt");
            var snapshot = new TaskExecutionSnapshot(task, context, run, "synthetic", TaskExecutionLifecycle.Running,
                TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
                { OwnerBinding = binding, PersistenceRevision = 1 };
            var lease = new Lease(binding, attempt); var admission = new TaskRunAttemptAdmission(snapshot, attempt, lease);
            Harness? h = null;
            var runtime = new TaskRunOriginalFrameOwner((actualTask, actualRun, actualAttempt, _) =>
                h!.LookupFailure is { } error ? Task.FromException<TaskRunAttemptAdmission?>(error)
                    : Task.FromResult<TaskRunAttemptAdmission?>(actualTask == task && actualRun == run && actualAttempt == attempt ? admission : null));
            return h = new() { Runtime = runtime, Admission = admission, Lease = lease };
        }
    }
    private sealed class Lease(TaskExecutionOwnerBinding owner, Guid attempt) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => attempt;
        public TaskRunRouteCandidate Candidate => new("synthetic-route", 1, "synthetic-provider", "synthetic-model", null, false, []);
        public string ReceiptReference => "synthetic-issued-lease";
        public int Revalidations;
        public int Disposes;
        public ValueTask RevalidateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(0, Disposes);
            Revalidations++;
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }
}
