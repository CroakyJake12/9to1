using System.IO.Pipes;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Bounded App startup-continuation ownership, using the SAME production
/// adapter, the actual App owner, real held OS I/O and real recovery files.
/// These controls do not substitute a successful Canvas route, Rnote, or installed Home.</summary>
public sealed class UnifiedCanvasInitialRouteTests
{
    [Fact]
    public Task A_live_App_continuation_joins_the_same_actual_source_and_persists_real_completion() => WithRig(async rig =>
    {
        Task? completion = null;
        var admitted = false;
        var startup = rig.Owner.RunAsync(async original =>
        {
            await original.AwaitAsync(rig.Read);
            admitted = await rig.App.TryAwaitOriginalStartupContinuationAsync(original,
                () => completion = rig.Recovery.MarkStartupCompletedAsync(CancellationToken.None));
        });
        Assert.False(rig.Read.IsCompleted); Assert.False(startup.IsCompleted);
        Assert.Null(completion); Assert.False(rig.Completed());
        await rig.ReleaseAsync();
        await startup.WaitAsync(rig.Token);
        Assert.True(admitted); Assert.NotNull(completion);
        Assert.True(completion.IsCompletedSuccessfully); Assert.Equal(1, await rig.Read);
        Assert.True(rig.Completed());
        await rig.Owner.CloseAndDrainAsync().WaitAsync(rig.Token);
    });

    [Fact]
    public Task Accepted_App_retirement_during_a_held_original_prevents_late_completion_admission() => WithRig(async rig =>
    {
        Task? completion = null;
        var admitted = true;
        var startup = rig.Owner.RunAsync(async original =>
        {
            await original.AwaitAsync(rig.Read);
            admitted = await rig.App.TryAwaitOriginalStartupContinuationAsync(original,
                () => completion = rig.Recovery.MarkStartupCompletedAsync(CancellationToken.None));
        });
        Assert.False(rig.Read.IsCompleted); Assert.False(startup.IsCompleted);
        rig.Owner.RequestRetirement();
        var close = Assert.IsAssignableFrom<Task>(rig.Owner.OriginalClose);
        Assert.Same(close, rig.Owner.CloseAndDrainAsync()); Assert.False(close.IsCompleted);
        await rig.ReleaseAsync();
        await startup.WaitAsync(rig.Token); await close.WaitAsync(rig.Token);
        Assert.False(admitted); Assert.Null(completion); Assert.False(rig.Completed());
        Assert.Equal(1, await rig.Read); Assert.True(startup.IsCompletedSuccessfully);
    });

    [Fact]
    public Task Accepted_App_retirement_also_prevents_a_late_synchronous_publication() => WithRig(async rig =>
    {
        var publication = Path.Combine(rig.Paths.DataDirectory, "late-publication.txt");
        var admitted = true;
        var startup = rig.Owner.RunAsync(async original =>
        {
            await original.AwaitAsync(rig.Read);
            admitted = rig.App.TryInvokeOriginalStartupContinuation(original,
                () => File.WriteAllText(publication, "actual synchronous startup effect"));
        });
        Assert.False(rig.Read.IsCompleted); Assert.False(startup.IsCompleted);
        rig.Owner.RequestRetirement();
        var close = Assert.IsAssignableFrom<Task>(rig.Owner.OriginalClose);
        Assert.False(close.IsCompleted);
        await rig.ReleaseAsync(); await startup.WaitAsync(rig.Token); await close.WaitAsync(rig.Token);
        Assert.False(admitted); Assert.False(File.Exists(publication)); Assert.False(rig.Completed());
    });

