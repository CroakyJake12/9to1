using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.Accounts;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Accounts.Native;
using NineToOne.Accounts.Remote;

namespace Haven.Desktop.Tests;

public sealed class DesktopOriginalShutdownSequenceTests
{
    [Fact]
    public void Shutdown_presentation_does_not_expand_repeated_aggregate_messages()
    {
        Exception failure = new IOException("Could not close the document.");
        for (var i = 0; i < 100; i++) failure = new AggregateException("Retained owner", failure, failure);
        var presentation = ShutdownFailurePresentation.From(failure);
        Assert.Equal("• Could not close the document.", presentation.Details);
        Assert.DoesNotContain("Retained owner", presentation.Summary);
    }

    [Fact]
    public void Shutdown_log_writes_shared_causal_nodes_once_without_recursive_expansion()
    {
        Exception failure = new IOException("The original leaf.");
        for (var i = 0; i < 100; i++) failure = new AggregateException("Owner", failure, failure);
        using var log = new StringWriter();
        ShutdownFailurePresentation.WriteDiagnostic(log, failure);
        Assert.Equal(1, log.ToString().Split("The original leaf.").Length - 1);
        Assert.Equal(101, log.ToString().Split("[9to1 shutdown cause ").Length - 1);
        Assert.True(log.ToString().Length < 30000);
    }

    [Fact]
    public void Shutdown_presentation_bounds_diagnostic_text_and_retains_original_failure()
    {
        var cause = new IOException(new string('x', 10000));
        var failure = new AggregateException(Enumerable.Range(0, 40).Select(i => new InvalidOperationException("Cause " + i)).Append(cause));
        var presentation = ShutdownFailurePresentation.From(failure);
        Assert.True(presentation.Details.Length < 2200);
        Assert.Contains("application log", presentation.Details);
        Assert.Same(cause, failure.InnerExceptions[^1]);
        Assert.Contains("window has been kept open", presentation.Summary);
    }

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public Task Repeated_close_joins_same_held_startup_and_does_not_prepare_early() => RunCase(async scope =>
    {
        var startup = scope.Hold();
        var startupEntered = NewHold();
        scope.App = () => { startupEntered.TrySetResult(); return startup.Task; };
        var close = scope.Close();
        Assert.Same(close, scope.Close()); Assert.False(close.IsCompleted);
        await startupEntered.Task.WaitAsync(Bound);
        Assert.False(startup.Task.IsCompleted);
        Assert.Empty(scope.Order); Assert.NotNull(scope.Owner.OriginalRequiredDrain);
        startup.TrySetResult(); await close.WaitAsync(Bound);
        Assert.Equal(["save", "prepare", "borrowers", "provider", "final"], scope.Order);
        Assert.Same(scope.Owner.OriginalRequiredDrain, scope.Writer.ObservedDrain);
    });

