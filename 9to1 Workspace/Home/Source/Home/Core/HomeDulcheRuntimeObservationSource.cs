using System.Runtime.InteropServices;

namespace HavenOS.Home.Core;

/// <summary>Composes the independent, genuinely observed axes of the shared local Dulche runtime.
/// Each axis is obtained from a real source (package probe, broker adapter, model evidence). No axis
/// is inferred from the platform name, and Ready is reached only when every required positive
/// observation is present — including a live process, a healthy endpoint and a verified generation
/// capability probe. Verification alone is not model Ready; service health alone is not generation
/// capability.</summary>
public sealed class HomeDulcheRuntimeObservationSource : IHomeDulcheRuntimeObservationSource
{
    private readonly IHomeDulcheRuntimePackageSource _package;
    private readonly IHomeDulcheBrokerAdapter _broker;
    private readonly IHomeDulcheModelEvidenceSource _models;
    private readonly Func<CancellationToken, Task<bool>>? _capabilityProbe;
    private readonly string _platform;
    private readonly string _architecture;

    public HomeDulcheRuntimeObservationSource(
        IHomeDulcheRuntimePackageSource package,
        IHomeDulcheBrokerAdapter broker,
        IHomeDulcheModelEvidenceSource models,
        Func<CancellationToken, Task<bool>>? capabilityProbe = null,
        string? platform = null,
        string? architecture = null)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _capabilityProbe = capabilityProbe;
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

        var capabilitySupported = false;
        if (_capabilityProbe is not null)
        {
            if (processRunning && endpointAvailable && modelVerified)
            {
                try { capabilitySupported = await _capabilityProbe(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { capabilitySupported = false; }
            }
            if (!capabilitySupported)
                deficiencies.Add("The installed runtime/model has no verified generation-capability probe.");
        }
        else
        {
            deficiencies.Add("No verified generation-capability probe is configured; generation capability is blocked.");
        }

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
