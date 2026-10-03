using System.Runtime.Versioning;
using Haven.Application;

namespace NineToOne.Os.Shell.Authority;

// This first concrete authority records ONLY the Home child actually started by its
// administrator supervisor. It does not approve independently started owner applications.
[SupportedOSPlatform("linux")]
internal sealed class LinuxRootHomeControlledLaunchAuthority(LinuxRootSupervisedHome originalHome)
    : IHomeNativeControlledLaunchAuthority
{
    public async ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation, CancellationToken ct)
    {
        var original = originalHome.ObserveOriginalContext();
        if (observation.AppId != "os.shell" || (observation.RequiredRole != "" && observation.RequiredRole != "home.session-host") ||
            observation.ProcessId != original.OriginalHostPeer.ProcessId ||
            observation.OperatingSystemPrincipalId != original.OriginalHostPeer.OperatingSystemPrincipalId ||
            observation.ProcessStartIdentity != original.OriginalHostProcessStartIdentity ||
            observation.ExecutableIdentity != original.OriginalHostExecutableIdentity ||
            observation.ProfileId != original.ProfileId || observation.SessionLeaseIdentity != original.SessionLeaseIdentity)
            return false;
        return await originalHome.IsOriginalIssuedContextCurrentAsync(original, ct).ConfigureAwait(false);
    }
}
