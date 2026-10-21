using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>
/// A validated, shell-free launch description for one local llama.cpp server.
/// </summary>
public sealed record LlamaCppLaunchPlan(
    string FileName,
    IReadOnlyList<string> Arguments,
    Uri? Endpoint,
    bool AlwaysLoaded,
    string? UnixSocketPath = null);

/// <summary>
/// Builds the smallest supported llama.cpp server invocation without touching global system state.
/// </summary>
public static class LlamaCppLaunchPlanner
{
    private const int MinimumPort = 1024;
    private const int MaximumPort = 65535;
    private const int MinimumContextSize = 512;
    private const int MaximumContextSize = 262144;
    private const int MaximumParallelRequests = 16;

    public static LlamaCppLaunchPlan Create(LlamaCppRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
            throw new InvalidOperationException("The llama.cpp runtime is disabled.");

        var executablePath = RequireAbsolutePath(options.ExecutablePath, nameof(options.ExecutablePath));
        var modelPath = RequireAbsolutePath(options.ModelPath, nameof(options.ModelPath));
        if (!string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The configured llama.cpp model must be a GGUF file.", nameof(options.ModelPath));
        if (options.Port is < MinimumPort or > MaximumPort)
            throw new ArgumentOutOfRangeException(nameof(options.Port), $"The llama.cpp port must be between {MinimumPort} and {MaximumPort}.");
        if (options.ContextSize is < MinimumContextSize or > MaximumContextSize)
            throw new ArgumentOutOfRangeException(nameof(options.ContextSize), $"The llama.cpp context size must be between {MinimumContextSize} and {MaximumContextSize} tokens.");
        if (options.ParallelRequests is < 1 or > MaximumParallelRequests)
            throw new ArgumentOutOfRangeException(nameof(options.ParallelRequests), $"The llama.cpp parallel-request count must be between 1 and {MaximumParallelRequests}.");

        // The verified-peer local provider reaches the owned child over an AF_UNIX socket. When a
        // socket path is configured (Linux only), llama-server is bound to that socket so the
        // provider's kernel-identified peer path applies to the exact process Haven started. Without
        // a socket path the server stays on the loopback host/port extension.
        var unixSocketPath = NormalizeUnixSocketPath(options.SocketPath);

        var arguments = new List<string>();
        if (options.UseUnifiedCli) arguments.Add("serve");
        if (unixSocketPath is not null)
        {
            if (string.IsNullOrWhiteSpace(options.ModelAlias))
                throw new ArgumentException("A model alias is required on the verified Unix socket path so the peer can be identified.", nameof(options.ModelAlias));
            arguments.AddRange([
                "--model", modelPath,
                "--alias", options.ModelAlias!,
                "--host", unixSocketPath,
                "--no-ui",
                "--no-slots",
                "--log-disable",
                "--offline",
                "--parallel", options.ParallelRequests.ToString(CultureInfo.InvariantCulture),
                "--ctx-size", options.ContextSize.ToString(CultureInfo.InvariantCulture),
                "--cache-ram", "0"
            ]);
        }
        else
        {
            arguments.AddRange([
                "--model", modelPath,
                "--host", "127.0.0.1",
                "--port", options.Port.ToString(CultureInfo.InvariantCulture),
                "--ctx-size", options.ContextSize.ToString(CultureInfo.InvariantCulture),
                "--parallel", options.ParallelRequests.ToString(CultureInfo.InvariantCulture),
                "--no-webui"
            ]);
        }

        // Template/reasoning controls are TYPED and optional, so explicit/default callers that did
        // not opt in are unchanged. --reasoning off is the supported switch that disables thinking
        // (sets enable_reasoning 0 + default_template_kwargs enable_thinking false); --jinja selects
        // the jinja template engine. Never emit shell/raw extraargs.
        if (options.UseJinja is { } useJinja)
            arguments.Add(useJinja ? "--jinja" : "--no-jinja");
        if (options.Reasoning != LlamaCppReasoningMode.Unset)
        {
            var reasoningValue = options.Reasoning switch
            {
                LlamaCppReasoningMode.Auto => "auto",
                LlamaCppReasoningMode.On => "on",
                LlamaCppReasoningMode.Off => "off",
                _ => throw new ArgumentOutOfRangeException(nameof(options.Reasoning), options.Reasoning,
                    $"Unsupported {nameof(LlamaCppReasoningMode)} value.")
            };
            arguments.Add("--reasoning");
            arguments.Add(reasoningValue);
        }

        return new LlamaCppLaunchPlan(
            executablePath,
            arguments,
            unixSocketPath is null ? new Uri($"http://127.0.0.1:{options.Port}/v1/", UriKind.Absolute) : null,
            options.AlwaysLoaded,
            unixSocketPath);
    }

    private static string? NormalizeUnixSocketPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The verified Unix socket path is only supported on Linux.");
        var path = value.Trim();
        if (path.Length < 2 || path[0] != '/' || path.Contains('\\'))
            throw new ArgumentException("The verified Unix socket path must be an absolute POSIX path.", nameof(value));
        if (Encoding.UTF8.GetByteCount(path) > UnixSocketPathMaxBytes)
            throw new ArgumentException($"The verified Unix socket path must be at most {UnixSocketPathMaxBytes} bytes.", nameof(value));
        return path;
    }

