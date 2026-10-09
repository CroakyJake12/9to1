using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Managed controls exercise the ACTUAL component used by NewChatPage and
/// its original iterator Tasks. They do not claim native page, actor, or shell acceptance.</summary>
public sealed class NewChatOriginalWorkLifetimeTests
{
    [Fact]
    public async Task Close_starts_scoped_stop_then_joins_same_held_MoveNext_and_Dispose_once()
    {
        var cleanup = 0;
        var stop = 0;
        var canceled = false;
        var stream = new HeldOriginalStream();
        var owner = new NewChatOriginalWorkLifetime(() => { ++stop; return Task.CompletedTask; },
            () => { ++cleanup; return Task.CompletedTask; });
        Task? callbackClose = null;
        var actual = owner.RunAsync(async original =>
        {
            using var registration = original.Token.Register(() =>
            { canceled = true; callbackClose = owner.OriginalClose; });
            await original.ReadStreamAsync(stream, _ => Task.CompletedTask);
        });
        await stream.MoveAcquired.Task;
        var close = owner.CloseAndDrainAsync();
        Assert.Same(close, owner.CloseAndDrainAsync());
        Assert.Same(close, owner.OriginalClose);
        Assert.Same(close, callbackClose);
        Assert.True(canceled);
        Assert.Equal(1, stop);
        Assert.False(close.IsCompleted);
        Assert.Equal(0, cleanup);
        stream.Move.SetResult(false);
        await stream.DisposeAcquired.Task;
        Assert.False(actual.IsCompleted);
        Assert.False(close.IsCompleted);
        Assert.Equal(1, stream.DisposeCalls);
        stream.Dispose.SetResult();
        await actual;
        await close;
        Assert.Equal(1, cleanup);
        Assert.Equal(1, stream.MoveCalls);
        Assert.Equal(1, stream.DisposeCalls);
    }

