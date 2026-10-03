using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Terminal;

/// <summary>
/// Unix adapter backed by a native PTY spawn shim. Windows support intentionally belongs in a separate ConPTY
/// adapter; this class never substitutes redirected pipes for a PTY.
/// </summary>
public sealed class UnixPtyProcessFactory : IPtyProcessFactory
{
    public bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    public string AdapterName => "Unix native PTY";
    public string? UnavailableReason => IsSupported
        ? null
        : "Interactive PTY sessions require the Unix native PTY adapter. Windows requires a separate ConPTY adapter.";

    public Task<IPtyProcess> StartAsync(PtyProcessStartOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupported) throw new PlatformNotSupportedException(UnavailableReason);
        Validate(options);
        return Task.FromResult<IPtyProcess>(UnixPtyProcess.Start(options));
    }

    private static void Validate(PtyProcessStartOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);
        if (!Path.IsPathRooted(options.FileName) || !File.Exists(options.FileName))
            throw new FileNotFoundException("The PTY executable must be an existing absolute path.", options.FileName);
        if (!Path.IsPathRooted(options.WorkingDirectory))
            throw new ArgumentException("The PTY working directory must be absolute.", nameof(options));
        if (!Directory.Exists(options.WorkingDirectory)) throw new DirectoryNotFoundException(options.WorkingDirectory);
        if (options.Arguments.Any(static value => value is null))
            throw new ArgumentException("PTY arguments cannot contain null values.", nameof(options));
    }
}

internal sealed class UnixPtyProcess : IPtyProcess
{
    private const int SigInt = 2;
    private const int SigTerm = 15;
    private const int SigKill = 9;
    private readonly object _sync = new();
    private readonly FileStream _master;
    private readonly FileStream _writer;
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly TaskCompletionSource<PtyProcessExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _readTask;
    private readonly Task _waitTask;
    private PtySize _size;
    private PtyProcessState _state = PtyProcessState.Running;
    private int _disposed;
    private int _childReaped;

