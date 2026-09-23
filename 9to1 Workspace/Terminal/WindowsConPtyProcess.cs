using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Apps.Terminal;

/// <summary>Windows ConPTY adapter for real interactive console processes.</summary>
public sealed class WindowsConPtyProcessFactory : IPtyProcessFactory
{
    private const int MinimumWindowsBuild = 17763;

    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild);
    public string AdapterName => "Windows ConPTY";
    public string? UnavailableReason => IsSupported
        ? null
        : "Interactive Windows terminal sessions require Windows 10 version 1809 (build 17763) or later.";

    public Task<IPtyProcess> StartAsync(PtyProcessStartOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupported)
            throw new PlatformNotSupportedException(UnavailableReason);

        WindowsConPtyProcess.Validate(options);
        return Task.FromResult<IPtyProcess>(WindowsConPtyProcess.Start(options, cancellationToken));
    }
}

/// <summary>Selects the native PTY adapter for the current operating system.</summary>
public sealed class PlatformPtyProcessFactory : IPtyProcessFactory
{
    private readonly IPtyProcessFactory _inner = OperatingSystem.IsWindows()
        ? new WindowsConPtyProcessFactory()
        : new UnixPtyProcessFactory();

    public bool IsSupported => _inner.IsSupported;
    public string AdapterName => _inner.AdapterName;
    public string? UnavailableReason => _inner.UnavailableReason;

    public Task<IPtyProcess> StartAsync(PtyProcessStartOptions options, CancellationToken cancellationToken = default) =>
        _inner.StartAsync(options, cancellationToken);
}

internal sealed class WindowsConPtyProcess : IPtyProcess
{
    private const uint WaitObject0 = 0;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint CreateSuspended = 0x00000004;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint PseudoConsoleAttribute = 0x00020016;

    private readonly object _sync = new();
    private readonly SafeKernelObjectHandle _processHandle;
    private readonly SafeKernelObjectHandle _jobHandle;
    private readonly SafePseudoConsoleHandle _pseudoConsole;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly PtyOutputBuffer _outputBuffer = new();
    private readonly TaskCompletionSource<PtyProcessExit> _exit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _readTask;
    private readonly Task _waitTask;
    private PtySize _size;
    private PtyProcessState _state = PtyProcessState.Running;
    private int _disposed;
    private int _processExited;

    private WindowsConPtyProcess(
        int processId,
        PtySize size,
        SafeKernelObjectHandle processHandle,
        SafeKernelObjectHandle jobHandle,
        SafePseudoConsoleHandle pseudoConsole,
        SafeFileHandle inputHandle,
        SafeFileHandle outputHandle)
    {
        ProcessId = processId;
        _size = size;
        _processHandle = processHandle;
        _jobHandle = jobHandle;
        _pseudoConsole = pseudoConsole;
        _input = new FileStream(inputHandle, FileAccess.Write, bufferSize: 4096, isAsync: false);
        _output = new FileStream(outputHandle, FileAccess.Read, bufferSize: 4096, isAsync: false);
        _readTask = ReadOutputAsync();
        _waitTask = Task.Run(WaitForChildAsync);
    }

    public int ProcessId { get; }
    public PtySize Size { get { lock (_sync) return _size; } }
    public PtyProcessState State { get { lock (_sync) return _state; } }
    public event EventHandler<PtyOutputChunk>? OutputReceived
    {
        add => _outputBuffer.OutputReceived += value;
        remove => _outputBuffer.OutputReceived -= value;
    }
    public event EventHandler<PtyProcessStateChange>? StateChanged;

