using System.Text;
using HavenOS.Apps.Terminal;

internal static class PtyProcessSpecs
{
    public static void RunOutputBufferSpec() => OutputBufferRetainsOutputUntilFirstSubscription();

    public static async Task RunDelayedOutputSpecAsync()
    {
        var factory = new WindowsConPtyProcessFactory();
        Check(factory.IsSupported, "delayed PTY output spec requires Windows ConPTY support");
        await DelayedCommandOutputIsCapturedAsync(factory);
    }

    public static async Task RunWorkingDirectorySpecAsync()
    {
        var factory = new WindowsConPtyProcessFactory();
        Check(factory.IsSupported, "working-directory PTY spec requires Windows ConPTY support");
        await OutputWorkingDirectoryResizeExitAndDisposalAsync(factory);
    }

    public static async Task RunAsync()
    {
        await PtySizeRejectsEmptyDimensionsAsync();
        OutputBufferRetainsOutputUntilFirstSubscription();

        await UnsupportedFactoryReportsItsReasonAsync(new UnixPtyProcessFactory());
        var factory = new WindowsConPtyProcessFactory();
        if (!factory.IsSupported)
        {
            Check(!string.IsNullOrWhiteSpace(factory.UnavailableReason), "unsupported ConPTY must explain why it is unavailable");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                await factory.StartAsync(
                    new PtyProcessStartOptions("missing.exe", [], Environment.CurrentDirectory, new PtySize(80, 25)),
                    canceled.Token);
                throw new InvalidOperationException("A canceled ConPTY start should not succeed.");
            }
            catch (OperationCanceledException) { }
            return;
        }