    [Fact]
    public async Task All_direct_MoveNext_faults_and_independent_Dispose_fault_remain_exact()
    {
        var first = new IOException("original read");
        var sibling = new UnauthorizedAccessException("same read sibling");
        var cleanup = new InvalidOperationException("actual iterator dispose");
        var stream = new HeldOriginalStream();
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var actual = owner.RunAsync(original => original.ReadStreamAsync(stream, _ => Task.CompletedTask));
        await stream.MoveAcquired.Task;
        stream.Move.SetException([first, sibling]);
        await stream.DisposeAcquired.Task;
        Assert.False(actual.IsCompleted);
        stream.Dispose.SetException(cleanup);
        var operation = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Collection(operation.InnerExceptions, item => Assert.Same(first, item),
            item => Assert.Same(sibling, item), item => Assert.Same(cleanup, item));
        var close = await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAndDrainAsync());
        Assert.Collection(close.InnerExceptions, item => Assert.Same(first, item),
            item => Assert.Same(sibling, item), item => Assert.Same(cleanup, item));
        Assert.Equal(1, stream.DisposeCalls);
    }

    [Fact]
    public async Task Existing_handled_policy_can_return_success_without_erasing_its_real_faults()
    {
        var first = new IOException("handled read");
        var sibling = new InvalidOperationException("hidden direct sibling");
        var read = new TaskCompletionSource();
        read.SetException([first, sibling]);
        var policyRan = false;
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var actual = owner.RunAsync(async original =>
        {
            try { await original.AwaitAsync(read.Task); }
            catch (IOException) { policyRan = true; } // Existing UI policy remains compatible.
        });
        await actual;
        Assert.True(actual.IsCompletedSuccessfully);
        Assert.True(policyRan);
        var close = await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAndDrainAsync());
        Assert.Collection(close.InnerExceptions, item => Assert.Same(first, item), item => Assert.Same(sibling, item));
    }

    [Fact]
    public async Task Independent_actual_stop_and_cleanup_tasks_retain_every_direct_fault()
    {
        var stopFirst = new IOException("stop primary");
        var stopSibling = new InvalidOperationException("stop sibling");
        var cleanup = new UnauthorizedAccessException("cleanup");
        var actualStop = new TaskCompletionSource();
        actualStop.SetException([stopFirst, stopSibling]);
        var cleanupCalled = false;
        var owner = new NewChatOriginalWorkLifetime(() => actualStop.Task, () =>
        { cleanupCalled = true; return Task.FromException(cleanup); });
        var close = owner.CloseAndDrainAsync();
        var failures = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.True(cleanupCalled);
        Assert.Collection(failures.InnerExceptions, item => Assert.Same(stopFirst, item),
            item => Assert.Same(stopSibling, item), item => Assert.Same(cleanup, item));
        Assert.Same(close, owner.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Action_requests_retirement_returns_then_external_owner_joins_same_close()
    {
        var returned = false;
        var cleanupSawReturn = false;
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask,
            () => { cleanupSawReturn = returned; return Task.CompletedTask; });
        var actual = owner.RunAsync(_ =>
        {
            owner.RequestRetirement();
            returned = true;
            return Task.CompletedTask;
        });
        await actual;
        var close = owner.CloseAndDrainAsync();
        await close;
        Assert.True(returned);
        Assert.True(cleanupSawReturn);
        Assert.Same(close, owner.OriginalClose);
        Assert.Throws<ObjectDisposedException>(() => { _ = owner.RunAsync(_ => Task.CompletedTask); });
    }

    [Fact]
    public async Task Actual_predicate_retirement_returning_true_cannot_publish_after_reentry()
    {
        var published = false;
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var actual = owner.RunAsync(original =>
        {
            original.BindPublicationGuard(() => { owner.RequestRetirement(); return true; });
            original.DemandPublication();
            published = true;
            return Task.CompletedTask;
        });
        var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.False(published);
        Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(failure.InnerExceptions));
        var close = await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAndDrainAsync());
        Assert.Same(failure.InnerExceptions[0], Assert.Single(close.InnerExceptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raw_faulted_OCE_and_canceled_status_stay_distinct_with_both_actual_observations(bool canceled)
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var cause = new OperationCanceledException("foreign faulted OCE", source.Token);
        var stage = canceled ? Task.FromCanceled(source.Token) : Task.FromException(cause);
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        Task? actualPhase = null;
        var actual = owner.RunAsync(original => actualPhase = original.AwaitAsync(stage));
        var operation = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(actual.IsFaulted);
        Assert.Equal(canceled, stage.IsCanceled);
        Assert.True(actualPhase!.IsCanceled);
        Assert.Single(operation.InnerExceptions);
        Assert.All(operation.InnerExceptions, item => Assert.IsAssignableFrom<OperationCanceledException>(item));
        if (!canceled) Assert.Same(cause, operation.InnerExceptions[0]);
        else Assert.Same(stage, Assert.IsType<TaskCanceledException>(operation.InnerExceptions[0]).Task);
        var close = await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAndDrainAsync());
        Assert.Single(close.InnerExceptions);
        Assert.Same(operation.InnerExceptions[0], close.InnerExceptions[0]);
    }

    [Fact]
    public async Task Unknown_empty_aggregate_is_opaque_and_remains_the_original_close_cause()
    {
        var unknown = new AggregateException("unknown empty payload");
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var actual = owner.RunAsync(original => original.AwaitAsync(Task.FromException(unknown)));
        Assert.Same(unknown, await Assert.ThrowsAsync<AggregateException>(() => actual));
        Assert.Same(unknown, await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAndDrainAsync()));
    }

    [Fact]
    public async Task Failed_original_publication_cannot_start_body_and_is_still_owned()
    {
        var publication = new IOException("owner publication failure");
        var bodyCalled = false;
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var actual = owner.RunAsync(_ => { bodyCalled = true; return Task.CompletedTask; },
            _ => throw publication);
        Assert.Same(publication, await Assert.ThrowsAsync<IOException>(() => actual));
        Assert.False(bodyCalled);
        Assert.Same(publication, await Assert.ThrowsAsync<IOException>(() => owner.CloseAndDrainAsync()));
    }

    [Fact]
    public async Task Admitted_original_cannot_join_its_own_already_published_close()
    {
        var returned = false;
        var cleanupSawReturn = false;
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask,
            () => { cleanupSawReturn = returned; return Task.CompletedTask; });
        var actual = owner.RunAsync(original =>
        {
            owner.RequestRetirement();
            Assert.NotNull(owner.OriginalClose);
            Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
            returned = true;
            return Task.CompletedTask;
        });
        await actual;
        var close = owner.CloseAndDrainAsync();
        await close;
        Assert.True(returned);
        Assert.True(cleanupSawReturn);
        Assert.Same(close, owner.OriginalClose);
    }

    [Fact]
    public async Task Healthy_originals_retire_without_sealing_a_live_page_after_128_operations()
    {
        var operations = 0;
        var cleanup = 0;
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask,
            () => { ++cleanup; return Task.CompletedTask; });
        for (var index = 0; index != 256; ++index)
            await owner.RunAsync(_ => { ++operations; return Task.CompletedTask; });
        Assert.Equal(256, operations);
        Assert.False(owner.IsRetiring);
        Assert.Equal(0, cleanup);
        await owner.CloseAndDrainAsync();
        Assert.Equal(1, cleanup);
    }

    [Fact]
    public async Task Actual_stop_and_cleanup_callbacks_cannot_join_their_own_close()
    {
        NewChatOriginalWorkLifetime? owner = null;
        Task? stoppedClose = null, cleanedClose = null;
        var stopRefused = false;
        var cleanupRefused = false;
        owner = new NewChatOriginalWorkLifetime(() =>
        {
            stoppedClose = owner!.OriginalClose;
            Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
            stopRefused = true;
            owner.RequestRetirement();
            return Task.CompletedTask;
        }, () =>
        {
            owner!.RunCloseCallback(() =>
            {
                cleanedClose = owner.OriginalClose;
                Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
                cleanupRefused = true;
                owner.RequestRetirement();
            });
            return Task.CompletedTask;
        });
        var close = owner.CloseAndDrainAsync();
        await close;
        Assert.True(stopRefused);
        Assert.True(cleanupRefused);
        Assert.Same(close, stoppedClose);
        Assert.Same(close, cleanedClose);
        Assert.Same(close, owner.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Inherited_completed_original_marker_does_not_refuse_a_later_external_join()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = 0;
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask,
            () => { ++cleanup; return Task.CompletedTask; });
        Task<Task>? later = null;
        var actual = owner.RunAsync(original =>
        {
            later = LaterExternalJoinAsync(); // This test owns and joins this separate child flow.
            return Task.CompletedTask;
        });
        await actual;
        Assert.True(actual.IsCompletedSuccessfully);
        Assert.False(owner.IsRetiring);
        release.SetResult();
        var close = await later!;
        await close;
        Assert.Same(close, owner.OriginalClose);
        Assert.Equal(1, cleanup);

        async Task<Task> LaterExternalJoinAsync()
        {
            await release.Task;
            return owner.CloseAndDrainAsync();
        }
    }

    [Fact]
    public async Task Synchronous_target_callback_publishes_before_reentry_and_preserves_direct_failure()
    {
        var cause = new IOException("actual synchronous target callback");
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        Task? callbackTask = null;
        var returned = false;
        var thrown = Assert.Throws<IOException>(() => owner.RunSynchronous(original =>
        {
            callbackTask = original.Task;
            Assert.False(callbackTask.IsCompleted);
            Assert.Same(original, owner.Executing);
            Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
            returned = true;
            throw cause;
        }));
        Assert.True(returned);
        Assert.Same(cause, thrown);
        Assert.True(callbackTask!.IsFaulted);
        var callbackCause = await Assert.ThrowsAsync<IOException>(() => callbackTask);
        Assert.Same(cause, callbackCause);
        var closeCause = await Assert.ThrowsAsync<IOException>(() => owner.CloseAndDrainAsync());
        Assert.Same(cause, closeCause);
    }

    [Fact]
    public async Task Completed_original_cancellation_registration_cannot_join_close_under_captured_context()
    {
        var owner = new NewChatOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        CancellationTokenRegistration registration = default;
        Task? callbackClose = null;
        var refused = false;
        var admitted = owner.RunAsync(original =>
        {
            registration = original.Token.Register(() =>
            {
                callbackClose = owner.OriginalClose;
                Assert.Null(owner.Executing); // The captured original has actually completed.
                Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
                refused = true;
                owner.RequestRetirement();
            });
            return Task.CompletedTask;
        });
        try
        {
            await admitted;
            Assert.True(admitted.IsCompletedSuccessfully);
            var close = owner.CloseAndDrainAsync();
            await close;
            Assert.True(refused);
            Assert.Same(close, callbackClose);
            Assert.Same(close, owner.CloseAndDrainAsync());
        }
        finally { registration.Dispose(); }
    }

    private sealed class HeldOriginalStream : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        internal readonly TaskCompletionSource<bool> Move = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Dispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource MoveAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposeAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int MoveCalls;
        internal int DisposeCalls;
        public int Current => 1;
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
        public ValueTask<bool> MoveNextAsync()
        { ++MoveCalls; MoveAcquired.TrySetResult(); return new(Move.Task); }
        public ValueTask DisposeAsync()
        { ++DisposeCalls; DisposeAcquired.TrySetResult(); return new(Dispose.Task); }
    }
}
