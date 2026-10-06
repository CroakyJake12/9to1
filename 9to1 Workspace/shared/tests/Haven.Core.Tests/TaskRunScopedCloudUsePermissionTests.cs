using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

// Real central engine and private lease; explicit synthetic actor/grant fixture only.
// No credential, provider start, Home approval, installed model or live cloud operation.
public sealed class TaskRunScopedCloudUsePermissionTests
{
    [Fact]
    public async Task Actual_held_actor_raw_fault_preserves_all_direct_causes_after_scope_fault()
    {
        var rig = new Rig(); var lease = await rig.Acquire();
        var raw = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = new IOException("actual post-acquisition scope fault");
        var one = new OperationCanceledException("faulted actual actor"); var two = new IOException("actual actor sibling");
        var originals = new List<Task>(); rig.Actors.Read = () => { reached.TrySetResult(); return new(raw.Task); };
        Task? driver = null; Exception? primary = null;
        try
        {
            driver = ((ITaskRunOriginalScopedCloudUsePermissionLease)lease).RevalidateWithinOriginalSourceAsync(
                callback => { callback(); throw before; }, originals.Add, default).AsTask();
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(driver.IsCompleted);
            Assert.Contains(originals, actual => ReferenceEquals(actual, raw.Task));
            raw.TrySetException([one, two]);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => driver);
            var leaves = failure.Flatten().InnerExceptions;
            Assert.Contains(leaves, error => ReferenceEquals(error, before)); Assert.Contains(leaves, error => ReferenceEquals(error, one));
            Assert.Contains(leaves, error => ReferenceEquals(error, two)); Assert.True(driver.IsFaulted); Assert.False(driver.IsCanceled);
        }
        catch (Exception error) { primary = error; throw; }
        finally { raw.TrySetException([one, two]); await Cleanup(lease, driver, primary, [before, one, two]); }
    }
    [Fact]
    public async Task Delayed_callback_is_revoked_before_actor_factory_after_failed_driver()
    {
        var rig = new Rig(); var lease = await rig.Acquire(); var count = 0; Action? saved = null;
        rig.Actors.Read = () => { count++; return ValueTask.FromResult<AuthenticatedResourceActor?>(rig.Actor); };
        Task? driver = null; Exception? primary = null; Exception[] known = [];
        try
        {
            driver = ((ITaskRunOriginalScopedCloudUsePermissionLease)lease).RevalidateWithinOriginalSourceAsync(
                callback => saved = callback, _ => { }, default).AsTask();
            var failure = await Assert.ThrowsAsync<AggregateException>(() => driver); known = failure.Flatten().InnerExceptions.ToArray();
            Assert.NotNull(saved); Assert.Equal(0, count); Assert.Throws<InvalidOperationException>(() => saved!()); Assert.Equal(0, count);
            Assert.True(driver.IsFaulted); Assert.False(driver.IsCanceled);
        }
        catch (Exception error) { primary = error; throw; }
        finally { await Cleanup(lease, driver, primary, known); }
    }
    [Fact]
    public async Task Actual_canceled_actor_task_keeps_canceled_driver_and_exact_raw_identity()
    {
        var rig = new Rig(); var lease = await rig.Acquire(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var raw = Task.FromCanceled<AuthenticatedResourceActor?>(cancel.Token); rig.Actors.Read = () => new(raw);
        var originals = new List<Task>(); Task? driver = null; Exception? primary = null;
        try
        {
            driver = ((ITaskRunOriginalScopedCloudUsePermissionLease)lease).RevalidateWithinOriginalSourceAsync(
                callback => callback(), originals.Add, default).AsTask();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver);
            Assert.True(driver.IsCanceled); Assert.False(driver.IsFaulted); Assert.Contains(originals, actual => ReferenceEquals(actual, raw));
        }
        catch (Exception error) { primary = error; throw; }
        finally { await Cleanup(lease, driver, primary, [], canceled: true); }
    }
    [Fact]
    public async Task Post_callback_synchronous_OCE_is_faulted_even_when_actual_actor_task_succeeds()
    {
        var rig = new Rig(); var lease = await rig.Acquire(); var one = new OperationCanceledException("actual synchronous caller fault");
        var raw = Task.FromResult<AuthenticatedResourceActor?>(rig.Actor); rig.Actors.Read = () => new(raw);
        var originals = new List<Task>(); Task? driver = null; Exception? primary = null;
        try
        {
            driver = ((ITaskRunOriginalScopedCloudUsePermissionLease)lease).RevalidateWithinOriginalSourceAsync(
                callback => { callback(); throw one; }, originals.Add, default).AsTask();
            var failure = await Assert.ThrowsAsync<AggregateException>(() => driver);
            Assert.Contains(failure.Flatten().InnerExceptions, actual => ReferenceEquals(actual, one));
            Assert.True(driver.IsFaulted); Assert.False(driver.IsCanceled); Assert.Contains(originals, actual => ReferenceEquals(actual, raw));
            Assert.True(raw.IsCompletedSuccessfully);
        }
        catch (Exception error) { primary = error; throw; }
        finally { await Cleanup(lease, driver, primary, [one]); }
    }
    [Fact]
    public async Task Swallowed_second_callback_refusal_still_faults_real_central_validation()
    {
        var rig = new Rig(); var lease = await rig.Acquire(); var reads = 0;
        var raw = Task.FromResult<AuthenticatedResourceActor?>(rig.Actor);
        rig.Actors.Read = () => { reads++; return new(raw); };
        var originals = new List<Task>(); InvalidOperationException? refusal = null; Task? driver = null; Exception? primary = null;
        try
        {
            driver = ((ITaskRunOriginalScopedCloudUsePermissionLease)lease).RevalidateWithinOriginalSourceAsync(
                callback => { callback(); try { callback(); } catch (InvalidOperationException error) { refusal = error; } },
                originals.Add, default).AsTask();
            var failure = await Assert.ThrowsAsync<AggregateException>(() => driver);
            Assert.NotNull(refusal); Assert.Equal(1, reads);
            Assert.Contains(failure.Flatten().InnerExceptions, error => ReferenceEquals(error, refusal));
            Assert.All(failure.Flatten().InnerExceptions, error => Assert.Same(refusal, error));
            Assert.Contains(originals, actual => ReferenceEquals(actual, raw)); Assert.True(raw.IsCompletedSuccessfully);
            Assert.True(driver.IsFaulted); Assert.False(driver.IsCanceled);
        }
        catch (Exception error) { primary = error; throw; }
        finally { await Cleanup(lease, driver, primary, refusal is null ? [] : [refusal]); }
    }
    private static async Task Cleanup(ITaskRunCloudUsePermissionLease lease, Task? driver, Exception? primary, Exception[] known, bool canceled = false)
    {
        var failures = new List<Exception>(); if (primary is not null) failures.Add(primary); Task? close = null;
        try { close = lease.DisposeAsync().AsTask(); } catch (Exception error) { failures.Add(error); }
        if (driver is not null) try { await driver.ConfigureAwait(false); }
            catch (Exception error)
            {
                var leaves = driver.Exception?.Flatten().InnerExceptions.ToArray() ?? [error];
                if (!(canceled && driver.IsCanceled && leaves.All(value => value is OperationCanceledException)) &&
                    !leaves.All(value => known.Any(actual => ReferenceEquals(actual, value))))
                { failures.Add(error); if (driver.Exception is { } group) { failures.Add(group); failures.AddRange(group.InnerExceptions); } }
            }
        if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error)
            { failures.Add(error); if (close.Exception is { } group) { failures.Add(group); failures.AddRange(group.InnerExceptions); } }
        if (failures.Count != 0) throw new AggregateException("Actual scoped central fixture body/raw driver/lease cleanup failed.", failures);
    }
    private sealed class Rig
    {
        public readonly AuthenticatedResourceActor Actor = new("synthetic-response-task", "synthetic-profile", null, null, "one");
        public readonly Actors Actors; public readonly PermissionDecisionEngine Policy = new();
        public readonly TaskRunCentralCloudUsePermissionSource Source; public readonly TaskExecutionOwnerBinding Owner;
        public readonly TaskRunRouteCandidate Candidate = new("synthetic-response", 1, "synthetic-cloud", "model", null, true, ["Text"]);
        public Rig()
        {
            Actors = new(Actor); Source = new(Actors, Policy);
            Owner = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Actor.ActorId, Actor.ProfileId, null, null, Actor.AuthenticationRevision, "synthetic-original");
            Policy.Grant(TaskRunCentralCloudUsePermissionSource.ScopeFor(Owner, Candidate)); // explicit fixture only
        }
        public Task<ITaskRunCloudUsePermissionLease> Acquire() => Source.AcquireOriginalAsync(Owner, Candidate, default).AsTask();
    }
    private sealed class Actors(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public Func<ValueTask<AuthenticatedResourceActor?>>? Read;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Read?.Invoke() ?? ValueTask.FromResult<AuthenticatedResourceActor?>(actor); }
    }
}
