using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace HavenOS.Home.Core;

/// <summary>
/// Linux transport adapter for the shared broker. The packaged Dulche broker is a systemd user
/// service (<c>haven-inference-broker.service</c>) whose sandbox restricts address families to
/// AF_UNIX and whose worker listens on the runtime socket <c>$XDG_RUNTIME_DIR/haven/inference.sock</c>.
/// This adapter ATTACHES to that already-running packaged broker (borrowed ownership) and only
/// publishes a positive observation after it actually observes the live broker process identity from
/// systemd and a healthy AF_UNIX HTTP endpoint. It never launches a competing server and never
/// infers running state from the platform name. It releases nothing it did not acquire; it never
/// kills or shuts down the borrowed systemd broker.
/// </summary>
public sealed class HomeDulcheLinuxBrokerAdapter : IHomeDulcheBrokerAdapter
{
    public const string PlatformId = "linux";
    private const string BrokerUnitName = "haven-inference-broker.service";
    private const string SocketRelativePath = "haven/inference.sock";

    private readonly string? _socketPath;
    private readonly Func<CancellationToken, Task<long?>> _brokerPidSource;
    private readonly Func<string, long, CancellationToken, Task<bool>> _healthProbe;
    private readonly TimeSpan _commandTimeout;

    public HomeDulcheLinuxBrokerAdapter(
        string? socketPath = null,
        Func<CancellationToken, Task<long?>>? brokerPidSource = null,
        Func<string, long, CancellationToken, Task<bool>>? healthProbe = null,
        TimeSpan? commandTimeout = null)
    {
        _socketPath = socketPath;
        _brokerPidSource = brokerPidSource ?? DefaultBrokerPidSourceAsync;
        _healthProbe = healthProbe ?? DefaultHealthProbeAsync;
        _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(3);
    }

    public string Platform => PlatformId;
    public bool Supported => OperatingSystem.IsLinux() && SocketPath is not null;

    private string? SocketPath
    {
        get
        {
            if (_socketPath is not null) return _socketPath;
            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrWhiteSpace(runtimeDir)) return null;
            var candidate = Path.Combine(runtimeDir, SocketRelativePath);
            return Path.IsPathFullyQualified(candidate) ? candidate : null;
        }
    }

    public async ValueTask<HomeDulcheBrokerObservation?> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var socketPath = SocketPath;
        if (!Supported || socketPath is null) return null;
        var pid = await _brokerPidSource(cancellationToken).ConfigureAwait(false);
        if (pid is not { } livePid || livePid <= 0) return null;
        var healthy = await _healthProbe(socketPath, livePid, cancellationToken).ConfigureAwait(false);
        if (!healthy) return null;
        return new HomeDulcheBrokerObservation(livePid, "unix-socket", null, DateTimeOffset.UtcNow, socketPath);
    }

    /// <summary>Bounded health probe over the real AF_UNIX socket. It verifies the live broker
    /// process identity a second way: the accepted Unix socket peer credentials must match the
    /// systemd MainPID observed by <see cref="DefaultBrokerPidSourceAsync"/>. Connect/read/exit are
    /// all cancellable and time-bounded; a stuck socket never stalls optional service startup.</summary>
    private static async Task<bool> DefaultHealthProbeAsync(string socketPath, long expectedPid, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || expectedPid <= 0) return false;
        long? observedPeerPid = null;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = (context, token) => ConnectUnixAsync(socketPath, expectedPid, value => observedPeerPid = value, token)
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await client.GetAsync(new Uri("http://localhost/health"), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode && observedPeerPid == expectedPid;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async ValueTask<Stream> ConnectUnixAsync(string socketPath, long expectedPid, Action<long?> report, CancellationToken token)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token).ConfigureAwait(false);
            var peer = HomeNativePeerObservation.FromAcceptedUnixSocket(socket);
            report(peer?.ProcessId);
            if (peer is null || peer.ProcessId != expectedPid)
            {
                socket.Dispose();
                throw new IOException("The AF_UNIX peer process does not match the systemd MainPID.");
            }
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Reads the live broker MainPID from systemd with cancellable, time-bounded async
    /// stdout and exit observation. Unlike a synchronous ReadToEnd before WaitForExit, this honours
    /// cancellation/timeout, checks the real exit status and never silently ignores a failed/partial
    /// lookup child.</summary>
    private async Task<long?> DefaultBrokerPidSourceAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var psi = new ProcessStartInfo("systemctl")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("--user");
        psi.ArgumentList.Add("show");
        psi.ArgumentList.Add(BrokerUnitName);
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("MainPID");
        psi.ArgumentList.Add("--value");
        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start()) return null;
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            bool exited;
            try { exited = await RunToExitAsync(process, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return null; }

            var output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            if (!exited || process.ExitCode != 0) return null;
            return long.TryParse(output.Trim(), out var pid) && pid > 0 ? pid : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Waits for a child process to exit within the configured bound. If the bound elapses
    /// the child is killed and reaped and <c>false</c> is returned, so no zombie or orphaned
    /// redirected reader is left behind. Caller cancellation propagates.</summary>
    private async Task<bool> RunToExitAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_commandTimeout);
        bool exited;
        try
        {
            exited = await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKillAndReap(process);
            return false;
        }
        return exited;
    }

    private static void TryKillAndReap(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        try { process.WaitForExit(1000); } catch { }
    }
}

/// <summary>Explicit unsupported-platform broker adapter. An absent platform adapter stays unsupported;
/// it never fakes a running process or Ready state.</summary>
public sealed class HomeDulcheUnsupportedBrokerAdapter(string platform) : IHomeDulcheBrokerAdapter
{
    public string Platform { get; } = platform;
    public bool Supported => false;
    public ValueTask<HomeDulcheBrokerObservation?> ObserveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<HomeDulcheBrokerObservation?>(null);
}