    private UnixPtyProcess(int processId, int masterFd, PtySize size)
    {
        ProcessId = processId;
        _size = size;
        using var incoming = new SafeFileHandle((IntPtr)masterFd, ownsHandle: true);
        SafeFileHandle? readHandle = null;
        SafeFileHandle? writeHandle = null;
        FileStream? reader = null;
        FileStream? writer = null;
        try
        {
            // Atomic close-on-exec duplication; ownership remains local until both streams exist.
            var readFd = UnixNative.Dup(incoming);
            if (readFd < 0) throw new IOException("The PTY read handle could not be duplicated.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            readHandle = new SafeFileHandle((IntPtr)readFd, ownsHandle: true);
            reader = new FileStream(readHandle, FileAccess.ReadWrite, 4096, isAsync: false);
            var writeFd = UnixNative.Dup(incoming);
            if (writeFd < 0) throw new IOException("The PTY write handle could not be duplicated.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            writeHandle = new SafeFileHandle((IntPtr)writeFd, ownsHandle: true);
            // Independent synchronization permits writes while a blocking PTY read is pending.
            writer = new FileStream(writeHandle, FileAccess.Write, 4096, isAsync: false);
            _master = reader;
            _writer = writer;
        }
        catch
        {
            writer?.Dispose();
            writeHandle?.Dispose();
            reader?.Dispose();
            readHandle?.Dispose();
            throw;
        }
        _readTask = ReadOutputAsync();
        _waitTask = Task.Run(WaitForChild);
    }

    public int ProcessId { get; }
    public PtySize Size { get { lock (_sync) return _size; } }
    public PtyProcessState State { get { lock (_sync) return _state; } }
    public event EventHandler<PtyOutputChunk>? OutputReceived;
    public event EventHandler<PtyProcessStateChange>? StateChanged;

    internal static UnixPtyProcess Start(PtyProcessStartOptions options)
    {
        var allocations = new List<IntPtr>();
        try
        {
            var executable = AllocateUtf8(options.FileName, allocations);
            var workingDirectory = AllocateUtf8(options.WorkingDirectory, allocations);
            var argumentPointers = new IntPtr[options.Arguments.Count + 2];
            argumentPointers[0] = executable;
            for (var index = 0; index < options.Arguments.Count; index++)
                argumentPointers[index + 1] = AllocateUtf8(options.Arguments[index], allocations);
            var argv = Marshal.AllocHGlobal(IntPtr.Size * argumentPointers.Length);
            allocations.Add(argv);
            Marshal.Copy(argumentPointers, 0, argv, argumentPointers.Length);

            var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .Select(item => $"{item.Key}={item.Value}").ToArray();
            var environmentPointers = new IntPtr[environment.Length + 1];
            for (var index = 0; index < environment.Length; index++)
                environmentPointers[index] = AllocateUtf8(environment[index], allocations);
            var envp = Marshal.AllocHGlobal(IntPtr.Size * environmentPointers.Length);
            allocations.Add(envp);
            Marshal.Copy(environmentPointers, 0, envp, environmentPointers.Length);
            // All child-side PTY setup and exec stay in the native shim, never the CLR.
            var pid = UnixNative.Spawn(executable, argv, workingDirectory, envp,
                options.InitialSize.Rows, options.InitialSize.Columns, out var master);
            if (pid < 0) throw new IOException("Native PTY spawn failed.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            try { return new UnixPtyProcess(pid, master, options.InitialSize); }
            catch
            {
                // Constructor owns/closes master even when stream setup fails. We still own
                // the unreaped child here, including before it has established a process group.
                UnixNative.Abort(pid);
                throw;
            }
        }
        finally
        {
            foreach (var pointer in allocations) Marshal.FreeHGlobal(pointer);
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.IsEmpty) return;
        ThrowIfNotRunning();
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfNotRunning();
            await _writer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public ValueTask ResizeAsync(PtySize size, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfNotRunning();
        var window = new NativeWindowSize(size.Rows, size.Columns, 0, 0);
        var request = OperatingSystem.IsMacOS() ? 0x80087467UL : 0x5414UL;
        if (UnixNative.Ioctl(_master.SafeFileHandle, request, ref window) != 0)
            throw new IOException("The PTY could not be resized.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        lock (_sync) _size = size;
        return ValueTask.CompletedTask;
    }

    public ValueTask SignalAsync(PtySignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfNotRunning();
        var nativeSignal = signal switch
        {
            PtySignal.Interrupt => SigInt,
            PtySignal.Terminate => SigTerm,
            PtySignal.Kill => SigKill,
            _ => throw new ArgumentOutOfRangeException(nameof(signal))
        };
        SignalProcessGroup(nativeSignal);
        return ValueTask.CompletedTask;
    }

    public Task<PtyProcessExit> WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _exit.Task.WaitAsync(cancellationToken);

    private async Task ReadOutputAsync()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!_readCancellation.IsCancellationRequested)
            {
                var count = await _master.ReadAsync(buffer, _readCancellation.Token).ConfigureAwait(false);
                if (count == 0) break;
                PublishOutput(buffer.AsSpan(0, count).ToArray());
            }
        }
        catch (OperationCanceledException) when (_readCancellation.IsCancellationRequested) { }
        catch (IOException) when (_exit.Task.IsCompleted || Volatile.Read(ref _disposed) != 0) { }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
        catch (Exception ex)
        {
            SetState(PtyProcessState.Faulted, ex.Message);
        }
    }

    private void WaitForChild()
    {
        PtyProcessExit? observedExit = null;
        IOException? failure = null;
        while (observedExit is null && failure is null)
        {
            lock (_sync)
            {
                // Reaping and signaling share one lock. While unreaped, this child's PID
                // cannot be reused; once reaped, no later signal may target its numeric ID.
                var result = UnixNative.WaitPid(ProcessId, out var status, 1); // WNOHANG
                if (result == ProcessId)
                {
                    Volatile.Write(ref _childReaped, 1);
                    var signal = status & 0x7f;
                    observedExit = signal == 0
                        ? new((status >> 8) & 0xff, null, DateTimeOffset.UtcNow)
                        : new(null, signal, DateTimeOffset.UtcNow);
                }
                else if (result < 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != 4) // EINTR is not process exit.
                    {
                        // ECHILD means this PID is no longer ours to signal.
                        if (error != 10) UnixNative.Abort(ProcessId);
                        Volatile.Write(ref _childReaped, 1);
                        failure = new IOException("The PTY exit status could not be observed.",
                            new System.ComponentModel.Win32Exception(error));
                    }
                }
            }
            if (observedExit is null && failure is null) Thread.Sleep(10);
        }
        // Publish callbacks outside the lifetime lock.
        if (failure is not null)
        {
            SetState(PtyProcessState.Faulted, failure.Message);
            _exit.TrySetException(failure);
        }
        else
        {
            SetState(PtyProcessState.Exited);
            _exit.TrySetResult(observedExit!);
        }
    }

    private void SignalProcessGroup(int signal)
    {
        lock (_sync)
        {
            if (_childReaped != 0) return;
            if (UnixNative.Kill(-ProcessId, signal) == 0) return;
            var error = Marshal.GetLastPInvokeError();
            // Immediately after fork the child may not have executed setsid yet.
            // The lock prevents our waiter reaping it during this positive-PID fallback.
            if (error == 3 && UnixNative.Kill(ProcessId, signal) == 0) return;
            error = Marshal.GetLastPInvokeError();
            if (error != 3)
                throw new IOException("The PTY process could not be signalled.", new System.ComponentModel.Win32Exception(error));
        }
    }

    private void ThrowIfNotRunning()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (State != PtyProcessState.Running) throw new InvalidOperationException("The PTY process is not running.");
    }

