using System.Runtime.CompilerServices;
using Dulche.Runtime;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Managed producer controls. These are not installed model/Windows execution evidence.</summary>
public sealed class EndpointLifetimeTests
{
    [Fact]
    public Task Stop_joins_the_actual_worker_ownership_finally_after_request_result_is_available() => Control(async owner =>
    {
        var request = await owner.BeginWithTemporaryOwnership();
        owner.Adapter.GenerationReturn.TrySetResult();
        await owner.Adapter.ReleaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var result = await request.AwaitResult(CancellationToken.None);
        Assert.Equal(RequestState.Completed, result.Status);
        var original = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        await owner.Adapter.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(original.IsCompleted);
        Assert.Same(original, owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId));
        Assert.Equal(EndpointState.Stopping, owner.Runtime.InspectEndpoint(owner.Endpoint.EndpointId).Value!.State);
        owner.Adapter.ReleaseReturn.TrySetResult();
        var stopped = await original;
        Assert.True(stopped.Succeeded);
        Assert.Equal(EndpointState.Stopped, stopped.Value!.State);
        Assert.Equal(1, owner.Adapter.StopCount);
        Assert.True(owner.Adapter.ReleaseOriginal.IsCompletedSuccessfully);
        Assert.Same(original, owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId));
    });

    [Fact]
    public Task Original_stop_is_published_before_actual_cancel_and_stop_callbacks_reenter() => Control(async owner =>
    {
        await owner.BeginWithTemporaryOwnership();
        Task<OperationResult<DulcheEndpoint>>? cancelReentry = null, stopReentry = null;
        owner.Adapter.OnCancel = () => cancelReentry = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        owner.Adapter.OnStop = () => stopReentry = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        var original = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        await owner.Adapter.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(original, cancelReentry);
        Assert.Same(original, stopReentry);
        Assert.False(original.IsCompleted);
        owner.Adapter.GenerationReturn.TrySetResult();
        owner.Adapter.ReleaseReturn.TrySetResult();
        Assert.True((await original).Succeeded);
        Assert.Equal(1, owner.Adapter.CancelCount);
        Assert.Equal(1, owner.Adapter.StopCount);
    });

    [Fact]
    public Task Sealed_endpoint_refuses_submit_queue_and_both_replacement_paths_without_new_dispatch() => Control(async owner =>
    {
        var request = await owner.BeginWithTemporaryOwnership();
        var queued = owner.Runtime.Submit(new("queued"), owner.Endpoint.EndpointId).Value!;
        var original = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        await owner.Adapter.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DulcheErrorCode.InvalidState, owner.Runtime.Submit(new("late"), owner.Endpoint.EndpointId).Error!.Code);
        Assert.Equal(DulcheErrorCode.InvalidState, owner.Runtime.QueuePrompt(request.RequestId, "late continuation").Error!.Code);
        Assert.Equal(DulcheErrorCode.InvalidState, (await owner.Runtime.ReplacePromptAsync(queued.RequestId, "late queued replacement")).Error!.Code);
        Assert.Equal(DulcheErrorCode.InvalidState, (await owner.Runtime.ReplacePromptAsync(request.RequestId, "late active replacement")).Error!.Code);
        Assert.Equal(1, owner.Adapter.GenerateCount);
        owner.Adapter.GenerationReturn.TrySetResult();
        owner.Adapter.ReleaseReturn.TrySetResult();
        Assert.True((await original).Succeeded);
        Assert.Equal(1, owner.Adapter.GenerateCount);
        Assert.Empty(owner.Runtime.GetQueueSnapshot(owner.Endpoint.EndpointId).Value!.Queued);
    });

    [Fact]
    public Task Caller_wait_cancellation_does_not_cancel_or_replace_the_retained_stop() => Control(async owner =>
    {
        await owner.BeginWithTemporaryOwnership();
        var original = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        await owner.Adapter.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var caller = new CancellationTokenSource();
        var wait = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId, caller.Token);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.False(original.IsCompleted);
        Assert.Same(original, owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId));
        owner.Adapter.GenerationReturn.TrySetResult();
        owner.Adapter.ReleaseReturn.TrySetResult();
        Assert.True((await original).Succeeded);
        Assert.Equal(1, owner.Adapter.StopCount);
    });

    [Fact]
    public Task Cancel_stop_and_compound_original_cleanup_errors_all_remain_owned_by_the_same_close() => Control(async owner =>
    {
        var cancelCause = new IOException("actual controlled cancel failure");
        var stopCause = new IOException("actual controlled stop failure");
        var releaseOne = new IOException("first original release failure");
        var releaseTwo = new IOException("second original release failure");
        owner.Adapter.CancelOriginal = Task.FromException<OperationResult<Unit>>(cancelCause);
        owner.Adapter.StopOriginal = Task.FromException<OperationResult<Unit>>(stopCause);
        var release = Task.WhenAll(Task.FromException(releaseOne), Task.FromException(releaseTwo));
        owner.Adapter.ReleaseOriginal = release;
        await owner.BeginWithTemporaryOwnership();
        var original = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        await owner.Adapter.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(original.IsCompleted);
        owner.Adapter.GenerationReturn.TrySetResult();
        var observed = await Assert.ThrowsAsync<AggregateException>(() => original);
        var all = Walk(observed).ToArray();
        Assert.Contains(all, error => ReferenceEquals(error, cancelCause));
        Assert.Contains(all, error => ReferenceEquals(error, stopCause));
        Assert.Contains(all, error => ReferenceEquals(error, releaseOne));
        Assert.Contains(all, error => ReferenceEquals(error, releaseTwo));
        Assert.Same(release, owner.Adapter.ReleaseOriginal);
        Assert.Equal(2, release.Exception!.InnerExceptions.Count);
        Assert.Same(original, owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId));
        Assert.Equal(EndpointState.Failed, owner.Runtime.InspectEndpoint(owner.Endpoint.EndpointId).Value!.State);
        owner.ExpectedFaultedClose = original; // Only after exact original cause/whole task assertions above.
    });

    [Fact]
    public Task Cancellation_before_first_stop_has_no_effect_and_an_independent_owner_can_still_retire() => Control(async owner =>
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var withheld = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId, caller.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => withheld);
        Assert.Equal(0, owner.Adapter.StopCount);
        Assert.Equal(EndpointState.Ready, owner.Runtime.InspectEndpoint(owner.Endpoint.EndpointId).Value!.State);
        var original = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        Assert.True((await original).Succeeded);
        Assert.Same(original, owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId));
        Assert.Equal(0, owner.Adapter.GenerateCount);
        Assert.Equal(1, owner.Adapter.StopCount);
    });

    [Fact]
    public Task Original_worker_fault_cannot_disappear_through_re_admission_or_restart() => Control(async owner =>
    {
        var failure = new IOException("exact original worker cleanup fault");
        owner.Adapter.ReleaseOriginal = Task.FromException(failure);
        var handle = await owner.BeginWithTemporaryOwnership();
        owner.Adapter.GenerationReturn.TrySetResult();
        await owner.Adapter.ReleaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(owner.Runtime.TryObserveOriginalRequestWork(handle, out var processing, out var worker, out var cleanup));
        Assert.NotNull(processing); Assert.NotNull(worker);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => processing!));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => worker!));
        Assert.Same(owner.Adapter.ReleaseOriginal, Assert.Single(cleanup));
        Assert.Equal(DulcheErrorCode.InvalidState, owner.Runtime.Submit(new("cannot restart after fault"), owner.Endpoint.EndpointId).Error!.Code);
        var close = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => close));
        Assert.Same(close, owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId));
        Assert.Equal(1, owner.Adapter.GenerateCount);
        owner.ExpectedFaultedClose = close;
    });

    [Fact]
    public Task Replaced_logical_id_keeps_exact_old_attempt_processing_and_cleanup_refs() => Control(async owner =>
    {
        var old = await owner.BeginWithTemporaryOwnership();
        owner.Adapter.GenerationReturn.TrySetResult();
        await owner.Adapter.ReleaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await old.AwaitResult(CancellationToken.None);
        Assert.True(owner.Runtime.TryObserveOriginalRequestWork(old, out var oldProcessing, out _, out var oldCleanup));
        Assert.NotNull(oldProcessing); Assert.False(oldProcessing.IsCompleted);
        Assert.Same(owner.Adapter.ReleaseOriginal, Assert.Single(oldCleanup));
        var next = (await owner.Runtime.ReplacePromptAsync(old.RequestId, "next owned attempt")).Value!;
        Assert.Equal(old.RequestId, next.RequestId); Assert.NotSame(old, next);
        Assert.False(owner.Runtime.TryObserveOriginalRequestWork(old with { }, out _, out _, out _));
        var close = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId);
        await owner.Adapter.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(close.IsCompleted);
        Assert.True(owner.Runtime.TryObserveOriginalRequestWork(next, out var nextProcessing, out _, out var nextCleanup));
        Assert.NotNull(nextProcessing); await nextProcessing;
        Assert.Empty(nextCleanup);
        Assert.False(oldProcessing.IsCompleted);
        owner.Adapter.ReleaseReturn.TrySetResult();
        Assert.True((await close).Succeeded);
        Assert.True(oldProcessing.IsCompletedSuccessfully);
        Assert.Equal(1, owner.Adapter.GenerateCount);
        Assert.True(owner.Runtime.TryObserveOriginalRequestWork(old, out var sameOldProcessing, out _, out var sameOldCleanup));
        Assert.Same(oldProcessing, sameOldProcessing);
        Assert.Same(owner.Adapter.ReleaseOriginal, Assert.Single(sameOldCleanup));
    });

    private static IEnumerable<Exception> Walk(Exception original)
    {
        yield return original;
        if (original is AggregateException group)
            foreach (var child in group.InnerExceptions)
                foreach (var nested in Walk(child)) yield return nested;
    }

    private static async Task Control(Func<Owner, Task> body)
    {
        var errors = new List<Exception>();
        var owner = new Owner();
        try
        {
            await owner.Initialize();
            await body(owner);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            owner.Adapter.GenerationReturn.TrySetResult();
            owner.Adapter.ReleaseReturn.TrySetResult();
            if (owner.Endpoint is not null)
            {
                Task<OperationResult<DulcheEndpoint>>? actual = null;
                try { actual = owner.Runtime.StopEndpointAsync(owner.Endpoint.EndpointId); }
                catch (Exception error) { errors.Add(error); }
                if (actual is not null)
                {
                    try { await actual.WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (Exception error)
                    {
                        if (!ReferenceEquals(actual, owner.ExpectedFaultedClose))
                        {
                            if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error);
                            if (actual.Exception is { } compound)
                                foreach (var member in compound.InnerExceptions)
                                    if (!errors.Any(item => ReferenceEquals(item, member))) errors.Add(member);
                        }
                    }
                }
            }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }

    private sealed class Owner
    {
        public readonly Adapter Adapter = new();
        public DulcheRuntime Runtime = null!;
        public DulcheEndpoint Endpoint = null!;
        public Task? ExpectedFaultedClose;
        public async Task Initialize()
        {
            Runtime = new([Adapter], acquisitionAdapters: [Adapter]);
            Endpoint = (await Runtime.StartLocalAsync(Adapter.ProviderId, 9511)).Value!;
        }
        public async Task<RuntimeRequestHandle> BeginWithTemporaryOwnership()
        {
            var handle = Runtime.Submit(new("managed original"), Endpoint.EndpointId).Value!;
            await Adapter.GenerationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var artifact = new ModelArtifact(new(Adapter.ProviderId, "controlled-model"), "fixture", "fixture", null, null, null, null, ModelState.Verified);
            Assert.True((await Runtime.PullModelAsync(Adapter.ProviderId, artifact, handle.RequestId)).Succeeded);
            return handle;
        }
    }

    private sealed class Adapter : IDulcheAdapter, IDulcheAcquisitionAdapter
    {
        public string ProviderId => "managed-original-control";
        public string RuntimeVersion => "managed-fixture";
        public bool IsLocal => true;
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>();
        public readonly TaskCompletionSource GenerationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource GenerationReturn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseReturn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource StopCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ReleaseOriginal;
        public Task<OperationResult<Unit>> CancelOriginal = Task.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public Task<OperationResult<Unit>> StopOriginal = Task.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public Action? OnCancel, OnStop;
        public int GenerateCount, CancelCount, StopCount;
        public Adapter() => ReleaseOriginal = ReleaseReturn.Task;
        public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public async IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint, DulcheRequest request, string requestId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref GenerateCount); GenerationEntered.TrySetResult();
            await GenerationReturn.Task.ConfigureAwait(false); // Real held managed producer intentionally ignores cancellation.
            yield return new(Text: "original controlled result");
        }
        public async IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint, DulcheRequest request, string requestId, ToolInvocationResult result, [EnumeratorCancellation] CancellationToken cancellationToken)
        { await Task.Yield(); yield break; }
        public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken)
        { Interlocked.Increment(ref CancelCount); OnCancel?.Invoke(); return new(CancelOriginal); }
        public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken)
        { Interlocked.Increment(ref StopCount); OnStop?.Invoke(); StopCalled.TrySetResult(); return new(StopOriginal); }
        public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken) => ValueTask.FromResult(new RuntimeHealth(endpoint.EndpointId, endpoint.State, DateTimeOffset.UtcNow, null, Metric<double>.Na, Metric<double>.Na, Metric<double>.Na));
        public Task<OperationResult<AcquisitionProgress>> PullAsync(ModelArtifact model, string ownerScopeId, IProgress<AcquisitionProgress>? progress, CancellationToken cancellationToken) => Task.FromResult(OperationResult<AcquisitionProgress>.Success(new("controlled-pull", model.Identity, ModelState.Verified, null, null, null, null, null, null)));
        public Task ReleaseTemporaryOwnershipAsync(ModelIdentity model, string ownerScopeId, CancellationToken cancellationToken)
        { ReleaseEntered.TrySetResult(); return ReleaseOriginal; }
        public Task<IReadOnlyList<ModelArtifact>> ListModelsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<ModelArtifact>> FindModelAsync(string query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<ModelArtifact>> InstallAsync(ModelArtifact model, string installLocation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<ModelReplaceResult>> ReplaceAsync(ModelArtifact oldModel, ModelArtifact replacement, string policy, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<ModelArtifact>> RollbackAsync(string rollbackId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
