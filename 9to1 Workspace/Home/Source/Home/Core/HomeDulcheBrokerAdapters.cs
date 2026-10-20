namespace HavenOS.Home.Core;

/// <summary>Linux transport adapter for the shared broker. The packaged Dulche broker is a systemd
/// user service exposing an HTTP control endpoint on loopback (default Dulche port 9477). This
/// adapter ATTACHES to an already-running broker (borrowed ownership) and only publishes a positive
/// observation after it actually observes a live broker process identity and a healthy endpoint. It
/// never launches a competing server and never infers running state from the platform name.</summary>
public sealed class HomeDulcheLinuxBrokerAdapter : IHomeDulcheBrokerAdapter
{
    public const string PlatformId = "linux";
    public const int DefaultDulchePort = 9477;
    private const string BrokerUnitName = "haven-inference-broker.service";

    private readonly int _port;
    private readonly Func<Uri, CancellationToken, Task<bool>> _healthProbe;
    private readonly Func<CancellationToken, Task<long?>> _brokerPidSource;

    public HomeDulcheLinuxBrokerAdapter(
        int? port = null,
        Func<Uri, CancellationToken, Task<bool>>? healthProbe = null,
        Func<CancellationToken, Task<long?>>? brokerPidSource = null)
    {
        _port = port ?? DefaultDulchePort;
        if (_port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "The broker port must be between 1024 and 65535.");
        _healthProbe = healthProbe ?? DefaultHealthProbeAsync;
        _brokerPidSource = brokerPidSource ?? DefaultBrokerPidSourceAsync;
    }

    public string Platform => PlatformId;
    public bool Supported => true;

    public async ValueTask<HomeDulcheBrokerObservation?> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pid = await _brokerPidSource(cancellationToken).ConfigureAwait(false);
        if (pid is not { } livePid || livePid <= 0) return null;
        var endpoint = new Uri($"http://127.0.0.1:{_port}/v1/", UriKind.Absolute);
        var healthy = await _healthProbe(endpoint, cancellationToken).ConfigureAwait(false);
        if (!healthy) return null;
        return new HomeDulcheBrokerObservation(livePid, "http-loopback", endpoint, DateTimeOffset.UtcNow);
    }

    private static async Task<bool> DefaultHealthProbeAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        try
        {
            var baseUri = new Uri(endpoint, "../health");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync(baseUri, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
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

    private static Task<long?> DefaultBrokerPidSourceAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) return Task.FromResult<long?>(null);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("systemctl")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add("--user");
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add(BrokerUnitName);
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add("MainPID");
            psi.ArgumentList.Add("--value");
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return Task.FromResult<long?>(null);
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return Task.FromResult(long.TryParse(output, out var pid) && pid > 0 ? pid : (long?)null);
        }
        catch
        {
            return Task.FromResult<long?>(null);
        }
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