        Check(factory.UnavailableReason is null, "supported ConPTY must not report an unavailable reason");
        await StartCancellationIsObservedBeforeNativeWorkAsync(factory);
        await InvalidInitialDimensionsAreRejectedAsync(factory);
        await DelayedCommandOutputIsCapturedAsync(factory);
        await OutputWorkingDirectoryResizeExitAndDisposalAsync(factory);
        await InterruptStopsAConsoleChildAsync(factory);
        await TerminateAndKillEndTheProcessTreeAsync(factory);
        await DisposalTerminatesARunningChildAsync(factory);
        await DefaultSessionSelectsTheWindowsShellAsync();
    }

    private static void OutputBufferRetainsOutputUntilFirstSubscription()
    {
        var buffer = new PtyOutputBuffer();
        var sender = new object();
        var received = new List<string>();
        buffer.Publish(sender, new PtyOutputChunk(Encoding.ASCII.GetBytes("first startup chunk")));
        buffer.Publish(sender, new PtyOutputChunk(Encoding.ASCII.GetBytes("second startup chunk")));
        Check(received.Count == 0, "output should wait while the process has no subscriber");

        EventHandler<PtyOutputChunk> handler = (_, chunk) => received.Add(Encoding.ASCII.GetString(chunk.Bytes.Span));
        buffer.OutputReceived += handler;
        Check(received.SequenceEqual(["first startup chunk", "second startup chunk"]),
            "the first subscriber should receive buffered startup chunks once and in order");

        buffer.Publish(sender, new PtyOutputChunk(Encoding.ASCII.GetBytes("live chunk")));
        Check(received.SequenceEqual(["first startup chunk", "second startup chunk", "live chunk"]),
            "output after subscription should continue through the same event");

        buffer.OutputReceived -= handler;
        buffer.Publish(sender, new PtyOutputChunk(Encoding.ASCII.GetBytes("unobserved chunk")));
        Check(received.Count == 3, "output should not accumulate for later subscribers after the first subscription");
    }

    private static Task PtySizeRejectsEmptyDimensionsAsync()
    {
        Expect<ArgumentOutOfRangeException>(() => _ = new PtySize(0, 25));
        Expect<ArgumentOutOfRangeException>(() => _ = new PtySize(80, 0));
        return Task.CompletedTask;
    }

    private static async Task StartCancellationIsObservedBeforeNativeWorkAsync(WindowsConPtyProcessFactory factory)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await factory.StartAsync(
                new PtyProcessStartOptions(GetCommandProcessor(), [], Environment.CurrentDirectory, new PtySize(80, 25)),
                cancellation.Token);
            throw new InvalidOperationException("A canceled ConPTY start should not create a process.");
        }
        catch (OperationCanceledException) { }
    }

    private static async Task InvalidInitialDimensionsAreRejectedAsync(WindowsConPtyProcessFactory factory)
    {
        try
        {
            var process = await factory.StartAsync(new PtyProcessStartOptions(
                GetCommandProcessor(), [], Environment.CurrentDirectory, default));
            await process.DisposeAsync();
            throw new InvalidOperationException("Default ConPTY dimensions should be rejected before native process creation.");
        }
        catch (ArgumentOutOfRangeException) { }
    }

    private static async Task UnsupportedFactoryReportsItsReasonAsync(IPtyProcessFactory factory)
    {
        if (factory.IsSupported) return;

        Check(!string.IsNullOrWhiteSpace(factory.UnavailableReason),
            $"unsupported {factory.AdapterName} factory must explain why it is unavailable");
        await ExpectAsync<PlatformNotSupportedException>(() => factory.StartAsync(
            new PtyProcessStartOptions("missing.exe", [], Environment.CurrentDirectory, new PtySize(80, 25))));
    }

    private static async Task OutputWorkingDirectoryResizeExitAndDisposalAsync(WindowsConPtyProcessFactory factory)
    {
        var temporaryRoot = CreateTemporaryDirectory();
        IPtyProcess? process = null;
        try
        {
            var workingDirectory = Path.Combine(temporaryRoot, "working directory");
            Directory.CreateDirectory(workingDirectory);
            var output = new PtyByteCapture();
            var startedProcess = await factory.StartAsync(new PtyProcessStartOptions(
                GetCommandProcessor(),
                ["/D", "/Q"],
                workingDirectory,
                new PtySize(80, 25)));
            process = startedProcess;
            startedProcess.OutputReceived += output.OnOutput;

            await ExpectAsync<ArgumentOutOfRangeException>(() => startedProcess.ResizeAsync(default).AsTask());
            Check(startedProcess.State == PtyProcessState.Running, "new ConPTY child should be running");
            Check(startedProcess.Size == new PtySize(80, 25), "initial ConPTY dimensions should be retained");

            using (var canceledOperations = new CancellationTokenSource())
            {
                canceledOperations.Cancel();
                await ExpectAsync<OperationCanceledException>(() =>
                    startedProcess.WriteAsync(Encoding.ASCII.GetBytes("must-not-be-written"), canceledOperations.Token).AsTask());
                await ExpectAsync<OperationCanceledException>(() =>
                    startedProcess.ResizeAsync(new PtySize(90, 30), canceledOperations.Token).AsTask());
                await ExpectAsync<OperationCanceledException>(() =>
                    startedProcess.SignalAsync(PtySignal.Terminate, canceledOperations.Token).AsTask());
                Check(startedProcess.State == PtyProcessState.Running,
                    "canceling input, resize, or signal should leave the ConPTY child running");
                Check(startedProcess.Size == new PtySize(80, 25),
                    "a canceled resize should not update the ConPTY dimensions");
            }

            await startedProcess.WriteAsync(Encoding.ASCII.GetBytes("cd\r\n"));
            await output.WaitForTextAsync(workingDirectory, TimeSpan.FromSeconds(8));

            var resized = new PtySize(103, 31);
            await startedProcess.ResizeAsync(resized);
            Check(startedProcess.Size == resized, "successful resize should update the reported dimensions");
            await startedProcess.WriteAsync(Encoding.ASCII.GetBytes("mode con\r\n"));
            await output.WaitForPatternAsync(@"Columns:\s+103", TimeSpan.FromSeconds(8));
            await output.WaitForPatternAsync(@"Lines:\s+31", TimeSpan.FromSeconds(8));

            const string marker = "PTY_RAW_BYTES_9TO1";
            await startedProcess.WriteAsync(Encoding.ASCII.GetBytes($"echo {marker}\r\n"));
            var captured = await output.WaitForTextAsync(marker, TimeSpan.FromSeconds(8));
            Check(ContainsBytes(Encoding.ASCII.GetBytes(captured), Encoding.ASCII.GetBytes(marker)),
                "ConPTY output must be delivered as the original byte sequence");

            await startedProcess.WriteAsync(Encoding.ASCII.GetBytes("exit /b 17\r\n"));
            using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var exit = await startedProcess.WaitForExitAsync(waitLimit.Token);
            Check(exit.ExitCode == 17, $"ConPTY should preserve the child exit code; actual {exit.ExitCode}");
            Check(exit.Signal is null, "Windows exit should not be reported as a Unix signal");
            Check(startedProcess.State == PtyProcessState.Exited, "normal child completion should set Exited");

            await startedProcess.DisposeAsync();
            Check(startedProcess.State == PtyProcessState.Disposed, "disposing an exited process should set Disposed");
        }
        finally
        {
            if (process is not null)
                await process.DisposeAsync();
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    private static async Task DelayedCommandOutputIsCapturedAsync(WindowsConPtyProcessFactory factory)
    {
        var output = new PtyByteCapture();
        var process = await factory.StartAsync(new PtyProcessStartOptions(
            GetCommandProcessor(),
            ["/D", "/Q", "/C", "ping -n 3 127.0.0.1 >nul & echo PTY_DELAYED_OUTPUT"],
            Environment.CurrentDirectory,
            new PtySize(80, 25)));
        try
        {
            process.OutputReceived += output.OnOutput;
            await output.WaitForTextAsync("PTY_DELAYED_OUTPUT", TimeSpan.FromSeconds(8));
            using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var exit = await process.WaitForExitAsync(waitLimit.Token);
            Check(exit.ExitCode == 0, $"ConPTY command child should exit normally; actual {exit.ExitCode}");
        }
        finally
        {
            await process.DisposeAsync();
        }
    }

    private static async Task InterruptStopsAConsoleChildAsync(WindowsConPtyProcessFactory factory)
    {
        var process = await factory.StartAsync(new PtyProcessStartOptions(
            GetCommandProcessor(),
            ["/D", "/Q", "/C", "ping -t 127.0.0.1 >nul"],
            Environment.CurrentDirectory,
            new PtySize(80, 25)));
        try
        {
            await Task.Delay(500);
            await process.SignalAsync(PtySignal.Interrupt);
            using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var exit = await process.WaitForExitAsync(waitLimit.Token);
            Check(process.State == PtyProcessState.Exited, "Ctrl+C input should allow the console child to exit");
            Check(exit.ExitCode.HasValue, "interrupted Windows child should provide an exit code");
        }
        finally
        {
            await process.DisposeAsync();
        }
    }

    private static async Task TerminateAndKillEndTheProcessTreeAsync(WindowsConPtyProcessFactory factory)
    {
        foreach (var (signal, expectedCode) in new[] { (PtySignal.Terminate, 1), (PtySignal.Kill, 137) })
        {
            var process = await factory.StartAsync(new PtyProcessStartOptions(
                GetCommandProcessor(),
                ["/D", "/Q"],
                Environment.CurrentDirectory,
                new PtySize(80, 25)));
            try
            {
                using var canceledWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(75));
                try
                {
                    await process.WaitForExitAsync(canceledWait.Token);
                    throw new InvalidOperationException("Canceling a wait must not terminate the child.");
                }
                catch (OperationCanceledException) { }

                Check(process.State == PtyProcessState.Running, "canceling WaitForExitAsync should leave the shell running");
                await process.SignalAsync(signal);
                using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var exit = await process.WaitForExitAsync(waitLimit.Token);
                Check(exit.ExitCode == expectedCode, $"{signal} should report exit code {expectedCode}; actual {exit.ExitCode}");
            }
            finally
            {
                await process.DisposeAsync();
            }
        }
    }

    private static async Task DisposalTerminatesARunningChildAsync(WindowsConPtyProcessFactory factory)
    {
        var process = await factory.StartAsync(new PtyProcessStartOptions(
            GetCommandProcessor(), ["/D", "/Q"], Environment.CurrentDirectory, new PtySize(80, 25)));
        try
        {
            using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await process.DisposeAsync();
            Check(process.State == PtyProcessState.Disposed,
                "disposing a running ConPTY child should complete its lifecycle");
            var exit = await process.WaitForExitAsync(waitLimit.Token);
            Check(exit.ExitCode == 1, $"disposing a running child should terminate it; actual exit code {exit.ExitCode}");
        }
        finally
        {
            await process.DisposeAsync();
        }
    }

    private static async Task DefaultSessionSelectsTheWindowsShellAsync()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var commandProcessor = GetCommandProcessor();
            var fallbackShell = InteractiveTerminalSession.ResolveShell(
                shell: null,
                shellPreference: null,
                commandProcessor: commandProcessor,
                systemDirectory: Environment.SystemDirectory,
                isWindows: true);
            Check(fallbackShell == commandProcessor,
                "the default Windows shell resolver should use ComSpec when no shell preference is supplied");

            await using var session = new InteractiveTerminalSession();
            Check(session.IsSupported, "default terminal session should select the supported Windows adapter");
            var process = await session.StartAsync(temporaryRoot, new PtySize(80, 25), fallbackShell);
            await process.WriteAsync(Encoding.ASCII.GetBytes("exit\r\n"));
            using var waitLimit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await process.WaitForExitAsync(waitLimit.Token);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    private static string GetCommandProcessor()
    {
        var commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        if (!string.IsNullOrWhiteSpace(commandProcessor) && File.Exists(commandProcessor))
            return Path.GetFullPath(commandProcessor);

        var systemCommandProcessor = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (File.Exists(systemCommandProcessor))
            return systemCommandProcessor;

        throw new InvalidOperationException("Windows integration specs require cmd.exe.");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "9to1-terminal-pty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        var target = Path.GetFullPath(path);
        if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }

    private static bool ContainsBytes(byte[] source, byte[] search)
    {
        for (var start = 0; start <= source.Length - search.Length; start++)
        {
            if (source.AsSpan(start, search.Length).SequenceEqual(search))
                return true;
        }

        return false;
    }

    private static void Expect<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }
        catch (TException) { }
    }

    private static async Task ExpectAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }
        catch (TException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("PTY spec failed: " + message);
    }

    private sealed class PtyByteCapture
    {
        private readonly object _sync = new();
        private readonly MemoryStream _bytes = new();

        public void OnOutput(object? sender, PtyOutputChunk chunk)
        {
            lock (_sync)
                _bytes.Write(chunk.Bytes.Span);
        }

        public async Task<string> WaitForPatternAsync(string pattern, TimeSpan timeout)
        {
            var expression = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            using var cancellation = new CancellationTokenSource(timeout);
            while (true)
            {
                string snapshot;
                lock (_sync)
                    snapshot = Encoding.ASCII.GetString(_bytes.ToArray());

                if (expression.IsMatch(snapshot))
                    return snapshot;

                try
                {
                    await Task.Delay(20, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException($"Timed out waiting for PTY output pattern '{pattern}'. Captured bytes: {snapshot}");
                }
            }
        }

        public async Task<string> WaitForTextAsync(string marker, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            var markerBytes = Encoding.ASCII.GetBytes(marker);
            while (true)
            {
                byte[] snapshot;
                lock (_sync)
                    snapshot = _bytes.ToArray();

                if (ContainsBytes(snapshot, markerBytes))
                    return Encoding.ASCII.GetString(snapshot);

                try
                {
                    await Task.Delay(20, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException($"Timed out waiting for PTY output '{marker}'. Captured bytes: {Encoding.ASCII.GetString(snapshot)}");
                }
            }
        }
    }

}
