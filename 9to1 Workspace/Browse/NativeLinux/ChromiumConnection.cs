using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HavenOS.Apps.Browse;

/// <summary>
/// One owned Chromium child process for a canonical engine-isolated profile
/// directory. It launches headless Chromium with a loopback-only DevTools port,
/// discovers the child-owned WebSocket endpoint from stderr, validates that it is
/// a loopback/random-port/devtools browser endpoint, performs the version
/// handshake, then exposes the multiplexed DevTools client
/// (<see cref="ChromiumClient"/>) to every attached page session. stdout/stderr
/// are drained; the child exit and every source task are joined on close. This
/// process is terminated only after the last attached tab retires.
/// </summary>
internal sealed class ChromiumConnection : IChromiumEndpoint
{
    private static readonly Regex EndpointPattern = new(@"DevTools listening on (ws://\S+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

    private readonly string _profileDirectory;
    private readonly string _binaryPath;
    private readonly ChromiumClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Process? _process;
    private Task? _stderrDrain, _stdoutDrain, _closeTask;

    private ChromiumConnection(string profileDirectory, string binaryPath)
    {
        _profileDirectory = profileDirectory;
        _binaryPath = binaryPath;
        _client = new ChromiumClient(new ClientWebSocketTransport());
        _client.Recv += OnClientRecv;
    }

    public event EventHandler<CdpEvent>? Recv;

    private void OnClientRecv(object? sender, CdpEvent e) => Recv?.Invoke(this, e);

    public static async Task<IChromiumEndpoint> StartAsync(string profileDirectory, string binaryPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileDirectory);
        ArgumentNullException.ThrowIfNull(binaryPath);
        var connection = new ChromiumConnection(profileDirectory, binaryPath);
        await connection.StartCoreAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_profileDirectory);
        var start = new ProcessStartInfo
        {
            FileName = _binaryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in ChromiumArguments.Build(_profileDirectory)) start.ArgumentList.Add(argument);

        var endpoint = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Chromium refused to start.");
        _stderrDrain = DrainAsync(_process.StandardError, line => TryCaptureEndpoint(line, endpoint), _lifetime.Token);
        _stdoutDrain = DrainAsync(_process.StandardOutput, static _ => { }, _lifetime.Token);

        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        startupCancellation.CancelAfter(StartupTimeout);
        Uri endpointUri;
        try
        {
            var raw = await endpoint.Task.WaitAsync(startupCancellation.Token).ConfigureAwait(false);
            endpointUri = ChromiumEndpointPolicy.RequireValid(raw);
        }
        catch
        {
            KillProcess();
            throw new InvalidOperationException("Chromium did not announce a valid loopback DevTools endpoint.");
        }

        try
        {
            await _client.StartAsync(endpointUri, startupCancellation.Token).ConfigureAwait(false);
            var version = await _client.SendAsync(null, "Browser.getVersion", null, startupCancellation.Token).ConfigureAwait(false);
            if (!version.TryGetProperty("product", out var product) ||
                product.ValueKind != JsonValueKind.String ||
                !product.GetString()!.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The DevTools endpoint did not report a Chromium build.");
        }
        catch
        {
            KillProcess();
            try { await _client.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
            throw;
        }
    }

    private static void TryCaptureEndpoint(string line, TaskCompletionSource<string> endpoint)
    {
        var match = EndpointPattern.Match(line);
        if (!match.Success) return;
        endpoint.TrySetResult(match.Groups[1].Value);
    }

    private static async Task DrainAsync(StreamReader reader, Action<string> onLine, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                try { onLine(line); } catch { /* bounded: never let a log line kill the drain */ }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* child closed its pipe; drain ends */ }
    }

    public Task<JsonElement> SendAsync(string? sessionId, string method, object? parameters, CancellationToken cancellationToken) =>
        _client.SendAsync(sessionId, method, parameters, cancellationToken);

    public Task<string> CreateTargetAsync(string url, CancellationToken cancellationToken) =>
        _client.CreateTargetAsync(url, cancellationToken);

    public Task<string> AttachAsync(string targetId, CancellationToken cancellationToken) =>
        _client.AttachAsync(targetId, cancellationToken);

    private void KillProcess()
    {
        Process? process;
        lock (_gate) process = _process;
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception) { }
        }
    }

    private async Task WaitForProcessExitAsync()
    {
        Process? process;
        lock (_gate) process = _process;
        if (process is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (Exception) { /* process already gone or timed out; bounded */ }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closeTask is not null) return new(_closeTask);
            _closeTask = CloseCoreAsync();
        }
        return new(_closeTask);
    }

    private async Task CloseCoreAsync()
    {
        _lifetime.Cancel();
        var failures = new List<Exception>();
        async Task Join(Task? task) { if (task is null) return; try { await task.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); } }

        await Join(_client.DisposeAsync().AsTask());
        KillProcess();
        await Join(_stderrDrain);
        await Join(_stdoutDrain);
        await Join(WaitForProcessExitAsync());
        _lifetime.Dispose();
        if (failures.Count != 0) throw new AggregateException("Chromium retained its process source failures.", failures);
    }
}
