using Haven.Desktop.Services;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_personal_Den_post_callback_failure_joins_raw_read_and_retains_the_same_close_cause()
        => RunWithOriginalDen(async rig =>
        {
            var host = rig.DenHost!; var post = new IOException("Actual caller rejected the completed source callback.");
            var depth = new ThreadLocal<int>(() => 0); var rejected = 0; var raw = new List<Task>();
            try
            {
                void Scope(Action body)
                {
                    depth.Value++;
                    try
                    {
                        body();
                        if (depth.Value == 1 && Interlocked.CompareExchange(ref rejected, 1, 0) == 0) throw post;
                    }
                    finally { depth.Value--; }
                }
                void Retain(Task actual) { lock (raw) raw.Add(actual); rig.Retain(actual); }
                var read = rig.Keep(host.ReadWithinOriginalSourceAsync(rig.OriginalOpenedDen!.DenId, Scope, Retain, rig.Token).AsTask());
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(rig.Token));
                Assert.True(read.IsFaulted); AssertHostCause(read.Exception ?? failure, post);
                Task[] originals; lock (raw) originals = raw.ToArray();
                Assert.NotEmpty(originals); Assert.All(originals, actual => Assert.True(actual.IsCompleted));
                Assert.Contains(originals, actual => actual is Task<HomeLocalStoreEvidence?> { IsCompletedSuccessfully: true });
                rig.KeepExpected(read, failure);
                await AssertSameHostFailedCloseAsync(rig, host, post, read.Exception ?? failure);
            }
            finally { depth.Dispose(); }
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_personal_Den_retainer_and_replacement_scope_causes_survive_the_same_raw_read_join()
        => RunWithOriginalDen(async rig =>
        {
            var host = rig.DenHost!; var bodyFailure = new IOException("Actual raw retainer refused.");
            var scopeFailure = new InvalidOperationException("Actual caller replaced the callback failure.");
            var refused = 0; var raw = new List<Task>();
            void Scope(Action body)
            {
                try { body(); } catch { throw scopeFailure; }
            }
            void Retain(Task actual)
            {
                lock (raw) raw.Add(actual); rig.Retain(actual);
                if (Interlocked.CompareExchange(ref refused, 1, 0) == 0) throw bodyFailure;
            }
            var read = rig.Keep(host.ReadWithinOriginalSourceAsync(rig.OriginalOpenedDen!.DenId, Scope, Retain, rig.Token).AsTask());
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(rig.Token));
            AssertHostCause(read.Exception ?? failure, bodyFailure); AssertHostCause(read.Exception ?? failure, scopeFailure);
            Task[] originals; lock (raw) originals = raw.ToArray();
            Assert.NotEmpty(originals); Assert.All(originals, actual => Assert.True(actual.IsCompleted));
            foreach (var actual in originals)
                if (!actual.IsCompletedSuccessfully) rig.KeepExpected(actual, actual.Exception ?? failure);
            rig.KeepExpected(read, failure);
            await AssertSameHostFailedCloseAsync(rig, host, bodyFailure, read.Exception ?? failure);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_personal_Den_repeated_callback_cannot_be_swallowed_into_a_successful_read()
        => RunWithOriginalDen(async rig =>
        {
            Exception? protocol = null; var repeated = 0;
            void Scope(Action body)
            {
                body();
                if (Interlocked.CompareExchange(ref repeated, 1, 0) == 0)
                {
                    try { body(); } catch (InvalidOperationException cause) { protocol = cause; }
                }
            }
            var host = rig.DenHost!;
            var read = rig.Keep(host.ReadWithinOriginalSourceAsync(rig.OriginalOpenedDen!.DenId, Scope, rig.Retain, rig.Token).AsTask());
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(rig.Token));
            Assert.NotNull(protocol); Assert.True(read.IsFaulted); AssertHostCause(read.Exception ?? failure, protocol);
            rig.KeepExpected(read, failure);
            await AssertSameHostFailedCloseAsync(rig, host, protocol, read.Exception ?? failure);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_personal_Den_absent_callback_refuses_before_publishing_a_raw_read()
        => RunWithOriginalDen(async rig =>
        {
            var published = new List<Task>(); var host = rig.DenHost!;
            var read = rig.Keep(host.ReadWithinOriginalSourceAsync(rig.OriginalOpenedDen!.DenId, _ => { },
                actual => { published.Add(actual); rig.Retain(actual); }, rig.Token).AsTask());
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(rig.Token));
            Assert.True(read.IsFaulted); Assert.Empty(published);
            Assert.Contains(HostCauseReferences(read.Exception ?? failure), cause => cause is InvalidOperationException &&
                cause.Message.Contains("not entered", StringComparison.Ordinal));
            rig.KeepExpected(read, failure);
            await AssertSameHostFailedCloseAsync(rig, host, HostCauseReferences(read.Exception ?? failure).First(cause =>
                cause is InvalidOperationException && cause.Message.Contains("not entered", StringComparison.Ordinal)), read.Exception ?? failure);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_personal_Den_foreign_thread_callback_retains_protocol_refusal_when_caller_swallows_it()
        => RunWithOriginalDen(async rig =>
        {
            Exception? protocol = null; var published = new List<Task>(); var host = rig.DenHost!;
            void Scope(Action body)
            {
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var foreign = new Thread(() =>
                {
                    try { body(); }
                    catch (InvalidOperationException cause) { protocol = cause; }
                    finally { completed.SetResult(); }
                });
                foreign.Start(); completed.Task.WaitAsync(rig.Token).GetAwaiter().GetResult(); foreign.Join();
            }
            var read = rig.Keep(host.ReadWithinOriginalSourceAsync(rig.OriginalOpenedDen!.DenId, Scope,
                actual => { published.Add(actual); rig.Retain(actual); }, rig.Token).AsTask());
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => read.WaitAsync(rig.Token));
            Assert.NotNull(protocol); Assert.Empty(published); AssertHostCause(read.Exception ?? failure, protocol);
            rig.KeepExpected(read, failure);
            await AssertSameHostFailedCloseAsync(rig, host, protocol, read.Exception ?? failure);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_personal_Den_late_Open_success_retains_provider_before_post_callback_failure()
        => RunWithOriginalDen(async rig =>
        {
            var host = new OriginalAssistantPersonalDenHost(new Paths(rig.Root), rig.Home.Profiles);
            var post = new IOException("Caller rejected the actual existing Den acquisition callback.");
            var depth = new ThreadLocal<int>(() => 0); var rejected = 0; var raw = new List<Task>();
            Task? expectedClose = null; Exception? expectedCloseCause = null;
            try
            {
                void Scope(Action body)
                {
                    depth.Value++;
                    try
                    {
                        body();
                        if (depth.Value == 1 && Interlocked.CompareExchange(ref rejected, 1, 0) == 0) throw post;
                    }
                    finally { depth.Value--; }
                }
                void Retain(Task actual) { lock (raw) raw.Add(actual); rig.Retain(actual); }
                var open = rig.Keep(host.OpenExistingWithinOriginalSourceAsync(Scope, Retain, rig.Token));
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => open.WaitAsync(rig.Token));
                Assert.True(open.IsFaulted); AssertHostCause(open.Exception ?? failure, post);
                Assert.True(host.HasAcquiredProvider);
                Task[] originals; lock (raw) originals = raw.ToArray();
                var rawOpen = Assert.Single(originals.OfType<Task<HomeDenStoreEvidenceProvider>>().Distinct());
                Assert.True(rawOpen.IsCompletedSuccessfully);
                var retained = await rig.Keep(host.OpenExistingOriginalAsync(rig.Token));
                Assert.Same(await rawOpen, retained);
                rig.KeepExpected(open, failure);
                expectedClose = await AssertSameHostFailedCloseAsync(rig, host, post, open.Exception ?? failure);
                expectedCloseCause = expectedClose.Exception;
                var actualProviderClose = Assert.IsAssignableFrom<Task>(retained.OriginalClose);
                Assert.True(actualProviderClose.IsCompletedSuccessfully);
            }
            finally
            {
                Task? close = null;
                try { close = rig.Keep(host.CloseAndDrainAsync()); await close; }
                catch (Exception cause)
                {
                    if (close is null) throw;
                    if (expectedClose is not null)
                    {
                        Assert.Same(expectedClose, close);
                        AssertKnownHostCloseGraph(close.Exception ?? cause, expectedCloseCause!);
                    }
                    else
                    {
                        AssertKnownHostCloseGraph(close.Exception ?? cause, post);
                        rig.KeepExpected(close, cause);
                    }
                }
                depth.Dispose();
            }
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_personal_Den_callback_cannot_join_host_after_restoring_an_unrelated_execution_context()
        => RunWithOriginalDen(async rig =>
        {
            var host = rig.DenHost!; var neutral = ExecutionContext.Capture()!; var inspected = 0;
            void Scope(Action body)
            {
                if (Interlocked.CompareExchange(ref inspected, 1, 0) == 0)
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        ExecutionContext.Run(neutral.CreateCopy(), state => { _ = host.CloseAndDrainAsync(); }, null);
                    });
                body();
            }
            var evidence = await rig.Keep(host.ReadWithinOriginalSourceAsync(rig.OriginalOpenedDen!.DenId,
                Scope, rig.Retain, rig.Token).AsTask());
            Assert.NotNull(evidence); Assert.Equal(1, inspected); Assert.Null(host.OriginalClose);
            var close = rig.Keep(host.CloseAndDrainAsync()); await close; Assert.True(close.IsCompletedSuccessfully);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_personal_Den_more_than_one_retention_window_of_healthy_reads_closes_after_same_source_joins()
        => RunWithOriginalDen(async rig =>
        {
            var host = rig.DenHost!; var callbacks = 0; var raw = new List<Task>();
            void Scope(Action body) { Interlocked.Increment(ref callbacks); body(); }
            void Retain(Task actual) { lock (raw) raw.Add(actual); rig.Retain(actual); }
            for (var index = 0; index < 140; index++)
            {
                var evidence = await rig.Keep(host.ReadWithinOriginalSourceAsync(rig.OriginalOpenedDen!.DenId,
                    Scope, Retain, rig.Token).AsTask());
                Assert.NotNull(evidence); Assert.Equal(rig.OriginalOpenedDen.DenId, evidence.StoreId);
            }
            Assert.True(callbacks >= 140); Task[] originals; lock (raw) originals = raw.ToArray();
            Assert.NotEmpty(originals); Assert.All(originals, actual => Assert.True(actual.IsCompletedSuccessfully));
            var close = rig.Keep(host.CloseAndDrainAsync()); await close;
            Assert.Same(close, host.OriginalClose); Assert.Same(close, host.CloseAndDrainAsync());
            Assert.True(close.IsCompletedSuccessfully);
        });

    private static async Task<Task> AssertSameHostFailedCloseAsync(Rig rig, OriginalAssistantPersonalDenHost host, Exception expected, Exception originalObserved)
    {
        var close = rig.Keep(host.CloseAndDrainAsync());
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(rig.Token));
        Assert.True(close.IsFaulted); AssertHostCause(close.Exception ?? failure, expected);
        AssertKnownHostCloseGraph(close.Exception ?? failure, originalObserved);
        Assert.Same(close, host.OriginalClose); Assert.Same(close, host.CloseAndDrainAsync());
        rig.KeepExpected(close, failure); return close;
    }
    private static void AssertKnownHostCloseGraph(Exception actual, Exception originalObserved)
    {
        var observed = HostCauseReferences(originalObserved).ToHashSet(ReferenceEqualityComparer.Instance);
        void Check(Exception cause)
        {
            if (observed.Contains(cause)) return;
            // New owned combining wrappers may contain only already-observed
            // occurrences. An unknown direct cause or empty group cannot pass.
            var wrapper = Assert.IsType<AggregateException>(cause);
            Assert.NotEmpty(wrapper.InnerExceptions);
            foreach (var direct in wrapper.InnerExceptions) Check(direct);
        }
        Check(actual);
    }
    private static void AssertHostCause(Exception actual, Exception expected) =>
        Assert.Contains(HostCauseReferences(actual), cause => ReferenceEquals(cause, expected));
    private static IEnumerable<Exception> HostCauseReferences(Exception cause)
    {
        yield return cause;
        if (cause is AggregateException group)
            foreach (var child in group.InnerExceptions)
                foreach (var retained in HostCauseReferences(child)) yield return retained;
    }
}
