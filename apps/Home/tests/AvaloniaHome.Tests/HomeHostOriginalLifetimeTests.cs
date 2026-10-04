#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using HavenOS.Home.Core;
using Xunit;

namespace AvaloniaHome.Tests;

public sealed class HomeHostOriginalLifetimeTests
{
    [Fact]
    public async Task Held_original_work_and_actual_Core_stop_settle_before_desktop_exit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "home-original-shutdown-" + Guid.NewGuid().ToString("N"));
        var statePath = Path.Combine(directory, "state.json");
        var store = new FileHomeCoreStateStore(statePath);
        var payload = JsonSerializer.SerializeToElement(new { marker = "original-write-before-core-close" });
        var record = new HomeCoreStateRecord("shutdown.fixture", "ShutdownFixture", 1,
            HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 0, payload);
        var releaseWork = NewSignal();
        var enteredStop = NewSignal();
        var releaseStop = NewSignal();
        var service = new OriginalService("shutdown.service", () =>
        {
            Assert.True(File.Exists(statePath));
            enteredStop.TrySetResult();
            return releaseStop.Task;
        });
        var runtime = new HomeCoreRuntime(new[] { service });
        var exits = new List<int>();
        var lifetime = new HomeHostOriginalLifetime(() => runtime.DisposeAsync().AsTask(), code =>
        {
            Assert.True(releaseWork.Task.IsCompleted);
            Assert.True(releaseStop.Task.IsCompleted);
            exits.Add(code);
            return Task.CompletedTask;
        });
        Task? start = null;
        Task? work = null;
        Task? close = null;
        var failures = new List<Exception>();
        try
        {
            start = runtime.StartAsync();
            await start;
            work = Assert.IsAssignableFrom<Task>(work = lifetime.TryRunOriginal(async () =>
            {
                await releaseWork.Task;
                var originalWrite = store.WriteAsync(record, 0);
                var result = await originalWrite;
                Assert.True(result.IsSuccess);
            }));
            close = lifetime.RequestShutdownAsync();
            Assert.Same(close, lifetime.RequestShutdownAsync());
            Assert.Null(lifetime.TryRunOriginal(() => throw new InvalidOperationException("No new acquisition after close.")));
            Assert.False(close.IsCompleted);
            Assert.Equal(0, service.StopCalls);
            Assert.Empty(exits);
            releaseWork.TrySetResult();
            await enteredStop.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(work.IsCompletedSuccessfully);
            var persisted = await store.ReadAsync();
            Assert.True(persisted.IsSuccess);
            var written = Assert.Single(persisted.State!.Records);
            Assert.Equal(record.RecordId, written.RecordId);
            Assert.Equal(1, written.Revision);
            Assert.Equal("original-write-before-core-close", written.Payload.GetProperty("marker").GetString());
            Assert.False(close.IsCompleted);
            Assert.Empty(exits);
            releaseStop.TrySetResult();
            await close;
            Assert.Equal(new[] { 0 }, exits);
            Assert.Equal(1, service.StopCalls);
            Assert.True(lifetime.OriginalCoreClose!.IsCompletedSuccessfully);
            Assert.True(lifetime.OriginalDesktopExit!.IsCompletedSuccessfully);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            releaseWork.TrySetResult();
            releaseStop.TrySetResult();
            try { close ??= lifetime.RequestShutdownAsync(); }
            catch (Exception error) { Add(failures, error); }
            if (start is not null) await Collect(start, failures);
            if (work is not null) await Collect(work, failures);
            if (close is not null) await Collect(close, failures);
            try
            {
                var originalCoreClose = runtime.DisposeAsync().AsTask();
                await Collect(originalCoreClose, failures);
            }
            catch (Exception error) { Add(failures, error); }
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (Exception error)
            {
                if (!failures.Any(prior => ReferenceEquals(prior, error))) failures.Add(error);
            }
        }
        Throw(failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Work_Core_and_exit_faults_keep_exact_causes_and_stop_remaining_actual_services(bool foreignCancellation)
    {
        var workFailure = new IOException("Original work failure.");
        Exception coreFailure = foreignCancellation
            ? new OperationCanceledException("Foreign service cancellation.")
            : new IOException("Original service failure.");
        var exitFailure = new IOException("Original desktop exit failure.");
        var remaining = new OriginalService("shutdown.a", () => Task.CompletedTask);
        var failed = new OriginalService("shutdown.z", () => Task.FromException(coreFailure));
        var runtime = new HomeCoreRuntime(new[] { remaining, failed });
        var exitCodes = new List<int>();
        var lifetime = new HomeHostOriginalLifetime(() => runtime.DisposeAsync().AsTask(), code =>
        {
            Assert.Equal(1, remaining.StopCalls);
            Assert.Equal(1, failed.StopCalls);
            exitCodes.Add(code);
            return Task.FromException(exitFailure);
        });
        Task? start = null;
        Task? work = null;
        Task? close = null;
        Exception? expectedWork = null;
        Exception? expectedClose = null;
        Exception? expectedCore = null;
        var failures = new List<Exception>();
        try
        {
            start = runtime.StartAsync();
            await start;
            work = Assert.IsAssignableFrom<Task>(work = lifetime.TryRunOriginal(() => Task.FromException(workFailure)));
            var observedWork = await Assert.ThrowsAsync<IOException>(() => work!);
            Assert.Same(workFailure, observedWork);
            expectedWork = observedWork;
            close = lifetime.RequestShutdownAsync();
            var combined = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.Equal(3, combined.InnerExceptions.Count);
            Assert.Contains(combined.InnerExceptions, error => ReferenceEquals(error, workFailure));
            Assert.Contains(combined.InnerExceptions, error => ReferenceEquals(error, coreFailure));
            expectedCore = coreFailure;
            Assert.Contains(combined.InnerExceptions, error => ReferenceEquals(error, exitFailure));
            expectedClose = combined;
            Assert.Equal(new[] { 1 }, exitCodes);
            Assert.Same(close, lifetime.RequestShutdownAsync());
            Assert.True(lifetime.OriginalCoreClose!.IsCompleted);
            Assert.True(lifetime.OriginalDesktopExit!.IsCompleted);
            // Every original is settled. These exact asserted failures remain retained, never waived as cleanup.
            Assert.True(work.IsCompleted);
            Assert.True(close.IsCompleted);
        }
        catch (Exception error) { Add(failures, error); }
        finally
        {
            try { close ??= lifetime.RequestShutdownAsync(); }
            catch (Exception error) { Add(failures, error); }
            if (start is not null) await Collect(start, failures);
            if (work is not null) await Collect(work, failures, expectedWork);
            if (close is not null) await Collect(close, failures, expectedClose);
            try
            {
                var originalCoreClose = runtime.DisposeAsync().AsTask();
                await Collect(originalCoreClose, failures, expectedCore);
            }
            catch (Exception error) { Add(failures, error); }
        }
        Throw(failures);
    }

    [Fact]
    public async Task Original_close_is_published_before_Core_callback_reentry()
    {
        HomeHostOriginalLifetime lifetime = null!;
        Task? observed = null;
        var entered = NewSignal();
        var release = NewSignal();
        var calls = 0;
        lifetime = new HomeHostOriginalLifetime(() =>
        {
            calls++;
            observed = lifetime.OriginalShutdown;
            Assert.Same(observed, lifetime.RequestShutdownAsync());
            entered.TrySetResult();
            return release.Task;
        }, _ => Task.CompletedTask);
        var close = lifetime.RequestShutdownAsync();
        var failures = new List<Exception>();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Same(close, observed);
            Assert.False(close.IsCompleted);
            release.TrySetResult();
            await close;
            Assert.Equal(1, calls);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            release.TrySetResult();
            await Collect(close, failures);
        }
        Throw(failures);
    }

    [Fact]
    public async Task Successful_originals_prune_but_all_128_faults_block_further_admission_and_remain_on_close()
    {
        var coreCalls = 0;
        var exitCodes = new List<int>();
        var lifetime = new HomeHostOriginalLifetime(() =>
        {
            coreCalls++;
            return Task.CompletedTask;
        }, code =>
        {
            exitCodes.Add(code);
            return Task.CompletedTask;
        });
        for (var index = 0; index < 512; index++)
            await Assert.IsAssignableFrom<Task>(lifetime.TryRunOriginal(() => Task.CompletedTask));
        var failures = new List<IOException>();
        for (var index = 0; index < 128; index++)
        {
            var failure = new IOException("Original fault " + index);
            failures.Add(failure);
            var original = Assert.IsAssignableFrom<Task>(lifetime.TryRunOriginal(() => Task.FromException(failure)));
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => original));
        }
        var acquired = false;
        Assert.Throws<InvalidOperationException>(() => lifetime.TryRunOriginal(() =>
        {
            acquired = true;
            return Task.CompletedTask;
        }));
        Assert.False(acquired);
        var close = lifetime.RequestShutdownAsync();
        var combined = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Equal(128, combined.InnerExceptions.Count);
        Assert.True(failures.All(original => combined.InnerExceptions.Any(error => ReferenceEquals(original, error))));
        Assert.Equal(1, coreCalls);
        Assert.Equal(new[] { 1 }, exitCodes);
        Assert.True(close.IsCompleted);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Collect(Task original, List<Exception> failures, Exception? exactAssertedExpected = null)
    {
        try { await original; }
        catch (Exception error)
        {
            if (!ReferenceEquals(error, exactAssertedExpected)) Add(failures, error);
        }
    }

    private static void Add(List<Exception> failures, Exception error)
    {
        if (!failures.Any(prior => ReferenceEquals(prior, error))) failures.Add(error);
    }

    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    private sealed class OriginalService : IHomeCoreService
    {
        private readonly Func<Task> _stop;
        internal OriginalService(string id, Func<Task> stop)
        {
            Descriptor = new HomeServiceDescriptor(id, new HomeContractVersion(1, 0, 0),
                HomeServiceLifecycleState.Stopped, false);
            _stop = stop;
        }

        internal int StopCalls { get; private set; }
        public HomeServiceDescriptor Descriptor { get; }
        public IReadOnlyList<string> Dependencies { get; } = new[] { "home.core" };
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return _stop();
        }
    }
}