    private void PublishOutput(byte[] bytes)
    {
        var handlers = OutputReceived;
        if (handlers is null) return;
        foreach (EventHandler<PtyOutputChunk> handler in handlers.GetInvocationList())
        {
            try { handler(this, new PtyOutputChunk(bytes)); }
            catch { /* Presentation callbacks cannot terminate process I/O ownership. */ }
        }
    }

    private void SetState(PtyProcessState state, string? detail = null)
    {
        lock (_sync)
        {
            if (_state == PtyProcessState.Disposed) return;
            _state = state;
        }
        var handlers = StateChanged;
        if (handlers is null) return;
        foreach (EventHandler<PtyProcessStateChange> handler in handlers.GetInvocationList())
        {
            try { handler(this, new(state, detail)); }
            catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (Volatile.Read(ref _childReaped) == 0)
        {
            try { SignalProcessGroup(SigTerm); } catch (IOException) { }
            if (await Task.WhenAny(_exit.Task, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false) != _exit.Task)
            {
                try { SignalProcessGroup(SigKill); } catch (IOException) { }
                await Task.WhenAny(_exit.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            }
        }
        _readCancellation.Cancel();
        _master.Dispose();
        _writer.Dispose();
        try { await _readTask.ConfigureAwait(false); } catch { }
        try { await _waitTask.ConfigureAwait(false); } catch { }
        lock (_sync) _state = PtyProcessState.Disposed;
        _readCancellation.Dispose();
        _writeGate.Dispose();
        StateChanged?.Invoke(this, new(PtyProcessState.Disposed));
    }

    private static IntPtr AllocateUtf8(string value, ICollection<IntPtr> allocations)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        allocations.Add(pointer);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowSize(ushort rows, ushort columns, ushort width, ushort height)
    {
        public ushort Rows = rows;
        public ushort Columns = columns;
        public ushort PixelWidth = width;
        public ushort PixelHeight = height;
    }

    private static class UnixNative
    {
        [DllImport("haven_terminal_pty", EntryPoint = "haven_terminal_spawn", SetLastError = true)]
        public static extern int Spawn(IntPtr file, IntPtr argv, IntPtr directory, IntPtr environment,
            ushort rows, ushort columns, out int master);

        [DllImport("haven_terminal_pty", EntryPoint = "haven_terminal_dup", SetLastError = true)]
        public static extern int Dup(SafeFileHandle file);

        [DllImport("haven_terminal_pty", EntryPoint = "haven_terminal_abort", SetLastError = true)]
        public static extern int Abort(int processId);

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        public static extern int Ioctl(SafeFileHandle file, ulong request, ref NativeWindowSize window);

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        public static extern int Kill(int processId, int signal);

        [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        public static extern int WaitPid(int processId, out int status, int options);

    }
}
