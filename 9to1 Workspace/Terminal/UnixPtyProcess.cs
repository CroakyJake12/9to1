using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Apps.Terminal;

/// <summary>
/// Unix adapter backed by forkpty(3). Windows support intentionally belongs in a separate ConPTY
/// adapter; this class never substitutes redirected pipes for a PTY.
/// </summary>
public sealed class UnixPtyProcessFactory : IPtyProcessFactory
{
    public bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    public string AdapterName => "Unix forkpty";
    public string? UnavailableReason => IsSupported
        ? null
        : "Interactive PTY sessions require the Unix forkpty adapter. Windows requires a separate ConPTY adapter.";

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
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly TaskCompletionSource<PtyProcessExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _readTask;
    private readonly Task _waitTask;
    private PtySize _size;
    private PtyProcessState _state = PtyProcessState.Running;
    private int _disposed;

    private UnixPtyProcess(int processId, int masterFd, PtySize size)
    {
        ProcessId = processId;
        _size = size;
        _master = new FileStream(new SafeFileHandle((IntPtr)masterFd, ownsHandle: true), FileAccess.ReadWrite, 4096, isAsync: true);
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

            var window = new NativeWindowSize(options.InitialSize.Rows, options.InitialSize.Columns, 0, 0);
            var pid = UnixNative.ForkPty(out var master, ref window);
            if (pid == 0)
            {
                if (UnixNative.Chdir(workingDirectory) != 0) UnixNative.Exit(126);
                UnixNative.ExecVp(executable, argv);
                UnixNative.Exit(127);
            }
            if (pid < 0) throw new IOException("forkpty failed.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            return new UnixPtyProcess(pid, master, options.InitialSize);
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
            await _master.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _master.FlushAsync(cancellationToken).ConfigureAwait(false);
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
        var result = UnixNative.WaitPid(ProcessId, out var status, 0);
        PtyProcessExit exit;
        if (result < 0)
        {
            exit = new(null, null, DateTimeOffset.UtcNow);
            SetState(PtyProcessState.Faulted, "waitpid failed.");
        }
        else
        {
            var signal = status & 0x7f;
            exit = signal == 0
                ? new((status >> 8) & 0xff, null, DateTimeOffset.UtcNow)
                : new(null, signal, DateTimeOffset.UtcNow);
            SetState(PtyProcessState.Exited);
        }
        _exit.TrySetResult(exit);
    }

    private void SignalProcessGroup(int signal)
    {
        if (UnixNative.Kill(-ProcessId, signal) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != 3) // ESRCH: the child exited between the state check and signal.
                throw new IOException("The PTY process group could not be signalled.", new System.ComponentModel.Win32Exception(error));
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
        if (!_exit.Task.IsCompleted)
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
        try { await _readTask.ConfigureAwait(false); } catch { }
        try { await _waitTask.ConfigureAwait(false); } catch { }
        lock (_sync) _state = PtyProcessState.Disposed;
        _readCancellation.Dispose();
        _writeGate.Dispose();
        StateChanged?.Invoke(this, new(PtyProcessState.Disposed));
    }

    private static IntPtr AllocateUtf8(string value, ICollection<IntPtr> allocations)
    {
        var pointer = Marshal.StringToCoTaskMemUTF8(value);
        allocations.Add(pointer);
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
        private delegate int ForkPtyDelegate(out int master, IntPtr name, IntPtr termios, ref NativeWindowSize window);
        private static readonly ForkPtyDelegate ForkPtyImplementation = LoadForkPty();

        public static int ForkPty(out int master, ref NativeWindowSize window) =>
            ForkPtyImplementation(out master, IntPtr.Zero, IntPtr.Zero, ref window);

        [DllImport("libc", EntryPoint = "chdir", SetLastError = true)]
        public static extern int Chdir(IntPtr path);

        [DllImport("libc", EntryPoint = "execvp", SetLastError = true)]
        public static extern int ExecVp(IntPtr file, IntPtr argv);

        [DllImport("libc", EntryPoint = "_exit")]
        public static extern void Exit(int status);

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        public static extern int Ioctl(SafeFileHandle file, ulong request, ref NativeWindowSize window);

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        public static extern int Kill(int processId, int signal);

        [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        public static extern int WaitPid(int processId, out int status, int options);

        private static ForkPtyDelegate LoadForkPty()
        {
            string[] libraries = OperatingSystem.IsMacOS()
                ? ["libutil.dylib", "/usr/lib/libutil.dylib", "libc"]
                : ["libutil.so.1", "libutil.so", "libc.so.6"];
            foreach (var library in libraries)
            {
                if (!NativeLibrary.TryLoad(library, out var handle)) continue;
                if (NativeLibrary.TryGetExport(handle, "forkpty", out var symbol))
                    return Marshal.GetDelegateForFunctionPointer<ForkPtyDelegate>(symbol);
                NativeLibrary.Free(handle);
            }
            throw new PlatformNotSupportedException("forkpty(3) was not found in the Unix runtime libraries.");
        }
    }
}
