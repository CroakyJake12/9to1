using System.Text.Json;

namespace HavenOS.Files.CUI.Tests;

internal static class OriginalExistingStateStoreTests
{
    private sealed record State(int Revision, string Label);
    public static async Task RunAllAsync()
    {
        await MissingExistingStateNeverCreatesAnEnvelopeOrDirectory();
        await AsyncTransformAndBothCommitChecksPrecedeTheActualPersistedEnvelope();
        await PostCallbackFailureKeepsHeldTransformAndAllRawSiblingsUntilDrain();
        await RestoredContextRetainerCannotJoinItsOwnStore();
    }

    private static async Task MissingExistingStateNeverCreatesAnEnvelopeOrDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "files-original-existing-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "state.json"); var initialCalls = 0; var transformCalls = 0;
        var store = new VersionedJsonStateStore<State>(path, 1, () => { initialCalls++; return new(1, "initial"); });
        var actual = store.UpdateExistingWithinOriginalSourceAsync((_, _) => { transformCalls++; return Task.FromResult(new State(2, "changed")); },
            null, body => body(), _ => { }, CancellationToken.None);
        var failure = await Failure(actual);
        var expected = Leaves(failure).OfType<FileNotFoundException>().Distinct<Exception>(ReferenceEqualityComparer.Instance).Single();
        Check.True(Leaves(failure).All(cause => ReferenceEquals(cause, expected)));
        Check.Equal(0, initialCalls); Check.Equal(0, transformCalls); Check.False(Directory.Exists(directory));
        var close = store.CloseOriginalUpdatesAndDrainAsync();
        Check.True(Leaves(await Failure(close)).All(cause => ReferenceEquals(cause, expected)));
        Check.True(ReferenceEquals(close, store.OriginalUpdatesClose));
        Check.True(ReferenceEquals(close, store.CloseOriginalUpdatesAndDrainAsync()));
    }

    private static async Task AsyncTransformAndBothCommitChecksPrecedeTheActualPersistedEnvelope() => await Fixture(async (directory, path) =>
    {
        var initialCalls = 0; var validationCalls = 0; var transformed = false;
        var store = new VersionedJsonStateStore<State>(path, 1, () => { initialCalls++; throw new InvalidOperationException("Existing-only source invoked initial state."); });
        var raw = new List<Task>();
        var actual = store.UpdateExistingWithinOriginalSourceAsync(async (current, _) =>
        {
            Check.Equal(1, current.Revision); await Task.Yield(); transformed = true; return new State(2, "actual persisted transform");
        }, _ =>
        {
            Check.True(transformed); validationCalls++;
            var persisted = JsonDocument.Parse(File.ReadAllText(path));
            using (persisted) Check.Equal(1, persisted.RootElement.GetProperty("state").GetProperty("revision").GetInt32());
            if (validationCalls == 2)
            {
                var temporary = Directory.GetFiles(directory, ".state.json.*.tmp"); Check.Equal(1, temporary.Length);
                using var flushed = JsonDocument.Parse(File.ReadAllText(temporary[0]));
                Check.Equal(2, flushed.RootElement.GetProperty("state").GetProperty("revision").GetInt32());
            }
            return ValueTask.CompletedTask;
        }, body => body(), task => { lock (raw) raw.Add(task); }, CancellationToken.None);
        var result = await actual; Check.Equal(2, result.Revision); Check.Equal(2, validationCalls); Check.Equal(0, initialCalls);
        var close = store.CloseOriginalUpdatesAndDrainAsync(); await close;
        Task[] children; lock (raw) children = raw.ToArray(); foreach (var child in children) await child;
        Check.True(ReferenceEquals(close, store.OriginalUpdatesClose));
        Check.Equal(2, (await new VersionedJsonStateStore<State>(path, 1, () => throw new InvalidOperationException()).ReadExistingAsync()).Revision);
        Check.Throws<ObjectDisposedException>(() => { _ = store.UpdateExistingWithinOriginalSourceAsync((state, _) => Task.FromResult(state), null, body => body(), _ => { }, CancellationToken.None); });
    });

    private static async Task PostCallbackFailureKeepsHeldTransformAndAllRawSiblingsUntilDrain() => await Fixture(async (_, path) =>
    {
        var held = new TaskCompletionSource<State>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scopeFailure = new IOException("Actual freshness check rejected the already-returned transform.");
        var faultedOce = new OperationCanceledException("Actual faulted transform cause remains faulted.");
        var foreign = new AggregateException("Actual foreign empty cause remains opaque.");
        var io = new IOException("Actual independent transform sibling.");
        var raw = new List<Task>(); var armed = false; var scopeDepth = 0; var validations = 0;
        void Scope(Action body)
        {
            scopeDepth++;
            try { body(); if (scopeDepth == 1 && armed) { armed = false; throw scopeFailure; } }
            finally { scopeDepth--; }
        }
        var store = new VersionedJsonStateStore<State>(path, 1, () => throw new InvalidOperationException());
        var actual = store.UpdateExistingWithinOriginalSourceAsync((_, _) => { armed = true; entered.TrySetResult(); return held.Task; },
            _ => { validations++; return ValueTask.CompletedTask; }, Scope, task => { lock (raw) raw.Add(task); }, CancellationToken.None);
        Task? close = null; Exception? primary = null;
        try
        {
            if (!ReferenceEquals(await Task.WhenAny(entered.Task, actual), entered.Task)) await actual;
            await entered.Task;
            lock (raw) Check.True(raw.Any(task => ReferenceEquals(task, held.Task)));
            Check.False(actual.IsCompleted); close = store.CloseOriginalUpdatesAndDrainAsync(); Check.False(close.IsCompleted);
        }
        catch (Exception cause) { primary = cause; }
        finally { held.TrySetException([faultedOce, foreign, io]); }
        var actualFailure = await Failure(actual);
        close ??= store.CloseOriginalUpdatesAndDrainAsync(); var closeFailure = await Failure(close);
        Check.True(held.Task.IsFaulted); Check.False(held.Task.IsCanceled); Check.Equal(0, validations);
        foreach (var expected in new Exception[] { scopeFailure, faultedOce, foreign, io })
        { Check.True(References(actualFailure, expected)); Check.True(References(closeFailure, expected)); }
        var expectedCauses = new Exception[] { scopeFailure, faultedOce, foreign, io };
        Check.True(Leaves(actualFailure).All(cause => expectedCauses.Any(expected => ReferenceEquals(cause, expected))));
        Check.True(Leaves(closeFailure).All(cause => expectedCauses.Any(expected => ReferenceEquals(cause, expected))));
        Check.True(ReferenceEquals(close, store.CloseOriginalUpdatesAndDrainAsync()));
        using var original = JsonDocument.Parse(File.ReadAllText(path)); Check.Equal(1, original.RootElement.GetProperty("state").GetProperty("revision").GetInt32());
        // This fixture intentionally retains the source-failed evidence directory.
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        throw new RetainExpectedFixture();
    });

    private static async Task RestoredContextRetainerCannotJoinItsOwnStore() => await Fixture(async (_, path) =>
    {
        var neutral = ExecutionContext.Capture()!; var refusals = 0;
        var store = new VersionedJsonStateStore<State>(path, 1, () => throw new InvalidOperationException());
        void Retain(Task _)
        {
            ExecutionContext.Run(neutral, ignored =>
            {
                Check.Throws<InvalidOperationException>(() => { _ = store.CloseOriginalUpdatesAndDrainAsync(); });
                refusals++;
            }, null);
        }
        var actual = store.UpdateExistingWithinOriginalSourceAsync((current, _) => Task.FromResult(current with { Revision = 2 }),
            null, body => body(), Retain, CancellationToken.None);
        await actual; var close = store.CloseOriginalUpdatesAndDrainAsync(); await close;
        Check.True(refusals > 1); Check.True(ReferenceEquals(close, store.OriginalUpdatesClose));
    });

    private sealed class RetainExpectedFixture : Exception;
    private static async Task Fixture(Func<string, string, Task> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "files-original-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "state.json"); var healthy = false;
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"state\":{\"revision\":1,\"label\":\"original\"}}");
        try { await body(directory, path); healthy = true; }
        catch (RetainExpectedFixture) { }
        finally { if (healthy) Directory.Delete(directory, recursive: true); }
    }
    private static async Task<Exception> Failure(Task actual)
    {
        try { await actual; } catch (Exception cause) { return actual.Exception ?? cause; }
        throw new InvalidOperationException("Expected the SAME original Task to remain failed.");
    }
    private static bool References(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(cause => References(cause, expected));
    private static IEnumerable<Exception> Leaves(Exception actual) => actual is AggregateException group && group.InnerExceptions.Count != 0
        ? group.InnerExceptions.SelectMany(Leaves) : [actual];
}
