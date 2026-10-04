using System.Collections.Frozen;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>
/// Authenticates the actual current Windows installer receipt format and generation for
/// these SAME protected bytes, canonical package entry, installed tuple and original peer.
/// The implementation is supplied by the genuine installation owner. It must independently
/// verify its accepted receipt issuer/format/current protected installation state; neither
/// this observation nor successful descriptor/image signature checks issue a grant.
/// No default registration, parallel registry or authoring fallback is provided.
/// </summary>
public interface IHomeNativeWindowsProtectedInstallationReceiptAuthority
{
    ValueTask<bool> IsCurrentAsync(HomeNativeWindowsProtectedInstallationReceiptObservation original,
        CancellationToken cancellationToken);
}

/// <summary>Missing authoritative Windows installer receipt evidence denies admission.</summary>
public sealed class UnavailableHomeNativeWindowsProtectedInstallationReceiptAuthority
    : IHomeNativeWindowsProtectedInstallationReceiptAuthority
{
    public ValueTask<bool> IsCurrentAsync(HomeNativeWindowsProtectedInstallationReceiptObservation original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}

/// <summary>
/// Immutable observation constructed only during original protected-handle verification.
/// Bytes returned to the supplied owner are detached copies, never writable verifier buffers.
/// This is evidence to authenticate, not proof transferable through IPC or request arguments.
/// </summary>
public sealed class HomeNativeWindowsProtectedInstallationReceiptObservation
{
    private readonly byte[] _signedDescriptor;
    private readonly byte[] _signedReceipt;
    private readonly byte[] _canonicalEntry;
    public int ProcessId { get; }
    public string OperatingSystemPrincipalId { get; }
    public string ProcessStartIdentity { get; }
    public string ProtectedRoot { get; }
    public string DescriptorRelativePath { get; }
    public string ReceiptRelativePath { get; }
    public long CanonicalRegistryRevision { get; }
    public Guid SessionLeaseIdentity { get; }
    public AuthenticatedResourceActor OriginalActor { get; }
    public HomeNativeInstalledPeer InstalledObservation { get; }

    internal HomeNativeWindowsProtectedInstallationReceiptObservation(byte[] signedDescriptor,
        byte[] signedReceipt, byte[] canonicalEntry, int pid, string principal, string start,
        string protectedRoot, string descriptorRelative, string receiptRelative,
        long registryRevision, Guid leaseIdentity, AuthenticatedResourceActor actor,
        HomeNativeInstalledPeer installed)
    {
        _signedDescriptor = signedDescriptor.ToArray();
        _signedReceipt = signedReceipt.ToArray();
        _canonicalEntry = canonicalEntry.ToArray();
        ProcessId = pid; OperatingSystemPrincipalId = principal; ProcessStartIdentity = start;
        ProtectedRoot = protectedRoot; DescriptorRelativePath = descriptorRelative;
        ReceiptRelativePath = receiptRelative; CanonicalRegistryRevision = registryRevision;
        SessionLeaseIdentity = leaseIdentity; OriginalActor = actor with { };
        InstalledObservation = installed with {
            AllowedServiceIds = installed.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal),
            Roles = installed.Roles.ToFrozenSet(StringComparer.Ordinal)
        };
    }

    public ReadOnlyMemory<byte> CopySignedDescriptor() => _signedDescriptor.ToArray();
    public ReadOnlyMemory<byte> CopySignedReceipt() => _signedReceipt.ToArray();
    public ReadOnlyMemory<byte> CopyCanonicalPackageEntry() => _canonicalEntry.ToArray();
}
