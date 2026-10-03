namespace Haven.Application;

/// <summary>Detached existing canonical metadata. This observation does not authorize resource access.</summary>
public sealed record InstalledApplicationReadSnapshot(string RegistryRecordId, long RegistryRevision,
    IReadOnlyList<InstalledApplicationReference> Applications);

/// <summary>Optional original-actor read port. Missing inventory returns null without initialization,
/// discovery, reconciliation or writes. Consumers must authorize their exact source independently.</summary>
public interface IInstalledApplicationOriginalReadRegistry
{
    ValueTask<InstalledApplicationReadSnapshot?> ReadExistingForActorAsync(
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}
