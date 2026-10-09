using System.Text;
using System.Text.Json;
using NineToOne.Accounts.Native;

namespace NineToOne.Accounts.Remote.Specs;

// Genuine producer and actual managed stream/exit Tasks, with a synthetic child host.
// No Windows process, Access credential, signed CAKE identity or protected-path proof.
internal static class CloudflaredLifetimeControls
{
    internal static async Task<int> RunAsync()
    {
        var failed = 0; var passed = 0;
        foreach (var control in All)
        {
            try { await control.Run(); passed++; Console.WriteLine("PASS " + control.Name); }
            catch (Exception failure) { failed++; Console.Error.WriteLine("FAIL " + control.Name + "\n" + failure); }
        }
        Console.WriteLine($"Cloudflared synthetic process controls: {passed} passed, {failed} failed, 0 skipped.");
        return failed == 0 ? 0 : 1;
    }

    internal static IEnumerable<(string Name, Func<Task> Run)> All => new (string, Func<Task>)[]
    {
        ("helper_coalesces_login_token_and_retains_original_exit", CoalescesAsync),
        ("helper_close_seals_before_a_partial_create_can_start", CreateRetirementAsync),
        ("helper_caller_withdrawal_does_not_stop_child_and_close_joins_stream", CallerWithdrawalAsync),
        ("helper_close_is_published_before_reentrant_start_callback", ReentrantStartAsync),
        ("helper_uncertain_start_fault_keeps_real_exit_and_streams", StartFaultAsync),
        ("helper_retains_compound_read_exit_and_unknown_settlement", CompoundAsync),
        ("helper_independently_retains_stream_child_and_image_cleanup_faults", CleanupFaultsAsync),
        ("helper_overflow_retains_late_read_fault_without_disclosure", OverflowAsync),
        ("helper_expiry_refuses_disclosure_without_restart", ExpiryAsync),
        ("helper_empty_missing_and_expired_token_output_are_refused", InvalidOutputAsync),
        ("helper_waiter_capacity_is_sticky_and_close_retains_refusal", CapacityAsync),
        ("helper_wrong_origin_is_refused_before_pin_or_start", WrongOriginAsync)
    };

    private static Task CoalescesAsync() => WithAsync(async rig =>
    {
        var login = new Child(new MemoryStream(), new MemoryStream(), initiallyExited: false);
        var token = rig.TokenChild(); rig.Host.Children.AddRange([login, token]);
        var a = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        var b = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        await CapturedAsync(rig, login);
        var acquisition = rig.Source.OriginalAcquisitionTask!;
        Check.False(acquisition.IsCompleted); Check.False(a.IsCompleted); Check.False(b.IsCompleted);
        Check.Same(login.Exit.Task, rig.Source.OriginalHelperExitTasks.Single());
        Check.Equal(1, rig.Host.Pins); Check.Equal(1, rig.Host.Creates); Check.Equal(0, rig.Host.Lease.Disposals);
        login.Exit.SetResult();
        Check.Equal(rig.Token, await a); Check.Equal(rig.Token, await b);
        Check.Same(acquisition, rig.Source.OriginalAcquisitionTask);
        Check.Equal(2, rig.Host.Creates); Check.Equal(1, login.Starts); Check.Equal(1, token.Starts);
        Check.True(rig.Host.Arguments[0].SequenceEqual(new[] { "access", "login", "--quiet", "--app=" + Origin.AbsoluteUri + "account" }));
        Check.True(rig.Host.Arguments[1].SequenceEqual(new[] { "access", "token", "--app=" + Origin.AbsoluteUri + "account" }));
        Check.Equal(1, rig.Host.Lease.Disposals);
        var close = rig.Track(rig.Source.CloseAndDrainAsync()); Check.Same(close, rig.Source.CloseAndDrainAsync()); await close;
        Check.Equal(0, login.Kills); Check.Equal(0, token.Kills);
    });

    private static Task CreateRetirementAsync() => WithAsync(async rig =>
    {
        var entered = Gate(); var release = rig.Own(new ManualResetEventSlim());
        var child = rig.TokenChild(); rig.Host.Children.Add(child); rig.Release(release.Set);
        rig.Host.OnCreate = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic Create was not released."); };
        var waiter = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = rig.Track(rig.Source.CloseAndDrainAsync());
        Check.Same(close, rig.Source.CloseAndDrainAsync()); Check.False(close.IsCompleted);
        release.Set();
        await rig.ExpectCancellationAsync(waiter); await rig.ExpectCancellationAsync(close);
        Check.Equal(0, child.Starts); Check.Equal(1, child.Disposals); Check.Equal(0, child.ExitAcquisitions);
        Check.Equal(1, rig.Host.Lease.Disposals);
    });

