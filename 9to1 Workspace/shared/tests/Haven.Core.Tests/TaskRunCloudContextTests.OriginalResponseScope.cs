using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed partial class TaskRunCloudContextTests
{
    [Fact]
    public async Task Actual_configured_response_validation_retains_sources_and_coalesces_healthy_close()
    {
        await WithScopedCloudAsync(async (_, raw, lease, retained, _) =>
        {
            var source = Assert.IsAssignableFrom<ITaskRunOriginalScopedCloudAdmissionLease>(lease);
            var actual = source.RevalidateWithinOriginalSourceAsync(action => action(), retained.Add, default).AsTask();
            await actual;
            Assert.Contains(retained, task => ReferenceEquals(task, actual));
            Assert.True(raw.Reads >= 3); // Actual acquisition plus both fresh configured checks.
            Assert.All(retained, task => Assert.True(task.IsCompletedSuccessfully));
            var one = lease.DisposeAsync().AsTask();
            var two = lease.DisposeAsync().AsTask();
            Assert.Same(one, two);
            await one;
            Assert.True(one.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task Held_actual_secret_read_is_joined_after_real_lease_seal_without_new_read()
    {
        await WithScopedCloudAsync(async (_, raw, lease, retained, expected) =>
        {
            var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void RetainActual(Task task) { retained.Add(task); if (ReferenceEquals(task, held.Task)) enrolled.TrySetResult(); }
            void Scope(Action action)
            {
                try { action(); }
                catch (ObjectDisposedException actualSealRefusal) { expected.Add(actualSealRefusal); throw; }
            }
            var errors = new List<Exception>();
            raw.Read = () => { entered.TrySetResult(); return held.Task; };
            Task? actual = null; Task? close = null;
            try
            {
                actual = ((ITaskRunOriginalScopedCloudAdmissionLease)lease)
                    .RevalidateWithinOriginalSourceAsync(Scope, RetainActual, default).AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                close = lease.DisposeAsync().AsTask();
                Assert.False(close.IsCompleted);
                Assert.False(actual.IsCompleted);
                Assert.Contains(retained, task => ReferenceEquals(task, held.Task));
                held.SetResult(raw.Value);
                var fault = await Record.ExceptionAsync(() => actual);
                Assert.NotNull(fault);
                Assert.True(actual.IsFaulted);
                Assert.Contains(AllCloudCauses(actual.Exception!), cause => cause is ObjectDisposedException);
                var closing = await Record.ExceptionAsync(() => close);
                Assert.NotNull(closing);
                Assert.True(close.IsFaulted);
                Assert.True(held.Task.IsCompletedSuccessfully);
                Assert.Equal(2, raw.Reads); // No fresh callback after the real close seal.
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                held.TrySetResult(raw.Value);
                await JoinCloudControlOriginalAsync(actual, expected, errors);
                await JoinCloudControlOriginalAsync(close, expected, errors);
            }
            Throw(errors);
        });
    }

    [Fact]
    public async Task Post_scope_fault_does_not_abandon_raw_faulted_secret_task_or_direct_siblings()
    {
        await WithScopedCloudAsync(async (_, raw, lease, retained, expected) =>
        {
            var held = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void RetainActual(Task task) { retained.Add(task); if (ReferenceEquals(task, held.Task)) enrolled.TrySetResult(); }
            var errors = new List<Exception>();
            var scopeFault = new IOException("Exact post-acquisition scope failure");
            var first = new OperationCanceledException("Faulted actual secret original, never canceled success");
            var second = new IOException("Exact second raw secret cause");
            expected.Add(scopeFault); expected.Add(first); expected.Add(second);
            var acquired = false; var thrown = false;
            raw.Read = () => { acquired = true; entered.TrySetResult(); return held.Task; };
            Task? actual = null;
            try
            {
                actual = ((ITaskRunOriginalScopedCloudAdmissionLease)lease).RevalidateWithinOriginalSourceAsync(
                    callback => { callback(); if (acquired && !thrown) { thrown = true; throw scopeFault; } },
                    RetainActual, default).AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                Assert.False(actual.IsCompleted);
                Assert.Contains(retained, task => ReferenceEquals(task, held.Task));
                held.SetException([first, second]);
                var error = await Record.ExceptionAsync(() => actual);
                Assert.IsType<AggregateException>(error);
                Assert.True(actual.IsFaulted);
                Assert.True(held.Task.IsFaulted);
                Assert.Same(first, held.Task.Exception!.InnerExceptions[0]);
                Assert.Same(second, held.Task.Exception.InnerExceptions[1]);
                var causes = AllCloudCauses(actual.Exception!).ToArray();
                Assert.Contains(causes, cause => ReferenceEquals(cause, scopeFault));
                Assert.Contains(causes, cause => ReferenceEquals(cause, first));
                Assert.Contains(causes, cause => ReferenceEquals(cause, second));
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                held.TrySetException([first, second]);
                await JoinCloudControlOriginalAsync(actual, expected, errors);
            }
            Throw(errors);
        });
    }

    [Fact]
    public async Task Genuinely_canceled_raw_secret_is_distinct_and_never_a_healthy_cleanup()
    {
        await WithScopedCloudAsync(async (_, raw, lease, retained, expected) =>
        {
            var stopped = new CancellationToken(canceled: true);
            var canceled = Task.FromCanceled<string?>(stopped);
            raw.Read = () => canceled;
            var actual = ((ITaskRunOriginalScopedCloudAdmissionLease)lease)
                .RevalidateWithinOriginalSourceAsync(action => action(), retained.Add, default).AsTask();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            Assert.True(actual.IsCanceled);
            Assert.True(canceled.IsCanceled);
            Assert.Null(canceled.Exception);
            Assert.Contains(retained, task => ReferenceEquals(task, canceled));
            // Only cancellation exceptions whose Task is this SAME known canceled
            // original/driver may be expected; an unrelated OCE or IO remains a failure.
            expected.CanceledOriginals.Add(actual); expected.CanceledOriginals.Add(canceled);
            var close = lease.DisposeAsync().AsTask();
            Assert.NotNull(await Record.ExceptionAsync(() => close));
            Assert.True(close.IsFaulted);
            Assert.False(close.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task Swallowed_second_source_callback_refusal_stays_faulted_before_secret_read()
    {
        await WithScopedCloudAsync(async (_, raw, lease, retained, expected) =>
        {
            Exception? refused = null;
            var actual = ((ITaskRunOriginalScopedCloudAdmissionLease)lease).RevalidateWithinOriginalSourceAsync(
                callback => { callback(); try { callback(); } catch (Exception cause) { refused = cause; } },
                retained.Add, default).AsTask();
            Assert.NotNull(await Record.ExceptionAsync(() => actual));
            Assert.IsType<InvalidOperationException>(refused);
            expected.Add(refused!);
            Assert.True(actual.IsFaulted);
            Assert.Contains(AllCloudCauses(actual.Exception!), cause => ReferenceEquals(cause, refused));
            Assert.Equal(1, raw.Reads);
        });
    }

    [Fact]
    public async Task Restored_execution_context_actual_secret_factory_cannot_join_own_close()
    {
        await WithScopedCloudAsync(async (_, raw, lease, retained, _) =>
        {
            var before = ExecutionContext.Capture()!;
            Exception? refusal = null;
            raw.Read = () =>
            {
                ExecutionContext.Run(before, _ =>
                {
                    try { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                    catch (Exception cause) { refusal = cause; }
                }, null);
                return Task.FromResult<string?>(raw.Value);
            };
            var actual = ((ITaskRunOriginalScopedCloudAdmissionLease)lease)
                .RevalidateWithinOriginalSourceAsync(action => action(), retained.Add, default).AsTask();
            await actual;
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.True(actual.IsCompletedSuccessfully);
            Assert.True(raw.Reads >= 3);
            await lease.DisposeAsync(); // Independent owner, after the actual source terminal.
        });
    }

    [Fact]
    public async Task Both_swallowed_callback_refusals_keep_their_exact_original_references()
    {
        await WithScopedCloudAsync(async (_, raw, lease, retained, expected) =>
        {
            var refused = new List<Exception>();
            var actual = ((ITaskRunOriginalScopedCloudAdmissionLease)lease).RevalidateWithinOriginalSourceAsync(
                callback =>
                {
                    callback();
                    for (var index = 0; index < 2; index++)
                        try { callback(); } catch (Exception cause) { refused.Add(cause); }
                }, retained.Add, default).AsTask();
            Assert.NotNull(await Record.ExceptionAsync(() => actual));
            Assert.Equal(2, refused.Count);
            Assert.NotSame(refused[0], refused[1]);
            Assert.All(refused, cause => Assert.IsType<InvalidOperationException>(cause));
            foreach (var cause in refused) expected.Add(cause);
            Assert.True(actual.IsFaulted);
            var observed = AllCloudCauses(actual.Exception!).ToArray();
            Assert.Contains(observed, cause => ReferenceEquals(cause, refused[0]));
            Assert.Contains(observed, cause => ReferenceEquals(cause, refused[1]));
            Assert.Equal(1, raw.Reads); // The actual first factory was retained; no second factory runs.
        });
    }

    private static async Task JoinCloudControlOriginalAsync(Task? actual, CloudExpected expected, List<Exception> errors)
    {
        if (actual is null) return;
        try { await actual.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
        catch (Exception cause)
        {
            // A timeout or unknown cleanup remains a failure even if a known original
            // payload exists. Both independent owners are still attempted by finally.
            if (!actual.IsCompleted) { errors.Add(cause); return; }
            IEnumerable<Exception> direct = actual.Exception is { } fault ? fault.InnerExceptions : [cause];
            foreach (var original in direct) if (!expected.Contains(original)) errors.Add(original);
        }
    }

    private sealed class CloudExpected
    {
        internal readonly HashSet<Exception> Causes = new(ReferenceEqualityComparer.Instance);
        internal readonly HashSet<Task> CanceledOriginals = new(ReferenceEqualityComparer.Instance);
        internal void Add(Exception cause) => Causes.Add(cause);
        internal bool Contains(Exception cause) => Causes.Contains(cause)
            || cause is TaskCanceledException cancellation && cancellation.Task is { } actual && CanceledOriginals.Contains(actual)
            || cause is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(Contains);
    }
    private static IEnumerable<Exception> AllCloudCauses(Exception cause)
    {
        yield return cause;
        if (cause is AggregateException group)
            foreach (var direct in group.InnerExceptions)
                foreach (var original in AllCloudCauses(direct)) yield return original;
    }
    private static async Task WithScopedCloudAsync(Func<Rig, RawCloudSecrets, ITaskRunCloudAdmissionLease,
        List<Task>, CloudExpected, Task> body)
    {
        await RunAsync(async rig =>
        {
            var raw = new RawCloudSecrets();
            var source = new TaskRunConfiguredCloudAdmissionSource(rig.Actors, rig.Configurations, raw,
                rig.Privacy, rig.Conversations, (_, _, _, _) => Task.FromResult<TaskRunAttemptAdmission?>(rig.Issued),
                rig.Knowledge, new TaskRunCentralCloudUsePermissionSource(rig.Actors, rig.Policy));
            ITaskRunCloudAdmissionLease? lease = null; Task? original = null;
            var expected = new CloudExpected(); var errors = new List<Exception>();
            try
            {
                var mint = source.AcquireOriginalAsync(rig.Owner, rig.Model, rig.Configurations.Row, rig.Candidate, default).AsTask();
                original = mint; lease = await mint ?? throw new InvalidOperationException("The actual configured source returned no lease.");
                original = body(rig, raw, lease, [], expected); await original;
            }
            catch (Exception cause) { Add(errors, original, cause); }
            finally
            {
                if (lease is not null)
                {
                    Task? close = null;
                    try { close = lease.DisposeAsync().AsTask(); await close; }
                    catch (Exception cause)
                    {
                        IEnumerable<Exception> direct = close?.Exception is { } fault ? fault.InnerExceptions : [cause];
                        foreach (var actual in direct) if (!expected.Contains(actual)) errors.Add(actual);
                    }
                }
            }
            Throw(errors);
        });
    }

    private sealed class RawCloudSecrets : IProviderSecretStore
    {
        internal readonly string Value = "Synthetic fixture credential observation only";
        internal int Reads;
        internal Func<Task<string?>>? Read;
        public Task<string?> GetAsync(string provider, string name, CancellationToken token)
        { Reads++; token.ThrowIfCancellationRequested(); return Read?.Invoke() ?? Task.FromResult<string?>(Value); }
        public Task SetAsync(string provider, string name, string value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string provider, string name, CancellationToken token) => throw new NotSupportedException();
    }
}
