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
    /// stdout and exit observation. The owned lookup child is ALWAYS terminated and reaped and its
    /// redirected readers joined on every exit path (normal, cancellation, timeout, failure); cleanup
    /// failures are surfaced truthfully instead of swallowed. It never touches the borrowed broker.</summary>
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
        }
        catch (Exception error)
        {
            throw new AggregateException("The systemd broker identity lookup could not be started.", error);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_commandTimeout);

        var failures = new List<Exception>();
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        bool exitedNormally = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            exitedNormally = process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TerminateOwnChild(process, failures);
            await DrainReadersAsync(outputTask, errorTask, failures).ConfigureAwait(false);
            if (failures.Count != 0)
                throw new AggregateException("The broker identity lookup was cancelled and did not settle cleanly.", failures);
            throw; // clean caller-cancellation propagation after confirming cleanup
        }
        catch (OperationCanceledException)
        {
            TerminateOwnChild(process, failures);
            await DrainReadersAsync(outputTask, errorTask, failures).ConfigureAwait(false);
            if (failures.Count != 0)
                throw new AggregateException("The broker identity lookup timed out and did not settle cleanly.", failures);
            throw new TimeoutException("The broker identity lookup exceeded its configured time bound.");
        }
        catch (Exception error)
        {
            failures.Add(error);
            TerminateOwnChild(process, failures);
            await DrainReadersAsync(outputTask, errorTask, failures).ConfigureAwait(false);
            throw new AggregateException("The broker identity lookup process failed.", failures);
        }

        // Normal exit: the child already settled; drain both readers fully and surface any read fault.
        TerminateOwnChild(process, failures);
        string? output = null;
        try { output = await outputTask.ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        try { _ = await errorTask.ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0)
            throw new AggregateException("The broker identity lookup did not settle cleanly.", failures);
        if (!exitedNormally) return null;
        return long.TryParse(output?.Trim(), out var pid) && pid > 0 ? pid : null;
    }

    /// <summary>Terminates ONLY the owned lookup child if it is still alive and reaps it, recording
    /// any kill/reap failure. It never kills or shuts down the borrowed systemd broker.</summary>
    private static void TerminateOwnChild(Process process, List<Exception> failures)
    {
        if (process.HasExited) return;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception error)
        {
            failures.Add(new InvalidOperationException("Failed to terminate the owned systemd broker identity lookup child.", error));
            return;
        }
        try
        {
            // Reap the actual child within a finite bound; if it still refuses to settle, cleanup is
            // not confirmed and must be reported truthfully.
            if (!process.WaitForExit(1000) && !process.HasExited)
                failures.Add(new InvalidOperationException("The owned systemd broker identity lookup child did not settle after termination."));
        }
        catch (Exception error)
        {
            failures.Add(new InvalidOperationException("Failed to reap the owned systemd broker identity lookup child.", error));
        }
    }

    private static async Task DrainReadersAsync(Task<string> outputTask, Task<string> errorTask, List<Exception> failures)
    {
        try { _ = await outputTask.ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        try { _ = await errorTask.ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
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
