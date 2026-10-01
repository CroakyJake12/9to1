namespace Haven.Application.Compatibility;

public enum CompatibilityBackendKind { Wine, WindowsEnvironment, Android }
public enum CompatibilityRoutingStatus { ReviewRequired, Denied, Unavailable, PreferredBackendUnavailable }

/// <summary>Current owning runtime evidence. Availability is never inferred from a source checkout.</summary>
public sealed record CompatibilityBackendObservation(string BackendId, CompatibilityBackendKind Kind,
    string EnvironmentId, string EnvironmentRevision, int Priority, bool Eligible, string? ExclusionReason);

/// <summary>Owner rereads policy, runtime capability and installed package identity for this exact canonical app.</summary>
public sealed record CompatibilityApplicationObservation(Guid ApplicationId, long ApplicationRevision,
    string Revision, bool PolicyAllowed, string? PolicyReason, bool PackageTrustVerified,
    string? PreferredBackendId, IReadOnlyList<CompatibilityBackendObservation> Backends);

public interface ICompatibilityApplicationOwner
{
    ValueTask<CompatibilityApplicationObservation?> ObserveAsync(AuthenticatedResourceActor actor,
        InstalledApplicationReference application, CancellationToken cancellationToken);
}

/// <summary>Read-only routing proposal, not a permission, human approval or executable launch handle.</summary>
public sealed record CompatibilityRoutingPreview(CompatibilityRoutingStatus Status, string Reason,
    Guid ApplicationId, long ApplicationRevision, string ObservationRevision,
    CompatibilityBackendObservation? ProposedBackend, IReadOnlyList<CompatibilityBackendObservation> Backends);
