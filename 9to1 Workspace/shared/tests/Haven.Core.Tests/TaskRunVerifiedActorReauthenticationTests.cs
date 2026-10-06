using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

// Synthetic identity metadata tests the original custody registry, not JWT/account authority.
// Separate original12 Node controls exercise the maintained real EdDSA/browser/account pipeline.
public sealed class TaskRunVerifiedActorReauthenticationTests
{
    private static TaskExecutionOwnerBinding Previous() => new(
        Guid.Parse("20f5e3a1-6404-4900-9c62-b766517f594c"),
        Guid.Parse("b1b48466-6efc-4e06-abee-4c4d3106eae1"),
        Guid.Parse("22d5b97d-3e12-4c5f-8ff2-cd2faf7696dc"), "synthetic:actor", "synthetic:profile",
        Guid.Parse("222f7a40-c183-488e-a7ad-5566c82f06a4"), null, "synthetic:old-generation", "synthetic:owner-receipt");
    private static AuthenticatedResourceActor Fresh(TaskExecutionOwnerBinding old) => new(
        old.ActorId, old.ProfileId, old.AccountId, old.OrganisationId, "synthetic:new-generation");

    [Fact]
    public async Task Original_identity_revalidates_and_closes_without_changing_previous_binding()
    {
        var previous = Previous(); var current = Fresh(previous);
        var actors = new Actors(_ => ValueTask.FromResult<AuthenticatedResourceActor?>(current));
        var source = new TaskRunVerifiedActorReauthenticationSource(actors);
        var original = await source.AcquireOriginalAsync(previous, CancellationToken.None);
        try
        {
            Assert.Same(current, original.CurrentActor);
            await source.ValidateOriginalAsync(original, previous, CancellationToken.None);
            Assert.Equal("synthetic:old-generation", previous.AuthenticationRevision);
            var close = original.DisposeAsync().AsTask(); Assert.Same(close, original.DisposeAsync().AsTask());
            await close;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ValidateOriginalAsync(original, previous, CancellationToken.None).AsTask());
        }
        finally { await original.DisposeAsync(); await source.DisposeAsync(); }
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("profile")]
    [InlineData("account")]
    [InlineData("revision")]
    public async Task Wrong_actual_stable_identity_or_no_new_activation_cannot_issue(string changed)
    {
        var previous = Previous(); var current = Fresh(previous);
        current = changed switch
        {
            "actor" => current with { ActorId = "synthetic:other" },
            "profile" => current with { ProfileId = "synthetic:other-profile" },
            "account" => current with { AccountId = Guid.Parse("24671904-3e7b-4f78-9aaa-dbf451b37fbf") },
            _ => current with { AuthenticationRevision = previous.AuthenticationRevision }
        };
        var source = new TaskRunVerifiedActorReauthenticationSource(new Actors(_ => ValueTask.FromResult<AuthenticatedResourceActor?>(current)));
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.AcquireOriginalAsync(previous, CancellationToken.None).AsTask());
        }
        finally { Assert.IsType<UnauthorizedAccessException>(await Record.ExceptionAsync(() => source.DisposeAsync().AsTask())); }
    }

    [Fact]
    public async Task Copied_lease_and_foreign_source_cannot_validate_private_original()
    {
        var previous = Previous(); var current = Fresh(previous);
        var actors = new Actors(_ => ValueTask.FromResult<AuthenticatedResourceActor?>(current));
        var source = new TaskRunVerifiedActorReauthenticationSource(actors);
        var foreign = new TaskRunVerifiedActorReauthenticationSource(actors);
        var issued = await source.AcquireOriginalAsync(previous, CancellationToken.None);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ValidateOriginalAsync(new Copy(current), previous, CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.ValidateOriginalAsync(issued, previous, CancellationToken.None).AsTask());
        }
        finally { await issued.DisposeAsync(); await source.DisposeAsync(); await foreign.DisposeAsync(); }
    }

    [Fact]
    public async Task Changed_generation_after_held_actual_read_is_denied_and_retained()
    {
        var previous = Previous(); var current = Fresh(previous);
        var entered = NewSignal(); var held = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var source = new TaskRunVerifiedActorReauthenticationSource(new Actors(_ =>
        {
            if (Interlocked.Increment(ref reads) == 1) return ValueTask.FromResult<AuthenticatedResourceActor?>(current);
            entered.TrySetResult(); return new(held.Task);
        }));
        var original = await source.AcquireOriginalAsync(previous, CancellationToken.None);
        Task? validation = null;
        try
        {
            validation = source.ValidateOriginalAsync(original, previous, CancellationToken.None).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(validation.IsCompleted);
            held.TrySetResult(current with { AuthenticationRevision = "synthetic:third-generation" });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => validation);
        }
        finally
        {
            held.TrySetResult(current); if (validation is not null) await Observe(validation);
            var close = await Record.ExceptionAsync(() => original.DisposeAsync().AsTask()); Assert.NotNull(close);
            Assert.True(Contains<UnauthorizedAccessException>(close!));
            Assert.NotNull(await Record.ExceptionAsync(() => source.DisposeAsync().AsTask()));
        }
    }

    [Fact]
    public async Task Actual_session_revocation_denies_existing_original_and_retains_its_failure()
    {
        var previous = Previous(); AuthenticatedResourceActor? current = Fresh(previous);
        var source = new TaskRunVerifiedActorReauthenticationSource(new Actors(_ => ValueTask.FromResult(current)));
        var original = await source.AcquireOriginalAsync(previous, CancellationToken.None);
        try
        {
            current = null;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ValidateOriginalAsync(original, previous, default).AsTask());
        }
        finally
        {
            var close = await Record.ExceptionAsync(() => original.DisposeAsync().AsTask()); Assert.NotNull(close);
            Assert.True(Contains<UnauthorizedAccessException>(close!));
            Assert.NotNull(await Record.ExceptionAsync(() => source.DisposeAsync().AsTask()));
        }
    }

    [Fact]
    public async Task Actual_faulted_OCE_first_group_keeps_fault_status_and_both_original_causes()
    {
        var previous = Previous(); using var caller = new CancellationTokenSource();
        var held = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = NewSignal();
        var source = new TaskRunVerifiedActorReauthenticationSource(new Actors(_ => { entered.TrySetResult(); return new(held.Task); }));
        var first = new OperationCanceledException("synthetic original faulted OCE", caller.Token);
        var second = new IOException("synthetic original sibling");
        var acquire = source.AcquireOriginalAsync(previous, caller.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); caller.Cancel(); held.TrySetException([first, second]);
            var failure = await Record.ExceptionAsync(() => acquire);
            Assert.True(held.Task.IsFaulted); Assert.True(acquire.IsFaulted); Assert.False(acquire.IsCanceled);
            Assert.NotNull(failure); Assert.True(ContainsReference(failure!, first)); Assert.True(ContainsReference(failure!, second));
        }
        finally
        {
            held.TrySetException([first, second]); await Observe(acquire);
            var close = source.DisposeAsync().AsTask(); Assert.Same(close, source.DisposeAsync().AsTask());
            var failure = await Record.ExceptionAsync(() => close); Assert.True(close.IsFaulted);
            Assert.NotNull(failure); Assert.True(ContainsReference(failure!, first)); Assert.True(ContainsReference(failure!, second));
        }
    }

    [Fact]
    public async Task Actual_canceled_actor_task_remains_canceled_but_source_drain_retains_it()
    {
        var previous = Previous(); using var caller = new CancellationTokenSource();
        var held = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = NewSignal(); CancellationToken rawToken = default;
        var source = new TaskRunVerifiedActorReauthenticationSource(new Actors(token => { rawToken = token; entered.TrySetResult(); return new(held.Task); }));
        var acquire = source.AcquireOriginalAsync(previous, caller.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); caller.Cancel(); held.TrySetCanceled(rawToken);
            await Observe(acquire); Assert.True(held.Task.IsCanceled); Assert.True(acquire.IsCanceled);
        }
        finally
        {
            held.TrySetCanceled(rawToken); await Observe(acquire);
            var close = source.DisposeAsync().AsTask(); Assert.NotNull(await Record.ExceptionAsync(() => close)); Assert.True(close.IsFaulted);
        }
    }

    [Fact]
    public async Task Callback_canceling_owner_then_throwing_exact_OCE_is_a_fault()
    {
        var previous = Previous(); using var caller = new CancellationTokenSource();
        var exact = new OperationCanceledException("synthetic synchronous callback fault", caller.Token);
        var source = new TaskRunVerifiedActorReauthenticationSource(new Actors(_ => { caller.Cancel(); throw exact; }));
        var acquire = source.AcquireOriginalAsync(previous, caller.Token).AsTask();
        try
        {
            var error = await Record.ExceptionAsync(() => acquire);
            Assert.True(acquire.IsFaulted); Assert.False(acquire.IsCanceled); Assert.NotNull(error); Assert.True(ContainsReference(error!, exact));
        }
        finally { var close = await Record.ExceptionAsync(() => source.DisposeAsync().AsTask()); Assert.NotNull(close); Assert.True(ContainsReference(close!, exact)); }
    }

    [Fact]
    public async Task Source_close_waits_for_actual_held_read_and_seals_new_admission()
    {
        var previous = Previous(); var current = Fresh(previous);
        var held = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously); var entered = NewSignal();
        var source = new TaskRunVerifiedActorReauthenticationSource(new Actors(_ => { entered.TrySetResult(); return new(held.Task); }));
        var acquire = source.AcquireOriginalAsync(previous, CancellationToken.None).AsTask(); Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); close = source.DisposeAsync().AsTask();
            Assert.Same(close, source.DisposeAsync().AsTask()); Assert.False(close.IsCompleted); Assert.False(acquire.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => { _ = source.AcquireOriginalAsync(previous, CancellationToken.None); });
        }
        finally
        {
            held.TrySetResult(current); await Observe(acquire); close ??= source.DisposeAsync().AsTask();
            Assert.NotNull(await Record.ExceptionAsync(() => close)); Assert.True(close.IsCompleted);
        }
    }

    [Fact]
    public async Task Restored_execution_context_callback_cannot_join_same_source()
    {
        var previous = Previous(); var current = Fresh(previous); var external = ExecutionContext.Capture()!;
        TaskRunVerifiedActorReauthenticationSource? source = null; Exception? denied = null;
        source = new(new Actors(readToken =>
        {
            ExecutionContext.Run(external, state => { denied = Record.Exception(() => { _ = source!.DisposeAsync(); }); }, null);
            return ValueTask.FromResult<AuthenticatedResourceActor?>(current);
        }));
        var original = await source.AcquireOriginalAsync(previous, CancellationToken.None);
        try { Assert.IsType<InvalidOperationException>(denied); }
        finally { await original.DisposeAsync(); await source.DisposeAsync(); }
    }

    [Fact]
    public async Task Healthy_closed_originals_prune_after_more_than_capacity_without_reusing_leases()
    {
        var previous = Previous(); var current = Fresh(previous);
        var source = new TaskRunVerifiedActorReauthenticationSource(new Actors(_ => ValueTask.FromResult<AuthenticatedResourceActor?>(current)));
        ITaskRunVerifiedReauthenticationLease? last = null;
        try
        {
            for (var index = 0; index < 160; index++)
            {
                var original = await source.AcquireOriginalAsync(previous, CancellationToken.None);
                Assert.NotSame(last, original); await source.ValidateOriginalAsync(original, previous, CancellationToken.None);
                await original.DisposeAsync(); last = original;
            }
        }
        finally { await source.DisposeAsync(); }
    }

    private sealed class Actors(Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>> read) : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => read(token); }
    private sealed class Copy(AuthenticatedResourceActor actor) : ITaskRunVerifiedReauthenticationLease
    { public AuthenticatedResourceActor CurrentActor => actor; public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Observe(Task original) { try { await original; } catch { _ = original.Exception; } }
    private static bool ContainsReference(Exception error, Exception original)
        => ReferenceEquals(error, original) || error is AggregateException group && group.InnerExceptions.Any(inner => ContainsReference(inner, original));
    private static bool Contains<T>(Exception error) where T : Exception
        => error is T || error is AggregateException group && group.InnerExceptions.Any(Contains<T>);
}
