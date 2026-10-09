using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed class AssistantMemoryCustodyTests
{
    private static readonly List<object[]> RetainedCachedCloseOriginals = [];

    [Fact]
    public async Task Healthy_cached_close_rechecks_the_same_late_callback_cause_without_rewriting_its_receipt()
    {
        var originals = new AssistantMemoryOriginals();
        var retained = new List<Task>();
        var gate = new object();
        void Keep(Task actual) { lock (gate) if (!retained.Any(value => ReferenceEquals(value, actual))) retained.Add(actual); }
        Action? escaped = null;
        var effects = 0;
        var scope = originals.CreateScope(body => { escaped = body; body(); }, Keep);
        lock (RetainedCachedCloseOriginals) RetainedCachedCloseOriginals.Add([originals, scope, retained, gate]);
        Task? close = null;
        Exception? known = null;
        var failures = new List<Exception>();
        try
        {
            var command = originals.Admit(async () =>
            {
                var value = await scope.Read(() => { effects++; return Task.FromResult(7); });
                await scope.JoinAsync();
                return value;
            });
            Keep(command);
            Assert.Equal(7, await command);
            close = originals.CloseAndDrainAsync(); Keep(close);
            await close;
            Assert.Same(close, originals.CloseAndDrainAsync());
            Assert.True(close.IsCompletedSuccessfully);
            Assert.NotNull(escaped);

            var observed = Assert.Throws<InvalidOperationException>(() => escaped!());
            Assert.Equal("The original memory callback expired, repeated or moved threads.", observed.Message);
            Assert.Null(observed.InnerException);
            known = observed;
            Assert.Equal(1, effects);
            Assert.Same(observed, Assert.Throws<InvalidOperationException>(() => { _ = originals.CloseAndDrainAsync(); }));
            Assert.Same(close, originals.OriginalClose);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.Throws<ObjectDisposedException>(() => { _ = originals.Admit(() => { effects++; return Task.FromResult(0); }); });
            Assert.Equal(1, effects);

            var joined = scope.JoinAsync(); Keep(joined);
            Assert.Same(observed, await Assert.ThrowsAsync<InvalidOperationException>(() => joined));
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            // Acquire and retain each actual cleanup receipt independently, even if
            // an earlier assertion failed. A late cause never rewrites a healthy close.
            try
            {
                if (close is null) { close = originals.CloseAndDrainAsync(); Keep(close); }
            }
            catch (Exception failure) { if (!OnlyKnownCachedCloseCause(failure, known)) failures.Add(failure); }
            try { var joined = scope.JoinAsync(); Keep(joined); }
            catch (Exception failure) { if (!OnlyKnownCachedCloseCause(failure, known)) failures.Add(failure); }
            var observed = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            while (true)
            {
                Task[] pending;
                lock (gate) pending = retained.Where(observed.Add).ToArray();
                if (pending.Length == 0) break;
                foreach (var actual in pending)
                    try { await actual; }
                    catch (Exception failure)
                    {
                        var original = actual.Exception ?? failure;
                        if (!OnlyKnownCachedCloseCause(original, known)) failures.Add(original);
                    }
            }
        }
        AssistantMemoryOriginals.Throw(failures);
    }

    private static bool OnlyKnownCachedCloseCause(Exception failure, Exception? known)
    {
        if (known is null) return false;
        var pending = new Stack<Exception>(); pending.Push(failure);
        var visited = 0;
        while (pending.Count != 0)
        {
            if (++visited > 4096) return false;
            var next = pending.Pop();
            if (ReferenceEquals(next, known)) continue;
            if (next.GetType() != typeof(AggregateException)) return false;
            var combined = (AggregateException)next;
            if (combined.InnerExceptions.Count == 0 || combined.InnerExceptions.Count > 4096 - visited - pending.Count) return false;
            foreach (var inner in combined.InnerExceptions) pending.Push(inner);
        }
        return true;
    }

    [Fact]
    public async Task Retirement_seals_input_admission_and_joins_held_original()
    {
        var originals = new AssistantMemoryOriginals();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = originals.Admit(async () => { entered.SetResult(); return await release.Task; });
        await entered.Task; var close = originals.CloseAndDrainAsync();
        Assert.False(close.IsCompleted); Assert.Same(close, originals.CloseAndDrainAsync());
        Assert.Throws<ObjectDisposedException>(() => { _ = originals.Admit(() => Task.FromResult(0)); });
        release.SetResult(7); Assert.Equal(7, await command); await close;
    }

    [Fact]
    public async Task Acquired_late_resource_survives_a_postcallback_failure()
    {
        var originals = new AssistantMemoryOriginals(); var expected = new object(); object? captured = null;
        var failure = new IOException("Actual postcallback check failed.");
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retained = new List<Task>();
        var scope = originals.CreateScope(body => { body(); throw failure; }, retained.Add);
        var actual = scope.Read(() => release.Task, result => captured = result);
        Assert.Same(release.Task, Assert.Single(retained)); Assert.False(actual.IsCompleted);
        release.SetResult(expected); Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => actual));
        Assert.Same(expected, captured);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(scope.JoinAsync));
    }

    [Fact]
    public async Task Faulted_and_canceled_raw_sources_are_not_waived_by_matching_exception_type()
    {
        var originals = new AssistantMemoryOriginals(); var failure = new OperationCanceledException("Foreign faulted source.");
        var source = Task.FromException<int>(failure); var scope = originals.CreateScope(body => body(), _ => { });
        var actual = originals.Admit(async () =>
        {
            try { await scope.Read(() => source); } catch (OperationCanceledException) { }
            await scope.JoinAsync(); return 0;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
        Assert.True(source.IsFaulted); Assert.False(source.IsCanceled);
        var close = originals.CloseAndDrainAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => close);
        Assert.Same(close, originals.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Swallowed_original_body_and_foreign_scope_failures_are_both_retained()
    {
        var originals = new AssistantMemoryOriginals();
        var bodyFailure = new IOException("Actual synchronous source failed.");
        var foreignFailure = new InvalidOperationException("Independent scope postguard failed.");
        var scope = originals.CreateScope(body =>
        {
            try { body(); } catch (IOException) { }
            throw foreignFailure;
        }, _ => { });
        var observed = Assert.Throws<AggregateException>(() => scope.Run(() => throw bodyFailure));
        Assert.Contains(bodyFailure, observed.InnerExceptions); Assert.Contains(foreignFailure, observed.InnerExceptions);
        var joined = await Assert.ThrowsAsync<AggregateException>(scope.JoinAsync);
        Assert.Contains(bodyFailure, joined.InnerExceptions); Assert.Contains(foreignFailure, joined.InnerExceptions);
    }

    [Fact]
    public async Task Swallowed_repeated_callback_cannot_become_healthy_source()
    {
        var originals = new AssistantMemoryOriginals(); var invoked = 0;
        var scope = originals.CreateScope(body =>
        {
            body();
            try { body(); } catch (InvalidOperationException) { }
        }, _ => { });
        Assert.Throws<InvalidOperationException>(() => scope.Run(() => invoked++));
        Assert.Equal(1, invoked);
        await Assert.ThrowsAsync<InvalidOperationException>(scope.JoinAsync);
    }
}