    [Fact]
    public Task Failed_app_original_still_joins_borrowers_and_keeps_provider_alive() => RunCase(async scope =>
    {
        var original = new IOException("Original startup error.");
        var borrower = scope.Hold(); scope.App = () => Task.FromException(original); scope.Borrowers = () => borrower.Task;
        var close = scope.Close(); await scope.BorrowerEntered.Task.WaitAsync(Bound);
        Assert.False(close.IsCompleted); Assert.DoesNotContain("provider", scope.Order); Assert.DoesNotContain("prepare", scope.Order);
        borrower.TrySetResult(); DemandCauses(await Fault(close), original);
        Assert.Equal(["borrowers"], scope.Order); Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Borrower_raw_siblings_and_independent_cleanup_are_preserved_before_provider_teardown() => RunCase(async scope =>
    {
        var first = new IOException("Actual borrower one."); var second = new InvalidOperationException("Actual borrower two.");
        var borrower = scope.Hold(); scope.Borrowers = () => borrower.Task;
        var close = scope.Close(); await scope.BorrowerEntered.Task.WaitAsync(Bound);
        borrower.TrySetException([first, second]); DemandCauses(await Fault(close), first, second);
        Assert.Equal(2, scope.Owner.OriginalFailures.Count); Assert.DoesNotContain("provider", scope.Order);
        Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Preparation_audit_must_settle_before_borrowed_diagnostics_provider_dispose() => RunCase(async scope =>
    {
        var audit = scope.Hold<IStartupRecoveryFinalCleanWriter>(); scope.Prepare = () => audit.Task;
        var close = scope.Close(); await scope.PrepareEntered.Task.WaitAsync(Bound);
        Assert.False(close.IsCompleted); Assert.DoesNotContain("provider", scope.Order); Assert.DoesNotContain("borrowers", scope.Order);
        audit.TrySetResult(scope.Writer); await close.WaitAsync(Bound);
        Assert.True(scope.Writer.ObservedDrain!.IsCompletedSuccessfully);
        Assert.Equal(["save", "prepare", "borrowers", "provider", "final"], scope.Order);
    });

    [Fact]
    public Task Held_provider_close_prevents_final_writer_even_after_audit_and_children() => RunCase(async scope =>
    {
        var provider = scope.Hold(); scope.Provider = () => provider.Task;
        var close = scope.Close(); await scope.ProviderEntered.Task.WaitAsync(Bound);
        Assert.False(close.IsCompleted); Assert.Equal(0, scope.Writer.Calls);
        provider.TrySetResult(); await close.WaitAsync(Bound);
        Assert.True(scope.Writer.ObservedDrain!.IsCompletedSuccessfully);
        Assert.Contains(provider.Task, scope.Owner.ActualSourceTasks);
    });

    [Fact]
    public Task Faulted_provider_close_is_uncertain_and_never_calls_final_writer() => RunCase(async scope =>
    {
        var first = new IOException("Actual provider disposal one."); var second = new InvalidOperationException("Actual provider disposal two.");
        var provider = scope.Hold(); scope.Provider = () => provider.Task;
        var close = scope.Close(); await scope.ProviderEntered.Task.WaitAsync(Bound);
        provider.TrySetException([first, second]); DemandCauses(await Fault(close), first, second);
        Assert.Equal(0, scope.Writer.Calls); Assert.False(scope.Owner.OriginalRequiredDrain!.IsCompletedSuccessfully);
        Assert.Same(close, scope.Close()); Assert.Equal(1, scope.ProviderCalls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Direct_callback_cancellation_is_faulted_and_retains_actual_reference() => RunCase(async scope =>
    {
        var original = new OperationCanceledException("Actual direct startup callback."); scope.App = () => throw original;
        var close = scope.Close(); DemandCauses(await Fault(close), original);
        Assert.True(close.IsFaulted); Assert.Contains(scope.Owner.OriginalFailures, error => ReferenceEquals(error, original));
        Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Opaque_empty_original_aggregate_is_not_replaced_by_success() => RunCase(async scope =>
    {
        var original = new AggregateException("Actual opaque source group."); scope.App = () => Task.FromException(original);
        DemandCauses(await Fault(scope.Close()), original); Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Actual_callback_join_is_refused_before_any_self_join_task_is_returned() => RunCase(async scope =>
    {
        Exception? refusal = null;
        scope.App = () =>
        {
            try { scope.Close(); } catch (Exception error) { refusal = error; }
            Assert.NotNull(scope.Owner.OriginalShutdown); Assert.NotNull(scope.Owner.OriginalRequiredDrain);
            return Task.CompletedTask;
        };
        await scope.Close().WaitAsync(Bound);
        Assert.IsType<InvalidOperationException>(refusal); Assert.Equal(1, scope.Writer.Calls);
    });

    [Fact]
    public Task Detached_context_after_actual_callback_retires_can_join_normally() => RunCase(async scope =>
    {
        var entered = scope.Hold(); var release = scope.Hold(); Task? external = null;
        scope.App = () =>
        {
            external = scope.Track(Task.Run(async () => { entered.TrySetResult(); await release.Task; await scope.Close(); }));
            return Task.CompletedTask;
        };
        var close = scope.Close(); await entered.Task.WaitAsync(Bound); await close.WaitAsync(Bound);
        release.TrySetResult(); await external!.WaitAsync(Bound);
        Assert.Equal(1, scope.Writer.Calls);
    });

    [Fact]
    public Task Final_writer_fault_is_preserved_and_never_retried_or_described_as_no_effect() => RunCase(async scope =>
    {
        var actual = new IOException("Actual final writer may have committed before failure."); scope.Writer.Complete = () => Task.FromException(actual);
        var close = scope.Close(); DemandCauses(await Fault(close), actual);
        Assert.Same(close, scope.Close()); Assert.Equal(1, scope.Writer.Calls);
        Assert.Contains(scope.Owner.OriginalFailures, error => ReferenceEquals(error, actual)); scope.ExpectFault = true;
    });

    [Fact]
    public Task External_start_callback_must_return_its_actual_task_before_any_phase_joins() => RunCase(async scope =>
    {
        var scheduling = scope.Hold(); Action? signal = null;
        scope.Schedule = action => { signal = action; scope.ReleaseOnCleanup(action); return scheduling.Task; };
        var close = scope.Close(); Assert.NotNull(signal); Assert.Empty(scope.Order);
        signal!(); Assert.Empty(scope.Order); Assert.False(close.IsCompleted);
        scheduling.TrySetResult(); await close.WaitAsync(Bound);
        Assert.Contains(scheduling.Task, scope.Owner.ActualSourceTasks);
        Assert.Equal(1, scope.Writer.Calls);
    });

    [Fact]
    public Task Failed_actual_external_start_retains_all_siblings_and_never_begins_teardown() => RunCase(async scope =>
    {
        var a = new IOException("Actual dispatcher original one."); var b = new InvalidOperationException("Actual dispatcher original two.");
        var scheduling = scope.Hold();
        scope.Schedule = signal => { signal(); return scheduling.Task; };
        var close = scope.Close(); scheduling.TrySetException([a, b]);
        DemandCauses(await Fault(close), a, b);
        Assert.Empty(scope.Order); Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Direct_external_start_cancellation_preserves_original_unknown_source_fault() => RunCase(async scope =>
    {
        var actual = new OperationCanceledException("Actual scheduling callback returned no canceled original task.");
        scope.Schedule = _ => throw actual;
        var close = scope.Close(); DemandCauses(await Fault(close), actual);
        Assert.True(close.IsFaulted); Assert.Empty(scope.Order); scope.ExpectFault = true;
    });

    [Fact]
    public Task Actual_async_scheduling_source_refuses_its_own_encompassing_join() => RunCase(async scope =>
    {
        InvalidOperationException? refusal = null;
        scope.Schedule = signal => scope.Track(Task.Run(async () =>
        {
            await Task.Yield();
            refusal = Assert.Throws<InvalidOperationException>(() => { _ = scope.Close(); });
            signal();
        }));
        await scope.Close().WaitAsync(Bound);
        Assert.NotNull(refusal); Assert.Equal(1, scope.Writer.Calls);
    });

    [Fact]
    public async Task Actual_early_startup_retirement_closes_cleanly_without_completing_startup_or_first_run()
    {
        using var paths = new RetirementPaths();
        var diagnostics = new ProductionDiagnostics(paths);
        var recovery = new StartupRecoveryCoordinator(paths, diagnostics);
        var recording = new RetiredStartupRecordingSource(recovery);
        var started = NewHold();
        var stopObserved = NewHold();
        var borrowersEntered = NewHold();
        var borrowers = NewHold();
        var appWork = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var startup = appWork.RunAsync(async original =>
        {
            await recovery.BeginStartupAsync(CancellationToken.None);
            started.SetResult();
            using var stop = original.Token.Register(() => stopObserved.TrySetResult());
            await stopObserved.Task;
            // Real early-return producer: close stops admission before the startup-complete write.
        });
        var failures = new List<Exception>();
        Task? close = null;
        try
        {
            await started.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.False(startup.IsCompleted);
            var sequence = new DesktopOriginalShutdownSequence(async () =>
            {
                var originalFailures = new List<Exception>();
                await App.JoinEarlyStartupCooperativelyAsync(appWork, startup, originalFailures);
                Assert.Empty(originalFailures);
            }, () => Task.CompletedTask,
                () => App.PrepareFinalWriterAfterOriginalAppRetirementAsync(appWork, recording),
                () => { borrowersEntered.TrySetResult(); return borrowers.Task; },
                () => diagnostics.DisposeAsync().AsTask());
            close = sequence.CloseAndDrainAsync();
            await borrowersEntered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.True(startup.IsCompletedSuccessfully);
            Assert.NotNull(appWork.OriginalClose);
            Assert.True(appWork.OriginalClose.IsCompletedSuccessfully);
            Assert.Same(appWork.OriginalClose, recording.Retirement);
            Assert.Equal(0, recording.StrictCalls);
            Assert.False(close.IsCompleted);
            var statePath = Path.Combine(paths.DataDirectory, "startup-recovery.json");
            var pending = JsonNode.Parse(await File.ReadAllTextAsync(statePath, TestContext.Current.CancellationToken))!;
            Assert.False(pending["currentRun"]!["cleanShutdown"]!.GetValue<bool>());
            Assert.False(pending["currentRun"]!["startupCompleted"]!.GetValue<bool>());
            borrowers.SetResult();
            await close.WaitAsync(Bound, TestContext.Current.CancellationToken);
            var clean = JsonNode.Parse(await File.ReadAllTextAsync(statePath, TestContext.Current.CancellationToken))!;
            Assert.True(clean["currentRun"]!["cleanShutdown"]!.GetValue<bool>());
            Assert.False(clean["currentRun"]!["startupCompleted"]!.GetValue<bool>());

            // The next real recovery starts normally; the separate durable first-run
            // service still requires Welcome and has no invented completion record.
            await using var returningDiagnostics = new ProductionDiagnostics(paths);
            var returning = await new StartupRecoveryCoordinator(paths, returningDiagnostics).BeginStartupAsync(CancellationToken.None);
            Assert.Equal(0, returning.RecentUncleanStarts);
            Assert.False(returning.IsSafeMode);
            var onboarding = new HomeOnboardingService(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home-core-state.json")));
            var firstRun = await onboarding.ReadCompletionAsync("early-close-test-profile", CancellationToken.None);
            Assert.Equal(HomeOnboardingReadState.NotCompleted, firstRun.State);
            Assert.False(firstRun.IsCompleted);
            var welcome = onboarding.Open();
            Assert.Equal(HomeOnboardingStep.Welcome, welcome.Step);
            Assert.False(welcome.Completed);
        }
        catch (Exception primary) { failures.Add(primary); }
        finally
        {
            stopObserved.TrySetResult(); borrowers.TrySetResult();
            foreach (var original in new[] { startup, close, appWork.CloseAndDrainAsync() }.OfType<Task>())
                try { await original.WaitAsync(Bound, CancellationToken.None); }
                catch (Exception cleanup) { failures.Add(cleanup); }
            try { await diagnostics.DisposeAsync(); } catch (Exception cleanup) { failures.Add(cleanup); }
            RuntimeSafetyState.DisableSafeMode();
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual early-close body and originals retained.", failures);
    }

    [Fact]
    public async Task Final_writer_preparation_refuses_an_unfinished_actual_App_retirement()
    {
        using var paths = new RetirementPaths();
        await using var diagnostics = new ProductionDiagnostics(paths);
        var recovery = new RetiredStartupRecordingSource(new StartupRecoveryCoordinator(paths, diagnostics));
        var held = NewHold();
        var appWork = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => held.Task);
        appWork.RequestRetirement();
        try
        {
            Assert.NotNull(appWork.OriginalClose);
            Assert.False(appWork.OriginalClose.IsCompleted);
            Assert.Throws<InvalidOperationException>(() =>
            { _ = App.PrepareFinalWriterAfterOriginalAppRetirementAsync(appWork, recovery); });
            Assert.Null(recovery.Retirement);
            Assert.Equal(0, recovery.StrictCalls);
        }
        finally { held.TrySetResult(); await appWork.CloseAndDrainAsync().WaitAsync(Bound, CancellationToken.None); }
    }

    [Fact]
    public async Task Older_recovery_source_keeps_its_strict_completed_startup_contract()
    {
        var scope = new Scope();
        var strict = new StrictOnlyRecoverySource(scope.Writer);
        var appWork = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        await appWork.CloseAndDrainAsync();
        var writer = await App.PrepareFinalWriterAfterOriginalAppRetirementAsync(appWork, strict);
        Assert.Same(scope.Writer, writer);
        Assert.Equal(1, strict.Calls);
    }

    [AvaloniaFact]
    public async Task Actual_DI_disposal_after_an_async_hop_reuses_the_terminal_native_account_close()
    {
        var session = new AccountRetirementSession(Task.CompletedTask);
        var gate = new AsyncDisposalGate();
        await using var provider = new ServiceCollection()
            .AddSingleton(_ => new NativeCakeAccountUiOwner(session))
            .AddSingleton(_ => gate).BuildServiceProvider();
        var owner = provider.GetRequiredService<NativeCakeAccountUiOwner>();
        _ = provider.GetRequiredService<AsyncDisposalGate>(); // Reverse disposal awaits this one FIRST.
        var original = owner.CloseAndDrainAsync();
        await original;
        var disposal = provider.DisposeAsync().AsTask();
        try
        {
            await gate.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
            Assert.False(disposal.IsCompleted);
            // Microsoft's actual DI loop resumes off-context after this held async disposable.
            await Task.Run(() => gate.Release.TrySetResult());
            await disposal.WaitAsync(Bound, TestContext.Current.CancellationToken);
            await Task.Run(async () =>
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                var repeated = owner.DisposeAsync().AsTask();
                Assert.Same(original, repeated);
                await repeated;
            });
            Assert.Equal(1, session.CloseCalls);
        }
        finally { gate.Release.TrySetResult(); await disposal.WaitAsync(Bound, CancellationToken.None); }
    }

    [AvaloniaFact]
    public async Task Terminal_native_account_disposal_preserves_the_same_failed_original_off_thread()
    {
        var cause = new IOException("The original native account retirement failed.");
        var session = new AccountRetirementSession(Task.FromException(cause));
        var owner = new NativeCakeAccountUiOwner(session);
        var original = owner.CloseAndDrainAsync();
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => original));
        await Task.Run(async () =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            var repeated = owner.DisposeAsync().AsTask();
            Assert.Same(original, repeated);
            Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => repeated));
        });
        Assert.Equal(1, session.CloseCalls);
    }

    [AvaloniaFact]
    public async Task First_and_pending_native_account_disposal_keep_the_actual_UI_thread_guard()
    {
        var held = NewHold();
        var session = new AccountRetirementSession(held.Task);
        var owner = new NativeCakeAccountUiOwner(session);
        await Task.Run(() =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Assert.Throws<InvalidOperationException>(() => { _ = owner.DisposeAsync(); });
        });
        Assert.Equal(0, session.CloseCalls);
        var original = owner.CloseAndDrainAsync();
        try
        {
            Assert.False(original.IsCompleted);
            await Task.Run(() =>
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                Assert.Throws<InvalidOperationException>(() => { _ = owner.DisposeAsync(); });
            });
            Assert.Equal(1, session.CloseCalls);
        }
        finally { held.TrySetResult(); await original.WaitAsync(Bound, CancellationToken.None); }
    }

    private sealed class AsyncDisposalGate : IAsyncDisposable
    {
        internal readonly TaskCompletionSource Entered = NewHold();
        internal readonly TaskCompletionSource Release = NewHold();
        public ValueTask DisposeAsync() { Entered.TrySetResult(); return new(Release.Task); }
    }

    private sealed class AccountRetirementSession(Task originalClose) : INativeCakeAccountSession
    {
        internal int CloseCalls;
        public bool IsAvailable => false;
        public bool TryGetCurrentSnapshot(out NativeCakeAccountSnapshot? snapshot) { snapshot = null; return false; }
        public Task<NativeCakeAccountSnapshot> SignInAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteCurrent>> CurrentAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteProfile>> ProfileAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteSession[]>> SessionsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteMutationAcknowledgement>> SignOutAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeSessionAsync(Guid sessionId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ApiResult<RemoteMutationAcknowledgement>> RevokeOtherSessionsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task CloseAndDrainAsync() { CloseCalls++; return originalClose; }
        public ValueTask DisposeAsync() => new(originalClose);
    }

    private sealed class StrictOnlyRecoverySource(IStartupRecoveryFinalCleanWriter writer) : IStartupRecoveryFinalCleanWriterSource
    {
        internal int Calls;
        public Task<IStartupRecoveryFinalCleanWriter> PrepareFinalCleanWriterAsync(CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(writer); }
    }

    private sealed class RetiredStartupRecordingSource(StartupRecoveryCoordinator original) :
        IStartupRecoveryFinalCleanWriterSource, IStartupRecoveryRetiredStartupFinalCleanWriterSource
    {
        internal Task? Retirement;
        internal int StrictCalls;
        public Task<IStartupRecoveryFinalCleanWriter> PrepareFinalCleanWriterAsync(CancellationToken cancellationToken)
        { StrictCalls++; return original.PrepareFinalCleanWriterAsync(cancellationToken); }
        public Task<IStartupRecoveryFinalCleanWriter> PrepareFinalCleanWriterAfterStartupRetirementAsync(
            Task actualStartupRetirement, CancellationToken cancellationToken)
        {
            Retirement = actualStartupRetirement;
            return original.PrepareFinalCleanWriterAfterStartupRetirementAsync(actualStartupRetirement, cancellationToken);
        }
    }

    private sealed class RetirementPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "9to1-actual-early-close-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true); }
    }

    private sealed class Writer(Scope scope) : IStartupRecoveryFinalCleanWriter
    {
        internal int Calls; internal Task? ObservedDrain; internal Func<Task> Complete = () => Task.CompletedTask;
        public Task CompleteAfterOriginalDrainAsync(Task actualRequiredOriginalDrain, CancellationToken cancellationToken)
        {
            Calls++; ObservedDrain = actualRequiredOriginalDrain; scope.Order.Add("final");
            Assert.True(actualRequiredOriginalDrain.IsCompletedSuccessfully);
            Assert.Contains("provider", scope.Order); Assert.Equal(CancellationToken.None, cancellationToken);
            return Complete();
        }
    }
    private sealed class Scope
    {
        private readonly List<Action> _release = [];
        private readonly List<Task> _extraOriginals = [];
        internal readonly List<string> Order = [];
        internal readonly TaskCompletionSource BorrowerEntered = NewHold();
        internal readonly TaskCompletionSource ProviderEntered = NewHold();
        internal readonly TaskCompletionSource PrepareEntered = NewHold();
        internal readonly DesktopOriginalShutdownSequence Owner;
        internal readonly Writer Writer;
        internal Func<Task> App = () => Task.CompletedTask;
        internal Func<Task> Save = () => Task.CompletedTask;
        internal Func<Task<IStartupRecoveryFinalCleanWriter>> Prepare;
        internal Func<Task> Borrowers = () => Task.CompletedTask;
        internal Func<Task> Provider = () => Task.CompletedTask;
        internal Func<Action, Task> Schedule = action => { action(); return Task.CompletedTask; };
        internal bool ExpectFault; internal int ProviderCalls;
        internal Scope()
        {
            Writer = new(this); Prepare = () => Task.FromResult<IStartupRecoveryFinalCleanWriter>(Writer);
            Owner = new(() => App(), () => { Order.Add("save"); return Save(); },
                () => { Order.Add("prepare"); PrepareEntered.TrySetResult(); return Prepare(); },
                () => { Order.Add("borrowers"); BorrowerEntered.TrySetResult(); return Borrowers(); },
                () => { ProviderCalls++; Order.Add("provider"); ProviderEntered.TrySetResult(); return Provider(); },
                action => Schedule(action));
        }
        internal Task Close() => Owner.CloseAndDrainAsync();
        internal Task Track(Task actual) { _extraOriginals.Add(actual); return actual; }
        internal void ReleaseOnCleanup(Action actual) => _release.Add(actual);
        internal TaskCompletionSource Hold() { var held = NewHold(); _release.Add(() => held.TrySetResult()); return held; }
        internal TaskCompletionSource<T> Hold<T>()
        {
            var held = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _release.Add(() => held.TrySetResult((T)(object)Writer)); return held;
        }
        internal async Task Cleanup()
        {
            foreach (var release in _release) release();
            var actual = Close(); var failures = new List<Exception>();
            foreach (var original in _extraOriginals.Append(actual).Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await original.WaitAsync(Bound); }
                catch (Exception error) { if (!(ExpectFault && original.IsFaulted)) failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Actual fixture close and every detached original are independently joined.", failures);
        }
    }
    private static TaskCompletionSource NewHold() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task RunCase(Func<Scope, Task> body)
    {
        var scope = new Scope(); var failures = new List<Exception>();
        try { await body(scope); } catch (Exception primary) { failures.Add(primary); }
        finally { try { await scope.Cleanup(); } catch (Exception cleanup) { failures.Add(cleanup); } }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual fixture body and cleanup preserved.", failures);
    }
    private static async Task<Exception> Fault(Task actual)
    {
        try { await actual.WaitAsync(Bound); } catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected an actual original failure.");
    }
    private static void DemandCauses(Exception actual, params Exception[] expected)
    {
        var all = new List<Exception>();
        void Walk(Exception error) { all.Add(error); if (error is AggregateException group) foreach (var inner in group.InnerExceptions) Walk(inner); }
        Walk(actual);
        foreach (var cause in expected) Assert.Contains(all, error => ReferenceEquals(error, cause));
    }
}
