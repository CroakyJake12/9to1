using System.Runtime.InteropServices;

namespace HavenOS.Home.Core;

/// <summary>Composes the independent, genuinely observed axes of the shared local Dulche runtime.
/// Each axis is obtained from a real source (package probe, broker adapter, model evidence). No axis
/// is inferred from the platform name, and Ready is reached only when every required positive
/// observation is present — including a live process, a healthy endpoint and an already-admitted
/// generation capability observation. Verification alone is not model Ready; service health alone is
/// not generation capability; and generation capability is never inferred by initiating a probe
/// during startup — it is recorded only from an explicit same-generation model-use admission.</summary>
public sealed class HomeDulcheRuntimeObservationSource : IHomeDulcheRuntimeObservationSource
{
    private readonly IHomeDulcheRuntimePackageSource _package;
    private readonly IHomeDulcheBrokerAdapter _broker;
    private readonly IHomeDulcheModelEvidenceSource _models;
    private readonly HomeDulcheCapabilityObservation? _alreadyAdmittedCapability;
    private readonly string _platform;
    private readonly string _architecture;

    public HomeDulcheRuntimeObservationSource(
        IHomeDulcheRuntimePackageSource package,
        IHomeDulcheBrokerAdapter broker,
        IHomeDulcheModelEvidenceSource models,
        HomeDulcheCapabilityObservation? alreadyAdmittedCapability = null,
        string? platform = null,
        string? architecture = null)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _alreadyAdmittedCapability = alreadyAdmittedCapability;
        _platform = platform ?? (OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsAndroid() ? "android" : "unknown");
        _architecture = architecture ?? RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    }

    public async Task<HomeDulcheRuntimeObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deficiencies = new List<string>();

        var package = await _package.ProbeAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // Preserve EVERY actual axis's diagnostics. The package probe is an independent truth axis:
        // dropping its deficiencies would let a Degraded observation (e.g. package.Verified=false)
        // publish with an empty list and fail open in the lifecycle owner.
        foreach (var packageDeficiency in package.Deficiencies)
            deficiencies.Add(packageDeficiency);

        var transportSupported = _broker.Supported;
        if (!transportSupported)
            deficiencies.Add($"Local connection transport is not supported on platform '{_platform}'.");

        HomeDulcheBrokerObservation? broker = null;
        if (transportSupported)
        {
            broker = await _broker.ObserveAsync(cancellationToken).ConfigureAwait(false);
            if (broker is null)
                deficiencies.Add("The shared local inference broker is not running or its endpoint is unhealthy.");
        }

        HomeDulcheModelEvidence? model = null;
        try
        {
            model = await _models.FindEligibleAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            deficiencies.Add($"Local model evidence could not be verified: {error.Message}");
        }
        cancellationToken.ThrowIfCancellationRequested();

        var modelInstalled = model is not null;
        var modelVerified = model?.IsVerifiedArtifact == true;
        if (!modelInstalled)
            deficiencies.Add("No installed local model is registered and verified.");
        else if (!modelVerified)
            deficiencies.Add("The installed local model record is not verified against a real SHA-256 pin.");

        var processRunning = broker is not null;
        var endpointAvailable = broker is not null;

        // Generation capability is NEVER probed during startup. It is recorded only from an
        // already-admitted, same-generation model-use observation that verified capability for the
        // SAME observed broker endpoint/process and SAME verified model. Absent such an admission,
        // the capability axis stays explicitly blocked (never inferred).
        var capabilitySupported = false;
        if (processRunning && endpointAvailable && modelVerified && model is not null &&
            _alreadyAdmittedCapability is { GenerationCapable: true } admitted &&
            admitted.IsSameGeneration(model, broker))
        {
            capabilitySupported = true;
        }
        if (!capabilitySupported)
            deficiencies.Add("No verified generation-capability admission is recorded for the observed runtime/model; generation capability is blocked.");

        var ready = package.Installed && package.Verified && modelVerified && transportSupported
            && processRunning && endpointAvailable && capabilitySupported;
        var lifecycle = ready ? HomeServiceLifecycleState.Ready : HomeServiceLifecycleState.Degraded;
        return new(
            lifecycle,
            package.Installed,
            package.Verified,
            modelInstalled,
            modelVerified,
            processRunning,
            endpointAvailable,
            transportSupported,
            capabilitySupported,
            _platform,
            _architecture,
            model?.ModelId,
            deficiencies);
    }
}