    private const int UnixSocketPathMaxBytes = 100;

    private static string RequireAbsolutePath(string? value, string parameterName)
    {
        var path = value?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An explicit path is required.", parameterName);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The path must be fully qualified so 9to1 cannot resolve an unintended executable or model.", parameterName);
        return path;
    }
}

/// <summary>
/// Reads process-local Haven configuration. Malformed numeric values remain invalid so startup fails closed.
/// </summary>
public static class LlamaCppRuntimeConfiguration
{
    public static LlamaCppRuntimeOptions FromEnvironment(Func<string, string?>? readVariable = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;
        return new LlamaCppRuntimeOptions(
            Enabled: ReadBoolean(readVariable("HAVEN_LLAMA_CPP_ENABLED")),
            ExecutablePath: Normalize(readVariable("HAVEN_LLAMA_CPP_EXECUTABLE")),
            ModelPath: Normalize(readVariable("HAVEN_LLAMA_CPP_MODEL")),
            UseUnifiedCli: ReadBoolean(readVariable("HAVEN_LLAMA_CPP_UNIFIED_CLI")),
            Port: ReadInteger(readVariable("HAVEN_LLAMA_CPP_PORT"), 18080),
            ContextSize: ReadInteger(readVariable("HAVEN_LLAMA_CPP_CONTEXT_SIZE"), 32768),
            ParallelRequests: ReadInteger(readVariable("HAVEN_LLAMA_CPP_PARALLEL"), 1),
            AlwaysLoaded: ReadBoolean(readVariable("HAVEN_LLAMA_CPP_ALWAYS_LOADED")));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ReadBoolean(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "on" or "enabled" => true,
        _ => false
    };

    private static int ReadInteger(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : int.MinValue;
    }
}

/// <summary>
/// Owns exactly one llama.cpp child process. Missing or invalid configuration returns a stopped status
/// instead of making the runtime boot-critical.
/// </summary>
public sealed class LlamaCppRuntime(LlamaCppRuntimeOptions options) : ILlamaCppRuntime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private bool _disposed;
    private Exception? _disposeFault;

    public LlamaCppRuntimeOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    public LlamaCppRuntimeStatus Status => GetStatus();

    public Task<LlamaCppRuntimeStatus> StartIfAlwaysLoadedAsync(CancellationToken cancellationToken)
    {
        if (!Options.Enabled)
            return Task.FromResult(GetStatus("The llama.cpp runtime is disabled."));
        if (!Options.AlwaysLoaded)
            return Task.FromResult(GetStatus("Always-loaded mode is disabled."));
        return EnsureStartedAsync(cancellationToken);
    }

    public async Task<LlamaCppRuntimeStatus> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (!Options.Enabled)
            return GetStatus("The llama.cpp runtime is disabled.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureNotDisposed();
            if (IsRunning(_process)) return GetStatus();
            DisposeExitedProcess();

            LlamaCppLaunchPlan plan;
            try
            {
                plan = LlamaCppLaunchPlanner.Create(Options);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return GetStatus(exception.Message);
            }

            if (!File.Exists(plan.FileName))
                return GetStatus("The configured llama.cpp executable does not exist.");
            if (!File.Exists(Options.ModelPath))
                return GetStatus("The configured GGUF model does not exist.");

            var startInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(plan.FileName) ?? Environment.CurrentDirectory
            };
            foreach (var argument in plan.Arguments) startInfo.ArgumentList.Add(argument);

