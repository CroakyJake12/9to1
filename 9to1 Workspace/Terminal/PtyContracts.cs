using System.Text;

namespace HavenOS.Apps.Terminal;

/// <summary>Character dimensions for a pseudo-terminal.</summary>
public readonly record struct PtySize
{
    public PtySize(ushort columns, ushort rows)
    {
        if (columns == 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows == 0) throw new ArgumentOutOfRangeException(nameof(rows));
        Columns = columns;
        Rows = rows;
    }

    public ushort Columns { get; }
    public ushort Rows { get; }
}

public enum PtyProcessState
{
    Running,
    Exited,
    Faulted,
    Disposed
}

public enum PtySignal
{
    Interrupt,
    Terminate,
    Kill
}

public sealed record PtyProcessStartOptions(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    PtySize InitialSize);

public sealed record PtyOutputChunk(ReadOnlyMemory<byte> Bytes);

public sealed record PtyProcessExit(int? ExitCode, int? Signal, DateTimeOffset ExitedAt);

public sealed record PtyProcessStateChange(PtyProcessState State, string? Detail = null);

/// <summary>
/// Platform-neutral ownership contract for one child process attached to a real pseudo-terminal.
/// Output is intentionally raw bytes so a terminal emulator can parse escape sequences without
/// lossy line or UTF-16 conversion.
/// </summary>
public interface IPtyProcess : IAsyncDisposable
{
    int ProcessId { get; }
    PtySize Size { get; }
    PtyProcessState State { get; }
    event EventHandler<PtyOutputChunk>? OutputReceived;
    event EventHandler<PtyProcessStateChange>? StateChanged;
    ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
    ValueTask ResizeAsync(PtySize size, CancellationToken cancellationToken = default);
    ValueTask SignalAsync(PtySignal signal, CancellationToken cancellationToken = default);
    Task<PtyProcessExit> WaitForExitAsync(CancellationToken cancellationToken = default);
}

public interface IPtyProcessFactory
{
    bool IsSupported { get; }
    string AdapterName { get; }
    string? UnavailableReason { get; }
    Task<IPtyProcess> StartAsync(PtyProcessStartOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Application-facing owner used by Terminal and Studio hosts.</summary>
public sealed class InteractiveTerminalSession : IAsyncDisposable
{
    private readonly IPtyProcessFactory _factory;
    private IPtyProcess? _process;
    private bool _disposed;

    public InteractiveTerminalSession() : this(new PlatformPtyProcessFactory())
    {
    }

    public InteractiveTerminalSession(IPtyProcessFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public bool IsSupported => _factory.IsSupported;
    public string AdapterName => _factory.AdapterName;
    public string? UnavailableReason => _factory.UnavailableReason;
    public IPtyProcess? Process => _process;
    public bool IsRunning => _process?.State == PtyProcessState.Running;

    public event EventHandler<PtyOutputChunk>? OutputReceived;
    public event EventHandler<PtyProcessStateChange>? StateChanged;

    public async Task<IPtyProcess> StartAsync(
        string workingDirectory,
        PtySize size,
        string? shell = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_factory.IsSupported)
            throw new PlatformNotSupportedException(_factory.UnavailableReason ?? "No PTY adapter is available.");

        var root = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var executable = ResolveShell(shell);
        var replacement = await _factory.StartAsync(
            new PtyProcessStartOptions(executable, [], root, size), cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            await replacement.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        replacement.OutputReceived += OnOutputReceived;
        replacement.StateChanged += OnStateChanged;
        var previous = Interlocked.Exchange(ref _process, replacement);
        if (previous is not null)
        {
            previous.OutputReceived -= OnOutputReceived;
            previous.StateChanged -= OnStateChanged;
            await previous.DisposeAsync().ConfigureAwait(false);
        }

        return replacement;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) =>
        GetRunningProcess().WriteAsync(bytes, cancellationToken);

    public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    public ValueTask ResizeAsync(PtySize size, CancellationToken cancellationToken = default) =>
        GetRunningProcess().ResizeAsync(size, cancellationToken);

    public ValueTask InterruptAsync(CancellationToken cancellationToken = default) =>
        GetRunningProcess().SignalAsync(PtySignal.Interrupt, cancellationToken);

    private IPtyProcess GetRunningProcess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _process is { State: PtyProcessState.Running } process
            ? process
            : throw new InvalidOperationException("No interactive terminal process is running.");
    }

    private static string ResolveShell(string? shell) => ResolveShell(
        shell,
        Environment.GetEnvironmentVariable("SHELL"),
        Environment.GetEnvironmentVariable("ComSpec"),
        Environment.SystemDirectory,
        OperatingSystem.IsWindows());

    internal static string ResolveShell(
        string? shell,
        string? shellPreference,
        string? commandProcessor,
        string systemDirectory,
        bool isWindows)
    {
        var candidate = string.IsNullOrWhiteSpace(shell)
            ? shellPreference
            : shell.Trim();
        if (!string.IsNullOrWhiteSpace(candidate) && Path.IsPathRooted(candidate) && File.Exists(candidate))
            return Path.GetFullPath(candidate);
        if (isWindows)
        {
            if (!string.IsNullOrWhiteSpace(commandProcessor) && File.Exists(commandProcessor))
                return Path.GetFullPath(commandProcessor);

            var systemCommandProcessor = Path.Combine(systemDirectory, "cmd.exe");
            if (File.Exists(systemCommandProcessor))
                return systemCommandProcessor;

            throw new PlatformNotSupportedException("No Windows command shell was found for the ConPTY session.");
        }
        if (File.Exists("/bin/sh")) return "/bin/sh";
        throw new PlatformNotSupportedException("No executable Unix shell was supplied for the PTY session.");
    }

    private void OnOutputReceived(object? sender, PtyOutputChunk output) => OutputReceived?.Invoke(this, output);
    private void OnStateChanged(object? sender, PtyProcessStateChange state) => StateChanged?.Invoke(this, state);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null) return;
        process.OutputReceived -= OnOutputReceived;
        process.StateChanged -= OnStateChanged;
        await process.DisposeAsync().ConfigureAwait(false);
    }
}