    [Fact]
    public Task Self_retirement_after_actual_acquisition_keeps_that_source_joined_and_skips_the_next_stage() => WithRig(async rig =>
    {
        Task? captured = null;
        var admitted = false;
        var laterAdmitted = true;
        var startup = rig.Owner.RunAsync(async original =>
        {
            admitted = await rig.App.TryAwaitOriginalStartupContinuationAsync(original, () =>
            {
                captured = rig.Read;
                rig.Owner.RequestRetirement();
                return captured;
            });
            laterAdmitted = await rig.App.TryAwaitOriginalStartupContinuationAsync(original,
                () => rig.Recovery.MarkStartupCompletedAsync(CancellationToken.None));
        });
        Assert.Same(rig.Read, captured);
        var close = Assert.IsAssignableFrom<Task>(rig.Owner.OriginalClose);
        Assert.False(rig.Read.IsCompleted); Assert.False(startup.IsCompleted); Assert.False(close.IsCompleted);
        await rig.ReleaseAsync(); await startup.WaitAsync(rig.Token); await close.WaitAsync(rig.Token);
        Assert.True(admitted); Assert.False(laterAdmitted); Assert.False(rig.Completed());
        Assert.True(rig.Read.IsCompletedSuccessfully);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_post_callback_guard_failure_still_joins_acquired_work_and_retains_opaque_source_siblings(bool nested) => WithRig(async rig =>
    {
        var admission = new IOException("Actual post-callback publication guard failed.");
        var empty = new AggregateException("Foreign empty startup source.");
        Exception source = nested
            ? new AggregateException("Foreign nested startup source.", empty, new OperationCanceledException("Faulted source sibling."))
            : empty;
        Task? captured = null;
        var checks = 0;
        var startup = rig.Owner.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => ++checks == 2 ? throw admission : true);
            await rig.App.TryAwaitOriginalStartupContinuationAsync(original,
                () => captured = rig.Read.ContinueWith(read =>
                {
                    Assert.True(read.IsCompletedSuccessfully);
                    ExceptionDispatchInfo.Capture(source).Throw();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        });
        Assert.NotNull(captured); Assert.False(captured.IsCompleted);
        Assert.False(startup.IsCompleted); Assert.False(rig.Read.IsCompleted);
        rig.Owner.RequestRetirement();
        var close = Assert.IsAssignableFrom<Task>(rig.Owner.OriginalClose);
        Assert.False(close.IsCompleted);
        await rig.ReleaseAsync();
        var failure = await Record.ExceptionAsync(() => startup.WaitAsync(rig.Token));
        Assert.True(captured.IsFaulted); Assert.True(startup.IsFaulted);
        AssertExactCauses(failure, admission, source);
        rig.ExpectedCloseFailure = await Record.ExceptionAsync(() => close.WaitAsync(rig.Token));
        AssertExactCauses(rig.ExpectedCloseFailure, admission, source);
        Assert.False(rig.Completed());
    });

    [Fact]
    public Task A_faulted_actual_OCE_stays_faulted_in_the_App_original_and_cached_close() => WithRig(async rig =>
    {
        var source = new OperationCanceledException("Actual faulted continuation, not a canceled source Task.");
        Task? captured = null;
        var startup = rig.Owner.RunAsync(async original =>
        {
            await rig.App.TryAwaitOriginalStartupContinuationAsync(original,
                () => captured = rig.Read.ContinueWith(read =>
                {
                    Assert.True(read.IsCompletedSuccessfully);
                    throw source;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        });
        Assert.NotNull(captured); Assert.False(captured.IsCompleted); Assert.False(startup.IsCompleted);
        rig.Owner.RequestRetirement();
        var close = Assert.IsAssignableFrom<Task>(rig.Owner.OriginalClose);
        Assert.False(close.IsCompleted);
        await rig.ReleaseAsync();
        AssertExactCauses(await Record.ExceptionAsync(() => startup.WaitAsync(rig.Token)), source);
        Assert.True(captured.IsFaulted); Assert.False(captured.IsCanceled); Assert.True(startup.IsFaulted);
        rig.ExpectedCloseFailure = await Record.ExceptionAsync(() => close.WaitAsync(rig.Token));
        AssertExactCauses(rig.ExpectedCloseFailure, source);
        Assert.True(close.IsFaulted); Assert.Same(close, rig.Owner.CloseAndDrainAsync());
        Assert.False(rig.Completed());
    });

    [Fact]
    public Task Restored_context_cannot_join_the_App_owner_from_its_actual_startup_source() => WithRig(async rig =>
    {
        var neutral = ExecutionContext.Capture()!;
        var guarded = false;
        var startup = rig.Owner.RunAsync(async original =>
        {
            await original.AwaitAsync(rig.Read);
            Assert.True(rig.App.TryInvokeOriginalStartupContinuation(original, () =>
                ExecutionContext.Run(neutral.CreateCopy(), _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => rig.Owner.CloseAndDrainAsync());
                    Assert.Null(rig.Owner.OriginalClose);
                    guarded = true;
                }, null)));
        });
        await rig.ReleaseAsync(); await startup.WaitAsync(rig.Token);
        Assert.True(guarded); Assert.Null(rig.Owner.OriginalClose); Assert.False(rig.Completed());
        await rig.Owner.CloseAndDrainAsync().WaitAsync(rig.Token);
    });

    private static void AssertExactCauses(Exception? actual, params Exception[] expected)
    {
        var group = Assert.IsType<AggregateException>(actual);
        Assert.Equal(expected.Length, group.InnerExceptions.Count);
        foreach (var cause in expected)
            Assert.Contains(group.InnerExceptions, item => ReferenceEquals(item, cause));
    }

    private static async Task WithRig(Func<Rig, Task> body)
    {
        var rig = await Rig.CreateAsync();
        var failures = new List<Exception>();
        try { await body(rig); }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            try { await rig.DisposeAsync(); }
            catch (Exception cause) { failures.Add(cause); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual App continuation and independent cleanup failed.", failures);
    }

    private sealed class Rig : IAsyncDisposable
    {
        internal readonly App App = new();
        internal readonly Paths Paths = new();
        internal readonly DesktopOriginalWorkLifetime Owner;
        private readonly AnonymousPipeServerStream _writer = new(PipeDirection.Out, HandleInheritability.None);
        private readonly AnonymousPipeClientStream _reader;
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(30));
        private readonly ProductionDiagnostics _diagnostics;
        private int _released;
        internal readonly StartupRecoveryCoordinator Recovery;
        internal readonly Task<int> Read;
        internal Exception? ExpectedCloseFailure;
        internal CancellationToken Token => _deadline.Token;

        private Rig()
        {
            Owner = Assert.IsType<DesktopOriginalWorkLifetime>(typeof(App).GetField("_originalAppWork",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(App));
            _reader = new AnonymousPipeClientStream(PipeDirection.In, _writer.ClientSafePipeHandle);
            Read = _reader.ReadAsync(new byte[1].AsMemory(), CancellationToken.None).AsTask();
            _diagnostics = new ProductionDiagnostics(Paths);
            Recovery = new StartupRecoveryCoordinator(Paths, _diagnostics);
        }

        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                await rig.Recovery.BeginStartupAsync(rig.Token);
                Assert.False(rig.Completed()); Assert.False(rig.Read.IsCompleted);
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }

        internal bool Completed()
        {
            var state = JsonNode.Parse(File.ReadAllText(Path.Combine(Paths.DataDirectory, "startup-recovery.json")))!;
            Assert.Equal(Recovery.Current.RunId, state["currentRun"]!["id"]!.GetValue<string>());
            return state["currentRun"]!["startupCompleted"]!.GetValue<bool>();
        }

        internal async Task ReleaseAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            await _writer.WriteAsync(new byte[] { 1 }, CancellationToken.None);
            await _writer.FlushAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            try { await ReleaseAsync(); }
            catch (Exception cause) { failures.Add(cause); _reader.Dispose(); }
            try { Assert.Equal(1, await Read); }
            catch (Exception cause) { failures.Add(cause); }
            Owner.RequestRetirement();
            var close = Owner.CloseAndDrainAsync();
            var closeFailure = await Record.ExceptionAsync(() => close);
            if (ExpectedCloseFailure is not null)
            {
                try { Assert.Same(ExpectedCloseFailure, closeFailure); }
                catch (Exception cause) { failures.Add(cause); }
            }
            else if (closeFailure is not null) failures.Add(closeFailure);
            try { await _diagnostics.DisposeAsync(); }
            catch (Exception cause) { failures.Add(cause); }
            _reader.Dispose(); _writer.Dispose(); _deadline.Dispose();
            RuntimeSafetyState.DisableSafeMode();
            if (Directory.Exists(Paths.DataDirectory)) Directory.Delete(Paths.DataDirectory, true);
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Actual App owner and source cleanup failed.", failures);
        }
    }

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "actual-App-startup-continuation-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
