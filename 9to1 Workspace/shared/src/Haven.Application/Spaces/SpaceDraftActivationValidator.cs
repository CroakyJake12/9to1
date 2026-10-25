namespace Haven.Application;

/// <summary>One existing canonical Space revision in an activation policy's ancestry.</summary>
public sealed record SpaceActivationAncestryRevision(Guid SpaceId, long SpaceRevision);

/// <summary>Owner-validated dependency snapshot for publishing a saved Space draft. The
/// registry rechecks this exact Space/draft/active CAS and ancestry in its commit transaction.</summary>
public sealed record SpaceDraftActivationValidation(
    Guid SpaceId,
    long SpaceRevision,
    long DraftConfigurationRevision,
    long ActiveConfigurationRevision,
    IReadOnlyList<SpaceActivationAncestryRevision> Ancestry);

/// <summary>Validates dependencies owned outside the Space registry. Implementations must
/// inspect the supplied candidate only; validation does not execute models or grant access.</summary>
public interface ISpaceDraftActivationValidator
{
    Task<SpaceDraftActivationValidation> ValidateAsync(
        SpaceDefinition current,
        SpaceConfigurationDocument draft,
        Action<Action> acquireOriginal,
        Action<Task> retainOriginal,
        CancellationToken cancellationToken = default);
}
