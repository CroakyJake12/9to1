using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;

namespace HavenOS.Home.NativeUI;

/// <summary>Projects the trusted original connection observation into the CUI startup gate.
/// An absent attachment is unknown/unready; it is not proof of an absent installation.
/// The owning platform supplies trusted install/update/repair actions separately.</summary>
public sealed class HomeNativeStartupCuiReadiness(IHomeNativeStartupSession? originalStartup) : ICuiSceneReadiness
{
    public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (originalStartup is null)
            return new(CuiSceneAvailabilityState.Unavailable, "HomeStartupAttachmentUnavailable",
                "Connect to the required installed Home service before starting the app.");
        var observed = await originalStartup.CheckAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return observed.CanStartNormally
            ? new(CuiSceneAvailabilityState.Ready, observed.Code, observed.Message)
            : new(CuiSceneAvailabilityState.Unavailable, observed.Code, observed.Message);
    }
}
