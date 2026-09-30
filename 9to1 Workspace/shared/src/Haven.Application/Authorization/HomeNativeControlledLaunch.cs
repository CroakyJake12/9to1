namespace Haven.Application;

/// <summary>Trusted verifier's observed process and current host context; never populated from IPC request JSON.</summary>
public sealed record HomeNativeControlledLaunchObservation(
    int ProcessId,
    string OperatingSystemPrincipalId,
    string ProcessStartIdentity,
    string ExecutableIdentity,
    string ProfileId,
    string SessionLeaseIdentity,
    string AppId,
    string RequiredRole);

/// <summary>
/// Platform/Home-owned launch authority independent of signed installation receipts.
/// Implementations must match the real OS PID, principal, start identity and executable against their
/// recorded controlled bootstrap, current authenticated profile and actually held session lease.
/// Every field is evidence to recheck, never a caller assertion that grants authority.
/// A clean environment or immutable payload alone does not satisfy this port.
/// </summary>
public interface IHomeNativeControlledLaunchAuthority
{
    ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation,
        CancellationToken cancellationToken);
}

/// <summary>No implicit authoring/debug or self-attestation fallback.</summary>
public sealed class UnavailableHomeNativeControlledLaunchAuthority : IHomeNativeControlledLaunchAuthority
{
    public ValueTask<bool> IsCurrentAsync(HomeNativeControlledLaunchObservation observation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