            try
            {
                _process = Process.Start(startInfo);
                return _process is null
                    ? GetStatus("llama.cpp did not return a process handle.")
                    : GetStatus();
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _process = null;
                return GetStatus($"llama.cpp could not start: {exception.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Caller cancellation governs admission to the stop path: a cancelled caller never
        // touches the owned child. Once admitted, stopping is committed and the same child is
        // settled before custody is released.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureNotDisposed();

            var process = _process;
            if (process is null) return;

            // Attribute any bound Unix socket to this live child before signalling, so the same
            // child's leftover socket can be safely removed after it really exits.
            var socket = CaptureOwnedSocketIdentity(process);

            // Signal and join the exact child until it really exits. A genuine failure keeps
            // custody below, so a successor start cannot slip in while the child may still be
            // alive. Once admitted, the same-child join is not cancellable by this caller.
            await RetireChildAsync(process).ConfigureAwait(false);

            // Only after the same child has retired do we settle its leftover owned socket,
            // relinquish custody, and dispose the handle.
            CleanupOwnedSocket(socket);
            _process = null;
            process.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Signals the owned child and joins it until it has really exited. Returns only once the
    /// process is retired. The only benign case is the already-exited race between the state
    /// check and the signal request; every genuine handle/API failure propagates and leaves
    /// custody with the caller. The join is deliberately not cancellable by the caller: once a
    /// stop/dispose is admitted, the same child is settled to completion so its exit is always
    /// observed.
    /// </summary>
    private static async Task RetireChildAsync(Process process)
    {
        // A genuine handle/unassociated fault from HasExited propagates here; it is never
        // accepted as a settled child. An already-retired real process simply reports
        // HasExited == true and is just joined below.
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch (InvalidOperationException)
            {
                // The only benign case: the owned child exited between the state check and this
                // signal request, so it is already retired. Every other fault propagates.
            }
        }

        // Real settlement: wait until the OS process has actually exited. This is the proof, not
        // any fixed delay. Genuine handle/API failures surface here and are never swallowed. The
        // wait uses CancellationToken.None because caller cancellation is honoured only at
        // admission; cancelling after admission must not abandon a killed child before its exit
        // is observed.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Captures the identity of the Unix socket bound by this exact live child, when a socket path
    /// is configured on Linux and the child is still running. Attribution requires the live peer
    /// (via SO_PEERCRED) to be the exact child PID under our effective user, AND the filesystem
    /// no-follow statx identity of the socket (inode/device/uid/type) plus its parent directory.
    /// Returns null when any step cannot be positively established; the owned socket is then left
    /// untouched and stopping still proceeds normally.
    /// </summary>
    internal OwnedSocketCapture? CaptureOwnedSocketIdentity(Process process)
    {
        var path = Options.SocketPath;
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(path) || process is null) return null;

        int pid;
        try { pid = process.Id; }
        catch (ObjectDisposedException) { return null; }
        catch (InvalidOperationException) { return null; }

        // The kernel socket table lists every process; it is NOT ownership evidence. Require the
        // connected peer to be this exact child incarnation under our effective user.
        if (!TryReadProcStartTime(pid, out var childStart)) return null;
        if (!ConfirmPeerMatchesChild(path, pid, childStart)) return null;

        // Filesystem identity of the owned socket file itself (no-follow) and its parent directory.
        if (!TryReadNoFollowStat(path, out var file) || !file.IsSocket || file.Uid != geteuid()) return null;
        var parentPath = Path.GetDirectoryName(Path.GetFullPath(path));
        if (parentPath is null || !TryReadNoFollowStat(parentPath, out var parent) || !parent.IsDirectory) return null;

        return new OwnedSocketCapture(path, file, parentPath, parent);
    }

    /// <summary>
    /// Connects to the Unix endpoint and verifies via SO_PEERCRED that the serving process is
    /// exactly <paramref name="expectedPid"/> under our effective user and that it is the very
    /// same incarnation (start time from /proc) as the launched child, so a reused PID cannot be
    /// mistaken for our live child. This is the same kernel peer-identity idiom used by the
    /// binder provider's BindActualPeer machinery, combined with proc start-time identity.
    /// </summary>
    internal static bool ConfirmPeerMatchesChild(string path, int expectedPid, ulong expectedStartTime)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(path)) return false;
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { probe.Connect(new UnixDomainSocketEndPoint(path)); }
        catch (SocketException) { return false; }