    private static Task CallerWithdrawalAsync() => WithAsync(async rig =>
    {
        using var caller = new CancellationTokenSource();
        var output = new HeldEofStream(); rig.Release(output.End);
        var login = new Child(output, new MemoryStream(), initiallyExited: false);
        rig.Host.Children.Add(login);
        var waiter = rig.Track(rig.Source.AcquireForRequestAsync(Origin, caller.Token).AsTask());
        await CapturedAsync(rig, login); await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel(); await rig.ExpectCancellationAsync(waiter, caller.Token);
        Check.Equal(0, login.Kills); Check.False(rig.Source.OriginalAcquisitionTask!.IsCompleted);
        var close = rig.Track(rig.Source.CloseAndDrainAsync());
        await login.Killed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check.Equal(1, login.Kills); Check.False(close.IsCompleted);
        Check.Same(login.Exit.Task, rig.Source.OriginalHelperExitTasks.Single());
        output.End(); await rig.ExpectCancellationAsync(close);
        Check.Equal(1, login.Disposals); Check.Equal(1, output.Disposals); Check.Equal(1, rig.Host.Lease.Disposals);
    });

    private static Task ReentrantStartAsync() => WithAsync(async rig =>
    {
        Task? callbackClose = null;
        var login = new Child(new MemoryStream(), new MemoryStream(), initiallyExited: false);
        login.OnStart = () => { callbackClose = rig.Source.CloseAndDrainAsync(); Check.Same(callbackClose, rig.Source.CloseAndDrainAsync()); Check.False(callbackClose.IsCompleted); };
        rig.Host.Children.Add(login);
        var waiter = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        await CapturedAsync(rig, login);
        var close = rig.Track(callbackClose ?? throw new InvalidOperationException("Start callback did not request close."));
        await rig.ExpectCancellationAsync(waiter); await rig.ExpectCancellationAsync(close);
        Check.Equal(1, login.Starts); Check.Equal(1, login.Kills); Check.Equal(1, login.Disposals);
        Check.Equal(1, rig.Host.Creates);
    });

    private static Task CompoundAsync() => WithAsync(async rig =>
    {
        var e1 = new IOException("Synthetic first read"); var e2 = new IOException("Synthetic second read");
        var e3 = new IOException("Synthetic original exit");
        var read = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        read.SetException(new[] { e1, e2 });
        var output = new OriginalReadStream(read.Task);
        var child = new Child(output, new MemoryStream(), initiallyExited: false); child.Exit.SetException(e3);
        rig.Host.Children.Add(child);
        var waiter = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        await rig.ExpectFaultsAsync(waiter, e1, e2, e3);
        Check.Same(read.Task, output.Original); Check.Equal(1, output.Reads);
        Check.Same(child.Exit.Task, rig.Source.OriginalHelperExitTasks.Single());
        var close = rig.Track(rig.Source.CloseAndDrainAsync()); var failure = await rig.ExpectFaultsAsync(close, e1, e2, e3);
        Check.True(Leaves(failure).OfType<IOException>().Any(x => x.Message.Contains("unverified", StringComparison.Ordinal)));
        Check.Equal(0, child.Disposals); Check.Equal(0, rig.Host.Lease.Disposals);
        Check.Same(close, rig.Source.CloseAndDrainAsync());
    });

    private static Task StartFaultAsync() => WithAsync(async rig =>
    {
        var exact = new IOException("Synthetic Start fault after native acquisition.");
        var child = new Child(new MemoryStream(), new MemoryStream(), initiallyExited: false);
        child.OnStart = () => throw exact; rig.Host.Children.Add(child);
        var waiter = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        await CapturedAsync(rig, child);
        Check.False(waiter.IsCompleted); Check.Equal(1, child.Starts); Check.Equal(1, child.ExitAcquisitions);
        Check.Same(child.Exit.Task, rig.Source.OriginalHelperExitTasks.Single());
        var close = rig.Track(rig.Source.CloseAndDrainAsync());
        await rig.ExpectFaultsAsync(waiter, exact); await rig.ExpectFaultsAsync(close, exact);
        Check.Equal(1, child.Kills); Check.Equal(1, child.Disposals); Check.Equal(1, rig.Host.Lease.Disposals);
    });

