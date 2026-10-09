using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class DenOriginalOwnershipObservationTests
{
    [Fact]
    public async Task Scoped_original_matches_canonical_content_hash_and_empty_observation()
    {
        var root = NewRoot(); using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var store = await DenStore.CreateAsync(root, [new("private", "personal")], lifetime.Token);
        List<Task> raw = []; Task? close = null; var healthy = false; Exception? bodyFailure = null;
        try
        {
            var empty = await store.ObserveOwnershipAsync(lifetime.Token);
            var first = store.ObserveOwnershipWithinOriginalSourceAsync(body => body(), task => RetainFixtureRaw(raw, task), body => body(), lifetime.Token);
            Assert.Equal(empty, await first); Assert.True(empty.IsEmpty);
            var record = Path.Combine(root, "records", "fixture-content.txt");
            await File.WriteAllTextAsync(record, "actual bounded original content", lifetime.Token);
            var canonical = await store.ObserveOwnershipAsync(lifetime.Token);
            var original = store.ObserveOwnershipWithinOriginalSourceAsync(body => body(), task => RetainFixtureRaw(raw, task), body => body(), lifetime.Token);
            Assert.Equal(canonical, await original); Assert.False(canonical.IsEmpty);
            Assert.NotEqual(empty.ContentRevision, canonical.ContentRevision);
            Assert.Contains(raw, task => !ReferenceEquals(task, first) && !ReferenceEquals(task, original));
            Assert.All(raw, task => Assert.True(task.IsCompletedSuccessfully));
            close = store.DisposeAsync().AsTask(); Assert.Same(close, store.DisposeAsync().AsTask()); await close;
            healthy = true;
        }
        catch (Exception cause) { bodyFailure = cause; throw; }
        finally { await SettleFixture(store, root, raw, null, close, healthy, bodyFailure); }
    }

    [Fact]
    public async Task Accepted_real_writer_retry_remains_joined_by_cached_store_close()
    {
        var root = NewRoot(); using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var store = await DenStore.CreateAsync(root, [new("private", "personal")], lifetime.Token);
        var canonical = await store.ObserveOwnershipAsync(lifetime.Token);
        FileStream? externalWriter = new(Path.Combine(root, ".den-write-lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<Task> raw = []; Task<DenOwnershipObservation>? original = null; Task? close = null; var healthy = false; Exception? bodyFailure = null;
        try
        {
            original = store.ObserveOwnershipWithinOriginalSourceAsync(body => body(), Retain, body => body(), lifetime.Token);
            await Task.WhenAny(acquired.Task, original).WaitAsync(lifetime.Token);
            if (!acquired.Task.IsCompletedSuccessfully) await original;
            Assert.True(acquired.Task.IsCompletedSuccessfully);
            close = store.DisposeAsync().AsTask(); Assert.Same(close, store.DisposeAsync().AsTask());
            Assert.False(original.IsCompleted); Assert.False(close.IsCompleted);
            externalWriter.Dispose(); externalWriter = null;
            Assert.Equal(canonical, await original); await close;
            Assert.All(raw, task => Assert.True(task.IsCompletedSuccessfully)); healthy = true;
        }
        catch (Exception cause) { bodyFailure = cause; throw; }
        finally
        {
            try { externalWriter?.Dispose(); }
            catch (Exception cause) { bodyFailure = bodyFailure is null ? cause : new AggregateException(bodyFailure, cause); }
            await SettleFixture(store, root, raw, original, close, healthy, bodyFailure);
        }
        void Retain(Task task)
        {
            RetainFixtureRaw(raw, task);
            // The encompassing driver is published before original is assigned. The
            // actual pending retry delay is returned later by the real writer source.
            if (original is not null && !ReferenceEquals(task, original) && !task.IsCompleted)
                acquired.TrySetResult();
        }
    }

    [Fact]
    public async Task Original_physical_source_refuses_self_join_even_in_restored_context()
    {
        var root = NewRoot(); using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var neutral = ExecutionContext.Capture()!;
        var store = await DenStore.CreateAsync(root, [new("private", "personal")], lifetime.Token);
        List<Task> raw = []; var checks = 0; var healthy = false; Exception? bodyFailure = null; Task? close = null;
        try
        {
            void Scope(Action body)
            {
                body();
                ExecutionContext.Run(neutral, state =>
                {
                    Assert.Throws<InvalidOperationException>(() => { _ = store.DisposeAsync(); }); checks++;
                }, null);
            }
            var original = store.ObserveOwnershipWithinOriginalSourceAsync(Scope, task => RetainFixtureRaw(raw, task), body => body(), lifetime.Token);
            Assert.True((await original).IsEmpty); Assert.True(checks > 0);
            close = store.DisposeAsync().AsTask(); await close; healthy = true;
        }
        catch (Exception cause) { bodyFailure = cause; throw; }
        finally { await SettleFixture(store, root, raw, null, close, healthy, bodyFailure); }
    }

    [Fact]
    public async Task Post_acquisition_manifest_refusal_retains_same_source_and_original_close_failure()
    {
        var root = NewRoot(); using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var store = await DenStore.CreateAsync(root, [new("private", "personal")], lifetime.Token);
        var refused = new IOException("actual scoped manifest post-acquisition refusal");
        var scopes = 0; List<Task> raw = []; Task? close = null; Exception? bodyFailure = null;
        void Scope(Action body) { body(); if (++scopes == 5) throw refused; }
        var original = store.ObserveOwnershipWithinOriginalSourceAsync(Scope, task => RetainFixtureRaw(raw, task), body => body(), lifetime.Token);
        try
        {
            var fault = await Assert.ThrowsAnyAsync<Exception>(() => original);
            Assert.True(original.IsFaulted); Assert.True(ContainsSame(fault, refused));
            Assert.Contains(raw, task => !ReferenceEquals(task, original) && task.IsCompletedSuccessfully);
            close = store.DisposeAsync().AsTask(); Assert.Same(close, store.DisposeAsync().AsTask());
            var failedClose = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(ContainsSame(failedClose, refused));
        }
        catch (Exception cause) { bodyFailure = cause; throw; }
        finally { await SettleFixture(store, root, raw, original, close, false, bodyFailure, refused); }
    }

    private static async Task SettleFixture(DenStore store, string root, List<Task> raw,
        Task? original, Task? originalClose, bool healthy, Exception? bodyFailure, Exception? expected = null)
    {
        List<Exception> errors = []; Task? close = originalClose;
        // Acquire the SAME cached store close independently before joining sources;
        // one failed source must not prevent the owning disposal from being observed.
        try { close ??= store.DisposeAsync().AsTask(); }
        catch (Exception cause) { errors.Add(cause); }
        HashSet<Task> joined = new(ReferenceEqualityComparer.Instance);
        if (original is not null) await Join(original);
        if (close is not null) await Join(close);
        // The actual enclosing originals are terminal before observing raw children;
        // the same retained list is also locked at each callback and snapshot.
        Task[] children; lock (raw) children = raw.ToArray();
        foreach (var task in children) await Join(task);
        async Task Join(Task task)
        {
            if (!joined.Add(task)) return;
            try { await task; }
            catch (Exception cause)
            {
                var actual = task.Exception ?? cause;
                var exactExpectedOccurrence = expected is not null &&
                    (ReferenceEquals(task, original) || ReferenceEquals(task, close)) &&
                    HasOnlyExpectedLeaves(actual, expected);
                if (!exactExpectedOccurrence) errors.Add(actual);
            }
        }
        if (errors.Count != 0)
        {
            if (bodyFailure is not null) errors.Insert(0, bodyFailure);
            throw new AggregateException("Actual Den fixture/source cleanup failed; preserve " + root, errors);
        }
        if (bodyFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(bodyFailure).Throw();
        if (healthy) Directory.Delete(root, true);
    }

    private static void RetainFixtureRaw(List<Task> raw, Task task) { lock (raw) raw.Add(task); }

    private static bool HasOnlyExpectedLeaves(Exception actual, Exception expected) =>
        ReferenceEquals(actual, expected) || actual is AggregateException { InnerExceptions.Count: > 0 } group &&
        group.InnerExceptions.All(cause => HasOnlyExpectedLeaves(cause, expected));

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "den-original-ownership-" + Guid.NewGuid().ToString("N"));
    private static bool ContainsSame(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(cause => ContainsSame(cause, expected));
}