    internal static void Validate(PtyProcessStartOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName);
        ArgumentNullException.ThrowIfNull(options.Arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows ConPTY is available only on Windows.");
        if (!Path.IsPathRooted(options.FileName) || !File.Exists(options.FileName))
            throw new FileNotFoundException("The PTY executable must be an existing absolute path.", options.FileName);
        if (!Path.IsPathRooted(options.WorkingDirectory))
            throw new ArgumentException("The PTY working directory must be absolute.", nameof(options));
        if (!Directory.Exists(options.WorkingDirectory))
            throw new DirectoryNotFoundException(options.WorkingDirectory);
        if (options.Arguments.Any(static value => value is null))
            throw new ArgumentException("PTY arguments cannot contain null values.", nameof(options));
        if (options.InitialSize.Columns == 0 || options.InitialSize.Rows == 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Windows ConPTY dimensions must be greater than zero.");
        if (options.InitialSize.Columns > short.MaxValue || options.InitialSize.Rows > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options), "Windows ConPTY dimensions must fit in a signed 16-bit coordinate.");
    }

    internal static WindowsConPtyProcess Start(PtyProcessStartOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SafeFileHandle? consoleInputRead = null;
        SafeFileHandle? applicationInputWrite = null;
        SafeFileHandle? applicationOutputRead = null;
        SafeFileHandle? consoleOutputWrite = null;
        SafePseudoConsoleHandle? pseudoConsole = null;
        SafeKernelObjectHandle? jobHandle = null;
        SafeKernelObjectHandle? processHandle = null;
        SafeKernelObjectHandle? threadHandle = null;
        IntPtr attributeList = IntPtr.Zero;
        var information = default(ProcessInformation);
        var processCreated = false;
        var ownershipTransferred = false;

        try
        {
            CreatePipe(out consoleInputRead, out applicationInputWrite);
            CreatePipe(out applicationOutputRead, out consoleOutputWrite);

            var size = new Coord((short)options.InitialSize.Columns, (short)options.InitialSize.Rows);
            var result = Native.CreatePseudoConsole(size, consoleInputRead, consoleOutputWrite, 0, out var pseudoConsoleHandle);
            if (result < 0)
                throw Marshal.GetExceptionForHR(result) ?? new IOException("CreatePseudoConsole failed.");

            pseudoConsole = new SafePseudoConsoleHandle(pseudoConsoleHandle);
            consoleInputRead.Dispose();
            consoleInputRead = null;
            consoleOutputWrite.Dispose();
            consoleOutputWrite = null;

            jobHandle = CreateJob();
            var attributeSize = IntPtr.Zero;
            _ = Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
            if (attributeSize == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not size the ConPTY process attributes.");
            attributeList = Marshal.AllocHGlobal(attributeSize);
            if (!Native.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeSize))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not initialize the ConPTY process attributes.");

            var pseudoConsoleValue = pseudoConsole.DangerousGetHandle();
            if (!Native.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    new IntPtr(PseudoConsoleAttribute),
                    pseudoConsoleValue,
                    new IntPtr(IntPtr.Size),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not attach the child process to ConPTY.");
            }

            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartfUseStdHandles
                },
                AttributeList = attributeList
            };
            var commandLine = new StringBuilder(BuildCommandLine(options.FileName, options.Arguments));
            if (!Native.CreateProcess(
                    options.FileName,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: false,
                    CreateSuspended | ExtendedStartupInfoPresent,
                    IntPtr.Zero,
                    options.WorkingDirectory,
                    ref startupInfo,
                    out information))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not start the ConPTY child process.");
            }

            processCreated = true;
            processHandle = new SafeKernelObjectHandle(information.ProcessHandle);
            information.ProcessHandle = IntPtr.Zero;
            threadHandle = new SafeKernelObjectHandle(information.ThreadHandle);
            information.ThreadHandle = IntPtr.Zero;

            if (!Native.AssignProcessToJobObject(jobHandle, processHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not assign the ConPTY child to its lifecycle job.");

            if (Native.ResumeThread(threadHandle) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not resume the ConPTY child process.");

            threadHandle.Dispose();
            threadHandle = null;
            cancellationToken.ThrowIfCancellationRequested();

            var process = new WindowsConPtyProcess(
                checked((int)information.ProcessId),
                options.InitialSize,
                processHandle,
                jobHandle,
                pseudoConsole,
                applicationInputWrite,
                applicationOutputRead);

            processHandle = null;
            jobHandle = null;
            pseudoConsole = null;
            applicationInputWrite = null;
            applicationOutputRead = null;
            ownershipTransferred = true;
            return process;
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new PlatformNotSupportedException(
                "This Windows version does not expose the ConPTY APIs required for interactive terminal sessions.", ex);
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                Native.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (!ownershipTransferred && processCreated && processHandle is not null && !processHandle.IsInvalid)
            {
                if (jobHandle is not null && !jobHandle.IsInvalid)
                    _ = Native.TerminateJobObject(jobHandle, 1);
                _ = Native.TerminateProcess(processHandle, 1);
                _ = Native.WaitForSingleObject(processHandle, 5_000);
            }

            if (information.ProcessHandle != IntPtr.Zero)
                _ = Native.CloseHandle(information.ProcessHandle);
            if (information.ThreadHandle != IntPtr.Zero)
                _ = Native.CloseHandle(information.ThreadHandle);
            threadHandle?.Dispose();
            processHandle?.Dispose();
            jobHandle?.Dispose();
            pseudoConsole?.Dispose();
            consoleInputRead?.Dispose();
            applicationInputWrite?.Dispose();
            applicationOutputRead?.Dispose();
            consoleOutputWrite?.Dispose();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfNotRunning();
        if (bytes.IsEmpty) return;

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfNotRunning();
            await _input.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
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
        if (size.Columns == 0 || size.Rows == 0 || size.Columns > short.MaxValue || size.Rows > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(size), "Windows ConPTY dimensions must be greater than zero and fit in a signed 16-bit coordinate.");

        var result = Native.ResizePseudoConsole(
            _pseudoConsole,
            new Coord((short)size.Columns, (short)size.Rows));
        if (result < 0)
            throw Marshal.GetExceptionForHR(result) ?? new IOException("ResizePseudoConsole failed.");

        lock (_sync) _size = size;
        return ValueTask.CompletedTask;
    }

    public async ValueTask SignalAsync(PtySignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfNotRunning();
        switch (signal)
        {
            case PtySignal.Interrupt:
                await WriteAsync(new byte[] { 0x03 }, cancellationToken).ConfigureAwait(false);
                break;
            case PtySignal.Terminate:
                TerminateJob(exitCode: 1);
                break;
            case PtySignal.Kill:
                TerminateJob(exitCode: 137);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(signal));
        }
    }

    public Task<PtyProcessExit> WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _exit.Task.WaitAsync(cancellationToken);

    private async Task ReadOutputAsync()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var count = await _output.ReadAsync(buffer).ConfigureAwait(false);
                if (count == 0) break;
                PublishOutput(buffer.AsSpan(0, count).ToArray());
            }
        }
        catch (IOException) when (Volatile.Read(ref _processExited) != 0 || Volatile.Read(ref _disposed) != 0) { }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
        catch (Exception ex)
        {
            SetState(PtyProcessState.Faulted, ex.Message);
            if (Volatile.Read(ref _disposed) == 0)
                _ = Native.TerminateJobObject(_jobHandle, 1);
        }
    }

    private async Task WaitForChildAsync()
    {
        PtyProcessExit exit;
        try
        {
            var waitResult = Native.WaitForSingleObject(_processHandle, Infinite);
            if (waitResult == WaitFailed)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not wait for the ConPTY child process.");
            if (waitResult != WaitObject0)
                throw new IOException($"Unexpected process wait result: {waitResult}.");

            Volatile.Write(ref _processExited, 1);
            _pseudoConsole.Dispose();
            if (!Native.GetExitCodeProcess(_processHandle, out var nativeExitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the ConPTY child exit code.");

            try { await _readTask.ConfigureAwait(false); }
            catch (Exception ex) { SetState(PtyProcessState.Faulted, ex.Message); }

            exit = new(unchecked((int)nativeExitCode), null, DateTimeOffset.UtcNow);
            if (State != PtyProcessState.Faulted)
                SetState(PtyProcessState.Exited);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _processExited, 1);
            _pseudoConsole.Dispose();
            exit = new(null, null, DateTimeOffset.UtcNow);
            SetState(PtyProcessState.Faulted, ex.Message);
        }

        _exit.TrySetResult(exit);
    }

    private void TerminateJob(uint exitCode)
    {
        if (Native.TerminateJobObject(_jobHandle, exitCode)) return;
        var error = Marshal.GetLastWin32Error();
        if (_exit.Task.IsCompleted || error == 5) // The process can exit between the state check and termination.
            return;
        throw new IOException("The ConPTY process tree could not be terminated.", new Win32Exception(error));
    }

    private void ThrowIfNotRunning()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (State != PtyProcessState.Running)
            throw new InvalidOperationException("The PTY process is not running.");
    }

    private void PublishOutput(byte[] bytes)
    {
        _outputBuffer.Publish(this, new PtyOutputChunk(bytes));
    }

    private void SetState(PtyProcessState state, string? detail = null)
    {
        lock (_sync)
        {
            if (_state == PtyProcessState.Disposed) return;
            if (_state == PtyProcessState.Faulted && state == PtyProcessState.Exited) return;
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
            try { _ = Native.TerminateJobObject(_jobHandle, 1); }
            catch (ObjectDisposedException) { }
            try { await _waitTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                _ = Native.TerminateProcess(_processHandle, 1);
                _pseudoConsole.Dispose();
                try { await _waitTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (TimeoutException) { }
            }
        }

        _pseudoConsole.Dispose();
        _input.Dispose();
        _output.Dispose();
        _jobHandle.Dispose();
        _processHandle.Dispose();
        try { await _readTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        catch (TimeoutException) { }
        catch (IOException) { }

        lock (_sync) _state = PtyProcessState.Disposed;
        _writeGate.Dispose();
        StateChanged?.Invoke(this, new(PtyProcessState.Disposed));
    }

    private static void CreatePipe(out SafeFileHandle readHandle, out SafeFileHandle writeHandle)
    {
        if (!Native.CreatePipe(out readHandle, out writeHandle, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the ConPTY transport pipes.");
    }

    private static SafeKernelObjectHandle CreateJob()
    {
        var job = new SafeKernelObjectHandle(Native.CreateJobObject(IntPtr.Zero, null));
        if (job.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create a ConPTY process job.");

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };
        if (!Native.SetInformationJobObject(
                job,
                JobObjectExtendedLimitInformationClass,
                ref limits,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error, "Windows could not configure ConPTY process-tree cleanup.");
        }

        return job;
    }

    private static string BuildCommandLine(string executable, IReadOnlyList<string> arguments) =>
        string.Join(" ", new[] { QuoteArgument(executable) }.Concat(arguments.Select(QuoteArgument)));

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(static character => char.IsWhiteSpace(character) || character == '"'))
            return argument;

        var result = new StringBuilder(argument.Length + 2);
        result.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', (backslashes * 2) + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes);
            result.Append(character);
            backslashes = 0;
        }

        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class SafeKernelObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelObjectHandle(IntPtr handle, bool ownsHandle = true) : base(ownsHandle) => SetHandle(handle);
        protected override bool ReleaseHandle() => Native.CloseHandle(handle);
    }

    private sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePseudoConsoleHandle(IntPtr handle) : base(ownsHandle: true) => SetHandle(handle);
        protected override bool ReleaseHandle()
        {
            Native.ClosePseudoConsole(handle);
            return true;
        }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr pipeAttributes, uint size);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr pseudoConsole);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern int ResizePseudoConsole(SafePseudoConsoleHandle pseudoConsole, Coord size);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern void ClosePseudoConsole(IntPtr pseudoConsole);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, uint flags, ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(IntPtr attributeList, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previousValue, IntPtr returnSize);

        [DllImport("kernel32.dll")]
        public static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(SafeKernelObjectHandle job, uint infoClass, ref JobObjectExtendedLimitInformation info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeKernelObjectHandle job, SafeKernelObjectHandle process);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(SafeKernelObjectHandle thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(SafeKernelObjectHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(SafeKernelObjectHandle process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(SafeKernelObjectHandle job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(SafeKernelObjectHandle process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
