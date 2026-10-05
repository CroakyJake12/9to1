using Haven.Core;

namespace Haven.Application;

/// <summary>Platform-observed entrypoint. Identity never depends on labels, icons, process IDs or UI coordinates.</summary>
public sealed record InstalledApplicationObservation(string OsApplicationId, string Entrypoint, string Label,
    string? Version, bool Enabled, AppOperability? Operability = null)
{
    /// <summary>Optional owner-declared stable launch locator within this OS application/profile.
    /// This is not installation, publisher, widget or execution authority.</summary>
    public string? StableLaunchIdentity { get; init; }
}
public sealed record InstalledApplicationProfileObservation(string PlatformProfileId, string Label,
    bool IsManaged, bool Accessible, IReadOnlyList<InstalledApplicationObservation> Applications);

/// <summary>Trusted platform adapter registered by the host, never supplied by app request arguments.</summary>
public interface IInstalledApplicationObservationProvider
{
    string ProviderId { get; }
    ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken cancellationToken);
}
public sealed record InstalledApplicationReference(Guid ApplicationId, string HomeProfileId, string ProviderId,
    string PlatformProfileId, string OsApplicationId, string Entrypoint, string Label, string? Version,
    bool Enabled, bool ProfileAccessible, bool IsManaged, long Revision, AppOperability Operability)
{
    public string? StableLaunchIdentity { get; init; }
}

/// <summary>Home's canonical installed entrypoint index. Package installation/update authority remains the package service.</summary>
public interface IInstalledApplicationRegistry
{
    ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken cancellationToken);
    ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid applicationId, long expectedRevision,
        CancellationToken cancellationToken);
}