        var length = (uint)Marshal.SizeOf<PeerCredentials>();
        if (getsockopt(probe.SafeHandle.DangerousGetHandle().ToInt32(), 1, 17, out var cred, ref length) != 0
            || length != Marshal.SizeOf<PeerCredentials>())
            return false;
        if (cred.Pid != expectedPid || cred.Uid != geteuid()) return false;
        if (!TryReadProcStartTime(cred.Pid, out var start) || start != expectedStartTime) return false;
        return true;
    }

    /// <summary>
    /// Reads the start-time identity of a process from <c>/proc/{pid}/stat</c> (field 22). Two
    /// probes with the same PID are the same process incarnation only when their start times
    /// match, which guards attribution against PID reuse after our child has exited.
    /// </summary>
    internal static bool TryReadProcStartTime(int pid, out ulong startTime)
    {
        startTime = 0;
        if (pid <= 0) return false;
        try
        {
            // The comm field (in parentheses) may contain spaces, so split after the last ')'.
            var text = File.ReadAllText($"/proc/{pid}/stat");
            var close = text.LastIndexOf(')');
            if (close < 0) return false;
            var rest = text.Substring(close + 1).Trim();
            var parts = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 20) return false;
            return ulong.TryParse(parts[19], NumberStyles.None, CultureInfo.InvariantCulture, out startTime);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Returns true when a live listener is currently bound at <paramref name="path"/> (a connect
    /// succeeds). Used to refuse deleting an endpoint that is still genuinely in use.
    /// </summary>
    internal static bool ProbeLiveConnection(string path)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(path)) return false;
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { probe.Connect(new UnixDomainSocketEndPoint(path)); return true; }
        catch (SocketException) { return false; }
    }

    /// <summary>
    /// Reads the no-follow statx identity of a filesystem entry (absolute path). On Linux this
    /// uses statx with AT_SYMLINK_NOFOLLOW, so a symlink is reported as itself (S_IFLNK) rather
    /// than followed. Returns false when the entry is absent or the kernel did not provide the
    /// fields we consume.
    /// </summary>
    internal static bool TryReadNoFollowStat(string path, out SocketFileIdentity identity)
    {
        identity = default;
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(path)) return false;
        return TryReadStatAt(AtFdcwd, path, AtNoFollow, out identity);
    }

    /// <summary>
    /// Core statx read against an open directory descriptor (or AT_FDCWD for an absolute path).
    /// <paramref name="flags"/> selects no-follow and/or empty-path semantics. The fixed statx
    /// UAPI layout (architecture-independent) supplies inode/device/uid/mode at known offsets.
    /// </summary>
    private static bool TryReadStatAt(int dirFd, string path, int flags, out SocketFileIdentity identity)
    {
        identity = default;
        var bytes = new byte[256];
        try
        {
            if (Statx(dirFd, path, flags, StatxBasicStats, bytes) != 0) return false;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }

        var mask = BitConverter.ToUInt32(bytes, 0);
        const uint required = 0x1 | 0x2 | 0x8 | 0x10 | 0x100 | 0x200; // type, mode, uid, gid, ino, size
        if ((mask & required) != required) return false;

        identity = new SocketFileIdentity(
            BitConverter.ToUInt64(bytes, 32),  // stx_ino
            BitConverter.ToUInt32(bytes, 136), // stx_dev_major
            BitConverter.ToUInt32(bytes, 140), // stx_dev_minor
            BitConverter.ToUInt32(bytes, 20),  // stx_uid
            BitConverter.ToUInt16(bytes, 28)); // stx_mode (type bits in the high nibble)
        return true;
    }

    /// <summary>
    /// Removes the leftover socket file left by our owned child after it really exited. The
    /// parent directory is pinned with a no-follow directory fd, the socket file is verified
    /// (inside that pinned parent, no-follow) to still be the exact owned socket, and the unlink
    /// is performed relative to that pinned parent — so a replaced/symlinked/foreign/pre-existing
    /// endpoint is never targeted and a live endpoint is never deleted. A genuine unlink failure
    /// propagates rather than being swallowed.
    /// </summary>
    internal static void CleanupOwnedSocket(OwnedSocketCapture? capture)
    {
        if (capture is not { } owned) return;

        int dirFd = open(owned.ParentPath, OpenDirectoryFlags(), 0);
        if (dirFd < 0) return; // cannot pin the private parent: leave the endpoint untouched

        try
        {
            // The pinned fd is this exact parent (no-follow). Verify its identity is unchanged.
            if (!TryReadStatAt(dirFd, "", AtEmptyPath, out var parent) || !parent.IsDirectory || !parent.SameFilesystemIdentity(owned.Parent)) return;

            // Inside the pinned parent, the entry must still be the exact owned socket file.
            if (!TryReadStatAt(dirFd, owned.FileName, AtNoFollow, out var file) || !file.IsSocket || !file.SameFilesystemIdentity(owned.File)) return;

            // Refuse deleting an endpoint that is still genuinely in use.
            if (ProbeLiveConnection(owned.Path)) return;

            if (unlinkat(dirFd, owned.FileName, 0) != 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Failed to remove the owned leftover socket");
        }
        finally
        {
            close(dirFd);
        }
    }

    /// <summary>Filesystem identity (inode, device, uid and mode incl. type bits) of one owned Unix
    /// socket capture. The socket file identity is taken from the filesystem entry itself, never
    /// from the kernel socket table, whose inode is not the filesystem inode.</summary>
    internal readonly struct SocketFileIdentity
    {
        public SocketFileIdentity(ulong inode, uint deviceMajor, uint deviceMinor, uint uid, ushort mode)
        {
            Inode = inode; DeviceMajor = deviceMajor; DeviceMinor = deviceMinor; Uid = uid; Mode = mode;
        }

        public ulong Inode { get; }
        public uint DeviceMajor { get; }
        public uint DeviceMinor { get; }
        public uint Uid { get; }
        public ushort Mode { get; }

        public bool IsSocket => (Mode & 0xf000) == 0xc000;
        public bool IsDirectory => (Mode & 0xf000) == 0x4000;

        public bool SameFilesystemIdentity(SocketFileIdentity other) =>
            Inode == other.Inode && DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor
            && Uid == other.Uid && Mode == other.Mode;
    }

    /// <summary>The attributed owned socket plus its parent directory path and identity, captured
    /// while the owned child was live and serving it. The parent directory is pinned and the
    /// socket is unlinked relative to it, so cleanup never acts on a replaced/foreign endpoint.</summary>
    internal readonly struct OwnedSocketCapture
    {
        public OwnedSocketCapture(string path, SocketFileIdentity file, string parentPath, SocketFileIdentity parent)
        {
            Path = path; File = file; ParentPath = parentPath; Parent = parent;
        }

        public string Path { get; }
        public SocketFileIdentity File { get; }
        public string ParentPath { get; }
        public SocketFileIdentity Parent { get; }
        public string FileName => System.IO.Path.GetFileName(Path);
    }

    public async ValueTask DisposeAsync()
    {
        // Idempotent, closed-admission disposal. The gate is intentionally kept alive (never
        // disposed) so queued concurrent Start/Stop/Dispose waiters are drained one by one under
        // the gate and observe the closed-admission flag instead of hanging on a disposed
        // semaphore. Admission is closed before the owned child is settled, and live-child
        // custody is preserved until the child really completes.
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                // A previous disposal attempt failed while the child was still live: surface the
                // exact same genuine failure instead of ever reporting a later silent success.
                if (_disposeFault is not null)
                    ExceptionDispatchInfo.Capture(_disposeFault).Throw();
                return;
            }

            _disposed = true;

            var process = _process;
            if (process is not null)
            {
                try
                {
                    var socket = CaptureOwnedSocketIdentity(process);
                    await RetireChildAsync(process).ConfigureAwait(false);
                    CleanupOwnedSocket(socket);
                    _process = null;
                    process.Dispose();
                }
                catch (Exception fault)
                {
                    // Retain the shared fault and keep live-child custody until real settlement.
                    _disposeFault = fault;
                    throw;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LlamaCppRuntime));
    }

    private LlamaCppRuntimeStatus GetStatus(string? detail = null)
    {
        var running = IsRunning(_process);
        int? processId = null;
        if (running)
        {
            try { processId = _process?.Id; }
            catch (ObjectDisposedException) { running = false; }
            catch (InvalidOperationException) { running = false; }
        }

        // When the verified Unix socket path is used the peer is reached through that socket; the
        // loopback URI is not representative and is intentionally left null so consumers never
        // mistake the socket endpoint for an arbitrary localhost listener.
        Uri? endpoint = string.IsNullOrWhiteSpace(Options.SocketPath)
            && Options.Port is >= 1024 and <= 65535
            ? new Uri($"http://127.0.0.1:{Options.Port}/v1/", UriKind.Absolute)
            : null;
        var finalDetail = !string.IsNullOrWhiteSpace(Options.SocketPath)
            ? string.IsNullOrWhiteSpace(detail)
                ? $"verified-unix-socket: {Options.SocketPath}"
                : $"{detail}; verified-unix-socket: {Options.SocketPath}"
            : detail;
        return new LlamaCppRuntimeStatus(Options.Enabled, running, Options.AlwaysLoaded, endpoint, processId, finalDetail);
    }

    private static bool IsRunning(Process? process)
    {
        if (process is null) return false;
        try { return !process.HasExited; }
        catch (ObjectDisposedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private void DisposeExitedProcess()
    {
        if (_process is null || IsRunning(_process)) return;
        _process.Dispose();
        _process = null;
    }

    private const int AtFdcwd = -100;
    private const int AtNoFollow = 0x100;   // AT_SYMLINK_NOFOLLOW (statx)
    private const int AtEmptyPath = 0x1000; // AT_EMPTY_PATH (statx)
    private const uint StatxBasicStats = 0x7ff;

    private const int O_RDONLY = 0;
    private const int O_CLOEXEC = 0x80000; // common across Linux UAPIs

    /// <summary>
    /// Linux <c>open</c> control flags for the owned-socket parent directory. The directory and
    /// no-follow flag values differ between the x86-64 and ARM64 UAPIs (x64: O_DIRECTORY=00200000
    /// =0x10000, O_NOFOLLOW=00400000=0x20000; ARM64: O_DIRECTORY=040000=0x4000, O_NOFOLLOW=
    /// 0100000=0x8000). Production <c>open</c> selects these per the actual process architecture, so
    /// the pinned controlled parent is opened with the correct contract on the real target.
    /// </summary>
    internal static (int DirectoryFlag, int NoFollowFlag) OpenDirectoryControls(Architecture architecture)
    {
        return architecture == Architecture.Arm64
            ? (0x4000, 0x8000)   // ARM64
            : (0x10000, 0x20000); // x86-64
    }

    internal static int OpenDirectoryFlags()
    {
        var (directory, noFollow) = OpenDirectoryControls(RuntimeInformation.ProcessArchitecture);
        return O_RDONLY | directory | noFollow | O_CLOEXEC;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PeerCredentials
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int getsockopt(int socket, int level, int option, out PeerCredentials credentials, ref uint length);

    [DllImport("libc")]
    private static extern uint geteuid();

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directoryFd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, [Out] byte[] result);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int close(int fd);

    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    private static extern int unlinkat(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}

/// <summary>
/// Explicit composition hook. Haven does not call this automatically, keeping llama.cpp non-boot-critical.
/// </summary>
public static class LlamaCppRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddHavenLlamaCppRuntime(
        this IServiceCollection services,
        LlamaCppRuntimeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<LlamaCppRuntimeOptions>(options ?? LlamaCppRuntimeConfiguration.FromEnvironment());
        services.AddSingleton<LlamaCppRuntime>();
        services.AddSingleton<ILlamaCppRuntime>(provider => provider.GetRequiredService<LlamaCppRuntime>());
        return services;
    }
}