    private static Task CleanupFaultsAsync() => WithAsync(async rig =>
    {
        var a = new IOException("Synthetic stdout close"); var b = new IOException("Synthetic stderr close");
        var c = new IOException("Synthetic child close"); var d = new IOException("Synthetic image close");
        var output = new DisposalStream(a); var error = new DisposalStream(b);
        var login = new Child(output, error) { DisposeError = c }; rig.Host.Lease.Error = d; rig.Host.Children.Add(login);
        var waiter = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        await rig.ExpectFaultsAsync(waiter, a, b, c, d);
        var close = rig.Track(rig.Source.CloseAndDrainAsync()); await rig.ExpectFaultsAsync(close, a, b, c, d);
        Check.Equal(1, output.Disposals); Check.Equal(1, error.Disposals); Check.Equal(1, login.Disposals);
        Check.Equal(1, rig.Host.Lease.Disposals); Check.Equal(1, rig.Host.Creates);
    });

    private static Task OverflowAsync() => WithAsync(async rig =>
    {
        var late = new IOException("Synthetic late original read");
        var output = new OverflowStream(late); var login = new Child(output, new MemoryStream()); rig.Host.Children.Add(login);
        var waiter = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        var failure = await rig.ExpectFaultsAsync(waiter, late);
        Check.True(Leaves(failure).OfType<IOException>().Any(x => x.Message.Contains("capture limit", StringComparison.Ordinal)));
        Check.True(output.Delivered > 16384); Check.True(output.Reads > 4);
        Check.False(failure.ToString().Contains("SYNTHETIC_PRIVATE_CAPTURE", StringComparison.Ordinal));
        var close = rig.Track(rig.Source.CloseAndDrainAsync()); await rig.ExpectFaultsAsync(close, late);
        Check.Equal(1, login.Disposals); Check.Equal(1, rig.Host.Lease.Disposals);
    });

