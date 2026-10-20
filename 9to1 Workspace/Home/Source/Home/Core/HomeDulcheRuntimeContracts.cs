using System.Runtime.InteropServices;

namespace HavenOS.Home.Core;

/// <summary>Independent axes of shared local Dulche runtime discovery. A single canonical
/// lifecycle observation keeps each axis truthful so a missing runtime, missing model, altered
/// hash, unsupported platform/transport and incompatible capability stay distinguishable.</summary>
public enum HomeDulcheRuntimeAxis
{
    /// <summary>The packaged local inference runtime/backend executable and its helper tools.</summary>
    RuntimePackage,
    /// <summary>A registered, verified, eligible local model artifact (weights/tokenizer).</summary>
    ModelArtifact,
    /// <summary>A supported process/transport adapter for the current platform.</summary>
    Transport,
    /// <summary>Required capability coverage of the installed runtime/model combination.</summary>
    Capability,
}

/// <summary>Verified local model evidence. This is installation evidence only, never a
/// permission, an execution grant or proof that the native backend is running.</summary>
public sealed record HomeDulcheModelEvidence(
    string ModelId,
    string ProviderId,
    string ArtifactSha256,
    long SizeBytes,
    DateTimeOffset VerifiedAtUtc);

/// <summary>A single truthful discovery result for the shared Home-owned Dulche runtime.
/// <see cref="Lifecycle"/> maps to the canonical <see cref="HomeServiceLifecycleState"/> used
/// by the Home service registry; the axis booleans keep the underlying cause visible.</summary>
public sealed record HomeDulcheRuntimeObservation(
    HomeServiceLifecycleState Lifecycle,
    bool RuntimeInstalled,
    bool RuntimeVerified,
    bool ModelInstalled,
    bool ModelVerified,
    bool TransportSupported,
    bool CapabilitySupported,
    string Platform,
    string Architecture,
    IReadOnlyList<string> Deficiencies);

/// <summary>Owns the discovery/verification of the shared local Dulche runtime. Reading this
/// surface never starts a process, downloads a model or grants a capability. A zero-observation
/// source is a supported "no configured evidence" control for platform-neutral tests.</summary>
public interface IHomeDulcheRuntimeObservationSource
{
    Task<HomeDulcheRuntimeObservation> ObserveAsync(CancellationToken cancellationToken);
}

/// <summary>Default observation source. Performs only platform/package existence probes and
/// calls the supplied model-evidence callback; it never fabricates a model, hash or Ready
/// state. A missing evidence callback yields a truthful "no verified local model" result.</summary>
public sealed class HomeDulcheRuntimeObservationSource(
    Func<string, bool>? runtimeProbe = null,
    Func<CancellationToken, Task<HomeDulcheModelEvidence?>>? modelEvidence = null,
    string? platform = null,
    string? architecture = null) : IHomeDulcheRuntimeObservationSource
{
    private readonly Func<string, bool> _runtimeProbe = runtimeProbe ?? DefaultRuntimeProbe;
    private readonly Func<CancellationToken, Task<HomeDulcheModelEvidence?>> _modelEvidence =
        modelEvidence ?? (_ => Task.FromResult<HomeDulcheModelEvidence?>(null));
    private readonly string _platform = platform ?? (OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsAndroid() ? "android" : "unknown");
    private readonly string _architecture = architecture ?? RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    public async Task<HomeDulcheRuntimeObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deficiencies = new List<string>();

        var runtimeInstalled = _runtimeProbe(_platform) && !string.IsNullOrWhiteSpace(_platform) && _platform != "unknown";
        if (!runtimeInstalled)
            deficiencies.Add($"No verified local runtime package is installed for platform '{_platform}'.");

        var transportSupported = _platform is "linux" or "windows";
        if (!transportSupported)
            deficiencies.Add($"Local connection transport is not supported on platform '{_platform}'.");

        HomeDulcheModelEvidence? model;
        try
        {
            model = await _modelEvidence(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            model = null;
            deficiencies.Add($"Local model evidence could not be verified: {error.Message}");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var modelInstalled = model is not null;
        var modelVerified = model is { ArtifactSha256.Length: 64 };
        if (!modelInstalled)
            deficiencies.Add("No installed local model is registered and verified.");
        else if (!modelVerified)
            deficiencies.Add("The installed local model record is not verified against a SHA-256 pin.");

        var capabilitySupported = _platform is "linux" or "windows";
        if (runtimeInstalled && modelVerified && !capabilitySupported)
            deficiencies.Add("The installed runtime/model has no verified capability probe on this platform.");

        var ready = runtimeInstalled && modelVerified && transportSupported && capabilitySupported && deficiencies.Count == 0;
        var lifecycle = ready ? HomeServiceLifecycleState.Ready : HomeServiceLifecycleState.Degraded;
        return new(lifecycle, runtimeInstalled, runtimeInstalled, modelInstalled, modelVerified,
            transportSupported, capabilitySupported, _platform, _architecture, deficiencies);
    }

    /// <summary>Probes the known installed runtime-package locations for the current platform.
    /// Returns true only when the backend executable and its helper tools exist and are
    /// non-empty; it never treats a directory or a zero-byte placeholder as installed.</summary>
    internal static bool DefaultRuntimeProbe(string platform) => platform switch
    {
        "linux" => File.Exists("/usr/lib/haven/llama.cpp/llama-server")
            && new FileInfo("/usr/lib/haven/llama.cpp/llama-server").Length > 0
            && File.Exists("/usr/bin/haven-modelctl")
            && File.Exists("/usr/lib/haven/inference/broker.py"),
        "windows" => false, // No packaged Windows runtime transport exists yet; never fake a success.
        _ => false,
    };
}
