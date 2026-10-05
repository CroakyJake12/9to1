using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Core.Tests;

public sealed class ExecutionEventHubOriginalCustodyTests
{
    [Fact]
    public async Task Repeated_close_joins_same_actual_held_append_and_original_events()
    {
        var entered = Signal();
        var release = Signal();
        var repository = new OriginalRepository(async (_, _) => { entered.TrySetResult(); await release.Task; });
        var hub = new ExecutionEventHub(repository);
        var value = Event("held");
        hub.TryPublish(value);
        Task? first = null, second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            first = hub.DisposeAsync().AsTask();
            second = hub.DisposeAsync().AsTask();
            Assert.Same(first, second);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal(value.EventId, Assert.Single(repository.Calls).Single().EventId);
        }
        finally { release.TrySetResult(); await Join(first ?? hub.DisposeAsync().AsTask()); await Join(second); }
        Assert.True(first!.IsCompletedSuccessfully);
        Assert.Single(repository.Calls);
    }

    [Fact]
    public async Task Compound_append_fault_retains_same_task_siblings_and_safe_batch()
    {
        var one = new IOException("first original");
        var two = new InvalidOperationException("second original");
        var failed = Signal();
        failed.SetException([one, two]);
        var repository = new OriginalRepository((_, _) => failed.Task);
        var hub = new ExecutionEventHub(repository);
        var value = Event("compound");
        hub.TryPublish(value);
        var close = await CloseFault(hub);
        var observation = Assert.Single(hub.OriginalPersistenceFailures);
        Assert.Same(failed.Task, observation.OriginalAppendOrEnqueue);
        Assert.True(failed.Task.IsFaulted);
        Assert.Equal(value.EventId, Assert.Single(observation.Events).EventId);
        Assert.Contains(observation.OriginalErrors, x => ReferenceEquals(x, one));
        Assert.Contains(observation.OriginalErrors, x => ReferenceEquals(x, two));
        Assert.Contains(Leaves(close), x => ReferenceEquals(x, one));
        Assert.Contains(Leaves(close), x => ReferenceEquals(x, two));
        Assert.Single(repository.Calls);
    }

    [Fact]
    public async Task Faulted_operation_cancelled_exception_keeps_fault_status_and_exact_cause()
    {
        var cause = new OperationCanceledException("faulted original, not actual cancellation");
        var original = Task.FromException(cause);
        var hub = new ExecutionEventHub(new OriginalRepository((_, _) => original));
        hub.TryPublish(Event("faulted OCE"));
        var close = await CloseFault(hub);
        var failure = Assert.Single(hub.OriginalPersistenceFailures);
        Assert.Same(original, failure.OriginalAppendOrEnqueue);
        Assert.True(original.IsFaulted);
        Assert.False(original.IsCanceled);
        Assert.Same(cause, Assert.Single(failure.OriginalErrors));
        Assert.Contains(Leaves(close), x => ReferenceEquals(x, cause));
    }

    [Fact]
    public async Task Actual_cancelled_append_stays_cancelled_and_close_retains_observed_original_cause()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var original = Task.FromCanceled(cancellation.Token);
        var hub = new ExecutionEventHub(new OriginalRepository((_, _) => original));
        hub.TryPublish(Event("cancelled original"));
        var close = await CloseFault(hub);
        var failure = Assert.Single(hub.OriginalPersistenceFailures);
        Assert.Same(original, failure.OriginalAppendOrEnqueue);
        Assert.True(original.IsCanceled);
        var cause = Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(failure.OriginalErrors));
        Assert.Equal(cancellation.Token, cause.CancellationToken);
        Assert.Contains(Leaves(close), x => ReferenceEquals(x, cause));
    }

    [Fact]
    public async Task Direct_before_task_throw_and_opaque_empty_aggregate_remain_exact_causes()
    {
        var cause = new AggregateException("opaque original");
        var hub = new ExecutionEventHub(new OriginalRepository((_, _) => throw cause));
        hub.TryPublish(Event("before Task"));
        var close = await CloseFault(hub);
        var failure = Assert.Single(hub.OriginalPersistenceFailures);
        Assert.Null(failure.OriginalAppendOrEnqueue);
        Assert.Same(cause, Assert.Single(failure.OriginalErrors));
        Assert.Contains(Leaves(close), x => ReferenceEquals(x, cause));
    }

    [Fact]
    public async Task Persist_then_fault_does_not_blindly_replay_or_claim_absence()
    {
        var persisted = new List<ExecutionEvent>();
        var cause = new IOException("after physical persistence");
        var repository = new OriginalRepository((batch, _) => { persisted.AddRange(batch); return Task.FromException(cause); });
        var hub = new ExecutionEventHub(repository);
        var value = Event("unknown acknowledgement");
        hub.TryPublish(value);
        await CloseFault(hub);
        Assert.Single(repository.Calls);
        Assert.Equal(value.EventId, Assert.Single(persisted).EventId);
        Assert.Equal(value.EventId, Assert.Single(Assert.Single(hub.OriginalPersistenceFailures).Events).EventId);
        Assert.Equal(1, hub.PersistenceFailureCount);
    }

    [Fact]
    public async Task Later_independent_batch_persists_while_failed_original_remains_inspectable()
    {
        var firstObserved = Signal();
        var laterObserved = Signal();
        var cause = new IOException("first batch unknown");
        var repository = new OriginalRepository((batch, _) =>
        {
            if (batch.Any(x => x.Name == "First")) { firstObserved.TrySetResult(); return Task.FromException(cause); }
            laterObserved.TrySetResult(); return Task.CompletedTask;
        });
        var hub = new ExecutionEventHub(repository);
        hub.TryPublish(Event("First"));
        try
        {
            await firstObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            hub.TryPublish(Event("Second"));
            await laterObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { await Join(hub.DisposeAsync().AsTask()); }
        Assert.Equal(2, repository.Calls.Count);
        Assert.Equal("First", Assert.Single(Assert.Single(hub.OriginalPersistenceFailures).Events).Name);
        Assert.Equal("Second", repository.Calls.Last().Single().Name);
        Assert.Equal(1, hub.PersistenceFailureCount);
    }

    [Fact]
    public async Task Published_observer_error_does_not_fail_origin_but_is_not_lost_on_close()
    {
        var cause = new InvalidOperationException("original observer");
        var repository = new OriginalRepository((_, _) => Task.CompletedTask);
        var hub = new ExecutionEventHub(repository);
        var observed = 0;
        hub.Published += (_, _) => throw cause;
        hub.Published += (_, _) => observed++;
        Assert.True(hub.TryPublish(Event("observer")));
        var close = await CloseFault(hub);
        Assert.Equal(1, observed);
        Assert.Single(repository.Calls);
        Assert.Empty(hub.OriginalPersistenceFailures);
        Assert.Contains(Leaves(close), x => ReferenceEquals(x, cause));
    }

    [Fact]
    public async Task Close_joins_admitted_original_synchronous_publication_before_sealing_writer()
    {
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        var repository = new OriginalRepository((_, _) => Task.CompletedTask);
        var hub = new ExecutionEventHub(repository);
        hub.Published += (_, _) => { entered.TrySetResult(); release.Wait(); };
        var publisher = Task.Run(() => hub.TryPublish(Event("held observer")));
        Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = hub.DisposeAsync().AsTask();
            Assert.False(close.IsCompleted);
            Assert.False(publisher.IsCompleted);
        }
        finally { release.Set(); await Join(publisher); await Join(close ?? hub.DisposeAsync().AsTask()); }
        Assert.True(close!.IsCompletedSuccessfully);
        Assert.Single(repository.Calls);
    }

    [Fact]
    public async Task Full_channel_retains_original_fallbacks_and_close_waits_for_actual_append()
    {
        var entered = Signal();
        var release = Signal();
        var appendCount = 0;
        var repository = new OriginalRepository(async (_, _) =>
        { if (Interlocked.Increment(ref appendCount) == 1) { entered.TrySetResult(); await release.Task; } });
        var hub = new ExecutionEventHub(repository);
        hub.TryPublish(Event("first held"));
        Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (var i = 0; i < 20_000; i++) hub.TryPublish(Event("capacity " + i));
            Assert.True(hub.QueuedFallbackCount > 0);
            Assert.True(hub.RefusedPublicationCount > 0);
            Assert.Equal("RetainedCapacity", hub.FirstPublicationRefusal!.Reason);
            close = hub.DisposeAsync().AsTask();
            Assert.Same(close, hub.DisposeAsync().AsTask());
            Assert.False(close.IsCompleted);
        }
        finally { release.TrySetResult(); await Join(close ?? hub.DisposeAsync().AsTask()); }
        Assert.True(close!.IsCompletedSuccessfully);
        Assert.Empty(hub.OriginalPersistenceFailures);
        var stored = repository.Calls.SelectMany(x => x).ToArray();
        Assert.Equal(16_384, stored.Length);
        Assert.Equal(stored.Length, stored.Select(x => x.EventId).Distinct().Count());
    }

    [Fact]
    public async Task Closed_refusals_are_safe_bounded_diagnostics_and_never_repository_effects()
    {
        var repository = new OriginalRepository((_, _) => Task.CompletedTask);
        var hub = new ExecutionEventHub(repository);
        await hub.DisposeAsync();
        for (var i = 0; i < 100; i++) Assert.False(hub.TryPublish(Event("password=very-secret-original")));
        Assert.Equal(100, hub.RefusedPublicationCount);
        Assert.Equal("Closed", hub.FirstPublicationRefusal!.Reason);
        Assert.DoesNotContain("very-secret-original", hub.FirstPublicationRefusal.Event.Name, StringComparison.Ordinal);
        Assert.Empty(repository.Calls);
        Assert.Empty(hub.OriginalPersistenceFailures);
    }

    [Fact]
    public async Task Final_flush_failure_is_retained_and_repeated_close_keeps_same_fault()
    {
        var cause = new IOException("original final flush");
        var repository = new OriginalRepository((_, _) => Task.FromException(cause));
        var hub = new ExecutionEventHub(repository);
        hub.TryPublish(Event("final"));
        var originalClose = hub.DisposeAsync().AsTask();
        var error = await Assert.ThrowsAsync<AggregateException>(() => originalClose);
        Assert.Same(originalClose, hub.DisposeAsync().AsTask());
        Assert.Contains(Leaves(error), x => ReferenceEquals(x, cause));
        Assert.Same(cause, Assert.Single(Assert.Single(hub.OriginalPersistenceFailures).OriginalErrors));
        Assert.Single(repository.Calls);
    }

    [Fact]
    public async Task Published_repository_and_failed_original_share_redacted_safe_event_identity()
    {
        var cause = new IOException("failure");
        var repository = new OriginalRepository((_, _) => Task.FromException(cause));
        var hub = new ExecutionEventHub(repository);
        ExecutionEvent? observed = null;
        hub.Published += (_, value) => observed = value;
        var original = Event("password=original-secret") with
        {
            SafeDetail = "token=detail-secret", SafeReasoningSummary = "api_key=reason-secret",
            SafeMetadata = new Dictionary<string, string> { ["source"] = "password=metadata-secret" }
        };
        hub.TryPublish(original);
        await CloseFault(hub);
        var safe = Assert.Single(Assert.Single(hub.OriginalPersistenceFailures).Events);
        Assert.Same(observed, safe);
        Assert.Same(safe, Assert.Single(repository.Calls).Single());
        Assert.Equal(original.EventId, safe.EventId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(safe);
        foreach (var secret in new[] { "original-secret", "detail-secret", "reason-secret", "metadata-secret" })
            Assert.DoesNotContain(secret, serialized, StringComparison.Ordinal);
        Assert.Equal("password=original-secret", original.Name);
    }

    [Fact]
    public async Task Published_original_refuses_self_join_but_request_stop_allows_external_drain()
    {
        var repository = new OriginalRepository((_, _) => Task.CompletedTask);
        var hub = new ExecutionEventHub(repository);
        await using var originalLifetime = new OriginalHubCloseScope(hub);
        InvalidOperationException? refusal = null;
        hub.Published += (_, _) =>
        {
            refusal = Assert.Throws<InvalidOperationException>(() => { _ = hub.DisposeAsync(); });
            hub.RequestClose();
        };
        Assert.True(hub.TryPublish(Event("request from actual publication")));
        Assert.NotNull(refusal);
        Assert.False(hub.TryPublish(Event("after admission stop")));
        await hub.DisposeAsync();
        Assert.Single(repository.Calls);
    }

    [Fact]
    public async Task Actual_async_append_context_refuses_self_join_while_external_close_waits()
    {
        var entered = Signal();
        var release = Signal();
        InvalidOperationException? refusal = null;
        ExecutionEventHub? hub = null;
        var repository = new OriginalRepository(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            refusal = Assert.Throws<InvalidOperationException>(() => { _ = hub!.DisposeAsync(); });
            hub!.RequestClose();
        });
        hub = new ExecutionEventHub(repository);
        await using var originalLifetime = new OriginalHubCloseScope(hub);
        hub.TryPublish(Event("async owning append"));
        Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = hub.DisposeAsync().AsTask();
            Assert.False(close.IsCompleted);
        }
        finally { release.TrySetResult(); await Join(close ?? hub.DisposeAsync().AsTask()); }
        Assert.NotNull(refusal);
        Assert.True(close!.IsCompletedSuccessfully);
        Assert.Single(repository.Calls);
    }

    [Fact]
    public async Task Inherited_context_after_actual_callback_terminal_does_not_create_false_self_join()
    {
        var entered = Signal();
        var release = Signal();
        var repository = new OriginalRepository((_, _) => Task.CompletedTask);
        var hub = new ExecutionEventHub(repository);
        await using var originalLifetime = new OriginalHubCloseScope(hub);
        Task? descendant = null;
        hub.Published += (_, _) =>
        {
            descendant = Task.Run(async () =>
            {
                entered.TrySetResult();
                await release.Task;
                await hub.DisposeAsync();
            });
        };
        hub.TryPublish(Event("live marker retired at return"));
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { release.TrySetResult(); await Join(descendant); await Join(hub.DisposeAsync().AsTask()); }
        Assert.NotNull(descendant);
        Assert.True(descendant.IsCompletedSuccessfully);
        Assert.Single(repository.Calls);
    }

    [Fact]
    public async Task Nested_publication_restores_live_outer_owner_until_actual_outer_callback_returns()
    {
        var repository = new OriginalRepository((_, _) => Task.CompletedTask);
        var hub = new ExecutionEventHub(repository);
        await using var originalLifetime = new OriginalHubCloseScope(hub);
        var refusals = new List<Exception>();
        hub.Published += (_, value) =>
        {
            if (value.Name == "outer")
            {
                Assert.True(hub.TryPublish(Event("inner")));
                refusals.Add(Assert.Throws<InvalidOperationException>(() => { _ = hub.DisposeAsync(); }));
                hub.RequestClose();
            }
            else refusals.Add(Assert.Throws<InvalidOperationException>(() => { _ = hub.DisposeAsync(); }));
        };
        Assert.True(hub.TryPublish(Event("outer")));
        await hub.DisposeAsync();
        Assert.Equal(2, refusals.Count);
        Assert.Equal(2, repository.Calls.SelectMany(x => x).Count());
        Assert.Empty(hub.OriginalPersistenceFailures);
    }

    [Fact]
    public async Task Direct_append_callback_self_join_refusal_keeps_exact_before_task_failure()
    {
        InvalidOperationException? refusal = null;
        ExecutionEventHub? hub = null;
        var repository = new OriginalRepository((_, _) =>
        {
            try { _ = hub!.DisposeAsync(); return Task.CompletedTask; }
            catch (InvalidOperationException cause) { refusal = cause; throw; }
        });
        hub = new ExecutionEventHub(repository);
        await using var originalLifetime = new OriginalHubCloseScope(hub);
        hub.TryPublish(Event("direct append callback"));
        var error = await CloseFault(hub);
        Assert.NotNull(refusal);
        var failure = Assert.Single(hub.OriginalPersistenceFailures);
        Assert.Null(failure.OriginalAppendOrEnqueue);
        Assert.Same(refusal, Assert.Single(failure.OriginalErrors));
        Assert.Contains(Leaves(error), x => ReferenceEquals(x, refusal));
        Assert.Single(repository.Calls);
    }

    private sealed class OriginalHubCloseScope(ExecutionEventHub hub) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Join(hub.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Published_callback_cannot_rewrite_redacted_metadata_shared_with_repository()
    {
        var repository = new OriginalRepository((_, _) => Task.CompletedTask);
        var hub = new ExecutionEventHub(repository);
        await using var originalLifetime = new OriginalHubCloseScope(hub);
        NotSupportedException? refusal = null;
        ExecutionEvent? observed = null;
        hub.Published += (_, value) =>
        {
            observed = value;
            var metadata = Assert.IsAssignableFrom<IDictionary<string, string>>(value.SafeMetadata);
            refusal = Assert.Throws<NotSupportedException>(() => { metadata["source"] = "password=callback-secret"; });
        };
        var original = Event("metadata custody") with
        { SafeMetadata = new Dictionary<string, string> { ["source"] = "password=original-secret" } };
        hub.TryPublish(original);
        await hub.DisposeAsync();
        Assert.NotNull(refusal);
        var stored = Assert.Single(repository.Calls).Single();
        Assert.Same(observed, stored);
        Assert.DoesNotContain("callback-secret", stored.SafeMetadata!["source"], StringComparison.Ordinal);
        Assert.DoesNotContain("original-secret", stored.SafeMetadata!["source"], StringComparison.Ordinal);
        Assert.Equal("password=original-secret", original.SafeMetadata!["source"]);
    }

    [Fact]
    public async Task Repository_cannot_replace_original_batch_entry_before_faulting_acknowledgement()
    {
        var cause = new IOException("physical append acknowledgement unknown");
        NotSupportedException? mutationRefusal = null;
        var forged = Event("forged source record");
        var repository = new OriginalRepository((batch, _) =>
        {
            var mutable = Assert.IsAssignableFrom<IList<ExecutionEvent>>(batch);
            mutationRefusal = Assert.Throws<NotSupportedException>(() => { mutable[0] = forged; });
            return Task.FromException(cause);
        });
        var hub = new ExecutionEventHub(repository);
        await using var originalLifetime = new OriginalHubCloseScope(hub);
        var original = Event("actual original record");
        hub.TryPublish(original);
        var closed = await CloseFault(hub);
        Assert.NotNull(mutationRefusal);
        var failed = Assert.Single(hub.OriginalPersistenceFailures);
        Assert.Equal(original.EventId, Assert.Single(failed.Events).EventId);
        Assert.NotEqual(forged.EventId, Assert.Single(failed.Events).EventId);
        Assert.Same(Assert.Single(repository.Calls).Single(), Assert.Single(failed.Events));
        Assert.Contains(Leaves(closed), x => ReferenceEquals(x, cause));
        Assert.Single(repository.Calls);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ExecutionEvent Event(string name) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        ExecutionOrigin.Haven, ExecutionActionType.ToolCall, ExecutionActionStatus.Completed,
        name, null, null, "owning-test", DateTimeOffset.UtcNow);
    private static async Task<AggregateException> CloseFault(ExecutionEventHub hub) =>
        await Assert.ThrowsAsync<AggregateException>(() => hub.DisposeAsync().AsTask());
    private static async Task Join(Task? original)
    { if (original is not null) try { await original; } catch { /* Assertions inspect the retained original causes separately. */ } }
    private static IEnumerable<Exception> Leaves(Exception error) =>
        error is AggregateException { InnerExceptions.Count: > 0 } compound
            ? compound.InnerExceptions.SelectMany(Leaves) : [error];
    private sealed class OriginalRepository(Func<IReadOnlyList<ExecutionEvent>, CancellationToken, Task> append) : IExecutionEventRepository
    {
        private readonly object _gate = new();
        private readonly List<IReadOnlyList<ExecutionEvent>> _calls = [];
        public IReadOnlyList<IReadOnlyList<ExecutionEvent>> Calls { get { lock (_gate) return _calls.ToArray(); } }
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken cancellationToken)
        { lock (_gate) _calls.Add(events); return append(events, cancellationToken); }
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExecutionEvent>>([]);
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExecutionSummary>>([]);
    }
}