    private static Task ExpiryAsync() => WithAsync(async rig =>
    {
        rig.Host.Children.AddRange([new Child(new MemoryStream(), new MemoryStream()), rig.TokenChild()]);
        var first = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask()); Check.Equal(rig.Token, await first);
        rig.Clock.Now = rig.Clock.Now.AddMinutes(5);
        var expired = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        var exact = await rig.ExpectFaultsAsync(expired); Check.True(exact is IOException);
        var repeated = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
        var same = await rig.ExpectFaultsAsync(repeated); Check.Same(exact, same);
        Check.Equal(2, rig.Host.Creates); Check.Equal(1, rig.Host.Pins);
        var close = rig.Track(rig.Source.CloseAndDrainAsync()); await rig.ExpectFaultsAsync(close, exact);
    });

    private static async Task InvalidOutputAsync()
    {
        foreach (var kind in new[] { "empty", "missing-exp", "expired" })
            await WithAsync(async rig =>
            {
                var value = kind == "empty" ? "" : Token(kind == "missing-exp" ? new { subject = "synthetic" } : new { exp = rig.Clock.Now.ToUnixTimeSeconds() });
                rig.Host.Children.AddRange([new Child(new MemoryStream(), new MemoryStream()), rig.TokenChild(value)]);
                var waiter = rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask());
                var refusal = await rig.ExpectFaultsAsync(waiter); Check.True(refusal is IOException);
                var close = rig.Track(rig.Source.CloseAndDrainAsync()); await rig.ExpectFaultsAsync(close, refusal);
                Check.Equal(2, rig.Host.Creates); Check.Equal(1, rig.Host.Lease.Disposals);
            });
    }

    private static Task CapacityAsync() => WithAsync(async rig =>
    {
        var login = new Child(new MemoryStream(), new MemoryStream(), initiallyExited: false); rig.Host.Children.Add(login);
        var waiters = new List<Task<string?>>();
        waiters.Add(rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask()));
        await CapturedAsync(rig, login);
        for (var i = 1; i < 128; i++) waiters.Add(rig.Track(rig.Source.AcquireForRequestAsync(Origin, default).AsTask()));
        var refusal = Capture(() => rig.Source.AcquireForRequestAsync(Origin, default)); Check.True(refusal is InvalidOperationException);
        Check.Same(refusal, Capture(() => rig.Source.AcquireForRequestAsync(Origin, default)));
        var close = rig.Track(rig.Source.CloseAndDrainAsync());
        foreach (var waiter in waiters) await rig.ExpectCancellationAsync(waiter);
        var failure = await rig.ExpectCancellationAndFaultAsync(close, refusal);
        Check.ContainsSame(refusal, failure); Check.Equal(1, rig.Host.Creates); Check.Equal(1, login.Kills);
        Check.True(Capture(() => rig.Source.AcquireForRequestAsync(Origin, default)) is ObjectDisposedException);
        Check.Same(close, rig.Source.CloseAndDrainAsync());
    });

    private static Task WrongOriginAsync() => WithAsync(async rig =>
    {
        Check.True(Capture(() => rig.Source.AcquireForRequestAsync(new Uri("https://different.invalid/"), default)) is UnauthorizedAccessException);
        Check.True(Capture(() => rig.Source.AcquireForRequestAsync(new Uri(Origin, "account"), default)) is UnauthorizedAccessException);
        Check.Equal(0, rig.Host.Pins); Check.Equal(0, rig.Host.Creates);
        var close = rig.Track(rig.Source.CloseAndDrainAsync()); await close;
    });

    private static readonly Uri Origin = new(CloudflaredNativeCakeAccessCredentialSource.ApplicationOrigin);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task CapturedAsync(Rig rig, Child child)
    {
        await child.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The callback records its return before the producer can publish that returned
        // Task. Wait for the real producer slot, rather than infer publication from it.
        var until = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!rig.Source.OriginalHelperExitTasks.Any(x => ReferenceEquals(x, child.Exit.Task)))
        {
            if (DateTimeOffset.UtcNow >= until) throw new TimeoutException("Original helper exit was not published.");
            await Task.Yield();
        }
    }
    private static string Token(object payload) => Program.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}")) + "." + Program.Encode(JsonSerializer.SerializeToUtf8Bytes(payload)) + "." + Program.Encode(new byte[32]);
    private static Exception Capture(Action action) { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("Expected synchronous refusal."); }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group && group.InnerExceptions.Count != 0 ? group.InnerExceptions.SelectMany(Leaves) : new[] { error };
    private static async Task WithAsync(Func<Rig, Task> control)
    {
        var rig = new Rig(); List<Exception> errors = [];
        try { await control(rig); } catch (Exception error) { Add(errors, error); }
        finally
        {
            foreach (var release in rig.Releases) try { release(); } catch (Exception error) { Add(errors, error); }
            foreach (var child in rig.Host.Children) child.ReleaseForFixtureCleanup();
            Task? close = null; try { close = rig.Track(rig.Source.CloseAndDrainAsync()); } catch (Exception error) { Add(errors, error); }
            foreach (var original in rig.Tasks.Distinct().ToArray())
                try { await original; } catch (Exception error) { if (!rig.Expected.Contains(original)) AddTask(errors, original, error); }
            if (close is not null && !rig.Tasks.Contains(close))
                try { await close; } catch (Exception error) { AddTask(errors, close, error); }
            foreach (var owned in rig.Owned) try { owned.Dispose(); } catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Synthetic helper control and original teardown failed.", errors);
    }
    private static void Add(List<Exception> errors, Exception error) { if (!errors.Any(x => ReferenceEquals(x, error))) errors.Add(error); }
    private static void AddTask(List<Exception> errors, Task task, Exception observed) { Add(errors, observed); if (task.Exception is { } group) foreach (var original in group.InnerExceptions) Add(errors, original); }

    private sealed class Rig
    {
        internal readonly HelperClock Clock = new(); internal readonly Host Host = new();
        internal readonly CloudflaredNativeCakeAccessCredentialSource Source;
        internal readonly List<Task> Tasks = []; internal readonly HashSet<Task> Expected = []; internal readonly List<Action> Releases = [];
        internal readonly List<IDisposable> Owned = [];
        internal string Token => CloudflaredLifetimeControls.Token(new { exp = Clock.Now.AddMinutes(5).ToUnixTimeSeconds() });
        internal Rig() { Source = new(Path.Combine(Path.GetTempPath(), "synthetic-reviewed-cloudflared.exe"), Host, clock: Clock); }
        internal T Track<T>(T task) where T : Task { if (!Tasks.Contains(task)) Tasks.Add(task); return task; }
        internal T Own<T>(T resource) where T : IDisposable { Owned.Add(resource); return resource; }
        internal void Release(Action release) => Releases.Add(release);
        internal Child TokenChild(string? value = null) => new(new MemoryStream(Encoding.UTF8.GetBytes((value ?? Token) + "\n")), new MemoryStream());
        internal async Task<Exception> ExpectFaultsAsync(Task original, params Exception[] exact)
        {
            Exception failure;
            try { await original; throw new InvalidOperationException("Expected actual original fault."); }
            catch (Exception error) when (original.IsFaulted) { failure = error; }
            foreach (var cause in exact) Check.ContainsSame(cause, failure);
            Expected.Add(original); return failure;
        }
        internal async Task ExpectCancellationAsync(Task original, CancellationToken? exactToken = null)
        {
            Exception failure;
            try { await original; throw new InvalidOperationException("Expected actual cancellation."); }
            catch (Exception error) when (original.IsCanceled || original.IsFaulted) { failure = error; }
            Check.True(Leaves(failure).All(x => x is OperationCanceledException));
            if (exactToken is { } token) Check.True(Leaves(failure).OfType<OperationCanceledException>().Any(x => x.CancellationToken == token));
            Expected.Add(original);
        }
        internal async Task<Exception> ExpectCancellationAndFaultAsync(Task original, Exception exact)
        {
            var error = await ExpectFaultsAsync(original, exact);
            Check.True(Leaves(error).All(x => x is OperationCanceledException || ReferenceEquals(x, exact)));
            return error;
        }
    }
    private sealed class HelperClock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Lease : IDisposable
    {
        internal int Disposals; internal Exception? Error;
        public void Dispose() { Disposals++; if (Error is { } error) throw error; }
    }
    private sealed class Host : ICloudflaredOwnedHelperHost
    {
        internal readonly Lease Lease = new(); internal readonly List<Child> Children = []; internal readonly List<string[]> Arguments = [];
        internal int Pins, Creates; internal Action? OnCreate;
        public Task<IDisposable> AcquirePinnedExecutableAsync(string absolutePath, CancellationToken token) { token.ThrowIfCancellationRequested(); Pins++; return Task.FromResult<IDisposable>(Lease); }
        public ICloudflaredOwnedHelperProcess Create(string absolutePath, IReadOnlyList<string> arguments)
        {
            var index = Creates++; Arguments.Add(arguments.ToArray()); OnCreate?.Invoke();
            return index < Children.Count ? Children[index] : throw new InvalidOperationException("Unexpected synthetic child acquisition.");
        }
    }
    private sealed class Child(Stream output, Stream error, bool initiallyExited = true) : ICloudflaredOwnedHelperProcess
    {
        internal readonly TaskCompletionSource Exit = CreateExit(initiallyExited);
        internal readonly TaskCompletionSource Captured = Gate(), Killed = Gate();
        internal int Starts, Kills, Disposals, ExitAcquisitions; internal Action? OnStart; internal Exception? DisposeError;
        private static TaskCompletionSource CreateExit(bool exited) { var original = Gate(); if (exited) original.SetResult(); return original; }
        public bool Start() { Starts++; OnStart?.Invoke(); return true; }
        public Stream OpenStandardOutput() => output;
        public Stream OpenStandardError() => error;
        public Task WaitForExitAsync() { ExitAcquisitions++; Captured.TrySetResult(); return Exit.Task; }
        public int ExitCode => 0;
        public bool HasExited => Exit.Task.IsCompletedSuccessfully;
        public void KillOwnedHelper() { Kills++; Killed.TrySetResult(); Exit.TrySetResult(); }
        public void Dispose() { Disposals++; if (DisposeError is { } failure) throw failure; }
        internal void ReleaseForFixtureCleanup() { Exit.TrySetResult(); if (output is HeldEofStream held) held.End(); }
    }
    private sealed class HeldEofStream : MemoryStream
    {
        private readonly TaskCompletionSource<int> end = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Entered = Gate(); internal int Disposals;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { Entered.TrySetResult(); return new(end.Task); }
        internal void End() => end.TrySetResult(0);
        protected override void Dispose(bool disposing) { if (disposing) Disposals++; base.Dispose(disposing); }
    }
    private sealed class OriginalReadStream(Task<int> original) : MemoryStream
    {
        internal Task<int> Original => original; internal int Reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { Reads++; return new(original); }
    }
    private sealed class DisposalStream(Exception failure) : MemoryStream
    {
        internal int Disposals;
        protected override void Dispose(bool disposing) { if (disposing) { Disposals++; base.Dispose(disposing); throw failure; } base.Dispose(disposing); }
    }
    private sealed class OverflowStream(Exception late) : MemoryStream
    {
        internal int Delivered, Reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            Reads++; if (Delivered >= 20480) return new(Task.FromException<int>(late));
            var marker = Encoding.UTF8.GetBytes("SYNTHETIC_PRIVATE_CAPTURE");
            for (var i = 0; i < buffer.Length; i++) buffer.Span[i] = marker[i % marker.Length];
            Delivered += buffer.Length; return ValueTask.FromResult(buffer.Length);
        }
    }
}
