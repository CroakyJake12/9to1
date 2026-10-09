using System.Text.Json;
namespace HavenOS.Files.CUI.Tests;

internal static class OriginalAttachmentStateReadTests
{
    private sealed record State(int Revision, string Text);
    private static readonly List<object> RetainedFixtures = [];
    public static async Task RunAllAsync()
    {
        await MissingDirectoryAndMissingSidecarNeverInitialize();
        await ExistingReadPreservesBytesAndOrdinaryWriterRemainsAvailable();
        await PostCallbackFailureRetainsTheActualHeldGateUntilDrain();
        await RestoredContextCannotJoinItsOwnReadCohort();
    }
    private static string FreshPath() => Path.Combine(Path.GetTempPath(), "files-attachment-read-" + Guid.NewGuid().ToString("N"), "state.json");
    private static async Task MissingDirectoryAndMissingSidecarNeverInitialize()
    {
        foreach (var existingState in new[] { false, true })
        {
            var initial = 0;
            await RunFixture(() => { initial++; return new(0, "forbidden"); }, async fixture =>
            {
                var path = fixture.Path; var store = fixture.Store;
                if (existingState) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); await fixture.Keep(File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"state\":{\"revision\":1,\"text\":\"kept\"}}")); }
                var before = existingState ? await fixture.Keep(File.ReadAllBytesAsync(path)) : null;
                var raw = fixture.Keep(store.ReadExistingWithinOriginalSourceAsync(body => body(), fixture.Retain, CancellationToken.None));
                var failure = await Failure(raw); var originals = TerminalCauses(failure).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
                Check.Equal(1, originals.Length);
                Check.True(originals[0] is FileNotFoundException or DirectoryNotFoundException);
                var close = fixture.Keep(store.CloseOriginalReadsAndDrainAsync()); var closeFailure = await Failure(close);
                fixture.ExpectOnly(failure, originals); fixture.ExpectOnly(closeFailure, originals);
                Check.True(ReferenceEquals(close, store.CloseOriginalReadsAndDrainAsync())); Check.Equal(0, initial);
                Check.False(File.Exists(path + ".lock"));
                if (existingState) Check.True(before!.SequenceEqual(await fixture.Keep(File.ReadAllBytesAsync(path))));
                else Check.False(Directory.Exists(System.IO.Path.GetDirectoryName(path)));
            });
        }
    }
    private static async Task ExistingReadPreservesBytesAndOrdinaryWriterRemainsAvailable()
    {
        var initial = 0;
        await RunFixture(() => { initial++; return new(0, "initial"); }, async fixture =>
        {
            var path = fixture.Path; var store = fixture.Store;
            await fixture.Keep(store.UpdateAsync(_ => new(1, "actual maintained writer"))); Check.Equal(1, initial);
            var before = await fixture.Keep(File.ReadAllBytesAsync(path)); var names = Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!).Order().ToArray();
            var raw = fixture.Keep(store.ReadExistingWithinOriginalSourceAsync(body => body(), fixture.Retain, CancellationToken.None));
            Check.Equal(new State(1, "actual maintained writer"), await raw);
            await fixture.Keep(store.CloseOriginalReadsAndDrainAsync());
            Check.True(before.SequenceEqual(await fixture.Keep(File.ReadAllBytesAsync(path))));
            Check.True(names.SequenceEqual(Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!).Order())); Check.Equal(1, initial);
            await fixture.Keep(store.UpdateAsync(current => current with { Revision = 2 })); // Read retirement is not global store shutdown.
            Check.Equal(2, (await fixture.Keep(store.ReadExistingAsync())).Revision);
            Check.Throws<ObjectDisposedException>(() => { _ = store.ReadExistingWithinOriginalSourceAsync(body => body(), fixture.Retain, CancellationToken.None); });
        });
    }
    private static async Task PostCallbackFailureRetainsTheActualHeldGateUntilDrain()
    {
        await RunFixture(() => new(0, "initial"), async fixture =>
        {
            var store = fixture.Store;
            await fixture.Keep(store.UpdateAsync(_ => new(1, "before")));
            var enteredWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWriter = new TaskCompletionSource<State>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Release = () => releaseWriter.TrySetResult(new(2, "actual writer completes"));
            var writer = fixture.Keep(store.UpdateExistingWithinOriginalSourceAsync((_, _) => { enteredWriter.SetResult(); return releaseWriter.Task; },
                null, body => body(), fixture.Retain, CancellationToken.None));
            if (!ReferenceEquals(await Task.WhenAny(enteredWriter.Task, writer), enteredWriter.Task)) await writer;
            await enteredWriter.Task;
            var expected = new IOException("Actual caller scope failed after the original gate wait was retained.");
            var capturedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0; var armed = false;
            void Scope(Action body) { body(); if (armed) { armed = false; throw expected; } }
            void Retain(Task actual) { fixture.Retain(actual); if (Interlocked.Increment(ref count) == 2) { armed = true; capturedGate.TrySetResult(); } }
            var raw = fixture.Keep(store.ReadExistingWithinOriginalSourceAsync(Scope, Retain, CancellationToken.None));
            if (!ReferenceEquals(await Task.WhenAny(capturedGate.Task, raw), capturedGate.Task)) await raw;
            await capturedGate.Task;
            var close = fixture.Keep(store.CloseOriginalReadsAndDrainAsync()); Check.False(close.IsCompleted);
            fixture.Release!();
            // Proof assertions never substitute for the independent finally joins below.
            var failed = await Failure(raw); var closed = await Failure(close);
            fixture.ExpectOnly(failed, [expected]); fixture.ExpectOnly(closed, [expected]);
            await writer;
            Check.Equal(2, (await fixture.Keep(store.ReadExistingAsync())).Revision);
        });
    }
    private static async Task RestoredContextCannotJoinItsOwnReadCohort()
    {
        await RunFixture(() => new(0, "initial"), async fixture =>
        {
            var store = fixture.Store;
            await fixture.Keep(store.UpdateAsync(_ => new(1, "real state"))); var neutral = ExecutionContext.Capture()!; var refusals = 0;
            void Retain(Task original)
            {
                fixture.Retain(original);
                ExecutionContext.Run(neutral, ignored => { Check.Throws<InvalidOperationException>(() => { _ = store.CloseOriginalReadsAndDrainAsync(); }); refusals++; }, null);
            }
            var raw = fixture.Keep(store.ReadExistingWithinOriginalSourceAsync(body => body(), Retain, CancellationToken.None));
            Check.Equal(1, (await raw).Revision); await fixture.Keep(store.CloseOriginalReadsAndDrainAsync()); Check.True(refusals > 1);
        });
    }
    private static async Task RunFixture(Func<State> initial, Func<Fixture, Task> body)
    {
        var fixture = new Fixture(FreshPath(), initial);
        lock (RetainedFixtures) RetainedFixtures.Add(fixture); // Root store/path/receipts before the first callback or assertion.
        var errors = new List<Exception>();
        try { await body(fixture); } catch (Exception cause) { errors.Add(cause); }
        finally { await fixture.Settle(errors); }
        if (errors.Count != 0) throw new AggregateException("Actual attachment read fixture body and independent source drains must all settle.", errors);
    }
    private sealed class Fixture(string path, Func<State> initial)
    {
        internal string Path => path;
        internal readonly VersionedJsonStateStore<State> Store = new(path, 1, initial);
        private readonly List<Task> _raw = [];
        private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        internal Action? Release;
        internal void Retain(Task actual) { lock (_raw) _raw.Add(actual); }
        internal Task Keep(Task actual) { Retain(actual); return actual; }
        internal Task<T> Keep<T>(Task<T> actual) { Retain(actual); return actual; }
        internal void ExpectOnly(Exception actual, Exception[] expected)
        {
            var causes = TerminalCauses(actual).ToArray();
            Check.True(causes.Length > 0 && causes.All(cause => expected.Any(known => ReferenceEquals(cause, known))));
            foreach (var expectedCause in expected) Check.True(causes.Any(cause => ReferenceEquals(cause, expectedCause)));
            foreach (var cause in expected) _expected.Add(cause);
        }
        internal async Task Settle(List<Exception> errors)
        {
            try { Release?.Invoke(); } catch (Exception cause) { errors.Add(cause); }
            // Acquire BOTH actual cached drains before any await, even if either admission faults.
            try { _ = Keep(Store.CloseOriginalReadsAndDrainAsync()); } catch (Exception cause) { errors.Add(cause); }
            try { _ = Keep(Store.CloseOriginalUpdatesAndDrainAsync()); } catch (Exception cause) { errors.Add(cause); }
            var observed = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            while (true)
            {
                Task[] batch; lock (_raw) batch = _raw.Where(actual => !observed.Contains(actual)).ToArray();
                if (batch.Length == 0) break;
                foreach (var actual in batch)
                {
                    if (!observed.Add(actual)) continue;
                    try { await actual; }
                    catch (Exception cause)
                    {
                        var original = actual.Exception ?? cause; var causes = TerminalCauses(original).ToArray();
                        if (causes.Length == 0 || causes.Any(value => !_expected.Contains(value))) errors.Add(original);
                    }
                }
            }
        }
    }
    private static async Task<Exception> Failure(Task raw)
    { try { await raw; } catch (Exception cause) { return raw.Exception ?? cause; } throw new InvalidOperationException("Expected the actual original task to fail."); }
    // These controls create only the actual store's task/helper aggregate envelopes.
    // No foreign aggregates are supplied or normalized by this assertion helper.
    private static IEnumerable<Exception> TerminalCauses(Exception cause) => cause is AggregateException { InnerExceptions.Count: > 0 } group
        ? group.InnerExceptions.SelectMany(TerminalCauses) : [cause];
}
